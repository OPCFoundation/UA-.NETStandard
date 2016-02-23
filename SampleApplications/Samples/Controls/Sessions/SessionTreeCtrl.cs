/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Security.Cryptography.X509Certificates;
using WinRTXamlToolkit.Controls;
using Windows.UI.Xaml.Controls;
using Windows.ApplicationModel;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.UI.Core;
using System.Collections.ObjectModel;

namespace Opc.Ua.Sample.Controls
{
    public partial class SessionTreeCtrl : BaseTreeCtrl
    {
        #region Contructors
        public SessionTreeCtrl()
        {
            m_eventRegistrations = new Dictionary<object, TreeItemViewModel>();
            m_endpointUrls = new StringCollection();
            m_dialogs = new Dictionary<Subscription, SubscriptionDlg>();

            m_SessionSubscriptionsChanged = new EventHandler(Session_SubscriptionsChanged);
            m_SubscriptionStateChanged = new SubscriptionStateChangedEventHandler(Subscription_StateChanged);
        }
        #endregion

        #region Private Fields
        private BrowseTreeCtrl m_AddressSpaceCtrl;
        private NotificationMessageListCtrl m_NotificationMessagesCtrl;
        private EventHandler m_SessionSubscriptionsChanged;
        private SubscriptionStateChangedEventHandler m_SubscriptionStateChanged;
        private Dictionary<object, TreeItemViewModel> m_eventRegistrations;
        private StringCollection m_endpointUrls;
        private Dictionary<Subscription,SubscriptionDlg> m_dialogs;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_messageContext;
        private ConfiguredEndpoint m_endpoint;
        private string m_filePath;
        #endregion

        #region Public Interface
        public delegate void SessionConnected(Session session);

        /// <summary>
        /// The configuration to use when creating sessions.
        /// </summary>
        public ApplicationConfiguration Configuration
        {
            get { return m_configuration;  }
            set { m_configuration = value; }
        }

        /// <summary>
        /// The message context to use with the sessions.
        /// </summary>
        public ServiceMessageContext MessageContext
        {
            get { return m_messageContext;  }
            set { m_messageContext = value; }
        }

        /// <summary>
        /// The locales to use when creating the session.
        /// </summary>
        public string[] PreferredLocales { get; set; }

        /// <summary>
        /// Closes all open sessions within the control.
        /// </summary>
        public void Close()
        {
            // close all active sessions.
            foreach (TreeItemViewModel root in NodesTV.TreeItems)
            {
                Session session = root.Item as Session;

                if (session != null)
                {
                    session.Close();
                }
            }

            Clear();
        }

        /// <summary>
        /// The control used to display the address space for a session.
        /// </summary>
        public BrowseTreeCtrl AddressSpaceCtrl
        {
            get { return m_AddressSpaceCtrl;  }
            set { m_AddressSpaceCtrl = value; }
        }

        /// <summary>
        /// The control used to display the notification messages returned for a session..
        /// </summary>
        public NotificationMessageListCtrl NotificationMessagesCtrl
        {
            get { return m_NotificationMessagesCtrl; }
            set { m_NotificationMessagesCtrl = value; }
        }

