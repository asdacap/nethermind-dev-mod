// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;
using Nethermind.Logging;

// Note: AllocationTick events on .NET 10 are dispatched on a separate
// EventPipeEventDispatcher thread, not the allocating one — so capturing
// StackTrace here only shows the dispatch plumbing. For real allocation
// stacks, use external `dotnet-trace` with the GC keyword.

namespace NethermindDevPlugin;

/// <summary>
/// Subscribes to <c>Microsoft-Windows-DotNETRuntime</c> GC events and logs every
/// large-object-heap allocation (<c>GCAllocationTick_V4</c> with <c>AllocationKind == Large</c>).
/// One log line per ~100 KiB tick; for objects ≥ 85 KiB this is effectively one line per LOH alloc.
/// Type name is reported but no managed stack — for that, use <c>dotnet-trace</c> with
/// <c>GCSampledObjectAllocationHigh</c> (keyword 0x80000) for short windows.
/// </summary>
public sealed class LohAllocListener(ILogger logger) : EventListener
{
    private const string RuntimeSourceName = "Microsoft-Windows-DotNETRuntime";
    private const EventKeywords GcKeyword = (EventKeywords)0x1;
    private const int GCAllocationTickEventId = 10;

    // Polled by the metrics tick. Lets us confirm whether OnEventSourceCreated /
    // OnEventWritten fire at all, separate from any logger/file-write side path.
    public static long SourcesSeen;
    public static long RuntimeSourceAttached;
    public static long EventsReceived;
    public static long AllocTicksReceived;
    public static long LargeAllocsLogged;

    // Last log timestamp (Stopwatch ticks) per type name — used to throttle to one line/sec/type.
    private static readonly ConcurrentDictionary<string, long> LastLogTicksByType = new();
    private static readonly long LogIntervalTicks = Stopwatch.Frequency; // 1 second

    private readonly ILogger _logger = logger;
    private EventSource _runtimeSource;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        Interlocked.Increment(ref SourcesSeen);
        if (eventSource.Name == RuntimeSourceName)
        {
            _runtimeSource = eventSource;
            EnableEvents(eventSource, EventLevel.Verbose, GcKeyword);
            Interlocked.Increment(ref RuntimeSourceAttached);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        Interlocked.Increment(ref EventsReceived);
        if (eventData.EventId == GCAllocationTickEventId) Interlocked.Increment(ref AllocTicksReceived);
        if (eventData.EventId != GCAllocationTickEventId) return;
        if (eventData.Payload is null) return;

        // GCAllocationTick_V4 payload: AllocationAmount, AllocationKind, ClrInstanceID,
        // AllocationAmount64, TypeID, TypeName, HeapIndex, Address, ObjectSize.
        // AllocationKind: 0=Small, 1=Large, 2=Pinned.
        int kind = -1;
        long amount = 0;
        long objectSize = 0;
        string typeName = null;
        int heapIndex = -1;

        for (int i = 0; i < eventData.PayloadNames!.Count; i++)
        {
            switch (eventData.PayloadNames[i])
            {
                case "AllocationKind":
                    kind = Convert.ToInt32(eventData.Payload[i]);
                    break;
                case "AllocationAmount64":
                    amount = Convert.ToInt64(eventData.Payload[i]);
                    break;
                case "ObjectSize":
                    objectSize = Convert.ToInt64(eventData.Payload[i]);
                    break;
                case "TypeName":
                    typeName = eventData.Payload[i] as string;
                    break;
                case "HeapIndex":
                    heapIndex = Convert.ToInt32(eventData.Payload[i]);
                    break;
            }
        }

        if (kind != 1) return; // Only Large

        Interlocked.Increment(ref LargeAllocsLogged);

        // Throttle: at most one log line per type per second.
        string key = typeName ?? "<null>";
        long nowTicks = Stopwatch.GetTimestamp();
        long prev = LastLogTicksByType.GetOrAdd(key, 0L);
        if (nowTicks - prev < LogIntervalTicks) return;
        if (!LastLogTicksByType.TryUpdate(key, nowTicks, prev)) return; // lost race

        if (_logger.IsInfo)
            _logger.Info($"LOH alloc: type={typeName} size={objectSize} sampledTickAmount={amount} heap={heapIndex}");
    }

    public override void Dispose()
    {
        if (_runtimeSource is not null)
        {
            try { DisableEvents(_runtimeSource); } catch { /* ignore */ }
            _runtimeSource = null;
        }
        base.Dispose();
    }
}
