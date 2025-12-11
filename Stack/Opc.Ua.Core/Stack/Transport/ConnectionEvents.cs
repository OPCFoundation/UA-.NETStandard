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
        public string ServerUri { get; }

        /// <inheritdoc/>
        public Uri EndpointUrl { get; }

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
        internal ConnectionStatusEventArgs(
            Uri endpointUrl,
            ServiceResult channelStatus,
            bool closed)
        {
            EndpointUrl = endpointUrl;
            ChannelStatus = channelStatus;
            Closed = closed;
        }

        /// <summary>
        /// The endpoint Url of the channel which changed the status.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// The new status of the channel.
        /// </summary>
        public ServiceResult ChannelStatus { get; }

        /// <summary>
        /// Indicate that the channel is closed.
        /// </summary>
        public bool Closed { get; }
    }
}
