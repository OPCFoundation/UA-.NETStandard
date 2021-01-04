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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Reflection;

using Opc.Ua.Client.Controls;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A list of servers.
    /// </summary>
    public partial class ConfiguredServerListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initalize the control.
        /// </summary>
        public ConfiguredServerListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
        }
        #endregion
        
        #region Private Fields
        // The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{ 
			new object[] { "Name",          HorizontalAlignment.Left, null },  
			new object[] { "Host",          HorizontalAlignment.Left, null },
			new object[] { "Protocol",      HorizontalAlignment.Left, null },
			new object[] { "Security Mode", HorizontalAlignment.Left, null },
			new object[] { "User Token",    HorizontalAlignment.Left, null }
		};
        
        private ApplicationConfiguration m_configuration;
        private ConfiguredEndpointCollection m_endpoints;
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

            AdjustColumns();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Enables context menu items.
        /// </summary>
        protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            base.EnableMenuItems(clickedItem);

            NewMI.Enabled = true;
            
            if (clickedItem != null)
            {
                ConfiguredEndpoint endpoint = clickedItem.Tag as ConfiguredEndpoint;

                if (endpoint == null)
                {
                    return;
                }

                ConfigureMI.Enabled = true;
                DeleteMI.Enabled = true;
            }
        }

        /// <summary>
        /// Updates an item in the control.
        /// </summary>
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            ConfiguredEndpoint endpoint = listItem.Tag as ConfiguredEndpoint;

            if (endpoint == null)
            {
                base.UpdateItem(listItem, endpoint);
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

			listItem.SubItems[0].Text = String.Format("{0}", endpoint.Description.Server.ApplicationName);
			listItem.SubItems[1].Text = String.Format("{0}", hostname);
			listItem.SubItems[2].Text = String.Format("{0}", protocol); 

			listItem.SubItems[3].Text = String.Format(
                "{0}/{1}", 
                SecurityPolicies.GetDisplayName(endpoint.Description.SecurityPolicyUri),
                endpoint.Description.SecurityMode);
            
			listItem.SubItems[4].Text = "<Unknown>";

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

			    listItem.SubItems[4].Text = buffer.ToString();
            }

            listItem.ImageKey = GuiUtils.Icons.Process;
        }
        #endregion
        
        #region Event Handlers
        private void NewMI_Click(object sender, EventArgs e)
        {
            try
            {
                ApplicationDescription server = new DiscoveredServerListDlg().ShowDialog(null, m_configuration);

                if (server == null)
                {
                    return;
                }

                ConfiguredEndpoint endpoint = new ConfiguredServerDlg().ShowDialog(server, m_configuration);

                if (endpoint == null)
                {
                    return;
                }

                AddItem(endpoint);
                AdjustColumns();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ConfigureMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = SelectedTag as ConfiguredEndpoint;
                
                if (endpoint == null)
                {
                    return;
                }

                endpoint = new ConfiguredServerDlg().ShowDialog(endpoint, m_configuration);

                if (endpoint == null)
                {
                    return;
                }
                
                UpdateItem(ItemsLV.SelectedItems[0], endpoint);
                AdjustColumns();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
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

                ItemsLV.SelectedItems[0].Remove();
                AdjustColumns();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
