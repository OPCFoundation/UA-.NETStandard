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
using Windows.UI.Popups;
using Windows.UI;
using System.Threading;
using System.Text;

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
            SessionsCTRL.ContextMenu = new PopupMenu();

            // get list of cached endpoints.
            m_endpoints = m_configuration.LoadCachedEndpoints(true);
            m_endpoints.DiscoveryUrls = configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            
            // hook up endpoint selector
            EndpointSelectorCTRL.Initialize(m_endpoints, m_configuration);
            EndpointSelectorCTRL.ConnectEndpoint += EndpointSelectorCTRL_ConnectEndpoint;
            EndpointSelectorCTRL.EndpointsChanged += EndpointSelectorCTRL_OnChange;

            BrowseCTRL.SessionTreeCtrl = SessionsCTRL;
            BrowseCTRL.NodeSelected += BrowseCTRL_NodeSelected;
            BrowseCTRL.ContextMenu = new PopupMenu();

            // exception dialog
            GuiUtils.ExceptionMessageDlg += ExceptionMessageDlg;

            ServerUrlTB.Text = "None";
        }

        private void SessionCtrl_NodeSelected(object sender, TreeNodeActionEventArgs e)
        {
            SessionsCTRL.ContextMenu.Commands.Clear();

            if (e.Node != null)
            {
                MonitoredItem item = e.Node as MonitoredItem;
                if (e.Node is MonitoredItem)
                    SessionsCTRL.ContextMenu.Commands.Add(new UICommand("Delete", ContextMenu_OnDelete, e.Node));
                else if (e.Node is Subscription)
                    SessionsCTRL.ContextMenu.Commands.Add(new UICommand("Cancel", ContextMenu_OnCancel, e.Node));
                else if (e.Node is Session)
                {
                    SessionsCTRL.ContextMenu.Commands.Add(new UICommand("Disconnect", ContextMenu_OnDisconnect, e.Node));

                    // Update current session object
                    m_session = (Session)e.Node;
                }
            }
        }

        private void BrowseCTRL_NodeSelected(object sender, TreeNodeActionEventArgs e)
        {
            BrowseCTRL.ContextMenu.Commands.Clear();

            if (e.Node != null)
            { 
                ReferenceDescription reference = e.Node as ReferenceDescription;
                if (reference != null && reference.NodeClass == NodeClass.Variable)
                {
                    BrowseCTRL.ContextMenu.Commands.Add(new UICommand("Report", ContextMenu_OnReport, e.Node));
                    BrowseCTRL.ContextMenu.Commands.Add(new UICommand("Sample", ContextMenu_OnSample, e.Node));
                }
            }
        }

        private void ContextMenu_OnDisconnect(IUICommand command)
        {
            try
            {
                SessionsCTRL.Delete(command.Id as Session);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ContextMenu_OnCancel(IUICommand command)
        {
            try
            {
                SessionsCTRL.Delete(command.Id as Subscription);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ContextMenu_OnDelete(IUICommand command)
        {
            try
            {
                var monitoredItem = command.Id as MonitoredItem;
                if (monitoredItem == null)
                    return;
                var subscription = monitoredItem.Subscription;
                SessionsCTRL.Delete(monitoredItem);
                if (subscription.MonitoredItemCount == 0)
                {
                    // Remove subscription if no more items
                    command.Id = subscription;
                    ContextMenu_OnCancel(command);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ContextMenu_OnReport(IUICommand command)
        {
            try
            {
                // can only subscribe to local variables. 
                ReferenceDescription reference = command.Id as ReferenceDescription;
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

        private void ContextMenu_OnSample(IUICommand command)
        {
            try
            {
                // can only subscribe to local variables. 
                ReferenceDescription reference = command.Id as ReferenceDescription;
                if (m_session != null && reference != null)
                {
                    CreateMonitoredItem(
                        m_session, null, (NodeId)reference.NodeId, MonitoringMode.Sampling);
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
            try
            {
                if (e.NotificationValue == null)
                {
                    return;
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,() =>
                {
                    XmlEncoder encoder = new XmlEncoder(monitoredItem.Subscription.Session.MessageContext);
                    e.NotificationValue.Encode(encoder);
                    ServerStatusTB.Text = encoder.Close();
                });
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error processing monitored item notification.");
            }
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