        /// <summary>
        /// Creates a session with the endpoint.
        /// </summary>
        public async Task<Session> Connect(ConfiguredEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException("endpoint");

            // check if the endpoint needs to be updated.
            if (endpoint.UpdateBeforeConnect)
            {
                ConfiguredServerDlg configurationDialog = new ConfiguredServerDlg();
                endpoint = await configurationDialog.ShowDialog(endpoint, m_configuration);
            }

            if (endpoint == null)
            {
                return null;
            }

            m_endpoint = endpoint;

            // copy the message context.
            m_messageContext = m_configuration.CreateMessageContext();

            X509Certificate2 clientCertificate = null;

            if (endpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
            {
                if (m_configuration.SecurityConfiguration.ApplicationCertificate == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate must be specified.");
                }

                clientCertificate = await m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

                if (clientCertificate == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate cannot be found.");
                }
            }

            // create the channel.
            ITransportChannel channel = SessionChannel.Create(
                m_configuration,
                endpoint.Description,
                endpoint.Configuration,
                //clientCertificateChain,
                clientCertificate,
                m_messageContext);

            try
            {
                // create the session.
                Session session = new Session(channel, m_configuration, endpoint, null);
                session.ReturnDiagnostics = DiagnosticsMasks.All;

                SessionOpenDlg sessiondlg = new SessionOpenDlg();
                session = await sessiondlg.ShowDialog(session, PreferredLocales);

                if (session != null)
                {
                    // session now owns the channel.
                    channel = null;
                    // add session to tree.
                    AddNode(session);

                    return session;
                }

            }
            finally
            {
                // ensure the channel is closed on error.
                if (channel != null)
                {
                    channel.Close();
                    channel = null;
                }
            }

            return null;
        }

        public void UpdateSessionNode(Session session)
        {
            TreeItemViewModel node = FindNode(NodesTV.TreeItems, session);
            if (node != null)
            {
                String state = "Server";
                if (session.KeepAliveStopped)
                {
                    state = "ServerKeepAliveStopped";
                }
                if (!session.Connected)
                {
                    state = "ServerStopped";
                }
                UpdateNode(node, session, session.SessionName, state);
            }
        }

        /// <summary>
        /// Deletes a session.
        /// </summary>
        public void Delete(Session session)
        {
            if (session == null) throw new ArgumentNullException("session");

            TreeItemViewModel node = FindNode(NodesTV.TreeItems, session);

            if (node != null)
            {
                node.Children.Clear();
            }

            // close any dialogs.
            foreach (SubscriptionDlg dialog in new List<SubscriptionDlg>(m_dialogs.Values))
            {
                //dialog.Close();
            }

            session.Close();
            NodesTV.TreeItems.Remove(node);
        }

        /// <summary>
        /// Deletes a subscription.
        /// </summary>
        public void Delete(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");

            TreeItemViewModel node = FindNode(NodesTV.TreeItems, subscription);

            if (node != null)
            {
                node.Children.Clear();
            }
            
            // close any dialog.
            SubscriptionDlg dialog = null;

            if (m_dialogs.TryGetValue(subscription, out dialog))
            {
                //dialog.Close();
            }

            Session session = subscription.Session;
            session.RemoveSubscription(subscription);
            NodesTV.SelectedItem = FindNode(NodesTV.TreeItems, session);
        }
        
        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        public void Delete(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null) throw new ArgumentNullException("monitoredItem");

            TreeItemViewModel node = FindNode(NodesTV.TreeItems, monitoredItem);

            if (node != null)
            {
                node.Children.Clear();
            }

            Subscription subscription = monitoredItem.Subscription;
            subscription.RemoveItem(monitoredItem);
            subscription.ApplyChanges();
            NodesTV.SelectedItem = FindNode(NodesTV.TreeItems, subscription);
        }

        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        public Subscription CreateSubscription(Session session)
        {                                        
            // create form.
            SubscriptionDlg dialog = new SubscriptionDlg();
            dialog.Unloaded += Dialog_Unloaded;

            // create subscription.
            Subscription subscription;
            Task<Subscription> t = Task.Run(() => dialog.New(session));
            t.Wait();
            subscription = t.Result;
            if (subscription != null)
            {
                m_dialogs.Add(subscription, dialog);
                return subscription;
            }

            return null;
        }
#endregion
        
#region Overridden Members
    
        /// <summary>
        /// Finds the first tag in the tree above the node that matches the type argument.
        /// </summary>
        private T Get<T>(TreeItemViewModel node)
        {
            if (node == null)
            {
                return default(T);
            }

            if (node.Item is T)
            {
                return (T)node.Item ;
            }

            return Get<T>(node.Parent);
        }

        /// <see cref="BaseTreeCtrl.EnableMenuItems" />
        protected override void EnableMenuItems(TreeItemViewModel clickedNode)
        {
            // Context menu is currently not implemented
            Session session = Get<Session>(clickedNode);
        }

        /// <see cref="BaseTreeCtrl.SelectNode" />
        protected override void SelectNode()
        {
            base.SelectNode();

            TreeItemViewModel selectedNode = NodesTV.SelectedItem;
            
            Session session = Get<Session>(selectedNode);
            Subscription subscription = Get<Subscription>(selectedNode); 
                        
            // update address space control.
            if (m_AddressSpaceCtrl != null)
            {
                m_AddressSpaceCtrl.SetView(session, BrowseViewType.Objects, null);
            }

            // update notification messages control.
            if (m_NotificationMessagesCtrl != null)
            {
                m_NotificationMessagesCtrl.Initialize(session, subscription);
            }

        }

#endregion

