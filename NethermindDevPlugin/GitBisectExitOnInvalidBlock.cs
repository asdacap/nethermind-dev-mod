using Nethermind.Api.Steps;
using Nethermind.Config;
using Nethermind.Consensus.Processing;
using Nethermind.Init.Steps;
using Nethermind.Logging;

namespace NethermindDevPlugin;

[RunnerStepDependencies(typeof(InitializeBlockchain))]
public class GitBisectExitOnInvalidBlock(
    IMainProcessingContext mainProcessingContext,
    IProcessExitSource processExitSource,
    ILogManager logManager
) : IStep
{
    public Task Execute(CancellationToken cancellationToken)
    {
      Console.Error.WriteLine("===================== setup invalid block ===============================");
        mainProcessingContext.BlockchainProcessor.InvalidBlock += (sender, args) =>
        {
            logManager.GetClassLogger<GitBisectExitOnInvalidBlock>().Info("Exiting on invalid block");
            processExitSource.Exit(10);
        };

        return Task.CompletedTask;
    }
}