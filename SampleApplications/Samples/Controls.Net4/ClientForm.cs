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
using System.Data;
using System.Drawing;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Description;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;

namespace Opc.Ua.Sample.Controls
{
    public partial class ClientForm : Form
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
        private ClientForm m_masterForm;
        private List<ClientForm> m_forms;
        #endregion
        
        public ClientForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        public ClientForm(
            ServiceMessageContext context,
            ApplicationInstance application, 
            ClientForm masterForm, 
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
                m_forms = new List<ClientForm>();
            }

            SessionsCTRL.Configuration  = m_configuration = configuration;
            SessionsCTRL.MessageContext = context;

            // get list of cached endpoints.
            m_endpoints = m_configuration.LoadCachedEndpoints(true);
            m_endpoints.DiscoveryUrls = configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            EndpointSelectorCTRL.Initialize(m_endpoints, m_configuration);

            // initialize control state.
            Disconnect();
        }

        /// <summary>
        /// Opens a new form.
        /// </summary>
        public void OpenForm()
        {
            if (m_masterForm == null)
            {
                ClientForm form = new ClientForm(m_context, m_application, this, m_configuration);
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
        /// Handles a close event fo
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
        /// Disconnect from the server if ths form is closing.
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

                List<ClientForm> forms = new List<ClientForm>(m_forms);

                foreach (ClientForm form in forms)
                {
                    form.Close();
                }
            }

            Disconnect();
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
        public void Connect(ConfiguredEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return;
            }

            Session session = SessionsCTRL.Connect(endpoint); 

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

        private void PerformanceTestMI_Click(object sender, EventArgs e)
        {  
            try
            {
                new PerformanceTestDlg().ShowDialog(
                    m_configuration,
                    m_endpoints,
                    m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true));
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
                System.Diagnostics.Process.Start( Path.GetDirectoryName(Application.ExecutablePath) + "\\WebHelp\\index.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }
    }
}
