using Nethermind.Config;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Evm.Tracing;

namespace NethermindDevPlugin;

public class ExitOnAnyExceptionBlockProcessor(IBlockProcessor baseBlockProcessor, IProcessExitSource exitSource) : IBlockProcessor
{
    public (Block Block, TxReceipt[] Receipts) ProcessOne(Block suggestedBlock, ProcessingOptions options,
        IBlockTracer blockTracer, IReleaseSpec spec, CancellationToken token = new CancellationToken())
    {
        try
        {
            return baseBlockProcessor.ProcessOne(suggestedBlock, options, blockTracer, spec, token);
        }
        catch (Exception)
        {
            exitSource.Exit(10);
            throw;
        }
    }
}