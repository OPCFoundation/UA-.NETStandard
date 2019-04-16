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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A base class for classes that implement an OPC COM specification.
    /// </summary>
    public class ComAe2Proxy : ComProxy
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaProxy"/> class.
        /// </summary>
        public ComAe2Proxy()
		{
            m_mapper = new ComAeNamespaceMapper();
            m_subscriptions = new List<ComAe2Subscription>();
            m_conditionManager = new AeConditionManager();
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    lock (m_subscriptions)
                    {
                        for (int ii = 0; ii < m_subscriptions.Count; ii++)
                        {
                            m_subscriptions[ii].Dispose();
                        }

                        m_subscriptions.Clear();
                    }

                    if (m_browser != null)
                    {
                        m_browser.Dispose();
                        m_browser = null;
                    }

                    m_mapper = null;
                }

                m_disposed = true;
            }

            base.Dispose(disposing);
        }

        private bool m_disposed = false;
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        protected override void OnSessionCreated()
        {
            lock (Lock)
            {
                // fetch the configuration.
                m_configuration = Endpoint.ParseExtension<ComAe2ProxyConfiguration>(null);

                if (m_configuration == null)
                {
                    m_configuration = new ComAe2ProxyConfiguration();
                }

                Session session = Session;

                // update the mapping and pass the new session to other objects.
                m_mapper.Initialize(session, m_configuration);

                // save the configuration.
                Endpoint.UpdateExtension<ComAe2ProxyConfiguration>(null, m_configuration);
                SaveConfiguration();

                // create the browser.
                m_browser = new ComAe2Browser(this, m_configuration, m_mapper);
                m_conditionManager.Initialize();
            }
        }

        /// <summary>
        /// Called when a session is reconnected.
        /// </summary>
        protected override void OnSessionReconected()
        {
            lock (m_subscriptions)
            {
                for (int ii = 0; ii < m_subscriptions.Count; ii++)
                {
                    m_subscriptions[ii].OnSessionReconected(Session);
                }
            }
        }
        #endregion

        /// <summary>
        /// Gets the last update time.
        /// </summary>
        public DateTime LastUpdateTime
        {
            get
            {
                DateTime latest = DateTime.MinValue;

                lock (m_subscriptions)
                {
                    for (int ii = 0; ii < m_subscriptions.Count; ii++)
                    {
                        DateTime time = m_subscriptions[ii].LastUpdateTime;

                        if (time > latest)
                        {
                            latest = time;
                        }
                    }
                }

                return latest;
            }
        }

        /// <summary>
        /// Gets the supported event categories.
        /// </summary>
        public List<AeEventCategory> QueryEventCategories(int eventType)
        {
            ThrowIfNotConnected();
            return m_mapper.GetCategories(eventType);
        }

        /// <summary>
        /// Gets the supported event attributes.
        /// </summary>
        public List<AeEventAttribute> QueryEventAttributes(uint categoryId)
        {
            ThrowIfNotConnected();
            return m_mapper.GetAttributes(categoryId);
        }

        /// <summary>
        /// Creates a new area browser.
        /// </summary>
        public ComAe2Browser CreateBrowser()
        {
            ThrowIfNotConnected();
            return new ComAe2Browser(this, m_configuration, m_mapper);
        }

        /// <summary>
        /// Creates a new event subscription.
        /// </summary>
        public ComAe2Subscription CreateSubscription()
        {
            ThrowIfNotConnected();

            ComAe2Subscription subscription = new ComAe2Subscription(this, m_configuration, m_mapper, m_browser, m_conditionManager);

            lock (m_subscriptions)
            {
                m_subscriptions.Add(subscription);
            }

            return subscription;
        }

        /// <summary>
        /// Called when a subscription is deleted.
        /// </summary>
        public void SubscriptionDeleted(ComAe2Subscription subscription)
        {
            lock (m_subscriptions)
            {
                for (int ii = 0; ii < m_subscriptions.Count; ii++)
                {
                    if (Object.ReferenceEquals(subscription, m_subscriptions[ii]))
                    {
                        m_subscriptions.RemoveAt(ii);
                        break;
                    }
                }
            }
        }
        
        /// <summary>
        /// Checks that the source node exists.
        /// </summary>
        public bool IsSourceValid(string sourceId)
        {
            ThrowIfNotConnected();
            return m_browser.IsValidQualifiedName(sourceId, false);
        }

        /// <summary>
        /// Acknowledges one or more events.
        /// </summary>
        public int[] AcknowledgeEvents(
            string comment,
            string acknowledgerId,
            AeAcknowledgeRequest[] requests)
        {
            ThrowIfNotConnected();
            return m_conditionManager.AcknowledgeEvents(Session, comment, acknowledgerId, requests);
        }
        
        #region Private Fields
        private ComAe2ProxyConfiguration m_configuration;
        private ComAeNamespaceMapper m_mapper;
        private List<ComAe2Subscription> m_subscriptions;
        private ComAe2Browser m_browser;
        private AeConditionManager m_conditionManager;
        #endregion
	}
}
