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
        }
        #endregion
        
        #region Private Fields
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
