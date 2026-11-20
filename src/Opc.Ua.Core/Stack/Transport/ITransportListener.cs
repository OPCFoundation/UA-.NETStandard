/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

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
    public interface ITransportListener : IAsyncDisposable
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
        /// <remarks>
        /// A listener that implements <see cref="ITransportListenerAcceptControl"/>
        /// only binds its address when
        /// <see cref="TransportListenerSettings.DeferAccept"/> is set.
        /// </remarks>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        ValueTask OpenAsync(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback,
            CancellationToken ct = default);

        /// <summary>
        /// Closes the listener and stops accepting connection.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        ValueTask CloseAsync(CancellationToken ct = default);

        /// <summary>
        /// Updates the application certificate for a listener.
        /// </summary>
        /// <param name="validator">
        /// The peer-certificate validator used by the listener to validate
        /// inbound client certificates.
        /// </param>
        /// <param name="serverCertificates">
        /// The registry that exposes the server's instance certificates and
        /// chain blobs.
        /// </param>
        void CertificateUpdate(
            ICertificateValidatorEx validator,
            ICertificateRegistry serverCertificates);

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
    /// A transport listener that can be opened (bound to its address) without
    /// taking connections yet.
    /// </summary>
    /// <remarks>
    /// When <see cref="TransportListenerSettings.DeferAccept"/> is set, the
    /// listener reserves its address in
    /// <see cref="ITransportListener.OpenAsync"/>, so a taken port still fails
    /// the server start, but serves no client until
    /// <see cref="StartAcceptingAsync"/> is called. A server uses this to keep
    /// clients out until it is fully started. Connections that arrive before
    /// wait (for example in the socket backlog) and are served afterwards.
    /// A listener that does not implement this interface accepts connections
    /// as soon as it is opened.
    /// </remarks>
    public interface ITransportListenerAcceptControl
    {
        /// <summary>
        /// Whether the listener serves connections.
        /// </summary>
        bool IsAccepting { get; }

        /// <summary>
        /// Starts serving connections on a listener opened with
        /// <see cref="TransportListenerSettings.DeferAccept"/>. Does nothing
        /// when the listener already serves connections.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask StartAcceptingAsync(CancellationToken ct = default);
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
