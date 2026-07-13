/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Crdt.Transport;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// An <see cref="ITransport"/> decorator that frames raw CRDT snapshots for the gossip transport and
    /// deframes received frames back to raw snapshots.
    /// </summary>
    /// <remarks>
    /// The <see cref="ITransport"/> contract requires <see cref="ITransport.SendAsync"/> to be given a complete
    /// length-prefixed frame (see <see cref="FrameCodec"/>): the TCP and UDP gossip transports
    /// <see cref="FrameCodec.Decode"/> the bytes on send and raise the whole encoded frame on
    /// <see cref="ITransport.FrameReceived"/>. The in-memory transport, by contrast, passes bytes through
    /// verbatim, so a store that sends and receives raw CRDT snapshots works in-process but throws
    /// <c>"Frame length does not match the encoded body length"</c> against a real TCP/UDP transport. This
    /// decorator lets the CRDT stores keep speaking raw snapshots while remaining wire-compatible with every
    /// transport: it wraps each outgoing snapshot as a <see cref="MessageType.State"/> frame and unwraps each
    /// incoming <see cref="MessageType.State"/> frame (transport control frames such as acknowledgements are
    /// ignored).
    /// </remarks>
    internal sealed class FramingGossipTransport : ITransport
    {
        /// <summary>
        /// Creates a framing decorator over an inner gossip transport.
        /// </summary>
        /// <param name="inner">The transport to wrap; owned by this decorator.</param>
        /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <c>null</c>.</exception>
        public FramingGossipTransport(ITransport inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_inner.FrameReceived += OnInnerFrameReceived;
        }

        /// <inheritdoc/>
        public event Action<ReadOnlyMemory<byte>>? FrameReceived;

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken ct = default)
        {
            return m_inner.StartAsync(ct);
        }

        /// <summary>
        /// Frames <paramref name="snapshot"/> as a <see cref="MessageType.State"/> frame and sends it through the
        /// inner transport.
        /// </summary>
        /// <param name="snapshot">The raw CRDT snapshot bytes.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task that completes when the frame has been queued or sent.</returns>
        public ValueTask SendAsync(ReadOnlyMemory<byte> snapshot, CancellationToken ct = default)
        {
            byte[] frame = FrameCodec.Encode(MessageType.State, snapshot.Span);
            return m_inner.SendAsync(frame, ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_inner.FrameReceived -= OnInnerFrameReceived;
            await m_inner.DisposeAsync().ConfigureAwait(false);
        }

        private void OnInnerFrameReceived(ReadOnlyMemory<byte> frame)
        {
            if (FrameCodec.TryDecode(frame, out DecodedFrame decoded) &&
                decoded.MessageType == MessageType.State)
            {
                FrameReceived?.Invoke(decoded.Payload);
            }
        }

        private readonly ITransport m_inner;
    }
}
