using Nethermind.Config;

namespace NethermindDevPlugin;

public enum SimulatedReorgMode
{
    /// One sibling per canonical block; head bounces canonical↔sibling each block.
    FlipFlop,
    /// Every N canonical blocks, build a chain of N+1 siblings starting at depth N.
    Batch,
}

public interface IDevPluginConfig : IConfig
{
    [ConfigItem(Description = "Length of each simulated reorg fork. Every N canonical blocks the dev plugin produces a fresh sibling chain that diverges by dropping the last tx of the fork's first canonical block. 0 disables.", DefaultValue = "0")]
    int SimulatedReorgDepth { get; set; }

    [ConfigItem(Description = "Reorg pattern. FlipFlop: build a sibling per canonical block (head bounces). Batch: every N canonical blocks, build a chain of N+1 siblings starting at depth N.", DefaultValue = "FlipFlop")]
    SimulatedReorgMode SimulatedReorgMode { get; set; }
}
