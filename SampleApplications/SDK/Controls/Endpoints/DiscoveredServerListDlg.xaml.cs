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

using Opc.Ua;
using Opc.Ua.Client.Controls;
using System;
using System.Collections.Generic;
using System.Reflection;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Allows the user to browse a list of servers.
    /// </summary>
    public partial class DiscoveredServerListDlg : Page
    {
        #region Constructors
        /// <summary>
        /// Initializes the dialog.
        /// </summary>
        public DiscoveredServerListDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private string m_hostname;
        private ApplicationDescription m_server;
        private ApplicationConfiguration m_configuration;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ApplicationDescription ShowDialog(string hostname, ApplicationConfiguration configuration)
        {
            m_configuration = configuration;

            if (String.IsNullOrEmpty(hostname))
            {
                hostname = Utils.GetHostName();
            }

            m_hostname = hostname;
            m_server = null;

            List<string> hostnames = new List<string>();

            HostNameCTRL.Initialize(hostname, hostnames);
            ServersCTRL.Initialize(hostname, configuration);

            OkBTN.IsEnabled = false;

            return m_server;
        }
        #endregion
        
        #region Event Handlers
        private void HostNameCTRL_HostSelected(object sender, SelectHostCtrlEventArgs e)
        {
            try
            {
                if (m_hostname != e.Hostname)
                {
                    m_hostname = e.Hostname;
                    ServersCTRL.Initialize(m_hostname, m_configuration);
                    m_server = null;
                    OkBTN.IsEnabled = false;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void HostNameCTRL_HostConnected(object sender, SelectHostCtrlEventArgs e)
        {
            try
            {
                m_hostname = e.Hostname;
                ServersCTRL.Initialize(m_hostname, m_configuration);
                m_server = null;
                OkBTN.IsEnabled = false;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ServersCTRL_ItemsSelected(object sender, ListItemActionEventArgs e)
        {
            try
            {
                m_server = null;

                foreach (ApplicationDescription server in e.Items)
                {
                    m_server = server;
                    break;
                }

                OkBTN.IsEnabled = m_server != null;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ServersCTRL_ItemsPicked(object sender, ListItemActionEventArgs e)
        {
            try
            {
                m_server = null;

                foreach (ApplicationDescription server in e.Items)
                {
                    m_server = server;
                    break;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }
}
