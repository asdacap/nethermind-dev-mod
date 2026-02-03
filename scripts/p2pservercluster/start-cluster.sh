#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 [--sync-backup] [--cleanup] <num_nodes>"
    echo ""
    echo "Options:"
    echo "  --sync-backup    Create/update backup by syncing a temporary node first"
    echo "  --cleanup        Reset node data from backup (default: preserve existing data)"
    echo ""
    echo "If BACKUP_PATH doesn't exist, sync will run automatically."
    exit 1
}

# Parse arguments
SYNC_BACKUP=false
CLEANUP=false
NUM_NODES=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --sync-backup)
            SYNC_BACKUP=true
            shift
            ;;
        --cleanup)
            CLEANUP=true
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            if [[ -z "$NUM_NODES" ]]; then
                NUM_NODES="$1"
            else
                echo "Error: Unexpected argument: $1"
                usage
            fi
            shift
            ;;
    esac
done

if [[ -z "$NUM_NODES" ]]; then
    usage
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CONFIG_FILE="${CONFIG_FILE:-$HOME/nethermindstaticnodes/cluster.conf}"

# Load config
if [[ ! -f "$CONFIG_FILE" ]]; then
    echo "Error: Config file not found: $CONFIG_FILE"
    echo "Copy cluster.conf.example to $CONFIG_FILE and edit it"
    exit 1
fi
source "$CONFIG_FILE"

# Use paths from config
# SHARED_NODES_DIR - from config (shared registry, can be on network storage)
# DATA_DIR - from config (local data, must be local/fast storage)

PIDS_DIR="$DATA_DIR/pids"
BIN_DIR="$DATA_DIR/bin"

# === Setup directories ===
mkdir -p "$SHARED_NODES_DIR" "$PIDS_DIR" "$BIN_DIR"

# === Build Nethermind and copy binary ===
echo "Building Nethermind from $NETHERMIND_REPO..."
nix develop "$DOTNET_FLAKE" --command bash -c "
    cd '$NETHERMIND_REPO'
    dotnet build src/Nethermind/Nethermind.Runner -c Release
"

echo "Build done"

rm -rf "$BIN_DIR/nethermind"
cp -av "$NETHERMIND_REPO/src/Nethermind/artifacts/bin/Nethermind.Runner/release" "$BIN_DIR/nethermind"
NETHERMIND_BIN="$BIN_DIR/nethermind/nethermind"
chmod +x "$NETHERMIND_BIN"
echo "Binary copied to $BIN_DIR/nethermind"

# === Build and deploy NethermindClusterPlugin ===
CLUSTER_PLUGIN_DIR="$SCRIPT_DIR/../../NethermindClusterPlugin"
echo "Building NethermindClusterPlugin from $CLUSTER_PLUGIN_DIR..."
nix develop "$DOTNET_FLAKE" --command bash -c "
    cd '$CLUSTER_PLUGIN_DIR'
    ./build.sh
"
cp -v "$CLUSTER_PLUGIN_DIR/out/NethermindClusterPlugin."* "$BIN_DIR/nethermind/plugins/"
echo "Cluster plugin deployed"

