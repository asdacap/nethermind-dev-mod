# Shared Filesystem Node Discovery

## Overview

Enable Nethermind nodes running on different VMs to automatically discover and connect to each other via a shared filesystem directory (e.g., NFS, EFS, GlusterFS).

## Configuration

Two environment variables control the feature:

```bash
SHARED_NODES_DIR=/mnt/shared/nethermind-nodes   # Required - path to shared directory
SHARED_NODES_SUBNET=192.168.1.0/24              # Optional - subnet for IP selection
```

- If `SHARED_NODES_DIR` is not set or empty, the feature is disabled
- If `SHARED_NODES_SUBNET` is set, the node finds a local interface IP matching that subnet
- If `SHARED_NODES_SUBNET` is not set, uses `IIPResolver.LocalIp`

## File Format

Each node writes a JSON file named after its public key (hex-encoded, no `0x` prefix):

```
/mnt/shared/nethermind-nodes/
├── d837e193233c08d6950913bf69105096457fbe...4e4b.json
├── a1b2c3d4e5f6...json
└── ...
```

File contents:

```json
{
  "enode": "enode://d837e19...@192.168.1.10:30303",
  "lastSeen": 1706520000
}
```

- `enode`: Full enode URL (public key, IP address, port)
- `lastSeen`: Unix timestamp (seconds) of last heartbeat

## Timing

- **Heartbeat interval:** 10 seconds (update own file's `lastSeen`)
- **Scan interval:** 10 seconds (check directory for other nodes)
- **TTL:** 30 seconds (ignore files with `lastSeen` older than this)

## Lifecycle

### Own File (Heartbeat Writer)

1. On startup: create/overwrite own JSON file with enode and current timestamp
2. Every 10 seconds: update `lastSeen` timestamp
3. On graceful shutdown: attempt to delete own file (best-effort)

### Other Nodes (Directory Scanner)

1. On startup: scan directory and add all valid nodes to static nodes list
2. Every 10 seconds:
   - Scan all JSON files in directory
   - Skip own file (matching public key)
   - For files with `lastSeen` within 30 seconds:
     - If not in static nodes → `IStaticNodesManager.AddAsync(enode)`
   - For previously-added nodes now stale or deleted:
     - Remove via `IStaticNodesManager.RemoveAsync(enode)`

## IP Resolution

When `SHARED_NODES_SUBNET` is set (e.g., `192.168.1.0/24`):

1. Enumerate all network interfaces
2. Find first IPv4 address within the CIDR range
3. Use that IP in the enode
4. If no match found, log error and disable feature

Example:
```
eth0: 10.0.0.5       (public)
eth1: 192.168.1.10   (internal)

SHARED_NODES_SUBNET=192.168.1.0/24 → uses 192.168.1.10
```

## Implementation

### New Files

1. **`SharedNodeDiscoveryStep.cs`** - `IStep` that starts the background service
2. **`SharedNodeDiscovery.cs`** - Core logic (heartbeat, scanning, node management)

### Integration

In `DevPluginModule.Load()`:

```csharp
string? sharedNodesDir = Environment.GetEnvironmentVariable("SHARED_NODES_DIR");
if (!string.IsNullOrEmpty(sharedNodesDir))
{
    builder.AddStep(typeof(SharedNodeDiscoveryStep));
}
```

### Dependencies

- `IStaticNodesManager` - add/remove discovered nodes
- `IRlpxHost` - get `LocalNodeId` (public key) and `LocalPort`
- `IIPResolver` - fallback IP resolution
- `ILogManager` - logging
- `IProcessExitSource` - graceful shutdown

### Error Handling

- File I/O errors: log and continue
- JSON parse errors: log and skip file
- Missing directory: log warning, retry next tick
- No matching subnet interface: log error, disable feature

## Logging

- INFO: "Shared node discovery enabled at {path}"
- INFO: "Discovered new node: {enode}"
- INFO: "Removed stale node: {enode}"
- WARN: "Shared nodes directory not accessible: {path}"
- DEBUG: "Heartbeat updated"
- DEBUG: "Scanned {n} node files"

## Limitations

- Does not replace normal peer discovery (bootnodes, discv4/v5)
- Does not handle NAT traversal - nodes must be directly reachable
- Does not encrypt or authenticate files - relies on filesystem security
- `FileSystemWatcher` not used due to unreliability on network filesystems

## Deployment Example

```
VM1 (node A)                    VM2 (node B)
     │                               │
     └───────► NFS Mount ◄───────────┘
               /mnt/shared/nethermind-nodes/
               ├── <pubkeyA>.json
               └── <pubkeyB>.json
```

Both nodes set:
```bash
export SHARED_NODES_DIR=/mnt/shared/nethermind-nodes
export SHARED_NODES_SUBNET=192.168.1.0/24
```
