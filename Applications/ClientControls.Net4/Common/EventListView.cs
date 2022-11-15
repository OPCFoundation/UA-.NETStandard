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
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control which displays a list of events.
    /// </summary>
    public partial class EventListView : UserControl
    {
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public EventListView()
        {
            InitializeComponent();
        }

        #region Private Methods
        private Session m_session;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private FilterDeclaration m_filter;
        private NodeId m_areaId;
        private bool m_isSubscribed;
        private bool m_displayConditions;
        #endregion

        #region Public Members
        /// <summary>
        /// Whether the control subscribes for new events.
        /// </summary>
        public bool IsSubscribed
        {
            get { return m_isSubscribed; }
            
            set 
            {
                if (m_isSubscribed != value)
                {
                    m_isSubscribed = value;

                    if (m_session != null)
                    {
                        if (m_isSubscribed)
                        {
                            CreateSubscription();
                        }
                        else
                        {
                            DeleteSubscription();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether to display the events as conditions.
        /// </summary>
        public bool DisplayConditions
        {
            get { return m_displayConditions; }
            set { m_displayConditions = value; }
        }

        /// <summary>
        /// The context menu to use.
        /// </summary>
        public override ContextMenuStrip ContextMenuStrip
        {
            get { return this.EventsLV.ContextMenuStrip; }
            set { this.EventsLV.ContextMenuStrip = value; }
        }

        /// <summary>
        /// The event area displayed in the control.
        /// </summary>
        public NodeId AreaId
        {
            get { return m_areaId; }
        }

        /// <summary>
        /// The event filter applied to the control.
        /// </summary>
        public FilterDeclaration Filter
        {
            get { return m_filter; }
        }

        /// <summary>
        /// Changes the session.
        /// </summary>
        public void ChangeSession(Session session, bool fetchRecent)
        {
            if (Object.ReferenceEquals(session, m_session))
            {
                return;
            }

            if (m_session != null)
            {
                DeleteSubscription();
                m_session = null;
            }

            m_session = session;
            EventsLV.Items.Clear();

            if (m_session != null && m_isSubscribed)
            {
                CreateSubscription();

                if (fetchRecent)
                {
                    ReadRecentHistory();
                }
            }
        }
        
        /// <summary>
        /// Updates the control after the session has reconnected.
        /// </summary>
        public void SessionReconnected(Session session)
        {
            m_session = session;
            
            if (m_isSubscribed)
            {
                foreach (Subscription subscription in m_session.Subscriptions)
                {
                    if (Object.ReferenceEquals(subscription.Handle, this))
                    {
                        m_subscription = subscription;

                        foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
                        {
                            m_monitoredItem = monitoredItem;
                            break;
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Changes the area monitored by the control.
        /// </summary>
        public void ChangeArea(NodeId areaId, bool fetchRecent)
        {
            m_areaId = areaId;
            EventsLV.Items.Clear();

            if (fetchRecent)
            {
                ReadRecentHistory();
            }

            if (m_subscription != null)
            {
                MonitoredItem monitoredItem = new MonitoredItem(m_monitoredItem);
                monitoredItem.StartNodeId = areaId;

                m_subscription.AddItem(monitoredItem);
                m_subscription.RemoveItem(m_monitoredItem);
                m_monitoredItem = monitoredItem;

                monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

                m_subscription.ApplyChanges();
            }
        }

        /// <summary>
        /// Changes the filter used to select the events.
        /// </summary>
        public void ChangeFilter(FilterDeclaration filter, bool fetchRecent)
        {
            m_filter = filter;
            EventsLV.Items.Clear();

            int index = 0;

            if (m_filter != null)
            {
                // add or update existing columns.
                for (int ii = 0; ii < m_filter.Fields.Count; ii++)
                {
                    if (m_filter.Fields[ii].DisplayInList)
                    {
                        if (index >= EventsLV.Columns.Count)
                        {
                            EventsLV.Columns.Add(new ColumnHeader());
                        }

                        EventsLV.Columns[index].Text = m_filter.Fields[ii].InstanceDeclaration.DisplayName;
                        EventsLV.Columns[index].TextAlign = HorizontalAlignment.Left;
                        index++;
                    }
                }
            }

            // remove extra columns.
            while (index < EventsLV.Columns.Count)
            {
                EventsLV.Columns.RemoveAt(EventsLV.Columns.Count - 1);
            }

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }

            // fetch recent history.
            if (fetchRecent)
            {
                ReadRecentHistory();
            }

            // update subscription.
            if (m_subscription != null && m_filter != null)
            {
                m_monitoredItem.Filter = m_filter.GetFilter();
                m_subscription.ApplyChanges();
            }
        }

        /// <summary>
        /// Clears the event history in the control.
        /// </summary>
        public void ClearEventHistory()
        {
            EventsLV.Items.Clear();

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Adds the event history to the control.
        /// </summary>
        public void AddEventHistory(HistoryEvent events)
        {
            for (int ii = 0; ii < events.Events.Count; ii++)
            {
                ListViewItem item = CreateListItem(m_filter, events.Events[ii].EventFields);
                EventsLV.Items.Add(item);
            }

            // adjust the width of the columns.
            for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
            {
                EventsLV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Refreshes the conditions displayed.
        /// </summary>
        public void ConditionRefresh()
        {
            if (m_subscription != null)
            {
                m_subscription.ConditionRefresh();
            }
        }

        /// <summary>
        /// Returns the currently selected event at the specified index (null index is not valid).
        /// </summary>
        public VariantCollection GetSelectedEvent(int index)
        {
            if (EventsLV.SelectedItems.Count > index)
            {
                return EventsLV.SelectedItems[index].Tag as VariantCollection;
            }

            return null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private void CreateSubscription()
        {
            m_subscription = new Subscription();
            m_subscription.Handle = this;
            m_subscription.DisplayName = null;
            m_subscription.PublishingInterval = 1000;
            m_subscription.KeepAliveCount = 10;
            m_subscription.LifetimeCount = 100;
            m_subscription.MaxNotificationsPerPublish = 1000;
            m_subscription.PublishingEnabled = true;
            m_subscription.TimestampsToReturn = TimestampsToReturn.Both;

            m_session.AddSubscription(m_subscription);
            m_subscription.Create();

            m_monitoredItem = new MonitoredItem();
            m_monitoredItem.StartNodeId = m_areaId;
            m_monitoredItem.AttributeId = Attributes.EventNotifier;
            m_monitoredItem.SamplingInterval = 0;
            m_monitoredItem.QueueSize = 1000;
            m_monitoredItem.DiscardOldest = true;

            ChangeFilter(m_filter, false);

            m_monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

            m_subscription.AddItem(m_monitoredItem);
            m_subscription.ApplyChanges();
        }

        /// <summary>
        /// Deletes the subscription.
        /// </summary>
        private void DeleteSubscription()
        {
            if (m_subscription != null)
            {
                m_subscription.Delete(true);
                m_session.RemoveSubscription(m_subscription);
                m_subscription = null;
                m_monitoredItem = null;
            }
        }

        /// <summary>
        /// Creates list item for an event.
        /// </summary>
        private ListViewItem CreateListItem(FilterDeclaration filter, VariantCollection fieldValues)
        {
            ListViewItem item = null;

            if (m_displayConditions)
            {
                NodeId conditionId = fieldValues[0].Value as NodeId;

                if (conditionId != null)
                {
                    for (int ii = 0; ii < EventsLV.Items.Count; ii++)
                    {
                        VariantCollection fields = EventsLV.Items[ii].Tag as VariantCollection;

                        if (fields != null && Utils.IsEqual(conditionId, fields[0].Value))
                        {
                            item = EventsLV.Items[ii];
                            break;
                        }
                    }
                }
            }

            if (item == null)
            {
                item = new ListViewItem();
            }

            item.Tag = fieldValues;
            int position = -1;

            for (int ii = 1; ii < filter.Fields.Count; ii++)
            {
                if (!filter.Fields[ii].DisplayInList)
                {
                    continue;
                }

                position++;

                string text = null;
                Variant value = fieldValues[ii + 1];

                // check for missing fields.
                if (value.Value == null)
                {
                    text = String.Empty;
                }

                // display the name of a node instead of the node id.
                else if (value.TypeInfo.BuiltInType == BuiltInType.NodeId)
                {
                    INode node = m_session.NodeCache.Find((NodeId)value.Value);

                    if (node != null)
                    {
                        text = node.ToString();
                    }
                }

                // display local time for any time fields.
                else if (value.TypeInfo.BuiltInType == BuiltInType.DateTime)
                {
                    DateTime datetime = (DateTime)value.Value;

                    if (m_filter.Fields[ii].InstanceDeclaration.DisplayName.Contains("Time"))
                    {
                        text = datetime.ToLocalTime().ToString("HH:mm:ss.fff");
                    }
                    else
                    {
                        text = datetime.ToLocalTime().ToString("yyyy-MM-dd");
                    }
                }

                // use default string format.
                else
                {
                    text = value.ToString();
                }

                // update subitem text.
                if (item.Text == String.Empty)
                {
                    item.Text = text;
                    item.SubItems[0].Text = text;
                }
                else
                {
                    if (item.SubItems.Count <= position)
                    {
                        item.SubItems.Add(text);
                    }
                    else
                    {
                        item.SubItems[position].Text = text;
                    }
                }
            }

            return item;
        }

        /// <summary>
        /// Updates the display with a new value for a monitored variable. 
        /// </summary>
        private void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MonitoredItemNotificationEventHandler(MonitoredItem_Notification), monitoredItem, e);
                return;
            }

            try
            {
                // check for valid notification.
                EventFieldList notification = e.NotificationValue as EventFieldList;

                if (notification == null)
                {
                    return;
                }

                // check if monitored item has changed.
                if (!Object.ReferenceEquals(m_monitoredItem, monitoredItem))
                {
                    return;
                }

                // check if the filter has changed.
                if (notification.EventFields.Count != m_filter.Fields.Count+1)
                {
                    return;
                }

                if (m_displayConditions)
                {
                    NodeId eventTypeId = m_filter.GetValue<NodeId>(Opc.Ua.BrowseNames.EventType, notification.EventFields, null);

                    if (eventTypeId == Opc.Ua.ObjectTypeIds.RefreshStartEventType)
                    {
                        EventsLV.Items.Clear();
                    }

                    if (eventTypeId == Opc.Ua.ObjectTypeIds.RefreshEndEventType)
                    {
                        return;
                    }
                }

                // create an item and add to top of list.
                ListViewItem item = CreateListItem(m_filter, notification.EventFields);

                if (item.ListView == null)
                {
                    EventsLV.Items.Insert(0, item);
                }

                // adjust the width of the columns.
                for (int ii = 0; ii < EventsLV.Columns.Count; ii++)
                {
                    EventsLV.Columns[ii].Width = -2;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        
        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private void ReadRecentHistory()
        {
            // check if session is active.
            if (m_session != null)
            {
                // check if area supports history.
                IObject area = m_session.NodeCache.Find(m_areaId) as IObject;

                if (area != null && ((area.EventNotifier & EventNotifiers.HistoryRead) != 0))
                {
                    // get the last hour or 10 events.
                    ReadEventDetails details = new ReadEventDetails();
                    details.StartTime = DateTime.UtcNow.AddSeconds(30);
                    details.EndTime = details.StartTime.AddHours(-1);
                    details.NumValuesPerNode = 10;
                    details.Filter = m_filter.GetFilter();

                    // read the history.
                    ReadHistory(details, m_areaId);
                }
            }
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private void ReadHistory(ReadEventDetails details, NodeId areaId)
        {
            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = areaId;
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Neither,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            HistoryEvent events = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;
            AddEventHistory(events);

            // release continuation points.
            if (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                m_session.HistoryRead(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Neither,
                    true,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);
            }
        }

        /// <summary>
        /// Deletes the recent history.
        /// </summary>
        private void DeleteHistory(NodeId areaId, List<VariantCollection> events, FilterDeclaration filter)
        {
            // find the event id.
            int index = 0;

            foreach (FilterDeclarationField field in filter.Fields)
            {
                if (field.InstanceDeclaration.BrowseName == Opc.Ua.BrowseNames.EventId)
                {
                    break;
                }

                index++;
            }

            // can't delete events if no event id.
            if (index >= filter.Fields.Count)
            {
                throw ServiceResultException.Create(StatusCodes.BadEventIdUnknown, "Cannot delete events if EventId was not selected.");
            }

            // build list of nodes to delete.
            DeleteEventDetails details = new DeleteEventDetails();
            details.NodeId = areaId;

            foreach (VariantCollection e in events)
            {
                byte[] eventId = null;

                if (e.Count > index)
                {
                    eventId = e[index].Value as byte[];
                }

                details.EventIds.Add(eventId);
            }

            // delete the events.
            ExtensionObjectCollection nodesToUpdate = new ExtensionObjectCollection();
            nodesToUpdate.Add(new ExtensionObject(details));

            HistoryUpdateResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryUpdate(
                null,
                nodesToUpdate,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToUpdate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToUpdate);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            // check for item level errors.
            if (results[0].OperationResults.Count > 0)
            {
                int count = 0;

                for (int ii = 0; ii < results[0].OperationResults.Count; ii++)
                {
                    if (StatusCode.IsBad(results[0].OperationResults[ii]))
                    {
                        count++;
                    }
                }

                // raise an error.
                if (count > 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEventIdUnknown, 
                        "Error deleting events. Only {0} of {1} deletes succeeded.",
                        events.Count - count,
                        events.Count);
                }
            }
        }

        private void ViewDetailsMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                VariantCollection fields = EventsLV.SelectedItems[0].Tag as VariantCollection;

                if (fields != null)
                {
                    // new ViewEventDetailsDlg().ShowDialog(m_filter, fields);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void DeleteHistoryMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count == 0)
                {
                    return;
                }

                List<VariantCollection> events = new List<VariantCollection>();

                foreach (ListViewItem item in EventsLV.SelectedItems)
                {
                    VariantCollection fields = item.Tag as VariantCollection;

                    if (fields != null)
                    {
                        events.Add(fields);
                    }
                }

                if (events.Count > 0)
                {
                    DeleteHistory(m_areaId, events, m_filter);

                    foreach (ListViewItem item in EventsLV.SelectedItems)
                    {
                        VariantCollection fields = item.Tag as VariantCollection;

                        if (fields != null)
                        {
                            item.Font = new Font(item.Font, FontStyle.Strikeout);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
