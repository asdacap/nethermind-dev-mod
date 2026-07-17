namespace NethermindDevPlugin;

public class DevPluginConfig : IDevPluginConfig
{
    public int SimulatedReorgDepth { get; set; }
    public SimulatedReorgMode SimulatedReorgMode { get; set; } = SimulatedReorgMode.FlipFlop;
}
