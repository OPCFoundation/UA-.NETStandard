/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using Windows.UI.Text;
using Windows.UI;
using System.Threading;

namespace Opc.Ua.SampleClient
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class ClientPage : Page
    {
        #region Private Fields
        private Session m_session;
        private ApplicationInstance m_application;
        private Opc.Ua.Server.StandardServer m_server;
        private ConfiguredEndpointCollection m_endpoints;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_context;
        private ClientPage m_masterPage;
        private List<ClientPage> m_pages;
        #endregion

        public ClientPage()
        {
            InitializeComponent();
        }

        public ClientPage(
           ServiceMessageContext context,
           ApplicationInstance application,
           ClientPage masterPage,
           ApplicationConfiguration configuration)
        {
            InitializeComponent();

            if (!configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                configuration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
        
            m_masterPage = masterPage;
            m_context = context;
            m_application = application;
            m_server = application.Server as Opc.Ua.Server.StandardServer;

            if (m_masterPage == null)
            {
                m_pages = new List<ClientPage>();
            }

            m_configuration = configuration;
            
            SessionsCTRL.Configuration = configuration;
            SessionsCTRL.MessageContext = context;
            SessionsCTRL.AddressSpaceCtrl = BrowseCTRL;
            SessionsCTRL.NodeSelected += SessionCtrl_NodeSelected;

            // get list of cached endpoints.
            m_endpoints = m_configuration.LoadCachedEndpoints(true);
            m_endpoints.DiscoveryUrls = configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            
            // hook up endpoint selector
            EndpointSelectorCTRL.Initialize(m_endpoints, m_configuration);
            EndpointSelectorCTRL.ConnectEndpoint += EndpointSelectorCTRL_ConnectEndpoint;
            EndpointSelectorCTRL.EndpointsChanged += EndpointSelectorCTRL_OnChange;

            BrowseCTRL.SessionTreeCtrl = SessionsCTRL;
            BrowseCTRL.NodeSelected += BrowseCTRL_NodeSelected;

            // exception dialog
            GuiUtils.ExceptionMessageDlg += ExceptionMessageDlg;

            ServerUrlTB.Text = "None";
        }

        void RemoveAllClickEventsFromButton()
        {
            CommandBTN.Click -= ContextMenu_OnDelete;
            CommandBTN.Click -= ContextMenu_OnCancel;
            CommandBTN.Click -= ContextMenu_OnDisconnect;
            CommandBTN.Click -= ContextMenu_OnReport;
        }

        private void SessionCtrl_NodeSelected(object sender, TreeNodeActionEventArgs e)
        {
            if (e.Node != null)
            {
                MonitoredItem item = e.Node as MonitoredItem;
                if (e.Node is MonitoredItem)
                {
                    CommandBTN.Visibility = Visibility.Visible;
                    CommandBTN.Content = "Delete";
                    RemoveAllClickEventsFromButton();
                    CommandBTN.Click += ContextMenu_OnDelete;
                    CommandBTN.Tag = e.Node;
                }
                else if (e.Node is Subscription)
                {
                    CommandBTN.Visibility = Visibility.Visible;
                    CommandBTN.Content = "Cancel";
                    RemoveAllClickEventsFromButton();
                    CommandBTN.Click += ContextMenu_OnCancel;
                    CommandBTN.Tag = e.Node;
                }
                else if (e.Node is Session)
                {
                    CommandBTN.Visibility = Visibility.Visible;
                    CommandBTN.Content = "Disconnect";
                    RemoveAllClickEventsFromButton();
                    CommandBTN.Click += ContextMenu_OnDisconnect;
                    CommandBTN.Tag = e.Node;

                    // Update current session object
                    m_session = (Session)e.Node;
                }
                else
                {
                    RemoveAllClickEventsFromButton();
                    CommandBTN.Visibility = Visibility.Collapsed;
                    CommandBTN.Tag = null;
                }
            }
        }

        private void BrowseCTRL_NodeSelected(object sender, TreeNodeActionEventArgs e)
        {
            if (e.Node != null)
            {
                ReferenceDescription reference = e.Node as ReferenceDescription;
                if (reference != null && reference.NodeClass == NodeClass.Variable)
                {
                    CommandBTN.Visibility = Visibility.Visible;
                    CommandBTN.Content = "Report";
                    RemoveAllClickEventsFromButton();
                    CommandBTN.Click += ContextMenu_OnReport;
                    CommandBTN.Tag = e.Node;
                }
                else
                {
                    RemoveAllClickEventsFromButton();
                    CommandBTN.Visibility = Visibility.Collapsed;
                    CommandBTN.Tag = null;
                }
            }
        }

        private void ContextMenu_OnDisconnect(object sender, RoutedEventArgs e)
        {
            try
            {
                SessionsCTRL.Delete(CommandBTN.Tag as Session);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ContextMenu_OnCancel(object sender, RoutedEventArgs e)
        {
            try
            {
                SessionsCTRL.Delete(CommandBTN.Tag as Subscription);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ContextMenu_OnDelete(object sender, RoutedEventArgs e)
        {
            try
            {
                var monitoredItem = CommandBTN.Tag as MonitoredItem;
                if (monitoredItem == null)
                    return;
                var subscription = monitoredItem.Subscription;
                SessionsCTRL.Delete(monitoredItem);
                if (subscription.MonitoredItemCount == 0)
                {
                    // Remove subscription if no more items
                    CommandBTN.Tag = subscription;
                    ContextMenu_OnCancel(sender, e);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ContextMenu_OnReport(object sender, RoutedEventArgs e)
        {
            try
            {
                // can only subscribe to local variables. 
                ReferenceDescription reference = CommandBTN.Tag as ReferenceDescription;
                if (m_session != null && reference != null)
                {
                    CreateMonitoredItem(
                        m_session, null, (NodeId)reference.NodeId, MonitoringMode.Reporting);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            ManualResetEvent ev = new ManualResetEvent(false);
            Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    await GuiUtils.HandleCertificateValidationError(this, validator, e);
                    ev.Set();
                }
                ).AsTask().Wait();
            ev.WaitOne();
        }

        public void OpenPage()
        {
            if (m_masterPage == null)
            {
                ClientPage page = new ClientPage(m_context, m_application, this, m_configuration);
                m_pages.Add(page);
                page.Unloaded += Window_PageClosing;
            }
            else
            {
                m_masterPage.OpenPage();
            }
        }

        async void Window_PageClosing(object sender, RoutedEventArgs e)
        {
            if (m_masterPage == null && m_pages.Count > 0)
            {
                MessageDlg dialog = new MessageDlg("Close all sessions?", MessageDlgButton.Yes, MessageDlgButton.No);
                MessageDlgButton result = await dialog.ShowAsync();
                if (result != MessageDlgButton.Yes)
                {
                    return;
                }
            }

            BrowseCTRL.Clear();

            for (int ii = 0; ii < m_pages.Count; ii++)
            {
                if (Object.ReferenceEquals(m_pages[ii], sender))
                {
                    m_pages.RemoveAt(ii);
                    break;
                }
            }
        }

        /// <summary>
        /// Provides a user defined method.
        /// </summary>
        protected virtual async void DoTest(Session session)
        {
            MessageDlg dialog = new MessageDlg("A handy place to put test code.");
            await dialog.ShowAsync();
        }

        async Task EndpointSelectorCTRL_ConnectEndpoint(object sender, ConnectEndpointEventArgs e)
        {
            try
            {
                // disable Connect while connecting button
                EndpointSelectorCTRL.IsEnabled = false;
                // Connect
                e.UpdateControl = await Connect(e.Endpoint);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
                e.UpdateControl = false;
            }
            finally
            {
                // enable Connect button
                EndpointSelectorCTRL.IsEnabled = true;
            }
        }

        private void EndpointSelectorCTRL_OnChange(object sender, EventArgs e)
        {
            try
            {
                m_endpoints.Save();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        /// <summary>
        /// Connects to a server.
        /// </summary>
        public async Task<bool> Connect(ConfiguredEndpoint endpoint)
        {
            bool result = false;
            if (endpoint == null)
            {
                return false;
            }

            // connect dialogs
            Session session = await SessionsCTRL.Connect(endpoint);

            if (session != null)
            {
                //hook up new session
                session.KeepAlive += new KeepAliveEventHandler(StandardClient_KeepAlive);
                StandardClient_KeepAlive(session, null);

               // BrowseCTRL.SetView(session, BrowseViewType.Objects, null);
                result = true;
            }

            return result;
        }

        /// <summary>
        /// Updates the status control when a keep alive event occurs.
        /// </summary>
        async void StandardClient_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (!Dispatcher.HasThreadAccess)
            {
                await Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    StandardClient_KeepAlive( sender, e);
                });
                return;
            }

            if (sender != null && sender.Endpoint != null)
            {
                ServerUrlTB.Text = Utils.Format(
                    "{0} ({1}) {2}",
                    sender.Endpoint.EndpointUrl,
                    sender.Endpoint.SecurityMode,
                    (sender.EndpointConfiguration.UseBinaryEncoding) ? "UABinary" : "XML");
            }
            else
            {
                ServerUrlTB.Text = "None";
            }

            if (e != null && m_session != null)
            {
                SessionsCTRL.UpdateSessionNode(m_session);

                if (ServiceResult.IsGood(e.Status))
                {
                    ServerStatusTB.Text = Utils.Format(
                        "Server Status: {0} {1:yyyy-MM-dd HH:mm:ss} {2}/{3}",
                        e.CurrentState,
                        e.CurrentTime.ToLocalTime(),
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount);
                    ServerStatusTB.Foreground = new SolidColorBrush(Colors.Black);
                    ServerStatusTB.FontWeight = FontWeights.Normal;
                }
                else
                {
                    ServerStatusTB.Text = String.Format(
                        "{0} {1}/{2}", e.Status,
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount);
                    ServerStatusTB.Foreground = new SolidColorBrush(Colors.Red);
                    ServerStatusTB.FontWeight = FontWeights.Bold;
                }
            }
        }

        async void ExceptionMessageDlg(string message)
        {
            await Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
            {
                MessageDlg dialog = new MessageDlg(message);
                await dialog.ShowAsync();
            });
        }

        private void MainPage_PageClosing(object sender, RoutedEventArgs e)
        {
            try
            {
                SessionsCTRL.Close();

                if (m_masterPage == null)
                {
                    m_application.Stop();
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void DiscoverServersMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = new ConfiguredServerListDlg().ShowDialog(m_configuration, true);

                if (endpoint != null)
                {
                    EndpointSelectorCTRL.SelectedEndpoint = endpoint;
                    return;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void NewWindowMI_Click(object sender, EventArgs e)
        {
            try
            {
                this.OpenPage();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void Discovery_RegisterMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_server != null)
                {
                    OnRegister(null);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void OnRegister(object sender)
        {
            try
            {
                Opc.Ua.Server.StandardServer server = m_server;

                if (server != null)
                {
                    await server.RegisterWithDiscoveryServer();
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Could not register with the LDS");
            }
        }

        public void CreateMonitoredItem(
           Session session, Subscription subscription, NodeId nodeId, MonitoringMode mode)
        {
            if (subscription == null)
            {
                subscription = session.DefaultSubscription;
                if (session.AddSubscription(subscription))
                    subscription.Create();
            }
            else
            {
                session.AddSubscription(subscription);
            }

            // add the new monitored item.
            MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem);

            monitoredItem.StartNodeId = nodeId;
            monitoredItem.AttributeId = Attributes.Value;
            monitoredItem.DisplayName = nodeId.Identifier.ToString();
            monitoredItem.MonitoringMode = mode;
            monitoredItem.SamplingInterval = mode == MonitoringMode.Sampling ? 1000 : 0;
            monitoredItem.QueueSize = 0;
            monitoredItem.DiscardOldest = true;

            monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);
            subscription.AddItem(monitoredItem);
            subscription.ApplyChanges();
        }

        private async void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (e.NotificationValue == null)
            {
                return;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,() =>
            {
                try
                {
                    XmlEncoder encoder = new XmlEncoder(monitoredItem.Subscription.Session.MessageContext);
                    e.NotificationValue.Encode(encoder);
                    ServerStatusTB.Text = encoder.Close();
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Error processing monitored item notification.");
                }
            });
        }

        private void Task_TestMI_Click(object sender, EventArgs e)
        {
            try
            {
                DoTest(m_session);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

    }
}
