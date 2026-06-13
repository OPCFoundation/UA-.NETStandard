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

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// <see cref="IUaSCByteTransport"/> implementation that reads and
    /// writes UASC <c>MessageChunk</c>s through a Kestrel
    /// <see cref="ConnectionContext"/>'s duplex
    /// <see cref="IDuplexPipe"/>. Used by
    /// <see cref="KestrelTcpConnectionHandler"/> to bridge an accepted
    /// Kestrel connection into the existing
    /// <see cref="UaSCUaBinaryChannel"/> pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chunk framing matches the wire format produced by the
    /// existing raw-socket <c>TcpByteTransport</c>:
    /// <c>[messageType (4 bytes BE)][size (4 bytes LE)][body]</c>.
    /// Each Send / Receive operates on exactly one chunk.
    /// </para>
    /// <para>
    /// Server-side only - <see cref="ConnectAsync"/> throws
    /// <see cref="NotSupportedException"/> because the
    /// <see cref="ConnectionContext"/> is created by Kestrel on accept,
    /// not dialed outbound by the transport.
    /// </para>
    /// </remarks>
    internal sealed class PipeByteTransport : IUaSCByteTransport, IDisposable
    {
        public PipeByteTransport(
            ConnectionContext connection,
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            m_receiveBufferSize = receiveBufferSize;
            m_logger = telemetry.CreateLogger<PipeByteTransport>();
            m_sendLock = new SemaphoreSlim(1, 1);
        }

        public string Implementation => "UA-KESTREL-TCP";

        public TransportChannelFeatures Features => TransportChannelFeatures.Reconnect;

        public EndPoint? LocalEndpoint => m_connection.LocalEndPoint;

        public EndPoint? RemoteEndpoint => m_connection.RemoteEndPoint;

        public ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            throw new NotSupportedException(
                "PipeByteTransport is server-side only; built from an accepted Kestrel ConnectionContext.");
        }

        public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            ThrowIfClosed();
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                PipeWriter writer = m_connection.Transport.Output;
                Memory<byte> destination = writer.GetMemory(chunk.Length);
                chunk.CopyTo(destination);
                writer.Advance(chunk.Length);
                FlushResult result = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (result.IsCompleted || result.IsCanceled)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Outbound pipe completed/canceled while writing chunk.");
                }
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }
            ThrowIfClosed();
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                PipeWriter writer = m_connection.Transport.Output;
                foreach (ArraySegment<byte> segment in buffers)
                {
                    if (segment.Array == null || segment.Count == 0)
                    {
                        continue;
                    }
                    Memory<byte> destination = writer.GetMemory(segment.Count);
                    new ReadOnlyMemory<byte>(segment.Array, segment.Offset, segment.Count).CopyTo(destination);
                    writer.Advance(segment.Count);
                }
                FlushResult result = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (result.IsCompleted || result.IsCanceled)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Outbound pipe completed/canceled while writing buffer collection.");
                }
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            PipeReader reader = m_connection.Transport.Input;
            byte[]? rented = null;
            try
            {
                // Read until we have at least the 8-byte UASC header.
                while (true)
                {
                    ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (buffer.Length < 8)
                    {
                        if (result.IsCompleted)
                        {
                            reader.AdvanceTo(buffer.Start, buffer.End);
                            throw ServiceResultException.Create(
                                StatusCodes.BadConnectionClosed,
                                "Pipe completed before UASC message header.");
                        }
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        continue;
                    }

                    Span<byte> headerSpan = stackalloc byte[8];
                    buffer.Slice(0, 8).CopyTo(headerSpan);
                    int size = BinaryPrimitives.ReadInt32LittleEndian(headerSpan.Slice(4));
                    if (size < 8)
                    {
                        reader.AdvanceTo(buffer.End);
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTypeInvalid,
                            "Invalid UASC chunk size {0}.",
                            size);
                    }
                    if (size > m_receiveBufferSize)
                    {
                        reader.AdvanceTo(buffer.End);
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTooLarge,
                            "UASC chunk size {0} exceeds receive buffer size {1}.",
                            size,
                            m_receiveBufferSize);
                    }

                    if (buffer.Length < size)
                    {
                        if (result.IsCompleted)
                        {
                            reader.AdvanceTo(buffer.Start, buffer.End);
                            throw ServiceResultException.Create(
                                StatusCodes.BadConnectionClosed,
                                "Pipe completed mid-chunk (had {0} bytes, expected {1}).",
                                buffer.Length,
                                size);
                        }
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        continue;
                    }

                    rented = m_bufferManager.TakeBuffer(m_receiveBufferSize, nameof(ReceiveChunkAsync));
                    ReadOnlySequence<byte> chunkSeq = buffer.Slice(0, size);
                    chunkSeq.CopyTo(rented.AsSpan(0, size));
                    reader.AdvanceTo(buffer.GetPosition(size));
                    ArraySegment<byte> segment = new(rented, 0, size);
                    rented = null; // ownership transferred to the caller
                    return segment;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ServiceResultException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "PipeByteTransport: pipe read error.");
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    ex,
                    "Pipe read failed.");
            }
            finally
            {
                if (rented != null)
                {
                    m_bufferManager.ReturnBuffer(rented, nameof(ReceiveChunkAsync));
                }
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref m_closed, 1) != 0)
            {
                return;
            }
            try { m_connection.Transport.Input.Complete(); } catch { }
            try { m_connection.Transport.Output.Complete(); } catch { }
            try { m_connection.Abort(); } catch { }
            m_sendLock.Dispose();
        }

        public void Dispose() => Close();

        private void ThrowIfClosed()
        {
            if (Volatile.Read(ref m_closed) != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "Transport is closed.");
            }
        }

        private readonly ConnectionContext m_connection;
        private readonly BufferManager m_bufferManager;
        private readonly int m_receiveBufferSize;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_sendLock;
        private int m_closed;
    }
}