# === Sync backup if needed ===
if [[ ! -d "$BACKUP_PATH" ]] || [[ "$SYNC_BACKUP" == "true" ]]; then
    echo ""
    echo "=== Syncing backup at $BACKUP_PATH ==="

    SYNC_DIR="$DATA_DIR/sync-temp"
    SYNC_EL="$SYNC_DIR/nethermind"
    SYNC_CL="$SYNC_DIR/lighthouse"
    mkdir -p "$SYNC_EL" "$SYNC_CL"

    # Use fixed ports for sync node
    SYNC_P2P_PORT=30399
    SYNC_RPC_PORT=8599
    SYNC_ENGINE_PORT=8560
    SYNC_METRICS_PORT=9099
    SYNC_LH_P2P_PORT=9098
    SYNC_LH_HTTP_PORT=5099

    echo "Starting temporary sync node..."
    echo "  Nethermind: P2P=$SYNC_P2P_PORT Engine=$SYNC_ENGINE_PORT"
    echo "  Lighthouse: P2P=$SYNC_LH_P2P_PORT HTTP=$SYNC_LH_HTTP_PORT"

    # Kill any existing processes using sync ports
    for port in $SYNC_P2P_PORT $SYNC_RPC_PORT $SYNC_ENGINE_PORT $SYNC_METRICS_PORT $SYNC_LH_P2P_PORT $SYNC_LH_HTTP_PORT; do
        pid=$(lsof -ti :$port 2>/dev/null || true)
        if [[ -n "$pid" ]]; then
            echo "  Killing process $pid using port $port..."
            kill $pid 2>/dev/null || true
        fi
    done
    sleep 1

    # Generate JWT secret for sync node
    openssl rand -hex 32 > "$SYNC_DIR/jwtsecret"

    # Start Nethermind for sync
    "$NETHERMIND_BIN" \
        --datadir "$SYNC_EL" \
        --config mainnet \
        --JsonRpc.Enabled true \
        --JsonRpc.Port $SYNC_RPC_PORT \
        --JsonRpc.EnginePort $SYNC_ENGINE_PORT \
        --JsonRpc.EngineHost 127.0.0.1 \
        --JsonRpc.JwtSecretFile "$SYNC_DIR/jwtsecret" \
        --Network.P2PPort $SYNC_P2P_PORT \
        --Network.DiscoveryPort $SYNC_P2P_PORT \
        --Sync.NonValidatorNode true \
        --Metrics.Enabled true \
        --Metrics.ExposePort $SYNC_METRICS_PORT \
        --Sync.DownloadBodiesInFastSync false --Sync.DownloadReceiptsInFastSync false \
        --Sync.SnapSync true \
        --Sync.ExitOnSynced true \
        --Sync.ExitOnSyncedWaitTimeSec 10 &
    SYNC_NM_PID=$!
    echo "  Nethermind PID: $SYNC_NM_PID"

    # Wait for Engine API
    echo "Waiting for Engine API..."
    for i in {1..30}; do
        if curl -s "http://127.0.0.1:$SYNC_ENGINE_PORT" >/dev/null 2>&1; then
            break
        fi
        sleep 2
    done

    # Start Lighthouse for sync
    "$LIGHTHOUSE_BIN" bn \
        --network mainnet \
        --datadir "$SYNC_CL" \
        --execution-jwt "$SYNC_DIR/jwtsecret" \
        --execution-endpoint "http://127.0.0.1:$SYNC_ENGINE_PORT" \
        --checkpoint-sync-url "https://mainnet.checkpoint.sigp.io" \
        --port $SYNC_LH_P2P_PORT \
        --http --http-port $SYNC_LH_HTTP_PORT \
        > "$SYNC_DIR/lighthouse.log" 2>&1 &
    SYNC_LH_PID=$!
    echo "  Lighthouse PID: $SYNC_LH_PID"

    echo ""
    echo "Sync node running. Will exit automatically when sync completes."
    echo "  Check: curl -s localhost:$SYNC_RPC_PORT -X POST -H 'Content-Type: application/json' -d '{\"jsonrpc\":\"2.0\",\"method\":\"eth_syncing\",\"params\":[],\"id\":1}'"
    echo ""

    # Wait for Nethermind to exit (ExitOnSynced will trigger exit when done)
    wait $SYNC_NM_PID

    # Stop Lighthouse if still running
    if kill -0 $SYNC_LH_PID 2>/dev/null; then
        kill $SYNC_LH_PID
        wait $SYNC_LH_PID 2>/dev/null || true
    fi

    echo "Sync stopped. Creating backup..."
    rm -rf "$BACKUP_PATH"
    cp -a "$SYNC_EL" "$BACKUP_PATH"
    echo "Backup created at $BACKUP_PATH"

    # Cleanup sync directory
    rm -rf "$SYNC_DIR"
    echo ""
