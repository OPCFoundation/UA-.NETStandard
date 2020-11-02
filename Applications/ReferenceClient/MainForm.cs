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
using System.Diagnostics;
using System.Collections;

namespace Quickstarts.ReferenceClient
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
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        public MainForm(ApplicationConfiguration configuration)
        {
            InitializeComponent();
            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private bool m_connectedOnce;
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_Click(object sender, EventArgs e)
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

                // set a suitable initial state.
                if (m_session != null && !m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                // browse the instances in the server.
                BrowseCTRL.Initialize(m_session, ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, ReferenceTypeIds.Aggregates);
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
                BrowseCTRL.ChangeSession(null);
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
                BrowseCTRL.ChangeSession(m_session);
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

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit this application?", "Reference Client", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }

        private void Help_ContentsMI_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start( Path.GetDirectoryName(Application.ExecutablePath) + "\\WebHelp\\overview_-_reference_client.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }

        /* 
           TODO:
           Sufficient error trapping.
        */
        private Subscription m_subscription;
        private List<MonitoredItem> monitoredItems = new List<MonitoredItem>();
        private SubscriptionOutput subscriptionOutputWindow;
        private WriteOutput writeOutputWindow;
        private void subscribeToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if ((NodeId)BrowseCTRL.SelectedNode.NodeId != null)
            {
                if (subscriptionOutputWindow == null)
                {
                    subscriptionOutputWindow = new SubscriptionOutput();
                }

                if (m_subscription == null)
                {
                    m_subscription = new Subscription(m_session.DefaultSubscription);
                    m_subscription.PublishingEnabled = true;
                    m_subscription.PublishingInterval = 1000;
                    m_session.AddSubscription(m_subscription);
                    m_subscription.Create();
                }

                if (monitoredItems.Count == 0)
                {
                    if (BrowseCTRL.GetChildOfSelectedNode(0) != null)
                    {
                        BrowseDescription nodeToBrowse = new BrowseDescription();
                        nodeToBrowse.NodeId = (NodeId)BrowseCTRL.SelectedNode.NodeId;

                        nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                        nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences;
                        nodeToBrowse.IncludeSubtypes = true;
                        //nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object);
                        nodeToBrowse.ResultMask = (uint)(BrowseResultMask.All);

                        ReferenceDescriptionCollection references = ClientUtils.Browse(m_session, nodeToBrowse, false);
                        subscriptionOutputWindow.label1.Text = "You selected a folder: these are the child nodes:\n";
                        foreach (var item in references)
                        {
                            var mi = new MonitoredItem(m_subscription.DefaultItem);
                            mi.StartNodeId = (NodeId)item.NodeId;
                            mi.AttributeId = Attributes.DisplayName;
                            mi.MonitoringMode = MonitoringMode.Reporting;
                            mi.SamplingInterval = 1000;
                            mi.QueueSize = 0;
                            mi.DiscardOldest = true;
                            mi.Notification += new MonitoredItemNotificationEventHandler(Mi_Notification);
                            monitoredItems.Add(mi);
                        }
                    } else
                    {
                        var mi = new MonitoredItem(m_subscription.DefaultItem);
                        mi.StartNodeId = (NodeId)BrowseCTRL.SelectedNode.NodeId;
                        mi.AttributeId = Attributes.Value;
                        mi.MonitoringMode = MonitoringMode.Reporting;
                        mi.SamplingInterval = 1000;
                        mi.QueueSize = 0;
                        mi.DiscardOldest = true;
                        mi.Notification += new MonitoredItemNotificationEventHandler(Mi_Notification);
                        // define event handler for this item, and then add to subscription
                        mi.Notification += new MonitoredItemNotificationEventHandler(monitoredItem_Notification);
                        monitoredItems.Add(mi);
                    }
                    m_subscription.AddItems(monitoredItems);
                }
                subscriptionOutputWindow.Show();
                subscriptionOutputWindow.FormClosed += OutputWindow_FormClosed;
                m_subscription.ApplyChanges();
            }
        }

        private void OutputWindow_FormClosed(object sender, FormClosedEventArgs e)
        {
            m_subscription.Delete(false);
            m_subscription = null;
            monitoredItems = new List<MonitoredItem>();
            subscriptionOutputWindow = null;
        }

        private void Mi_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MonitoredItemNotificationEventHandler(Mi_Notification), monitoredItem, e);
                return;
            }
            MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
            if (notification == null)
            {
                return;
            }
            String output = Utils.Format("Node: {0}\n", notification.Value.WrappedValue.ToString());
            if (!subscriptionOutputWindow.label1.Text.Contains(output))
            {
                subscriptionOutputWindow.label1.Text += output;
            }
        }

        public void monitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MonitoredItemNotificationEventHandler(monitoredItem_Notification), monitoredItem, e);
                return;
            }
            MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
            if (notification == null)
            {
                return;
            }
            String output = "value: " + Utils.Format("{0}", notification.Value.WrappedValue.ToString()) +
              ";\nStatusCode: " + Utils.Format("{0}", notification.Value.StatusCode.ToString()) +
              ";\nSource timestamp: " + notification.Value.SourceTimestamp.ToString() +
              ";\nServer timestamp: " + notification.Value.ServerTimestamp.ToString();

            subscriptionOutputWindow.label1.Text = output;
        }

        private void writeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            /*
            List<WriteValue> nodesToWrite = new List<WriteValue>();
            if ((NodeId)BrowseCTRL.SelectedNode.NodeId != null)
            {
                m_session.Write(nodesToWrite);
            }
            */
            writeOutputWindow = new WriteOutput(m_session);
            writeOutputWindow.Show();
        }
    }
}
