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
using System.Buffers.Binary;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Carries complete UACP chunks through an established optional native Client/Server flow.
    /// The SecureChannel pipeline continues to own UACP framing and UASC security.
    /// </summary>
    /// <remarks>
    /// Takes ownership of the supplied flow, but not its context or other flows. Each send is
    /// exactly one DATA payload; chunk coalescing, nested legacy FLO/FLC carriage and queue expiry are unavailable.
    /// Use asynchronous disposal to await flow reset and observe any closure failure.
    /// </remarks>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelByteTransport :
        IUaSCByteTransport,
        IUaSCByteTransportLimits,
        IDisposable,
        IAsyncDisposable
    {
        /// <summary>
        /// Wraps a ready native flow selected for the reserved Client/Server resource.
        /// The caller retains the context and buffer manager for at least the transport lifetime.
        /// </summary>
        public DataChannelByteTransport(
            DataChannelContext context,
            DataChannel channel,
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry,
            EndPoint? localEndpoint = null,
            EndPoint? remoteEndpoint = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            m_channel = channel ?? throw new ArgumentNullException(nameof(channel));
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            ValidateReceiveBufferSize(receiveBufferSize);
            ValidateFlow(context, channel);
            m_receiveBufferSize = receiveBufferSize;
            m_logger = telemetry.CreateLogger<DataChannelByteTransport>();
            m_closed = new CancellationTokenSource();
            m_closedToken = m_closed.Token;
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
        }

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint { get; }

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint { get; }

        /// <inheritdoc/>
        public TransportChannelFeatures Features => TransportChannelFeatures.None;

        /// <inheritdoc/>
        public string Implementation => "UA-FLOW";

        /// <summary>
        /// Gets the flow identifier for correlating native and SecureChannel diagnostics.
        /// </summary>
        public uint ChannelId => m_channel.ChannelId;

        /// <inheritdoc/>
        public ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            ct.ThrowIfCancellationRequested();
            throw new NotSupportedException("The native Client/Server flow is already established.");
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            ThrowIfClosed();
            ValidateChunk(chunk.Span, m_channel.Parameters.MaxMessageSize);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, m_closedToken);
            try
            {
                DataChannelResult result = await m_channel.SendWithAdmissionAsync(
                    new ByteString(chunk),
                    cancellationToken: cancellation.Token).ConfigureAwait(false);
                if (result != DataChannelResult.Good)
                {
                    throw new ServiceResultException(
                        ToStatusCode(result),
                        $"The native Client/Server flow refused the chunk: {result}.");
                }
            }
            catch (DataChannelException exception)
            {
                throw new ServiceResultException(
                    ToStatusCode(exception.Result),
                    "The native Client/Server flow failed while sending a chunk.",
                    exception);
            }
            catch (OperationCanceledException) when (m_closedToken.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConnectionClosed,
                    "The native Client/Server flow closed while sending a chunk.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }
            ThrowIfClosed();
            ct.ThrowIfCancellationRequested();
            uint maximum = m_channel.Parameters.MaxMessageSize;
            long length = 0;
            foreach (ArraySegment<byte> segment in buffers)
            {
                length += segment.Count;
                if (length > maximum || length > int.MaxValue)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTcpMessageTooLarge,
                        "The gathered chunk exceeds the admitted native flow message size.");
                }
            }
            if (length <= TcpMessageLimits.MessageTypeAndSize)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTcpMessageTooLarge,
                    "A native Client/Server flow payload must contain a complete UACP chunk.");
            }

            byte[] chunk = m_bufferManager.TakeBuffer((int)length, nameof(SendChunkAsync), ct);
            try
            {
                int offset = 0;
                foreach (ArraySegment<byte> segment in buffers)
                {
                    segment.AsSpan().CopyTo(chunk.AsSpan(offset));
                    offset += segment.Count;
                }
                await SendChunkAsync(chunk.AsMemory(0, (int)length), ct).ConfigureAwait(false);
            }
            finally
            {
                m_bufferManager.ReturnBuffer(chunk, nameof(SendChunkAsync));
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            ThrowIfClosed();
            if (Interlocked.Exchange(ref m_receiving, 1) != 0)
            {
                throw new InvalidOperationException("A native Client/Server chunk receive is already in progress.");
            }

            try
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, m_closedToken);
                using DataChannelMessage message = await m_channel.ReadAsync(cancellation.Token).ConfigureAwait(false)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadConnectionClosed,
                        "The native Client/Server flow ended.");
                ValidateChunk(message.Payload.Span, (uint)Volatile.Read(ref m_receiveBufferSize));
                byte[] buffer = m_bufferManager.TakeBuffer(
                    message.Length,
                    nameof(ReceiveChunkAsync),
                    cancellation.Token);
                try
                {
                    message.Payload.Span.CopyTo(buffer);
                    return new ArraySegment<byte>(buffer, 0, message.Length);
                }
                catch
                {
                    m_bufferManager.ReturnBuffer(buffer, nameof(ReceiveChunkAsync));
                    throw;
                }
            }
            catch (DataChannelException exception)
            {
                throw new ServiceResultException(
                    ToStatusCode(exception.Result),
                    "The native Client/Server flow failed while receiving a chunk.",
                    exception);
            }
            catch (OperationCanceledException) when (m_closedToken.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConnectionClosed,
                    "The native Client/Server flow closed while receiving a chunk.");
            }
            finally
            {
                Volatile.Write(ref m_receiving, 0);
            }
        }

        /// <summary>
        /// Cancels local operations and starts an isolated reset of this flow, never its context.
        /// Closure failures are logged and also propagated by asynchronous disposal.
        /// </summary>
        public void Close()
        {
            if (Interlocked.Exchange(ref m_closeStarted, 1) != 0)
            {
                return;
            }
            try
            {
                m_closed.Cancel();
            }
            finally
            {
                _ = CloseFlowAsync();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Close();
            await m_closeCompletion.Task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        void IUaSCByteTransportLimits.SetReceiveBufferSize(int receiveBufferSize)
        {
            ValidateReceiveBufferSize(receiveBufferSize);
            Volatile.Write(ref m_receiveBufferSize, receiveBufferSize);
        }

        /// <summary>
        /// Validates the complete native lane descriptor and its context ownership.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The context is not a confirmed native association or the flow does not meet the lane contract.
        /// </exception>
        private static void ValidateFlow(DataChannelContext context, DataChannel channel)
        {
            if (context.CarrierKind is not DataChannelCarrierKind.Quic and not DataChannelCarrierKind.WebTransport ||
                !context.IsReady)
            {
                throw new ArgumentException("A confirmed native data context is required.", nameof(context));
            }
            bool belongsToContext = false;
            foreach (DataChannel candidate in context.Channels)
            {
                if (ReferenceEquals(candidate, channel))
                {
                    belongsToContext = true;
                    break;
                }
            }
            if (!belongsToContext)
            {
                throw new ArgumentException("The flow does not belong to this data context.", nameof(channel));
            }

            DataChannelParametersDataType parameters = channel.Parameters;
            if (parameters.ResourceUri != DataChannelProfiles.ClientServerResource ||
                parameters.Protocol != DataChannelProfiles.OpaquePayload ||
                parameters.Direction != DataChannelDirection.Duplex ||
                parameters.Delivery != DataChannelDeliveryMode.Ordered ||
                parameters.Protection != DataChannelProtectionMode.Confidential ||
                parameters.AllowQueueExpiry ||
                parameters.MaxMessageSize <= TcpMessageLimits.MessageTypeAndSize ||
                channel.State is not DataChannelState.Open)
            {
                throw new ArgumentException(
                    "The flow does not meet the native Client/Server lane contract.",
                    nameof(channel));
            }
        }

        /// <summary>
        /// Checks the advertised message length before allocating or handing bytes to UASC.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The payload is not exactly one recognized UACP chunk within the admitted capacity.
        /// </exception>
        private static void ValidateChunk(ReadOnlySpan<byte> chunk, uint maximum)
        {
            if (chunk.Length <= TcpMessageLimits.MessageTypeAndSize || chunk.Length > maximum)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTcpMessageTooLarge,
                    "The native flow payload is not a complete UACP chunk within the negotiated size.");
            }
            uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
            if (!TcpMessageType.IsValid(messageType) ||
                (messageType & TcpMessageType.MessageTypeMask) is
                    TcpMessageType.FlowData or TcpMessageType.FlowControl)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTcpMessageTypeInvalid,
                    "The native Client/Server lane requires an ordinary UACP message type, not a legacy FLO/FLC type.");
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]) != chunk.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTcpMessageTooLarge,
                    "Each native Client/Server DATA payload must contain exactly one complete UACP chunk.");
            }
        }

        /// <summary>
        /// Validates the local chunk capacity independently of the flow's mutable send limit.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The receive capacity cannot contain a UACP header and body.
        /// </exception>
        private static void ValidateReceiveBufferSize(int receiveBufferSize)
        {
            if (receiveBufferSize <= TcpMessageLimits.MessageTypeAndSize)
            {
                throw new ArgumentOutOfRangeException(nameof(receiveBufferSize));
            }
        }

        /// <summary>
        /// Maps flow outcomes to existing transport status codes without introducing new codes.
        /// </summary>
        private static StatusCode ToStatusCode(DataChannelResult result)
        {
            return result switch
            {
                DataChannelResult.Unsupported => StatusCodes.BadNotSupported,
                DataChannelResult.InvalidArgument => StatusCodes.BadInvalidArgument,
                DataChannelResult.InvalidState => StatusCodes.BadInvalidState,
                DataChannelResult.NotAuthorized => StatusCodes.BadUserAccessDenied,
                DataChannelResult.SecurityFailure => StatusCodes.BadSecurityChecksFailed,
                DataChannelResult.TooLarge => StatusCodes.BadTcpMessageTooLarge,
                DataChannelResult.Timeout => StatusCodes.BadTimeout,
                DataChannelResult.Closed => StatusCodes.BadConnectionClosed,
                DataChannelResult.UnknownChannel => StatusCodes.BadSecureChannelIdInvalid,
                DataChannelResult.WouldBlock => StatusCodes.BadResourceUnavailable,
                _ => StatusCodes.BadUnexpectedError
            };
        }

        /// <summary>
        /// Rejects new operations after synchronous closure has begun.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The transport is already closed.
        /// </exception>
        private void ThrowIfClosed()
        {
            if (Volatile.Read(ref m_closeStarted) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadConnectionClosed, "The native lane is closed.");
            }
        }

        /// <summary>
        /// Observes asynchronous reset for the synchronous transport close entry point.
        /// </summary>
        private async Task CloseFlowAsync()
        {
            Exception? failure = null;
            try
            {
                await m_channel.ResetAsync(DataChannelResult.Closed).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                m_closed.Dispose();
            }
            if (failure == null)
            {
                m_closeCompletion.TrySetResult(true);
            }
            else
            {
                try
                {
                    m_logger.NativeLaneCloseFailed(failure, ChannelId);
                }
                finally
                {
                    m_closeCompletion.TrySetException(failure);
                }
            }
        }

        private readonly DataChannel m_channel;
        private readonly BufferManager m_bufferManager;
        private readonly ILogger m_logger;
        private readonly CancellationTokenSource m_closed;
        private readonly CancellationToken m_closedToken;

        private readonly TaskCompletionSource<bool> m_closeCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_receiveBufferSize;
        private int m_receiving;
        private int m_closeStarted;
    }

    /// <summary>
    /// Source-generated native Client/Server lane diagnostics.
    /// </summary>
    internal static partial class DataChannelByteTransportLog
    {
        /// <summary>
        /// Reports a failed asynchronous reset requested through synchronous transport closure.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.DataChannelByteTransport, Level = LogLevel.Warning,
            Message = "Could not close native Client/Server flow {ChannelId}.")]
        public static partial void NativeLaneCloseFailed(this ILogger logger, Exception exception, uint channelId);
    }
}
