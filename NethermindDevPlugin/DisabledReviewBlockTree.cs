using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Api.Steps;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Init.Steps;
using Nethermind.Logging;
using Nethermind.State;

namespace NethermindDevPlugin;

[RunnerStepDependencies(typeof(LoadGenesisBlock))]
public class DisabledReviewBlockTree(
    // Unused — present so EthereumStepsLoader prefers this step over base ReviewBlockTree.
    INethermindApi _,
    IWorldStateManager worldStateManager,
    IInitConfig initConfig,
    ISyncConfig syncConfig,
    IBlockProcessingQueue blockProcessingQueue,
    IBlockTree blockTree,
    IBlockTreeHealer blockTreeHealer,
    ILogManager logManager
) : ReviewBlockTree(worldStateManager, initConfig, syncConfig, blockProcessingQueue, blockTree, blockTreeHealer, logManager), IStep
{
    private readonly ILogger _logger = logManager.GetClassLogger<DisabledReviewBlockTree>();

    Task IStep.Execute(CancellationToken cancellationToken)
    {
        Block? head = blockTree.Head;
        if (head is null) return Task.CompletedTask;

        // BlockHeader.IsPostMerge isn't RLP-serialized, so a header loaded from disk
        // has it false. BlockTree.Suggest only assigns BestSuggestedBody when
        // IsPostMerge is true (without ShouldProcess), so flip it on for post-merge
        // chains — Difficulty == 0 is the standard post-merge marker.
        if (head.Header.Difficulty.IsZero)
        {
            head.Header.IsPostMerge = true;
        }

        AddBlockResult result = blockTree.SuggestBlock(head, BlockTreeSuggestOptions.None);
        if (_logger.IsInfo)
        {
            _logger.Info($"DisabledReviewBlockTree: SuggestBlock(head #{head.Number}) -> {result}. " +
                         $"BestSuggestedHeader={blockTree.BestSuggestedHeader?.Number.ToString() ?? "<null>"}, " +
                         $"BestSuggestedBody={blockTree.BestSuggestedBody?.Number.ToString() ?? "<null>"}");
        }
        return Task.CompletedTask;
    }
}
