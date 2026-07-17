// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Autofac.Core;
using Nethermind.Api.Extensions;

namespace Nethermind.FakeColumns;

/// <summary>
/// Replaces the PBT column-family database with one standalone database per column, so that RocksDB
/// attributes its metrics to a single column rather than aggregating them across the column families.
/// </summary>
/// <remarks>
/// Development use only: the per-column databases are a different on-disk layout to column families,
/// so a database written with this plugin enabled cannot be read with it disabled, and vice versa.
/// </remarks>
public class FakeColumnsPlugin(IFakeColumnsConfig config) : INethermindPlugin
{
    public string Name => "FakeColumns";
    public string Description => "Backs the PBT columns database with one standalone database per column, for per-column metrics";
    public string Author => "Ashraf";

    public bool Enabled => config.Enabled;

    public IModule Module => new FakeColumnsModule();
}
