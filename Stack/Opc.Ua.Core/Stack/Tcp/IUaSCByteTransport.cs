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
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Byte-level transport abstraction used by the OPC UA Secure Conversation
    /// (UASC) binary channel pipeline (see <see cref="UaSCUaBinaryChannel"/>).
    /// Each <c>Send</c> / <c>Receive</c> operates on a complete UASC
    /// <c>MessageChunk</c> as defined in OPC UA Part 6 §6.7.2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface is the runtime transport boundary for the binary
    /// SecureChannel pipeline. It is intentionally narrow: the abstraction does
    /// not own the UASC framing, signing or encryption — those concerns live
    /// inside <see cref="UaSCUaBinaryChannel"/> — it only moves opaque chunk
    /// bytes across a connection.
    /// </para>
    /// <para>
    /// Implementations exist for raw TCP sockets and for secure WebSocket
    /// connections; both feed the same channel implementation. The interface
    /// is internal so that it can evolve freely across minor releases; the
    /// public extensibility points remain <see cref="ITransportChannel"/> and
    /// <see cref="ITransportListener"/>.
    /// </para>
    /// <para>
    /// Lifetime: <see cref="Close"/> fully releases the underlying connection
    /// and any managed resources (locks, buffers); it is idempotent and safe
    /// to call from a synchronous <c>Dispose</c> on the owning channel.
    /// </para>
    /// </remarks>
    internal interface IUaSCByteTransport
    {
        /// <summary>
        /// The local endpoint of the underlying connection, when known.
        /// May return <c>null</c> before <see cref="ConnectAsync"/> completes
        /// or for transports that do not expose a network endpoint.
        /// </summary>
        EndPoint? LocalEndpoint { get; }

        /// <summary>
        /// The remote endpoint of the underlying connection, when known.
        /// May return <c>null</c> before <see cref="ConnectAsync"/> completes
        /// or for transports that do not expose a network endpoint.
        /// </summary>
        EndPoint? RemoteEndpoint { get; }

        /// <summary>
        /// The optional capabilities supported by this transport.
        /// </summary>
        TransportChannelFeatures Features { get; }

        /// <summary>
        /// A short, stable identifier describing the implementation, used for
        /// diagnostic strings produced by the UASC channel
        /// (for example <c>"UA-TCP"</c> or <c>"UA-WSS"</c>).
        /// </summary>
        string Implementation { get; }

        /// <summary>
        /// Connects this transport (client side) to <paramref name="url"/>.
        /// Server-side transports throw <see cref="NotSupportedException"/>.
        /// </summary>
        /// <param name="url">The remote endpoint URL.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask ConnectAsync(Uri url, CancellationToken ct);

        /// <summary>
        /// Sends one complete UASC <c>MessageChunk</c> as a contiguous buffer.
        /// </summary>
        /// <param name="chunk">The encoded chunk bytes (header + body).</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct);

        /// <summary>
        /// Sends one complete UASC <c>MessageChunk</c> gathered from multiple
        /// buffer segments (vectored / gather send). Concrete implementations
        /// may copy into a single buffer if the underlying transport does not
        /// support vectored writes.
        /// </summary>
        /// <param name="buffers">The buffer segments that make up the chunk,
        /// in order.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct);

        /// <summary>
        /// Reads the next complete UASC <c>MessageChunk</c> from the transport.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A segment whose underlying byte array is rented from the
        /// <see cref="BufferManager"/> supplied at transport construction. The
        /// caller takes ownership of the buffer and must return it via
        /// <see cref="BufferManager.ReturnBuffer"/> when processing is complete.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadConnectionClosed"/> when the
        /// peer closes the connection cleanly, or with another transport
        /// status code when a read error occurs.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown if <paramref name="ct"/> is cancelled before a chunk arrives.
        /// </exception>
        ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct);

        /// <summary>
        /// Forcefully closes the underlying connection and releases all
        /// managed resources held by the transport (e.g. socket / WebSocket
        /// handles, synchronization primitives). Idempotent and safe to call
        /// from a synchronous <c>Dispose</c> on the owning channel.
        /// </summary>
        void Close();
    }

    /// <summary>
    /// Factory used by client-side transport channels to create an
    /// unconnected <see cref="IUaSCByteTransport"/> instance configured with
    /// the buffer pool and receive sizing required by the UASC channel.
    /// </summary>
    /// <remarks>
    /// Server-side listeners construct transports directly from accepted
    /// connections (sockets, WebSockets) and therefore do not use this
    /// factory.
    /// </remarks>
    internal interface IUaSCByteTransportFactory
    {
        /// <summary>
        /// A short, stable identifier describing the implementation
        /// (matches <see cref="IUaSCByteTransport.Implementation"/>).
        /// </summary>
        string Implementation { get; }

        /// <summary>
        /// Creates an unconnected client transport.
        /// </summary>
        /// <param name="bufferManager">
        /// Buffer pool used by the transport's receive path to rent
        /// chunk-sized buffers.
        /// </param>
        /// <param name="receiveBufferSize">
        /// Maximum size, in bytes, of a single UASC message chunk this
        /// transport may receive. Buffers rented from
        /// <paramref name="bufferManager"/> are at least this large.
        /// </param>
        /// <param name="telemetry">
        /// Telemetry context used to build loggers / observability instruments.
        /// </param>
        IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry);
    }
}
