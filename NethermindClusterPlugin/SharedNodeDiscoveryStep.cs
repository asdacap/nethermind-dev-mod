using Nethermind.Api.Steps;
using Nethermind.Config;
using Nethermind.Init.Steps;
using Nethermind.Logging;
using Nethermind.Network;

namespace NethermindClusterPlugin;

[RunnerStepDependencies(typeof(InitializeNetwork))]
public class SharedNodeDiscoveryStep(
    IEnode enode,
    IIPResolver ipResolver,
    IStaticNodesManager staticNodesManager,
    ITrustedNodesManager trustedNodesManager,
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
            enode,
            ipResolver,
            staticNodesManager,
            trustedNodesManager,
            processExitSource,
            logManager);

        discovery.Start();

        return Task.CompletedTask;
    }
}
