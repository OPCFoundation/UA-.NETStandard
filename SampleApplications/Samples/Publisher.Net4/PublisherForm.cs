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
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using System.Threading.Tasks;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using Opc.Ua.Publisher;

namespace Opc.Ua.Sample.Controls
{
    public partial class PublisherForm : Form
    {
        #region Private Fields
        private Session m_session;
        private SessionReconnectHandler m_reconnectHandler;
        private int m_reconnectPeriod = 10;
        private ApplicationInstance m_application;
        private Opc.Ua.Server.StandardServer m_server;
        private ConfiguredEndpointCollection m_endpoints;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_context;
        private PublisherForm m_masterForm;
        private List<PublisherForm> m_forms;
        private AmqpConnectionCollection m_publishers;
        #endregion

        public PublisherForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        public PublisherForm(
            ServiceMessageContext context,
            ApplicationInstance application, 
            PublisherForm masterForm, 
            ApplicationConfiguration configuration)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_masterForm = masterForm;
            m_context = context;
            m_application = application;
            m_server = application.Server as Opc.Ua.Server.StandardServer;

            if (m_masterForm == null)
            {
                m_forms = new List<PublisherForm>();
            }

            SessionsCTRL.Configuration  = m_configuration = configuration;
            SessionsCTRL.MessageContext = context;

            // get list of cached endpoints.
            m_endpoints = m_configuration.LoadCachedEndpoints(true);
            m_endpoints.DiscoveryUrls = configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            EndpointSelectorCTRL.Initialize(m_endpoints, m_configuration);

            // initialize control state.
            Disconnect();

            m_publishers = AmqpConnectionCollection.Load(configuration);
            foreach (var publisher in m_publishers)
            {
                Task t = publisher.OpenAsync();
            }

            this.NotificationsCTRL.ItemsAdded += NotificationsCTRL_ItemsAdded;
        }

