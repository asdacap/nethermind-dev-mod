// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Api.Steps;
using Nethermind.Init.Steps;
using Nethermind.Logging;
using Nethermind.Monitoring;
using Prometheus;
using Prometheus.DotNetRuntime;

namespace NethermindDevPlugin;

/// <summary>
/// Wires up:
/// - prometheus-net.DotNetRuntime collector (.NET runtime metrics via EventListener — gen
///   counts, gc-pause, threadpool, jit, exceptions; only fires when EventSource.IsSupported
///   is enabled at the runtime level).
/// - Polled GC gauges (`nethermind_gc_*`) via <see cref="GC.GetGCMemoryInfo"/> /
///   <see cref="GC.GetTotalAllocatedBytes"/> / <see cref="GC.GetTotalPauseDuration"/>;
///   these always work regardless of EventSource availability.
/// - <see cref="LohAllocListener"/> for per-LOH-alloc logging (one line per type per
///   second), plus its `nethermind_loh_listener_stats` instrumentation gauge.
///
/// Lives in the dev plugin so the production Nethermind.Monitoring assembly stays clean.
/// </summary>
[RunnerStepDependencies(typeof(InitializeNetwork))]
public class RuntimeMetricsStep(IMonitoringService monitoringService, ILogManager logManager) : IStep
{
    private static IDisposable? _runtimeStatsCollector;
    private static LohAllocListener? _lohListener;

    public Task Execute(CancellationToken cancellationToken)
    {
        ILogger logger = logManager.GetClassLogger<RuntimeMetricsStep>();

        // .NET runtime EventListener-driven metrics. Stuck at zero on .NET 10 in some
        // builds; the polled gauges below are the always-on fallback.
        _runtimeStatsCollector ??= DotNetRuntimeStatsBuilder.Default().StartCollecting();

        // Per-LOH-alloc logger (throttled per type per second).
        _lohListener ??= new LohAllocListener(logger);

        Gauge gcHeapSize = Prometheus.Metrics.CreateGauge(
            "nethermind_gc_heap_size_bytes",
            "Managed heap size in bytes by generation (gen0/gen1/gen2/loh/poh), polled from GC.GetGCMemoryInfo",
            "generation");
        Gauge gcHeapFragmentation = Prometheus.Metrics.CreateGauge(
            "nethermind_gc_heap_fragmentation_bytes",
            "Managed heap fragmentation in bytes by generation, polled from GC.GetGCMemoryInfo",
            "generation");
        Gauge gcAllocatedTotal = Prometheus.Metrics.CreateGauge(
            "nethermind_gc_allocated_bytes_total",
            "Total bytes allocated on the managed heap since process start, from GC.GetTotalAllocatedBytes");
        Gauge gcCommitted = Prometheus.Metrics.CreateGauge(
            "nethermind_gc_committed_bytes",
            "Total bytes committed by the GC, polled from GC.GetGCMemoryInfo");
        Gauge gcPauseTotal = Prometheus.Metrics.CreateGauge(
            "nethermind_gc_pause_seconds_total",
            "Total time spent paused in GC since process start, from GC.GetTotalPauseDuration");

        Gauge lohListenerStats = Prometheus.Metrics.CreateGauge(
            "nethermind_loh_listener_stats",
            "LohAllocListener instrumentation: did the in-process EventListener attach and receive events?",
            "kind");

        monitoringService.AddMetricsUpdateAction(() =>
        {
            lohListenerStats.WithLabels("sources_seen").Set(LohAllocListener.SourcesSeen);
            lohListenerStats.WithLabels("runtime_attached").Set(LohAllocListener.RuntimeSourceAttached);
            lohListenerStats.WithLabels("events_received").Set(LohAllocListener.EventsReceived);
            lohListenerStats.WithLabels("alloc_ticks_received").Set(LohAllocListener.AllocTicksReceived);
            lohListenerStats.WithLabels("large_allocs_logged").Set(LohAllocListener.LargeAllocsLogged);
        });

        monitoringService.AddMetricsUpdateAction(() =>
        {
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            ReadOnlySpan<GCGenerationInfo> gens = info.GenerationInfo;
            string[] names = ["gen0", "gen1", "gen2", "loh", "poh"];
            for (int i = 0; i < gens.Length && i < names.Length; i++)
            {
                gcHeapSize.WithLabels(names[i]).Set(gens[i].SizeAfterBytes);
                gcHeapFragmentation.WithLabels(names[i]).Set(gens[i].FragmentationAfterBytes);
            }
            gcAllocatedTotal.Set(GC.GetTotalAllocatedBytes());
            gcCommitted.Set(info.TotalCommittedBytes);
            gcPauseTotal.Set(GC.GetTotalPauseDuration().TotalSeconds);
        });

        if (logger.IsInfo) logger.Info("Runtime metrics + LohAllocListener wired up");
        return Task.CompletedTask;
    }
}
