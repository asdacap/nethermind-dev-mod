using Autofac;
using Autofac.Core;
using Nethermind.Api.Extensions;
using Nethermind.Api.Steps;

namespace NethermindClusterPlugin;

public class ClusterPlugin() : INethermindPlugin
{
    public string Name => "Cluster Plugin";
    public string Description => "Shared filesystem node discovery for cluster deployments";
    public string Author => "Ashraf";
    public bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHARED_NODES_DIR"));
    public IModule Module => new ClusterPluginModule();
}

public class ClusterPluginModule() : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        Console.Error.WriteLine("Cluster Plugin loading =======================================================");

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHARED_NODES_DIR")))
        {
            builder.AddStep(typeof(SharedNodeDiscoveryStep));
        }
    }
}
