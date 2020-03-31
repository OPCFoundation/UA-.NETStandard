/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Attempts to reconnect to the server.
    /// </summary>
    public class SessionReconnectHandler : IDisposable
    {
        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
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
        public Session Session
        {
            get { return m_session; }
        }

        /// <summary>
        /// Begins the reconnect process.
        /// </summary>
        public void BeginReconnect(Session session, int reconnectPeriod, EventHandler callback)
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
                m_reconnectTimer = new System.Threading.Timer(OnReconnect, null, reconnectPeriod, Timeout.Infinite);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Called when the reconnect timer expires.
        /// </summary>
        private void OnReconnect(object state)
        {
            try
            {
                // check for exit.
                if (m_reconnectTimer == null)
                {
                    return;
                }

                // do the reconnect.
                if (DoReconnect())
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
                Utils.Trace(exception, "Unexpected error during reconnect.");
            }

            // schedule the next reconnect.
            lock (m_lock)
            {
                m_reconnectTimer = new System.Threading.Timer(OnReconnect, null, m_reconnectPeriod, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Reconnects to the server.
        /// </summary>
        private bool DoReconnect()
        {
            // try a reconnect.
            if (!m_reconnectFailed)
            {
                try
                {
                    m_session.Reconnect();

                    // monitored items should start updating on their own.
                    return true;
                }
                catch (Exception exception)
                {
                    // recreate the session if it has been closed.
                    ServiceResultException sre = exception as ServiceResultException;

                    // check if the server endpoint could not be reached.
                    if ((sre != null &&
                        (sre.StatusCode == StatusCodes.BadTcpInternalError ||
                         sre.StatusCode == StatusCodes.BadCommunicationError ||
                         sre.StatusCode == StatusCodes.BadNotConnected)) ||
                        exception is System.ServiceModel.EndpointNotFoundException)
                    {
                        // check if reconnecting is still an option.
                        if (m_session.LastKeepAliveTime.AddMilliseconds(m_session.SessionTimeout) > DateTime.UtcNow)
                        {
                            Utils.Trace("Calling OnReconnectSession in {0} ms.", m_reconnectPeriod);
                            return false;
                        }
                    }

                    m_reconnectFailed = true;
                }
            }

            // re-create the session.
            try
            {
                Session session = Session.Recreate(m_session);
                m_session.Close();
                m_session = session;
                return true;
            }
            catch (Exception exception)
            {
                Utils.Trace("Could not reconnect the Session. {0}", exception.Message);
                return false;
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Session m_session;
        private bool m_reconnectFailed;
        private int m_reconnectPeriod;
        private Timer m_reconnectTimer;
        private EventHandler m_callback;
        #endregion
    }
}
