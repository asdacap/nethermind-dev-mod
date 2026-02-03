# Shared Filesystem Node Discovery Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable Nethermind nodes to auto-discover peers via JSON files in a shared filesystem directory.

**Architecture:** A background service writes the node's enode to a JSON file (heartbeat every 10s), scans for other node files, and adds/removes them from the static nodes list based on TTL (30s). Uses `IStaticNodesManager` for node management.

**Tech Stack:** C# .NET 10, Autofac DI, System.Text.Json, System.Net.NetworkInformation

---

### Task 1: Create SharedNodeDiscovery Core Class

**Files:**
- Create: `SharedNodeDiscovery.cs`

**Step 1: Create the SharedNodeDiscovery class with constructor and fields**

```csharp
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
}
```

**Step 2: Commit**

```bash
git add SharedNodeDiscovery.cs
git commit -m "feat: add SharedNodeDiscovery class skeleton"
```

---

### Task 2: Add IP Resolution Logic

**Files:**
- Modify: `SharedNodeDiscovery.cs`

**Step 1: Add ResolveIpForSubnet method**

Add this method to `SharedNodeDiscovery`:

```csharp
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
```

**Step 2: Commit**

```bash
git add SharedNodeDiscovery.cs
git commit -m "feat: add subnet-based IP resolution"
```

---

### Task 3: Add Start Method with Enode Building

**Files:**
- Modify: `SharedNodeDiscovery.cs`

**Step 1: Add Start method**

Add this method to `SharedNodeDiscovery`:

```csharp
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
```

**Step 2: Commit**

```bash
git add SharedNodeDiscovery.cs
git commit -m "feat: add Start method with enode building"
```

---

### Task 4: Add Heartbeat Loop

**Files:**
- Modify: `SharedNodeDiscovery.cs`

**Step 1: Add HeartbeatLoopAsync method**

Add this method to `SharedNodeDiscovery`:

```csharp
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
```

**Step 2: Commit**

```bash
git add SharedNodeDiscovery.cs
git commit -m "feat: add heartbeat loop for writing own node file"
```

---

### Task 5: Add Scan Loop

**Files:**
- Modify: `SharedNodeDiscovery.cs`

**Step 1: Add ScanLoopAsync method**

Add this method to `SharedNodeDiscovery`:

```csharp
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
```

**Step 2: Commit**

```bash
git add SharedNodeDiscovery.cs
git commit -m "feat: add scan loop for discovering other nodes"
```

---

### Task 6: Add Stop Method

**Files:**
- Modify: `SharedNodeDiscovery.cs`

**Step 1: Add Stop method**

Add this method to `SharedNodeDiscovery`:

```csharp
    public void Stop()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
```

**Step 2: Commit**

```bash
git add SharedNodeDiscovery.cs
git commit -m "feat: add Stop method for graceful shutdown"
```

---

### Task 7: Create SharedNodeDiscoveryStep

**Files:**
- Create: `SharedNodeDiscoveryStep.cs`

**Step 1: Create the step class**

```csharp
using Nethermind.Api.Steps;
using Nethermind.Config;
using Nethermind.Init.Steps;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.Network.Rlpx;

namespace NethermindDevPlugin;

[RunnerStepDependencies(typeof(InitializeNetwork))]
public class SharedNodeDiscoveryStep(
    IRlpxHost rlpxHost,
    IIPResolver ipResolver,
    IStaticNodesManager staticNodesManager,
    IProcessExitSource processExitSource,
    ILogManager logManager) : IStep
{
    public Task Execute(CancellationToken cancellationToken)
    {
        var sharedNodesDir = Environment.GetEnvironmentVariable("SHARED_NODES_DIR");
        if (string.IsNullOrEmpty(sharedNodesDir))
            return Task.CompletedTask;

        var subnetCidr = Environment.GetEnvironmentVariable("SHARED_NODES_SUBNET");

        var discovery = new SharedNodeDiscovery(
            sharedNodesDir,
            subnetCidr,
            rlpxHost,
            ipResolver,
            staticNodesManager,
            processExitSource,
            logManager);

        discovery.Start();

        return Task.CompletedTask;
    }
}
```

**Step 2: Commit**

```bash
git add SharedNodeDiscoveryStep.cs
git commit -m "feat: add SharedNodeDiscoveryStep IStep implementation"
```

---

### Task 8: Register Step in DevPluginModule

**Files:**
- Modify: `DevPlugin.cs`

**Step 1: Add using statement for Nethermind.Network**

At the top of `DevPlugin.cs`, add:

```csharp
using Nethermind.Network;
```

**Step 2: Register the step in DevPluginModule.Load()**

In `DevPluginModule.Load()`, after the existing `builder.AddStep(typeof(GitBisectExitOnInvalidBlock));` line, add:

```csharp
        // Shared filesystem node discovery
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHARED_NODES_DIR")))
        {
            builder.AddStep(typeof(SharedNodeDiscoveryStep));
        }
```

**Step 3: Commit**

```bash
git add DevPlugin.cs
git commit -m "feat: register SharedNodeDiscoveryStep in plugin module"
```

---

### Task 9: Add Project Reference for Nethermind.Network

**Files:**
- Modify: `NethermindDevPlugin.csproj`

**Step 1: Add ProjectReference for Nethermind.Network**

Add this line in the `<ItemGroup>` with other `<ProjectReference>` elements:

```xml
        <ProjectReference Include="../../../nethermind/src/Nethermind/Nethermind.Network/Nethermind.Network.csproj" />
```

**Step 2: Commit**

```bash
git add NethermindDevPlugin.csproj
git commit -m "feat: add Nethermind.Network project reference"
```

---

### Task 10: Build and Verify

**Step 1: Build the project**

Run:
```bash
cd /home/amirul/repo/nethermind-dev-mod/src/NethermindDevPlugin && dotnet build
```

Expected: Build succeeds with no errors.

**Step 2: Commit final state**

```bash
git add -A
git commit -m "feat: complete shared filesystem node discovery feature" --allow-empty
```

---

## Summary

Files created:
- `SharedNodeDiscovery.cs` - Core discovery logic (heartbeat writer + directory scanner)
- `SharedNodeDiscoveryStep.cs` - IStep entry point

Files modified:
- `DevPlugin.cs` - Register step when `SHARED_NODES_DIR` is set
- `NethermindDevPlugin.csproj` - Add Nethermind.Network reference

Configuration:
- `SHARED_NODES_DIR` - Path to shared directory (required to enable)
- `SHARED_NODES_SUBNET` - CIDR for IP selection (optional)
