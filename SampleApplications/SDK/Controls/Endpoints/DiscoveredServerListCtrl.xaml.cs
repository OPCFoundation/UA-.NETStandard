/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua;
using Opc.Ua.Client.Controls;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a list of servers.
    /// </summary>
    public sealed partial class DiscoveredServerListCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initalize the control.
        /// </summary>
        public DiscoveredServerListCtrl()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private int m_discoveryTimeout;
        private int m_discoverCount = 0;
        private bool m_updating = false;
        private int m_updateCount = 0;
        private string m_discoveryUrl;
        #endregion

        #region Public Interface
        /// <summary>
        /// The timeout in milliseconds to use when discovering servers.
        /// </summary>
        [System.ComponentModel.DefaultValue(5000)]
        public int DiscoveryTimeout
        {
            get { return m_discoveryTimeout; }
            set { m_discoveryTimeout = value; }
        }

        /// <summary>
        /// Gets or sets the discovery URL used to find the servers displayed in the control.
        /// </summary>
        /// <value>The discovery URL.</value>
        public string DiscoveryUrl
        {
            get { return m_discoveryUrl; }
            set { m_discoveryUrl = value; }
        }

        /// <summary>
        /// Displays a list of servers in the control.
        /// </summary>
        public void Initialize(ConfiguredEndpointCollection endpoints, ApplicationConfiguration configuration)
        {
            Interlocked.Exchange(ref m_configuration, configuration);

            ItemsLV.Items.Clear();

            foreach (ApplicationDescription server in endpoints.GetServers())
            {
                AddItem(server);
            }
        }

        /// <summary>
        /// Displays a list of servers in the control.
        /// </summary>
        public void Initialize(string hostname, ApplicationConfiguration configuration)
        {
            Interlocked.Exchange(ref m_configuration, configuration);

            ItemsLV.Items.Clear();

            if (String.IsNullOrEmpty(hostname))
            {
                hostname = Utils.GetHostName();
            }
            
            this.Instructions.Text = Utils.Format("Discovering servers on host '{0}'.", hostname);

            // get a list of well known discovery urls to use.
            StringCollection discoveryUrls = null;

            if (configuration != null && configuration.ClientConfiguration != null)
            {
                discoveryUrls = configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            }

            if (discoveryUrls == null || discoveryUrls.Count == 0)
            {
                discoveryUrls = new StringCollection(Utils.DiscoveryUrls);
            }
            
            // update the urls with the hostname.
            StringCollection urlsToUse = new StringCollection();

            foreach (string discoveryUrl in discoveryUrls)
            {
                urlsToUse.Add(Utils.Format(discoveryUrl, hostname));
            }

            Interlocked.Increment(ref m_discoverCount);
            Task.Run(() =>
            {
                OnDiscoverServers(urlsToUse);
            });
        }

        public ListViewItem AddItem(object item, int index = -1)
        {
            ListViewItem listItem = null;

            if (m_updating)
            {
                if (m_updateCount < ItemsLV.Items.Count)
                {
                    listItem = (ListViewItem)ItemsLV.Items[m_updateCount];
                }

                m_updateCount++;
            }

            if (listItem == null)
            {
                listItem = new ListViewItem();
            }

            listItem.Name = String.Format("{0}", item);
            listItem.Tag = item;

            // calculate new index.
            int newIndex = index;

            if (index < 0 || index > ItemsLV.Items.Count)
            {
                newIndex = ItemsLV.Items.Count;
            }

            // update columns.
            listItem.Tag = item;

            if (listItem.Parent == null)
            {
                // add to control.
                if (index >= 0 && index <= ItemsLV.Items.Count)
                {
                    ItemsLV.Items.Insert(index, listItem);
                }
                else
                {
                    ItemsLV.Items.Add(listItem);
                }
            }

            // return new item.
            return listItem;
        }
        
        /// <summary>
        /// Updates the list of servers displayed in the control.
        /// </summary>
        private void OnUpdateServers(object state)
        {
            ItemsLV.Items.Clear();

            ApplicationDescriptionCollection servers = state as ApplicationDescriptionCollection;

            if (servers != null)
            {
                foreach (ApplicationDescription server in servers)
                {
                    if (server.ApplicationType == ApplicationType.DiscoveryServer)
                    {
                        continue;
                    }

                    AddItem(server);
                }
            }

            if (ItemsLV.Items.Count == 0)
            {
                this.Instructions.Text = Utils.Format("No servers to display.");
            }
        }

        /// <summary>
        /// Attempts fetch the list of servers from the discovery server.
        /// </summary>
        private void OnDiscoverServers(object state)
        {
            try
            {
                int discoverCount = m_discoverCount;

                // do nothing if a valid list is not provided.
                IList<string> discoveryUrls = state as IList<string>;

                if (discoveryUrls == null)
                {
                    return;
                }

                // process each url.
                foreach (string discoveryUrl in discoveryUrls)
                {
                    Uri url = Utils.ParseUri(discoveryUrl);

                    if (url != null)
                    {
                        if (DiscoverServers(url))
                        {
                            return;
                        }

                        // check if another discover operation has started.
                        if (discoverCount != m_discoverCount)
                        {
                            return;
                        }
                    }
                }

                // display empty list.
                OnUpdateServers(null);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error discovering servers.");
            }
        }

        /// <summary>
        /// Fetches the servers from the discovery server.
        /// </summary>
        private bool DiscoverServers(Uri discoveryUrl)
        {
            // use a short timeout.
            EndpointConfiguration configuration = EndpointConfiguration.Create(m_configuration);
            configuration.OperationTimeout = m_discoveryTimeout;

            DiscoveryClient client = null;

            try
            {
                client = DiscoveryClient.Create(
                    discoveryUrl,
                    EndpointConfiguration.Create(m_configuration));

                ApplicationDescriptionCollection servers = client.FindServers(null);
                m_discoveryUrl = discoveryUrl.ToString();
                OnUpdateServers(servers);
                return true;
            }
            catch (Exception e)
            {
                Utils.Trace("DISCOVERY ERROR - Could not fetch servers from url: {0}. Error=({2}){1}", discoveryUrl, e.Message, e.GetType());
                return false;
            }
            finally
            {
                if (client != null)
                {
                    client.Close();
                }
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Updates an item in the control.
        /// </summary>
        public void UpdateItem(ListViewItem listItem, object item)
        {
            ApplicationDescription server = listItem.Tag as ApplicationDescription;

            if (server == null)
            {
                listItem.Tag = server;
                return;
            }

            string hostname = "";

            // extract host from application uri.
            Uri uri = Utils.ParseUri(server.ApplicationUri);
            
            if (uri != null)
            {
                hostname = uri.DnsSafeHost; 
            }

            // get the host name from the discovery urls.
            if (String.IsNullOrEmpty(hostname))
            {
                foreach (string discoveryUrl in server.DiscoveryUrls)
                {
                    Uri url = Utils.ParseUri(discoveryUrl);

                    if (url != null)
                    {
                        hostname = url.DnsSafeHost;
                        break;
                    }
                }
            }

			listItem.Content = String.Format("Name: {0}, type: {1}, host: {2}, URI: {3}", server.ApplicationName, server.ApplicationType, hostname, server.ApplicationUri); 
        }
        #endregion
    }
}
