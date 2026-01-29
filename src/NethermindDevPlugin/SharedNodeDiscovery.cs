using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Nethermind.Config;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.Network.Rlpx;

namespace NethermindDevPlugin;

public class SharedNodeDiscovery(
    string sharedNodesDir,
    string? subnetCidr,
    IRlpxHost rlpxHost,
    IIPResolver ipResolver,
    IStaticNodesManager staticNodesManager,
    IProcessExitSource processExitSource,
    ILogManager logManager)
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleTtl = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = logManager.GetClassLogger<SharedNodeDiscovery>();
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _addedEnodes = new();

    private string? _ownFilePath;
    private string? _ownEnode;

    public record NodeFileContent(string Enode, long LastSeen);

    private IPAddress? ResolveIpForSubnet(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var prefixLength))
        {
            _logger.Error($"Invalid CIDR format: {cidr}");
            return null;
        }

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var addr in iface.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                if (IsInSubnet(addr.Address, network, prefixLength))
                {
                    _logger.Info($"Found IP {addr.Address} matching subnet {cidr} on interface {iface.Name}");
                    return addr.Address;
                }
            }
        }

        return null;
    }

    private static bool IsInSubnet(IPAddress address, IPAddress network, int prefixLength)
    {
        var addrBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        var mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        var addrUint = (uint)(addrBytes[0] << 24 | addrBytes[1] << 16 | addrBytes[2] << 8 | addrBytes[3]);
        var networkUint = (uint)(networkBytes[0] << 24 | networkBytes[1] << 16 | networkBytes[2] << 8 | networkBytes[3]);

        return (addrUint & mask) == (networkUint & mask);
    }

    public void Start()
    {
        IPAddress ip;
        if (!string.IsNullOrEmpty(subnetCidr))
        {
            var resolved = ResolveIpForSubnet(subnetCidr);
            if (resolved is null)
            {
                _logger.Error($"No interface found matching subnet {subnetCidr}. Shared node discovery disabled.");
                return;
            }
            ip = resolved;
        }
        else
        {
            ip = ipResolver.LocalIp;
        }

        var publicKey = rlpxHost.LocalNodeId.ToString(false);
        var port = rlpxHost.LocalPort;
        _ownEnode = $"enode://{publicKey}@{ip}:{port}";
        _ownFilePath = Path.Combine(sharedNodesDir, $"{publicKey}.json");

        _logger.Info($"Shared node discovery enabled at {sharedNodesDir}");
        _logger.Info($"Own enode: {_ownEnode}");

        if (!Directory.Exists(sharedNodesDir))
        {
            _logger.Warn($"Shared nodes directory does not exist: {sharedNodesDir}");
            return;
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, processExitSource.Token);

        _ = HeartbeatLoopAsync(linkedCts.Token);
        _ = ScanLoopAsync(linkedCts.Token);
    }

    public void Stop()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);

        // Write immediately on start
        WriteOwnFile();

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                WriteOwnFile();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            DeleteOwnFile();
        }
    }

    private void WriteOwnFile()
    {
        if (_ownFilePath is null || _ownEnode is null)
            return;

        try
        {
            var content = new NodeFileContent(_ownEnode, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var json = JsonSerializer.Serialize(content);
            File.WriteAllText(_ownFilePath, json);
            _logger.Debug("Heartbeat updated");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to write own node file: {ex.Message}");
        }
    }

    private void DeleteOwnFile()
    {
        if (_ownFilePath is null)
            return;

        try
        {
            if (File.Exists(_ownFilePath))
            {
                File.Delete(_ownFilePath);
                _logger.Info("Deleted own node file on shutdown");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to delete own node file: {ex.Message}");
        }
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);

        // Scan immediately on start
        await ScanDirectoryAsync();

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await ScanDirectoryAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ScanDirectoryAsync()
    {
        if (!Directory.Exists(sharedNodesDir))
        {
            _logger.Warn($"Shared nodes directory not accessible: {sharedNodesDir}");
            return;
        }

        var ownPublicKey = rlpxHost.LocalNodeId.ToString(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var seenEnodes = new HashSet<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(sharedNodesDir, "*.json"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Skip own file
                if (fileName.Equals(ownPublicKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var content = JsonSerializer.Deserialize<NodeFileContent>(json);

                    if (content is null || string.IsNullOrEmpty(content.Enode))
                        continue;

                    // Check TTL
                    if (now - content.LastSeen > (long)StaleTtl.TotalSeconds)
                    {
                        _logger.Debug($"Skipping stale node file: {fileName}");
                        continue;
                    }

                    seenEnodes.Add(content.Enode);

                    if (!_addedEnodes.Contains(content.Enode))
                    {
                        if (await staticNodesManager.AddAsync(content.Enode, updateFile: false))
                        {
                            _addedEnodes.Add(content.Enode);
                            _logger.Info($"Discovered new node: {content.Enode}");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Debug($"Failed to parse node file {file}: {ex.Message}");
                }
                catch (IOException ex)
                {
                    _logger.Debug($"Failed to read node file {file}: {ex.Message}");
                }
            }

            // Remove nodes that are no longer present or stale
            foreach (var enode in _addedEnodes.ToList())
            {
                if (!seenEnodes.Contains(enode))
                {
                    if (await staticNodesManager.RemoveAsync(enode, updateFile: false))
                    {
                        _addedEnodes.Remove(enode);
                        _logger.Info($"Removed stale node: {enode}");
                    }
                }
            }

            _logger.Debug($"Scanned {seenEnodes.Count} node files");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Error scanning shared nodes directory: {ex.Message}");
        }
    }
}
