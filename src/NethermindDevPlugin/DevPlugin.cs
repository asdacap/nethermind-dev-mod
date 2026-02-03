using Autofac;
using Autofac.Core;
using Nethermind.Api.Extensions;
using Nethermind.Api.Steps;
using Nethermind.Blockchain;
using Nethermind.Consensus.Processing;
using Nethermind.Evm;
using Nethermind.Core;
using Nethermind.Db;
using Nethermind.Db.Rocks.Config;
using Nethermind.Init.Modules;
using Nethermind.Config;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.State;
using Nethermind.State.Flat;
using Nethermind.State.Flat.Persistence;

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
        builder.AddDecorator<IBlockProcessor, ExitOnAnyExceptionBlockProcessor>();
        builder.AddStep(typeof(GitBisectExitOnInvalidBlock));

        // Override IBlockhashProvider to eliminate temporary array race condition
        builder.AddScoped<IBlockhashProvider, DirectCacheBlockhashProvider>();

        // FakeColumnsDb for per-column metrics
        Console.Error.WriteLine("=================================== CHEKING FAKE COLUMN ==============================================");
        if (Environment.GetEnvironmentVariable("USE_FAKE_FLAT_COLUMNS") == "1")
        {
            ConfigureFakeColumn<FlatDbColumns>(builder, "flat");

            // Also enable cached reader persistence with fake columns
            /*
            builder.AddDecorator<IPersistence>((ctx, persistence) =>
                new CachedReaderPersistence(persistence, ctx.Resolve<IProcessExitSource>(), ctx.Resolve<ILogManager>()).Init());
                */
        }
    }

    private void ConfigureFakeColumn<T>(ContainerBuilder builder, string namePrefix)
        where T : struct, Enum
    {
        Console.Error.WriteLine($"=================================== USING FAKE COLUMN for {typeof(T).Name} ==============================================");
        
        foreach (var k in Enum.GetValues<T>())
        {
            builder.AddDatabase(namePrefix + k.ToString());
        }
        
        builder.AddSingleton<IColumnsDb<T>>((ctx) =>
        {
            Dictionary<T, IDb> dbDict = new Dictionary<T, IDb>();
            
            foreach (var k in Enum.GetValues<T>())
            {
                dbDict[k] = ctx.ResolveKeyed<IDb>(namePrefix + k.ToString());
            }
            
            return new FakeColumnsDb<T>(dbDict);
        });

        builder.AddDecorator<IRocksDbConfigFactory>((ctx, configFactory) =>
        {
            return new ColumnRocksdbOptionsRedirector<T>(configFactory, namePrefix);
        });
    }
    
    private class ColumnRocksdbOptionsRedirector<T>(IRocksDbConfigFactory baseFactory, string prefixName): IRocksDbConfigFactory
        where T: struct, Enum
    {
        private Dictionary<string, string> fakeDbNames = Enum.GetValues<T>().ToDictionary((k) =>
        {
            return GetTitleDbName(prefixName) + k.ToString();
        }, (k) => k.ToString());
        
        public IRocksDbConfig GetForDatabase(string databaseName, string? columnName)
        {
            string mainDbName = GetTitleDbName(prefixName);
            Console.Error.WriteLine($"Got {databaseName}");
            if (fakeDbNames.TryGetValue(databaseName, out string? actualColumn))
            {
                Console.Error.WriteLine($"Replace config {databaseName} with {mainDbName} + {actualColumn}");
                return baseFactory.GetForDatabase(mainDbName, actualColumn);
            }
            
            return baseFactory.GetForDatabase(databaseName, columnName);
        }
        private static string GetTitleDbName(string dbName) => char.ToUpper(dbName[0]) + dbName[1..];
    }
}