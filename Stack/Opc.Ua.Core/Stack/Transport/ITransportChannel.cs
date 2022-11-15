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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// This is an interface to a channel which supports 
    /// </summary>
    public interface ITransportChannel : IDisposable
    {
        /// <summary>
        /// A masking indicating which features are implemented.
        /// </summary>
        TransportChannelFeatures SupportedFeatures { get; }

        /// <summary>
        /// Gets the description for the endpoint used by the channel.
        /// </summary>
        EndpointDescription EndpointDescription { get; }

        /// <summary>
        /// Gets the configuration for the channel.
        /// </summary>
        EndpointConfiguration EndpointConfiguration { get; }

        /// <summary>
        /// Gets the context used when serializing messages exchanged via the channel.
        /// </summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets the the channel's current security token.
        /// </summary>
        ChannelToken CurrentToken { get; }

        /// <summary>
        /// Gets or sets the default timeout for requests send via the channel.
        /// </summary>
        int OperationTimeout { get; set; }

        /// <summary>
        /// Initializes a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <param name="url">The URL for the endpoint.</param>
        /// <param name="settings">The settings to use when creating the channel.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        void Initialize(
            Uri url,
            TransportChannelSettings settings);

        /// <summary>
        /// Initializes a secure channel with the endpoint identified by the URL.
        /// </summary>
        void Initialize(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings);

        /// <summary>
        /// Opens a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        void Open();

        /// <summary>
        /// Begins an asynchronous operation to open a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>The result which must be passed to the EndOpen method.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Open"/>
        IAsyncResult BeginOpen(
            AsyncCallback callback,
            object callbackData);

        /// <summary>
        /// Completes an asynchronous operation to open a secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginOpen call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Open" />
        void EndOpen(IAsyncResult result);

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <remarks>
        /// Calling this method will cause outstanding requests over the current secure channel to fail.
        /// </remarks>
        void Reconnect();

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        /// <param name="connection">The waiting reverse connection for the reconnect attempt.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <remarks>
        /// Calling this method will cause outstanding requests over the current secure channel to fail.
        /// </remarks>
        void Reconnect(ITransportWaitingConnection connection);

        /// <summary>
        /// Begins an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>The result which must be passed to the EndReconnect method.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Reconnect()" />
        IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData);

        /// <summary>
        /// Completes an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        /// <param name="result">The result returned from the BeginReconnect call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Reconnect()" />
        void EndReconnect(IAsyncResult result);

        /// <summary>
        /// Closes the secure channel.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        void Close();

        /// <summary>
        /// Begins an asynchronous operation to close the secure channel.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>The result which must be passed to the EndClose method.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Close" />
        IAsyncResult BeginClose(AsyncCallback callback, object callbackData);

        /// <summary>
        /// Completes an asynchronous operation to close the secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginClose call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Close" />
        void EndClose(IAsyncResult result);

        /// <summary>
        /// Sends a request over the secure channel.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <returns>The response returned by the server.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        IServiceResponse SendRequest(IServiceRequest request);

        /// <summary>
        /// Sends a request over the secure channel, async version.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The response returned by the server.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        Task<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct);

        /// <summary>
        /// Begins an asynchronous operation to send a request over the secure channel.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>The result which must be passed to the EndSendRequest method.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="SendRequest" />
        IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData);

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginSendRequest call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="SendRequest" />
        IServiceResponse EndSendRequest(IAsyncResult result);
    }

    /// <summary>
    /// The masks for the optional features which may not be supported by every transport channel.
    /// </summary>
    [Flags]
    public enum TransportChannelFeatures
    {
        /// <summary>
        /// The channel does not support any optional features.
        /// </summary>
        None = 0x0000,

        /// <summary>
        /// The channel supports Open.
        /// </summary>
        Open = 0x0001,

        /// <summary>
        /// The channel supports asynchronous Open.
        /// </summary>
        BeginOpen = 0x0002,

        /// <summary>
        /// The channel supports Reconnect.
        /// </summary>
        Reconnect = 0x0004,

        /// <summary>
        /// The channel supports asynchronous Reconnect.
        /// </summary>
        BeginReconnect = 0x0008,

        /// <summary>
        /// The channel supports asynchronous Close.
        /// </summary>
        BeginClose = 0x0010,

        /// <summary>
        /// The channel supports asynchronous SendRequest.
        /// </summary>
        BeginSendRequest = 0x0020,

        /// <summary>
        /// The channel supports Reconnect.
        /// </summary>
        ReverseConnect = 0x0040,

        /// <summary>
        /// The channel supports asynchronous SendRequestAsync.
        /// </summary>
        SendRequestAsync = 0x0080,
    }
}
