// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Specs;
using Nethermind.Evm.State;
using Nethermind.StateDiffArchive.Storage;

namespace Nethermind.StateDiffArchive.Replay;

/// <summary>
/// Decorates the block cache prewarmer during replay: a block that will be replayed from the archive runs no
/// EVM, so there is nothing to prewarm — skip it. Blocks past the archive (which fall through to real
/// execution) are prewarmed normally by delegating to the inner prewarmer.
/// </summary>
public sealed class ReplayPrewarmGate(IBlockCachePreWarmer inner, StateDiffStore store) : IBlockCachePreWarmer
{
    public Task PreWarmCaches(Block suggestedBlock, BlockHeader? parent, IReleaseSpec spec, CancellationToken cancellationToken = default)
        => store.HasRecord(suggestedBlock.Number)
            ? Task.CompletedTask
            : inner.PreWarmCaches(suggestedBlock, parent, spec, cancellationToken);

    public CacheType ClearCaches() => inner.ClearCaches();

    // Replay applies recorded state diffs directly (no EVM), so BAL read-warming must NOT run: it warms the
    // shared flat-DB reads out from under the diff-apply and corrupts the recomputed state root
    // (deterministic NodeHashMismatch). Off for the whole replay processing env — archived blocks don't need
    // it, and blocks past the archive re-execute fine without it.
    public bool IsBalReadWarmingEnabled(IReleaseSpec spec) => false;

    // Same hazard as read-warming, but worse: a speculative session runs in the background and would race the
    // diff-apply of whichever block is replaying. Never speculate in the replay env.
    public Task StartSpeculativePreWarm(BlockHeader head, IReleaseSpec spec, long generation, Func<CancellationToken, Block?> nextDelta, int idlePassDelayMs, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public void Dispose() => inner.Dispose();
}
