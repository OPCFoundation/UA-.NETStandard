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
        public UAClient(ApplicationConfiguration configuration, TextWriter writer, Action<IList, IList> validateResponse)
        {
            m_validateResponse = validateResponse;
            m_output = writer;
            m_configuration = configuration;
            m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;
            m_reverseConnectManager = null;
        }

        /// <summary>
        /// Initializes a new instance of the UAClient class for reverse connections.
        /// </summary>
        public UAClient(ApplicationConfiguration configuration, ReverseConnectManager reverseConnectManager, TextWriter writer, Action<IList, IList> validateResponse)
        {
            m_validateResponse = validateResponse;
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
            Utils.SilentDispose(m_session);
            m_configuration.CertificateValidator.CertificateValidation -= CertificateValidation;
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Action used 
        /// </summary>
        Action<IList, IList> ValidateResponse => m_validateResponse;

        /// <summary>
        /// Gets the client session.
        /// </summary>
        public ISession Session => m_session;

        /// <summary>
        /// The session keepalive interval to be used in ms.
        /// </summary>
        public int KeepAliveInterval { get; set; } = 5000;

        /// <summary>
        /// The reconnect period to be used in ms.
        /// </summary>
        public int ReconnectPeriod { get; set; } = 5000;

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
        public bool AutoAccept { get; set; } = false;

        /// <summary>
        /// The file to use for log output.
        /// </summary>
        public string LogFile { get; set; }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a session with the UA server
        /// </summary>
        public async Task<bool> ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken ct = default)
        {
            if (serverUrl == null) throw new ArgumentNullException(nameof(serverUrl));

            try
            {
                if (m_session != null && m_session.Connected == true)
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
                            using (var cts = new CancellationTokenSource(30_000))
                            using (var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token))
                            {
                                connection = await m_reverseConnectManager.WaitForConnection(new Uri(serverUrl), null, linkedCTS.Token).ConfigureAwait(false);
                                if (connection == null)
                                {
                                    throw new ServiceResultException(StatusCodes.BadTimeout, "Waiting for a reverse connection timed out.");
                                }
                                if (endpointDescription == null)
                                {
                                    m_output.WriteLine("Discover reverse connection endpoints....");
                                    endpointDescription = CoreClientUtils.SelectEndpoint(m_configuration, connection, useSecurity);
                                    connection = null;
                                }
                            }
                        } while (connection == null);
                    }
                    else
                    {
                        m_output.WriteLine("Connecting to... {0}", serverUrl);
                        endpointDescription = CoreClientUtils.SelectEndpoint(m_configuration, serverUrl, useSecurity);
                    }

                    // Get the endpoint by connecting to server's discovery endpoint.
                    // Try to find the first endopint with security.
                    EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                    ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

#if NET6_0_OR_GREATER
                    var sessionFactory = TraceableSessionFactory.Instance;
#else
                    var sessionFactory = DefaultSessionFactory.Instance;
#endif

                    // Create the session
                    var session = await sessionFactory.CreateAsync(
                        m_configuration,
                        connection,
                        endpoint,
                        connection == null,
                        false,
                        m_configuration.ApplicationName,
                        SessionLifeTime,
                        UserIdentity,
                        null
                    ).ConfigureAwait(false);

                    // Assign the created session
                    if (session != null && session.Connected)
                    {
                        m_session = session;

                        // override keep alive interval
                        m_session.KeepAliveInterval = KeepAliveInterval;

                        // support transfer
                        m_session.DeleteSubscriptionsOnClose = false;
                        m_session.TransferSubscriptionsOnReconnect = true;

                        // set up keep alive callback.
                        m_session.KeepAlive += Session_KeepAlive;

                        // prepare a reconnect handler
                        m_reconnectHandler = new SessionReconnectHandler(true, ReconnectPeriodExponentialBackoff);
                    }

                    // Session created successfully.
                    m_output.WriteLine("New Session Created with SessionName = {0}", m_session.SessionName);
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
        public void Disconnect()
        {
            try
            {
                if (m_session != null)
                {
                    m_output.WriteLine("Disconnecting...");

                    lock (m_lock)
                    {
                        m_session.KeepAlive -= Session_KeepAlive;
                        m_reconnectHandler?.Dispose();
                        m_reconnectHandler = null;
                    }

                    m_session.Close();
                    m_session.Dispose();
                    m_session = null;

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
                if (!Object.ReferenceEquals(session, m_session))
                {
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    if (ReconnectPeriod <= 0)
                    {
                        Utils.LogWarning("KeepAlive status {0}, but reconnect is disabled.", e.Status);
                        return;
                    }

                    var state = m_reconnectHandler.BeginReconnect(m_session, m_reverseConnectManager, ReconnectPeriod, Client_ReconnectComplete);
                    if (state == SessionReconnectHandler.ReconnectState.Triggered)
                    {
                        Utils.LogInfo("KeepAlive status {0}, reconnect status {1}, reconnect period {2}ms.", e.Status, state, ReconnectPeriod);
                    }
                    else
                    {
                        Utils.LogInfo("KeepAlive status {0}, reconnect status {1}.", e.Status, state);
                    }

                    return;
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
            if (!Object.ReferenceEquals(sender, m_reconnectHandler))
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
                    if (!Object.ReferenceEquals(m_session, m_reconnectHandler.Session))
                    {
                        m_output.WriteLine("--- RECONNECTED TO NEW SESSION --- {0}", m_reconnectHandler.Session.SessionId);
                        var session = m_session;
                        m_session = m_reconnectHandler.Session;
                        Utils.SilentDispose(session);
                    }
                    else
                    {
                        m_output.WriteLine("--- REACTIVATED SESSION --- {0}", m_reconnectHandler.Session.SessionId);
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
        protected virtual void CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
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
                m_output.WriteLine("Untrusted Certificate accepted. Subject = {0}", e.Certificate.Subject);
                e.Accept = true;
            }
            else
            {
                m_output.WriteLine("Untrusted Certificate rejected. Subject = {0}", e.Certificate.Subject);
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ReverseConnectManager m_reverseConnectManager;
        private ApplicationConfiguration m_configuration;
        private SessionReconnectHandler m_reconnectHandler;
        private ISession m_session;
        private readonly TextWriter m_output;
        private readonly Action<IList, IList> m_validateResponse;
        #endregion
    }
}
