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
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reverse connection states.
    /// </summary>
    public enum ReverseConnectState
    {
        Closed,
        Connecting,
        Connected,
        Rejected,
        Errored
    }

    /// <summary>
    /// Describes the properties of a reverse connection.
    /// </summary>
    public class ReverseConnectProperty
    {
        public ReverseConnectProperty(
            Uri clientUrl,
            int timeout,
            bool configEntry)
        {
            ClientUrl = clientUrl;
            Timeout = timeout > 0 ? timeout : ReverseConnectServer.DefaultReverseConnectTimeout;
            ConfigEntry = true;
        }

        public readonly Uri ClientUrl;
        public readonly int Timeout;
        public readonly bool ConfigEntry;
        public ServiceResult ServiceResult;
        public ReverseConnectState State = ReverseConnectState.Closed;
        public DateTime RejectTime;
    }

    /// <summary>
    /// The standard implementation of a UA server with reverse connect.
    /// </summary>
    public class ReverseConnectServer : StandardServer
    {
        public static int DefaultReverseConnectInterval => 15000;
        public static int DefaultReverseConnectTimeout => 30000;
        public static int DefaultReverseConnectRejectTimeout => 60000;

        public ReverseConnectServer()
        {
            m_connectInterval = DefaultReverseConnectInterval;
            m_connectTimeout = DefaultReverseConnectTimeout;
            m_rejectTimeout = DefaultReverseConnectRejectTimeout;
            m_connections = new Dictionary<Uri, ReverseConnectProperty>();
        }

        #region StandardServer overrides
        /// <inheritdoc />
        protected override void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
            base.OnUpdateConfiguration(configuration);
            UpdateConfiguration(configuration);
        }


        /// <inheritdoc />
        protected override void StartApplication(ApplicationConfiguration configuration)
        {
            base.StartApplication(configuration);
            StartTimer(false);
        }

        /// <inheritdoc />
        protected override void OnServerStopping()
        {
            DisposeTimer();
            base.OnServerStopping();
        }

        /// <inheritdoc />
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);
            UpdateConfiguration(configuration);
            StartTimer(true);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Add a reverse connection url.
        /// </summary>
        public virtual void AddReverseConnection(Uri url, int timeout = 0)
        {
            var reverseConnection = new ReverseConnectProperty(url, timeout, false);
            lock (m_connections)
            {
                m_connections[url] = reverseConnection;
                StartTimer(false);
            }
        }

        /// <summary>
        /// Remove a reverse connection url.
        /// </summary>
        public virtual void RemoveReverseConnection(Uri url)
        {
            lock (m_connections)
            {
                m_connections.Remove(url);
                if (m_connections.Count == 0)
                {
                    DisposeTimer();
                }
            }
        }

        /// <summary>
        /// Return a dictionary of configured reverse connection Urls.
        /// </summary>
        /// <returns></returns>
        public virtual Dictionary<Uri, ReverseConnectProperty> GetReverseConnections()
        {
            lock (m_connections)
            {
                return m_connections;
            }
        }
        #endregion

        #region Private Properties
        /// <summary>
        /// Timer callback to establish new reverse connections.
        /// </summary>
        private void OnReverseConnect(object state)
        {
            lock (m_connections)
            {
                foreach (var reverseConnection in m_connections.Values)
                {
                    // recharge a rejected connection after timeout
                    if (reverseConnection.State == ReverseConnectState.Rejected &&
                        reverseConnection.RejectTime + TimeSpan.FromMilliseconds(m_rejectTimeout) < DateTime.UtcNow)
                    {
                        reverseConnection.State = ReverseConnectState.Closed;
                    }
                    // try the reverse connect
                    if (reverseConnection.State == ReverseConnectState.Closed)
                    {
                        try
                        {
                            reverseConnection.State = ReverseConnectState.Connecting;
                            base.CreateConnection(reverseConnection.ClientUrl,
                                reverseConnection.Timeout > 0 ? reverseConnection.Timeout : m_connectTimeout);
                            Utils.Trace($"Create Connection! [{reverseConnection.State}][{reverseConnection.ClientUrl}]");
                        }
                        catch (Exception e)
                        {
                            reverseConnection.State = ReverseConnectState.Errored;
                            reverseConnection.ServiceResult = new ServiceResult(e);
                            Utils.Trace($"Create Connection failed! [{reverseConnection.State}][{reverseConnection.ClientUrl}]");
                        }
                    }
                }
            }
            StartTimer(true);
        }

        /// <summary>
        /// Track reverse connection status.
        /// </summary>
        protected override void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            lock (m_connections)
            {
                ReverseConnectProperty reverseConnection = null;
                if (m_connections.TryGetValue(e.EndpointUrl, out reverseConnection))
                {
                    ServiceResult priorStatus = reverseConnection.ServiceResult;
                    if (ServiceResult.IsBad(e.ChannelStatus))
                    {
                        reverseConnection.ServiceResult = e.ChannelStatus;
                        if (e.ChannelStatus.Code == StatusCodes.BadTcpMessageTypeInvalid)
                        {
                            reverseConnection.State = ReverseConnectState.Rejected;
                            reverseConnection.RejectTime = DateTime.UtcNow;
                            Utils.Trace($"Client Rejected Connection! [{reverseConnection.State}][{e.EndpointUrl}]");
                            return;
                        }
                        else
                        {
                            reverseConnection.State = ReverseConnectState.Closed;
                            Utils.Trace($"Connection Error! [{reverseConnection.State}][{e.EndpointUrl}]");
                            return;
                        }
                    }
                    reverseConnection.State = e.Closed ? ReverseConnectState.Closed : ReverseConnectState.Connected;
                    Utils.Trace($"New Connection State! [{reverseConnection.State}][{e.EndpointUrl}]");
                }
                else
                {
                    Utils.Trace($"Warning: Status changed for unknown connection: [{e.ChannelStatus}][{e.EndpointUrl}]");
                }
            }
        }

        /// <summary>
        /// Restart the timer. 
        /// </summary>
        private void StartTimer(bool forceRestart)
        {
            if (forceRestart)
            {
                DisposeTimer();
            }
            lock (m_connections)
            {
                if (m_connectInterval > 0 &&
                    m_connections.Count > 0 &&
                    m_reverseConnectTimer == null)
                {
                    m_reverseConnectTimer = new Timer(OnReverseConnect, this, m_connectInterval, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Dispose the current timer.
        /// </summary>
        private void DisposeTimer()
        {
            // start registration timer.
            lock (m_connections)
            {
                if (m_reverseConnectTimer != null)
                {
                    Utils.SilentDispose(m_reverseConnectTimer);
                    m_reverseConnectTimer = null;
                }
            }
        }

        /// <summary>
        /// Remove a reverse connection url.
        /// </summary>
        private void ClearConnections(bool configEntry)
        {
            lock (m_connections)
            {
                var toRemove = m_connections.Where(r => r.Value.ConfigEntry == configEntry);
                foreach (var entry in toRemove)
                {
                    m_connections.Remove(entry.Key);
                }
            }
        }

        /// <summary>
        /// Update the reverse connect configuration from the application configuration.
        /// </summary>
        private void UpdateConfiguration(ApplicationConfiguration configuration)
        {
            ClearConnections(true);

            // get the configuration for the reverse connections.
            var reverseConnect = configuration.ServerConfiguration.ReverseConnect;

            // add configuration reverse client connection properties.
            if (reverseConnect != null)
            {
                lock (m_connections)
                {
                    m_connectInterval = reverseConnect.ConnectInterval > 0 ? reverseConnect.ConnectInterval : DefaultReverseConnectInterval;
                    m_connectTimeout = reverseConnect.ConnectTimeout > 0 ? reverseConnect.ConnectTimeout : DefaultReverseConnectTimeout;
                    m_rejectTimeout = reverseConnect.RejectTimeout > 0 ? reverseConnect.RejectTimeout : DefaultReverseConnectRejectTimeout;
                    foreach (var client in reverseConnect.Clients)
                    {
                        var uri = Utils.ParseUri(client.EndpointUrl);
                        if (uri != null)
                        {
                            m_connections[uri] = new ReverseConnectProperty(uri, client.Timeout, true);
                        }
                    }
                }
            }
        }
        #endregion

        #region Private Fields
        private Timer m_reverseConnectTimer;
        private int m_connectInterval;
        private int m_connectTimeout;
        private int m_rejectTimeout;
        private Dictionary<Uri, ReverseConnectProperty> m_connections;
        #endregion
    }
}
