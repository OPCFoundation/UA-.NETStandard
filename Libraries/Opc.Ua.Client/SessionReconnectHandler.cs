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
        /// Create a reconnect handler.
        /// </summary>
        /// <param name="reconnectAbort">Set to <c>true</c> to allow reconnect abort if keep alive recovered.</param>
        public SessionReconnectHandler(bool reconnectAbort = false)
        {
            m_reconnectAbort = reconnectAbort;
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
        /// Begins the reconnect process.
        /// </summary>
        public void BeginReconnect(ISession session, int reconnectPeriod, EventHandler callback)
        {
            BeginReconnect(session, null, reconnectPeriod, callback);
        }

        /// <summary>
        /// Begins the reconnect process using a reverse connection.
        /// </summary>
        public void BeginReconnect(ISession session, ReverseConnectManager reverseConnectManager, int reconnectPeriod, EventHandler callback)
        {
            lock (m_lock)
            {
                if (m_reconnectTimer != null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }

                m_session = session;
                m_reconnectFailed = false;
                m_reconnectPeriod = reconnectPeriod;
                m_callback = callback;
                m_reverseConnectManager = reverseConnectManager;
                m_reconnectTimer = new System.Threading.Timer(OnReconnect, null, reconnectPeriod, Timeout.Infinite);
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
                if (m_reconnectTimer == null)
                {
                    return;
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
                        if (m_reconnectTimer != null)
                        {
                            m_reconnectTimer.Dispose();
                            m_reconnectTimer = null;
                        }
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
                int adjustedReconnectPeriod = m_reconnectPeriod - (int)DateTime.UtcNow.Subtract(reconnectStart).TotalMilliseconds;
                if (adjustedReconnectPeriod <= 0)
                {
                    adjustedReconnectPeriod = 100;
                }
                m_reconnectTimer = new Timer(OnReconnect, null, adjustedReconnectPeriod, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Reconnects to the server.
        /// </summary>
        private async Task<bool> DoReconnect()
        {
            // override operation timeout
            var operationTimeout = m_session.OperationTimeout;

            // try a reconnect.
            if (!m_reconnectFailed)
            {
                try
                {
                    m_session.OperationTimeout = m_reconnectPeriod;
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
                m_session.OperationTimeout = m_reconnectPeriod;
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
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ISession m_session;
        private bool m_reconnectFailed;
        private bool m_reconnectAbort;
        private int m_reconnectPeriod;
        private Timer m_reconnectTimer;
        private EventHandler m_callback;
        private ReverseConnectManager m_reverseConnectManager;
        #endregion
    }
}
