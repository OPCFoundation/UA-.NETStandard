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
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using Windows.System;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a drop down list of hosts.
    /// </summary>
    public sealed partial class SelectHostCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initializes the control.
        /// </summary>
        public SelectHostCtrl()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private int m_selectedIndex;
        private bool m_selectDomains;
        private event EventHandler<SelectHostCtrlEventArgs> m_HostSelected;
        private event EventHandler<SelectHostCtrlEventArgs> m_HostConnected;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Whether the control is used to select domains instead of hosts.
        /// </summary>
        [System.ComponentModel.DefaultValue(false)]
        public bool SelectDomains
        {
            get { return m_selectDomains;  }
            set { m_selectDomains = value; }
        }

        /// <summary>
        /// The text displayed on the connect button.
        /// </summary>
        [System.ComponentModel.DefaultValue("Connect")]
        public string CommandText
        {
            get { return ConnectBTN.Content.ToString();  }
            set { ConnectBTN.Content = value; }
        }

        /// <summary>
        /// Displays a set of hostnames in the control.
        /// </summary>
        public void Initialize(string defaultHost, IList<string> hostnames)
        {
            HostsCB.Items.Clear();

            // add option to browse for hosts.
            HostsCB.Items.Add("<Browse...>");

            // add any existing hosts.
            if (hostnames != null)
            {
                foreach (string hostname in hostnames)
                {
                    HostsCB.Items.Add(hostname);
                }
            }

            // set a suitable default hostname.
            if (String.IsNullOrEmpty(defaultHost))
            {
                if (!m_selectDomains)
                {
                    defaultHost = Utils.GetHostName();
                }
                else
                {
                    defaultHost = CredentialCache.DefaultNetworkCredentials.Domain;
                }

                if (hostnames != null && hostnames.Count > 0)
                {
                    defaultHost = hostnames[0];
                }
            }

            // set the current selection.
            m_selectedIndex = HostsCB.Items.IndexOf(HostsCB.FindName(defaultHost));

            if (m_selectedIndex == -1)
            {
                HostsCB.Items.Add(defaultHost);
                m_selectedIndex = HostsCB.SelectedIndex;
            }

            HostsCB.SelectedIndex = m_selectedIndex;
        }

        /// <summary>
        /// Raised when a host is selected in the control.
        /// </summary>
        public event EventHandler<SelectHostCtrlEventArgs> HostSelected
        {
            add { m_HostSelected += value; }
            remove { m_HostSelected -= value; }
        }

        /// <summary>
        /// Raised when the connect button is clicked.
        /// </summary>
        public event EventHandler<SelectHostCtrlEventArgs> HostConnected
        {
            add { m_HostConnected += value; }
            remove { m_HostConnected -= value; }
        }
        #endregion
        
        #region Event Handlers
        private void HostsCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {   
                if (HostsCB.SelectedIndex != 0)
                {
                    if (m_HostSelected != null)
                    {
                        m_HostSelected(this, new SelectHostCtrlEventArgs((string)HostsCB.SelectedItem));
                    }

                    m_selectedIndex = HostsCB.SelectedIndex;
                    return;
                }

                if (!m_selectDomains)
                {
                    // prompt user to select a host.
                    string hostname = new HostListDlg().ShowDialog(null);

                    if (hostname == null)
                    {
                        HostsCB.SelectedIndex = m_selectedIndex;
                        return;
                    }
                    
                    // set the current selection.
                    m_selectedIndex = HostsCB.Items.IndexOf(hostname);
                    if (m_selectedIndex == -1)
                    {
                        HostsCB.Items.Add(hostname);
                        m_selectedIndex = HostsCB.SelectedIndex;
                    }
                }
                
                HostsCB.SelectedIndex = m_selectedIndex;
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ConnectBTN_Click(object sender, EventArgs e)
        {
            try
            {
                int index = HostsCB.SelectedIndex;
                if (index == 0)
                {
                    return;
                }

                if (m_HostConnected != null)
                {
                    if (index == -1)
                    {
                        if (!String.IsNullOrEmpty(HostsCB.SelectedItem.ToString()))
                        {
                            m_HostConnected(this, new SelectHostCtrlEventArgs(HostsCB.SelectedItem.ToString()));
                        }
                        
                        m_selectedIndex = HostsCB.Items.IndexOf(HostsCB.SelectedIndex);
                        return;
                    }
                
                    m_HostConnected(this, new SelectHostCtrlEventArgs((string)HostsCB.SelectedItem));
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }

    #region SelectHostCtrlEventArgs Class
    /// <summary>
    /// The event arguments passed when the SelectHostCtrlEventArgs raises events.
    /// </summary>
    public class SelectHostCtrlEventArgs : EventArgs
    {
        /// <summary>
        /// Initilizes the object with the current hostname.
        /// </summary>
        public SelectHostCtrlEventArgs(string hostname)
        {
            m_hostname = hostname;
        }

        /// <summary>
        /// The current hostname.
        /// </summary>
        public string Hostname
        {
            get { return m_hostname; }
        }

        private string m_hostname;
    }
    #endregion
}
