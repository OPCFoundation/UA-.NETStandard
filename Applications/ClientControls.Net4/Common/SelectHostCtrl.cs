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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Configuration;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a drop down list of hosts.
    /// </summary>
    public partial class SelectHostCtrl : UserControl
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
            get { return ConnectBTN.Text;  }
            set { ConnectBTN.Text = value; }
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
                defaultHost = System.Net.Dns.GetHostName();
                
                if (hostnames != null && hostnames.Count > 0)
                {
                    defaultHost = hostnames[0];
                }
            }

            // set the current selection.
            m_selectedIndex = HostsCB.FindString(defaultHost);

            if (m_selectedIndex == -1)
            {
                m_selectedIndex = HostsCB.Items.Add(defaultHost);
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
                    m_selectedIndex = HostsCB.FindString(hostname);

                    if (m_selectedIndex == -1)
                    {
                        m_selectedIndex = HostsCB.Items.Add(hostname);
                    }
                }
                
                HostsCB.SelectedIndex = m_selectedIndex;
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ConnectBTN_Click(object sender, EventArgs e)
        {
            try
            {   int index = HostsCB.SelectedIndex;

                if (index == 0)
                {
                    return;
                }

                if (m_HostConnected != null)
                {
                    if (index == -1)
                    {
                        if (!String.IsNullOrEmpty(HostsCB.Text))
                        {
                            m_HostConnected(this, new SelectHostCtrlEventArgs(HostsCB.Text));
                        }
                        
                        // add host to list.
                        m_selectedIndex = HostsCB.FindString(HostsCB.Text);

                        if (m_selectedIndex == -1)
                        {
                            m_selectedIndex = HostsCB.Items.Add(HostsCB.Text);
                        }

                        return;
                    }
                
                    m_HostConnected(this, new SelectHostCtrlEventArgs((string)HostsCB.SelectedItem));
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
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
