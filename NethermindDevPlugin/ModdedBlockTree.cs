using Nethermind.Blockchain;
using Nethermind.Blockchain.Visitors;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Logging;

namespace NethermindDevPlugin;

public class ModdedBlockTree(IBlockTree baseBlockTree, ILogManager logManager): IBlockTree, IBlockTreeHealer
{
    ILogger _logger = logManager.GetClassLogger<ModdedBlockTree>();
    
    public Block? FindBlock(Hash256 blockHash, BlockTreeLookupOptions options, ulong? blockNumber = null)
    {
        return baseBlockTree.FindBlock(blockHash, options, blockNumber);
    }

    public Block? FindBlock(ulong blockNumber, BlockTreeLookupOptions options)
    {
        return baseBlockTree.FindBlock(blockNumber, options);
    }

    public bool HasBlock(ulong blockNumber, Hash256 blockHash)
    {
        return baseBlockTree.HasBlock(blockNumber, blockHash);
    }

    public BlockHeader? FindHeader(Hash256 blockHash, BlockTreeLookupOptions options, ulong? blockNumber = null)
    {
        return baseBlockTree.FindHeader(blockHash, options, blockNumber);
    }

    public BlockHeader? FindHeader(ulong blockNumber, BlockTreeLookupOptions options)
    {
        return baseBlockTree.FindHeader(blockNumber, options);
    }

    public Hash256? FindBlockHash(ulong blockNumber)
    {
        return baseBlockTree.FindBlockHash(blockNumber);
    }

    public bool IsMainChain(BlockHeader blockHeader)
    {
        return baseBlockTree.IsMainChain(blockHeader);
    }

    public bool IsMainChain(Hash256 blockHash, bool throwOnMissingHash = true)
    {
        return baseBlockTree.IsMainChain(blockHash, throwOnMissingHash);
    }

    public BlockHeader FindBestSuggestedHeader()
    {
        return baseBlockTree.FindBestSuggestedHeader();
    }

    public ulong GetLowestBlock()
    {
        return baseBlockTree.GetLowestBlock();
    }

    public Hash256 HeadHash => baseBlockTree.HeadHash;

    public Hash256 GenesisHash => baseBlockTree.GenesisHash;

    public Hash256? PendingHash => baseBlockTree.PendingHash;

    public Hash256? FinalizedHash => baseBlockTree.FinalizedHash;

    public Hash256? SafeHash => baseBlockTree.SafeHash;

    public Block? Head => baseBlockTree.Head;

    public AddBlockResult Insert(BlockHeader header, BlockTreeInsertHeaderOptions headerOptions = BlockTreeInsertHeaderOptions.None)
    {
        return baseBlockTree.Insert(header, headerOptions);
    }

    public void BulkInsertHeader(IReadOnlyList<BlockHeader> headers, BlockTreeInsertHeaderOptions headerOptions = BlockTreeInsertHeaderOptions.None)
    {
        baseBlockTree.BulkInsertHeader(headers, headerOptions);
    }

    public AddBlockResult Insert(Block block, BlockTreeInsertBlockOptions insertBlockOptions = BlockTreeInsertBlockOptions.None,
        BlockTreeInsertHeaderOptions insertHeaderOptions = BlockTreeInsertHeaderOptions.None, WriteFlags bodiesWriteFlags = WriteFlags.None)
    {
        return baseBlockTree.Insert(block, insertBlockOptions, insertHeaderOptions, bodiesWriteFlags);
    }

    public void UpdateHeadBlock(Hash256 blockHash)
    {
        baseBlockTree.UpdateHeadBlock(blockHash);
    }

    public void NewOldestBlock(ulong oldestBlock)
    {
        baseBlockTree.NewOldestBlock(oldestBlock);
    }

    public AddBlockResult SuggestBlock(Block block, BlockTreeSuggestOptions options = BlockTreeSuggestOptions.ShouldProcess)
    {
        return baseBlockTree.SuggestBlock(block, options);
    }

