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
            EndpointDescriptionCollection endpoints,
            ITelemetryContext telemetry)
            : base(contextId, listener, bufferManager, quotas, null, endpoints, telemetry)
        {
            m_logger = telemetry.CreateLogger<TcpReverseConnectChannel>();
        }

        /// <summary>
        /// The channel name used in trace output.
        /// </summary>
        public override string ChannelName => "TCPREVERSECONNECTCHANNEL";

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
                        m_logger.LogInformation("ChannelId {Id}: ProcessReverseHelloMessage", ChannelId);
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
                string serverUri = decoder.ReadString(null);
                string endpointUrlString = decoder.ReadString(null);
                var endpointUri = new Uri(endpointUrlString);

                State = TcpChannelState.Connecting;

                var t = Task.Run(async () =>
                {
                    try
                    {
                        if (!await Listener
                                .TransferListenerChannelAsync(Id, serverUri, endpointUri)
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
}
