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
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.SimpleEvents.Client
{
    /// <summary>
    /// The main form for a simple Quickstart Client application.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        public MainForm(ApplicationConfiguration configuration)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62563/Quickstarts/SimpleEventsServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private Opc.Ua.Client.Controls.FilterDeclaration m_filter;
        private Dictionary<NodeId, Type> m_knownEventTypes;
        private Dictionary<NodeId, NodeId> m_eventTypeMappings;
        private MonitoredItemNotificationEventHandler m_MonitoredItem_Notification;
        private bool m_connectedOnce;
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.Connect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private void Server_ConnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    return;
                }

                // set a suitable initial state.
                if (m_session != null && !m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                CreateSubscription();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                foreach (Subscription subscription in m_session.Subscriptions)
                {
                    m_subscription = subscription;
                    break;
                }

                foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
                {
                    m_monitoredItem = monitoredItem;
                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConnectServerCTRL.Disconnect();
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private void CreateSubscription()
        {
            // create the default subscription.
            m_subscription = new Subscription();

            m_subscription.DisplayName = null;
            m_subscription.PublishingInterval = 1000;
            m_subscription.KeepAliveCount = 10;
            m_subscription.LifetimeCount = 100;
            m_subscription.MaxNotificationsPerPublish = 1000;
            m_subscription.PublishingEnabled = true;
            m_subscription.TimestampsToReturn = TimestampsToReturn.Both;

            m_session.AddSubscription(m_subscription);
            m_subscription.Create();

            // a table used to track event types.
            m_eventTypeMappings = new Dictionary<NodeId, NodeId>();

            NodeId knownEventId = ExpandedNodeId.ToNodeId(ObjectTypeIds.SystemCycleStatusEventType, m_session.NamespaceUris);

            m_knownEventTypes = new Dictionary<NodeId, Type>();
            m_knownEventTypes.Add(knownEventId, typeof(SystemCycleStatusEventState));
            
            TypeDeclaration type = new TypeDeclaration();
            type.NodeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.SystemCycleStatusEventType, m_session.NamespaceUris);
            type.Declarations = ClientUtils.CollectInstanceDeclarationsForType(m_session, type.NodeId);

            // the filter to use.
            m_filter = new FilterDeclaration(type, null);

            // declate callback.
            m_MonitoredItem_Notification = new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

            // create a monitored item based on the current filter settings.            
            m_monitoredItem = new MonitoredItem();
            m_monitoredItem.StartNodeId = Opc.Ua.ObjectIds.Server;
            m_monitoredItem.AttributeId = Attributes.EventNotifier;
            m_monitoredItem.SamplingInterval = 0;
            m_monitoredItem.QueueSize = 1000;
            m_monitoredItem.DiscardOldest = true;
            m_monitoredItem.Filter = m_filter.GetFilter();

            // set up callback for notifications.
            m_monitoredItem.Notification += m_MonitoredItem_Notification;

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
                m_filter = null;
                m_monitoredItem = null;
            }
        }
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
                NodeId eventTypeId = ClientUtils.FindEventType(monitoredItem, notification);

                // ignore unknown events.
                if (NodeId.IsNull(eventTypeId))
                {
                    return;
                }

                // construct the audit object.
                SystemCycleStatusEventState status = ClientUtils.ConstructEvent(
                    m_session,
                    monitoredItem,
                    notification,
                    m_knownEventTypes,
                    m_eventTypeMappings) as SystemCycleStatusEventState;

                if (e == null)
                {
                    return;
                }

                ListViewItem item = new ListViewItem(String.Empty);

                item.SubItems.Add(String.Empty); // Source
                item.SubItems.Add(String.Empty); // Type
                item.SubItems.Add(String.Empty); // CycleId
                item.SubItems.Add(String.Empty); // Step
                item.SubItems.Add(String.Empty); // Time
                item.SubItems.Add(String.Empty); // Message

                // look up the condition type metadata in the local cache.
                INode type = m_session.NodeCache.Find(status.TypeDefinitionId);

                // Source
                if (status.SourceName != null)
                {
                    item.SubItems[0].Text = Utils.Format("{0}", status.SourceName.Value);
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

                // CycleId
                if (status.CycleId != null)
                {
                    item.SubItems[2].Text = Utils.Format("{0}", status.CycleId.Value);
                }
                else
                {
                    item.SubItems[2].Text = null;
                }

                // Step
                if (status.CurrentStep != null && status.CurrentStep.Value != null)
                {
                    item.SubItems[3].Text = Utils.Format("{0}", status.CurrentStep.Value.Name);
                }
                else
                {
                    item.SubItems[3].Text = null;
                }

                // Time
                if (status.Time != null)
                {
                    item.SubItems[4].Text = Utils.Format("{0:HH:mm:ss.fff}", status.Time.Value.ToLocalTime());
                }
                else
                {
                    item.SubItems[4].Text = null;
                }

                // Message
                if (status.Message != null)
                {
                    item.SubItems[5].Text = Utils.Format("{0}", status.Message.Value);
                }
                else
                {
                    item.SubItems[5].Text = null;
                }

                item.Tag = status;
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
        /// Sets the locale to use.
        /// </summary>
        private void Server_SetLocaleMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                string locale = new SelectLocaleDlg().ShowDialog(m_session);

                if (locale == null)
                {
                    return;
                }

                ConnectServerCTRL.PreferredLocales = new string[] { locale };
                m_session.ChangePreferredLocales(new StringCollection(ConnectServerCTRL.PreferredLocales));
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
