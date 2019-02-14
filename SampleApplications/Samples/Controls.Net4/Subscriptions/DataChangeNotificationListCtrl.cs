/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class DataChangeNotificationListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public DataChangeNotificationListCtrl()
        {
            InitializeComponent();                        
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private int m_maxChangeCount = 20;
        private bool m_showHistory = false;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Item",        HorizontalAlignment.Left, null },
			new object[] { "Variable",    HorizontalAlignment.Left, null },
			new object[] { "Value",       HorizontalAlignment.Left, String.Empty, 250 },
			new object[] { "Status",      HorizontalAlignment.Left, String.Empty },
			new object[] { "Source Time", HorizontalAlignment.Center, String.Empty },
			new object[] { "Server Time", HorizontalAlignment.Center, String.Empty }
		};
		#endregion

        #region Public Interface
        /// <summary>
        /// The maximum number of changes to display in the control.
        /// </summary>
        [DefaultValue(20)]
        public int MaxChangeCount
        {
            get { return m_maxChangeCount;  }
            set { m_maxChangeCount = value; }
        }
        
        /// <summary>
        /// Whether to show previous values in the control after an update.
        /// </summary>
        [DefaultValue(false)]
        public bool ShowHistory
        {
            get { return m_showHistory;  }
            set { m_showHistory = value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Sets the nodes in the control.
        /// </summary>
        public void Initialize(Subscription subscription, MonitoredItem monitoredItem)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");
            
            Clear();
                        
            // start receiving notifications from the new subscription.
            m_subscription  = subscription;
            m_monitoredItem = monitoredItem;

            // get the events.
            List<MonitoredItemNotification> changes = new List<MonitoredItemNotification>();

            foreach (NotificationMessage notification in m_subscription.Notifications)
            {
                foreach (MonitoredItemNotification change in notification.GetDataChanges(false))
                {
                    if (m_monitoredItem != null)
                    {
                        if (m_monitoredItem.ClientHandle != change.ClientHandle)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (m_subscription.FindItemByClientHandle(change.ClientHandle) == null)
                        {
                            continue;
                        }
                    }

                    changes.Add(change);

                    if (changes.Count >= MaxChangeCount)
                    {
                        break;
                    }
                }

                if (changes.Count >= MaxChangeCount)
                {
                    break;
                }
            }

            UpdateChanges(changes, 0);
            AdjustColumns();
        }
        
        /// <summary>
        /// Processes a new notification.
        /// </summary>
        public void NotificationReceived(NotificationEventArgs e)
        {
            // get the changes.
            List<MonitoredItemNotification> changes = new List<MonitoredItemNotification>();

            foreach (MonitoredItemNotification change in e.NotificationMessage.GetDataChanges(false))
            {
                if (m_monitoredItem != null)
                {
                    if (m_monitoredItem.ClientHandle != change.ClientHandle)
                    {
                        continue;
                    }
                }
                else
                {
                    if (m_subscription.FindItemByClientHandle(change.ClientHandle) == null)
                    {
                        continue;
                    }
                }

                changes.Add(change);
            }

            // check if nothing more to do.
            if (changes.Count == 0)
            {
                return;
            }

            int offset = changes.Count;

            if (m_showHistory)
            {
                // fill in earlier changes.
                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    MonitoredItemNotification change = listItem.Tag as MonitoredItemNotification;
                
                    if (change == null)
                    {
                        continue;
                    }

                    if (m_monitoredItem != null)
                    {
                        if (m_monitoredItem.ClientHandle != change.ClientHandle)
                        {
                            continue;
                        }
                    }

                    changes.Add(change);

                    if (changes.Count >= MaxChangeCount)
                    {
                        break;
                    }
                }

                // ensure the newest changes appear first.
                changes.Reverse();
            }

            UpdateChanges(changes, offset);
            AdjustColumns();
        }
        
        /// <summary>
        /// Processes a new notification.
        /// </summary>
        public void NotificationReceived(MonitoredItemNotificationEventArgs e)
        {
            MonitoredItemNotification change = e.NotificationValue as MonitoredItemNotification;
        
            if (change == null)
            {
                return;
            }
            
            if (m_monitoredItem != null)
            {
                if (m_monitoredItem.ClientHandle != change.ClientHandle)
                {
                    return;
                }
            }

            // add new change.
            List<MonitoredItemNotification> changes = new List<MonitoredItemNotification>();
            changes.Add(change);

            // fill in earlier changes.            
            if (m_showHistory)
            {
                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    change = listItem.Tag as MonitoredItemNotification;
                
                    if (change == null)
                    {
                        continue;
                    }

                    if (m_monitoredItem != null)
                    {
                        if (m_monitoredItem.ClientHandle != change.ClientHandle)
                        {
                            continue;
                        }
                    }

                    changes.Add(change);

                    if (changes.Count >= MaxChangeCount)
                    {
                        break;
                    }
                }
            }
            
            UpdateChanges(changes, 1);
            AdjustColumns();
        }
                
        /// <summary>
        /// Processes a change to the subscription.
        /// </summary>
        public void SubscriptionChanged(SubscriptionStateChangedEventArgs e)
        {
            if ((e.Status & SubscriptionChangeMask.ItemsDeleted) != 0)
            {
                // collect events for items that have been deleted.
                List<ListViewItem> itemsToRemove = new List<ListViewItem>();

                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    MonitoredItemNotification change = listItem.Tag as MonitoredItemNotification;

                    if (change != null)
                    {
                        if (m_subscription.FindItemByClientHandle(change.ClientHandle) == null)
                        {
                            itemsToRemove.Add(listItem);
                        }
                    }
                }
                
                // remove events for items that have been deleted.
                foreach (ListViewItem listItem in itemsToRemove)
                {
                    listItem.Remove();
                }           
            }
        }

        /// <summary>
        /// Updates the display after the publish status for the subscription changes.
        /// </summary>
        public void PublishStatusChanged()
        {
            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                MonitoredItemNotification change = listItem.Tag as MonitoredItemNotification;

                if (change != null)
                {
                    UpdateItem(listItem, change);
                }
            }
            
            AdjustColumns();
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            ViewMI.Enabled   = ItemsLV.SelectedItems.Count == 1;
            DeleteMI.Enabled = ItemsLV.SelectedItems.Count > 0;
		}
        
        /// <see cref="BaseListCtrl.PickItems" />
        protected override void PickItems()
        {
            base.PickItems();
            ViewMI_Click(this, null);
        }
                        
        /// <summary>
        /// Updates the events displayed in the control.
        /// </summary>
        private void UpdateChanges(IList<MonitoredItemNotification> changes, int offset)
        {
            // save selected indexes.
            List<int> indexes = new List<int>(ItemsLV.SelectedIndices.Count);

            foreach (int index in ItemsLV.SelectedIndices)
            {
                indexes.Add(index);
            }
                     
            // add all new values.
            if (m_showHistory)
            {
                BeginUpdate();

                foreach (MonitoredItemNotification change in changes)
                {
                    AddItem(change);
                }

                EndUpdate();
            }

            // only update changed values.
            else
            {          
                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    listItem.ForeColor = Color.Gray;
                }

                for (int ii = changes.Count-1; ii >= 0; ii--)
                {
                    bool found = false;

                    foreach (ListViewItem listItem in ItemsLV.Items)
                    {
                        MonitoredItemNotification change = listItem.Tag as MonitoredItemNotification;

                        if (change != null && change.ClientHandle == changes[ii].ClientHandle)
                        {
                            UpdateItem(listItem, changes[ii]);
                            found = true;
                            listItem.ForeColor = Color.Empty;
                            break;
                        }
                    }

                    if (!found)
                    {                
                        AddItem(changes[ii]);
                    }
                }
            }

            // preserve selection.
            foreach (int index in indexes)
            {
                ItemsLV.Items[index].Selected = false;

                if (index+offset < ItemsLV.Items.Count)
                {
                    ItemsLV.Items[index+offset].Selected = true;
                }
            }
        }

        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
			MonitoredItemNotification change = item as MonitoredItemNotification;

			if (change == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}                        
            
            // fill in the columns.
            listItem.SubItems[0].Text = String.Format("[{0}]", change.ClientHandle);

            MonitoredItem monitoredItem = null;
            
            if (m_subscription != null)
            {
                monitoredItem = m_subscription.FindItemByClientHandle(change.ClientHandle);
            }

            if (monitoredItem != null)
            {
                listItem.SubItems[1].Text = String.Format("{0}", monitoredItem.DisplayName);
            }
            else
            {
                listItem.SubItems[1].Text = "(unknown)";
            }
                
            listItem.SubItems[2].Text = String.Format("{0}", change.Value.WrappedValue);

            // check of publishing has stopped for some reason.
            if (m_subscription.PublishingStopped)
            {
                listItem.SubItems[3].Text = String.Format("{0}", (StatusCode)StatusCodes.UncertainNoCommunicationLastUsableValue);
            }
            else
            {
                listItem.SubItems[3].Text = change.Value.StatusCode.ToString();
            }

            DateTime time = change.Value.SourceTimestamp;

            if (time != null && time != DateTime.MinValue)
            {
                listItem.SubItems[4].Text = String.Format("{0:HH:mm:ss.fff}", time.ToLocalTime());
            }
            else
            {
                listItem.SubItems[4].Text = String.Empty;
            }
            
            time = change.Value.ServerTimestamp;

            if (time != null && time != DateTime.MinValue)
            {
                listItem.SubItems[5].Text = String.Format("{0:HH:mm:ss.fff}", time.ToLocalTime());
            }
            else
            {
                listItem.SubItems[5].Text = String.Empty;
            }
   
            listItem.Tag = change;
            listItem.ForeColor = (m_subscription.PublishingStopped)?Color.Red:Color.Empty;
        }
        #endregion
               
        #region Event Handlers
        private void ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                MonitoredItemNotification change = SelectedTag as MonitoredItemNotification;

                if (change == null)
                {
                    return;
                }

                new ComplexValueEditDlg().ShowDialog(change);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
