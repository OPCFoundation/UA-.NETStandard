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
