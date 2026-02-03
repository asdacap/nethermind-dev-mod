# P2P Server Cluster Scripts

Scripts to start N Nethermind + Lighthouse node clusters for sync testing.

## Setup

1. Create the cluster directory and config:

```bash
mkdir -p ~/nethermindstaticnodes
cp cluster.conf.example ~/nethermindstaticnodes/cluster.conf
```

2. Edit `~/nethermindstaticnodes/cluster.conf` with your paths:
   - `NETHERMIND_REPO`: Path to Nethermind source repository
   - `LIGHTHOUSE_BIN`: Path to Lighthouse binary
   - `DOTNET_FLAKE`: Nix flake for dotnet SDK
   - `SHARED_NODES_DIR`: Directory for shared node registry (can be on network storage)
   - `DATA_DIR`: Local data directory for node data, binaries, logs (must be local/fast storage)
   - `BACKUP_PATH`: Path to data backup for copying

## Usage

### Start a cluster

```bash
./start-cluster.sh <num_nodes> [backup_path_override]

# Examples:
./start-cluster.sh 3                              # Start 3 nodes
./start-cluster.sh 2 /mnt/workspace/other_backup  # Start 2 nodes with different backup
```

### Stop the cluster

```bash
./stop-cluster.sh
```

## Port Scheme

Each node gets unique ports (node i, 0-indexed):

| Service           | Port Formula   | Node 0 | Node 1 | Node 2 |
|-------------------|----------------|--------|--------|--------|
| P2P               | 30303 + i      | 30303  | 30304  | 30305  |
| JSON-RPC          | 8545 + i       | 8545   | 8546   | 8547   |
| Engine API        | 8551 + i       | 8551   | 8552   | 8553   |
| Metrics           | 9090 + i       | 9090   | 9091   | 9092   |
| Lighthouse P2P    | 9000 + i*2     | 9000   | 9002   | 9004   |
| Lighthouse HTTP   | 5052 + i       | 5052   | 5053   | 5054   |

## Directory Structure

After starting, the directories look like:

**Shared node registry** (SHARED_NODES_DIR - can be on network storage):
```
~/nethermindstaticnodes/shared/  # or network path
├── <pubkey1>.json
└── <pubkey2>.json
```

**Local data** (DATA_DIR - must be local/fast storage):
```
~/nethermindstaticnodes/
├── cluster.conf               # User configuration
├── bin/                       # Copied Nethermind binaries (frozen snapshot)
│   └── nethermind/
├── jwtsecret                  # Shared JWT secret
├── node-0/
│   ├── nethermind/            # Copied from backup
│   ├── lighthouse/            # Lighthouse data
│   ├── nethermind.log
│   └── lighthouse.log
├── node-1/
│   └── ...
└── pids/                      # PID files for process management
```

## Verification

```bash
# Check logs
tail -f ~/nethermindstaticnodes/node-0/nethermind.log

# Verify shared discovery files appear
ls ~/nethermindstaticnodes/shared/

# Check JSON-RPC (node 0)
curl -s localhost:8545 -X POST -H "Content-Type: application/json" \
    -d '{"jsonrpc":"2.0","method":"eth_syncing","params":[],"id":1}'

# Check JSON-RPC (node 1)
curl -s localhost:8546 -X POST -H "Content-Type: application/json" \
    -d '{"jsonrpc":"2.0","method":"eth_syncing","params":[],"id":1}'
```
