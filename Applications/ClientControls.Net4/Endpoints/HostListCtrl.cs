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
using System.Net;

using Opc.Ua.Configuration;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A list of hosts.
    /// </summary>
    public partial class HostListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initalize the control.
        /// </summary>
        public HostListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);            
            m_enumerator = new HostEnumerator();
            m_enumerator.HostsDiscovered += new EventHandler<HostEnumeratorEventArgs>(HostEnumerator_HostsDiscovered);
        }
        #endregion
        
        #region Private Fields
        // The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{ 
			new object[] { "Name",        HorizontalAlignment.Left, null },  
			new object[] { "Addresses",   HorizontalAlignment.Left, null }
		};
        
        private HostEnumerator m_enumerator;
        private bool m_waitingForHosts;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays a list of servers in the control.
        /// </summary>
        public void Initialize(string domain)
        {
            ItemsLV.Items.Clear();

            this.Instructions = Utils.Format("Discovering hosts on domain '{0}'.", domain);
            AdjustColumns();

            m_waitingForHosts = true;
            m_enumerator.Start(domain);
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Finds the addresses for the specified host.
        /// </summary>
        private void OnFetchAddresses(object state)
        {
            ListViewItem listItem = state as ListViewItem;

            if (listItem == null)
            {
                return;
            }

            string hostname = listItem.Tag as string;

            if (hostname == null)
            {
                return;
            }

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(hostname);

                StringBuilder buffer = new StringBuilder();

                for (int ii = 0; ii < addresses.Length; ii++)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.AppendFormat("{0}", addresses[ii]);
                }

                ThreadPool.QueueUserWorkItem(new WaitCallback(OnUpdateAddress), new object[] { listItem, buffer.ToString() });
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not get ip addresses for host: {0}", hostname);                
                ThreadPool.QueueUserWorkItem(new WaitCallback(OnUpdateAddress), new object[] { listItem, e.Message });
            }
        }

        /// <summary>
        /// Updates the addresses for a host.
        /// </summary>
        private void OnUpdateAddress(object state)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new WaitCallback(OnUpdateAddress), state);
                return;
            }
            
            ListViewItem listItem = ((object[])state)[0] as ListViewItem;

            if (listItem == null)
            {
                return;
            }

            string addresses = ((object[])state)[1] as string;

            if (addresses == null)
            {
                return;
            }

			listItem.SubItems[1].Text = addresses;

            AdjustColumns();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Updates an item in the control.
        /// </summary>
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            string hostname = listItem.Tag as string;

            if (hostname == null)
            {
                base.UpdateItem(listItem, hostname);
                return;
            }

			listItem.SubItems[0].Text = String.Format("{0}", hostname);
			listItem.SubItems[1].Text = "<Unknown>";

            listItem.ImageKey = GuiUtils.Icons.Computer;

            ThreadPool.QueueUserWorkItem(new WaitCallback(OnFetchAddresses), listItem);
        }
        #endregion
        
        #region Event Handlers
        private void HostEnumerator_HostsDiscovered(object sender, HostEnumeratorEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler<HostEnumeratorEventArgs>(HostEnumerator_HostsDiscovered), sender, e);
                return;
            }

            // check if this is the first callback.
            if (m_waitingForHosts)
            {
                ItemsLV.Items.Clear();
                m_waitingForHosts = false;
            }

            // populate list with hostnames.
            if (e != null && e.Hostnames != null)
            {
                foreach (string hostname in e.Hostnames)
                {
                    AddItem(hostname);
                }
            }
                
            AdjustColumns();
        }
        #endregion
    }
}