fi

# === Start each node ===
for ((i=0; i<NUM_NODES; i++)); do
    NODE_DIR="$DATA_DIR/node-$i"
    mkdir -p "$NODE_DIR"

    # Copy data from backup if cleanup requested or node doesn't exist
    if [[ "$CLEANUP" == "true" ]] || [[ ! -d "$NODE_DIR/nethermind" ]]; then
        echo "Copying data for node-$i from $BACKUP_PATH..."
        rm -rf "$NODE_DIR/nethermind"
        cp -a --reflink=auto "$BACKUP_PATH" "$NODE_DIR/nethermind"
        # Delete node key so new identity is generated
        rm -f "$NODE_DIR/nethermind/keystore/node.key.plain"
    else
        echo "Using existing data for node-$i"
    fi

    # Generate JWT secret for this node (if not exists)
    [[ -f "$NODE_DIR/jwtsecret" ]] || openssl rand -hex 32 > "$NODE_DIR/jwtsecret"

    # Calculate ports
    P2P_PORT=$((30303 + i))
    RPC_PORT=$((8545 + i))
    ENGINE_PORT=$((8551 + i))
    METRICS_PORT=$((9090 + i))
    LH_P2P_PORT=$((9000 + i*2))
    LH_HTTP_PORT=$((5052 + i))

    # Start Nethermind with shared node discovery
    echo "Starting Nethermind node-$i..."
    SHARED_NODES_DIR="$SHARED_NODES_DIR" \
    SHARED_NODES_SUBNET="${SHARED_NODES_SUBNET:-}" \
    "$NETHERMIND_BIN" \
        --datadir "$NODE_DIR/nethermind" \
        --config mainnet \
        --JsonRpc.Enabled true \
        --JsonRpc.Port $RPC_PORT \
        --JsonRpc.EnginePort $ENGINE_PORT \
        --JsonRpc.EngineHost 127.0.0.1 \
        --JsonRpc.JwtSecretFile "$NODE_DIR/jwtsecret" \
        --Network.P2PPort $P2P_PORT \
        --Sync.DownloadBodiesInFastSync false --Sync.DownloadReceiptsInFastSync false \
        --Sync.NonValidatorNode true \
        --Network.DiscoveryPort $P2P_PORT \
        --Metrics.Enabled true \
        --Metrics.ExposePort $METRICS_PORT \
        --Sync.SnapSync true \
        > "$NODE_DIR/nethermind.log" 2>&1 &
    echo $! > "$PIDS_DIR/nethermind-$i.pid"

    # Start Lighthouse with checkpoint sync
    echo "Starting Lighthouse node-$i..."
    mkdir -p "$NODE_DIR/lighthouse"
    "$LIGHTHOUSE_BIN" bn \
        --network mainnet \
        --datadir "$NODE_DIR/lighthouse" \
        --execution-jwt "$NODE_DIR/jwtsecret" \
        --execution-endpoint "http://127.0.0.1:$ENGINE_PORT" \
        --checkpoint-sync-url "https://mainnet.checkpoint.sigp.io" \
        --port $LH_P2P_PORT \
        --http --http-port $LH_HTTP_PORT \
        > "$NODE_DIR/lighthouse.log" 2>&1 &
    echo $! > "$PIDS_DIR/lighthouse-$i.pid"

    echo "  Node-$i: P2P=$P2P_PORT RPC=$RPC_PORT Engine=$ENGINE_PORT LH=$LH_P2P_PORT"
done

echo ""
echo "=== Cluster started with $NUM_NODES nodes ==="
echo "Shared discovery: $SHARED_NODES_DIR"
echo "Logs: $DATA_DIR/node-*/nethermind.log"
echo "Stop: $SCRIPT_DIR/stop-cluster.sh"
