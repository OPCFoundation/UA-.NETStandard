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
using System.Drawing;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client.Controls;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client
{
    public partial class MainForm : Form
    {
        public MainForm(ApplicationInstance application)
        {
            InitializeComponent();
            Icon = ClientUtils.GetAppIcon();

            m_application = application;

            // get the configuration.
            m_configuration = m_application.ApplicationConfiguration.ParseExtension<GlobalDiscoveryClientConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new GlobalDiscoveryClientConfiguration()
                {
                    GlobalDiscoveryServerUrl = "opc.tcp://localhost:58810/GlobalDiscoveryServer",
                    ExternalEditor = "notepad.exe"
                };
            }

            m_filters = new QueryServersFilter();
            m_identity = new UserIdentity();
            m_gds = new GlobalDiscoveryServerClient(m_application, m_configuration.GlobalDiscoveryServerUrl);
            m_gds.KeepAlive += GdsServer_KeepAlive;
            m_gds.ServerStatusChanged += GdsServer_StatusNotification;
            m_lds = new LocalDiscoveryServerClient(m_application.ApplicationConfiguration);
            m_server = new ServerPushConfigurationClient(m_application);
            m_server.AdminCredentialsRequired += Server_AdminCredentialsRequired;
            m_server.KeepAlive += Server_KeepAlive;
            m_server.ServerStatusChanged += Server_StatusNotification;
            m_server.ConnectionStatusChanged += Server_ConnectionStatusChanged;

            RegistrationPanel.Initialize(m_gds, m_server, null, m_configuration);

            m_application.ApplicationConfiguration.CertificateValidator.CertificateValidation += CertificateValidator_CertificateValidation;
            UpdateStatus(true, DateTime.MinValue, "---");
            UpdateGdsStatus(true, DateTime.MinValue, "---");
            UpdateMainFormHeader();

            ShowPanel(Panel.None);


            SelectServerButton.Enabled = false;
            ServerStatusButton.Enabled = false;
            CertificateButton.Enabled = false;
            HttpsCertificateButton.Visible = false;
            TrustListButton.Enabled = false;
            HttpsTrustListButton.Visible = false;
        }

        private ApplicationInstance m_application;
        private ConfiguredEndpointCollection m_endpoints = null;
        private QueryServersFilter m_filters;
        private UserIdentity m_identity;
        private GlobalDiscoveryServerClient m_gds;
        private LocalDiscoveryServerClient m_lds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_registeredApplication;
        private GlobalDiscoveryClientConfiguration m_configuration;
        private bool m_gdsConfigured;

        private enum Panel
        {
            None,
            Registration,
            SelectServer,
            ServerStatus,
            Certificate,
            HttpsCertificate,
            TrustList,
            HttpsTrustList,
            Discovery
        }

        private void Server_ConnectionStatusChanged(object sender, EventArgs e)
        {
            if (Object.ReferenceEquals(sender, m_server))
            {
                if (m_server.IsConnected)
                {
                    ServerStatusPanel.Initialize(m_server);
                }
                else
                {
                    ServerStatusPanel.Initialize(null);
                }
            }
        }

        private void ShowPanel(Panel panel)
        {
            ServerUrlPanel.Visible = false;
            RegistrationPanel.Visible = false;
            ServerStatusPanel.Visible = false;
            CertificatePanel.Visible = false;
            TrustListPanel.Visible = false;
            DiscoveryPanel.Visible = false;
            
            foreach (var control in LeftPanel.Controls)
            {
                if (control is Button button)
                {
                    button.BackColor = Color.MidnightBlue;
                }
            }

            if (panel == Panel.Registration)
            {
                RegistrationPanel.Visible = true;
                RegistrationButton.BackColor = Color.CornflowerBlue;
            }

            if (panel == Panel.ServerStatus)
            {
                ServerUrlPanel.Visible = true;
                ServerStatusPanel.Visible = true;
                ServerStatusButton.BackColor = Color.CornflowerBlue;
            }

            if (panel == Panel.Certificate)
            {
                CertificatePanel.Visible = true;
                CertificateButton.BackColor = Color.CornflowerBlue;
            }

            if (panel == Panel.HttpsCertificate)
            {
                CertificatePanel.Visible = true;
                HttpsCertificateButton.BackColor = Color.CornflowerBlue;
            }

            if (panel == Panel.TrustList)
            {
                TrustListPanel.Visible = true;
                TrustListButton.BackColor = Color.CornflowerBlue;
            }

            if (panel == Panel.HttpsTrustList)
            {
                TrustListPanel.Visible = true;
                HttpsTrustListButton.BackColor = Color.CornflowerBlue;
            }

            if (panel == Panel.Discovery)
            {
                DiscoveryPanel.Visible = true;
                DiscoveryButton.BackColor = Color.CornflowerBlue;
            }
        }

        private void SetServer(EndpointDescription endpoint)
        {
            DisconnectButton_Click(this, null);

            if (endpoint != null)
            {
                var ce = new ConfiguredEndpointCollection().Add(endpoint);
                ServerUrlTextBox.Text = ce.ToString();
                ServerUrlTextBox.Tag = m_server.Endpoint = ce;
                UpdateStatus(true, DateTime.UtcNow, "Disconnected {0}", ce);
            }
            else
            {
                ServerUrlTextBox.Text = "";
                ServerUrlTextBox.Tag = m_server.Endpoint = null;
                UpdateStatus(true, DateTime.MinValue, "---");
            }

            ServerStatusButton.Enabled = endpoint != null;
        }

        private void SelectServerButton_Click(object sender, EventArgs e)
        {
            try
            {
                var endpoint = new SelectServerDialog().ShowDialog(this, m_endpoints, m_lds, m_gds, m_filters);

                if (endpoint != null)
                {
                    SetServer(endpoint);
                    RegistrationPanel.Initialize(m_gds, m_server, endpoint, m_configuration);
                    SelectGdsButton.Visible = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private async void ConnectButton_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = (ConfiguredEndpoint)ServerUrlTextBox.Tag;

                if (endpoint == null)
                {
                    return;
                }

                await m_server.Connect(endpoint.Description.EndpointUrl);

                ServerStatusPanel.Initialize(m_server);
                await CertificatePanel.Initialize(m_configuration, m_gds, m_server, m_registeredApplication, false);
            }
            catch (Exception exception)
            {
                ExceptionDlg.Show(this.Text, exception);
            }
        }

        private void GdsServer_StatusNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MonitoredItemNotificationEventHandler(GdsServer_StatusNotification), monitoredItem, e);
                return;
            }

            try
            {
                MonitoredItemNotification notification = (MonitoredItemNotification)e.NotificationValue;
                ServerStatusPanel.SetServerStatus(notification.Value.GetValue<ServerStatusDataType>(null));
            }
            catch (Exception exception)
            {
                ExceptionDlg.Show(this.Text, exception);
            }
        }

        private void Server_StatusNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MonitoredItemNotificationEventHandler(Server_StatusNotification), monitoredItem, e);
                return;
            }

            try
            {
                MonitoredItemNotification notification = (MonitoredItemNotification)e.NotificationValue;
                ServerStatusPanel.SetServerStatus(notification.Value.GetValue<ServerStatusDataType>(null));
            }
            catch (Exception exception)
            {
                ExceptionDlg.Show(this.Text, exception);
            }
        }

        private void CertificateValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new CertificateValidationEventHandler(CertificateValidator_CertificateValidation), sender, e);
                return;
            }

            try
            {
                var result = new UntrustedCertificateDialog().ShowDialog(this, e.Certificate);

                if (result == DialogResult.OK)
                {
                    e.Accept = true;
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void GdsServer_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new KeepAliveEventHandler(GdsServer_KeepAlive), session, e);
                return;
            }

            try
            {
                // check for events from discarded sessions.
                if (!Object.ReferenceEquals(session, m_gds.Session))
                {
                    return;
                }

                if (e == null)
                {
                    UpdateGdsStatus(true, DateTime.Now, "Disconnected");
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    UpdateGdsStatus(true, e.CurrentTime, "Communication Error ({0})", e.Status);
                    return;
                }

                // update status.
                UpdateGdsStatus(false, e.CurrentTime, "Connected {0}", session.ConfiguredEndpoint);
            }
            catch (Exception exception)
            {
                ExceptionDlg.Show(this.Text, exception);
            }
        }

        private void Server_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (this.InvokeRequired)
            {
                BeginInvoke(new KeepAliveEventHandler(Server_KeepAlive), session, e);
                return;
            }

            try
            {
                // check for events from discarded sessions.
                if (!Object.ReferenceEquals(session, m_server.Session))
                {
                    return;
                }

                if (e == null)
                {
                    UpdateStatus(false, DateTime.Now, "Disconnected");
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    UpdateStatus(true, e.CurrentTime, "Communication Error ({0})", e.Status);
                    return;
                }

                // update status.
                UpdateStatus(false, e.CurrentTime, "Connected {0}", session.ConfiguredEndpoint);
            }
            catch (Exception exception)
            {
                ExceptionDlg.Show(this.Text, exception);
            }
        }

        private void UpdateGdsStatus(bool error, DateTime time, string status, params object[] args)
        {
            if (error)
            {
                GdsServerStatusIcon.Image = global::Opc.Ua.Gds.Client.Properties.Resources.error;
            }
            else
            {
                GdsServerStatusIcon.Image = global::Opc.Ua.Gds.Client.Properties.Resources.nav_plain_green;
            }

            GdsServerStatusLabel.Text = String.Format(status, args);
            GdsServerStatusLabel.ForeColor = (error) ? Color.Red : Color.Empty;
            GdsServerStatusTime.Text = (time != DateTime.MinValue) ? time.ToLocalTime().ToString("hh:mm:ss") : "---";
            GdsServerStatusTime.ForeColor = (error) ? Color.Red : Color.Empty;
        }

        private void UpdateStatus(bool error, DateTime time, string status, params object[] args)
        {
            if (error)
            {
                ServerStatusIcon.Image = global::Opc.Ua.Gds.Client.Properties.Resources.error;
            }
            else
            {
                ServerStatusIcon.Image = global::Opc.Ua.Gds.Client.Properties.Resources.nav_plain_green;
            }
            ServerStatusLabel.Text = String.Format(status, args);
            ServerStatusLabel.ForeColor = (error) ? Color.Red : Color.Empty;
            ServerStatusTime.Text = (time != DateTime.MinValue) ? time.ToLocalTime().ToString("hh:mm:ss") : "";
            ServerStatusTime.ForeColor = (error) ? Color.Red : Color.Empty;
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_server.IsConnected)
                {
                    m_server.Disconnect();
                    UpdateStatus(true, DateTime.UtcNow, "Disconnected {0}", m_server.Endpoint);
                    ServerStatusPanel.Initialize(null);
                }
            }
            catch (Exception exception)
            {
                ExceptionDlg.Show(this.Text, exception);
            }
        }

        private void RegistrationButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_gdsConfigured)
                {
                    string uri = new SelectGdsDialog().ShowDialog(null, m_gds, m_gds.GetDefaultGdsUrls(m_lds));
                    if (uri != null)
                    {
                        m_configuration.GlobalDiscoveryServerUrl = m_gds.EndpointUrl;
                        m_gdsConfigured = true;
                        SelectGdsButton.Visible = true;
                    }
                }

                ShowPanel(Panel.Registration);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void ServerStatusButton_Click(object sender, EventArgs e)
        {
            try
            {
                ShowPanel(Panel.ServerStatus);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private async void CertificateButton_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await CertificatePanel.Initialize(m_configuration, m_gds, m_server, m_registeredApplication, false);
                ShowPanel(Panel.Certificate);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private async void HttpsCertificateButton_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await CertificatePanel.Initialize(m_configuration, m_gds, m_server, m_registeredApplication, true);
                ShowPanel(Panel.HttpsCertificate);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void TrustListButton_Click(object sender, EventArgs e)
        {
            try
            {
                TrustListPanel.Initialize(m_gds, m_server, m_registeredApplication, false);
                ShowPanel(Panel.TrustList);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void HttpsTrustListButton_Click(object sender, EventArgs e)
        {
            try
            {
                TrustListPanel.Initialize(m_gds, m_server, m_registeredApplication, true);
                ShowPanel(Panel.HttpsTrustList);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void DiscoveryButton_Click(object sender, EventArgs e)
        {
            try
            {
                DiscoveryPanel.Initialize(m_endpoints, m_lds, m_gds, m_filters);
                ShowPanel(Panel.Discovery);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void RegistrationPanel_ServerRequired(object sender, SelectServerEventArgs e)
        {
            try 
            {
                SelectServerButton_Click(this, null);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private async void RegistrationPanel_RegisteredApplicationChangedAsync(object sender, RegisteredApplicationChangedEventArgs e)
        {
            try
            {
                var app =  m_registeredApplication = e.Application;

                if (app == null || app.RegistrationType == RegistrationType.ClientPull)
                {
                    SetServer(null);
                }
                else if (app.RegistrationType == RegistrationType.ServerPush)
                {
                    if (!String.IsNullOrEmpty(app.ServerUrl))
                    {
                        var endpoint = new EndpointDescription(app.ServerUrl);

                        endpoint.Server.ApplicationType = ApplicationType.Server;
                        endpoint.Server.ApplicationUri = app.ApplicationUri;
                        endpoint.Server.ProductUri = app.ProductUri;
                        endpoint.Server.ApplicationName = app.ApplicationName;
                        endpoint.Server.DiscoveryUrls = (app.DiscoveryUrl != null) ? new StringCollection(app.DiscoveryUrl) : null;

                        SetServer(endpoint);
                    }
                }

                CertificateButton.Enabled = (e.Application != null);
                TrustListButton.Enabled = (e.Application != null);
#if !NO_HTTPS
                HttpsCertificateButton.Visible = (e.Application != null && !String.IsNullOrEmpty(e.Application.GetHttpsDomainName()));
                HttpsTrustListButton.Visible = (e.Application != null && !String.IsNullOrEmpty(e.Application.HttpsTrustListStorePath));
#endif
                await CertificatePanel.Initialize(m_configuration, m_gds, m_server, e.Application, false);
                TrustListPanel.Initialize(m_gds, m_server, e.Application, false);
                UpdateMainFormHeader();
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void ConfigurationButton_Click(object sender, EventArgs e)
        {

        }

        private void SelectGdsButton_Click(object sender, EventArgs e)
        {
            m_gds.AdminCredentials = null;
            m_gds.Disconnect();
            m_gdsConfigured = false;
            UpdateGdsStatus(true, DateTime.UtcNow, "Disconnected");

            string uri = new SelectGdsDialog().ShowDialog(null, m_gds, m_gds.GetDefaultGdsUrls(m_lds));
            if (uri != null)
            {
                m_configuration.GlobalDiscoveryServerUrl = m_gds.EndpointUrl;
                m_gdsConfigured = true;
                UpdateGdsStatus(false, DateTime.UtcNow, "Connected");
            }
        }

        private void Server_AdminCredentialsRequired(object sender, AdminCredentialsRequiredEventArgs e)
        {
            try
            {
                var identity = new Opc.Ua.Client.Controls.UserNamePasswordDlg().ShowDialog(e.Credentials, "Provide PushServer Administrator Credentials");

                if (identity != null)
                {
                    e.Credentials = identity;
                    e.CacheCredentials = true;
                }
            }
            catch (Exception exception)
            {
                ExceptionDlg.Show(this.Text, exception);
            }
        }

        private void UpdateMainFormHeader()
        {
            string newText = "Global Discovery Client ";
            if (m_registeredApplication != null)
            {
                switch (m_registeredApplication.RegistrationType)
                {
                    case RegistrationType.ServerPush:
                        newText += "(Server Push)";
                        break;
                    case RegistrationType.ClientPull:
                        newText += "(Client Pull)";
                        break;
                    case RegistrationType.ServerPull:
                        newText += "(Server Pull)";
                        break;
                }
            }
            this.Text = newText;
        }

    }
}

