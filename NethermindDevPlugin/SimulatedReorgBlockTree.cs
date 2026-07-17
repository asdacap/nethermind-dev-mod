using System.Collections.Concurrent;
using System.Reflection;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Tracing;
using Nethermind.Blockchain.Visitors;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Logging;

namespace NethermindDevPlugin;

public class SimulatedReorgBlockTree(
    IBlockTree baseBlockTree,
    IDevPluginConfig config,
    IProcessExitSource exitSource,
    ILogManager logManager) : IBlockTree, IBlockTreeHealer
{
    private readonly ILogger _logger = logManager.GetClassLogger<SimulatedReorgBlockTree>();
    private readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<Hash256, TaskCompletionSource<bool>> _waiters = new();
    private readonly object _stateLock = new();

    private IBlockProducerEnv? _env;
    private IBlockProcessingQueue? _processingQueue;
    private bool _active;
    private SimulatedReorgMode _mode;
    // Flip-flop state.
    private Block? _lastSimulated;
    private int _counter;
    // Batch state.
    private readonly Queue<Block> _canonicalHistory = new();
    private int _batchCounter;

    public void Initialize(IBranchProcessor branchProcessor, IBlockProducerEnvFactory envFactory, IBlockProcessingQueue processingQueue)
    {
        if (config.SimulatedReorgDepth <= 0) return;
        _env = envFactory.CreatePersistent();
        _processingQueue = processingQueue;
        _mode = config.SimulatedReorgMode;
        branchProcessor.BlockProcessed += OnBranchBlockProcessed;
        _active = true;
        if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: active mode={_mode} depth={config.SimulatedReorgDepth}");
    }

    private void OnBranchBlockProcessed(object? sender, BlockProcessedEventArgs e)
    {
        if (e.Block.Hash is { } hash && _waiters.TryRemove(hash, out TaskCompletionSource<bool>? tcs))
        {
            tcs.TrySetResult(true);
        }
    }

    private bool WaitForProcessed(Hash256 hash, ulong blockNumber)
    {
        if (baseBlockTree.WasProcessed(blockNumber, hash)) return true;

        TaskCompletionSource<bool> tcs = _waiters.GetOrAdd(hash,
            _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        // Re-check after registering to close the race between processing and registration.
        if (baseBlockTree.WasProcessed(blockNumber, hash))
        {
            _waiters.TryRemove(hash, out _);
            return true;
        }

        try
        {
            return tcs.Task.Wait((int)_waitTimeout.TotalMilliseconds, exitSource.Token);
        }
        catch (OperationCanceledException) { return false; }
        finally
        {
            _waiters.TryRemove(hash, out _);
        }
    }

    private void ResetFork(string reason)
    {
        if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: reset fork — {reason}");
        _lastSimulated = null;
        _counter = 0;
    }

    public AddBlockResult SuggestBlock(Block block, BlockTreeSuggestOptions options = BlockTreeSuggestOptions.ShouldProcess)
    {
        AddBlockResult result = baseBlockTree.SuggestBlock(block, options);
        if (!_active || result != AddBlockResult.Added) return result;
        if ((options & BlockTreeSuggestOptions.ShouldProcess) == 0) return result;
        if (block.Hash is null) return result;

        try
        {
            InterceptAndChain(block);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: exception during interception for #{block.Number}: {ex}");
            ResetFork("exception during interception");
        }

        return result;
    }

    private void InterceptAndChain(Block block)
    {
        lock (_stateLock)
        {
            // 1. Wait for canonical to finish processing the just-suggested block. The base
            //    SuggestBlock above queues it; the canonical processor consumes the queue
            //    on its own thread and raises BranchProcessor.BlockProcessed when done.
            if (!WaitForProcessed(block.Hash!, block.Number))
            {
                ResetFork($"canonical did not process incoming #{block.Number} {block.Hash} within {_waitTimeout.TotalSeconds:F0}s");
                return;
            }

            switch (_mode)
            {
                case SimulatedReorgMode.FlipFlop:
                    RunFlipFlopReorg(block);
                    break;
                case SimulatedReorgMode.Batch:
                    RunBatchReorg(block);
                    break;
            }
        }
    }

    private void RunFlipFlopReorg(Block block)
    {
        // Divergence strategy: keep all canonical txs (no drops), but on the fork-start
        // sibling override Beneficiary so the block reward / EIP-1559 tip lands at a different
        // address. State diverges by that one balance; canonical txs all still execute since
        // their nonces/balances are unaffected. Subsequent (continuation) siblings reuse
        // canonical txs verbatim — divergence rides on the simulated parent state.
        Hash256 parentHash;
        Transaction[] txs;
        bool divergeBeneficiary;
        if (_counter == 0)
        {
            parentHash = block.Header.ParentHash!;
            divergeBeneficiary = true;
        }
        else
        {
            parentHash = _lastSimulated!.Header.Hash!;
            divergeBeneficiary = false;
        }
        txs = block.Transactions.Select(CloneTx).ToArray();

        Block? processed = BuildAndProcessSibling(block, parentHash, txs, divergeBeneficiary);
        if (processed?.Hash is null)
        {
            ResetFork($"producer Process returned null for sibling at #{block.Number}");
            return;
        }

        if (!SuggestAndEnqueue(processed, parentHash))
        {
            ResetFork($"could not suggest+enqueue sibling at #{processed.Number}");
            return;
        }

        if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: sibling #{processed.Number} {processed.Hash} parent={parentHash} counter={_counter + 1}/{config.SimulatedReorgDepth} suggested");

        if (!WaitForProcessed(processed.Hash, processed.Number))
        {
            ResetFork($"canonical did not process sibling #{processed.Number} {processed.Hash}");
            return;
        }

        _lastSimulated = processed;
        _counter = (_counter + 1) % config.SimulatedReorgDepth;
    }

    private void RunBatchReorg(Block block)
    {
        int depth = config.SimulatedReorgDepth;
        _canonicalHistory.Enqueue(block);
        while (_canonicalHistory.Count > depth + 1) _canonicalHistory.Dequeue();
        _batchCounter++;

        if (_batchCounter < depth) return;
        if (_canonicalHistory.Count < depth + 1) return;     // need a parent for the depth-N sibling

        _batchCounter = 0;

        Block[] history = _canonicalHistory.ToArray();      // oldest first → index 0 = depth N, index N = head
        if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: batch START — depth={depth}, head=#{history[^1].Number} {history[^1].Hash}, will build {history.Length} siblings from #{history[0].Number} (depth {depth}) up to head");
        Block? prevSibling = null;
        for (int i = 0; i < history.Length; i++)
        {
            Block canonical = history[i];
            Hash256 parentHash;
            bool divergeBeneficiary;
            if (i == 0)
            {
                parentHash = canonical.Header.ParentHash!;
                divergeBeneficiary = true;
            }
            else
            {
                parentHash = prevSibling!.Header.Hash!;
                divergeBeneficiary = false;
            }
            Transaction[] txs = canonical.Transactions.Select(CloneTx).ToArray();

            Block? processed = BuildAndProcessSibling(canonical, parentHash, txs, divergeBeneficiary);
            if (processed?.Hash is null)
            {
                if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: batch aborted — env Process returned null at depth {depth - i} (#{canonical.Number})");
                return;
            }

            if (!SuggestAndEnqueue(processed, parentHash))
            {
                if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: batch aborted — could not suggest+enqueue at depth {depth - i} (#{canonical.Number})");
                return;
            }

            if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: batch sibling depth={depth - i} #{processed.Number} {processed.Hash} parent={parentHash}");

            if (!WaitForProcessed(processed.Hash, processed.Number))
            {
                if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: batch aborted — canonical did not process sibling #{processed.Number} within timeout");
                return;
            }

            prevSibling = processed;
        }

        if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: batch DONE — {history.Length} siblings built, head now on side branch");
    }

    // Mirrors the engine_newPayload path (NewPayloadHandler.cs:353-385): SuggestBlockAsync with
    // ForceDontSetAsMain so we don't overwrite canonical at the sibling's height, then directly
    // Enqueue into the processing queue because BlockTree.Suggest's NewBestSuggestedBlock event
    // (the normal enqueue trigger) is gated by BestSuggestedImprovementRequirementsSatisfied,
    // which rejects siblings whose number is below current BestSuggestedBody.
    private bool SuggestAndEnqueue(Block sibling, Hash256 parentHash)
    {
        AddBlockResult res = baseBlockTree.SuggestBlockAsync(sibling, BlockTreeSuggestOptions.ForceDontSetAsMain)
            .AsTask().GetAwaiter().GetResult();
        if (res != AddBlockResult.Added && res != AddBlockResult.AlreadyKnown)
        {
            if (_logger.IsWarn) _logger.Warn($"SimulatedReorgBlockTree: SuggestBlockAsync returned {res} for sibling #{sibling.Number} parent={parentHash}");
            return false;
        }

        ValueTask vt = _processingQueue!.Enqueue(sibling, ProcessingOptions.StoreReceipts);
        if (!vt.IsCompletedSuccessfully) vt.AsTask().GetAwaiter().GetResult();
        return true;
    }

    // Sentinel used as the diverging ParentBeaconBlockRoot on fork-start siblings. EIP-4788's
    // system call at block start (BlockProcessor.cs:135) writes this value into the beacon root
    // contract's storage BEFORE any user tx — so state root diverges via that storage slot
    // without touching any user account's balance/nonce. No tx in the block is affected.
    private static readonly Hash256 DivergeBeaconRoot = Keccak.Compute("SimulatedReorgBlockTree.fork-start");

    private Block? BuildAndProcessSibling(Block canonical, Hash256 parentHash, Transaction[] txs, bool divergeBeneficiary)
    {
        BlockHeader siblingHeader = canonical.Header.Clone();
        siblingHeader.ParentHash = parentHash;
        if (divergeBeneficiary && siblingHeader.ParentBeaconBlockRoot is not null)
        {
            siblingHeader.ParentBeaconBlockRoot = DivergeBeaconRoot;
        }
        siblingHeader.StateRoot = null;
        siblingHeader.ReceiptsRoot = null;
        siblingHeader.Bloom = Bloom.Empty;
        siblingHeader.GasUsed = 0;
        siblingHeader.Hash = null;
        // TotalDifficulty intentionally retained — post-merge same TD across siblings, and
        // BlockchainProcessor.RunSimpleChecksAheadOfProcessing rejects null TD unconditionally.

        // BlockToProduce: env uses BlockProductionTransactionsExecutor which silently SKIPS
        // failing txs and rewrites block.Transactions + header.TxRoot to only the survivors
        // (BlockProcessor.BlockProductionTransactionsExecutor.cs:85-89). A plain Block would
        // leave the body unchanged so canonical would re-run the failed tx and throw. With
        // BlockToProduce, the suggested block contains only txs that actually executed.
        BlockToProduce siblingBlock = new(siblingHeader, txs, canonical.Uncles ?? [], canonical.Withdrawals);

        Block? processed = _env!.ChainProcessor.Process(
            siblingBlock,
            ProcessingOptions.ProducingBlock,
            NullBlockTracer.Instance,
            exitSource.Token);

        return processed;
    }

    // ===== Tx clone (avoid sharing Transaction references with canonical) =====
    //
    // Background: Transaction.SenderAddress is non-RLP, mutated in place by RecoverSignatures.
    // Our sibling shares tx references with canonical via SkipLast(...).ToArray() — so canonical's
    // recovery results bleed into our sibling. RecoverSignatures.RecoverData has an early-exit:
    // "if txs[0].SenderAddress is not null, return without recovering anything". When a reorg
    // re-runs a block with mixed in-memory state (txs[0] still has the cached sender but later txs
    // got new no-sender deserialized instances), the early-exit fires and a later tx hits the EVM
    // with SenderAddress == null → InvalidTransactionException("sender not specified").
    //
    // Fix: deep-clone the canonical txs we're keeping in the sibling, and reset SenderAddress so
    // recovery runs fully on our sibling's txs. Each sibling owns its own Transaction instances;
    // no shared mutation with canonical.

    private static readonly MethodInfo _memberwiseClone =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static Transaction CloneTx(Transaction src)
    {
        Transaction clone = (Transaction)_memberwiseClone.Invoke(src, null)!;
        clone.SenderAddress = null;
        return clone;
    }

    // ===== IBlockTree delegation =====

    public Block? FindBlock(Hash256 blockHash, BlockTreeLookupOptions options, ulong? blockNumber = null) =>
        baseBlockTree.FindBlock(blockHash, options, blockNumber);

    public Block? FindBlock(ulong blockNumber, BlockTreeLookupOptions options) =>
        baseBlockTree.FindBlock(blockNumber, options);

    public bool HasBlock(ulong blockNumber, Hash256 blockHash) => baseBlockTree.HasBlock(blockNumber, blockHash);

    public BlockHeader? FindHeader(Hash256 blockHash, BlockTreeLookupOptions options, ulong? blockNumber = null) =>
        baseBlockTree.FindHeader(blockHash, options, blockNumber);

    public BlockHeader? FindHeader(ulong blockNumber, BlockTreeLookupOptions options) =>
        baseBlockTree.FindHeader(blockNumber, options);

    public Hash256? FindBlockHash(ulong blockNumber) => baseBlockTree.FindBlockHash(blockNumber);

    public bool IsMainChain(BlockHeader blockHeader) => baseBlockTree.IsMainChain(blockHeader);

    public bool IsMainChain(Hash256 blockHash, bool throwOnMissingHash = true) =>
        baseBlockTree.IsMainChain(blockHash, throwOnMissingHash);

    public BlockHeader FindBestSuggestedHeader() => baseBlockTree.FindBestSuggestedHeader();

    public ulong GetLowestBlock() => baseBlockTree.GetLowestBlock();

    public Hash256 HeadHash => baseBlockTree.HeadHash;

    public Hash256 GenesisHash => baseBlockTree.GenesisHash;

    public Hash256? PendingHash => baseBlockTree.PendingHash;

    public Hash256? FinalizedHash => baseBlockTree.FinalizedHash;

    public Hash256? SafeHash => baseBlockTree.SafeHash;

    public Block? Head => baseBlockTree.Head;

    public AddBlockResult Insert(BlockHeader header, BlockTreeInsertHeaderOptions headerOptions = BlockTreeInsertHeaderOptions.None) =>
        baseBlockTree.Insert(header, headerOptions);

    public void BulkInsertHeader(IReadOnlyList<BlockHeader> headers, BlockTreeInsertHeaderOptions headerOptions = BlockTreeInsertHeaderOptions.None) =>
        baseBlockTree.BulkInsertHeader(headers, headerOptions);

    public AddBlockResult Insert(Block block, BlockTreeInsertBlockOptions insertBlockOptions = BlockTreeInsertBlockOptions.None,
        BlockTreeInsertHeaderOptions insertHeaderOptions = BlockTreeInsertHeaderOptions.None, WriteFlags bodiesWriteFlags = WriteFlags.None) =>
        baseBlockTree.Insert(block, insertBlockOptions, insertHeaderOptions, bodiesWriteFlags);

    public void UpdateHeadBlock(Hash256 blockHash) => baseBlockTree.UpdateHeadBlock(blockHash);

    public void NewOldestBlock(ulong oldestBlock) => baseBlockTree.NewOldestBlock(oldestBlock);

    public ValueTask<AddBlockResult> SuggestBlockAsync(Block block, BlockTreeSuggestOptions options = BlockTreeSuggestOptions.ShouldProcess) =>
        baseBlockTree.SuggestBlockAsync(block, options);

    public AddBlockResult SuggestHeader(BlockHeader header) => baseBlockTree.SuggestHeader(header);

    public bool IsKnownBlock(ulong number, Hash256 blockHash) => baseBlockTree.IsKnownBlock(number, blockHash);

    public bool IsKnownBeaconBlock(ulong number, Hash256 blockHash) => baseBlockTree.IsKnownBeaconBlock(number, blockHash);

    public bool WasProcessed(ulong number, Hash256 blockHash) => baseBlockTree.WasProcessed(number, blockHash);

    public bool TryUpdateMainChain(BlockHeader newHead, bool wereProcessed, bool forceUpdateHeadBlock = false, params ReadOnlySpan<Block> preloadedBlocks) =>
        baseBlockTree.TryUpdateMainChain(newHead, wereProcessed, forceUpdateHeadBlock, preloadedBlocks);

    public void MarkChainAsProcessed(IReadOnlyList<Block> blocks) => baseBlockTree.MarkChainAsProcessed(blocks);

    public Task Accept(IBlockTreeVisitor blockTreeVisitor, CancellationToken cancellationToken) =>
        baseBlockTree.Accept(blockTreeVisitor, cancellationToken);

    public (BlockInfo? Info, ChainLevelInfo? Level) GetInfo(ulong number, Hash256 blockHash) => baseBlockTree.GetInfo(number, blockHash);

    public ChainLevelInfo? FindLevel(ulong number) => baseBlockTree.FindLevel(number);

    public BlockInfo FindCanonicalBlockInfo(ulong blockNumber) => baseBlockTree.FindCanonicalBlockInfo(blockNumber);

    public Hash256? FindHash(ulong blockNumber) => baseBlockTree.FindHash(blockNumber);

    public IOwnedReadOnlyList<BlockHeader> FindHeaders(Hash256 hash, int numberOfBlocks, int skip, bool reverse) =>
        baseBlockTree.FindHeaders(hash, numberOfBlocks, skip, reverse);

    public void DeleteInvalidBlock(Block invalidBlock) => baseBlockTree.DeleteInvalidBlock(invalidBlock);

    public void ReportBadBlock(Block badBlock) => baseBlockTree.ReportBadBlock(badBlock);

    public void DeleteOldBlock(ulong blockNumber, Hash256 blockHash) => baseBlockTree.DeleteOldBlock(blockNumber, blockHash);

    public void ForkChoiceUpdated(Hash256? finalizedBlockHash, Hash256? safeBlockBlockHash) =>
        baseBlockTree.ForkChoiceUpdated(finalizedBlockHash, safeBlockBlockHash);

    public ulong LastFinalizedBlockLevel => baseBlockTree.LastFinalizedBlockLevel;

    public int DeleteChainSlice(in ulong startNumber, ulong? endNumber = null, bool force = false) =>
        baseBlockTree.DeleteChainSlice(in startNumber, endNumber, force);

    public bool IsBetterThanHead(BlockHeader? header) => baseBlockTree.IsBetterThanHead(header);

    public void UpdateBeaconMainChain(IReadOnlyList<BlockInfo>? blockInfos, ulong clearBeaconMainChainStartPoint) =>
        baseBlockTree.UpdateBeaconMainChain(blockInfos, clearBeaconMainChainStartPoint);

    public void RecalculateTreeLevels() => baseBlockTree.RecalculateTreeLevels();

    public void HealCanonicalChain(Hash256 startHash, long maxBlockDepth) =>
        ((IBlockTreeHealer)baseBlockTree).HealCanonicalChain(startHash, maxBlockDepth);

    public ulong NetworkId => baseBlockTree.NetworkId;

    public ulong ChainId => baseBlockTree.ChainId;

    public BlockHeader? Genesis => baseBlockTree.Genesis;

    public BlockHeader? BestSuggestedHeader => baseBlockTree.BestSuggestedHeader;

    public Block? BestSuggestedBody => baseBlockTree.BestSuggestedBody;

    public BlockHeader? BestSuggestedBeaconHeader => baseBlockTree.BestSuggestedBeaconHeader;

    public BlockHeader? LowestInsertedHeader
    {
        get => baseBlockTree.LowestInsertedHeader;
        set => baseBlockTree.LowestInsertedHeader = value;
    }

    public BlockHeader? LowestInsertedBeaconHeader
    {
        get => baseBlockTree.LowestInsertedBeaconHeader;
        set => baseBlockTree.LowestInsertedBeaconHeader = value;
    }

    public ulong BestKnownNumber => baseBlockTree.BestKnownNumber;

    public ulong BestKnownBeaconNumber => baseBlockTree.BestKnownBeaconNumber;

    public bool CanAcceptNewBlocks => baseBlockTree.CanAcceptNewBlocks;

    public (ulong BlockNumber, Hash256 BlockHash) SyncPivot
    {
        get => baseBlockTree.SyncPivot;
        set => baseBlockTree.SyncPivot = value;
    }

    public bool IsProcessingBlock
    {
        get => baseBlockTree.IsProcessingBlock;
        set => baseBlockTree.IsProcessingBlock = value;
    }

    public event EventHandler<FinalizeEventArgs>? BlocksFinalized
    {
        add => baseBlockTree.BlocksFinalized += value;
        remove => baseBlockTree.BlocksFinalized -= value;
    }

    public event EventHandler<BlockEventArgs>? NewBestSuggestedBlock
    {
        add => baseBlockTree.NewBestSuggestedBlock += value;
        remove => baseBlockTree.NewBestSuggestedBlock -= value;
    }

    public event EventHandler<BlockEventArgs>? NewSuggestedBlock
    {
        add => baseBlockTree.NewSuggestedBlock += value;
        remove => baseBlockTree.NewSuggestedBlock -= value;
    }

    public event EventHandler<BlockReplacementEventArgs>? BlockAddedToMain
    {
        add => baseBlockTree.BlockAddedToMain += value;
        remove => baseBlockTree.BlockAddedToMain -= value;
    }

    public event EventHandler<BlockEventArgs>? NewHeadBlock
    {
        add => baseBlockTree.NewHeadBlock += value;
        remove => baseBlockTree.NewHeadBlock -= value;
    }

    public event EventHandler<OnUpdateMainChainArgs>? OnUpdateMainChain
    {
        add => baseBlockTree.OnUpdateMainChain += value;
        remove => baseBlockTree.OnUpdateMainChain -= value;
    }

    public event EventHandler<IBlockTree.ForkChoiceUpdateEventArgs>? OnForkChoiceUpdated
    {
        add => baseBlockTree.OnForkChoiceUpdated += value;
        remove => baseBlockTree.OnForkChoiceUpdated -= value;
    }
}
