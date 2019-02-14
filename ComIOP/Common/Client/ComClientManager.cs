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
using System.Text;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Da;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Manages the COM connections used by the UA server.
    /// </summary>
    public class ComClientManager : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComClientManager"/> class.
        /// </summary>
        public ComClientManager()
        {
        }
        #endregion

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
                Utils.SilentDispose(m_statusTimer);
                m_statusTimer = null;

                Utils.SilentDispose(m_defaultClient);
                m_defaultClient = null;
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Selects the COM client to use for the current context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="useDefault">The whether to use the default context.</param>
        /// <returns>A COM client instance.</returns>
        public virtual ComClient SelectClient(ServerSystemContext context, bool useDefault)
        {
            if (useDefault)
            {
                return DefaultClient;
            }

            return GetLocalizedClient(context.UserIdentity, context.PreferredLocales);
        }

        /// <summary>
        /// Initializes the manager by creating the default instance.
        /// </summary>
        public void Initialize(
            ServerSystemContext context,
            ComClientConfiguration configuration, 
            ComServerStatusState statusNode,
            object statusNodeLock,
            WaitCallback reconnectCallback)
        {
            m_defaultSystemContext = context;
            m_configuration = configuration;
            m_statusNode = statusNode;
            m_statusNodeLock = statusNodeLock;
            m_statusUpdateInterval = m_configuration.MaxReconnectWait;
            m_reconnectCallback = reconnectCallback;

            // limit status updates to once per 10 seconds.
            if (m_statusUpdateInterval < 10000)
            {
                m_statusUpdateInterval = 10000;
            }

            StartStatusTimer(OnStatusTimerExpired);
        }

        /// <summary>
        /// Returns a localized client based on the preferred locales.
        /// </summary>
        /// <param name="identity">The identity to use.</param>
        /// <param name="preferredLocales">The locales to use.</param>
        /// <returns>A localized client.</returns>
        public ComClient GetLocalizedClient(IUserIdentity identity, IList<string> preferredLocales)
        {
            return GetLocalizedClient(identity, ComClient.SelectLocaleId(AvailableLocaleIds, preferredLocales));
        }

        /// <summary>
        /// Returns a localized client for the specified locale id.
        /// </summary>
        /// <param name="identity">The identity.</param>
        /// <param name="localeId">The locales id.</param>
        /// <returns>A localized client.</returns>
        public ComClient GetLocalizedClient(IUserIdentity identity, int localeId)
        {
            // check if a logon is required.
            string userName = null;

            if (identity != null && identity.TokenType == UserTokenType.UserName)
            {
                userName = (identity.GetIdentityToken() as UserNameIdentityToken).UserName;
            }

            if (String.IsNullOrEmpty(userName) && localeId == ComUtils.LOCALE_SYSTEM_DEFAULT)
            {
                Utils.Trace("COM Client Selected: DEFAULT (no match for locale)");
                return DefaultClient;
            }

            // create the key.
            StringBuilder buffer = new StringBuilder();
            buffer.Append(localeId);
            
            if (!String.IsNullOrEmpty(userName))
            {
                buffer.Append(':');
                buffer.Append(userName);
            }

            string key = buffer.ToString();

            if (m_localizedClients == null)
            {
                m_localizedClients = new Dictionary<string, ComClient>();
            }

            ComClient client = null;

            if (!m_localizedClients.TryGetValue(key, out client))
            {
                client = CreateClient();
                client.Key = key;
                client.LocaleId = localeId;
                client.UserIdentity = identity;
                client.CreateInstance();
                m_localizedClients[key] = client;
            }

            // Utils.Trace("COM Client Seleted: {0}", key);
            return client;
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// Gets the default system context.
        /// </summary>
        /// <value>The default system context.</value>
        protected ServerSystemContext DefaultSystemContext
        {
            get { return m_defaultSystemContext; }
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        protected ComClientConfiguration Configuration
        {
            get { return m_configuration; }
        }

        /// <summary>
        /// Gets or sets the default COM client instance.
        /// </summary>
        /// <value>The default client.</value>
        protected ComClient DefaultClient
        {
            get { return m_defaultClient; }
            set { m_defaultClient = value; }
        }

        /// <summary>
        /// The locale ids supported by the server.
        /// </summary>
        protected int[] AvailableLocaleIds { get; private set; }

        /// <summary>
        /// Gets the status node.
        /// </summary>
        /// <value>The status node.</value>
        protected ComServerStatusState StatusNode
        {
            get { return m_statusNode; }
        }

        /// <summary>
        /// Gets the synchronization object for the status node.
        /// </summary>
        /// <value>The ynchronization object for the status node.</value>
        protected object StatusNodeLock
        {
            get { return m_statusNodeLock; }
        }

        /// <summary>
        /// Starts the status timer.
        /// </summary>
        /// <param name="callback">The callback to use when the timer expires.</param>
        protected void StartStatusTimer(TimerCallback callback)
        {
            if (m_statusTimer != null)
            {
                m_statusTimer.Dispose();
                m_statusTimer = null;
            }

            m_statusTimer = new Timer(callback, null, 0, m_statusUpdateInterval);
        }

        /// <summary>
        /// Creates a new client object.
        /// </summary>
        protected virtual ComClient CreateClient()
        {
            return null;
        }

        /// <summary>
        /// Updates the status node.
        /// </summary>
        /// <returns>True if the update was successful.</returns>
        protected virtual bool UpdateStatus()
        {
            return false;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Called when thes status timer expires.
        /// </summary>
        /// <param name="state">The state.</param>
        private void OnStatusTimerExpired(object state)
        {
            try
            {
                // create the client if it has not already been created.
                if (m_defaultClient == null)
                {
                    m_defaultClient = CreateClient();
                    m_defaultClient.Key = String.Empty;

                    // set default locale.
                    if (AvailableLocaleIds != null && AvailableLocaleIds.Length > 0)
                    {
                        m_defaultClient.LocaleId = AvailableLocaleIds[0];
                    }
                    else
                    {
                        m_defaultClient.LocaleId = ComUtils.LOCALE_SYSTEM_DEFAULT;
                    }

                    m_defaultClient.CreateInstance();
                    m_lastStatusUpdate = DateTime.UtcNow;
                }

                // check if the last status updates appear to be hanging.
                bool reconnected = false;

                if (m_lastStatusUpdate.AddMilliseconds(m_statusUpdateInterval*1.1) < DateTime.UtcNow)
                {
                    if (m_reconnecting)
                    {
                        return;
                    }

                    m_reconnecting = true;

                    try
                    {
                        Utils.Trace("Communication with COM server failed. Disposing server and reconnecting.");

                        // dispose existing client in the background in case it blocks.
                        ThreadPool.QueueUserWorkItem(OnDisposeClient, m_defaultClient);

                        // dispose localized clients.
                        if (m_localizedClients != null)
                        {
                            foreach (ComClient localizedClient in m_localizedClients.Values)
                            {
                                ThreadPool.QueueUserWorkItem(OnDisposeClient, localizedClient);
                            }

                            m_localizedClients.Clear();
                        }

                        // create a new client.
                        m_defaultClient = CreateClient();
                        m_defaultClient.CreateInstance();

                        reconnected = true;
                    }
                    finally
                    {
                        m_reconnecting = false;
                    }
                }

                // fetches the status from the server and updates the status node. 
                if (UpdateStatus())
                {
                    AvailableLocaleIds = m_defaultClient.QueryAvailableLocales();
                    m_lastStatusUpdate = DateTime.UtcNow;

                    if (reconnected && m_reconnectCallback != null)
                    {
                        m_reconnectCallback(this);
                    }
                }
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, "Error fetching status from the COM server.");
            }
        }

        /// <summary>
        /// Called when a misbehaving client object needs to be disposed.
        /// </summary>
        /// <param name="state">The client object.</param>
        private void OnDisposeClient(object state)
        {
            Utils.SilentDispose(state);
        }
        #endregion

        #region Private Fields
        private ComClientConfiguration m_configuration;
        private ServerSystemContext m_defaultSystemContext;
        private ComServerStatusState m_statusNode;
        private object m_statusNodeLock;
        private ComClient m_defaultClient;
        private Timer m_statusTimer;
        private DateTime m_lastStatusUpdate;
        private int m_statusUpdateInterval;
        private WaitCallback m_reconnectCallback;
        private bool m_reconnecting;
        private Dictionary<string, ComClient> m_localizedClients;
        #endregion
    }
}
