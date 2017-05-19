using System;
using System.Drawing;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Gds;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.GdsClient
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
                m_configuration = new GlobalDiscoveryClientConfiguration();
                m_configuration.GlobalDiscoveryServerUrl = "opc.tcp://localhost:58810";
                m_configuration.ExternalEditor = "devenv.exe";
            }

            m_filters = new QueryServersFilter();
            m_identity = new UserIdentity();
            m_gds = new GlobalDiscoveryServer(m_application, m_configuration);
            m_lds = new LocalDiscoveryServer(m_application.ApplicationConfiguration);
       
            RegistrationPanel.Initialize(m_gds, null, m_configuration);

            m_application.ApplicationConfiguration.CertificateValidator.CertificateValidation += CertificateValidator_CertificateValidation;
            UpdateStatus(true, DateTime.MinValue, "---");

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
        private GlobalDiscoveryServer m_gds;
        private LocalDiscoveryServer m_lds;
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
                Button button = control as Button;

                if (button != null)
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
            if (endpoint != null)
            {
                var ce = new ConfiguredEndpointCollection().Add(endpoint);
                ServerUrlTextBox.Text = ce.ToString();
                ServerUrlTextBox.Tag = ce;
                UpdateStatus(true, DateTime.UtcNow, "Disconnected {0}", ce);
            }
            else
            {
                ServerUrlTextBox.Text = "";
                ServerUrlTextBox.Tag = null;
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
                    RegistrationPanel.Initialize(m_gds, endpoint, m_configuration);
                    return;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
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
                MessageBox.Show(Text + ": " + exception.Message);
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
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }
        
        private void UpdateStatus(bool error, DateTime time, string status, params object[] args)
        {
            if (error)
            {
                ServerStatusIcon.Image = global::Opc.Ua.GdsClient.Properties.Resources.error;
            }
            else
            {
                ServerStatusIcon.Image = global::Opc.Ua.GdsClient.Properties.Resources.nav_plain_green;
            }

            ServerStatusLabel.Text = String.Format(status, args);
            ServerStatusLabel.ForeColor = (error) ? Color.Red : Color.Empty;
            ServerStatusTime.Text = (time != DateTime.MinValue) ? time.ToLocalTime().ToString("hh:mm:ss") : "---";
            ServerStatusTime.ForeColor = (error) ? Color.Red : Color.Empty;
        }

        private void RegistrationButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_gdsConfigured)
                {
                    if (m_gds.SelectDefaultGds(m_lds))
                    {
                        m_configuration.GlobalDiscoveryServerUrl = m_gds.EndpointUrl;
                        m_gdsConfigured = true;
                    }
                }

                ShowPanel(Panel.Registration);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void ServerStatusButton_Click(object sender, EventArgs e)
        {
            try
            {
                ShowPanel(Panel.ServerStatus);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void CertificateButton_Click(object sender, EventArgs e)
        {
            try
            {
                CertificatePanel.Initialize(m_configuration, m_gds, m_registeredApplication, false);
                ShowPanel(Panel.Certificate);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void HttpsCertificateButton_Click(object sender, EventArgs e)
        {
            try
            {
                CertificatePanel.Initialize(m_configuration, m_gds, m_registeredApplication, true);
                ShowPanel(Panel.HttpsCertificate);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void TrustListButton_Click(object sender, EventArgs e)
        {
            try
            {
                TrustListPanel.Initialize(m_gds, m_registeredApplication, false);
                ShowPanel(Panel.TrustList);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void HttpsTrustListButton_Click(object sender, EventArgs e)
        {
            try
            {
                TrustListPanel.Initialize(m_gds, m_registeredApplication, true);
                ShowPanel(Panel.HttpsTrustList);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void DiscoveryButton_Click(object sender, EventArgs e)
        {
            try
            {
                DiscoveryPanel.Initialize(m_endpoints, m_lds, m_gds, m_filters);
                ShowPanel(Panel.Discovery);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void RegistrationPanel_ServerRequired(object sender, SelectServerEventArgs e)
        {
            try 
            {
                SelectServerButton_Click(this, null);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void RegistrationPanel_RegisteredApplicationChanged(object sender, RegisteredApplicationChangedEventArgs e)
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
                HttpsCertificateButton.Visible = (e.Application != null && !String.IsNullOrEmpty(e.Application.GetHttpsDomainName()));
                HttpsTrustListButton.Visible = (e.Application != null && !String.IsNullOrEmpty(e.Application.HttpsTrustListStorePath));

                CertificatePanel.Initialize(m_configuration, m_gds, e.Application, false);
                TrustListPanel.Initialize(m_gds, e.Application, false);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void ConfigurationButton_Click(object sender, EventArgs e)
        {

        }
    }
}

