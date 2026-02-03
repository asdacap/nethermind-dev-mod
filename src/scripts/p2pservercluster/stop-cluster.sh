#!/usr/bin/env bash
set -euo pipefail

CONFIG_FILE="${CONFIG_FILE:-$HOME/nethermindstaticnodes/cluster.conf}"

# Load config
if [[ ! -f "$CONFIG_FILE" ]]; then
    echo "Error: Config file not found: $CONFIG_FILE"
    exit 1
fi
source "$CONFIG_FILE"

PIDS_DIR="$DATA_DIR/pids"

echo "Stopping cluster..."

for pidfile in "$PIDS_DIR"/*.pid; do
    [[ -f "$pidfile" ]] || continue
    pid=$(cat "$pidfile")
    name=$(basename "$pidfile" .pid)
    if kill -0 "$pid" 2>/dev/null; then
        echo "  Stopping $name (PID $pid)..."
        kill "$pid"
    fi
    rm -f "$pidfile"
done

# Wait for processes to exit
sleep 2

echo "Cluster stopped"
echo "Data preserved in $DATA_DIR/node-*/"
