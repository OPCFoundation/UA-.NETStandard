/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Text;
using System.Threading;
using System.Reflection;
using Windows.UI.Xaml.Controls;


namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A list of servers.
    /// </summary>
    public partial class ConfiguredServerListCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initalize the control.
        /// </summary>
        public ConfiguredServerListCtrl()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private ConfiguredEndpointCollection m_endpoints;
        private bool m_updating = false;
        private int m_updateCount = 0;

        public object SelectedTag
        {
            get
            {
                if (ItemsLV.SelectedItems.Count != 1)
                {
                    return null;
                }

                return ((ListViewItem)ItemsLV.SelectedItems[0]).Tag;
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays a list of servers in the control.
        /// </summary>
        public void Initialize(ConfiguredEndpointCollection endpoints, ApplicationConfiguration configuration)
        {
            Interlocked.Exchange(ref m_configuration, configuration);

            ItemsLV.Items.Clear();

            m_endpoints = endpoints;

            if (endpoints != null)
            {
                foreach (ConfiguredEndpoint endpoint in endpoints.Endpoints)
                {
                    AddItem(endpoint);
                }
            }
        }
        #endregion

        #region Overridden Methods
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

            // update columns.
            UpdateItem(listItem, item);

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
         /// Updates an item in the control.
         /// </summary>
        public void UpdateItem(ListViewItem listItem, object item)
        {
            ConfiguredEndpoint endpoint = listItem.Tag as ConfiguredEndpoint;

            if (endpoint == null)
            {
                listItem.Tag = endpoint;
                return;
            }

            string hostname = "<Unknown>";
            string protocol = "<Unknown>";

            Uri uri = endpoint.EndpointUrl;
            
            if (uri != null)
            {
                hostname = uri.DnsSafeHost; 
                protocol = uri.Scheme;
            }

            String user = "<Unknown>";
            UserTokenPolicy policy = endpoint.SelectedUserTokenPolicy;
            if (policy != null)
            {
                StringBuilder buffer = new StringBuilder();
                buffer.Append(policy.TokenType);
                if (endpoint.UserIdentity != null)
                {
                    buffer.Append("/");
                    buffer.Append(endpoint.UserIdentity);
                }
                user = buffer.ToString();
            }

            listItem.Content = String.Format("Application: {0}\r\nHost: {1}\r\nProtocol: {2}\r\nSecurity: {3}/{4}\r\nUser: {5}",
                endpoint.Description.Server.ApplicationName,
			    hostname,
			    protocol,
                SecurityPolicies.GetDisplayName(endpoint.Description.SecurityPolicyUri),
                endpoint.Description.SecurityMode,
                user);
        }
        #endregion

        #region Event Handlers
        private async void NewMI_Click(object sender, EventArgs e)
        {
            try
            {
                ApplicationDescription server = new DiscoveredServerListDlg().ShowDialog(null, m_configuration);

                if (server == null)
                {
                    return;
                }

                ConfiguredEndpoint endpoint = await new ConfiguredServerDlg().ShowDialog(server, m_configuration);

                if (endpoint == null)
                {
                    return;
                }

                AddItem(endpoint);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void ConfigureMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = SelectedTag as ConfiguredEndpoint;

                if (endpoint == null)
                {
                    return;
                }

                endpoint = await new ConfiguredServerDlg().ShowDialog(endpoint, m_configuration);

                if (endpoint == null)
                {
                    return;
                }

                UpdateItem((ListViewItem)ItemsLV.SelectedItems[0], endpoint);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = SelectedTag as ConfiguredEndpoint;
                
                if (endpoint == null)
                {
                    return;
                }

                ItemsLV.Items.Remove(ItemsLV.SelectedItems[0]);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
#endregion
    }
}
