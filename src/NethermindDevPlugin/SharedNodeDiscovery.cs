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
}
