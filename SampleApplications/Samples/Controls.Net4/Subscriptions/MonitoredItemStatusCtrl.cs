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
    public partial class MonitoredItemStatusCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MonitoredItemStatusCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
        }
		#endregion

        #region Private Fields
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        
        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "ID",             HorizontalAlignment.Center, null       },
			new object[] { "Name",           HorizontalAlignment.Left,   null       },
			new object[] { "Class",          HorizontalAlignment.Left,   "Variable" },
			new object[] { "Sampling Rate",  HorizontalAlignment.Center, null       }, 
			new object[] { "Queue Size",     HorizontalAlignment.Center, "0"        }, 
			new object[] { "Value",          HorizontalAlignment.Left,   "",        200 }, 
			new object[] { "Status",         HorizontalAlignment.Left,   "",        }, 
			new object[] { "Timestamp",      HorizontalAlignment.Center, ""         },
		};
		#endregion

        #region Public Interface
        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Displays the items for the specified subscription in the control.
        /// </summary>
        public void Initialize(MonitoredItem monitoredItem)
        {
            // do nothing if same subscription provided.
            if (Object.ReferenceEquals(m_monitoredItem, monitoredItem))
            {
                return;
            }

            m_monitoredItem = monitoredItem;
            m_subscription  = null;

            Clear();
            
            if (m_monitoredItem != null)
            {
                m_subscription  = monitoredItem.Subscription;
                UpdateItems();
            }
        }

        /// <summary>
        /// Displays the items for the specified subscription in the control.
        /// </summary>
        public void Initialize(Subscription subscription)
        {
            // do nothing if same subscription provided.
            if (Object.ReferenceEquals(m_subscription, subscription))
            {
                return;
            }

            m_monitoredItem = null;
            m_subscription  = subscription;

            Clear();
            
            if (m_subscription != null)
            {
                UpdateItems();
            }
        }
        
        /// <summary>
        /// Called when the subscription changes.
        /// </summary>
        public void SubscriptionChanged(SubscriptionStateChangedEventArgs e)
        {
            UpdateItems();
        }

        /// <summary>
        /// Refreshes the state of all items displayed in the control.
        /// </summary>
        public void UpdateItems()
        {
            if (m_subscription != null)
            {
                BeginUpdate();

                foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
                {
                    if (m_monitoredItem == null || monitoredItem.ClientHandle == m_monitoredItem.ClientHandle)
                    {
                        AddItem(monitoredItem);
                    }
                }

                EndUpdate();

                AdjustColumns();
            }
        }
        
        /// <summary>
        /// Apply any changes to the set of items.
        /// </summary>
        public void ApplyChanges()
        {
            if (m_subscription != null)
            {
                m_subscription.ApplyChanges();

                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    UpdateItem(listItem, listItem.Tag);
                }

                AdjustColumns();
            }
        }
        #endregion
        
        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            // no menu defined at this time.
		}
        
        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
			MonitoredItem monitoredItem = item as MonitoredItem;

			if (monitoredItem == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

		    listItem.SubItems[0].Text = String.Format("{0}", monitoredItem.Status.Id);
		    listItem.SubItems[1].Text = String.Format("{0}", monitoredItem.DisplayName);
		    listItem.SubItems[2].Text = String.Format("{0}", monitoredItem.NodeClass);
            listItem.SubItems[3].Text = String.Format("{0}", monitoredItem.Status.SamplingInterval);
		    listItem.SubItems[4].Text = String.Format("{0}", monitoredItem.Status.QueueSize);
            listItem.SubItems[5].Text = String.Empty;
            listItem.SubItems[6].Text = String.Format("{0}", monitoredItem.Status.Error);
            listItem.SubItems[7].Text = String.Empty;

            IEncodeable value = monitoredItem.LastValue;

            if (value != null)
            {
                MonitoredItemNotification datachange = value as MonitoredItemNotification;

                if (datachange != null)
                {
                    listItem.SubItems[5].Text = String.Format("{0}", datachange.Value);

                    if (datachange.Value.SourceTimestamp != DateTime.MinValue)
                    {
                        listItem.SubItems[7].Text = String.Format("{0:HH:mm:ss.fff}", datachange.Value.SourceTimestamp.ToLocalTime());
                    }
                }

                EventFieldList eventFields = value as EventFieldList;

                if (eventFields != null)
                {
                    listItem.SubItems[5].Text = String.Format("{0}", monitoredItem.GetEventType(eventFields));
                    listItem.SubItems[7].Text = String.Format("{0:HH:mm:ss.fff}", monitoredItem.GetEventTime(eventFields).ToLocalTime());                
                }
            }
 
			listItem.Tag = item;
        }
		#endregion
    }
}
