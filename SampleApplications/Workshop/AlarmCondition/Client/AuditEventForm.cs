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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// A form which displays the audit events produced by the server.
    /// </summary>
    public partial class AuditEventForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private AuditEventForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AuditEventForm"/> class.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="subscription">The subscription.</param>
        public AuditEventForm(Session session, Subscription subscription)
        {
            InitializeComponent();

            m_session = session;
            m_subscription = subscription;

            // a table used to track event types.
            m_eventTypeMappings = new Dictionary<NodeId, NodeId>();

            // the filter to use.
            m_filter = new FilterDefinition();

            m_filter.AreaId = ObjectIds.Server;
            m_filter.Severity = EventSeverity.Min;
            m_filter.IgnoreSuppressedOrShelved = true;
            m_filter.EventTypes = new NodeId[] { ObjectTypeIds.AuditUpdateMethodEventType };

            // find the fields of interest.
            m_filter.SelectClauses = m_filter.ConstructSelectClauses(m_session, ObjectTypeIds.AuditUpdateMethodEventType);

            // declate callback.
            m_MonitoredItem_Notification = new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

            // create a monitored item based on the current filter settings.
            m_monitoredItem = m_filter.CreateMonitoredItem(m_session);

            // set up callback for notifications.
            m_monitoredItem.Notification += m_MonitoredItem_Notification;

            m_subscription.AddItem(m_monitoredItem);
            m_subscription.ApplyChanges();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Handles a server reconnect event.
        /// </summary>
        /// <param name="session">The new session.</param>
        /// <param name="subscription">The new subscription.</param>
        public void ReconnectComplete(Session session, Subscription subscription)
        {
            m_session = session;
            m_subscription = subscription;

            foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
            {
                if (Object.ReferenceEquals(monitoredItem.Handle, m_filter))
                {
                    m_monitoredItem = monitoredItem;
                    break;
                }
            }
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private FilterDefinition m_filter;
        private Dictionary<NodeId,NodeId> m_eventTypeMappings;
        private MonitoredItemNotificationEventHandler m_MonitoredItem_Notification;
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
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
                EventFieldList notification = e.NotificationValue as EventFieldList;

                if (notification == null)
                {
                    return;
                }

                // check the type of event.
                NodeId eventTypeId = FormUtils.FindEventType(monitoredItem, notification);

                // ignore unknown events.
                if (NodeId.IsNull(eventTypeId))
                {
                    return;
                }

                // construct the audit object.
                AuditUpdateMethodEventState audit = FormUtils.ConstructEvent(
                    m_session, 
                    monitoredItem, 
                    notification,
                    m_eventTypeMappings) as AuditUpdateMethodEventState;

                if (audit == null)
                {
                    return;
                }

                ListViewItem item = new ListViewItem(String.Empty);

                item.SubItems.Add(String.Empty); // Source
                item.SubItems.Add(String.Empty); // Type
                item.SubItems.Add(String.Empty); // Method
                item.SubItems.Add(String.Empty); // Status
                item.SubItems.Add(String.Empty); // Time
                item.SubItems.Add(String.Empty); // Message
                item.SubItems.Add(String.Empty); // Arguments
                
                // look up the condition type metadata in the local cache.
                INode type = m_session.NodeCache.Find(audit.TypeDefinitionId);

                // Source
                if (audit.SourceName != null)
                {
                    item.SubItems[0].Text = Utils.Format("{0}", audit.SourceName.Value);
                }
                else
                {
                    item.SubItems[0].Text = null;
                }

                // Type
                if (type != null)
                {
                    item.SubItems[1].Text = Utils.Format("{0}", type);
                }
                else
                {
                    item.SubItems[1].Text = null;
                }

                // look up the method metadata in the local cache.
                INode method = m_session.NodeCache.Find(BaseVariableState.GetValue(audit.MethodId));

                // Method
                if (method != null)
                {
                    item.SubItems[2].Text = Utils.Format("{0}", method);
                }
                else
                {
                    item.SubItems[2].Text = null;
                }

                // Status
                if (audit.Status != null)
                {
                    item.SubItems[3].Text = Utils.Format("{0}", audit.Status.Value);
                }
                else
                {
                    item.SubItems[3].Text = null;
                }

                // Time
                if (audit.Time != null)
                {
                    item.SubItems[4].Text = Utils.Format("{0:HH:mm:ss.fff}", audit.Time.Value.ToLocalTime());
                }
                else
                {
                    item.SubItems[4].Text = null;
                }

                // Message
                if (audit.Message != null)
                {
                    item.SubItems[5].Text = Utils.Format("{0}", audit.Message.Value);
                }
                else
                {
                    item.SubItems[5].Text = null;
                }

                // Arguments
                if (audit.InputArguments != null)
                {
                    item.SubItems[6].Text = Utils.Format("{0}", new Variant(audit.InputArguments.Value));
                }
                else
                {
                    item.SubItems[6].Text = null;
                }

                item.Tag = audit;
                EventsLV.Items.Add(item);

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
        /// Handles the Click event of the Conditions_MonitorMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Events_ViewMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (EventsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                AuditUpdateMethodEventState audit = (AuditUpdateMethodEventState)EventsLV.SelectedItems[0].Tag;
                new ViewEventDetailsDlg().ShowDialog(m_monitoredItem, audit.Handle as EventFieldList);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Events_ClearMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Events_ClearMI_Click(object sender, EventArgs e)
        {
            try
            {
                EventsLV.Items.Clear();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the FormClosing event of the AuditEventForm control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.FormClosingEventArgs"/> instance containing the event data.</param>
        private void AuditEventForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                m_monitoredItem.Notification -= m_MonitoredItem_Notification;
                m_subscription.RemoveItem(m_monitoredItem);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
