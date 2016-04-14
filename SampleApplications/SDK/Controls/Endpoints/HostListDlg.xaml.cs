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

using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using System;
using System.Net;
using System.Reflection;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Allows the user to browse a list of servers.
    /// </summary>
    public sealed partial class HostListDlg : Page
    {
        #region Constructors
        /// <summary>
        /// Initializes the dialog.
        /// </summary>
        public HostListDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private string m_domain;
        private string m_hostname;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public string ShowDialog(string domain)
        {
            if (String.IsNullOrEmpty(domain))
            {
                domain = CredentialCache.DefaultNetworkCredentials.Domain;
            }

            m_domain = domain;

            DomainNameCTRL.Initialize(m_domain, null);
            HostsCTRL.Initialize(m_domain);
            OkBTN.IsEnabled = false;

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;
            
            return m_hostname;
        }
        #endregion
        
        #region Event Handlers
        private void DomainNameCTRL_HostSelected(object sender, SelectHostCtrlEventArgs e)
        {
            try
            {
                if (m_domain != e.Hostname)
                {
                    m_domain = e.Hostname;
                    HostsCTRL.Initialize(m_domain);
                    m_hostname = null;
                    OkBTN.IsEnabled = false;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void DomainNameCTRL_HostConnected(object sender, SelectHostCtrlEventArgs e)
        {
            try
            {
                m_domain = e.Hostname;
                HostsCTRL.Initialize(m_domain);
                m_hostname = null;
                OkBTN.IsEnabled = false;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void HostsCTRL_ItemsSelected(object sender, ListItemActionEventArgs e)
        {
            try
            {
                m_hostname = null;

                foreach (string hostname in e.Items)
                {
                    m_hostname = hostname;
                    break;
                }

                OkBTN.IsEnabled = !String.IsNullOrEmpty(m_hostname);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void HostsCTRL_ItemsPicked(object sender, ListItemActionEventArgs e)
        {
            try
            {
                m_hostname = null;

                foreach (string hostname in e.Items)
                {
                    m_hostname = hostname;
                    break;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion

        private void OkBTN_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {

        }
    }
}
