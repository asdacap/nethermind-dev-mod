using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Nethermind.Config;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.Network.Rlpx;

namespace NethermindDevPlugin;

public class SharedNodeDiscovery(
    string sharedNodesDir,
    string? subnetCidr,
    IRlpxHost rlpxHost,
    IIPResolver ipResolver,
    IStaticNodesManager staticNodesManager,
    IProcessExitSource processExitSource,
    ILogManager logManager)
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleTtl = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = logManager.GetClassLogger<SharedNodeDiscovery>();
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _addedEnodes = new();

    private string? _ownFilePath;
    private string? _ownEnode;

    public record NodeFileContent(string Enode, long LastSeen);
}
