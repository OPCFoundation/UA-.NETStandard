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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Carries data channels over a SecureChannel using the inline framing of
    /// the Part 6 errata: a frame is one STR MessageChunk, written to the
    /// connection exactly as a MSG chunk is.
    /// </summary>
    /// <remarks>
    /// This owns the STR MessageType on the channel and is the only thing that
    /// knows the frame format; the channel itself sees an opaque body.
    /// </remarks>
    public sealed class DataChannelExtension : ISecureChannelMessageExtension, IDataChannelTransport
    {
        /// <summary>
        /// Attaches data channels to a SecureChannel.
        /// </summary>
        /// <param name="host">The channel that carries the frames.</param>
        /// <param name="isServer">True on the server side, which allocates
        /// ChannelIds.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="maxDataChannels">The most channels kept open at once
        /// on this SecureChannel.</param>
        /// <param name="maxCreditPerChannel">The largest window granted to one
        /// channel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="host"/> is <c>null</c>.</exception>
        public DataChannelExtension(
            ISecureChannelMessageHost host,
            bool isServer,
            ITelemetryContext telemetry,
            ushort maxDataChannels = 16,
            uint maxCreditPerChannel = 1024 * 1024)
        {
            m_host = host ?? throw new ArgumentNullException(nameof(host));
            m_isSource = isServer;

            Manager = new DataChannelManager(
                this,
                isServer,
                telemetry,
                maxDataChannels,
                maxCreditPerChannel);
        }

        /// <summary>
        /// The data channels multiplexed onto this SecureChannel.
        /// </summary>
        public DataChannelManager Manager { get; }

        /// <summary>
        /// Raised when a frame violated the framing rules badly enough to cost
        /// the whole SecureChannel.
        /// </summary>
        /// <remarks>
        /// The channel is faulted either way; this reports which rule was
        /// broken, which the transport error alone does not carry.
        /// </remarks>
        public event EventHandler<DataChannelFrameError>? ProtocolFault;

        /// <inheritdoc/>
        public uint MessageType => TcpMessageType.Stream;

        /// <inheritdoc/>
        public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

        /// <inheritdoc/>
        public int MaxFrameBodySize
            => DataChannelFrameCodec.MaxPayload(
                DataChannelFramingMode.Inline,
                m_host.SendBufferSize,
                m_host.SymmetricSignatureSize + 2,
                withDeadline: true);

        /// <inheritdoc/>
        public bool HasTransportFlowControl => false;

        /// <inheritdoc/>
        public BufferManager BufferManager => m_host.BufferManager;

        /// <inheritdoc/>
        public TimeProvider TimeProvider => m_host.TimeProvider;

        /// <inheritdoc/>
        public void OnMessageReceived(uint messageType, uint requestId, ArraySegment<byte> body)
        {
            if (!DataChannelFrameCodec.TryValidateChunkHeaders(
                messageType,
                requestId,
                out DataChannelFrameError headerError))
            {
                OnProtocolFault(headerError);
                return;
            }

            if (!DataChannelFrameCodec.TryDecode(
                new ReadOnlyMemory<byte>(body.Array!, body.Offset, body.Count),
                m_host.ReceiveBufferSize,
                out DataChannelFrame frame,
                out DataChannelFrameError error))
            {
                if (error.IsFatal())
                {
                    OnProtocolFault(error);
                    return;
                }

                if (Manager.TryGetChannel(frame.ChannelId, out DataChannel? faulted) &&
                    faulted != null)
                {
                    faulted.Reset(error.ToStatusCode());
                }

                return;
            }

            Manager.HandleFrame(frame);
        }

        /// <inheritdoc/>
        public void OnMessageRejected(ServiceResult reason)
        {
            // The chunk never decrypted, verified or sequenced, so nothing of
            // its content was seen and the fault is at the framing layer.
            OnProtocolFault(DataChannelFrameError.MalformedHeader);
        }

        /// <inheritdoc/>
        public void OnSecurityTokenActivated()
        {
            // The channel rebases the budget itself, because every MessageType
            // it carries draws on the same space.
        }

        /// <inheritdoc/>
        public void OnChannelClosed()
        {
            Manager.AbortAll(StatusCodes.BadSecureChannelClosed);
        }

        /// <inheritdoc/>
        public async ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
        {
            int size = frame.EncodedSize;
            byte[] body = BufferManager.TakeBuffer(size, nameof(SendFrameAsync), ct);

            try
            {
                int written = DataChannelFrameCodec.Encode(body.AsSpan(0, size), frame);

                await m_host
                    .SendMessageAsync(
                        TcpMessageType.Stream,
                        DataChannelConstants.FrameRequestId,
                        !m_isSource,
                        new ArraySegment<byte>(body, 0, written),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException e) when (e.StatusCode == StatusCodes.BadRequestTooLarge)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelLimitsExceeded,
                    "The data channel frame exceeds the negotiated buffer size.");
            }
            finally
            {
                BufferManager.ReturnBuffer(body, nameof(SendFrameAsync));
            }
        }

        /// <inheritdoc/>
        public void OnProtocolFault(DataChannelFrameError error)
        {
            ProtocolFault?.Invoke(this, error);

            m_host.Fault(ServiceResult.Create(
                StatusCodes.BadTcpMessageTypeInvalid,
                "A data channel frame violated the framing rules: {0}.",
                error));
        }

        private readonly ISecureChannelMessageHost m_host;
        private readonly bool m_isSource;
    }
}