        /// <summary>
        /// Recursively clears a subtree.
        /// </summary>
        private void Clear(List<TreeViewItem> nodes)
        {
            foreach (TreeViewItem node in nodes)
            {
                if (m_eventRegistrations.Remove(node.Tag))
                {
                    if (node.Tag is Session)
                    {
                        ((Session)node.Tag).SubscriptionsChanged -= m_SessionSubscriptionsChanged;
                    }

                    else if (node.Tag is Subscription)
                    {
                        ((Subscription)node.Tag).StateChanged -= m_SubscriptionStateChanged;
                    }                   
                }

                node.Items.Clear();
            }

            nodes.Clear();
        }

#region Private Members
        /// <summary>
        /// Called when the set of items for a subscription changes.
        /// </summary>
        private void Subscription_StateChanged(object sender, EventArgs e)
        {
            TreeItemViewModel node = FindNode(NodesTV.TreeItems, sender);

            if (node == null)
            {
                return;
            }
            
            UpdateNode(node, sender as Subscription);
        }

        /// <summary>
        /// Called when the set of subscriptions for a session changes.
        /// </summary>
        private void Session_SubscriptionsChanged(object sender, EventArgs e)
        {
            TreeItemViewModel node = FindNode(NodesTV.TreeItems, sender);

            if (node == null)
            {
                return;
            }
            
            UpdateNode(node, sender as Session);
        }

