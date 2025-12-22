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
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The delegate for the async connection waiting handler.
    /// </summary>
    public delegate Task ConnectionWaitingHandlerAsync(
        object sender,
        ConnectionWaitingEventArgs args);

    /// <summary>
    /// This is an interface to a listener which supports UA binary encoding.
    /// </summary>
    public interface ITransportListener : IDisposable
    {
        /// <summary>
        /// The Id of the transport listener.
        /// </summary>
        string ListenerId { get; }

        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        string UriScheme { get; }

        /// <summary>
        /// Opens the listener and starts accepting connection.
        /// </summary>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        void Open(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback);

        /// <summary>
        /// Closes the listener and stops accepting connection.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        void Close();

        /// <summary>
        /// Closes all connections of the listener.
        /// </summary>
        void CloseAllChannels(string reason);

        /// <summary>
        /// Updates the application certificate for a listener.
        /// </summary>
        void CertificateUpdate(
            ICertificateValidator validator,
            CertificateTypesProvider serverCertificateTypes);

        /// <summary>
        /// Raised when a new connection is waiting for a client.
        /// </summary>
        event ConnectionWaitingHandlerAsync ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
        event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// Creates a reverse connection to a client.
        /// </summary>
        void CreateReverseConnection(Uri url, int timeout);

        /// <summary>
        /// Updates the last active time of a global channel to defer
        /// clean up based on channel timeout.
        /// </summary>
        /// <param name="globalChannelId">The global channel id</param>
        void UpdateChannelLastActiveTime(string globalChannelId);
    }

    /// <summary>
    /// The arguments passed to the ConnectionWaiting event.
    /// </summary>
    /// <remarks>
    /// An object which implements this interface can be used
    /// to create a session using the reverse connect handshake.
    /// </remarks>
    public interface ITransportWaitingConnection
    {
        /// <summary>
        /// The application Uri of the server in the
        /// reverse hello message.
        /// </summary>
        string ServerUri { get; }

        /// <summary>
        /// The endpoint of the server in the
        /// reverse hello message.
        /// </summary>
        Uri EndpointUrl { get; }

        /// <summary>
        /// A handle to a message socket that can be used
        /// to connect to the server.
        /// </summary>
        object Handle { get; }
    }
}
