// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Config;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.State.Flat;
using Nethermind.State.Flat.Persistence;
using Nethermind.Trie;

namespace NethermindDevPlugin;

/// <summary>
/// A decorator for IPersistence that caches readers to reduce allocation overhead.
/// The cache is periodically cleared to allow database compaction.
/// </summary>
public class CachedReaderPersistence(
    IPersistence inner,
    IProcessExitSource processExitSource,
    ILogManager logManager) : IPersistence, IAsyncDisposable
{
    private readonly IPersistence _inner = inner;
    private readonly ILogger _logger = logManager.GetClassLogger();
    private readonly Lock _readerCacheLock = new();
    private readonly CancellationTokenSource _cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(processExitSource.Token);

    private RefCountingPersistenceReader? _cachedReader;
    private bool _mustNotClearReaderCache;
    private int _isDisposed;

    // Separate init from field declaration to allow for proper initialization
    public CachedReaderPersistence Init()
    {
        // Start the background cache clearing task
        Task.Run(async () =>
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));

            try
            {
                while (true)
                {
                    await timer.WaitForNextTickAsync(_cancelTokenSource.Token);
                    ClearReaderCache();
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        // Prime the reader cache
        using IPersistence.IPersistenceReader reader = CreateReader();

        return this;
    }

    public IPersistence.IPersistenceReader CreateReader()
    {
        RefCountingPersistenceReader? cachedReader = _cachedReader;
        if (cachedReader is not null && cachedReader.TryAcquire())
        {
            return cachedReader;
        }

        using Lock.Scope _ = _readerCacheLock.EnterScope();
        return CreateReaderNoLock();
    }

    private IPersistence.IPersistenceReader CreateReaderNoLock()
    {
        while (true)
        {
            RefCountingPersistenceReader? cachedReader = _cachedReader;
            if (cachedReader is null)
            {
                _cachedReader = cachedReader = new RefCountingPersistenceReader(
                    _inner.CreateReader(),
                    _logger
                );
            }

            if (cachedReader.TryAcquire())
            {
                return cachedReader;
            }

            // Was disposed but not cleared. Not yet at least.
            Interlocked.CompareExchange(ref _cachedReader, null, cachedReader);
        }
    }

    public IPersistence.IWriteBatch CreateWriteBatch(StateId from, StateId to, WriteFlags flags = WriteFlags.None)
    {
        // Prevent cache clear during write batch lifetime
        using (Lock.Scope _ = _readerCacheLock.EnterScope())
        {
            _mustNotClearReaderCache = true;
            using var reader = CreateReaderNoLock(); // Prime cache, then release lease
        }

        return new CacheClearPreventingWriteBatch(_inner.CreateWriteBatch(from, to, flags), this);
    }

    private void ClearReaderCache()
    {
        using Lock.Scope _ = _readerCacheLock.EnterScope();
        if (_mustNotClearReaderCache) return;
        RefCountingPersistenceReader? cachedReader = _cachedReader;
        _cachedReader = null;
        cachedReader?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 1) return;

        await _cancelTokenSource.CancelAsync();
        _cachedReader?.Dispose();
        _cancelTokenSource.Dispose();
    }

    private class CacheClearPreventingWriteBatch(IPersistence.IWriteBatch inner, CachedReaderPersistence parent)
        : IPersistence.IWriteBatch
    {
        public int SelfDestruct(Address addr) => inner.SelfDestruct(addr);
        public void SetAccount(Address addr, Account? account) => inner.SetAccount(addr, account);
        public void SetStorage(Address addr, in UInt256 slot, in SlotValue? value) => inner.SetStorage(addr, slot, value);
        public void SetStateTrieNode(in TreePath path, TrieNode tnValue) => inner.SetStateTrieNode(path, tnValue);
        public void SetStorageTrieNode(Hash256 address, in TreePath path, TrieNode tnValue) => inner.SetStorageTrieNode(address, path, tnValue);
        public void SetStorageRaw(Hash256 addrHash, Hash256 slotHash, in SlotValue? value) => inner.SetStorageRaw(addrHash, slotHash, value);
        public void SetAccountRaw(Hash256 addrHash, Account account) => inner.SetAccountRaw(addrHash, account);

        public void Dispose()
        {
            inner.Dispose();
            parent._mustNotClearReaderCache = false;
            parent.ClearReaderCache();
        }
    }
}
