/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
