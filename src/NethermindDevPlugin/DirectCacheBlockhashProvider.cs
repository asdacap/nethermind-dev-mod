// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.IO;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Blocks;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Evm;
using Nethermind.Evm.State;
using Nethermind.Logging;

namespace NethermindDevPlugin;

/// <summary>
/// A safer BlockhashProvider that eliminates the temporary Hash256[] array
/// and uses IBlockhashCache directly for all lookups.
/// </summary>
public class DirectCacheBlockhashProvider(
    IBlockhashCache blockhashCache,
    IWorldState worldState,
    ILogManager? logManager)
    : IBlockhashProvider
{
    public const int MaxDepth = 256;
    private readonly IBlockhashStore _blockhashStore = new BlockhashStore(worldState);
    private readonly ILogger _logger = logManager?.GetClassLogger() ?? throw new ArgumentNullException(nameof(logManager));

    public Hash256? GetBlockhash(BlockHeader currentBlock, long number, IReleaseSpec spec)
    {
        if (number < 0)
        {
            return ReturnOutOfBounds(currentBlock, number);
        }

        // EIP-2935 path: blockhash stored in state
        if (spec.IsBlockHashInStateAvailable)
        {
            return _blockhashStore.GetBlockHashFromState(currentBlock, number, spec);
        }

        long depth = currentBlock.Number - number;

        return depth switch
        {
            <= 0 or > MaxDepth => ReturnOutOfBounds(currentBlock, number),
            1 => currentBlock.ParentHash,
            // Always use cache directly - no temporary array
            _ => blockhashCache.GetHash(currentBlock, (int)depth)
                 ?? throw new InvalidDataException("Hash cannot be found when executing BLOCKHASH operation")
        };
    }

    private Hash256? ReturnOutOfBounds(BlockHeader currentBlock, long number)
    {
        if (_logger.IsTrace) _logger.Trace($"BLOCKHASH opcode returning null for {currentBlock.Number} -> {number}");
        return null;
    }

    public Task Prefetch(BlockHeader currentBlock, CancellationToken token)
    {
        // Simply delegate to cache's prefetch to warm up its internal caches
        // We don't store the result - we always query the cache directly in GetBlockhash()
        return blockhashCache.Prefetch(currentBlock, token);
    }
}
