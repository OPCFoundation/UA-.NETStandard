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
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The arguments passed to the ConnectionWaiting event. 
    /// </summary>
    public class ConnectionWaitingEventArgs : EventArgs, ITransportWaitingConnection
    {
        /// <summary>
        /// Create a connection waiting event for a reverse hello message.
        /// </summary>
        /// <param name="serverUri">The Uri of the server.</param>
        /// <param name="endpointUrl">The endpoint Url of the server.</param>
        protected ConnectionWaitingEventArgs(string serverUri, Uri endpointUrl)
        {
            ServerUri = serverUri;
            EndpointUrl = endpointUrl;
            Accepted = false;
        }

        /// <inheritdoc/>
        public string ServerUri { get; private set; }

        /// <inheritdoc/>
        public Uri EndpointUrl { get; private set; }

        /// <inheritdoc/>
        public virtual object Handle => null;

        /// <summary>
        /// Allow the event callback handler to accept the
        /// incoming reverse hello connection.
        /// </summary>
        public bool Accepted { get; set; }
    }

    /// <summary>
    /// The arguments passed to the ConnectionStatus event. 
    /// </summary>
    public class ConnectionStatusEventArgs : EventArgs
    {
        internal ConnectionStatusEventArgs(Uri endpointUrl, ServiceResult channelStatus, bool closed)
        {
            EndpointUrl = endpointUrl;
            ChannelStatus = channelStatus;
            Closed = closed;
        }

        /// <summary>
        /// The endpoint Url of the channel which changed the status.
        /// </summary>
        public Uri EndpointUrl { get; private set; }

        /// <summary>
        /// The new status of the channel.
        /// </summary>
        public ServiceResult ChannelStatus { get; private set; }

        /// <summary>
        /// Indicate that the channel is closed.
        /// </summary>
        public bool Closed { get; private set; }
    }
}
