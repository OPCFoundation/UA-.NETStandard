// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        public virtual object Handle => null!;

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
