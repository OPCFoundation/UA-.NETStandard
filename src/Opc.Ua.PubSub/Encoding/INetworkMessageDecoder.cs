// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Mapping-specific decoder for a single transport frame. Returns a
    /// <see cref="PubSubNetworkMessage"/> when the frame matches the
    /// profile and passes security validation, or <see langword="null"/>
    /// when the frame is unrecognised, deliberately filtered, or
    /// rejected by the security subsystem.
    /// </summary>
    /// <remarks>
    /// Implements the decoder side of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 UADP NetworkMessage mapping</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5 JSON NetworkMessage mapping</see>. The decoder
    /// signals soft-rejection (unknown publisher, unmatched writer
    /// group, security mismatch within the configured window) by
    /// returning <see langword="null"/>; only hard protocol corruption
    /// throws.
    /// </remarks>
    public interface INetworkMessageDecoder
    {
        /// <summary>
        /// Identifier of the transport profile this decoder targets.
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Attempts to decode a single transport frame.
        /// </summary>
        /// <param name="frame">
        /// The raw inbound frame bytes, exactly as received from the
        /// transport.
        /// </param>
        /// <param name="context">
        /// Per-message dependencies (stack message context, metadata
        /// registry, diagnostics, clock).
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The decoded message, or <see langword="null"/> when the
        /// frame is not recognised or fails a soft-validation step
        /// (e.g. unknown PublisherId, replay window rejection,
        /// unmatched DataSetWriterId). Hard protocol-level corruption
        /// throws.
        /// </returns>
        ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default);
    }
}
