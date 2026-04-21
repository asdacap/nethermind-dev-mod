#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$PWD"
WORKDIR="/mnt/workspace/nethermindworking$REPO_DIR"

if [[ ! -f "$REPO_DIR/src/Nethermind/Nethermind.Runner/Nethermind.Runner.csproj" ]]; then
  echo "Error: run from a nethermind repo root (current: $REPO_DIR)" >&2
  exit 1
fi

cd "$REPO_DIR"

NETWORK=mainnet
#DBPATH="/mnt/fastworkscratch4/nethermind_$NETWORK/"
DBPATH="/mnt/workspace/nethermind_$NETWORK/"

#DBPATH="/mnt/workscratch/nethermind/"

MODE=Debug
MODE=Release
MODE2=debug
MODE2=release
export ASHRAF_DEV=1

ORI_BIN=./src/Nethermind/artifacts/bin/Nethermind.Runner/$MODE2/*
#ORI_BIN=./src/Nethermind/artifacts/bin/Nethermind.Runner/debug/*
BINDIR=$WORKDIR/bin/$MODE
#BINDIR=$WORKDIR/bin/Debug

function build {
  #rm -r $ORI_BIN
  dotnet build -c $MODE src/Nethermind/Nethermind.Runner/Nethermind.Runner.csproj
  #dotnet build -c Debug src/Nethermind/Nethermind.Runner/Nethermind.Runner.csproj

  mkdir -p $BINDIR/plugins
  cp -avf $ORI_BIN $BINDIR

  cp $SCRIPT_DIR/../../NethermindClusterPlugin/bin/Release/net10.0/NethermindClusterPlugin.dll $BINDIR/plugins/

  #cp $SCRIPT_DIR/../../NethermindDevPlugin/bin/Release/net10.0/NethermindDevPlugin.dll $BINDIR/plugins/
}

function build_and_copy_rocksdb {
  #rm -r $ORI_BIN
  dotnet build -c Release src/Nethermind/Nethermind.Runner/Nethermind.Runner.csproj
  cp -avf $ORI_BIN $BINDIR
  rm -f $BINDIR/runtimes/linux-x64/native/librocksdb*
  cp -f $ROCKSDB_LIBRARY_PATH/lib/librocksdb.so $BINDIR/runtimes/linux-x64/native/librocksdb.so
}

IO_DEVICE=/dev/nvme0n1
GETH='enode://abf3a5f9da2919cac711afc36d2c117e8207370013135d27e94d6f421cb68f838026fc9d9d5cd027b77893a90eadd6c87e2e129e56729a51d065eeb03d1d5b47@127.0.0.1:30305'
NETH='enode://36306e32d43e7ac9ca1b177cf914d477e277c656d8753da045f2026f6d8763fb79342709ff3be85fbd78896c29cbd2ec6ae7072d5d96a06b03382d687bba4347@127.0.0.1:30308'

BOOTNODES='enr:-Ku4QImhMc1z8yCiNJ1TyUxdcfNucje3BGwEHzodEZUan8PherEo4sF7pPHPSIB1NNuSg5fZy7qFsjmUKs2ea1Whi0EBh2F0dG5ldHOIAAAAAAAAAACEZXRoMpD1pf1CAAAAAP__________gmlkgnY0gmlwhBLf22SJc2VjcDI1NmsxoQOVphkDqal4QzPMksc5wnpuC3gvSC8AfbFOnZY_On34wIN1ZHCCIyg,enr:-Le4QPUXJS2BTORXxyx2Ia-9ae4YqA_JWX3ssj4E_J-3z1A-HmFGrU8BpvpqhNabayXeOZ2Nq_sbeDgtzMJpLLnXFgAChGV0aDKQtTA_KgEAAAAAIgEAAAAAAIJpZIJ2NIJpcISsaa0Zg2lwNpAkAIkHAAAAAPA8kv_-awoTiXNlY3AyNTZrMaEDHAD2JKYevx89W0CcFJFiskdcEzkH_Wdv9iW42qLK79ODdWRwgiMohHVkcDaCI4I'

GETH=$GETH1,$GETH2
ALL_PEER="$NETH,$GETH"

#export IN_MEMORY_ACCOUNT=1

export SHARED_NODES_DIR=/home/amirul/nethermindstaticnodes/shared/
export SHARED_NODES_SUBNET="192.168.101.0/24"

function run_in_scope {
  systemd-run --user --scope \
    -p MemoryHigh=100G \
    -p MemoryMax=100G \
    -p "IOReadBandwidthMax=$IO_DEVICE 2000M" \
    -p "IOWriteBandwidthMax=$IO_DEVICE 2000M" \
    -p "IOReadIOPSMax=$IO_DEVICE 1000000" \
    -p "IOWriteIOPSMax=$IO_DEVICE 1000000" \
    "$@"
}

CMD_BASE="run_in_scope \
  $BINDIR/nethermind -dd $DBPATH \
  --JsonRpc.Enabled true \
  --JsonRpc.Port 8546 \
  --EthStats.Enabled true \
  --Init.WebSocketsEnabled true \
  --JsonRpc.IpcUnixDomainSocketPath ${DBPATH}nm.ipc \
  --Metrics.Enabled true \
  --Merge.SimulateBlockProduction false \
  --Metrics.InitializeStaticLabels false \
  --Metrics.EnableDetailedMetric true \
  --Metrics.ExposePort 9999 \
  --Metrics.DbMetricIntervalSeconds 5 \
  --Metrics.PauseDbMetricDuringBlockProcessing false \
  --JsonRpc.JwtSecretFile=/mnt/fastworkscratch4/lighthouse-$NETWORK/jwtsecret \
  --JsonRpc.EnabledModules Admin,Debug,Eth,PortalHistory,Rbuilder,Subscribe,Trace,Proof,Net \
  --Network.EnableUPnP true \
  --Db.EnableDbStatistics true \
  --Db.EnableMetricsUpdater true \
  --Db.StatsDumpPeriodSec 1 \
  --Network.P2PPort 30303 \
  --Network.DiscoveryPort 30303 \
  --Discovery.DiscoveryVersion V4 \
  --Network.DisableDiscV4DnsFeeder true \
  --Network.MaxOutgoingConnectPerSec 5000 \
  --Network.FilterPeersByRecentIp false \
  --Network.FilterPeersBySameSubnet false \
  --Network.FilterDiscoveryNodesByRecentIp false \
  --Network.FilterDiscoveryNodesBySameSubnet false \
  --Init.AutoDump Parity \
  --Sync.NonValidatorNode true \
  --Pruning.Mode Hybrid \
  --Pruning.FullPruningMinimumDelayHours 0 \
  --Pruning.FullPruningDisableLowPriorityWrites true \
  --Pruning.FullPruningMaxDegreeOfParallelism 32 \
  --Pruning.MaxBufferedCommitCount 128 \
  --Pruning.MaxUnpersistedBlockCount 30000000 \
  --Network.OnlyStaticPeers false \
  --Blocks.PreWarmStateOnBlockProcessing true \
  -c ${NETWORK} \
  --Network.MaxActivePeers 50"

#-c ${NETWORK}
  #--Pruning.SimulateLongFinalizationDepth 1024 \
  #--Pruning.CacheMb 50 \
  #--Network.DeterministicPeerPoolPortion 0.5 \
  #--Pruning.DirtyCacheMb 2000 \
  #--Pruning.DirtyNodeShardBit 8 \
  #--Pruning.CacheMb 2000 \
  #--Pruning.DirtyCacheMb 1200 \
  #--Pruning.DirtyNodeShardBit 8 \
  #--Pruning.MaxBufferedCommitCount 1 \
  #--Pruning.MaxUnpersistedBlockCount 30000000 \
  #--Db.FlushOnExit false \
  ##--Init.ExitOnInvalidBlock true \
  #--Network.EnableEnrDiscovery false \
  #--Init.DiagnosticMode VerifyTrie \
  #v--Discovery.ConcurrentDiscoveryJob 100 \
  #--Discovery.ConcurrentDiscoveryJob 10 \
  #--Sync.EnableSnapSyncStorageRangeSplit true \
  #--Discovery.ConcurrentDiscoveryJob 5 \
  #--Network.ClientIdMatcher Nethermind \
  #--Network.EnableEnrDiscovery false \
  #--Sync.MaxTxInForwardSyncBuffer 200000 \
# --Merge.SimulateBlockProduction false
  #--Blocks.PreWarmStateOnBlockProcessing true \
  #--Pruning.PersistenceInterval 1000000 \
  #--Init.StateDbKeyScheme HalfPath  \
# --Pruning.Mode Hybrid \
  #--Network.StaticPeers $ALL_PEER \
# --Pruning.FullPruningDisableLowPriorityWrites true \
# --Pruning.FullPruningMaxDegreeOfParallelism 32 \
  #--Network.ProcessingThreadCount 1 \
# --Pruning.FullPruningMinimumDelayHours 0 \
# --Pruning.FullPruningMemoryBudgetMb 6000 "
#--Pruning.TrackedPastKeyCountMemoryRatio 0 \
  #--Sync.MaxTxInForwardSyncBuffer 10000 \
#--Sync.AncientBodiesBarrier 21000000 \
  #--Sync.NonValidatorNode true \
  #--Sync.VerifyTrieOnStateSyncFinished false \
  #--Sync.FastSyncCatchUpHeightDelta 1024000 \
  #--Sync.MaxAttemptsToUpdatePivot -1 \
  #--Sync.PivotNumber 22004386 --Sync.PivotHash 0xe6ec6bd6a2160f31d8ebe7e35381de45987780299521698b2fa5217062d29f63 --Sync.PivotTotalDifficulty 58750003716598352816469 \
#--Sync.PivotNumber 1078331 --Sync.PivotHash 0x8de784c12507d37bdd487131dd6fd62c3451455c3ee5d6afc1ef37e1f48a6c63 --Sync.PivotTotalDifficulty 8179904909205883832 \
#--JsonRpc.WebSocketsProcessingConcurrency 32 \
#--JsonRpc.EnablePerMethodMetrics true \
  #--Network.ClientIdMatcher Nethermind \
#--Init.MemoryHint 2000000000
#--JsonRpc.IpcProcessingConcurrency 32 \
#--Pruning.FullPruningCompletionBehavior AlwaysShutdown \
#--Init.ExitOnBlockNumber 21060000 \
#--Db.StateDbWriteBufferSize 200000000 \
#-p AllowedCPUs=0-32 \
#-p MemoryMax=400G \
#-p MemoryHigh=500G \

#--Sync.VerifyTrieOnStateSyncFinished true #--JsonRpc.WebSocketsPort 8547 \
#--Init.MemoryHint 2000000000
#--Sync.AncientBodiesBarrier 21000000 \
#--Pruning.FullPruningCompletionBehavior AlwaysShutdown \
#--Pruning.CacheMb 2000 \
#--Network.ClientIdMatcher Nethermind \"
#--Sync.NonValidatorNode true \
#--Network.ClientIdMatcher Geth \

#CMD="$CMD_BASE --Sync.ExitOnSynced true"
CMD_GETH="$CMD --Network.ClientIdMatcher '^(?!\s*$).+'"
CMD_BODY="$CMD_BASE --Sync.DownloadBodiesInFastSync true --Sync.DownloadReceiptsInFastSync false --Sync.ExitOnSynced true --Sync.ExitOnSyncedWaitTimeSec 120"
CMD_FULL="$CMD_BASE --Sync.DownloadBodiesInFastSync true --Sync.DownloadReceiptsInFastSync true --Sync.ExitOnSyncedWaitTimeSec 300  --Sync.ExitOnSynced false"
CMD_SNAP="$CMD_BASE --Sync.DownloadBodiesInFastSync false --Sync.DownloadReceiptsInFastSync false "
CMD_SNAP_NO_STOP="$CMD_BASE --Sync.DownloadBodiesInFastSync false --Sync.DownloadReceiptsInFastSync false  --Sync.ExitOnSynced false"
CMD_STATE="$CMD_BASE --Sync.DownloadBodiesInFastSync false --Sync.DownloadReceiptsInFastSync false  --Sync.ExitOnSynced false --Sync.SnapSync false"
CMD_FULL_GENESIS="$CMD_BASE --Sync.FastSync false --Sync.SnapSync false"

#build
#$CMD --Network.ProcessingThreadCount 32

BAKDIR=/mnt/fastworkscratch3/nethermind_bak

#rm -rf ~/fastworkscratch3/nethermind_halfpath_workspace/ || true
#cp -av ~/fastworkscratch3/nethermind_halfpath/  ~/fastworkscratch3/nethermind_halfpath_workspace/

HASH_BACKUP=~/fastworkscratch/nethermind_something/
OTHERDB=~/fastworkscratch3/nethermind/
OTHERDB2=~/fastworkscratch3/nethermind_halfpath_2/
OTHERDB3=~/fastworkscratch3/nethermind_halfpath_3/
HASHBACKUP=~/fastworkscratch3/nethermind_hash/
FLAT_BACKUP=~/fastworkscratch3/nethermind_flat/

BEFOREDB=~/fastworkscratch3/nethermind_before/
AFTERDB=~/fastworkscratch3/nethermind_after
AFTERDB2=~/fastworkscratch3/nethermind_after2/

BEFOREDB=/mnt/fastworkscratch3/nethermind_state_9_aug
BEFOREDB2=/mnt/fastworkscratch3/nethermind_state_9_aug_halfpath_16k

#$CMD_SNAP --Db.StateDbAdditionalRocksDbOptions "block_based_table_factory.index_type=kHashSearch;block_based_table_factory.partition_filters=0;prefix_extractor=capped:44;"

#PARAM="--Era.ImportDirectory /mnt/fastworkscratch3/era-export-2-feb/ --Era.From 21020000 --Db.StateDbEnableFileWarmer true --Init.ExitOnBlockNumber 21070000 --Db.StateDbAdditionalRocksDbOptions block_based_table_factory={index_type=kBinarySearch;partition_filters=0;};ttl=0;use_direct_reads=1;"
PARAM=" --Pruning.DirtyCacheMb 2000 --Init.ExitOnBlockNumber 21200000 --Db.StateDbEnableFileWarmer true --Db.StateDbAdditionalRocksDbOptions block_based_table_factory={index_type=kBinarySearch;partition_filters=0;};ttl=0;"
PARAM=""
#$CMD_SNAP --Db.StateDbAdditionalRocksDbOptions "unordered_write=false;"


build

function run_full {

  build

  #rm -r $DBPATH || true
  $CMD_FULL
}


function era_export_full {

  build

  $CMD_FULL --Era.ExportDirectory /mnt/large_workscratch/era-sepolia-23-sept/
}

#run_full
#era_export_full

function replay_recent {

  build


  rm -r $DBPATH || true
  cp -av /mnt/fastworkscratch4/nethermind_mainnet_dis_16/ $DBPATH
  $CMD_FULL_GENESIS --Era.ImportDirectory /mnt/fastworkscratch3/era-export-2-feb \
    --Era.From 21413085 \
    --Era.Concurrency 1 \
    --Era.ImportBlocksBufferSize 9000 \
    --Pruning.Mode Hybrid \
    $@ || true


    #--Init.ExitOnBlockNumber 21100000 \
    #--Era.From 21413085 \
    #--Era.From 21033682 \
    #--Init.ExitOnBlockNumber 21100000 \ # should be about 15 minute
    #--Db.StateDbAdditionalRocksDbOptions "ttl=0;max_bytes_for_level_base=2000000000;max_bytes_for_level_multiplier=10;block_based_table_factory.block_size=64000;" \
    #--Init.ExitOnBlockNumber 21060000 \ # should be about 10 minute

}


function verify {

    build
    $CMD_SNAP --Init.DiagnosticMode VerifyTrie
}

#verify

function run_snap_hash {

    build
    rm -r $DBPATH || true

    $CMD_SNAP --Init.StateDbKeyScheme Hash
}

#run_snap_hash

function run_snap {

    build
    rm -r $DBPATH || true
    #cp -av /mnt/workspace/nethermind_mainnet_halfpath_april_15/ $DBPATH

    $CMD_SNAP
}

function run_flat_snap {

    build
    rm -r $DBPATH || true

    $CMD_SNAP --FlatDb.Enabled true
}

#run_flat_snap

function run_flat_full {

    build
    rm -r $DBPATH || true
    #cp -av /mnt/workspace/nethermind_hoodi/ $DBPATH

    $CMD_FULL_GENESIS --FlatDb.Enabled true
}

#run_flat_full

function run_full_genesis {
  build
  rm -r $DBPATH || true
  #cp -av /mnt/fastworkscratch4/nethermind_mainnet_archive_header_only/ $DBPATH
  #cp -av /mnt/fastworkscratch4/nethermind_mainnet_archive_header_7/ $DBPATH
  #cp -av /mnt/fastworkscratch4/nethermind_mainnet_archive_flatcache_23m/ $DBPATH
  $CMD_FULL_GENESIS $@
}

#run_full_genesis --Init.ExitOnBlockNumber 23300000

function era_from_genesis {

  build
  rm -r $DBPATH || true

  $CMD_FULL_GENESIS --Era.ImportDirectory /mnt/large_workscratch/era-hoodi-march-2/ \
    --Era.From 0 \
    --Era.Concurrency 1 \
    --Era.ImportBlocksBufferSize 100 \
    --FlatDb.CompactSize 256 \
    --FlatDb.Enabled true \
    $@ || true

    #--Era.From 500000 \
    #--FlatDb.Enabled true \
    #--Paprika.Enabled true \

}

function era_debug {

  build
  rm -r $DBPATH || true
  cp -av /mnt/workspace/mainnet_1000000/ $DBPATH

  $CMD_FULL_GENESIS --Era.ImportDirectory /mnt/large_workscratch/era-mainnet-27-jul/ \
    --Era.From 1000000 \
    --Era.Concurrency 1 \
    --Era.ImportBlocksBufferSize 512 \
    --Init.ExitOnBlockNumber 2000000 \
    --FlatDb.Enabled true \
    $@ || true

}

#era_debug


#era_from_genesis

function flat_import {
  local TARGET="$1"
  shift

  build
  rm -r $DBPATH || true
  cp -av /mnt/workspace/nethermind_mainnet_bak_with_headers/ $DBPATH

  $CMD_FULL_GENESIS  \
    --Era.ImportBlocksBufferSize 4096 \
    --FlatDb.VerifyWithTrie false \
    --FlatDb.MinReorgDepth 128 \
    --FlatDb.MaxReorgDepth 256 \
    --FlatDb.ImportFromPruningTrieState true \
    --Blocks.PreWarmStateOnBlockProcessing true \
    --FlatDb.Enabled true "$@"

  cp -av $DBPATH $TARGET
}

function flat_replay_genesis {
  rm -r $DBPATH || true

  $CMD_FULL_GENESIS  \
    --Era.ImportBlocksBufferSize 4096 \
    --FlatDb.Layout Flat \
    --FlatDb.VerifyWithTrie false \
    --FlatDb.MinReorgDepth 128 \
    --FlatDb.MaxReorgDepth 256 \
    --FlatDb.ImportFromPruningTrieState true \
    --FlatDb.EnablePreimageRecording true \
    --Blocks.PreWarmStateOnBlockProcessing true \
    --FlatDb.Enabled true \
    --Era.ImportDirectory /mnt/large_workscratch/era-mainnet-27-jul/ \
    --Init.ExitOnBlockNumber 1000000

}

#build

EXIT_BLOCK_NUMBER=33500000

function flat_replay {
  build
  rm -r $DBPATH || true
  cp -av $1 $DBPATH

  shift

  echo "Clearing cache.."
  sudo /nix/store/16npdypxihn91bg2ng9dwx5rq77p89gz-clear-ram-cache/bin/clear-ram-cache
  echo "Cleared"
  SKIP_PREWARM_TX=0 BAL_PATH=/mnt/workspace/recordedbal/ BAL_REPLAY=1 $CMD_FULL_GENESIS  \
    --Era.ImportBlocksBufferSize 4096 \
    --FlatDb.VerifyWithTrie false \
    --FlatDb.ImportFromPruningTrieState true \
    --Init.ExitOnBlockNumber $EXIT_BLOCK_NUMBER \
    --FlatDb.Enabled true $@ \
    #--Era.ImportDirectory /mnt/large_workscratch/era-mainnet-19-dis-23mil_up/ \
    #--FlatDb.TrieCacheMemoryBudget 1000000000 \
    #--FlatDb.TrieCacheMemoryBudget 2000000000 \
    #--FlatDb.CompactSize 64 \
    #--FlatDb.MaxReorgDepth 2048 \
    #--FlatDb.TrieCacheMemoryBudget 100000000 \
    #--FlatDb.MaxInMemoryReorgDepth 32 \
    #--FlatDb.TrieCacheMemoryBudget 2000000000 \
    #--FlatDb.MaxReorgDepth 2048 \
    #--FlatDb.MinReorgDepth 128 \
    #--Era.From 3000000 $@
    #--Init.ExitOnBlockNumber 23494695 \
    #--Init.ExitOnBlockNumber 23494695 \
    #--Init.ExitOnBlockNumber 23455625 \
    #--Era.ImportDirectory /mnt/large_workscratch/era-mainnet-27-jul/ \
    #--Era.From 23444695

}

function run_halfpath {

    build
    rm -r $DBPATH || true
    cp -av /mnt/workspace/nethermind_mainnet_bak_with_headers/ $DBPATH

    BAL_PATH=/mnt/workspace/recordedbal/ BAL_REPLAY=1 $CMD_SNAP
}

#flat_replay /mnt/workspace/nethermind_mainnet_flat_9mac_headers/

run_halfpath
