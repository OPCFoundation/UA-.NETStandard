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
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// This is an interface to a listener which supports UA binary encoding.
    /// </summary>
    public interface ITransportListener : IDisposable
    {
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
            ITransportListenerCallback callback
            );

        /// <summary>
        /// Closes the listener and stops accepting connection.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        void Close();

        /// <summary>
        /// Updates the application certificate for a listener.
        /// </summary>
        void CertificateUpdate(
            ICertificateValidator validator,
            X509Certificate2 serverCertificate,
            X509Certificate2Collection serverCertificateChain);

        /// <summary>
        /// Raised when a new connection is waiting for a client.
        /// </summary>
        event EventHandler<ConnectionWaitingEventArgs> ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
        event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// Creates a reverse connection to a client. 
        /// </summary>
        void CreateReverseConnection(Uri url, int timeout);

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
