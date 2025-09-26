/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts
{
    /// <summary>
    /// OPC UA Client with examples of basic functionality.
    /// </summary>
    public class UAClient : IUAClient, IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the UAClient class.
        /// </summary>
        public UAClient(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            Action<IList, IList> validateResponse)
            : this(
                configuration,
                null,
                telemetry,
                validateResponse)
        {
        }

        /// <summary>
        /// Initializes a new instance of the UAClient class for reverse connections.
        /// </summary>
        public UAClient(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ITelemetryContext telemetry,
            Action<IList, IList> validateResponse)
        {
            ValidateResponse = validateResponse;
            m_logger = telemetry.CreateLogger<UAClient>();
            m_telemetry = telemetry;
            m_configuration = configuration;
            m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;
            m_reverseConnectManager = reverseConnectManager;
        }

        /// <summary>
        /// Dispose objects.
        /// </summary>
        public void Dispose()
        {
            m_disposed = true;
            Utils.SilentDispose(Session);
            m_configuration.CertificateValidator.CertificateValidation -= CertificateValidation;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Action used
        /// </summary>
        internal Action<IList, IList> ValidateResponse { get; }

        /// <summary>
        /// Gets the client session.
        /// </summary>
        public ISession Session { get; private set; }

        /// <summary>
        /// The session keepalive interval to be used in ms.
        /// </summary>
        public int KeepAliveInterval { get; set; } = 5000;

        /// <summary>
        /// The reconnect period to be used in ms.
        /// </summary>
        public int ReconnectPeriod { get; set; } = 1000;

        /// <summary>
        /// The reconnect period exponential backoff to be used in ms.
        /// </summary>
        public int ReconnectPeriodExponentialBackoff { get; set; } = 15000;

        /// <summary>
        /// The session lifetime.
        /// </summary>
        public uint SessionLifeTime { get; set; } = 60 * 1000;

        /// <summary>
        /// The user identity to use to connect to the server.
        /// </summary>
        public IUserIdentity UserIdentity { get; set; } = new UserIdentity();

        /// <summary>
        /// Auto accept untrusted certificates.
        /// </summary>
        public bool AutoAccept { get; set; }

        /// <summary>
        /// The file to use for log output.
        /// </summary>
        public string LogFile { get; set; }

        /// <summary>
        /// Do a Durable Subscription Transfer
        /// </summary>
        public async Task<bool> DurableSubscriptionTransferAsync(
            string serverUrl,
            bool useSecurity = true,
            CancellationToken ct = default)
        {
            bool success = false;
            SubscriptionCollection subscriptions = [.. Session.Subscriptions];
            Session = null;
            if (await ConnectAsync(serverUrl, useSecurity, ct).ConfigureAwait(false) &&
                subscriptions != null &&
                Session != null)
            {
                m_logger.LogInformation(
                    "Transferring {Count} subscriptions from old session to new session...",
                    subscriptions.Count);
                success = await Session.TransferSubscriptionsAsync(
                    subscriptions,
                    true,
                    ct).ConfigureAwait(false);
                if (success)
                {
                    m_logger.LogInformation("Subscriptions transferred.");
                }
            }

            return success;
        }

        /// <summary>
        /// Creates a session with the UA server
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="ArgumentNullException"><paramref name="serverUrl"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<bool> ConnectAsync(
            string serverUrl,
            bool useSecurity = true,
            CancellationToken ct = default)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(UAClient));
            }

            if (serverUrl == null)
            {
                throw new ArgumentNullException(nameof(serverUrl));
            }

            try
            {
                if (Session != null && Session.Connected)
                {
                    m_logger.LogInformation("Session already connected!");
                }
                else
                {
                    ITransportWaitingConnection connection = null;
                    EndpointDescription endpointDescription = null;
                    if (m_reverseConnectManager != null)
                    {
                        m_logger.LogInformation("Waiting for reverse connection to.... {Url}", serverUrl);
                        do
                        {
                            using var cts = new CancellationTokenSource(30_000);
                            using var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(
                                ct,
                                cts.Token);
                            connection = await m_reverseConnectManager
                                .WaitForConnectionAsync(new Uri(serverUrl), null, linkedCTS.Token)
                                .ConfigureAwait(false);
                            if (connection == null)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadTimeout,
                                    "Waiting for a reverse connection timed out."
                                );
                            }
                            if (endpointDescription == null)
                            {
                                m_logger.LogInformation("Discover reverse connection endpoints....");
                                endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                                    m_configuration,
                                    connection,
                                    useSecurity,
                                    m_telemetry,
                                    ct
                                ).ConfigureAwait(false);
                                connection = null;
                            }
                        } while (connection == null);
                    }
                    else
                    {
                        m_logger.LogInformation("Connecting to... {Url}", serverUrl);
                        endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                            m_configuration,
                            serverUrl,
                            useSecurity,
                            m_telemetry,
                            ct).ConfigureAwait(false);
                    }

                    // Get the endpoint by connecting to server's discovery endpoint.
                    // Try to find the first endopint with security.
                    var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        endpointDescription,
                        endpointConfiguration);

                    // Create the session factory. - we could take it as parameter or as member
                    var sessionFactory = new TraceableSessionFactory(m_telemetry);

                    // Create the session
                    ISession session = await sessionFactory
                        .CreateAsync(
                            m_configuration,
                            connection,
                            endpoint,
                            connection == null,
                            false,
                            m_configuration.ApplicationName,
                            SessionLifeTime,
                            UserIdentity,
                            null,
                            ct)
                        .ConfigureAwait(false);

                    // Assign the created session
                    if (session != null && session.Connected)
                    {
                        Session = session;

                        // override keep alive interval
                        Session.KeepAliveInterval = KeepAliveInterval;

                        // support transfer
                        Session.DeleteSubscriptionsOnClose = false;
                        Session.TransferSubscriptionsOnReconnect = true;

                        // set up keep alive callback.
                        Session.KeepAlive += Session_KeepAlive;

                        // prepare a reconnect handler
                        m_reconnectHandler = new SessionReconnectHandler(
                            m_telemetry,
                            true,
                            ReconnectPeriodExponentialBackoff);
                    }

                    // Session created successfully.
                    m_logger.LogInformation(
                        "New Session Created with SessionName = {SessionName}",
                        Session.SessionName);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Log Error
                m_logger.LogInformation("Create Session Error : {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Disconnects the session.
        /// </summary>
        /// <param name="leaveChannelOpen">Leaves the channel open.</param>
        public async Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken ct = default)
        {
            try
            {
                if (Session != null)
                {
                    m_logger.LogInformation("Disconnecting...");

                    lock (m_lock)
                    {
                        Session.KeepAlive -= Session_KeepAlive;
                        m_reconnectHandler?.Dispose();
                        m_reconnectHandler = null;
                    }

                    await Session.CloseAsync(!leaveChannelOpen, ct).ConfigureAwait(false);
                    if (leaveChannelOpen)
                    {
                        // detach the channel, so it doesn't get
                        // closed when the session is disposed.
                        Session.DetachChannel();
                    }
                    Session.Dispose();
                    Session = null;

                    // Log Session Disconnected event
                    m_logger.LogInformation("Session Disconnected.");
                }
                else
                {
                    m_logger.LogInformation("Session not created!");
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_logger.LogError(ex, "Disconnect Error");
            }
        }

        /// <summary>
        /// Handles a keep alive event from a session and triggers a reconnect if necessary.
        /// </summary>
        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            try
            {
                // check for events from discarded sessions.
                if (Session == null || !Session.Equals(session))
                {
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    if (ReconnectPeriod <= 0)
                    {
                        m_logger.LogWarning(
                            "KeepAlive status {StatusCode}, but reconnect is disabled.",
                            e.Status);
                        return;
                    }

                    SessionReconnectHandler.ReconnectState state = m_reconnectHandler
                        .BeginReconnect(
                            Session,
                            m_reverseConnectManager,
                            ReconnectPeriod,
                            Client_ReconnectComplete
                            );
                    if (state == SessionReconnectHandler.ReconnectState.Triggered)
                    {
                        m_logger.LogInformation(
                            "KeepAlive status {StatusCode}, reconnect status {State}, reconnect period {ReconnectPeriod}ms.",
                            e.Status,
                            state,
                            ReconnectPeriod
                        );
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "KeepAlive status {StatusCode}, reconnect status {State}.",
                            e.Status,
                            state);
                    }

                    // cancel sending a new keep alive request, because reconnect is triggered.
                    e.CancelKeepAlive = true;
                }
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Error in OnKeepAlive.");
            }
        }

        /// <summary>
        /// Called when the reconnect attempt was successful.
        /// </summary>
        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!ReferenceEquals(sender, m_reconnectHandler))
            {
                return;
            }

            lock (m_lock)
            {
                // if session recovered, Session property is null
                if (m_reconnectHandler.Session != null)
                {
                    // ensure only a new instance is disposed
                    // after reactivate, the same session instance may be returned
                    if (!ReferenceEquals(Session, m_reconnectHandler.Session))
                    {
                        m_logger.LogInformation(
                            "--- RECONNECTED TO NEW SESSION --- {SessionId}",
                            m_reconnectHandler.Session.SessionId
                        );
                        ISession session = Session;
                        Session = m_reconnectHandler.Session;
                        Utils.SilentDispose(session);
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "--- REACTIVATED SESSION --- {SessionId}",
                            m_reconnectHandler.Session.SessionId);
                    }
                }
                else
                {
                    m_logger.LogInformation("--- RECONNECT KeepAlive recovered ---");
                }
            }
        }

        /// <summary>
        /// Handles the certificate validation event.
        /// This event is triggered every time an untrusted certificate is received from the server.
        /// </summary>
        protected virtual void CertificateValidation(
            CertificateValidator sender,
            CertificateValidationEventArgs e)
        {
            bool certificateAccepted = false;

            // ****
            // Implement a custom logic to decide if the certificate should be
            // accepted or not and set certificateAccepted flag accordingly.
            // The certificate can be retrieved from the e.Certificate field
            // ***

            ServiceResult error = e.Error;
            m_logger.LogInformation("{Error}", error);
            if (error.StatusCode == StatusCodes.BadCertificateUntrusted && AutoAccept)
            {
                certificateAccepted = true;
            }

            if (certificateAccepted)
            {
                m_logger.LogInformation(
                    "Untrusted Certificate accepted. Subject = {Subject}",
                    e.Certificate.Subject);
                e.Accept = true;
            }
            else
            {
                m_logger.LogInformation(
                    "Untrusted Certificate rejected. Subject = {Subject}",
                    e.Certificate.Subject);
            }
        }

        private readonly Lock m_lock = new();
        private readonly ReverseConnectManager m_reverseConnectManager;
        private readonly ApplicationConfiguration m_configuration;
        private SessionReconnectHandler m_reconnectHandler;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private bool m_disposed;
    }
}
