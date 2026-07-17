using Autofac;
using Autofac.Core;
using Nethermind.Api.Extensions;
using Nethermind.Api.Steps;
using Nethermind.Blockchain;
using Nethermind.Consensus.Processing;
using Nethermind.Evm;
using Nethermind.Core;
using Nethermind.Init.Modules;
using Nethermind.Config;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.State;

namespace NethermindDevPlugin;

public class DevPlugin(): INethermindPlugin
{
    public string Name => "Dev plugin";
    public string Description => "Some plugin code";
    public string Author => "Ashraf";
    public bool Enabled => Environment.GetEnvironmentVariable("SKIP_DEV_PLUGIN") != "1";
    public IModule Module => new DevPluginModule();
}

public class DevPluginModule() : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        Console.Error.WriteLine("Should decorate =======================================================");

        builder.AddDecorator<IBlockTree, ModdedBlockTree>();
        // Temporarily disabled.
        // builder.AddDecorator<IBlockTree, SimulatedReorgBlockTree>();
        builder.AddDecorator<IBlockProcessor, ExitOnAnyExceptionBlockProcessor>();
        builder.AddSingleton<IDevPluginConfig>(ctx =>
        {
            try { return ctx.Resolve<IConfigProvider>().GetConfig<IDevPluginConfig>(); }
            catch { return new DevPluginConfig(); } // ConfigProvider didn't discover our plugin config → default (depth 0 = disabled)
        });
        builder.AddStep(typeof(GitBisectExitOnInvalidBlock));
        builder.AddStep(typeof(RuntimeMetricsStep));
        // Temporarily disabled (paired with SimulatedReorgBlockTree decorator above).
        // builder.AddStep(typeof(SimulatedReorgStep));
        // Disabled — breaks forward sync.
        // builder.AddStep(typeof(DisabledReviewBlockTree));

        // Override IBlockhashProvider to eliminate temporary array race condition
        builder.AddScoped<IBlockhashProvider, DirectCacheBlockhashProvider>();
    }
}