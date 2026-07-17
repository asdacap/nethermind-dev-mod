// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.ComponentModel;
using Nethermind.Core.Attributes;
using Nethermind.Core.Metric;

namespace Nethermind.StateDiffArchive;

/// <summary>Prometheus metrics for the state-diff archive plugin.</summary>
public static class Metrics
{
    // Timing histograms are observed in microseconds. Exponential buckets span ~1us to ~7s.
    [DetailedMetric]
    [Description("Time to apply a whole block's recorded state diff during replay (microseconds)")]
    [ExponentialPowerHistogramMetric(Start = 1, Factor = 1.5, Count = 40)]
    public static IMetricObserver ReplayDiffApplyTime { get; set; } = new NoopMetricObserver();

    [DetailedMetric]
    [Description("Time to apply a write batch's storage slots during replay (microseconds)")]
    [ExponentialPowerHistogramMetric(Start = 1, Factor = 1.5, Count = 40)]
    public static IMetricObserver ReplayStorageApplyTime { get; set; } = new NoopMetricObserver();

    [DetailedMetric]
    [Description("Time to apply a write batch's account changes and flush the state tree during replay (microseconds)")]
    [ExponentialPowerHistogramMetric(Start = 1, Factor = 1.5, Count = 40)]
    public static IMetricObserver ReplayStateApplyTime { get; set; } = new NoopMetricObserver();

    [CounterMetric]
    [Description("Total blocks for which a state-diff record was written")]
    public static long BlocksRecorded { get; set; }

    [GaugeMetric]
    [Description("Highest block number recorded to the state-diff archive")]
    public static long LastRecordedBlock { get; set; }

    [CounterMetric]
    [Description("Total blocks replayed from the state-diff archive without the EVM")]
    public static long BlocksReplayed { get; set; }

    [GaugeMetric]
    [Description("Highest block number replayed from the state-diff archive")]
    public static long LastReplayedBlock { get; set; }
}
