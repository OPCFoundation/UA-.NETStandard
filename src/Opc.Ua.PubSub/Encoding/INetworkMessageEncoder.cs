// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Mapping-specific encoder for a complete
    /// <see cref="PubSubNetworkMessage"/>. Implementations cover one
    /// transport profile (UADP, JSON) and turn a fully-prepared
    /// message tree into the on-wire byte sequence expected by that
    /// profile.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 UADP NetworkMessage mapping</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5 JSON NetworkMessage mapping</see> as the
    /// pluggable encoder seam. Implementations are looked up by
    /// <see cref="TransportProfileUri"/>.
    /// </remarks>
    public interface INetworkMessageEncoder
    {
        /// <summary>
        /// Identifier of the transport profile this encoder targets
        /// (e.g. <c>http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp</c>).
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Number of bytes the encoder reserves for the mapping's
        /// fixed header / signature region. Used by the chunker to
        /// compute per-fragment payload budgets without an extra
        /// encode pass.
        /// </summary>
        int EstimatedHeaderOverhead { get; }

        /// <summary>
        /// Encodes <paramref name="networkMessage"/> into a single
        /// contiguous frame.
        /// </summary>
        /// <param name="networkMessage">
        /// Fully-prepared message tree to serialise.
        /// </param>
        /// <param name="context">
        /// Per-message dependencies (stack message context, metadata
        /// registry, diagnostics, clock).
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A <see cref="ReadOnlyMemory{Byte}"/> over the encoded
        /// frame. The memory may be backed by a pooled buffer; the
        /// transport is expected to dispatch and release synchronously
        /// within the returned task scope.
        /// </returns>
        ValueTask<ReadOnlyMemory<byte>> EncodeAsync(
            PubSubNetworkMessage networkMessage,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default);
    }
}
