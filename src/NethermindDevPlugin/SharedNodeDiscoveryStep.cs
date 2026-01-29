using Nethermind.Api.Steps;
using Nethermind.Config;
using Nethermind.Init.Steps;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.Network.Rlpx;

namespace NethermindDevPlugin;

[RunnerStepDependencies(typeof(InitializeNetwork))]
public class SharedNodeDiscoveryStep(
    IRlpxHost rlpxHost,
    IIPResolver ipResolver,
    IStaticNodesManager staticNodesManager,
    IProcessExitSource processExitSource,
    ILogManager logManager) : IStep
{
    public Task Execute(CancellationToken cancellationToken)
    {
        var sharedNodesDir = Environment.GetEnvironmentVariable("SHARED_NODES_DIR");
        if (string.IsNullOrEmpty(sharedNodesDir))
            return Task.CompletedTask;

        var subnetCidr = Environment.GetEnvironmentVariable("SHARED_NODES_SUBNET");

        var discovery = new SharedNodeDiscovery(
            sharedNodesDir,
            subnetCidr,
            rlpxHost,
            ipResolver,
            staticNodesManager,
            processExitSource,
            logManager);

        discovery.Start();

        return Task.CompletedTask;
    }
}
