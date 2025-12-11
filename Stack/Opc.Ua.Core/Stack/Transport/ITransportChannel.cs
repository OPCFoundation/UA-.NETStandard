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
        /// The unique identifier for the secure channel.
        /// </summary>
        byte[] ChannelThumbprint { get; }

        /// <summary>
        /// The client certificate used to establsih the secure channel.
        /// </summary>
        byte[] ClientChannelCertificate { get; }

        /// <summary>
        /// The server certificate used to establsih the secure channel.
        /// </summary>
        byte[] ServerChannelCertificate { get; }

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
