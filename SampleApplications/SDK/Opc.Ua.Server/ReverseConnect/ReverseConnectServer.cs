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
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The standard implementation of a UA server with reverse connect.
    /// </summary>
    public class ReverseConnectServer : StandardServer
    {
        public static int DefaultReverseConnectionTimeout = 15000;
        public static int DefaultMaxReverseConnections = 1;

        private enum ReverseConnectState
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
        private class ReverseConnection
        {
            public ReverseConnection(
                Uri clientUrl,
                int maxConnections = 0,
                int timeout = 0)
            {
                ClientUrl = clientUrl;
                MaxConnections = maxConnections != 0 ? maxConnections : DefaultMaxReverseConnections;
                Timeout = timeout != 0 ? timeout : DefaultReverseConnectionTimeout;
            }

            public readonly Uri ClientUrl;
            public readonly int MaxConnections;
            public readonly int Timeout;
            public ServiceResult ServiceResult;
            public ReverseConnectState State = ReverseConnectState.Closed;
            public DateTime RejectTime;
        }

        protected override void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
            base.OnUpdateConfiguration(configuration);
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        protected override void StartApplication(ApplicationConfiguration configuration)
        {
            base.StartApplication(configuration);
            StartTimer(false);
        }

        /// <summary>
        /// Called before the server stops
        /// </summary>
        protected override void OnServerStopping()
        {
            base.OnServerStopping();
        }

        /// <summary>
        /// Called before the server starts.
        /// </summary>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);

            // get the configuration for the reverse connections.
            m_configuration = configuration.ParseExtension<ReverseConnectServerConfiguration>();

            // add configuration reverse client connection properties.
            if (m_configuration != null)
            {
                lock (m_connections)
                {
                    foreach (var client in m_configuration.ReverseConnectClients)
                    {
                        var uri = Utils.ParseUri(client.EndpointUrl);
                        if (uri != null)
                        {
                            m_connections[uri] = new ReverseConnection(uri, client.MaxConnections, client.Timeout);
                        }
                    }
                    m_connectInterval = m_configuration.ConnectInterval;
                }
            }
        }

        /// <summary>
        /// Add a reverse connection url.
        /// </summary>
        public virtual void AddReverseConnection(Uri url, int maxConnections = 0, int timeout = 0)
        {
            var reverseConnection = new ReverseConnection(url, maxConnections, timeout);
            lock (m_connections)
            {
                m_connections.Add(url, reverseConnection);
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
        /// Timer callback to establish new reverse connections.
        /// </summary>
        private void OnReverseConnect(object state)
        {
            lock (m_connections)
            {
                foreach (var reverseConnection in m_connections.Values)
                {
                    // TODO: use channel counter
                    if (reverseConnection.State == ReverseConnectState.Closed)
                    {
                        try
                        {
                            reverseConnection.State = ReverseConnectState.Connecting;
                            base.CreateConnection(reverseConnection.ClientUrl, reverseConnection.Timeout);
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
        /// Track number of connections per reverse connection.
        /// </summary>
        protected override void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            lock (m_connections)
            {
                ReverseConnection reverseConnection = null;
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

        private Timer m_reverseConnectTimer;
        private ReverseConnectServerConfiguration m_configuration;
        private int m_connectInterval;
        private Dictionary<Uri, ReverseConnection> m_connections = new Dictionary<Uri, ReverseConnection>();
    }
}