        private void NotificationsCTRL_ItemsAdded(object sender, ListItemActionEventArgs e)
        {
            try
            {
                foreach (NotificationMessageListCtrl.ItemData item in e.Items)
                {
                    if (item.NotificationMessage == null || item.Subscription.Session == null)
                    {
                        return;
                    }

                    JsonEncoder encoder = new JsonEncoder(
                        item.Subscription.Session.MessageContext, false);

                    foreach (MonitoredItem monitoredItem in item.Subscription.MonitoredItems)
                    {
                        encoder.WriteNodeId("MonitoredItem", monitoredItem.ResolvedNodeId);
                        ((DataChangeNotification)((NotificationData)item.NotificationMessage.NotificationData[0].Body)).MonitoredItems[0].Encode(encoder);

                        string json = encoder.Close();
                        json = json.Replace("\\", "");
                        byte[] bytes = new UTF8Encoding(false).GetBytes(json);

                        foreach (var publisher in m_publishers)
                        {
                            try
                            {
                                publisher.Publish(new ArraySegment<byte>(bytes));
                                Utils.Trace(null, "Publishing: " + json);
                            }
                            catch (Exception ex)
                            {
                                Utils.Trace(ex, "Failed to publish message, dropping....");
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error processing monitored item notification.");
            }
        }

        /// <summary>
        /// Opens a new form.
        /// </summary>
        public void OpenForm()
        {
            if (m_masterForm == null)
            {
                PublisherForm form = new PublisherForm(m_context, m_application, this, m_configuration);
                m_forms.Add(form);
                form.FormClosing += new FormClosingEventHandler(Window_FormClosing);
                form.Show();
            }
            else
            {
                m_masterForm.OpenForm();
            }
        }

        /// <summary>
        /// Handles a close event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Window_FormClosing(object sender, FormClosingEventArgs e)
        {
            for (int ii = 0; ii < m_forms.Count; ii++)
            {
                if (Object.ReferenceEquals(m_forms[ii], sender))
                {
                    m_forms.RemoveAt(ii);
                    break;
                }
            }
        }

        /// <summary>
        /// Disconnect from the server if this form is closing.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (m_masterForm == null && m_forms.Count > 0)
            {
                if (MessageBox.Show("Close all sessions?", "Close Window", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                List<PublisherForm> forms = new List<PublisherForm>(m_forms);

                foreach (PublisherForm form in forms)
                {
                    form.Close();
                }
            }

            Disconnect();

            if (m_publishers != null)
            {
                foreach (var publisher in m_publishers)
                {
                    publisher.Close();
                }
            }
        }

        /// <summary>
        /// Disconnects from a server.
        /// </summary>
        public void Disconnect()
        {
            if (m_session != null)
            {
                // stop any reconnect operation.
                if (m_reconnectHandler != null)
                {
                    m_reconnectHandler.Dispose();
                    m_reconnectHandler = null;
                }

                m_session.Close();
                m_session = null;
            }

            ServerUrlLB.Text = "";
        }

        /// <summary>
        /// Provides a user defined method.
        /// </summary>
        protected virtual void DoTest(Session session)
        {
            MessageBox.Show("A handy place to put test code.");
        }
        
        void EndpointSelectorCTRL_ConnectEndpoint(object sender, ConnectEndpointEventArgs e)
        {
            try
            {
                Connect(e.Endpoint);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
                e.UpdateControl = false;
            }
        }

        private void EndpointSelectorCTRL_OnChange(object sender, EventArgs e)
        {
            try
            {
                m_endpoints.Save();
            }
            catch (Exception)
            {
				// GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Connects to a server.
        /// </summary>
        public async void Connect(ConfiguredEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return;
            }

            Session session = await SessionsCTRL.Connect(endpoint);

            if (session != null)
            {
                // stop any reconnect operation.
                if (m_reconnectHandler != null)
                {
                    m_reconnectHandler.Dispose();
                    m_reconnectHandler = null;
                }

                m_session = session;
                m_session.KeepAlive += new KeepAliveEventHandler(StandardClient_KeepAlive);
                BrowseCTRL.SetView(m_session, BrowseViewType.Objects, null);
                StandardClient_KeepAlive(m_session, null);
            }
        }

        /// <summary>
        /// Updates the status control when a keep alive event occurs.
        /// </summary>
        void StandardClient_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new KeepAliveEventHandler(StandardClient_KeepAlive), sender, e);
                return;
            }
            else if (!IsHandleCreated)
            {
                return;
            }

            if (sender != null && sender.Endpoint != null)
            {
                ServerUrlLB.Text = Utils.Format(
                    "{0} ({1}) {2}", 
                    sender.Endpoint.EndpointUrl, 
                    sender.Endpoint.SecurityMode, 
                    (sender.EndpointConfiguration.UseBinaryEncoding)?"UABinary":"XML");
            }
            else
            {
                ServerUrlLB.Text = "None";
            }

            if (e != null && m_session != null)
            {            
                if (ServiceResult.IsGood(e.Status))
                {
                    ServerStatusLB.Text = Utils.Format(
                        "Server Status: {0} {1:yyyy-MM-dd HH:mm:ss} {2}/{3}", 
                        e.CurrentState, 
                        e.CurrentTime.ToLocalTime(), 
                        m_session.OutstandingRequestCount, 
                        m_session.DefunctRequestCount);
                    
                    ServerStatusLB.ForeColor = Color.Empty;
                    ServerStatusLB.Font = new Font(ServerStatusLB.Font, FontStyle.Regular);
                }
                else
                {
                    ServerStatusLB.Text = String.Format(
                        "{0} {1}/{2}", e.Status,
                        m_session.OutstandingRequestCount, 
                        m_session.DefunctRequestCount);

                    ServerStatusLB.ForeColor = Color.Red;
                    ServerStatusLB.Font = new Font(ServerStatusLB.Font, FontStyle.Bold);

                    if (m_reconnectPeriod <= 0)
                    {
                        return;
                    }

                    if (m_reconnectHandler == null && m_reconnectPeriod > 0)
                    {
                        m_reconnectHandler = new SessionReconnectHandler();
                        m_reconnectHandler.BeginReconnect(m_session, m_reconnectPeriod * 1000, StandardClient_Server_ReconnectComplete);
                    }
                }
            }
        }

        private void StandardClient_Server_ReconnectComplete(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(StandardClient_Server_ReconnectComplete), sender, e);
                return;
            }

            try
            {
                // ignore callbacks from discarded objects.
                if (!Object.ReferenceEquals(sender, m_reconnectHandler))
                {
                    return;
                }

                m_session = m_reconnectHandler.Session;
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;

                BrowseCTRL.SetView(m_session, BrowseViewType.Objects, null);

                SessionsCTRL.Reload(m_session);

                StandardClient_KeepAlive(m_session, null);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {            
            try
            {
                SessionsCTRL.Close();

                if (m_masterForm == null)
                {
                    m_application.Stop();
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void FileExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private async void PerformanceTestMI_Click(object sender, EventArgs e)
        {
            try
            {
                new PerformanceTestDlg().ShowDialog(
                    m_configuration,
                    m_endpoints,
                    await m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true));
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void DiscoverServersMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = new ConfiguredServerListDlg().ShowDialog(m_configuration, true);
                
                if (endpoint != null)
                {
                    this.EndpointSelectorCTRL.SelectedEndpoint = endpoint;
                    return;
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void NewWindowMI_Click(object sender, EventArgs e)
        {
            try
            {
                this.OpenForm();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void Discovery_RegisterMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_server != null)
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(OnRegister, null);
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void OnRegister(object sender)
        {
            try
            {
                Opc.Ua.Server.StandardServer server = m_server;

                if (server != null)
                {
                    server.RegisterWithDiscoveryServer();
                }
            }
            catch (Exception exception)
            {
				Utils.Trace(exception, "Could not register with the LDS");
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
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void contentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start( Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + "WebHelp" + Path.DirectorySeparatorChar + "index.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }
    }
}
