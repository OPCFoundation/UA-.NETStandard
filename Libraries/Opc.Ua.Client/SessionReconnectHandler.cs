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
using System.Threading;
using System.Threading.Tasks;

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
        /// The default reconnect period in ms.
        /// </summary>
        public const int DefaultReconnectPeriod = 5000;

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
        };

        /// <summary>
        /// Create a reconnect handler.
        /// </summary>
        /// <param name="reconnectAbort">Set to <c>true</c> to allow reconnect abort if keep alive recovered.</param>
        public SessionReconnectHandler(bool reconnectAbort = false)
        {
            m_reconnectAbort = reconnectAbort;
            m_reconnectTimer = new Timer(OnReconnect, this, Timeout.Infinite, Timeout.Infinite);
            m_state = ReconnectState.Ready;
            m_cancelReconnect = false;
        }

        #region IDisposable Members
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
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets the session managed by the handler.
        /// </summary>
        /// <value>The session.</value>
        public ISession Session => m_session;

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
                    m_session = null;
                    EnterReadyState();
                    return;
                }

                m_cancelReconnect= true;
            }
        }

        /// <summary>
        /// Begins the reconnect process.
        /// </summary>
        public ReconnectState BeginReconnect(ISession session, int reconnectPeriod, EventHandler callback)
        {
            return BeginReconnect(session, null, reconnectPeriod, callback);
        }

        /// <summary>
        /// Begins the reconnect process using a reverse connection.
        /// </summary>
        public ReconnectState BeginReconnect(ISession session, ReverseConnectManager reverseConnectManager, int reconnectPeriod, EventHandler callback)
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
                        m_session = null;
                        EnterReadyState();
                        return m_state;
                    }
                    // reconnect already in progress, schedule cancel
                    m_cancelReconnect = true;
                    return m_state;
                }

                // ignore subsequent trigger requests
                if (m_state == ReconnectState.Ready)
                {
                    m_session = session;
                    m_reconnectFailed = false;
                    m_cancelReconnect = false;
                    m_reconnectPeriod = reconnectPeriod;
                    m_callback = callback;
                    m_reverseConnectManager = reverseConnectManager;
                    if (reconnectPeriod < MinReconnectPeriod)
                    {
                        m_reconnectPeriod = MinReconnectPeriod;
                    }
                    m_reconnectTimer.Change(reconnectPeriod, Timeout.Infinite);
                    m_state = ReconnectState.Triggered;
                    return m_state;
                }

                // override reconnect period
                m_reconnectPeriod = reconnectPeriod;

                return m_state;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Called when the reconnect timer expires.
        /// </summary>
        private async void OnReconnect(object state)
        {
            DateTime reconnectStart = DateTime.UtcNow;
            try
            {
                // check for exit.
                lock (m_lock)
                {
                    if (m_reconnectTimer == null || m_session == null)
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
                if (m_session != null && m_reconnectAbort &&
                    m_session.Connected && !m_session.KeepAliveStopped)
                {
                    keepaliveRecovered = true;
                    // breaking change, the callback must only assign the new
                    // session if the property is != null
                    m_session = null;
                    Utils.LogInfo("Reconnect aborted, KeepAlive recovered.");
                }

                // do the reconnect.
                if (keepaliveRecovered ||
                    await DoReconnect().ConfigureAwait(false))
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
                Utils.LogError(exception, "Unexpected error during reconnect.");
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
                        int adjustedReconnectPeriod = m_reconnectPeriod - (int)DateTime.UtcNow.Subtract(reconnectStart).TotalMilliseconds;
                        if (adjustedReconnectPeriod <= MinReconnectPeriod)
                        {
                            adjustedReconnectPeriod = MinReconnectPeriod;
                        }
                        m_reconnectTimer.Change(adjustedReconnectPeriod, Timeout.Infinite);
                        m_state = ReconnectState.Triggered;
                    }
                }
            }
        }

        /// <summary>
        /// Reconnects to the server.
        /// </summary>
        private async Task<bool> DoReconnect()
        {
            // helper to override operation timeout
            int operationTimeout = m_session.OperationTimeout;
            int reconnectOperationTimeout = m_reconnectPeriod >= DefaultReconnectPeriod ?
                m_reconnectPeriod : DefaultReconnectPeriod;

            // try a reconnect.
            if (!m_reconnectFailed)
            {
                try
                {
                    m_session.OperationTimeout = reconnectOperationTimeout;
                    if (m_reverseConnectManager != null)
                    {
                        var connection = await m_reverseConnectManager.WaitForConnection(
                                new Uri(m_session.Endpoint.EndpointUrl),
                                m_session.Endpoint.Server.ApplicationUri
                            ).ConfigureAwait(false);

                        m_session.Reconnect(connection);
                    }
                    else
                    {
                        m_session.Reconnect();
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
                        if (sre.StatusCode == StatusCodes.BadTcpInternalError ||
                            sre.StatusCode == StatusCodes.BadCommunicationError ||
                            sre.StatusCode == StatusCodes.BadNotConnected ||
                            sre.StatusCode == StatusCodes.BadRequestTimeout ||
                            sre.StatusCode == StatusCodes.BadTimeout)
                        {
                            // check if reconnecting is still an option.
                            if (m_session.LastKeepAliveTime.AddMilliseconds(m_session.SessionTimeout) > DateTime.UtcNow)
                            {
                                Utils.LogInfo("Calling OnReconnectSession in {0} ms.", m_reconnectPeriod);
                                return false;
                            }
                        }
                    }
                    else
                    {
                        Utils.LogError(exception, "Reconnect failed.");
                    }

                    m_reconnectFailed = true;
                }
                finally
                {
                    m_session.OperationTimeout = operationTimeout;
                }
            }

            // re-create the session.
            try
            {
                ISession session;
                m_session.OperationTimeout = reconnectOperationTimeout;
                if (m_reverseConnectManager != null)
                {
                    var connection = await m_reverseConnectManager.WaitForConnection(
                            new Uri(m_session.Endpoint.EndpointUrl),
                            m_session.Endpoint.Server.ApplicationUri
                        ).ConfigureAwait(false);

                    session = await m_session.SessionFactory.RecreateAsync(m_session, connection).ConfigureAwait(false);
                }
                else
                {
                    session = await m_session.SessionFactory.RecreateAsync(m_session).ConfigureAwait(false);
                }
                m_session.Close();
                m_session = session;
                return true;
            }
            catch (Exception exception)
            {
                Utils.LogError("Could not reconnect the Session. {0}", exception.Message);
                return false;
            }
            finally
            {
                m_session.OperationTimeout = operationTimeout;
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
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ISession m_session;
        private ReconnectState m_state;
        private bool m_reconnectFailed;
        private bool m_reconnectAbort;
        private bool m_cancelReconnect;
        private int m_reconnectPeriod;
        private Timer m_reconnectTimer;
        private EventHandler m_callback;
        private ReverseConnectManager m_reverseConnectManager;
        #endregion
    }
}
