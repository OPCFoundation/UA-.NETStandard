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
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Quic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Carries an OPC UA conversation over a QUIC connection: the UACP
    /// and Secure Conversation exchange on the first client initiated
    /// bidirectional stream, and one stream per data channel beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The control stream carries HEL, ACK, ERR, OPN, MSG and CLO byte
    /// for byte as they appear over opc.tcp, which is why this type also
    /// implements <see cref="IUaSCByteTransport"/>: the UASC pipeline
    /// above it is unchanged. Losing the control stream is treated as
    /// losing the SecureChannel.
    /// </para>
    /// <para>
    /// Data channel streams carry frames in QUIC framing - the message
    /// header followed directly by the stream header - because QUIC's TLS
    /// 1.3 record layer already authenticates and encrypts every byte and
    /// QUIC already orders and deduplicates each stream. The message
    /// header is retained so that one decoder serves both transports and
    /// so a frame is self delimiting without reassembly state.
    /// </para>
    /// </remarks>
    public sealed class QuicMultiplexedTransport :
        IUaSCByteTransport,
        IMultiplexedByteTransport,
        IUaSCSecureChannelBoundTransport,
        IAsyncDisposable
    {
        /// <summary>
        /// Wraps an established QUIC connection.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="controlStream">The stream carrying the UACP
        /// conversation.</param>
        /// <param name="bufferManager">The pool receive buffers are
        /// rented from.</param>
        /// <param name="receiveBufferSize">The largest chunk this
        /// transport accepts.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public QuicMultiplexedTransport(
            QuicConnection connection,
            QuicStream controlStream,
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
            m_controlStream = controlStream ?? throw new ArgumentNullException(nameof(controlStream));
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            m_receiveBufferSize = receiveBufferSize;
            m_logger = telemetry.CreateLogger<QuicMultiplexedTransport>();
            m_options = new QuicClientOptions();
            m_localInitiatorBit = QuicServerInitiatorBit;
        }

        /// <summary>
        /// Creates an unconnected client transport.
        /// <see cref="ConnectAsync"/> establishes the QUIC connection and
        /// opens the control stream.
        /// </summary>
        /// <param name="bufferManager">The pool receive buffers are
        /// rented from.</param>
        /// <param name="receiveBufferSize">The largest chunk this
        /// transport accepts.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="options">How the connection is established.</param>
        public QuicMultiplexedTransport(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry,
            QuicClientOptions? options = null)
        {
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            m_receiveBufferSize = receiveBufferSize;
            m_logger = telemetry.CreateLogger<QuicMultiplexedTransport>();
            m_options = options ?? new QuicClientOptions();
            m_localInitiatorBit = QuicClientInitiatorBit;
        }

        /// <summary>
        /// The stable identifier this implementation reports.
        /// </summary>
        public const string ImplementationName = "UA-QUIC";

        /// <inheritdoc/>
        public string Implementation => ImplementationName;

        /// <inheritdoc/>
        public TransportChannelFeatures Features
            => TransportChannelFeatures.Reconnect |
               TransportChannelFeatures.ReverseConnect |
               TransportChannelFeatures.MessageExtensions |
               TransportChannelFeatures.MultiplexedStreams;

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint => m_connection?.LocalEndPoint;

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint => m_connection?.RemoteEndPoint;

        /// <inheritdoc/>
        public bool SupportsDatagrams => MaxDatagramSize > 0;

        /// <summary>
        /// The pool used for chunks received from QUIC streams.
        /// </summary>
        internal BufferManager BufferManager => m_bufferManager;

        /// <summary>
        /// The largest complete QUIC-framed chunk this transport accepts.
        /// </summary>
        internal int ReceiveBufferSize => m_receiveBufferSize;

        /// <inheritdoc/>
        /// <remarks>
        /// Always zero. .NET's QUIC surface exposes no RFC 9221 datagram
        /// API, so the extension cannot be advertised or used from this
        /// runtime. A server therefore reports
        /// <c>SupportsUnreliableDatagrams</c> as false and refuses an
        /// Unreliable or PartiallyReliable request with
        /// Bad_DeliveryModeUnsupported, rather than silently carrying it
        /// on the channel's stream — which would deliver a reliability
        /// guarantee the application did not ask for and did not budget
        /// latency for.
        /// </remarks>
        public int MaxDatagramSize => 0;

        /// <summary>
        /// The certificate the peer presented in the TLS handshake, which
        /// the key equality check of the errata 7.6.1 binds to the
        /// OPC UA identity.
        /// </summary>
        public System.Security.Cryptography.X509Certificates.X509Certificate2? PeerCertificate
            => m_connection?.RemoteCertificate as
                System.Security.Cryptography.X509Certificates.X509Certificate2;

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (m_connection != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The transport is already connected.");
            }

            QuicConnection connection = await QuicConnectionBuilder
                .ConnectAsync(url, m_options, ct)
                .ConfigureAwait(false);

            try
            {
                // The first client-initiated bidirectional stream carries
                // the UACP and Secure Conversation conversation byte for
                // byte as it appears over opc.tcp (Part 6 errata 7.3).
                QuicStream control = await connection
                    .OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct)
                    .ConfigureAwait(false);

                m_connection = connection;
                m_controlStream = control;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                QuicStream control = RequireControlStream();
                await control.WriteAsync(chunk, ct).ConfigureAwait(false);
                await control.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }

            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                QuicStream control = RequireControlStream();

                for (int ii = 0; ii < buffers.Count; ii++)
                {
                    ArraySegment<byte> segment = buffers[ii];

                    await control
                        .WriteAsync(
                            new ReadOnlyMemory<byte>(segment.Array!, segment.Offset, segment.Count),
                            ct)
                        .ConfigureAwait(false);
                }

                await control.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            return ReadChunkAsync(RequireControlStream(), ct);
        }

        /// <inheritdoc/>
        public async ValueTask<ulong> OpenStreamAsync(bool bidirectional, CancellationToken ct)
        {
            QuicStream stream = await RequireConnection()
                .OpenOutboundStreamAsync(
                    bidirectional ? QuicStreamType.Bidirectional : QuicStreamType.Unidirectional,
                    ct)
                .ConfigureAwait(false);

            var id = (ulong)stream.Id;
            m_streams[id] = stream;
            return id;
        }

        /// <inheritdoc/>
        public async ValueTask<ulong> AcceptStreamAsync(CancellationToken ct)
        {
            QuicStream stream = await AcceptInboundStreamCoreAsync(ct).ConfigureAwait(false);
            return (ulong)stream.Id;
        }

        /// <summary>
        /// Accepts inbound streams until the stream id named by an
        /// OpenDataChannel exchange is available locally.
        /// </summary>
        /// <param name="streamId">The QUIC stream id carried in
        /// transportChannelId or revisedTransportChannelId.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask BindInboundStreamAsync(ulong streamId, CancellationToken ct)
        {
            if (m_streams.ContainsKey(streamId))
            {
                return;
            }

            while (true)
            {
                QuicStream? stream = await AcceptInboundStreamCoreAsync(
                    streamId,
                    ct).ConfigureAwait(false);

                if (stream == null)
                {
                    return;
                }

                if ((ulong)stream.Id == streamId)
                {
                    return;
                }

                if (m_streams.ContainsKey(streamId))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Validates the transportChannelId rules that can be checked from
        /// the QUIC binding before accepting an inbound data-channel stream.
        /// </summary>
        /// <param name="streamId">The stream id from transportChannelId.</param>
        /// <param name="bidirectional">True when the data channel requires a bidirectional stream.</param>
        internal void ValidateInboundDataChannelStream(ulong streamId, bool bidirectional)
        {
            QuicStream control = RequireControlStream();

            if (streamId == (ulong)control.Id)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelLimitsExceeded,
                    "The QUIC control stream cannot be bound to a data channel.");
            }

            if ((streamId & QuicInitiatorMask) == m_localInitiatorBit)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelLimitsExceeded,
                    "The QUIC stream was initiated by the wrong endpoint for this data-channel direction.");
            }

            bool streamIsBidirectional = (streamId & QuicDirectionMask) == 0;

            if (streamIsBidirectional != bidirectional)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelLimitsExceeded,
                    "The QUIC stream directionality does not match the data-channel direction.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendOnStreamAsync(
            ulong streamId,
            ReadOnlyMemory<byte> frame,
            CancellationToken ct)
        {
            QuicStream stream = RequireStream(streamId);

            await stream.WriteAsync(frame, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask SendDatagramAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            // Fragmenting a frame across datagrams is not permitted, so
            // the only correct behaviour without a datagram API is to
            // refuse rather than to fall back to the stream.
            throw ServiceResultException.Create(
                StatusCodes.BadDeliveryModeUnsupported,
                "This runtime exposes no QUIC datagram API, so genuine in-flight loss is unavailable.");
        }

        /// <inheritdoc/>
        public ValueTask<ArraySegment<byte>> ReceiveOnStreamAsync(
            ulong streamId,
            CancellationToken ct)
        {
            return ReadChunkAsync(RequireStream(streamId), ct);
        }

        /// <inheritdoc/>
        public ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken ct)
        {
            throw new NotSupportedException(
                "Datagram reception is surfaced through the connection's datagram callback.");
        }

        /// <inheritdoc/>
        public void AbortStream(ulong streamId, uint errorCode)
        {
            if (!m_streams.TryRemove(streamId, out QuicStream? stream))
            {
                return;
            }

            try
            {
                // RESET is realized as a QUIC RESET_STREAM whose
                // application error code carries the StatusCode.
                stream.Abort(QuicAbortDirection.Both, errorCode);
            }
            catch (QuicException)
            {
                // The stream is already gone.
            }
            finally
            {
                stream.Dispose();
            }
        }

        /// <inheritdoc/>
        public void CloseStream(ulong streamId)
        {
            if (!m_streams.TryRemove(streamId, out QuicStream? stream))
            {
                return;
            }

            try
            {
                stream.CompleteWrites();
            }
            catch (QuicException)
            {
                // The stream is already gone.
            }
            finally
            {
                stream.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            _ = DisposeAsync().AsTask();
        }

        /// <inheritdoc/>
        public void OnSecureChannelAttached(string secureChannelId)
        {
            if (string.IsNullOrEmpty(secureChannelId))
            {
                return;
            }

            m_secureChannelId = secureChannelId;
            QuicServerDataChannelTransport.BindSecureChannel(secureChannelId, this);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;

            if (m_secureChannelId != null)
            {
                QuicServerDataChannelTransport.UnbindSecureChannel(m_secureChannelId, this);
                m_secureChannelId = null;
            }

            foreach (QuicStream stream in m_streams.Values)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            m_streams.Clear();

            if (m_controlStream != null)
            {
                await m_controlStream.DisposeAsync().ConfigureAwait(false);
                m_controlStream = null;
            }

            if (m_connection != null)
            {
                await m_connection.DisposeAsync().ConfigureAwait(false);
                m_connection = null;
            }

            m_sendLock.Dispose();
            m_acceptLock.Dispose();
        }

        private QuicConnection RequireConnection()
        {
            return m_connection ?? throw ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "The QUIC transport is not connected.");
        }

        private QuicStream RequireControlStream()
        {
            return m_controlStream ?? throw ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "The QUIC control stream is not open.");
        }

        private QuicStream RequireStream(ulong streamId)
        {
            if (m_streams.TryGetValue(streamId, out QuicStream? stream))
            {
                return stream;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadDataChannelIdInvalid,
                "Stream {0} is not open on this QUIC connection.",
                streamId);
        }

        private async ValueTask<QuicStream> AcceptInboundStreamCoreAsync(CancellationToken ct)
        {
            return (await AcceptInboundStreamCoreAsync(null, ct).ConfigureAwait(false))!;
        }

        private async ValueTask<QuicStream?> AcceptInboundStreamCoreAsync(
            ulong? stopWhenStreamIdAvailable,
            CancellationToken ct)
        {
            await m_acceptLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                if (stopWhenStreamIdAvailable.HasValue &&
                    m_streams.ContainsKey(stopWhenStreamIdAvailable.Value))
                {
                    return null;
                }

                QuicStream stream = await RequireConnection()
                    .AcceptInboundStreamAsync(ct)
                    .ConfigureAwait(false);

                var id = (ulong)stream.Id;
                m_streams[id] = stream;
                return stream;
            }
            finally
            {
                m_acceptLock.Release();
            }
        }

        /// <summary>
        /// Reads one complete chunk. The message header declares the
        /// total length, so a chunk is self delimiting and needs no
        /// reassembly state.
        /// </summary>
        private async ValueTask<ArraySegment<byte>> ReadChunkAsync(
            QuicStream stream,
            CancellationToken ct)
        {
            byte[] buffer = m_bufferManager.TakeBuffer(
                m_receiveBufferSize,
                nameof(ReadChunkAsync),
                ct);

            bool success = false;

            try
            {
                await ReadExactAsync(
                    stream,
                    buffer.AsMemory(0, TcpMessageLimits.MessageTypeAndSize),
                    ct).ConfigureAwait(false);

                uint messageSize = BinaryPrimitives.ReadUInt32LittleEndian(
                    buffer.AsSpan(4, 4));

                if (messageSize < TcpMessageLimits.MessageTypeAndSize ||
                    messageSize > (uint)m_receiveBufferSize)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTcpMessageTooLarge,
                        "The chunk declares a size of {0} bytes, outside 8..{1}.",
                        messageSize,
                        m_receiveBufferSize);
                }

                int remaining = (int)messageSize - TcpMessageLimits.MessageTypeAndSize;

                if (remaining > 0)
                {
                    await ReadExactAsync(
                        stream,
                        buffer.AsMemory(TcpMessageLimits.MessageTypeAndSize, remaining),
                        ct).ConfigureAwait(false);
                }

                success = true;
                return new ArraySegment<byte>(buffer, 0, (int)messageSize);
            }
            finally
            {
                if (!success)
                {
                    m_bufferManager.ReturnBuffer(buffer, nameof(ReadChunkAsync));
                }
            }
        }

        private static async ValueTask ReadExactAsync(
            QuicStream stream,
            Memory<byte> destination,
            CancellationToken ct)
        {
            int read = 0;

            while (read < destination.Length)
            {
                int count = await stream
                    .ReadAsync(destination.Slice(read), ct)
                    .ConfigureAwait(false);

                if (count == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "The peer closed the QUIC stream.");
                }

                read += count;
            }
        }

        private readonly ConcurrentDictionary<ulong, QuicStream> m_streams = new();
        private readonly SemaphoreSlim m_sendLock = new(1, 1);
        private readonly SemaphoreSlim m_acceptLock = new(1, 1);
        private QuicConnection? m_connection;
        private QuicStream? m_controlStream;
        private readonly QuicClientOptions m_options;
        private readonly BufferManager m_bufferManager;
        private readonly int m_receiveBufferSize;
        private readonly ILogger m_logger;
        private readonly ulong m_localInitiatorBit;
        private string? m_secureChannelId;
        private bool m_disposed;

        private const ulong QuicInitiatorMask = 0x01;
        private const ulong QuicDirectionMask = 0x02;
        private const ulong QuicClientInitiatorBit = 0x00;
        private const ulong QuicServerInitiatorBit = 0x01;
    }
}
