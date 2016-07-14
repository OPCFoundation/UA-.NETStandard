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
using System.Collections.Generic;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class NotificationMessageListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public NotificationMessageListCtrl()
        {
            MaxMessageCount = 10;

            InitializeComponent();                        
			//SetColumns(m_ColumnNames);

            //ItemsLV.Sorting = SortOrder.Descending;
            m_SessionNotification = new NotificationEventHandler(Session_Notification);
        }
		#endregion

        #region Private Fields
        private Session m_session;
        private Subscription m_subscription;
        private NotificationEventHandler m_SessionNotification;
        private int m_maxMessageCount;
#endregion

#region Public Interface
        /// <summary>
        /// The maximum number of messages displayed in the control.
        /// </summary>
        public int MaxMessageCount
        {
            get { return m_maxMessageCount;  }
            set { m_maxMessageCount = value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            //AdjustColumns();
        }

        /// <summary>
        /// Initializes the control with the session/subscription indicated.
        /// </summary>
        public void Initialize(Session session, Subscription subscription)
        {
            // do nothing if nothing has changed.
            if (Object.ReferenceEquals(session, m_session) && Object.ReferenceEquals(subscription, m_subscription))
            {
                return;
            }

            // subscription to event notifications.
            if (!Object.ReferenceEquals(session, m_session))
            {
                if (m_session != null)
                {
                    m_session.Notification -= m_SessionNotification;
                }

                if (session != null)
                {
                    session.Notification += m_SessionNotification;
                }
            }

            Clear();

            m_session = session;
            m_subscription = subscription;

            // nothing to do if no session provided.
            if (m_session == null)
            {
                return;
            }                     
                        
            List<ItemData> tags = new List<ItemData>();

            // display only items for current subscription.
            if (subscription != null)
            {
                foreach (NotificationMessage item in subscription.Notifications)
                {
                    tags.Insert(0, new ItemData(subscription, item));
                }
            }

            // display all notifications for all subscriptions.
            else
            {
                foreach (Subscription item1 in m_session.Subscriptions)
                {
                    foreach (NotificationMessage item2 in item1.Notifications)
                    {
                        tags.Insert(0, new ItemData(item1, item2));
                    }
                }
            }
            
            // update control.
            Update(tags);
        }
#endregion
        
#region ItemData Class
        /// <summary>
        /// Stores the data associated with a list view item.
        /// </summary>
        private class ItemData
        {
            public Subscription        Subscription;
            public NotificationMessage NotificationMessage;

            public ItemData(
                Subscription        subscription,
                NotificationMessage notificationMessage)
            {
                Subscription        = subscription;
                NotificationMessage = notificationMessage;
            }
        }
#endregion

#region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
		}

        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            ItemData itemData = item as ItemData;

			if (itemData == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

            int events = 0;
            int datachanges = 0;
            int notifications = 0;

            foreach (ExtensionObject notification in itemData.NotificationMessage.NotificationData)
            {
                notifications++;

                if (ExtensionObject.IsNull(notification))
                {
                    continue;
                }

                DataChangeNotification datachangeNotification = notification.Body as DataChangeNotification;

                if (datachangeNotification != null)
                {
                    datachanges += datachangeNotification.MonitoredItems.Count;
                }

                EventNotificationList EventNotification = notification.Body as EventNotificationList;

                if (EventNotification != null)
                {
                    events += EventNotification.Events.Count;
                }
            }

			listItem.Tag = item;
        }
#endregion

        private void Update(List<ItemData> tags)
        {
            if (tags.Count > MaxMessageCount)
            {
                tags.RemoveRange(MaxMessageCount, tags.Count-MaxMessageCount);
            }
                            
            BeginUpdate();
            
            foreach (ItemData tag in tags)
            {
                AddItem(tag);
            }

            EndUpdate();
        }

        private void Session_Notification(Session sender, NotificationEventArgs e)
        {
            try
            {
                if (m_subscription != null)
                {
                    if (!Object.ReferenceEquals(m_subscription, e.Subscription))
                    {
                        return;
                    }
                }

                // get the current control contents.
                List<ItemData> tags = new List<ItemData>();

                foreach (ListViewItem item in ItemsLV.Items)
                {
                    ItemData tag = item.Tag as ItemData;

                    if (tag != null)
                    {
                        tags.Add(tag);
                    }
                }

                tags.Insert(0, new ItemData(e.Subscription, e.NotificationMessage));

                // update control.
                Update(tags);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                for (int ii = 0; ii < ItemsLV.SelectedItems.Count;)
                {
                    ItemsLV.SelectedItems.RemoveAt(ii);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ClearMI_Click(object sender, EventArgs e)
        {
            try
            {
                Clear();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void RepublishMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
    }
}