    public ValueTask<AddBlockResult> SuggestBlockAsync(Block block, BlockTreeSuggestOptions options = BlockTreeSuggestOptions.ShouldProcess)
    {
        return baseBlockTree.SuggestBlockAsync(block, options);
    }

    public AddBlockResult SuggestHeader(BlockHeader header)
    {
        return baseBlockTree.SuggestHeader(header);
    }

    public bool IsKnownBlock(ulong number, Hash256 blockHash)
    {
        return baseBlockTree.IsKnownBlock(number, blockHash);
    }

    public bool IsKnownBeaconBlock(ulong number, Hash256 blockHash)
    {
        return baseBlockTree.IsKnownBeaconBlock(number, blockHash);
    }

    public bool WasProcessed(ulong number, Hash256 blockHash)
    {
        return baseBlockTree.WasProcessed(number, blockHash);
    }

    public bool TryUpdateMainChain(BlockHeader newHead, bool wereProcessed, bool forceUpdateHeadBlock = false, params ReadOnlySpan<Block> preloadedBlocks)
    {
        return baseBlockTree.TryUpdateMainChain(newHead, wereProcessed, forceUpdateHeadBlock, preloadedBlocks);
    }

    public void MarkChainAsProcessed(IReadOnlyList<Block> blocks)
    {
        baseBlockTree.MarkChainAsProcessed(blocks);
    }

    public Task Accept(IBlockTreeVisitor blockTreeVisitor, CancellationToken cancellationToken)
    {
        return baseBlockTree.Accept(blockTreeVisitor, cancellationToken);
    }

    public (BlockInfo? Info, ChainLevelInfo? Level) GetInfo(ulong number, Hash256 blockHash)
    {
        return baseBlockTree.GetInfo(number, blockHash);
    }

    public ChainLevelInfo? FindLevel(ulong number)
    {
        return baseBlockTree.FindLevel(number);
    }

    public BlockInfo FindCanonicalBlockInfo(ulong blockNumber)
    {
        return baseBlockTree.FindCanonicalBlockInfo(blockNumber);
    }

    public Hash256? FindHash(ulong blockNumber)
    {
        return baseBlockTree.FindHash(blockNumber);
    }

    public IOwnedReadOnlyList<BlockHeader> FindHeaders(Hash256 hash, int numberOfBlocks, int skip, bool reverse)
    {
        return baseBlockTree.FindHeaders(hash, numberOfBlocks, skip, reverse);
    }

    public void DeleteInvalidBlock(Block invalidBlock)
    {
        _logger.Warn("Invalid block deletion skipped");
    }

    public void ReportBadBlock(Block badBlock)
    {
        _logger.Warn($"Bad block reporting skipped: {badBlock.ToString(Block.Format.Short)}");
    }

    public void DeleteOldBlock(ulong blockNumber, Hash256 blockHash)
    {
        baseBlockTree.DeleteOldBlock(blockNumber, blockHash);
    }

    public void ForkChoiceUpdated(Hash256? finalizedBlockHash, Hash256? safeBlockBlockHash)
    {
        baseBlockTree.ForkChoiceUpdated(finalizedBlockHash, safeBlockBlockHash);
    }

    public ulong LastFinalizedBlockLevel => baseBlockTree.LastFinalizedBlockLevel;

    public int DeleteChainSlice(in ulong startNumber, ulong? endNumber = null, bool force = false)
    {
        return baseBlockTree.DeleteChainSlice(in startNumber, endNumber, force);
    }

    public bool IsBetterThanHead(BlockHeader? header)
    {
        return baseBlockTree.IsBetterThanHead(header);
    }

    public void UpdateBeaconMainChain(IReadOnlyList<BlockInfo>? blockInfos, ulong clearBeaconMainChainStartPoint)
    {
        baseBlockTree.UpdateBeaconMainChain(blockInfos, clearBeaconMainChainStartPoint);
    }

    public void RecalculateTreeLevels() =>
        baseBlockTree.RecalculateTreeLevels();

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