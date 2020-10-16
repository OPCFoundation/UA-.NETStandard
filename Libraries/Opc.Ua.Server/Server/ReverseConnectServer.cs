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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reverse connection states.
    /// </summary>
    public enum ReverseConnectState
    {
        /// <summary>
        /// The connection is closed.
        /// </summary>
        Closed = 0,

        /// <summary>
        /// The server is connecting.
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// The server is connected with a client.
        /// </summary>
        Connected = 2,

        /// <summary>
        /// The client rejected the connection with the server.
        /// </summary>
        Rejected = 3,

        /// <summary>
        /// An error occurred connecting with the client.
        /// </summary>
        Errored = 4
    }

    /// <summary>
    /// Describes the properties of a server reverse connection.
    /// </summary>
    public class ReverseConnectProperty
    {
        /// <summary>
        /// Initialize a reverse connect server property.
        /// </summary>
        /// <param name="clientUrl">The Url of the reverse connect client.</param>
        /// <param name="timeout">The timeout to use for a reverse connect attempt.</param>
        /// <param name="maxSessionCount">The maximum number of sessions allowed to the client.</param>
        /// <param name="configEntry">If this is an application configuration entry.</param>
        /// <param name="enabled">If the connection is enabled.</param>
        public ReverseConnectProperty(
            Uri clientUrl,
            int timeout,
            int maxSessionCount,
            bool configEntry,
            bool enabled = true)
        {
            ClientUrl = clientUrl;
            Timeout = timeout > 0 ? timeout : ReverseConnectServer.DefaultReverseConnectTimeout;
            MaxSessionCount = maxSessionCount;
            ConfigEntry = configEntry;
            Enabled = enabled;
        }

        /// <summary>
        /// The Url of the reverse connect client.
        /// </summary>
        public readonly Uri ClientUrl;

        /// <summary>
        /// The timeout to use for a reverse connect attempt.
        /// </summary>
        public readonly int Timeout;

        /// <summary>
        /// If this is an application configuration entry.
        /// </summary>
        public readonly bool ConfigEntry;

        /// <summary>
        /// The service result of the last connection attempt.
        /// </summary>
        public ServiceResult ServiceResult;

        /// <summary>
        /// The last state of the reverse connection.
        /// </summary>
        public ReverseConnectState LastState = ReverseConnectState.Closed;

        /// <summary>
        /// The maximum number of sessions allowed to the client.
        /// </summary>
        public int MaxSessionCount;

        /// <summary>
        /// If the connection is enabled.
        /// </summary>
        public bool Enabled;

        /// <summary>
        /// The time when the connection was rejected.
        /// </summary>
        public DateTime RejectTime;
    }

    /// <summary>
    /// The standard implementation of a UA server with reverse connect.
    /// </summary>
    public class ReverseConnectServer : StandardServer
    {
        /// <summary>
        /// The default reverse connect interval.
        /// </summary>
        public static int DefaultReverseConnectInterval => 15000;

        /// <summary>
        /// The default reverse connect timeout.
        /// </summary>
        public static int DefaultReverseConnectTimeout => 30000;

        /// <summary>
        /// The default timeout after a rejected connection attempt.
        /// </summary>
        public static int DefaultReverseConnectRejectTimeout => 60000;

        /// <summary>
        /// Creates a reverse connect server based on a StandardServer.
        /// </summary>
        public ReverseConnectServer()
        {
            m_connectInterval = DefaultReverseConnectInterval;
            m_connectTimeout = DefaultReverseConnectTimeout;
            m_rejectTimeout = DefaultReverseConnectRejectTimeout;
            m_connections = new Dictionary<Uri, ReverseConnectProperty>();
        }

        #region StandardServer overrides
        /// <inheritdoc/>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            UpdateConfiguration(base.Configuration);
            StartTimer(true);
        }

        /// <inheritdoc />
        protected override void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
            base.OnUpdateConfiguration(configuration);
            UpdateConfiguration(configuration);
        }

        /// <inheritdoc />
        protected override void OnServerStopping()
        {
            DisposeTimer();
            base.OnServerStopping();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Add a reverse connection url.
        /// </summary>
        public virtual void AddReverseConnection(Uri url, int timeout = 0, int maxSessionCount = 0, bool enabled = true)
        {
            if (m_connections.ContainsKey(url))
            {
                throw new ArgumentException("Connection for specified clientUrl is already configured", nameof(url));
            }
            else
            {
                var reverseConnection = new ReverseConnectProperty(url, timeout, maxSessionCount, false, enabled);
                lock (m_connectionsLock)
                {
                    m_connections[url] = reverseConnection;
                    Utils.Trace("Reverse Connection added for EndpointUrl: {0}.", url);

                    StartTimer(false);
                }
            }
        }

        /// <summary>
        /// Remove a reverse connection url.
        /// </summary>
        /// <returns>true if the reverse connection is found and removed</returns>
        public virtual bool RemoveReverseConnection(Uri url)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            lock (m_connectionsLock)
            {
                bool connectionRemoved = m_connections.Remove(url);

                if (connectionRemoved)
                {
                    Utils.Trace("Reverse Connection removed for EndpointUrl: {0}.", url);
                }

                if (m_connections.Count == 0)
                {
                    DisposeTimer();
                }

                return connectionRemoved;
            }
        }

        /// <summary>
        /// Return a dictionary of configured reverse connection Urls.
        /// </summary>
        public virtual ReadOnlyDictionary<Uri, ReverseConnectProperty> GetReverseConnections()
        {
            lock (m_connectionsLock)
            {
                return new ReadOnlyDictionary<Uri, ReverseConnectProperty>(m_connections);
            }
        }
        #endregion

        #region Private Properties
        /// <summary>
        /// Timer callback to establish new reverse connections.
        /// </summary>
        private void OnReverseConnect(object state)
        {
            try
            {
                lock (m_connectionsLock)
                {
                    foreach (var reverseConnection in m_connections.Values)
                    {
                        // recharge a rejected connection after timeout
                        if (reverseConnection.LastState == ReverseConnectState.Rejected &&
                            reverseConnection.RejectTime + TimeSpan.FromMilliseconds(m_rejectTimeout) < DateTime.UtcNow)
                        {
                            reverseConnection.LastState = ReverseConnectState.Closed;
                        }

                        // try the reverse connect
                        if ((reverseConnection.Enabled) &&
                            (reverseConnection.MaxSessionCount == 0 ||
                            (reverseConnection.MaxSessionCount == 1 && reverseConnection.LastState == ReverseConnectState.Closed) ||
                             reverseConnection.MaxSessionCount > ServerInternal.SessionManager.GetSessions().Count))
                        {
                            try
                            {
                                reverseConnection.LastState = ReverseConnectState.Connecting;
                                base.CreateConnection(reverseConnection.ClientUrl,
                                    reverseConnection.Timeout > 0 ? reverseConnection.Timeout : m_connectTimeout);
                                Utils.Trace($"Create Connection! [{reverseConnection.LastState}][{reverseConnection.ClientUrl}]");
                            }
                            catch (Exception e)
                            {
                                reverseConnection.LastState = ReverseConnectState.Errored;
                                reverseConnection.ServiceResult = new ServiceResult(e);
                                Utils.Trace($"Create Connection failed! [{reverseConnection.LastState}][{reverseConnection.ClientUrl}]");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OnReverseConnect unexpected error: {0}", ex.Message);
            }
            finally
            {
                StartTimer(true);
            }
        }

        /// <summary>
        /// Track reverse connection status.
        /// </summary>
        protected override void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            lock (m_connectionsLock)
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
                            reverseConnection.LastState = ReverseConnectState.Rejected;
                            reverseConnection.RejectTime = DateTime.UtcNow;
                            Utils.Trace($"Client Rejected Connection! [{reverseConnection.LastState}][{e.EndpointUrl}]");
                            return;
                        }
                        else
                        {
                            reverseConnection.LastState = ReverseConnectState.Closed;
                            Utils.Trace($"Connection Error! [{reverseConnection.LastState}][{e.EndpointUrl}]");
                            return;
                        }
                    }
                    reverseConnection.LastState = e.Closed ? ReverseConnectState.Closed : ReverseConnectState.Connected;
                    Utils.Trace($"New Connection State! [{reverseConnection.LastState}][{e.EndpointUrl}]");
                }
                else
                {
                    Utils.Trace($"Warning: Status changed for unknown reverse connection: [{e.ChannelStatus}][{e.EndpointUrl}]");
                }
            }

            base.OnConnectionStatusChanged(sender, e);
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
            lock (m_connectionsLock)
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
            lock (m_connectionsLock)
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
            lock (m_connectionsLock)
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
                lock (m_connectionsLock)
                {
                    m_connectInterval = reverseConnect.ConnectInterval > 0 ? reverseConnect.ConnectInterval : DefaultReverseConnectInterval;
                    m_connectTimeout = reverseConnect.ConnectTimeout > 0 ? reverseConnect.ConnectTimeout : DefaultReverseConnectTimeout;
                    m_rejectTimeout = reverseConnect.RejectTimeout > 0 ? reverseConnect.RejectTimeout : DefaultReverseConnectRejectTimeout;
                    foreach (var client in reverseConnect.Clients)
                    {
                        var uri = Utils.ParseUri(client.EndpointUrl);
                        if (uri != null)
                        {
                            if (m_connections.ContainsKey(uri))
                            {
                                Utils.Trace("Warning: ServerConfiguration.ReverseConnect contains duplicate EndpointUrl: {0}.", uri);
                            }
                            else
                            {
                                m_connections[uri] = new ReverseConnectProperty(uri, client.Timeout, client.MaxSessionCount, true, client.Enabled);
                                Utils.Trace("Reverse Connection added for EndpointUrl: {0}.", uri);
                            }
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
        private object m_connectionsLock = new object();
        #endregion
    }
}
