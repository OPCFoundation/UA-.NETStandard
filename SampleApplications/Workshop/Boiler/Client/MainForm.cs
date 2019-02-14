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

namespace Quickstarts.Boiler.Client
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
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62567/Quickstarts/BoilerServer";
            this.Text = m_configuration.ApplicationName;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private Subscription m_subscription;
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
                    BoilerCB.Enabled = false;
                    return;
                }

                // set a suitable initial state.
                if (m_session != null && !m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                BoilerCB.Enabled = true;

                // update list of boilers
                GetBoilers();
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
                BoilerCB.Enabled = false;
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

                BoilerCB.Enabled = true;
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
        /// Gets the boilers.
        /// </summary>
        private void GetBoilers()
        {
            BoilerCB.Items.Clear();

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = Opc.Ua.ObjectIds.ObjectsFolder;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object);
            nodeToBrowse.ResultMask = (uint)(BrowseResultMask.All);

            ReferenceDescriptionCollection references = ClientUtils.Browse(
                m_session,
                nodeToBrowse,
                false);

            if (references != null)
            {
                NodeId boilerTypeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.BoilerType, m_session.NamespaceUris);

                for (int ii = 0; ii < references.Count; ii++)
                {
                    if (boilerTypeId == references[ii].TypeDefinition)
                    {
                        BoilerCB.Items.Add(references[ii]);
                    }
                }

                if (BoilerCB.Items.Count > 0)
                {
                    BoilerCB.SelectedIndex = 0;
                }
            }
        }
        #endregion

        #region Event Handlers
        private void BoilerCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                if (m_subscription != null)
                {
                    m_session.RemoveSubscription(m_subscription);
                    m_subscription = null;
                }
                
                ReferenceDescription boiler = (ReferenceDescription)BoilerCB.SelectedItem;

                if (boiler == null)
                {
                    return;
                }
                
                m_subscription = new Subscription();

                m_subscription.PublishingEnabled = true;
                m_subscription.PublishingInterval = 1000;
                m_subscription.Priority = 1;
                m_subscription.KeepAliveCount = 10;
                m_subscription.LifetimeCount = 20;
                m_subscription.MaxNotificationsPerPublish = 1000;

                m_session.AddSubscription(m_subscription);
                m_subscription.Create();

                NamespaceTable wellKnownNamespaceUris = new NamespaceTable();
                wellKnownNamespaceUris.Append(Namespaces.Boiler);

                string[] browsePaths = new string[] 
                {
                    "1:PipeX001/1:FTX001/1:Output",
                    "1:DrumX001/1:LIX001/1:Output",
                    "1:PipeX002/1:FTX002/1:Output",
                    "1:LCX001/1:SetPoint",
                };

                List<NodeId> nodes = ClientUtils.TranslateBrowsePaths(
                    m_session,
                    (NodeId)boiler.NodeId,
                    wellKnownNamespaceUris,
                    browsePaths);

                Control[] controls = new Control[] 
                {
                    InputPipeFlowTB,
                    DrumLevelTB,
                    OutputPipeFlowTB,
                    DrumLevelSetPointTB
                };

                for (int ii = 0; ii < nodes.Count; ii++)
                {
                    controls[ii].Text = "---";

                    if (nodes[ii] != null)
                    {
                        MonitoredItem monitoredItem = new MonitoredItem();
                        monitoredItem.StartNodeId = nodes[ii];
                        monitoredItem.AttributeId = Attributes.Value;
                        monitoredItem.Handle = controls[ii];
                        monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);
                        m_subscription.AddItem(monitoredItem);
                    }
                }

                m_subscription.ApplyChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MonitoredItemNotificationEventHandler(MonitoredItem_Notification), monitoredItem, e);
                return;
            }

            try
            {
                Control control = monitoredItem.Handle as Control;

                if (control == null)
                {
                    return;
                }

                MonitoredItemNotification datachange = e.NotificationValue as MonitoredItemNotification;

                if (datachange == null)
                {
                    return;
                }

                control.Text = datachange.Value.WrappedValue.ToString();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
