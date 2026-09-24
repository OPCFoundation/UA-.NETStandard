/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Coordinates channel lifetime and connection handoffs with a UA TCP listener.
    /// </summary>
    public interface ITcpChannelListener
    {
        /// <summary>
        /// Gets the endpoint URL advertised by the listener.
        /// </summary>
        /// <value>
        /// The listener's endpoint URL.
        /// </value>
        Uri EndpointUrl { get; }

        /// <summary>
        /// Binds a new transport to an existing channel.
        /// </summary>
        /// <param name="reconnectingChannel">
        /// The temporary channel offering the connection after processing its renewal request.
        /// </param>
        /// <param name="transport">
        /// The detached connection to adopt. The caller must close it if the handoff is rejected or throws.
        /// </param>
        /// <param name="requestId">
        /// The Secure Conversation request identifier to use for the renewal response.
        /// </param>
        /// <param name="sequenceNumber">
        /// The renewal request's sequence number, checked against the retained channel's replay state.
        /// </param>
        /// <param name="channelId">
        /// The identifier of the existing secure channel requested by the client.
        /// </param>
        /// <param name="clientCertificate">
        /// The borrowed client certificate presented by the reconnecting channel.
        /// </param>
        /// <param name="token">
        /// The negotiated token, including any owned key-agreement nonces. The caller remains responsible for
        /// cleanup until the handoff succeeds.
        /// </param>
        /// <param name="request">
        /// The decoded OpenSecureChannel renewal request to answer.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the target channel has accepted the transport and token;
        /// <see langword="false"/> when the handoff is rejected.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The requested channel is unknown, cannot accept the negotiated security settings, or rejects the renewal.
        /// </exception>
        bool ReconnectToExistingChannel(
            TcpListenerChannel reconnectingChannel,
            IUaSCByteTransport transport,
            uint requestId,
            uint sequenceNumber,
            uint channelId,
            Certificate clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request);

        /// <summary>
        /// Transfers a reverse connection to a waiting client.
        /// </summary>
        /// <param name="channelId">
        /// The identifier of the listener channel that received the reverse hello message.
        /// </param>
        /// <param name="serverUri">
        /// The server application URI supplied in the reverse hello message.
        /// </param>
        /// <param name="endpointUrl">
        /// The server endpoint URL supplied in the reverse hello message.
        /// </param>
        /// <returns>
        /// A task whose result is <see langword="true"/> when a client accepts the connection, or
        /// <see langword="false"/> when no client accepts it and the listener retains it for another attempt.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The requested listener channel no longer exists.
        /// </exception>
        [Obsolete("Use TransferListenerChannelAsync instead.")]
        Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl);

        /// <summary>
        /// Transfers a reverse connection to a waiting client after draining the listener channel's receive loop.
        /// </summary>
        /// <param name="channelId">
        /// The identifier of the listener channel that received the reverse hello message.
        /// </param>
        /// <param name="serverUri">
        /// The server application URI supplied in the reverse hello message.
        /// </param>
        /// <param name="endpointUrl">
        /// The server endpoint URL supplied in the reverse hello message.
        /// </param>
        /// <returns>
        /// A task whose result is <see langword="true"/> when a client accepts the connection, or
        /// <see langword="false"/> when no client accepts it and the listener retains it for another attempt.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The requested listener channel no longer exists.
        /// </exception>
        Task<bool> TransferListenerChannelAsync(uint channelId, string serverUri, Uri endpointUrl);

        /// <summary>
        /// Notifies the listener to remove and dispose a closed channel.
        /// </summary>
        /// <param name="channelId">
        /// The identifier of the closed channel to remove from the listener.
        /// </param>
        void ChannelClosed(uint channelId);
    }
}
