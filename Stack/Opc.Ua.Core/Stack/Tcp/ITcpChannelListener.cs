/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Bindings
{

    /// <summary>
    /// Interface between listener and UA TCP channel
    /// </summary>
    public interface ITcpChannelListener
    {
        /// <summary>
        /// The endpoint url of the listener
        /// </summary>
        Uri EndpointUrl { get; }

        /// <summary>
        /// Binds a new socket to an existing channel.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="requestId"></param>
        /// <param name="sequenceNumber"></param>
        /// <param name="channelId"></param>
        /// <param name="clientCertificate"></param>
        /// <param name="token"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        bool ReconnectToExistingChannel(
            IMessageSocket socket,
            uint requestId,
            uint sequenceNumber,
            uint channelId,
            X509Certificate2 clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request);

        /// <summary>
        /// Used to transfer a reverse connection socket to the client.
        /// </summary>
        bool TransferListenerChannel(
            uint channelId,
            string serverUri,
            Uri endpointUrl);

        /// <summary>
        /// Called when a channel closes.
        /// </summary>
        void ChannelClosed(uint channelId);
    }
}