        /// <summary>
        /// Recursively finds the node with the specified tag.
        /// </summary>
        private TreeItemViewModel FindNode(ObservableCollection<TreeItemViewModel> treeItems, object item)
        {
            foreach (TreeItemViewModel node in treeItems)
            {
                if (Object.ReferenceEquals(node.Item, item))
                {
                    return node;
                }

                TreeItemViewModel child = FindNode(node, item);

                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// Recursively finds the node with the specified tag.
        /// </summary>
        private TreeItemViewModel FindNode( TreeItemViewModel vm, object item)
        {
            if (Object.ReferenceEquals(vm.Item, item))
            {
                return vm;
            }

            foreach (TreeItemViewModel node in vm.Children)
            {
                if (Object.ReferenceEquals(node.Item, item))
                {
                    return node;
                }

                TreeItemViewModel child = FindNode( node, item);

                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively finds the node with the specified tag and returns the immediate child with the specified child tag.
        /// </summary>
        private TreeItemViewModel FindChild(TreeItemViewModel node, object tag, object childTag)
        {
            TreeItemViewModel parent = FindNode(node, tag);

            if (parent == null)
            {
                return null;
            }

            foreach (TreeItemViewModel child in parent.Children)
            {
                if (Object.ReferenceEquals(child.Item, childTag))
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a session to the tree.
        /// </summary>
        private void AddNode(Session session)
        {
            if (session == null) throw new ArgumentNullException("session");

            TreeItemViewModel node = AddNode(null, session, session.SessionName, "Server");
            UpdateNode(node, session);
            
            if (!m_eventRegistrations.ContainsKey(session))
            {
                session.SubscriptionsChanged += m_SessionSubscriptionsChanged;
                m_eventRegistrations.Add(session, node);
            }
            
            NodesTV.SelectedItem = node;
            SelectNode();
        }

        /// <summary>
        /// Updates a session node in the tree.
        /// </summary>
        private void UpdateNode(TreeItemViewModel parent, Session session)
        {
            UpdateNode(parent, session, session.SessionName, (session.Connected)?"Server":"ServerStopped");
            parent.Children.Clear();

            if (Object.ReferenceEquals(parent.Item, session))
            {
                foreach (Subscription subscription in session.Subscriptions)
                {
                    AddNode(parent, subscription);
                }
            }
        }

        /// <summary>
        /// Adds a subscription to the tree.
        /// </summary>
        private void AddNode(TreeItemViewModel parent, Subscription subscription)
        {
            TreeItemViewModel node = AddNode(parent, subscription, subscription.DisplayName, "Object");
            UpdateNode(node, subscription);

            if (!m_eventRegistrations.ContainsKey(subscription))
            {
                subscription.StateChanged += m_SubscriptionStateChanged;
                m_eventRegistrations.Add(subscription, node);
            }
        }
        
        /// <summary>
        /// Updates a subscription node in the tree.
        /// </summary>
        private void UpdateNode(TreeItemViewModel parent, Subscription subscription)
        {
            parent.Children.Clear();            
            parent.Text = subscription.DisplayName;

            foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
            {                
                AddNode(parent, monitoredItem, monitoredItem.DisplayName, "Property");
            }
        }
#endregion

        private void BrowseAllMI_Click(object sender, EventArgs e)
        {     
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    new AddressSpaceDlg().Show(session, BrowseViewType.All, null);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseObjectsMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    new AddressSpaceDlg().Show(session, BrowseViewType.Objects, null);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseObjectTypesMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    new BrowseTypesDlg().Show(session, ObjectTypeIds.BaseObjectType);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseVariableTypesMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    new BrowseTypesDlg().Show(session, VariableTypeIds.BaseDataVariableType);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseDataTypesMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    new AddressSpaceDlg().Show(session, BrowseViewType.DataTypes, null);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseReferenceTypesMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    new AddressSpaceDlg().Show(session, BrowseViewType.ReferenceTypes, null);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseEventTypesMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    new BrowseTypesDlg().Show(session, ObjectTypeIds.BaseEventType);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseServerViewsMI_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    Browser browser = new Browser(session);

                    browser.BrowseDirection   = BrowseDirection.Forward;
                    browser.IncludeSubtypes   = true;
                    browser.ReferenceTypeId   = null;
                    browser.NodeClassMask     = (int)NodeClass.View;
                    browser.ContinueUntilDone = true;

                    ReferenceDescriptionCollection references = browser.Browse(Objects.ViewsFolder);

                    foreach (ReferenceDescription reference in references)
                    {
                        //ToolStripItem item = BrowseServerViewsMI.DropDown.Items.Add(reference.ToString());
                        //item.Click += new EventHandler(BrowseServerViewsMI_Click);
                        //item.Tag = reference;
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        void BrowseServerViewsMI_Click(object sender, EventArgs e)
        {
            try
            {
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                // change nothing if nothing selected.
                if (selectedNode == null)
                {
                    return;
                }
                
                // get selected session.
                Session session = selectedNode.Item as Session; 

                if (session != null)
                {
                    //if (menuitem != null)
                    {
                        ReferenceDescription reference = new ReferenceDescription();// = menuitem.Tag as ReferenceDescription;
                        
                        new AddressSpaceDlg().Show(
                            session, 
                            BrowseViewType.ServerDefinedView, 
                            (NodeId)reference.NodeId);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void SubscriptionCreateMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get current selection.
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                if (selectedNode == null)
                {
                    return;
                }

                // get selected session.
                Session session = selectedNode.Item as Session;

                if (session == null)
                {
                    return;
                }

                // create the subscription.
                CreateSubscription(session);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void NewSessionMI_Click(object sender, EventArgs e)
        {
            try
            {
                await Connect(m_endpoint);
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
                // get current selection.
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                if (selectedNode == null)
                {
                    return;
                }

                // delete session.
                Session session = selectedNode.Item as Session;

                if (session != null)
                {
                    Delete(session);
                }

                // delete subscription
                Subscription subscription = selectedNode.Item as Subscription;

                if (subscription != null)
                {
                    Delete(subscription);
                }

                // delete monitored item
                MonitoredItem monitoredItem = selectedNode.Item as MonitoredItem;

                if (monitoredItem != null)
                {
                    Delete(monitoredItem);
                }                
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ReadMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get the current session.
                Session session = Get<Session>(NodesTV.SelectedItem);

                if (session == null || !session.Connected)
                {
                    return;
                }

                // build list of nodes to read.
                ReadValueIdCollection valueIds = new ReadValueIdCollection();

                MonitoredItem monitoredItem = Get<MonitoredItem>(NodesTV.SelectedItem);

                if (monitoredItem != null)
                {
                    ReadValueId valueId = new ReadValueId();

                    valueId.NodeId       = monitoredItem.ResolvedNodeId;
                    valueId.AttributeId  = monitoredItem.AttributeId;
                    valueId.IndexRange   = monitoredItem.IndexRange;
                    valueId.DataEncoding = monitoredItem.Encoding;

                    valueIds.Add(valueId);
                }
                else
                {
                    Subscription subscription = Get<Subscription>(NodesTV.SelectedItem);

                    if (subscription != null)
                    {
                        foreach (MonitoredItem item in subscription.MonitoredItems)
                        {
                            ReadValueId valueId = new ReadValueId();

                            valueId.NodeId       = item.ResolvedNodeId;
                            valueId.AttributeId  = item.AttributeId;
                            valueId.IndexRange   = item.IndexRange;
                            valueId.DataEncoding = item.Encoding;

                            valueIds.Add(valueId);
                        }
                    }
                }

                // show form.
                //new ReadDlg().Show(session, valueIds);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void WriteMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get the current session.
                Session session = Get<Session>(NodesTV.SelectedItem);

                if (session == null || !session.Connected)
                {
                    return;
                }

                // build list of nodes to read.
                WriteValueCollection values = new WriteValueCollection();

                MonitoredItem monitoredItem = Get<MonitoredItem>(NodesTV.SelectedItem);

                if (monitoredItem != null)
                {
                    WriteValue value = new WriteValue();

                    value.NodeId      = monitoredItem.ResolvedNodeId;
                    value.AttributeId = monitoredItem.AttributeId; 
                    value.IndexRange  = monitoredItem.IndexRange;

                    MonitoredItemNotification datachange = monitoredItem.LastValue as MonitoredItemNotification;

                    if (datachange != null)
                    {
                        value.Value = (DataValue)Utils.Clone(datachange.Value);
                    }

                    values.Add(value);
                }
                else
                {
                    Subscription subscription = Get<Subscription>(NodesTV.SelectedItem);

                    if (subscription != null)
                    {
                        foreach (MonitoredItem item in subscription.MonitoredItems)
                        {
                            WriteValue value = new WriteValue();

                            value.NodeId      = item.ResolvedNodeId;
                            value.AttributeId = item.AttributeId;     
                            value.IndexRange  = item.IndexRange;                      

                            MonitoredItemNotification datachange = item.LastValue as MonitoredItemNotification;

                            if (datachange != null)
                            {
                                value.Value = (DataValue)Utils.Clone(datachange.Value);
                            }

                            values.Add(value);
                        }
                    }
                }

                // show form.
                //new WriteDlg().Show(session, values);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void SubscriptionEnabledPublishingMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get current selection.
                TreeItemViewModel selectedNode = NodesTV.SelectedItem;

                if (selectedNode == null)
                {
                    return;
                }

                // delete session.
                Subscription subscription = selectedNode.Item as Subscription;

                if (subscription != null)
                {
                    subscription.SetPublishingMode(false);
                }   
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void SubscriptionMonitorMI_Click(object sender, EventArgs e)
        {            
            try
            {
                // get selected session.
                Subscription subscription = SelectedTag as Subscription;

                if (subscription == null)
                {
                    return;
                }
                                        
                // show form
                SubscriptionDlg dialog = null;

                if (!m_dialogs.TryGetValue(subscription, out dialog))
                {
                    dialog = new SubscriptionDlg();
                    dialog.Unloaded += Dialog_Unloaded;
                    m_dialogs.Add(subscription, dialog);
                }
                
                dialog.Show(subscription);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void Dialog_Unloaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            foreach (KeyValuePair<Subscription, SubscriptionDlg> current in m_dialogs)
            {
                if (current.Value == sender)
                {
                    m_dialogs.Remove(current.Key);
                    return;
                }
            }
        }

        private async void SessionSaveMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get selected session.
                Session session = SelectedTag as Session;

                if (session == null)
                {
                    return;
                }

                // create a default file.
                if (String.IsNullOrEmpty(m_filePath))
                {
                    FileInfo defaultInfo = new FileInfo(Package.Current.DisplayName);

                    m_filePath = defaultInfo.DirectoryName;
                    m_filePath += "\\";
                    m_filePath += session.SessionName;
                    m_filePath += ".xml";
                }

                // prompt user to select file.
                FileSavePicker dialog = new FileSavePicker();
                dialog.DefaultFileExtension = ".xml";
                dialog.SuggestedFileName = m_filePath;
                dialog.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                dialog.CommitButtonText = "Save Subscriptions";
                StorageFile file = await dialog.PickSaveFileAsync();
                
                // save file.
                session.Save(file.Path);

                // remember file path.
                m_filePath = file.Path;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void SessionLoadMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get selected session.
                Session session = SelectedTag as Session;

                if (session == null)
                {
                    return;
                }

                // create a default file.
                if (String.IsNullOrEmpty(m_filePath))
                {
                    FileInfo defaultInfo = new FileInfo(Package.Current.DisplayName);

                    m_filePath = defaultInfo.DirectoryName;
                    m_filePath += "\\";
                    m_filePath += session.SessionName;
                    m_filePath += ".xml";
                }

                FileInfo fileInfo = new FileInfo(m_filePath);
                FileOpenPicker dialog = new FileOpenPicker();
                dialog.FileTypeFilter.Add(".xml");
                dialog.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                dialog.CommitButtonText = "Load Subscriptions";
                StorageFile file = await dialog.PickSingleFileAsync();

                // remember file path.
                m_filePath = file.Path;

                // load file.
                IEnumerable<Subscription> subscriptions = session.Load(file.Path);

                // create the subscriptions automatically if the session is connected.
                if (session.Connected)
                {
                    foreach (Subscription subscription in subscriptions)
                    {
                        subscription.Create();
                    }
                }

            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void SetLocaleMI_Click(object sender, EventArgs e)
        {
            try
            {
                // get selected session.
                Session session = SelectedTag as Session;

                if (session == null)
                {
                    return;
                }

                string locale = new SelectLocaleDlg().ShowDialog(session);

                if (locale == null)
                {
                    return;
                }

                PreferredLocales = new string[] { locale };
                session.ChangePreferredLocales(new StringCollection(PreferredLocales));
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
    }
}
