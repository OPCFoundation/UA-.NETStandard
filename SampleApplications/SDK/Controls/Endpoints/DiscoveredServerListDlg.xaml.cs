/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
