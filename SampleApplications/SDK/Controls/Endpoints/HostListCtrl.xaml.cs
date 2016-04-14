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
using System.Net;
using Windows.UI.Xaml.Controls;
using Opc.Ua.Configuration;
using Opc.Ua;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A list of hosts.
    /// </summary>
    public sealed partial class HostListCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initalize the control.
        /// </summary>
        public HostListCtrl()
        {
            InitializeComponent();
            m_enumerator = new HostEnumerator();
            m_enumerator.HostsDiscovered += new EventHandler<HostEnumeratorEventArgs>(HostEnumerator_HostsDiscovered);
        }
        #endregion
        
        #region Private Fields
        private HostEnumerator m_enumerator;
        private bool m_waitingForHosts;
        private bool m_updating = false;
        private int m_updateCount = 0;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays a list of servers in the control.
        /// </summary>
        public void Initialize(string domain)
        {
            ItemsLV.Items.Clear();

            this.Instructions.Text = Utils.Format("Discovering hosts on domain '{0}'.", domain);

            m_waitingForHosts = true;
            m_enumerator.Start(domain);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Finds the addresses for the specified host.
        /// </summary>
        private async Task OnFetchAddresses(object state)
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
                IPAddress[] addresses = await Utils.GetHostAddresses(hostname);

                StringBuilder buffer = new StringBuilder();

                for (int ii = 0; ii < addresses.Length; ii++)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.AppendFormat("{0}", addresses[ii]);
                }

                await Task.Run(() =>
                {
                    OnUpdateAddress(new object[] { listItem, buffer.ToString() });
                });
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not get ip addresses for host: {0}", hostname);
                await Task.Run(() =>
                {
                    OnUpdateAddress(new object[] { listItem, e.Message });
                });
            }
        }

        /// <summary>
        /// Updates the addresses for a host.
        /// </summary>
        private void OnUpdateAddress(object state)
        {
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

			listItem.Content = String.Format("Host: {0}\r\nAddress: {1}", listItem.Tag, addresses);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Updates an item in the control.
        /// </summary>
        public async void UpdateItem(ListViewItem listItem, object item)
        {
            string hostname = listItem.Tag as string;

            if (hostname == null)
            {
                listItem.Tag = hostname;
                return;
            }

			listItem.Content = String.Format("Host: {0}\r\nAddress: <Unknown>", hostname);

            await OnFetchAddresses(listItem);

        }
        #endregion
        
        #region Event Handlers
        private void HostEnumerator_HostsDiscovered(object sender, HostEnumeratorEventArgs e)
        {
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
        }

        /// <summary>
		/// Adds an item to the list.
		/// </summary>
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
        #endregion
    }
}
