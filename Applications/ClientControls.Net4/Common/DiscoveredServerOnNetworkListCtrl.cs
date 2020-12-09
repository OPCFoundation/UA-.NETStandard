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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;

using Opc.Ua.Client.Controls;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a list of servers.
    /// </summary>
    public partial class DiscoveredServerOnNetworkListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initalize the control.
        /// </summary>
        public DiscoveredServerOnNetworkListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
            ItemsLV.Sorting = SortOrder.Descending;
            ItemsLV.MultiSelect = false;
        }
        #endregion
        
        #region Private Fields
        // The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{ 
			new object[] { "RecordId", HorizontalAlignment.Left, null },  
			new object[] { "ServerName", HorizontalAlignment.Left, null },
			new object[] { "DiscoveryUrl", HorizontalAlignment.Left, null },
			new object[] { "ServerCapabilities",  HorizontalAlignment.Left, null }
		};
        
        private ApplicationConfiguration m_configuration;
        private int m_discoveryTimeout;
        private int m_discoverCount;
        private string m_discoveryUrl;
        private NumericUpDown m_startingRecordIdUpDown;
        private NumericUpDown m_maxRecordsToReturnUpDown;
        private TextBox m_capabilityFilterTextBox;


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
        public void Initialize(string hostname, NumericUpDown startingRecordId, NumericUpDown maxRecordsToReturn, TextBox capabilityFilterText, ApplicationConfiguration configuration)
        {
            Interlocked.Exchange(ref m_configuration, configuration);
            ItemsLV.Items.Clear();
            m_startingRecordIdUpDown = startingRecordId;
            m_maxRecordsToReturnUpDown = maxRecordsToReturn;
            m_capabilityFilterTextBox = capabilityFilterText;

            if (String.IsNullOrEmpty(hostname))
            {
                hostname = System.Net.Dns.GetHostName();
            }
            
            this.Instructions = Utils.Format("Discovering servers on host '{0}'.", hostname);
            AdjustColumns();

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
            ThreadPool.QueueUserWorkItem(new WaitCallback(OnDiscoverServersOnNetwork), urlsToUse);
        }

        /// <summary>
        /// Updates the list of servers displayed in the control.
        /// </summary>
        private void OnUpdateServers(object state)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new WaitCallback(OnUpdateServers), state);
                return;
            }
            
            ItemsLV.Items.Clear();

            ServerOnNetworkCollection servers = state as ServerOnNetworkCollection;

            if (servers != null)
            {
                foreach (ServerOnNetwork server in servers)
                {
                    AddItem(server);
                }
            }

            if (ItemsLV.Items.Count == 0)
            {
                this.Instructions = Utils.Format("No servers to display.");
            }

            AdjustColumns();
        }

        /// <summary>
        /// Attempts fetch the list of network servers from the discovery server.
        /// </summary>
        private void OnDiscoverServersOnNetwork(object state)
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
                        if (DiscoverServersOnNetwork(url))
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
        /// Fetches the network servers from the discovery server.
        /// </summary>
        private bool DiscoverServersOnNetwork(Uri discoveryUrl)
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

                uint startingRecordId = (uint)0;
                uint maxRecordsToReturn = (uint)0;
                StringCollection serverCapabilityFilter = new StringCollection();
                DateTime lastCounterResetTime = DateTime.MinValue;

                try
                {
                    startingRecordId = (uint)m_startingRecordIdUpDown.Value;
                    maxRecordsToReturn = (uint)m_maxRecordsToReturnUpDown.Value;

                    if (!String.IsNullOrEmpty(m_capabilityFilterTextBox.Text))
                    {
                        serverCapabilityFilter = new StringCollection(m_capabilityFilterTextBox.Text.Split(','));
                    }
                }
                catch (Exception e)
                {
                    Utils.Trace("Error retrieving FindServersOnNetwork paramters. Error=({1}){0}", e.Message, e.GetType());
                    return false;
                }

                ServerOnNetworkCollection servers = client.FindServersOnNetwork(startingRecordId, maxRecordsToReturn, serverCapabilityFilter, out lastCounterResetTime);
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
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            ServerOnNetwork server = listItem.Tag as ServerOnNetwork;

            if (server == null)
            {
                base.UpdateItem(listItem, server);
                return;
            }

            listItem.SubItems[0].Text = String.Format("{0}", server.RecordId);
            listItem.SubItems[1].Text = String.Format("{0}", server.ServerName);
            listItem.SubItems[2].Text = String.Format("{0}", server.DiscoveryUrl);
            listItem.SubItems[3].Text = String.Format("{0}", string.Join(",", server.ServerCapabilities)); 

            listItem.ImageKey = GuiUtils.Icons.Service;
        }
        #endregion
    }
}
