// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Carries intact, genuinely unreliable direct datagrams within one bound transport association.
    /// The context authenticates tokens, directions, revisions and replay sequences before delivery.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelDirectDatagramProvider
    {
        /// <summary>
        /// Gets the current path/session payload capacity including the 24-byte direct prefix,
        /// or zero when datagrams are unavailable. No stream fallback is permitted.
        /// </summary>
        int MaxDirectDatagramSize { get; }

        /// <summary>
        /// Borrows one complete datagram until transmission completes. Calls TryCommit immediately
        /// before irreversible admission; a pre-commit refusal must not transmit any bytes.
        /// </summary>
        ValueTask<DataChannelResult> SendDirectDatagramAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives one complete owned datagram. Only one receive may be pending.
        /// Unsolicited input and input exceeding bounded admission may be dropped without waiting
        /// for an application reader. Cancellation must release the pending receive.
        /// </summary>
        ValueTask<ByteString> ReceiveDirectDatagramAsync(CancellationToken cancellationToken = default);
    }
}
