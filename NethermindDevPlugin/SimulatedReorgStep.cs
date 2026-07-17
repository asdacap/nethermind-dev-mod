using Nethermind.Api.Steps;
using Nethermind.Blockchain;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Producers;
using Nethermind.Init.Steps;
using Nethermind.Logging;

namespace NethermindDevPlugin;

[RunnerStepDependencies(typeof(InitializeBlockchain), typeof(ReviewBlockTree))]
public class SimulatedReorgStep(
    IBlockTree blockTree,
    IMainProcessingContext mainProcessingContext,
    IBlockProducerEnvFactory blockProducerEnvFactory,
    IDevPluginConfig config,
    ILogManager logManager) : IStep
{
    public Task Execute(CancellationToken cancellationToken)
    {
        if (config.SimulatedReorgDepth <= 0) return Task.CompletedTask;

        if (blockTree is SimulatedReorgBlockTree decorator)
        {
            decorator.Initialize(mainProcessingContext.BranchProcessor, blockProducerEnvFactory, mainProcessingContext.BlockProcessingQueue);
        }
        else
        {
            ILogger logger = logManager.GetClassLogger<SimulatedReorgStep>();
            if (logger.IsWarn) logger.Warn($"SimulatedReorgStep: IBlockTree is {blockTree.GetType().Name}, not SimulatedReorgBlockTree — interception inactive");
        }

        return Task.CompletedTask;
    }
}
