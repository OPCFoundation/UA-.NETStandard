/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Callback when the token is activated
    /// </summary>
    public delegate void ChannelTokenActivatedEventHandler(
        ITransportChannel channel,
        ChannelToken currentToken,
        ChannelToken previousToken);

    /// <summary>
    /// Secure channels implement this interface and the
    /// ITransportChannel interface. It is not to be exposed
    /// to clients but should be implemented by channels.
    /// </summary>
    public interface ISecureChannel
    {
        /// <summary>
        /// Gets the channel's current security token.
        /// </summary>
        ChannelToken? CurrentToken { get; }

        /// <summary>
        /// Register for token change events
        /// </summary>
        event ChannelTokenActivatedEventHandler OnTokenActivated;

        /// <summary>
        /// Initializes a secure channel with the endpoint identified
        /// by the URL.
        /// </summary>
        /// <param name="url">The URL for the endpoint.</param>
        /// <param name="settings">The settings to use when creating
        /// the channel.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">Thrown if any
        /// communication error occurs.</exception>
        ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct);

        /// <summary>
        /// Initializes a secure channel with the endpoint of the
        /// waiting connection
        /// </summary>
        /// <param name="connection">A waiting connection</param>
        /// <param name="settings">The settings to use when creating
        /// the channel.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">Thrown if any
        /// communication error occurs.</exception>
        ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct);
    }

    /// <summary>
    /// This is an interface to a channel which supports sending
    /// requests and receiving responses. This is the api exposed
    /// to clients.
    /// </summary>
    public interface ITransportChannel : IDisposable
    {
        /// <summary>
        /// A masking indicating which features are implemented.
        /// </summary>
        TransportChannelFeatures SupportedFeatures { get; }

        /// <summary>
        /// Gets the description for the endpoint used by the channel.
        /// Throws if the channel is not yet opened.
        /// </summary>
        EndpointDescription EndpointDescription { get; }

        /// <summary>
        /// Gets the configuration for the channel.
        /// Throws if the channel is not yet opened.
        /// </summary>
        EndpointConfiguration EndpointConfiguration { get; }

        /// <summary>
        /// Gets the context used when serializing messages exchanged
        /// via the channel. Throws if the channel is not yet opened.
        /// </summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets or sets the default timeout for requests send via
        /// the channel.
        /// </summary>
        int OperationTimeout { get; set; }

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        /// <param name="connection">The waiting reverse connection
        /// for the reconnect attempt.</param>
        /// <exception cref="ServiceResultException">Thrown if any
        /// communication error occurs.</exception>
        /// <param name="ct">The cancellation token.</param>
        /// <remarks>
        /// Calling this method will cause outstanding requests over
        /// the current secure channel to fail.
        /// </remarks>
        ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default);

        /// <summary>
        /// Sends a request over the secure channel
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The response returned by the server.</returns>
        /// <exception cref="ServiceResultException">Thrown if any
        /// communication error occurs.</exception>
        ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Closes the secure channel
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">Thrown if any
        /// communication error occurs.</exception>
        ValueTask CloseAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// The masks for the optional features which may not be
    /// supported by every transport channel.
    /// </summary>
    [Flags]
    public enum TransportChannelFeatures
    {
        /// <summary>
        /// The channel does not support any optional features.
        /// </summary>
        None = 0x0000,

        /// <summary>
        /// The channel supports Reconnect.
        /// </summary>
        Reconnect = 0x0004,

        /// <summary>
        /// The channel supports Reverse connect.
        /// </summary>
        ReverseConnect = 0x0040
    }
}
