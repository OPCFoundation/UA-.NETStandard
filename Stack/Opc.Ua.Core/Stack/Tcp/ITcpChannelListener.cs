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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

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
        [Obsolete("Use TransferListenerChannelAsync instead.")]
        Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl);

        /// <summary>
        /// Used to transfer a reverse connection socket to the client.
        /// </summary>
        Task<bool> TransferListenerChannelAsync(uint channelId, string serverUri, Uri endpointUrl);

        /// <summary>
        /// Called when a channel closes.
        /// </summary>
        void ChannelClosed(uint channelId);
    }
}
