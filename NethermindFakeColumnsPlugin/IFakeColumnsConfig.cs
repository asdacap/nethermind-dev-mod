// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Config;

namespace Nethermind.FakeColumns;

/// <summary>
/// Configuration for the fake-columns plugin: replaces the PBT column-family database with one
/// standalone database per column, so RocksDB reports its metrics per column instead of aggregated.
/// </summary>
[ConfigCategory(Description = "Splits the PBT columns database into one standalone database per column so RocksDB metrics are reported per column. Development use only — changes the on-disk layout.")]
public interface IFakeColumnsConfig : IConfig
{
    [ConfigItem(Description = "Whether to back IColumnsDb<PbtColumns> with one standalone database per column instead of RocksDB column families. Changes the on-disk layout: an existing Pbt database cannot be read with this enabled.", DefaultValue = "false")]
    bool Enabled { get; set; }
}
