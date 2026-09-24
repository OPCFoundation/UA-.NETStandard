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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Carries data channel frames over per-channel QUIC streams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A frame on a data channel stream is in <b>QUIC framing</b>: the
    /// message header followed directly by the stream header, payload and
    /// nothing else. The symmetric security header, the sequence header
    /// and the message footer are omitted, because QUIC's TLS 1.3 record
    /// layer already authenticates and encrypts every byte and QUIC
    /// already orders and deduplicates each stream. The message header is
    /// retained so that one decoder serves both transports and a frame
    /// stays self delimiting without reassembly state.
    /// </para>
    /// <para>
    /// QUIC applies its own per-stream and per-connection flow control,
    /// so <see cref="HasTransportFlowControl"/> is true: no CREDIT frame
    /// is sent or expected, and a receiver ignores one. Duplicating the
    /// window in two layers gains nothing and deadlocks when the two
    /// disagree.
    /// </para>
    /// </remarks>
    public sealed class QuicDataChannelTransport : IDataChannelTransport, IAsyncDisposable
    {
        /// <summary>
        /// Creates a data channel transport over a QUIC connection.
        /// </summary>
        /// <param name="transport">The multiplexed connection whose
        /// streams carry the channels.</param>
        /// <param name="bufferManager">The pool frame buffers are rented
        /// from.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">The clock, or null for the system
        /// clock.</param>
        public QuicDataChannelTransport(
            QuicMultiplexedTransport transport,
            BufferManager bufferManager,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            BufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            TimeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<QuicDataChannelTransport>();
            m_stop = new CancellationTokenSource();
        }

        /// <summary>
        /// The engine inbound frames are dispatched to. Set once the
        /// manager has been constructed around this transport.
        /// </summary>
        public DataChannelManager? Manager
        {
            get => m_manager;
            set
            {
                if (ReferenceEquals(m_manager, value))
                {
                    return;
                }

                if (m_manager != null)
                {
                    m_manager.ChannelStateChanged -= OnChannelStateChanged;
                }

                m_manager = value;

                if (m_manager != null)
                {
                    // §7.4: closing a data channel closes its QUIC stream, and
                    // a RESET is realized as a RESET_STREAM carrying the
                    // StatusCode. Without this the stream is never released
                    // and counts against MaxInboundStreams for the life of the
                    // connection, so a peer that opens and closes channels
                    // eventually cannot open any.
                    m_manager.ChannelStateChanged += OnChannelStateChanged;
                }
            }
        }

        /// <summary>
        /// The identifier of the SecureChannel whose frames this transport
        /// carries. Part 6 errata §5.1 requires the Message header's
        /// SecureChannelId to be that of the enclosing SecureChannel under
        /// both framings, and the published QUIC wire vector carries a
        /// non-zero value, so one decoder serves both transports. It is
        /// assigned once OpenSecureChannel completes on the control stream.
        /// </summary>
        public uint SecureChannelId { get; set; }

        /// <inheritdoc/>
        public DataChannelFramingMode FramingMode => DataChannelFramingMode.Quic;

        /// <inheritdoc/>
        public int MaxFrameBodySize { get; set; } = 8192;

        /// <inheritdoc/>
        public bool HasTransportFlowControl => true;

        /// <summary>
        /// The pool receive buffers are rented from.
        /// </summary>
        public BufferManager BufferManager { get; }

        /// <summary>
        /// The clock used for deadlines and round trip measurement.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// Binds a data channel to a QUIC stream and starts reading it.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="streamId">The transport stream identifier that
        /// travelled in transportChannelId.</param>
        public void BindChannel(uint channelId, ulong streamId)
        {
            RecordChannel(channelId, streamId, canSend: true, canReceive: true);

            StartReceiveLoop(channelId, streamId);
        }

        /// <summary>
        /// Opens the stream for a channel when this peer is the §7.4
        /// initiator, records the channel binding, and returns the id to
        /// carry in OpenDataChannel.
        /// </summary>
        /// <param name="channelId">The data channel id.</param>
        /// <param name="direction">The channel direction from
        /// OpenDataChannel.</param>
        /// <param name="isOpcUaServer">True when this endpoint is the OPC
        /// UA Server role, independent of the QUIC/TLS role.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<ulong> OpenChannelStreamAsync(
            uint channelId,
            DataChannelDirection direction,
            bool isOpcUaServer,
            CancellationToken ct)
        {
            if (!IsStreamInitiator(direction, isOpcUaServer))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The {0} role does not initiate {1} data-channel streams.",
                    isOpcUaServer ? "OPC UA Server" : "OPC UA Client",
                    direction);
            }

            ulong streamId = await m_transport
                .OpenStreamAsync(IsBidirectionalStream(direction), ct)
                .ConfigureAwait(false);

            RecordChannel(
                channelId,
                streamId,
                CanSend(direction, isOpcUaServer),
                CanReceive(direction, isOpcUaServer));
            StartReceiveLoopIfNeeded(channelId, streamId);
            return streamId;
        }

        /// <summary>
        /// Associates a ChannelId with the QUIC stream id carried in
        /// OpenDataChannel and waits until that inbound stream has
        /// materialized on this connection.
        /// </summary>
        /// <param name="channelId">The data channel id.</param>
        /// <param name="streamId">The stream id from transportChannelId or
        /// revisedTransportChannelId.</param>
        /// <param name="direction">The negotiated channel direction.</param>
        /// <param name="isOpcUaServer">True when this endpoint is the OPC
        /// UA Server role, independent of the QUIC/TLS role.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask BindChannelAsync(
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            bool isOpcUaServer,
            CancellationToken ct)
        {
            ValidateAndReserveChannel(channelId, streamId, direction, isOpcUaServer);

            await CompleteInboundBindAsync(channelId, streamId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies the §7.4 checks a Server has to make before it binds a
        /// channel, and reserves the stream for it.
        /// </summary>
        /// <remarks>
        /// This is separated from waiting for the stream to materialize so
        /// that a refusal reaches the caller of OpenDataChannel. §7.4
        /// requires a Server to reject an unacceptable
        /// <c>transportChannelId</c> and forbids it echoing a value it has
        /// not validated, so none of these checks may be deferred to a
        /// background continuation.
        /// </remarks>
        /// <param name="channelId">The data channel id.</param>
        /// <param name="streamId">The stream id from transportChannelId.</param>
        /// <param name="direction">The negotiated channel direction.</param>
        /// <param name="isOpcUaServer">True when this endpoint is the OPC
        /// UA Server role, independent of the QUIC/TLS role.</param>
        public void ValidateAndReserveChannel(
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            bool isOpcUaServer)
        {
            m_transport.ValidateInboundDataChannelStream(
                streamId,
                IsBidirectionalStream(direction));

            RecordChannel(
                channelId,
                streamId,
                CanSend(direction, isOpcUaServer),
                CanReceive(direction, isOpcUaServer));
        }

        /// <summary>
        /// Waits for a reserved inbound stream to materialize and starts
        /// reading it.
        /// </summary>
        /// <remarks>
        /// A peer-initiated QUIC stream becomes observable only once the
        /// peer writes to it, so this may outlive the OpenDataChannel call
        /// that reserved it. If it fails the channel binding is dropped,
        /// which makes every later send on that ChannelId fault rather than
        /// travel on a stream that was never established. The stream itself
        /// stays reserved, because §7.4 forbids rebinding a stream that has
        /// been bound while the SecureChannel is open.
        /// </remarks>
        /// <param name="channelId">The data channel id.</param>
        /// <param name="streamId">The reserved stream id.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask CompleteInboundBindAsync(
            uint channelId,
            ulong streamId,
            CancellationToken ct)
        {
            try
            {
                await m_transport.BindInboundStreamAsync(streamId, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                m_channelsByChannel.TryRemove(channelId, out _);
                throw;
            }

            StartReceiveLoopIfNeeded(channelId, streamId);
        }

        /// <summary>
        /// Starts waiting for a reserved inbound stream without holding up
        /// the caller.
        /// </summary>
        /// <remarks>
        /// A peer-initiated QUIC stream becomes observable only once the peer
        /// writes to it, and a Client normally waits for the OpenDataChannel
        /// response before it writes. Awaiting the stream inside the Service
        /// call would therefore deadlock the two ends against each other, so
        /// the wait runs against this transport's own lifetime instead of the
        /// request that reserved the stream.
        /// </remarks>
        /// <param name="channelId">The data channel id.</param>
        /// <param name="streamId">The reserved stream id.</param>
        public void BeginInboundBind(uint channelId, ulong streamId)
        {
            _ = ObserveInboundBindAsync(channelId, streamId);
        }

        private async Task ObserveInboundBindAsync(uint channelId, ulong streamId)
        {
            try
            {
                await CompleteInboundBindAsync(channelId, streamId, m_stop.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The transport is shutting down.
            }
            catch (Exception e)
            {
                // The binding has already been dropped, so the channel faults
                // on its first send rather than appearing usable. Reported
                // rather than discarded: a bind that never completes is
                // otherwise invisible.
                m_logger.QuicDataChannelBindFailed(e, channelId, streamId);
            }
        }

        /// <summary>
        /// Releases a channel's stream. A RESET carrying a StatusCode is
        /// realized as a QUIC RESET_STREAM whose application error code
        /// carries it; an orderly close completes the writes.
        /// </summary>
        /// <param name="channelId">The data channel.</param>
        /// <param name="status">The StatusCode, or Good for an orderly
        /// close.</param>
        public void ReleaseChannel(uint channelId, StatusCode status)
        {
            if (!m_channelsByChannel.TryRemove(channelId, out ChannelStreamBinding? binding))
            {
                return;
            }

            ulong streamId = binding.StreamId;

            if (StatusCode.IsBad(status))
            {
                m_transport.AbortStream(streamId, status.Code);
            }
            else
            {
                m_transport.CloseStream(streamId);
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
        {
            if (!m_channelsByChannel.TryGetValue(frame.ChannelId, out ChannelStreamBinding? binding))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelIdInvalid,
                    "ChannelId {0} is not bound to a QUIC stream.",
                    frame.ChannelId);
            }

            if (!binding.CanSend)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelDirectionUnsupported,
                    "This OPC UA role may not send DATA on ChannelId {0}.",
                    frame.ChannelId);
            }

            int bodySize = frame.EncodedSize;
            int total = MessageHeaderSize + bodySize;

            byte[] buffer = BufferManager.TakeBuffer(total, nameof(SendFrameAsync), ct);

            try
            {
                WriteMessageHeader(buffer.AsSpan(0, MessageHeaderSize), total);
                DataChannelFrameCodec.Encode(buffer.AsSpan(MessageHeaderSize, bodySize), frame);

                await m_transport
                    .SendOnStreamAsync(binding.StreamId, new ReadOnlyMemory<byte>(buffer, 0, total), ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                BufferManager.ReturnBuffer(buffer, nameof(SendFrameAsync));
            }
        }

        /// <inheritdoc/>
        public void OnProtocolFault(DataChannelFrameError error)
        {
            // Losing the framing on a data channel stream is not a reason
            // to destroy the SecureChannel, because the control stream is
            // a different stream and is still trustworthy. The channel is
            // reset instead.
            m_logger.QuicDataChannelFramingFault(error.ToString());
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            await m_stop.CancelAsync().ConfigureAwait(false);
            m_stop.Dispose();

            m_channelsByChannel.Clear();
            m_channelsByStream.Clear();
        }

        /// <summary>
        /// Returns true when the OPC UA role opens the QUIC stream for the
        /// requested direction, per Part 6 §7.4 and §7.10.
        /// </summary>
        public static bool IsStreamInitiator(DataChannelDirection direction, bool isOpcUaServer)
        {
            return direction switch
            {
                DataChannelDirection.SourceToSink => isOpcUaServer,
                DataChannelDirection.SinkToSource => !isOpcUaServer,
                DataChannelDirection.Bidirectional => !isOpcUaServer,
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelDirectionUnsupported,
                    "DataChannelDirection {0} is not supported over opc.quic.",
                    direction)
            };
        }

        /// <summary>
        /// Returns true when the channel uses a bidirectional QUIC stream.
        /// </summary>
        public static bool IsBidirectionalStream(DataChannelDirection direction)
        {
            return direction switch
            {
                DataChannelDirection.Bidirectional => true,
                DataChannelDirection.SourceToSink or DataChannelDirection.SinkToSource => false,
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelDirectionUnsupported,
                    "DataChannelDirection {0} is not supported over opc.quic.",
                    direction)
            };
        }

        private async Task RunReceiveLoopAsync(uint channelId, ulong streamId, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                ArraySegment<byte> chunk;

                try
                {
                    chunk = await m_transport
                        .ReceiveOnStreamAsync(streamId, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ServiceResultException e) when
                    (e.StatusCode == StatusCodes.BadConnectionClosed)
                {
                    // The peer completed or reset the stream, which is
                    // the transport level form of END or RESET.
                    Manager?.TryGetChannel(channelId, out DataChannel? closing);
                    return;
                }
#pragma warning disable CA1031 // One bad stream must not stop the others.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    m_logger.QuicDataChannelReceiveFailed(e, channelId);
                    return;
                }

                try
                {
                    DispatchChunk(channelId, chunk);
                }
                finally
                {
                    BufferManager.ReturnBuffer(chunk.Array, nameof(RunReceiveLoopAsync));
                }
            }
        }

        private void DispatchChunk(uint channelId, ArraySegment<byte> chunk)
        {
            if (chunk.Count <= MessageHeaderSize)
            {
                return;
            }

            var body = new ReadOnlyMemory<byte>(
                chunk.Array!,
                chunk.Offset + MessageHeaderSize,
                chunk.Count - MessageHeaderSize);

            if (!DataChannelFrameCodec.TryDecode(
                body,
                MaxFrameBodySize,
                out DataChannelFrame frame,
                out DataChannelFrameError error))
            {
                OnProtocolFault(error);

                if (Manager != null &&
                    Manager.TryGetChannel(channelId, out DataChannel? faulted) &&
                    faulted != null)
                {
                    faulted.Reset(error.ToStatusCode());
                }

                return;
            }

            // The stream a frame arrived on is the authoritative binding,
            // so a frame naming a different ChannelId is a protocol error
            // rather than a demultiplexing hint.
            if (frame.ChannelId != channelId &&
                frame.ChannelId != DataChannelConstants.ConnectionControlChannelId)
            {
                m_logger.QuicDataChannelMisdirectedFrame(frame.ChannelId, channelId);
                return;
            }

            Manager?.HandleFrame(frame);
        }

        private void RecordChannel(
            uint channelId,
            ulong streamId,
            bool canSend,
            bool canReceive)
        {
            var binding = new ChannelStreamBinding(streamId, canSend, canReceive);

            if (!m_channelsByStream.TryAdd(streamId, channelId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelLimitsExceeded,
                    "QUIC stream {0} is already bound to a data channel on this SecureChannel.",
                    streamId);
            }

            if (!m_channelsByChannel.TryAdd(channelId, binding))
            {
                m_channelsByStream.TryRemove(streamId, out _);
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelLimitsExceeded,
                    "ChannelId {0} is already bound to a QUIC stream.",
                    channelId);
            }
        }

        private static bool CanSend(DataChannelDirection direction, bool isOpcUaServer)
        {
            return direction switch
            {
                DataChannelDirection.SourceToSink => isOpcUaServer,
                DataChannelDirection.SinkToSource => !isOpcUaServer,
                DataChannelDirection.Bidirectional => true,
                _ => false
            };
        }

        private static bool CanReceive(DataChannelDirection direction, bool isOpcUaServer)
        {
            return direction switch
            {
                DataChannelDirection.SourceToSink => !isOpcUaServer,
                DataChannelDirection.SinkToSource => isOpcUaServer,
                DataChannelDirection.Bidirectional => true,
                _ => false
            };
        }

        private void StartReceiveLoopIfNeeded(uint channelId, ulong streamId)
        {
            if (m_channelsByChannel.TryGetValue(channelId, out ChannelStreamBinding? binding) &&
                binding.CanReceive)
            {
                StartReceiveLoop(channelId, streamId);
            }
        }

        private void StartReceiveLoop(uint channelId, ulong streamId)
        {
            _ = RunReceiveLoopAsync(channelId, streamId, m_stop.Token);
        }

        /// <summary>
        /// Writes the 12-byte Message header that precedes the stream
        /// header under QUIC framing. Internal so the emitted bytes can be
        /// compared against the specification's published wire vector,
        /// whose first twelve bytes the codec tests do not cover.
        /// </summary>
        /// <param name="destination">The header destination.</param>
        /// <param name="totalSize">The whole frame length.</param>
        internal void WriteMessageHeader(Span<byte> destination, int totalSize)
        {
            // 'STR' followed by 'F': a data channel frame is a single
            // chunk and is never a Message abort.
            destination[0] = (byte)'S';
            destination[1] = (byte)'T';
            destination[2] = (byte)'R';
            destination[3] = (byte)'F';
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4), (uint)totalSize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), SecureChannelId);
        }

        /// <summary>
        /// Releases a channel's stream once the channel reaches a terminal
        /// state.
        /// </summary>
        /// <remarks>
        /// §7.4 makes the stream the channel's lifetime: an orderly close
        /// completes the writes, and a <c>RESET</c> becomes a
        /// <c>RESET_STREAM</c> whose application error code carries the
        /// StatusCode, which is how the peer learns of it at the transport
        /// layer at all.
        /// </remarks>
        private void OnChannelStateChanged(object? sender, DataChannelStateChangedEventArgs e)
        {
            if (e.State is DataChannelState.Closed or DataChannelState.Faulted)
            {
                ReleaseChannel(e.ChannelId, e.Status);
            }
        }

        /// <summary>
        /// MessageType, IsFinal, MessageSize and SecureChannelId.
        /// </summary>
        private const int MessageHeaderSize = 12;

        private sealed record ChannelStreamBinding(ulong StreamId, bool CanSend, bool CanReceive);

        private readonly ConcurrentDictionary<uint, ChannelStreamBinding> m_channelsByChannel = new();
        private readonly ConcurrentDictionary<ulong, uint> m_channelsByStream = new();
        private readonly QuicMultiplexedTransport m_transport;
        private readonly CancellationTokenSource m_stop;
        private readonly ILogger m_logger;
        private DataChannelManager? m_manager;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for
    /// <see cref="QuicDataChannelTransport"/>.
    /// </summary>
    internal static partial class QuicDataChannelTransportLog
    {
        [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
            Message = "opc.quic data channel framing fault: {Error}.")]
        public static partial void QuicDataChannelFramingFault(
            this ILogger logger,
            string error);

        [LoggerMessage(EventId = 11, Level = LogLevel.Warning,
            Message = "opc.quic data channel {ChannelId} receive loop ended with an error.")]
        public static partial void QuicDataChannelReceiveFailed(
            this ILogger logger,
            Exception exception,
            uint channelId);

        [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
            Message = "A frame naming ChannelId {FrameChannelId} arrived on the stream bound to {StreamChannelId}.")]
        public static partial void QuicDataChannelMisdirectedFrame(
            this ILogger logger,
            uint frameChannelId,
            uint streamChannelId);

        [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
            Message = "opc.quic data channel {ChannelId} was never bound to stream {StreamId}.")]
        public static partial void QuicDataChannelBindFailed(
            this ILogger logger,
            Exception exception,
            uint channelId,
            ulong streamId);
    }
}
