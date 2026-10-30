// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Completes a direct sender whose ordinary disposal may discard transport-owned writes.
    /// </summary>
    /// <remarks>
    /// The returned stream implements this interface when teardown requires an explicit retention barrier.
    /// The runtime supplies the encoded END as the final write before orderly disposal, never for an abort.
    /// Success guarantees retention of all preceding writes through disposal, not peer application consumption.
    /// Streams whose normal asynchronous disposal already preserves accepted writes do not need this interface.
    /// </remarks>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelDirectStreamCompletion
    {
        /// <summary>
        /// Writes the final bytes and prevents orderly disposal from abandoning them or preceding writes.
        /// No further writes are permitted after successful completion.
        /// </summary>
        ValueTask CompleteWritesAsync(
            ByteString finalBytes, CancellationToken cancellationToken = default);
    }
}
