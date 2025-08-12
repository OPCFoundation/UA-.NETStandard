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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts
{
    /// <summary>
    /// OPC UA Client with examples of basic functionality.
    /// </summary>
    public class UAClient : IUAClient, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the UAClient class.
        /// </summary>
        public UAClient(
            ApplicationConfiguration configuration,
            TextWriter writer,
            Action<IList, IList> validateResponse
        )
        {
            ValidateResponse = validateResponse;
            m_output = writer;
            m_configuration = configuration;
            m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;
            m_reverseConnectManager = null;
        }

        /// <summary>
        /// Initializes a new instance of the UAClient class for reverse connections.
        /// </summary>
        public UAClient(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            TextWriter writer,
            Action<IList, IList> validateResponse
        )
        {
            ValidateResponse = validateResponse;
            m_output = writer;
            m_configuration = configuration;
            m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;
            m_reverseConnectManager = reverseConnectManager;
        }
        #endregion

        #region IDisposable
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
        #endregion

        #region Public Properties
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
        #endregion

        #region Public Methods
        /// <summary>
        /// Do a Durable Subscription Transfer
        /// </summary>
        public async Task<bool> DurableSubscriptionTransferAsync(
            string serverUrl,
            bool useSecurity = true,
            CancellationToken ct = default
        )
        {
            bool success = false;
            SubscriptionCollection subscriptions = [.. Session.Subscriptions];
            Session = null;
            if (
                await ConnectAsync(serverUrl, useSecurity, ct).ConfigureAwait(false) &&
                subscriptions != null &&
                Session != null
            )
            {
                m_output.WriteLine(
                    "Transferring " +
                    subscriptions.Count.ToString(CultureInfo.InvariantCulture) +
                    " subscriptions from old session to new session..."
                );
                success = Session.TransferSubscriptions(subscriptions, true);
                if (success)
                {
                    m_output.WriteLine("Subscriptions transferred.");
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
                    m_output.WriteLine("Session already connected!");
                }
                else
                {
                    ITransportWaitingConnection connection = null;
                    EndpointDescription endpointDescription = null;
                    if (m_reverseConnectManager != null)
                    {
                        m_output.WriteLine("Waiting for reverse connection to.... {0}", serverUrl);
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
                                m_output.WriteLine("Discover reverse connection endpoints....");
                                endpointDescription = CoreClientUtils.SelectEndpoint(
                                    m_configuration,
                                    connection,
                                    useSecurity
                                );
                                connection = null;
                            }
                        } while (connection == null);
                    }
                    else
                    {
                        m_output.WriteLine("Connecting to... {0}", serverUrl);
                        endpointDescription = CoreClientUtils.SelectEndpoint(
                            m_configuration,
                            serverUrl,
                            useSecurity);
                    }

                    // Get the endpoint by connecting to server's discovery endpoint.
                    // Try to find the first endopint with security.
                    var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        endpointDescription,
                        endpointConfiguration);

                    TraceableSessionFactory sessionFactory = TraceableSessionFactory.Instance;

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
                            ct
                        )
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
                            true,
                            ReconnectPeriodExponentialBackoff);
                    }

                    // Session created successfully.
                    m_output.WriteLine(
                        "New Session Created with SessionName = {0}",
                        Session.SessionName);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine("Create Session Error : {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Disconnects the session.
        /// </summary>
        /// <param name="leaveChannelOpen">Leaves the channel open.</param>
        public void Disconnect(bool leaveChannelOpen = false)
        {
            try
            {
                if (Session != null)
                {
                    m_output.WriteLine("Disconnecting...");

                    lock (m_lock)
                    {
                        Session.KeepAlive -= Session_KeepAlive;
                        m_reconnectHandler?.Dispose();
                        m_reconnectHandler = null;
                    }

                    Session.Close(!leaveChannelOpen);
                    if (leaveChannelOpen)
                    {
                        // detach the channel, so it doesn't get closed when the session is disposed.
                        Session.DetachChannel();
                    }
                    Session.Dispose();
                    Session = null;

                    // Log Session Disconnected event
                    m_output.WriteLine("Session Disconnected.");
                }
                else
                {
                    m_output.WriteLine("Session not created!");
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine($"Disconnect Error : {ex.Message}");
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
                        Utils.LogWarning(
                            "KeepAlive status {0}, but reconnect is disabled.",
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
                        Utils.LogInfo(
                            "KeepAlive status {0}, reconnect status {1}, reconnect period {2}ms.",
                            e.Status,
                            state,
                            ReconnectPeriod
                        );
                    }
                    else
                    {
                        Utils.LogInfo(
                            "KeepAlive status {0}, reconnect status {1}.",
                            e.Status,
                            state);
                    }

                    // cancel sending a new keep alive request, because reconnect is triggered.
                    e.CancelKeepAlive = true;
                }
            }
            catch (Exception exception)
            {
                Utils.LogError(exception, "Error in OnKeepAlive.");
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
                        m_output.WriteLine(
                            "--- RECONNECTED TO NEW SESSION --- {0}",
                            m_reconnectHandler.Session.SessionId
                        );
                        ISession session = Session;
                        Session = m_reconnectHandler.Session;
                        Utils.SilentDispose(session);
                    }
                    else
                    {
                        m_output.WriteLine(
                            "--- REACTIVATED SESSION --- {0}",
                            m_reconnectHandler.Session.SessionId);
                    }
                }
                else
                {
                    m_output.WriteLine("--- RECONNECT KeepAlive recovered ---");
                }
            }
        }
        #endregion

        #region Protected Methods
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
            m_output.WriteLine(error);
            if (error.StatusCode == StatusCodes.BadCertificateUntrusted && AutoAccept)
            {
                certificateAccepted = true;
            }

            if (certificateAccepted)
            {
                m_output.WriteLine(
                    "Untrusted Certificate accepted. Subject = {0}",
                    e.Certificate.Subject);
                e.Accept = true;
            }
            else
            {
                m_output.WriteLine(
                    "Untrusted Certificate rejected. Subject = {0}",
                    e.Certificate.Subject);
            }
        }
        #endregion

        #region Private Fields
        private readonly Lock m_lock = new();
        private readonly ReverseConnectManager m_reverseConnectManager;
        private readonly ApplicationConfiguration m_configuration;
        private SessionReconnectHandler m_reconnectHandler;
        private readonly TextWriter m_output;
        private bool m_disposed;
        #endregion
    }
}
