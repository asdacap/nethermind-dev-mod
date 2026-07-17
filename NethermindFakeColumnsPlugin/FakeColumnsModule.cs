// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using Nethermind.Core;
using Nethermind.Db;
using Nethermind.Db.Rocks.Config;
using Nethermind.Logging;
using Nethermind.State.Pbt;

namespace Nethermind.FakeColumns;

public class FakeColumnsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        ConfigureFakeColumn<PbtColumns>(builder, DbNames.Pbt);
    }

    /// <summary>
    /// Splits the columns db of <typeparamref name="T"/> into one standalone db per column.
    /// </summary>
    /// <remarks>
    /// Decorates <see cref="IDbFactory"/> rather than re-registering <see cref="IColumnsDb{T}"/>: the backend
    /// that owns the columns db is itself a plugin (PbtPlugin), and plugin modules are ordered alphabetically
    /// by type name, so a last-wins registration here is not reliably last — PbtModule would overwrite it.
    /// The factory is resolved when the db is activated, so this applies whatever the plugin order.
    /// </remarks>
    private static void ConfigureFakeColumn<T>(ContainerBuilder builder, string namePrefix)
        where T : struct, Enum
    {
        builder.AddDecorator<IDbFactory>((ctx, inner) =>
            new FakeColumnsDbFactory<T>(inner, ctx.Resolve<ILogManager>()));

        builder.AddDecorator<IRocksDbConfigFactory>((_, configFactory) =>
            new ColumnRocksdbOptionsRedirector<T>(configFactory, namePrefix));
    }

    private sealed class FakeColumnsDbFactory<TTarget>(IDbFactory inner, ILogManager logManager) : IDbFactory
        where TTarget : struct, Enum
    {
        private readonly ILogger _logger = logManager.GetClassLogger<FakeColumnsDbFactory<TTarget>>();

        public IDb CreateDb(DbSettings dbSettings) => inner.CreateDb(dbSettings);

        public string GetFullDbPath(DbSettings dbSettings) => inner.GetFullDbPath(dbSettings);

        public IColumnsDb<T> CreateColumnsDb<T>(DbSettings dbSettings) where T : struct, Enum
        {
            if (typeof(T) != typeof(TTarget)) return inner.CreateColumnsDb<T>(dbSettings);

            Dictionary<T, IDb> columns = Enum.GetValues<T>()
                .ToDictionary(column => column, column => inner.CreateDb(ColumnSettings(dbSettings, column)));

            if (_logger.IsInfo) _logger.Info($"Fake columns: {dbSettings.DbName} split into {columns.Count} standalone dbs ({string.Join(", ", columns.Keys)}) instead of column families.");

            return new FakeColumnsDb<T>(columns);
        }

        /// <summary>Names each column db so it lands where <see cref="ColumnRocksdbOptionsRedirector{T}"/> expects it, e.g. Pbt + Account.</summary>
        private static DbSettings ColumnSettings<T>(DbSettings dbSettings, T column) where T : struct, Enum
        {
            DbSettings settings = dbSettings.Clone(dbSettings.DbName + column, dbSettings.DbPath + column);
            // A standalone db has one merge operator, not a per-column map.
            settings.MergeOperator = dbSettings.ColumnsMergeOperators?.GetValueOrDefault(column.ToString()!);
            settings.ColumnsMergeOperators = null;
            return settings;
        }
    }

    /// <summary>
    /// Makes each per-column database resolve the RocksDB config the column would have been given as a
    /// column family, so that splitting the database does not silently drop its per-column tuning.
    /// </summary>
    private class ColumnRocksdbOptionsRedirector<T>(IRocksDbConfigFactory baseFactory, string prefixName) : IRocksDbConfigFactory
        where T : struct, Enum
    {
        private readonly Dictionary<string, string> _fakeDbNames = Enum.GetValues<T>()
            .ToDictionary(column => GetTitleDbName(prefixName) + column, column => column.ToString()!);

        public IRocksDbConfig GetForDatabase(string databaseName, string? columnName) =>
            _fakeDbNames.TryGetValue(databaseName, out string? actualColumn)
                ? baseFactory.GetForDatabase(GetTitleDbName(prefixName), actualColumn)
                : baseFactory.GetForDatabase(databaseName, columnName);

        private static string GetTitleDbName(string dbName) => char.ToUpper(dbName[0]) + dbName[1..];
    }
}
