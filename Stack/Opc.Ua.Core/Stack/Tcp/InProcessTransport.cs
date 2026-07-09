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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// In-process loopback implementation of <see cref="IUaSCByteTransport"/>.
    /// Two paired transports communicate over a pair of in-memory
    /// <see cref="Channel{T}"/>s (one per direction). Useful for unit tests,
    /// co-located server / client pairs that want to skip the network stack
    /// entirely, and as a copy-paste reference for custom transport
    /// implementations.
    /// </summary>
    /// <remarks>
    /// The implementation uses only the public OPC UA byte-transport surface
    /// and therefore doubles as a contract validation for the
    /// <see cref="IUaSCByteTransport"/> extension point documented in
    /// <c>Docs/Transports.md</c> § "Implementing a custom byte transport".
    /// </remarks>
    public sealed class InProcessTransport : IUaSCByteTransport, IDisposable
    {
        /// <summary>
        /// Creates a connected pair of transports that read each other's
        /// writes.
        /// </summary>
        /// <param name="buffers">Buffer manager used to allocate receive buffers.</param>
        /// <param name="receiveBufferSize">Maximum size of a single chunk on the receive path.</param>
        /// <param name="telemetry">Telemetry context for diagnostics / activity correlation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="buffers"/> is <c>null</c>.</exception>
        public static (InProcessTransport, InProcessTransport) CreatePair(
            BufferManager buffers,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            var aToB = Channel.CreateUnbounded<byte[]>();
            var bToA = Channel.CreateUnbounded<byte[]>();
            var a = new InProcessTransport(
                buffers, receiveBufferSize, telemetry, bToA.Reader, aToB.Writer);
            var b = new InProcessTransport(
                buffers, receiveBufferSize, telemetry, aToB.Reader, bToA.Writer);
            return (a, b);
        }

        private InProcessTransport(
            BufferManager buffers,
            int receiveBufferSize,
            ITelemetryContext telemetry,
            ChannelReader<byte[]> inbound,
            ChannelWriter<byte[]> outbound)
        {
            m_buffers = buffers;
            m_receiveBufferSize = receiveBufferSize;
            m_telemetry = telemetry;
            m_inbound = inbound;
            m_outbound = outbound;
        }

        /// <inheritdoc/>
        public string Implementation => "UA-INPROC";

        /// <inheritdoc/>
        public TransportChannelFeatures Features => TransportChannelFeatures.None;

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint => null;

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint => null;

        /// <inheritdoc/>
        public ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            throw new NotSupportedException(
                "InProcessTransport is loopback only; use CreatePair to wire two peers.");
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            ThrowIfClosed();
            byte[] copy = chunk.ToArray();
            if (!m_outbound.TryWrite(copy))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "Outbound channel closed.");
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            ThrowIfClosed();
            int total = buffers.TotalSize;
            byte[] copy = new byte[total];
            int offset = 0;
            foreach (ArraySegment<byte> segment in buffers)
            {
                if (segment.Array == null)
                {
                    continue;
                }
                Buffer.BlockCopy(segment.Array, segment.Offset, copy, offset, segment.Count);
                offset += segment.Count;
            }
            if (!m_outbound.TryWrite(copy))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "Outbound channel closed.");
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            try
            {
                byte[] payload = await m_inbound.ReadAsync(ct).ConfigureAwait(false);
                byte[] buffer = m_buffers.TakeBuffer(m_receiveBufferSize, nameof(ReceiveChunkAsync));
                if (payload.Length > buffer.Length)
                {
                    m_buffers.ReturnBuffer(buffer, nameof(ReceiveChunkAsync));
                    throw ServiceResultException.Create(
                        StatusCodes.BadTcpMessageTooLarge,
                        "In-process chunk exceeds receive buffer size.");
                }
                Buffer.BlockCopy(payload, 0, buffer, 0, payload.Length);
                return new ArraySegment<byte>(buffer, 0, payload.Length);
            }
            catch (ChannelClosedException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "In-process transport peer closed.");
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (Interlocked.Exchange(ref m_closed, 1) != 0)
            {
                return;
            }
            m_outbound.TryComplete();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
        }

        private void ThrowIfClosed()
        {
            if (Volatile.Read(ref m_closed) != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "Transport is closed.");
            }
        }

        private readonly BufferManager m_buffers;
        private readonly int m_receiveBufferSize;
#pragma warning disable IDE0052 // Kept to preserve the telemetry dependency for future in-process transport diagnostics.
        private readonly ITelemetryContext m_telemetry;
#pragma warning restore IDE0052
        private readonly ChannelReader<byte[]> m_inbound;
        private readonly ChannelWriter<byte[]> m_outbound;
        private int m_closed;
    }
}
