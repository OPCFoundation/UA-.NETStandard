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
    public partial class EventNotificationListListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public EventNotificationListListCtrl()
        {
            InitializeComponent();                        
			SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private int m_maxEventCount = 20;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Item",     HorizontalAlignment.Left, null },
			new object[] { "Type",     HorizontalAlignment.Left, null },
			new object[] { "Source",   HorizontalAlignment.Left, String.Empty },
			new object[] { "Time",     HorizontalAlignment.Center, String.Empty },
			new object[] { "Severity", HorizontalAlignment.Center, String.Empty },
			new object[] { "Message",  HorizontalAlignment.Left, String.Empty }
		};
		#endregion

        #region Public Interface
        /// <summary>
        /// The maximum number of events to display in the control.
        /// </summary>
        [DefaultValue(20)]
        public int MaxEventCount
        {
            get { return m_maxEventCount;  }
            set { m_maxEventCount = value; }
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
            List<EventFieldList> events = new List<EventFieldList>();

            foreach (NotificationMessage message in m_subscription.Notifications)
            {
                foreach (EventFieldList eventFields in message.GetEvents(true))
                {
                    if (m_monitoredItem != null)
                    {
                        if (m_monitoredItem.ClientHandle != eventFields.ClientHandle)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (m_subscription.FindItemByClientHandle(eventFields.ClientHandle) == null)
                        {
                            continue;
                        }
                    }
                    
                    events.Add(eventFields);

                    if (events.Count >= MaxEventCount)
                    {
                        break;
                    }
                }

                if (events.Count >= MaxEventCount)
                {
                    break;
                }
            }

            UpdateEvents(events, 0);
            AdjustColumns();
        }
        
        /// <summary>
        /// Processes a new notification.
        /// </summary>
        public void NotificationReceived(NotificationEventArgs e)
        {
            // get the events.
            List<EventFieldList> events = new List<EventFieldList>();

            foreach (EventFieldList eventFields in e.NotificationMessage.GetEvents(true))
            {
                if (m_monitoredItem != null)
                {
                    if (m_monitoredItem.ClientHandle != eventFields.ClientHandle)
                    {
                        continue;
                    }
                }
                else
                {
                    if (m_subscription.FindItemByClientHandle(eventFields.ClientHandle) == null)
                    {
                        continue;
                    }
                }   

                events.Add(eventFields);

                if (events.Count >= MaxEventCount)
                {
                    break;
                }
            }

            // check if nothing more to do.
            if (events.Count == 0)
            {
                return;
            }

            int offset = events.Count;

            // fill in earlier events.
            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                EventFieldList eventFields = listItem.Tag as EventFieldList;

                if (eventFields == null)
                {
                    continue;
                }

                if (m_monitoredItem != null)
                {
                    if (m_monitoredItem.ClientHandle != eventFields.ClientHandle)
                    {
                        continue;
                    }
                }
                
                 events.Add(eventFields);

                if (events.Count >= MaxEventCount)
                {
                    break;
                }
            }
            
            UpdateEvents(events, offset);
            AdjustColumns();
        }

        /// <summary>
        /// Processes a new notification.
        /// </summary>
        public void NotificationReceived(MonitoredItemNotificationEventArgs e)
        {
            EventFieldList eventFields = e.NotificationValue as EventFieldList;
        
            if (eventFields == null)
            {
                return;
            }
            
            if (m_monitoredItem != null)
            {
                if (m_monitoredItem.ClientHandle != eventFields.ClientHandle)
                {
                    return;
                }
            }

            // get the events.
            List<EventFieldList> events = new List<EventFieldList>();
            events.Add(eventFields);

            // fill in earlier events.
            foreach (ListViewItem listItem in ItemsLV.Items)
            {
                eventFields = listItem.Tag as EventFieldList;
                
                if (m_monitoredItem != null)
                {
                    if (m_monitoredItem.ClientHandle != eventFields.ClientHandle)
                    {
                        continue;
                    }
                }

                if (eventFields != null)
                {
                    events.Add(eventFields);
                }

                if (events.Count >= MaxEventCount)
                {
                    break;
                }
            }
            
            UpdateEvents(events, 1);
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
                    EventFieldList eventFields = listItem.Tag as EventFieldList;

                    if (eventFields != null)
                    {
                        if (m_subscription.FindItemByClientHandle(eventFields.ClientHandle) == null)
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
        private void UpdateEvents(IList<EventFieldList> events, int offset)
        {
            // save selected indexes.
            List<int> indexes = new List<int>(ItemsLV.SelectedIndices.Count);

            foreach (int index in ItemsLV.SelectedIndices)
            {
                indexes.Add(index);
            }

            // update items.
            BeginUpdate();
            
            foreach (EventFieldList eventFields in events)
            {
                AddItem(eventFields);
            }

            EndUpdate();

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
			EventFieldList eventFields = item as EventFieldList;

			if (eventFields == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}                        

            MonitoredItem monitoredItem = m_subscription.FindItemByClientHandle(eventFields.ClientHandle);

            if (monitoredItem == null)
            {
                listItem.SubItems[0].Text = String.Format("[{0}]", eventFields.ClientHandle);
                listItem.SubItems[1].Text = "(unknown)";
                listItem.SubItems[2].Text = null;
                listItem.SubItems[3].Text = null;
                listItem.SubItems[4].Text = null;
                listItem.SubItems[5].Text = null;
                                
                listItem.Tag = eventFields;
                return;
            }                       
    
            // get the event fields.
            NodeId eventType      = monitoredItem.GetFieldValue(eventFields, ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.EventType) as NodeId;
            string sourceName     = monitoredItem.GetFieldValue(eventFields, ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.SourceName) as string;
            DateTime? time        = monitoredItem.GetFieldValue(eventFields, ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.Time) as DateTime?;
            ushort? severity      = monitoredItem.GetFieldValue(eventFields, ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.Severity) as ushort?;
            LocalizedText message = monitoredItem.GetFieldValue(eventFields, ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.Message) as LocalizedText;
            
            // fill in the columns.
            listItem.SubItems[0].Text = String.Format("[{0}]", eventFields.ClientHandle);

            INode typeNode = m_subscription.Session.NodeCache.Find(eventType);

            if (typeNode == null)
            {
                listItem.SubItems[1].Text = String.Format("{0}", eventType);
            }
            else
            {
                listItem.SubItems[1].Text = String.Format("{0}", typeNode);
            }

            listItem.SubItems[2].Text = String.Format("{0}", sourceName);

            if (time != null && time.Value != DateTime.MinValue)
            {
                listItem.SubItems[3].Text = String.Format("{0:HH:mm:ss.fff}", time.Value.ToLocalTime());
            }
            else
            {
                listItem.SubItems[3].Text = String.Empty;
            }

            listItem.SubItems[4].Text = String.Format("{0}", severity);
            
            if (message != null)
            {
                listItem.SubItems[5].Text = String.Format("{0}", message.Text);
            }
            else
            {
                listItem.SubItems[5].Text = String.Empty;
            }
                        
            listItem.Tag = eventFields;
        }
        #endregion
               
        #region Event Handlers
        private void ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                EventFieldList fieldList = SelectedTag as EventFieldList;

                if (fieldList == null)
                {
                    return;
                }               

                new ComplexValueEditDlg().ShowDialog(fieldList, m_subscription.FindItemByClientHandle(fieldList.ClientHandle));
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
