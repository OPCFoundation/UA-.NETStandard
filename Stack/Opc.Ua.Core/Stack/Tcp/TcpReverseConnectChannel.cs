/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the reverse connect client UA TCP channel.
    /// </summary>
    public class TcpReverseConnectChannel : TcpListenerChannel
    {
        #region Constructors
        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpReverseConnectChannel(
            string contextId,
            ITcpChannelListener listener,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            EndpointDescriptionCollection endpoints)
        :
            base(contextId, listener, bufferManager, quotas, null, null, endpoints)
        {
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The channel name used in trace output.
        /// </summary>
        public override string ChannelName => "TCPREVERSECONNECTCHANNEL";
        #endregion

        #region Socket Event Handlers
        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the implementor takes ownership of the buffer.</returns>
        protected override bool HandleIncomingMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            lock (DataLock)
            {
                SetResponseRequired(true);

                try
                {
                    // check for reverse hello.
                    if (messageType == TcpMessageType.ReverseHello)
                    {
                        Utils.Trace("Channel {0}: ProcessReverseHelloMessage", ChannelId);
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
        #endregion

        #region Connect/Reconnect Sequence
        /// <summary>
        /// Processes a ReverseHello message from the server.
        /// </summary>
        private bool ProcessReverseHelloMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            // validate the channel state.            
            if (State != TcpChannelState.Connecting)
            {
                ForceChannelFault(StatusCodes.BadTcpMessageTypeInvalid, "Client sent an unexpected ReverseHello message.");
                return false;
            }

            try
            {
                MemoryStream istrm = new MemoryStream(messageChunk.Array, messageChunk.Offset, messageChunk.Count, false);
                BinaryDecoder decoder = new BinaryDecoder(istrm, Quotas.MessageContext);
                istrm.Seek(TcpMessageLimits.MessageTypeAndSize, SeekOrigin.Current);

                // read peer information.
                string serverUri = decoder.ReadString(null);
                string endpointUrlString = decoder.ReadString(null);
                Uri endpointUri = new Uri(endpointUrlString);

                State = TcpChannelState.Connecting;

                Task t = Task.Run(async () => {
                    try
                    {
                        if (false == await Listener.TransferListenerChannel(Id, serverUri, endpointUri))
                        {
                            SetResponseRequired(true);
                            ForceChannelFault(StatusCodes.BadTcpMessageTypeInvalid, "The reverse connection was rejected by the client.");
                        }
                        else
                        {
                            // Socket is now owned by client, don't clean up
                            CleanupTimer();
                        }
                    }
                    catch (Exception)
                    {
                        SetResponseRequired(true);
                        ForceChannelFault(StatusCodes.BadInternalError, "Internal error approving the reverse connection.");
                    }
                });
            }
            catch (Exception e)
            {
                ForceChannelFault(e, StatusCodes.BadTcpInternalError, "Unexpected error while processing a ReverseHello message.");
            }

            return false;
        }
        #endregion
    }
}
