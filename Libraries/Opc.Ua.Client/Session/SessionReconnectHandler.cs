/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redaction;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Attempts to reconnect to the server.
    /// </summary>
    public class SessionReconnectHandler : IDisposable
    {
        /// <summary>
        /// The minimum reconnect period in ms.
        /// </summary>
        public const int MinReconnectPeriod = 500;

        /// <summary>
        /// The maximum reconnect period in ms.
        /// </summary>
        public const int MaxReconnectPeriod = 30000;

        /// <summary>
        /// The default reconnect period in ms.
        /// </summary>
        public const int DefaultReconnectPeriod = 1000;

        /// <summary>
        /// The default reconnect operation timeout in ms.
        /// </summary>
        public const int MinReconnectOperationTimeout = 5000;

        /// <summary>
        /// The internal state of the reconnect handler.
        /// </summary>
        public enum ReconnectState
        {
            /// <summary>
            /// The reconnect handler is ready to start the reconnect timer.
            /// </summary>
            Ready = 0,

            /// <summary>
            /// The reconnect timer is triggered and waiting to reconnect.
            /// </summary>
            Triggered = 1,

            /// <summary>
            /// The reconnection is in progress.
            /// </summary>
            Reconnecting = 2,

            /// <summary>
            /// The reconnect handler is disposed and can not be used for further reconnect attempts.
            /// </summary>
            Disposed = 4
        }

        /// <summary>
        /// Create a reconnect handler.
        /// </summary>
        /// <param name="reconnectAbort">Set to <c>true</c> to allow reconnect abort if keep alive recovered.</param>
        /// <param name="maxReconnectPeriod">
        ///     The upper limit for the reconnect period after exponential backoff.
        ///     -1 (default) indicates that no exponential backoff should be used.
        /// </param>
        public SessionReconnectHandler(bool reconnectAbort = false, int maxReconnectPeriod = -1)
        {
            m_reconnectAbort = reconnectAbort;
            m_reconnectTimer = new Timer(
                OnReconnectAsync,
                this,
                Timeout.Infinite,
                Timeout.Infinite);
            m_state = ReconnectState.Ready;
            m_cancelReconnect = false;
            m_updateFromServer = false;
            m_baseReconnectPeriod = DefaultReconnectPeriod;
            m_maxReconnectPeriod =
                maxReconnectPeriod < 0
                    ? -1
                    : Math.Max(
                        MinReconnectPeriod,
                        Math.Min(maxReconnectPeriod, MaxReconnectPeriod));
            m_random = new Random();
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_lock)
                {
                    if (m_reconnectTimer != null)
                    {
                        m_reconnectTimer.Dispose();
                        m_reconnectTimer = null;
                    }
                    m_state = ReconnectState.Disposed;
                }
            }
        }

        /// <summary>
        /// Gets the session managed by the handler.
        /// </summary>
        /// <value>The session.</value>
        public ISession Session { get; private set; }

        /// <summary>
        /// The internal state of the reconnect handler.
        /// </summary>
        public ReconnectState State
        {
            get
            {
                lock (m_lock)
                {
                    if (m_reconnectTimer == null)
                    {
                        return ReconnectState.Disposed;
                    }
                    return m_state;
                }
            }
        }

        /// <summary>
        /// Cancel a reconnect in progress.
        /// </summary>
        public void CancelReconnect()
        {
            lock (m_lock)
            {
                if (m_reconnectTimer == null)
                {
                    return;
                }

                if (m_state == ReconnectState.Triggered)
                {
                    Session = null;
                    EnterReadyState();
                    return;
                }

                m_cancelReconnect = true;
            }
        }

        /// <summary>
        /// Begins the reconnect process.
        /// </summary>
        public ReconnectState BeginReconnect(
            ISession session,
            int reconnectPeriod,
            EventHandler callback)
        {
            return BeginReconnect(session, null, reconnectPeriod, callback);
        }

        /// <summary>
        /// Begins the reconnect process using a reverse connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ReconnectState BeginReconnect(
            ISession session,
            ReverseConnectManager reverseConnectManager,
            int reconnectPeriod,
            EventHandler callback)
        {
            lock (m_lock)
            {
                if (m_reconnectTimer == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }

                // cancel reconnect requested, if possible
                if (session == null)
                {
                    if (m_state == ReconnectState.Triggered)
                    {
                        Session = null;
                        EnterReadyState();
                        return m_state;
                    }
                    // reconnect already in progress, schedule cancel
                    m_cancelReconnect = true;
                    return m_state;
                }

                // set reconnect period within boundaries
                reconnectPeriod = CheckedReconnectPeriod(reconnectPeriod);

                // ignore subsequent trigger requests
                if (m_state == ReconnectState.Ready)
                {
                    Session = session;
                    m_baseReconnectPeriod = reconnectPeriod;
                    m_reconnectFailed = false;
                    m_cancelReconnect = false;
                    m_callback = callback;
                    m_reverseConnectManager = reverseConnectManager;
                    m_reconnectTimer.Change(
                        JitteredReconnectPeriod(reconnectPeriod),
                        Timeout.Infinite);
                    m_reconnectPeriod = CheckedReconnectPeriod(reconnectPeriod, true);
                    m_state = ReconnectState.Triggered;
                    return m_state;
                }

                // if triggered, reset timer only if requested reconnect period is shorter
                if (m_state == ReconnectState.Triggered && reconnectPeriod < m_baseReconnectPeriod)
                {
                    m_baseReconnectPeriod = reconnectPeriod;
                    m_reconnectTimer.Change(
                        JitteredReconnectPeriod(reconnectPeriod),
                        Timeout.Infinite);
                    m_reconnectPeriod = CheckedReconnectPeriod(reconnectPeriod, true);
                }

                return m_state;
            }
        }

        /// <summary>
        /// Returns the reconnect period with a random jitter.
        /// </summary>
        public virtual int JitteredReconnectPeriod(int reconnectPeriod)
        {
            // The factors result in a jitter of 10%.
            const int jitterResolution = 1000;
            const int jitterFactor = 10;
            int jitter =
                reconnectPeriod *
                m_random.Next(-jitterResolution, jitterResolution) /
                (jitterResolution * jitterFactor);
            return reconnectPeriod + jitter;
        }

        /// <summary>
        /// Returns the reconnect period within the min and max boundaries.
        /// </summary>
        public virtual int CheckedReconnectPeriod(
            int reconnectPeriod,
            bool exponentialBackoff = false)
        {
            // exponential backoff is controlled by m_maxReconnectPeriod
            if (m_maxReconnectPeriod > MinReconnectPeriod)
            {
                if (exponentialBackoff)
                {
                    reconnectPeriod *= 2;
                }
                return Math.Min(
                    Math.Max(reconnectPeriod, MinReconnectPeriod),
                    m_maxReconnectPeriod);
            }

            return Math.Max(reconnectPeriod, MinReconnectPeriod);
        }

        /// <summary>
        /// Called when the reconnect timer expires.
        /// </summary>
        private async void OnReconnectAsync(object state)
        {
            int reconnectStart = HiResClock.TickCount;
            try
            {
                // check for exit.
                lock (m_lock)
                {
                    if (m_reconnectTimer == null || Session == null)
                    {
                        return;
                    }
                    if (m_state != ReconnectState.Triggered)
                    {
                        return;
                    }
                    // enter reconnecting state
                    m_state = ReconnectState.Reconnecting;
                }

                bool keepaliveRecovered = false;

                // preserve legacy behavior if reconnectAbort is not set
                if (Session != null &&
                    m_reconnectAbort &&
                    Session.Connected &&
                    !Session.KeepAliveStopped)
                {
                    keepaliveRecovered = true;
                    // breaking change, the callback must only assign the new
                    // session if the property is != null
                    Utils.LogInfo(
                        "Reconnect {0} aborted, KeepAlive recovered.",
                        Session?.SessionId);
                    Session = null;
                }
                else
                {
                    Utils.LogInfo("Reconnect {0}.", Session?.SessionId);
                }

                // do the reconnect or recover state.
                if (keepaliveRecovered || await DoReconnectAsync().ConfigureAwait(false))
                {
                    lock (m_lock)
                    {
                        EnterReadyState();
                    }

                    // notify the caller.
                    m_callback(this, null);

                    return;
                }
            }
            catch (Exception exception)
            {
                Utils.LogError("Unexpected error during reconnect: {0}", Redact.Create(exception));
            }

            // schedule the next reconnect.
            lock (m_lock)
            {
                if (m_state != ReconnectState.Disposed)
                {
                    if (m_cancelReconnect)
                    {
                        EnterReadyState();
                    }
                    else
                    {
                        int elapsed = HiResClock.TickCount - reconnectStart;
                        Utils.LogInfo(
                            "Reconnect period is {0} ms, {1} ms elapsed in reconnect.",
                            m_reconnectPeriod,
                            elapsed);
                        int adjustedReconnectPeriod = CheckedReconnectPeriod(
                            m_reconnectPeriod - elapsed);
                        adjustedReconnectPeriod = JitteredReconnectPeriod(adjustedReconnectPeriod);
                        m_reconnectTimer.Change(adjustedReconnectPeriod, Timeout.Infinite);
                        Utils.LogInfo(
                            "Next adjusted reconnect scheduled in {0} ms.",
                            adjustedReconnectPeriod);
                        m_reconnectPeriod = CheckedReconnectPeriod(m_reconnectPeriod, true);
                        m_state = ReconnectState.Triggered;
                    }
                }
            }
        }

        /// <summary>
        /// Reconnects to the server.
        /// </summary>
        private async Task<bool> DoReconnectAsync()
        {
            // helper to override operation timeout
            ITransportChannel transportChannel = null;

            // try a reconnect.
            if (!m_reconnectFailed)
            {
                try
                {
                    if (m_reverseConnectManager != null)
                    {
                        ITransportWaitingConnection connection = await m_reverseConnectManager
                            .WaitForConnectionAsync(
                                new Uri(Session.Endpoint.EndpointUrl),
                                Session.Endpoint.Server.ApplicationUri)
                            .ConfigureAwait(false);

                        await Session.ReconnectAsync(connection).ConfigureAwait(false);
                    }
                    else
                    {
                        await Session.ReconnectAsync().ConfigureAwait(false);
                    }

                    // monitored items should start updating on their own.
                    return true;
                }
                catch (Exception exception)
                {
                    // recreate the session if it has been closed.
                    if (exception is ServiceResultException sre)
                    {
                        Utils.LogWarning("Reconnect failed. Reason={0}.", sre.Result);

                        // check if the server endpoint could not be reached.
                        if (sre.StatusCode
                            is StatusCodes.BadTcpInternalError
                                or StatusCodes.BadCommunicationError
                                or StatusCodes.BadNotConnected
                                or StatusCodes.BadRequestTimeout
                                or StatusCodes.BadTimeout
                                or StatusCodes.BadNoCommunication
                                or StatusCodes.BadConnectionClosed)
                        {
                            // check if reactivating is still an option.
                            int timeout =
                                Convert.ToInt32(Session.SessionTimeout) -
                                (HiResClock.TickCount - Session.LastKeepAliveTickCount);
                            if (timeout > 0)
                            {
                                Utils.LogInfo(
                                    "Retry to reactivate, est. session timeout in {0} ms.",
                                    timeout);
                                return false;
                            }
                        }

                        // check if the security configuration may have changed
                        if (sre.StatusCode is StatusCodes.BadSecurityChecksFailed or StatusCodes
                            .BadCertificateInvalid)
                        {
                            m_updateFromServer = true;
                            Utils.LogInfo(
                                "Reconnect failed due to security check. Request endpoint update from server. {0}",
                                sre.Message);
                        }
                        // recreate session immediately, use existing channel
                        else if (sre.StatusCode == StatusCodes.BadSessionIdInvalid)
                        {
                            transportChannel = Session.NullableTransportChannel;
                            Session.DetachChannel();
                        }
                        else
                        {
                            // wait for next scheduled reconnect if connection failed,
                            // next attempt is to recreate session
                            m_reconnectFailed = true;
                            return false;
                        }
                    }
                    else
                    {
                        Utils.LogError(exception, "Reconnect failed.");
                    }

                    m_reconnectFailed = true;
                }
            }

            // re-create the session.
            try
            {
                ISession session;
                if (m_reverseConnectManager != null)
                {
                    ITransportWaitingConnection connection;
                    do
                    {
                        EndpointDescription endpointDescription =
                            Session.Endpoint ?? transportChannel.EndpointDescription;

                        connection = await m_reverseConnectManager
                            .WaitForConnectionAsync(
                                new Uri(endpointDescription.EndpointUrl),
                                endpointDescription.Server.ApplicationUri)
                            .ConfigureAwait(false);

                        if (m_updateFromServer)
                        {
                            ConfiguredEndpoint endpoint = Session.ConfiguredEndpoint;
                            await endpoint
                                .UpdateFromServerAsync(
                                    endpoint.EndpointUrl,
                                    connection,
                                    endpoint.Description.SecurityMode,
                                    endpoint.Description.SecurityPolicyUri)
                                .ConfigureAwait(false);
                            m_updateFromServer = false;
                            connection = null;
                        }
                    } while (connection == null);

                    session = await Session.SessionFactory.RecreateAsync(Session, connection)
                        .ConfigureAwait(false);
                }
                else
                {
                    if (m_updateFromServer)
                    {
                        ConfiguredEndpoint endpoint = Session.ConfiguredEndpoint;
                        await endpoint
                            .UpdateFromServerAsync(
                                endpoint.EndpointUrl,
                                endpoint.Description.SecurityMode,
                                endpoint.Description.SecurityPolicyUri)
                            .ConfigureAwait(false);
                        m_updateFromServer = false;
                    }

                    session = await Session
                        .SessionFactory.RecreateAsync(Session, transportChannel)
                        .ConfigureAwait(false);
                }
                // note: the template session is not connected at this point
                //       and must be disposed by the owner
                Session = session;
                return true;
            }
            catch (ServiceResultException sre)
            {
                if (sre.InnerResult?.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    sre.InnerResult?.StatusCode == StatusCodes.BadSecurityChecksFailed ||
                    sre.InnerResult?.StatusCode == StatusCodes.BadCertificateInvalid)
                {
                    // schedule a fast endpoint update and retry
                    if (m_maxReconnectPeriod > MinReconnectPeriod &&
                        m_reconnectPeriod >= m_maxReconnectPeriod)
                    {
                        m_reconnectPeriod = m_baseReconnectPeriod;
                    }
                    Utils.LogError(
                        "Could not reconnect due to failed security check. Request endpoint update from server. {0}",
                        Redact.Create(sre));
                }
                else
                {
                    Utils.LogError(
                        "Could not reconnect the Session. Request endpoint update from server. {0}",
                        Redact.Create(sre));
                }
                m_updateFromServer = true;
                return false;
            }
            catch (Exception exception)
            {
                Utils.LogError("Could not reconnect the Session. {0}", Redact.Create(exception));
                return false;
            }
        }

        /// <summary>
        /// Reset the timer and enter ready state.
        /// </summary>
        private void EnterReadyState()
        {
            m_reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
            m_state = ReconnectState.Ready;
            m_cancelReconnect = false;
            m_updateFromServer = false;
        }

        private readonly Lock m_lock = new();
        private ReconnectState m_state;
        private readonly Random m_random;
        private bool m_reconnectFailed;
        private readonly bool m_reconnectAbort;
        private bool m_cancelReconnect;
        private bool m_updateFromServer;
        private int m_reconnectPeriod;
        private int m_baseReconnectPeriod;
        private readonly int m_maxReconnectPeriod;
        private Timer m_reconnectTimer;
        private EventHandler m_callback;
        private ReverseConnectManager m_reverseConnectManager;
    }
}
