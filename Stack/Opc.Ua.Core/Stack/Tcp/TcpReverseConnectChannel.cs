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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the reverse connect client UA TCP channel.
    /// </summary>
    public class TcpReverseConnectChannel : TcpListenerChannel
    {
        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpReverseConnectChannel(
            string contextId,
            ITcpChannelListener listener,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            List<EndpointDescription> endpoints,
            ITelemetryContext telemetry)
            : this(contextId, listener, bufferManager, quotas, endpoints, telemetry, null)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket using the supplied
        /// <see cref="TimeProvider"/> for activity tracking.
        /// </summary>
        public TcpReverseConnectChannel(
            string contextId,
            ITcpChannelListener listener,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            List<EndpointDescription> endpoints,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider)
            : base(contextId, listener, bufferManager, quotas, null!, endpoints, telemetry, timeProvider)
        {
            m_logger = telemetry.CreateLogger<TcpReverseConnectChannel>();
        }

        /// <summary>
        /// The channel name used in trace output.
        /// </summary>
        public override string ChannelName => "TCPREVERSECONNECTCHANNEL";

        /// <summary>
        /// Reverse-connect channels only need to read the single
        /// <c>ReverseHello</c> message before the transport is handed off
        /// to the awaiting application by
        /// <c>ITcpChannelListener.TransferListenerChannelAsync</c>. A
        /// long-running receive loop is undesirable here because it would
        /// otherwise block in <c>ReceiveChunkAsync</c> at the moment of
        /// handoff; for WebSocket transports a cancel of that blocking
        /// receive aborts the underlying WebSocket and breaks the
        /// handoff. By reading exactly one chunk and exiting cleanly
        /// the channel releases the transport ready for the new owner to
        /// start its own loop without interruption.
        /// </summary>
        protected internal override void StartReceiveLoop()
        {
            StartReceiveLoopWithBody(ReadReverseHelloOnceAsync);
        }

        private async Task ReadReverseHelloOnceAsync(
            IUaSCByteTransport transport,
            CancellationToken ct)
        {
            ArraySegment<byte> chunk;
            try
            {
                chunk = await transport.ReceiveChunkAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (ServiceResultException sre)
            {
                OnTransportError(sre.Result);
                return;
            }
            catch (Exception ex)
            {
                OnTransportError(ServiceResult.Create(
                    ex,
                    StatusCodes.BadTcpInternalError,
                    ex.Message));
                return;
            }

            OnChunkReceived(chunk);
        }

        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the implementor takes ownership of the buffer.</returns>
        protected override bool HandleIncomingMessage(
            uint messageType,
            ArraySegment<byte> messageChunk)
        {
            lock (DataLock)
            {
                SetResponseRequired(true);

                try
                {
                    // check for reverse hello.
                    if (messageType == TcpMessageType.ReverseHello)
                    {
                        m_logger.TcpReverseConnectChannelLogMessage0(ChannelId);
                        return ProcessReverseHelloMessage(messageType, messageChunk);
                    }

                    // invalid message type - must close socket and reconnect.
                    ForceChannelFault(
                        StatusCodes.BadTcpMessageTypeInvalid,
                        "The reverse connect handler does not recognize the message type: {0:X8}.",
                        messageType);

                    return false;
                }
                finally
                {
                    SetResponseRequired(false);
                }
            }
        }

        /// <summary>
        /// Processes a ReverseHello message from the server.
        /// </summary>
        private bool ProcessReverseHelloMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            // validate the channel state.
            if (State != TcpChannelState.Connecting)
            {
                ForceChannelFault(
                    StatusCodes.BadTcpMessageTypeInvalid,
                    "Client sent an unexpected ReverseHello message.");
                return false;
            }

            try
            {
                using var decoder = new BinaryDecoder(messageChunk, Quotas.MessageContext);
                ReadAndVerifyMessageTypeAndSize(
                    decoder,
                    TcpMessageType.ReverseHello,
                    messageChunk.Count);

                // read peer information.
                string? serverUri = decoder.ReadString(null);
                string? endpointUrlString = decoder.ReadString(null);
                var endpointUri = new Uri(endpointUrlString!);

                State = TcpChannelState.Connecting;

                var t = Task.Run(async () =>
                {
                    try
                    {
                        if (!await Listener
                                .TransferListenerChannelAsync(Id, serverUri!, endpointUri)
                                .ConfigureAwait(false))
                        {
                            SetResponseRequired(true);
                            ForceChannelFault(
                                StatusCodes.BadTcpMessageTypeInvalid,
                                "The reverse connection was rejected by the client.");
                        }
                    }
                    catch (Exception)
                    {
                        SetResponseRequired(true);
                        ForceChannelFault(
                            StatusCodes.BadInternalError,
                            "Internal error approving the reverse connection.");
                    }
                });
            }
            catch (Exception e)
            {
                ForceChannelFault(
                    e,
                    StatusCodes.BadTcpInternalError,
                    "Unexpected error while processing a ReverseHello message.");
            }

            return false;
        }

        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for TcpReverseConnectChannel.
    /// </summary>
    internal static partial class TcpReverseConnectChannelLog
    {
        [LoggerMessage(EventId = CoreEventIds.TcpReverseConnectChannel + 0, Level = LogLevel.Information,
            Message = "ChannelId {Id}: ProcessReverseHelloMessage")]
        public static partial void TcpReverseConnectChannelLogMessage0(this ILogger logger, uint id);
    }

}
