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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// An optional capability a byte transport may offer alongside
    /// <see cref="IUaSCByteTransport"/>: many independently ordered and
    /// independently flow controlled streams over one connection, plus an
    /// unreliable datagram path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IUaSCByteTransport"/> is deliberately chunk at a time
    /// over one connection, which is exactly right for the UACP
    /// conversation and cannot express the stream per data channel
    /// mapping the QUIC binding needs. This interface sits beside it
    /// rather than changing it, so every existing transport is
    /// unaffected.
    /// </para>
    /// <para>
    /// Experimental. A transport that implements it reports
    /// <see cref="TransportChannelFeatures.MultiplexedStreams"/>.
    /// </para>
    /// </remarks>
    public interface IMultiplexedByteTransport
    {
        /// <summary>
        /// True when the peer advertised the datagram extension, so a
        /// frame can genuinely be lost in flight rather than discarded at
        /// the sender.
        /// </summary>
        bool SupportsDatagrams { get; }

        /// <summary>
        /// The largest datagram the peer will accept, or zero when the
        /// datagram extension is unavailable.
        /// </summary>
        int MaxDatagramSize { get; }

        /// <summary>
        /// Opens a stream this peer will write to.
        /// </summary>
        /// <param name="bidirectional">True for a bidirectional stream,
        /// false for a unidirectional one.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The transport level stream identifier, which travels
        /// in the OpenDataChannel request or response.</returns>
        ValueTask<ulong> OpenStreamAsync(bool bidirectional, CancellationToken ct);

        /// <summary>
        /// Accepts the next stream the peer opened.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The transport level stream identifier.</returns>
        ValueTask<ulong> AcceptStreamAsync(CancellationToken ct);

        /// <summary>
        /// Writes one complete frame to a stream.
        /// </summary>
        /// <param name="streamId">The stream identifier.</param>
        /// <param name="frame">The encoded frame, including its message
        /// header.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SendOnStreamAsync(
            ulong streamId,
            ReadOnlyMemory<byte> frame,
            CancellationToken ct);

        /// <summary>
        /// Sends one complete frame as a datagram, which may be lost.
        /// </summary>
        /// <param name="frame">The encoded frame. Fragmenting a frame
        /// across datagrams is not permitted, because one lost fragment
        /// would destroy a frame the receiver could otherwise have used
        /// in part.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SendDatagramAsync(ReadOnlyMemory<byte> frame, CancellationToken ct);

        /// <summary>
        /// Reads the next complete frame from a stream.
        /// </summary>
        /// <param name="streamId">The stream identifier.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A segment whose backing array is rented from the
        /// transport's buffer pool. The caller takes ownership.</returns>
        ValueTask<ArraySegment<byte>> ReceiveOnStreamAsync(
            ulong streamId,
            CancellationToken ct);

        /// <summary>
        /// Reads the next datagram.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A segment whose backing array is rented from the
        /// transport's buffer pool. The caller takes ownership.</returns>
        ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken ct);

        /// <summary>
        /// Aborts a stream, carrying an application error code that the
        /// data channel layer sets to the StatusCode of the RESET that
        /// caused it.
        /// </summary>
        /// <param name="streamId">The stream identifier.</param>
        /// <param name="errorCode">The application error code.</param>
        void AbortStream(ulong streamId, uint errorCode);

        /// <summary>
        /// Closes a stream in an orderly fashion.
        /// </summary>
        /// <param name="streamId">The stream identifier.</param>
        void CloseStream(ulong streamId);
    }
}
