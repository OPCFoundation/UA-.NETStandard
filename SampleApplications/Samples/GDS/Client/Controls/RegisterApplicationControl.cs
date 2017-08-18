/* ========================================================================
 * Copyright (c) 2005-2017 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Serialization;
using Opc.Ua.Gds;

namespace Opc.Ua.GdsClient
{
    public partial class RegisterApplicationControl : UserControl
    {
        public RegisterApplicationControl()
        {
            InitializeComponent();
            m_lastSavePath = m_lastDirPath = "%MyDocuments%\\OPC Foundation\\GDS";
            Utils.GetAbsoluteDirectoryPath(m_lastDirPath, true, false, true);

            m_application = new Gds.RegisteredApplication();

            RegistrationTypeComboBox.Items.Add("Client - Pull Management");
            RegistrationTypeComboBox.Items.Add("Server - Pull Management");
            RegistrationTypeComboBox.Items.Add("Server - Push Management");
            RegistrationTypeComboBox.SelectedIndex = ServerPullManagement;

            m_promptOnRegistrationTypeChange = false;

            ApplyChangesButton.Enabled = false;
            RegisterApplicationButton.Enabled = false;
            UnregisterApplicationButton.Enabled = false;
            OpenConfigurationButton.Enabled = false;
        }

        private string m_lastDirPath;
        private string m_lastSavePath;
        private GlobalDiscoveryServer m_gds;
        private RegisteredApplication m_application;
        private bool m_promptOnRegistrationTypeChange;
        private string m_externalEditor;

        private const int ClientPullManagement = (int)RegistrationType.ClientPull;
        private const int ServerPullManagement = (int)RegistrationType.ServerPull;
        private const int ServerPushManagement = (int)RegistrationType.ServerPush;

        public event EventHandler<SelectServerEventArgs> SelectServer;
        public event EventHandler<RegisteredApplicationChangedEventArgs> RegisteredApplicationChanged;

        /// <summary>
        /// Gets the registered application.
        /// </summary>
        /// <value>
        /// The registered application.
        /// </value>
        public RegisteredApplication RegisteredApplication
        {
            get
            {
                return m_application;
            }
        }
        
        public void Initialize(GlobalDiscoveryServer gds, EndpointDescription endpoint, GlobalDiscoveryClientConfiguration configuration)
        {
            m_gds = gds;
            m_application.ServerUrl = null;

            if (configuration != null)
            {
                m_externalEditor = configuration.ExternalEditor;
            }

            if (endpoint != null)
            {
                ClearFields();

                m_application.ServerUrl = endpoint.EndpointUrl;
                var server = endpoint.Server;

                ApplicationUriTextBox.Text = server.ApplicationUri;
                ReadRegistration(true);

                ApplicationNameTextBox.Text = (server.ApplicationName != null) ? server.ApplicationName.Text : "";
                ProductUriTextBox.Text = server.ProductUri;
                SetDiscoveryUrls(server.DiscoveryUrls);
                SetServerCapabilities(new string[] { ServerCapability.LiveData });

                ControlToData();
                RaiseRegisteredApplicationChangedEvent(m_application);
            }
        }

        private string SelectServerUrl(IList<string> discoveryUrls)
        {
            if (discoveryUrls == null || discoveryUrls.Count == 0)
            {
                return null;
            }

            string url = null;

            // always use opc.tcp by default.
            foreach (string discoveryUrl in discoveryUrls)
            {
                if (discoveryUrl.StartsWith("opc.tcp://"))
                {
                    url = discoveryUrl;
                    break;
                }
            }

            // try HTTPS if no opc.tcp.
            if (url == null)
            {
                foreach (string discoveryUrl in discoveryUrls)
                {
                    if (discoveryUrl.StartsWith("https://"))
                    {
                        url = discoveryUrl;
                        break;
                    }
                }
            }

            // use the first URL if nothing else.
            if (url == null)
            {
                url = discoveryUrls[0];
            }

            return url;
        }

        private void ControlToData()
        {
            ApplicationRecordDataType record = (ApplicationRecordDataType)ApplicationIdTextBox.Tag;

            m_application.RegistrationType = (RegistrationType)RegistrationTypeComboBox.SelectedIndex;
            m_application.ServerUrl = SelectServerUrl(DiscoveryUrlsTextBox.Tag as IList<string>);

            if (record != null)
            {
                m_application.ApplicationId = (record.ApplicationId != null) ? record.ApplicationId.ToString() : null;
                m_application.ApplicationUri = record.ApplicationUri;
                m_application.ApplicationName = (record.ApplicationNames != null && record.ApplicationNames.Count > 0 && record.ApplicationNames[0].Text != null) ? record.ApplicationNames[0].Text.ToString() : null;
                m_application.ProductUri = record.ProductUri;
                m_application.DiscoveryUrl = (record.DiscoveryUrls != null) ? record.DiscoveryUrls.ToArray() : null;
                m_application.ServerCapability = (record.ServerCapabilities != null) ? record.ServerCapabilities.ToArray() : null;
            }
            else
            {
                m_application.ApplicationId = null;
                m_application.ApplicationUri = ApplicationUriTextBox.Text.Trim();
                m_application.ApplicationName = ApplicationNameTextBox.Text.Trim();
                m_application.ProductUri = ProductUriTextBox.Text.Trim();
                m_application.DiscoveryUrl = (DiscoveryUrlsTextBox.Tag != null) ? ((IList<string>)DiscoveryUrlsTextBox.Tag).ToArray() : null;
                m_application.ServerCapability = (ServerCapabilitiesTextBox.Tag != null) ? ((IList<string>)ServerCapabilitiesTextBox.Tag).ToArray() : null;
            }

            switch (m_application.RegistrationType)
            {
                case RegistrationType.ClientPull:
                case RegistrationType.ServerPull:
                {
                    m_application.ConfigurationFile = ConfigurationFileTextBox.Text;
                    m_application.CertificateStorePath = CertificateStorePathTextBox.Text.Trim();
                    m_application.CertificateSubjectName = CertificateSubjectNameTextBox.Text.Trim();
                    m_application.CertificatePublicKeyPath = CertificatePublicKeyPathTextBox.Text.Trim();
                    m_application.CertificatePrivateKeyPath = CertificatePrivateKeyPathTextBox.Text.Trim();
                    m_application.TrustListStorePath = TrustListStorePathTextBox.Text.Trim();
                    m_application.IssuerListStorePath = IssuerListStorePathTextBox.Text.Trim();
                    m_application.HttpsCertificatePublicKeyPath = HttpsCertificatePublicKeyPathTextBox.Text.Trim();
                    m_application.HttpsCertificatePrivateKeyPath = HttpsCertificatePrivateKeyPathTextBox.Text.Trim();
                    m_application.HttpsTrustListStorePath = HttpsTrustListStorePathTextBox.Text.Trim();
                    m_application.HttpsIssuerListStorePath = HttpsIssuerListStorePathTextBox.Text.Trim();
                    break;
                }

                case RegistrationType.ServerPush:
                {
                    m_application.ConfigurationFile = null;
                    m_application.CertificateStorePath = null;
                    m_application.CertificateSubjectName = null;
                    m_application.CertificatePublicKeyPath = null;
                    m_application.CertificatePrivateKeyPath = null;
                    m_application.TrustListStorePath = null;
                    m_application.IssuerListStorePath = null;
                    m_application.HttpsTrustListStorePath = null;
                    m_application.HttpsIssuerListStorePath = null;
                    break;
                }
            }
        }

        private void DataToControl()
        {
            ApplicationIdTextBox.Text = null;
            ApplicationUriTextBox.Text = (m_application != null) ? m_application.ApplicationUri : null;

            try
            {
                m_promptOnRegistrationTypeChange = false;
                RegistrationTypeComboBox.SelectedIndex = (int)m_application.RegistrationType;
            }
            finally
            {
                // m_promptOnRegistrationTypeChange = true;
            }

            ApplicationNameTextBox.Text = m_application.ApplicationName;
            ProductUriTextBox.Text = m_application.ProductUri;

            SetDiscoveryUrls(m_application.DiscoveryUrl);
            SetServerCapabilities(m_application.ServerCapability);

            switch (m_application.RegistrationType)
            {
                case RegistrationType.ClientPull:
                case RegistrationType.ServerPull:
                {
                    ConfigurationFileTextBox.Text = AddSpecialFolders(m_application.ConfigurationFile);
                    CertificateStorePathTextBox.Text = AddSpecialFolders(m_application.CertificateStorePath);
                    CertificateSubjectNameTextBox.Text = m_application.CertificateSubjectName;
                    CertificatePublicKeyPathTextBox.Text = AddSpecialFolders(m_application.CertificatePublicKeyPath);
                    CertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(m_application.CertificatePrivateKeyPath);
                    TrustListStorePathTextBox.Text = AddSpecialFolders(m_application.TrustListStorePath);
                    IssuerListStorePathTextBox.Text = AddSpecialFolders(m_application.IssuerListStorePath);
                    HttpsCertificatePublicKeyPathTextBox.Text = AddSpecialFolders(m_application.HttpsCertificatePublicKeyPath);
                    HttpsCertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(m_application.HttpsCertificatePrivateKeyPath);
                    HttpsTrustListStorePathTextBox.Text = AddSpecialFolders(m_application.HttpsTrustListStorePath);
                    HttpsIssuerListStorePathTextBox.Text = AddSpecialFolders(m_application.HttpsIssuerListStorePath);
                    break;
                }

                case RegistrationType.ServerPush:
                {
                    ConfigurationFileTextBox.Text = null;
                    CertificateStorePathTextBox.Text = null;
                    CertificateSubjectNameTextBox.Text = null;
                    CertificatePublicKeyPathTextBox.Text = null;
                    CertificatePrivateKeyPathTextBox.Text = null;
                    TrustListStorePathTextBox.Text = null;
                    IssuerListStorePathTextBox.Text = null;
                    HttpsCertificatePublicKeyPathTextBox.Text = null;
                    HttpsCertificatePrivateKeyPathTextBox.Text = null;
                    HttpsTrustListStorePathTextBox.Text = null;
                    HttpsIssuerListStorePathTextBox.Text = null;
                    break;
                }
            }
        }

        private string AddSpecialFolders(string filePath)
        {
            if (filePath == null)
            {
                return filePath;
            }

            string prefix = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "%" + Environment.SpecialFolder.ProgramFiles.ToString() + "%" + filePath.Substring(prefix.Length);
            }

            prefix = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "%" + Environment.SpecialFolder.CommonApplicationData.ToString() + "%" + filePath.Substring(prefix.Length);
            }

            prefix = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);

            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "%" + Environment.SpecialFolder.CommonProgramFiles.ToString() + "%" + filePath.Substring(prefix.Length);
            }

            prefix = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "%" + Environment.SpecialFolder.LocalApplicationData.ToString() + "%" + filePath.Substring(prefix.Length);
            }

            prefix = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "%" + Environment.SpecialFolder.ApplicationData.ToString() + "%" + filePath.Substring(prefix.Length);
            }

            prefix = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "%" + Environment.SpecialFolder.MyDocuments.ToString() + "%" + filePath.Substring(prefix.Length);
            }

            return filePath;
        }

        private string RemoveSpecialFolders(string filePath)
        {
            if (filePath == null)
            {
                return filePath;
            }

            return Utils.GetAbsoluteFilePath(filePath, true, false, false);
        }

        private void SetDiscoveryUrls(IList<string> discoveryUrls)
        {
            StringBuilder buffer = new StringBuilder();

            if (discoveryUrls != null)
            {
                foreach (var discoveryUrl in discoveryUrls)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(discoveryUrl);
                }
            }

            DiscoveryUrlsTextBox.Text = buffer.ToString();
            DiscoveryUrlsTextBox.Tag = discoveryUrls;
        }

        private void SetServerCapabilities(IList<string> capabilities)
        {
            StringBuilder buffer = new StringBuilder();

            if (capabilities != null)
            {
                foreach (var capability in capabilities)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(capability);
                }
            }

            ServerCapabilitiesTextBox.Text = buffer.ToString();
            ServerCapabilitiesTextBox.Tag = capabilities;
        }

        private string ReplaceLocalhost(string value)
        {
            if (value == null)
            {
                return null;
            }

            return value.Replace("localhost", System.Net.Dns.GetHostName());
        }

        private string HostnameToLocalhost(string value)
        {
            if (value == null)
            {
                return null;
            }

            return value.Replace(System.Net.Dns.GetHostName(), "localhost");
        }

        private void ClearFields()
        {
            ApplicationIdTextBox.Text = null;
            ApplicationIdTextBox.Tag = null;
            ApplicationNameTextBox.Text = null;
            ApplicationUriTextBox.Text = null;
            ProductUriTextBox.Text = null;
            DiscoveryUrlsTextBox.Text = null;
            DiscoveryUrlsTextBox.Tag = null;
            ServerCapabilitiesTextBox.Text = null;
            ServerCapabilitiesTextBox.Tag = null;
            CertificateStorePathTextBox.Text = null;
            CertificateSubjectNameTextBox.Text = null;
            CertificatePublicKeyPathTextBox.Text = null;
            CertificatePrivateKeyPathTextBox.Text = null;
            TrustListStorePathTextBox.Text = null;
            IssuerListStorePathTextBox.Text = null;
            HttpsCertificatePublicKeyPathTextBox.Text = null;
            HttpsCertificatePrivateKeyPathTextBox.Text = null;
            HttpsTrustListStorePathTextBox.Text = null;
            HttpsIssuerListStorePathTextBox.Text = null;

            UnregisterApplicationButton.Enabled = false;
        }

        private void RaiseRegisteredApplicationChangedEvent(RegisteredApplication application)
        {
            if (RegisteredApplicationChanged != null)
            {
                try
                {
                    RegisteredApplicationChanged(this, new RegisteredApplicationChangedEventArgs(application));
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error raising RegisteredApplicationChanged event.");
                }
            }
        }

        private void SetRegistrationTypeNoTrigger(RegistrationType registrationType)
        {
            try
            {
                m_promptOnRegistrationTypeChange = false;
                RegistrationTypeComboBox.SelectedIndex = (int)registrationType;
            }
            finally
            {
                // m_promptOnRegistrationTypeChange = true;
            }
        }

        private void InitializePullConfiguration(string configurationFilePath)
        {
            string path = Utils.GetAbsoluteFilePath(configurationFilePath, true, true, false);

            ClearFields();

            try
            {
                var configuration = new Opc.Ua.Security.SecurityConfigurationManager().ReadConfiguration(path);

                if (configuration.ApplicationType == Security.ApplicationType.Client_1)
                {
                    SetRegistrationTypeNoTrigger(RegistrationType.ClientPull);
                }
                else
                {
                    SetRegistrationTypeNoTrigger(RegistrationType.ServerPull);
                }

                ApplicationUriTextBox.Text = ReplaceLocalhost(configuration.ApplicationUri);
                ReadRegistration(true);

                ApplicationNameTextBox.Text = configuration.ApplicationName;
                ProductUriTextBox.Text = configuration.ProductName;
                
                if (configuration.ApplicationType != Security.ApplicationType.Client_1)
                {
                    SetDiscoveryUrls(configuration.BaseAddresses);
                    SetServerCapabilities(new string[] { ServerCapability.LiveData });
                }

                if (configuration.ApplicationCertificate != null)
                {
                    CertificateStorePathTextBox.Text = configuration.ApplicationCertificate.StorePath;
                    CertificateSubjectNameTextBox.Text = configuration.ApplicationCertificate.SubjectName;
                }

                if (configuration.TrustedCertificateStore != null)
                {
                    TrustListStorePathTextBox.Text = configuration.TrustedCertificateStore.StorePath;
                }

                if (configuration.IssuerCertificateStore != null)
                {
                    IssuerListStorePathTextBox.Text = configuration.IssuerCertificateStore.StorePath;
                }
            }
            catch (Exception)
            {
                // ignore.
            }
        }

        private void InitializePushConfiguration()
        {
            if (SelectServer != null)
            {
                try
                {
                    SelectServer(this, new SelectServerEventArgs(m_application));
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error raising SelectServer event.");
                }
            }
        }

        private void ConfigurationFileButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string configurationFile = RemoveSpecialFolders(ConfigurationFileTextBox.Text.Trim());

                if (!String.IsNullOrEmpty(configurationFile))
                {
                    directory = new FileInfo(configurationFile).Directory;
                }

                OpenFileDialog dialog = new OpenFileDialog();

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".xml";
                dialog.Filter = "Configuration Files (*.xml)|*.xml|Certificate Files (*.der)|*.der|All Files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.ValidateNames = true;
                dialog.Title = "Open Application Configuration File";
                dialog.FileName = (configurationFile != null) ? new FileInfo(configurationFile).Name : "";
                dialog.InitialDirectory = directory.FullName;

                if (dialog.ShowDialog(Parent) != DialogResult.OK)
                {
                    return;
                }

                m_lastDirPath = new FileInfo(dialog.FileName).Directory.FullName;

                if (dialog.FileName.EndsWith(".der", StringComparison.OrdinalIgnoreCase))
                {
                    ConfigurationFileTextBox.Text = null;
                    SetCertificatePublicKey(dialog.FileName);
                }
                else
                {
                    ConfigurationFileTextBox.Text = AddSpecialFolders(dialog.FileName);
                    InitializePullConfiguration(ConfigurationFileTextBox.Text);
                }

                ControlToData();
                RaiseRegisteredApplicationChangedEvent(m_application);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void ServerCapabilitiesButton_Click(object sender, EventArgs e)
        {
            try
            {
                var capabilities = new ServerCapabilitiesDialog().ShowDialog(Parent, ServerCapabilitiesTextBox.Tag as IList<string>);

                if (capabilities != null)
                {
                    SetServerCapabilities(capabilities);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void DiscoveryUrlsButton_Click(object sender, EventArgs e)
        {
            try
            {
                var discoveryUrls = new DiscoveryUrlsDialog().ShowDialog(Parent, DiscoveryUrlsTextBox.Tag as IList<string>);

                if (discoveryUrls != null)
                {
                    StringBuilder buffer = new StringBuilder();

                    foreach (var discoveryUrl in discoveryUrls)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Append(", ");
                        }

                        buffer.Append(discoveryUrl);
                    }

                    DiscoveryUrlsTextBox.Text = buffer.ToString();
                    DiscoveryUrlsTextBox.Tag = discoveryUrls;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void CertificateStorePathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string storePath = RemoveSpecialFolders(CertificateStorePathTextBox.Text.Trim());

                if (!String.IsNullOrEmpty(storePath))
                {
                    directory = new DirectoryInfo(storePath);
                }

                Cannoli.Services.FolderBrowserService dialog = new Cannoli.Services.FolderBrowserService();

                dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                dialog.SelectedPath = directory.FullName;
                dialog.ShowNewFolderButton = true;
                dialog.Description = "Select Application Certificate Directory Store";
                dialog.UseDescriptionForTitle = true;

                var result = dialog.ShowDialog(null, ParentForm);

                if (result == null || !result.Value)
                {
                    return;
                }

                directory = new DirectoryInfo(dialog.SelectedPath);

                if (directory.Name == "certs")
                {
                    directory = directory.Parent;
                }

                m_lastDirPath = directory.FullName;

                CertificateStorePathTextBox.Text = AddSpecialFolders(directory.FullName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void SetCertificatePublicKey(string path)
        {
            CertificatePublicKeyPathTextBox.Text = AddSpecialFolders(path);

            X509Certificate2 certificate = new X509Certificate2(RemoveSpecialFolders(CertificatePublicKeyPathTextBox.Text));

            try
            {
                if (String.IsNullOrWhiteSpace(ApplicationNameTextBox.Text))
                {
                    foreach (string field in Utils.ParseDistinguishedName(certificate.Subject))
                    {
                        if (field.StartsWith("CN="))
                        {
                            ApplicationNameTextBox.Text = field.Substring(3);
                            break;
                        }
                    }
                }

                if (String.IsNullOrWhiteSpace(ApplicationUriTextBox.Text))
                {
                    ApplicationUriTextBox.Text = Utils.GetApplicationUriFromCertificate(certificate);
                }

                if (String.IsNullOrWhiteSpace(DiscoveryUrlsTextBox.Text) && RegistrationTypeComboBox.SelectedIndex != ClientPullManagement)
                {
                    var domains = Utils.GetDomainsFromCertficate(certificate);

                    if (domains != null)
                    {
                        List<string> urls = new List<string>();

                        foreach (string domain in domains)
                        {
                            urls.Add("opc.tcp://" + domain + ":<insert port here>");
                        }

                        SetDiscoveryUrls(urls);
                    }

                    if (String.IsNullOrWhiteSpace(DiscoveryUrlsTextBox.Text))
                    {
                        SetDiscoveryUrls(new string[] { "opc.tcp://localhost:<insert port here>" });
                    }
                }
            }
            catch (Exception)
            {
                MessageBox.Show(
                    Parent,
                    "The certificate does not appear to be a valid DER file.",
                    Parent.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void CertificatePublicKeyPathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string path = RemoveSpecialFolders(CertificatePublicKeyPathTextBox.Text.Trim());

                if (!String.IsNullOrEmpty(path))
                {
                    if (Directory.Exists(path))
                    {
                        directory = new DirectoryInfo(path);
                    }
                    else
                    {
                        directory = new FileInfo(path).Directory;
                    }
                }

                OpenFileDialog dialog = new OpenFileDialog();

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".der";
                dialog.Filter = "DER Files (*.der)|*.der|CER Files (*.cer)|*.cer|All Files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.ValidateNames = true;
                dialog.Title = "Select Certificate File";
                dialog.FileName = (path != null) ? new FileInfo(path).Name : "";
                dialog.InitialDirectory = directory.FullName;

                if (dialog.ShowDialog(Parent) != DialogResult.OK)
                {
                    return;
                }

                m_lastDirPath = new FileInfo(dialog.FileName).Directory.FullName;

                CertificatePublicKeyPathTextBox.Text = AddSpecialFolders(dialog.FileName);
                
                X509Certificate2 certificate = new X509Certificate2(RemoveSpecialFolders(CertificatePublicKeyPathTextBox.Text));

                try
                {
                    if (String.IsNullOrWhiteSpace(ApplicationNameTextBox.Text))
                    {
                        foreach (string field in Utils.ParseDistinguishedName(certificate.Subject))
                        {
                            if (field.StartsWith("CN="))
                            {
                                ApplicationNameTextBox.Text = field.Substring(3);
                                break;
                            }
                        }
                    }

                    if (String.IsNullOrWhiteSpace(ApplicationUriTextBox.Text))
                    {
                        ApplicationUriTextBox.Text = Utils.GetApplicationUriFromCertificate(certificate);
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        Parent,
                        "The certificate does not appear to be a valid DER file.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void CertificatePrivateKeyPathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string path = RemoveSpecialFolders(CertificatePrivateKeyPathTextBox.Text.Trim());

                if (!String.IsNullOrEmpty(path))
                {
                    if (Directory.Exists(path))
                    {
                        directory = new DirectoryInfo(path);
                    }
                    else
                    {
                        directory = new FileInfo(path).Directory;
                    }
                }

                OpenFileDialog dialog = new OpenFileDialog();

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".pfx";
                dialog.Filter = "PFX Files (*.pfx)|*.pfx|PEM Files (*.pem)|*.pem|All Files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.ValidateNames = true;
                dialog.Title = "Select Private Key File";
                dialog.FileName = (path != null) ? new FileInfo(path).Name : "";
                dialog.InitialDirectory = directory.FullName;

                if (dialog.ShowDialog(Parent) != DialogResult.OK)
                {
                    return;
                }

                m_lastDirPath = new FileInfo(dialog.FileName).Directory.FullName;

                CertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(dialog.FileName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void HttpsCertificatePublicKeyPathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string path = RemoveSpecialFolders(HttpsCertificatePublicKeyPathTextBox.Text.Trim());

                if (!String.IsNullOrEmpty(path))
                {
                    if (Directory.Exists(path))
                    {
                        directory = new DirectoryInfo(path);
                    }
                    else
                    {
                        directory = new FileInfo(path).Directory;
                    }
                }

                OpenFileDialog dialog = new OpenFileDialog();

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".der";
                dialog.Filter = "DER Files (*.der)|*.der|CER Files (*.cer)|*.cer|All Files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.ValidateNames = true;
                dialog.Title = "Select Certificate File";
                dialog.FileName = (path != null) ? new FileInfo(path).Name : "";
                dialog.InitialDirectory = directory.FullName;

                if (dialog.ShowDialog(Parent) != DialogResult.OK)
                {
                    return;
                }

                m_lastDirPath = new FileInfo(dialog.FileName).Directory.FullName;

                CertificatePublicKeyPathTextBox.Text = AddSpecialFolders(dialog.FileName);

                X509Certificate2 certificate = new X509Certificate2(RemoveSpecialFolders(HttpsCertificatePublicKeyPathTextBox.Text));

                try
                {
                    foreach (string field in Utils.ParseDistinguishedName(certificate.Subject))
                    {
                        if (field.StartsWith("CN="))
                        {
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        Parent,
                        "The HTTPS certificate does not appear to be a valid DER file.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void HttpsCertificatePrivateKeyPathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string path = RemoveSpecialFolders(HttpsCertificatePrivateKeyPathTextBox.Text.Trim());

                if (!String.IsNullOrEmpty(path))
                {
                    if (Directory.Exists(path))
                    {
                        directory = new DirectoryInfo(path);
                    }
                    else
                    {
                        directory = new FileInfo(path).Directory;
                    }
                }

                OpenFileDialog dialog = new OpenFileDialog();

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".pfx";
                dialog.Filter = "PFX Files (*.pfx)|*.pfx|PEM Files (*.pem)|*.pem|All Files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.ValidateNames = true;
                dialog.Title = "Select Private Key File";
                dialog.FileName = (path != null) ? new FileInfo(path).Name : "";
                dialog.InitialDirectory = directory.FullName;

                if (dialog.ShowDialog(Parent) != DialogResult.OK)
                {
                    return;
                }

                m_lastDirPath = new FileInfo(dialog.FileName).Directory.FullName;

                HttpsCertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(dialog.FileName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void TrustListStorePathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string storePath = Utils.GetAbsoluteDirectoryPath(TrustListStorePathTextBox.Text.Trim(), true, false, false);

                if (!String.IsNullOrEmpty(storePath))
                {
                    while (storePath.EndsWith("\\"))
                    {
                        storePath = storePath.Substring(0, storePath.Length - 1);
                    }

                    directory = new DirectoryInfo(storePath);
                }


                Cannoli.Services.FolderBrowserService dialog = new Cannoli.Services.FolderBrowserService();

                dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                dialog.SelectedPath = directory.FullName;
                dialog.ShowNewFolderButton = true;
                dialog.Description = "Select Application Trust List";
                dialog.UseDescriptionForTitle = true;

                var result = dialog.ShowDialog(null, ParentForm);

                if (result == null || !result.Value)
                {
                    return;
                }

                directory = new DirectoryInfo(dialog.SelectedPath);

                if (directory.Name == "certs")
                {
                    directory = directory.Parent;
                }

                m_lastDirPath = directory.FullName;

                TrustListStorePathTextBox.Text = AddSpecialFolders(directory.FullName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void IssuerListStorePathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string storePath = Utils.GetAbsoluteDirectoryPath(IssuerListStorePathTextBox.Text.Trim(), true, false, false);

                if (!String.IsNullOrEmpty(storePath))
                {
                    directory = new DirectoryInfo(storePath);
                }

                Cannoli.Services.FolderBrowserService dialog = new Cannoli.Services.FolderBrowserService();

                dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                dialog.SelectedPath = directory.FullName;
                dialog.ShowNewFolderButton = true;
                dialog.Description = "Select Issuers List Directory Store";
                dialog.UseDescriptionForTitle = true;

                var result = dialog.ShowDialog(null, ParentForm);

                if (result == null || !result.Value)
                {
                    return;
                }

                directory = new DirectoryInfo(dialog.SelectedPath);

                if (directory.Name == "certs")
                {
                    directory = directory.Parent;
                }

                m_lastDirPath = directory.FullName;

                IssuerListStorePathTextBox.Text = AddSpecialFolders(directory.FullName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void HttpsTrustListStorePathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string storePath = Utils.GetAbsoluteDirectoryPath(HttpsTrustListStorePathTextBox.Text.Trim(), true, false, false);

                if (!String.IsNullOrEmpty(storePath))
                {
                    directory = new DirectoryInfo(storePath);
                }

                Cannoli.Services.FolderBrowserService dialog = new Cannoli.Services.FolderBrowserService();

                dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                dialog.SelectedPath = directory.FullName;
                dialog.ShowNewFolderButton = true;
                dialog.Description = "Select HTTPS Trust List Directory Store";
                dialog.UseDescriptionForTitle = true;

                var result = dialog.ShowDialog(null, ParentForm);

                if (result == null || !result.Value)
                {
                    return;
                }

                directory = new DirectoryInfo(dialog.SelectedPath);

                if (directory.Name == "certs")
                {
                    directory = directory.Parent;
                }

                m_lastDirPath = directory.FullName;

                HttpsTrustListStorePathTextBox.Text = AddSpecialFolders(directory.FullName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void HttpsIssuerListStorePathButton_Click(object sender, EventArgs e)
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(m_lastDirPath);

                string storePath = Utils.GetAbsoluteDirectoryPath(HttpsIssuerListStorePathTextBox.Text.Trim(), true, false, false);

                if (!String.IsNullOrEmpty(storePath))
                {
                    directory = new DirectoryInfo(storePath);
                }

                Cannoli.Services.FolderBrowserService dialog = new Cannoli.Services.FolderBrowserService();

                dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                dialog.SelectedPath = directory.FullName;
                dialog.ShowNewFolderButton = true;
                dialog.Description = "Select HTTPS Issuers List Directory Store";
                dialog.UseDescriptionForTitle = true;

                var result = dialog.ShowDialog(null, ParentForm);

                if (result == null || !result.Value)
                {
                    return;
                }

                directory = new DirectoryInfo(dialog.SelectedPath);

                if (directory.Name == "certs")
                {
                    directory = directory.Parent;
                }

                m_lastDirPath = directory.FullName;

                HttpsIssuerListStorePathTextBox.Text = AddSpecialFolders(directory.FullName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void RegisterApplicationButton_Click(object sender, EventArgs e)
        {
            try
            {   string applicationName = ApplicationNameTextBox.Text.Trim();

                if (String.IsNullOrEmpty(applicationName))
                {
                    throw new ArgumentException("The Application Name must specified.", "ApplicationName");
                }

                applicationName = ReplaceLocalhost(applicationName);

                string applicationUri = ApplicationUriTextBox.Text.Trim();

                if (String.IsNullOrEmpty(applicationUri))
                {
                    throw new ArgumentException("The Application URI must specified.", "ApplicationUri");
                }

                if (!Uri.IsWellFormedUriString(applicationUri, UriKind.Absolute))
                {
                    throw new ArgumentException(applicationUri + "is not a valid URI.", "ApplicationUri");
                }

                applicationUri = ReplaceLocalhost(applicationUri);

                string productUri = ProductUriTextBox.Text.Trim();

                if (String.IsNullOrEmpty(productUri))
                {
                    throw new ArgumentException("The Product URI must specified.", "ProductUri");
                }

                if (!Uri.IsWellFormedUriString(productUri, UriKind.Absolute))
                {
                    throw new ArgumentException(productUri + "is not a valid URI.", "ProductUri");
                }

                productUri = ReplaceLocalhost(productUri);

                if (RegistrationTypeComboBox.SelectedIndex == ClientPullManagement)
                {
                    DiscoveryUrlsTextBox.Tag = null;
                    DiscoveryUrlsTextBox.Text = null;
                    ServerCapabilitiesTextBox.Tag = null;
                    ServerCapabilitiesTextBox.Text = null;
                }

                IList<string> discoveryUrls = DiscoveryUrlsTextBox.Tag as IList<string>;

                if (RegistrationTypeComboBox.SelectedIndex != ClientPullManagement)
                {
                    if (discoveryUrls == null || discoveryUrls.Count == 0)
                    {
                        throw new ArgumentException("At least one Discovery URL must specified.", "DiscoveryUrls");
                    }
                }

                IList<string> capabilities = ServerCapabilitiesTextBox.Tag as IList<string>;

                if (RegistrationTypeComboBox.SelectedIndex != ClientPullManagement)
                {
                    if (capabilities == null || capabilities.Count == 0)
                    {
                        throw new ArgumentException("At least one Server Capability must specified.", "ServerCapabilities");
                    }
                }

                ApplicationRecordDataType recordToReplace = ApplicationIdTextBox.Tag as ApplicationRecordDataType;

                var records = m_gds.FindApplication(applicationUri);

                if (records != null)
                {
                    if (records.Length > 1)
                    {
                        recordToReplace = new ViewApplicationRecordsDialog(m_gds).ShowDialog(Parent, records, (recordToReplace != null) ? recordToReplace.ApplicationId : null);
                    }
                    else if (records.Length > 0)
                    {
                        recordToReplace = records[0];
                    }
                }

                if (recordToReplace == null)
                {
                    recordToReplace = new ApplicationRecordDataType();
                }

                StringCollection urls = new StringCollection();

                if (discoveryUrls != null)
                {
                    foreach (var discoveryUrl in discoveryUrls)
                    {
                        urls.Add(ReplaceLocalhost(discoveryUrl));
                    }
                }

                recordToReplace.ApplicationUri = applicationUri;
                recordToReplace.ApplicationType = (RegistrationTypeComboBox.SelectedIndex != ClientPullManagement)?ApplicationType.Server:ApplicationType.Client;
                recordToReplace.ApplicationNames = new LocalizedText[] { applicationName };
                recordToReplace.ProductUri = productUri;
                recordToReplace.DiscoveryUrls = urls;
                recordToReplace.ServerCapabilities = (capabilities != null)?new StringCollection(capabilities):new StringCollection();

                var applicationId = m_gds.RegisterApplication(recordToReplace);

                recordToReplace.ApplicationId = applicationId;

                ApplicationIdTextBox.Text = Utils.Format("{0}", applicationId);
                ApplicationIdTextBox.Tag = recordToReplace;

                ApplicationNameTextBox.Text = applicationName;
                ApplicationUriTextBox.Text = applicationUri;
                ProductUriTextBox.Text = productUri;
                SetDiscoveryUrls(urls);

                CertificatePublicKeyPathTextBox.Text = AddSpecialFolders(CertificatePublicKeyPathTextBox.Text);
                CertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(CertificatePrivateKeyPathTextBox.Text);
                CertificateStorePathTextBox.Text = AddSpecialFolders(CertificateStorePathTextBox.Text);
                TrustListStorePathTextBox.Text = AddSpecialFolders(TrustListStorePathTextBox.Text);
                IssuerListStorePathTextBox.Text = AddSpecialFolders(IssuerListStorePathTextBox.Text);
                HttpsCertificatePublicKeyPathTextBox.Text = AddSpecialFolders(HttpsCertificatePublicKeyPathTextBox.Text);
                HttpsCertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(HttpsCertificatePrivateKeyPathTextBox.Text);
                HttpsTrustListStorePathTextBox.Text = AddSpecialFolders(HttpsTrustListStorePathTextBox.Text);
                HttpsIssuerListStorePathTextBox.Text = AddSpecialFolders(HttpsIssuerListStorePathTextBox.Text);

                UnregisterApplicationButton.Enabled = true;

                ControlToData();

                if (RegisteredApplicationChanged != null)
                {
                    RegisteredApplicationChanged(this, new RegisteredApplicationChangedEventArgs(m_application));
                }

                MessageBox.Show(
                    Parent,
                    "The application was successfully registered.",
                    Parent.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void ReadRegistration(bool silent)
        {
            string applicationUri = ApplicationUriTextBox.Text.Trim();

            if (String.IsNullOrEmpty(applicationUri))
            {
                if (!silent)
                {
                    throw new ArgumentException("The Application URI must specified.", "ApplicationUri");
                }

                return;
            }

            if (!Uri.IsWellFormedUriString(applicationUri, UriKind.Absolute))
            {
                if (!silent)
                {
                    throw new ArgumentException(applicationUri + "is not a valid URI.", "ApplicationUri");
                }

                return;
            }

            applicationUri = ReplaceLocalhost(applicationUri);

            ApplicationRecordDataType existingRecord = null;

            try
            {
                var records = m_gds.FindApplication(applicationUri);

                if (records != null)
                {
                    if (records.Length > 1)
                    {
                        existingRecord = new ViewApplicationRecordsDialog(m_gds).ShowDialog(Parent, records, null);
                    }
                    else if (records.Length > 0)
                    {
                        existingRecord = records[0];
                    }
                }
            }
            catch (Exception)
            {
                if (!silent)
                {
                    throw;
                }

                return;
            }

            if (existingRecord == null)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        this,
                        "No matching record found in GDS.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Exclamation);
                }

                return;
            }

            ApplicationIdTextBox.Text = Utils.Format("{0}", existingRecord.ApplicationId);
            ApplicationIdTextBox.Tag = existingRecord;
            ApplicationUriTextBox.Text = existingRecord.ApplicationUri;
            ApplicationNameTextBox.Text = (existingRecord.ApplicationNames != null && existingRecord.ApplicationNames.Count > 0) ? Utils.Format("{0}", existingRecord.ApplicationNames[0]) : "";
            ProductUriTextBox.Text = existingRecord.ProductUri;
            SetDiscoveryUrls(existingRecord.DiscoveryUrls);
            SetServerCapabilities(existingRecord.ServerCapabilities);

            UnregisterApplicationButton.Enabled = true;

            ControlToData();
        }

        private void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            try
            {
                ConfigurationFileTextBox.Text = AddSpecialFolders(ConfigurationFileTextBox.Text);
                CertificateStorePathTextBox.Text = AddSpecialFolders(CertificateStorePathTextBox.Text);
                CertificatePublicKeyPathTextBox.Text = AddSpecialFolders(CertificatePublicKeyPathTextBox.Text);
                CertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(CertificatePrivateKeyPathTextBox.Text);
                TrustListStorePathTextBox.Text = AddSpecialFolders(TrustListStorePathTextBox.Text);
                IssuerListStorePathTextBox.Text = AddSpecialFolders(IssuerListStorePathTextBox.Text);
                HttpsCertificatePublicKeyPathTextBox.Text = AddSpecialFolders(HttpsCertificatePublicKeyPathTextBox.Text);
                HttpsCertificatePrivateKeyPathTextBox.Text = AddSpecialFolders(HttpsCertificatePrivateKeyPathTextBox.Text);
                HttpsTrustListStorePathTextBox.Text = AddSpecialFolders(HttpsTrustListStorePathTextBox.Text);
                HttpsIssuerListStorePathTextBox.Text = AddSpecialFolders(HttpsIssuerListStorePathTextBox.Text);

                ControlToData();

                RaiseRegisteredApplicationChangedEvent(m_application);

                ApplyChangesButton.Enabled = false;
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void UnregisterApplicationButton_Click(object sender, EventArgs e)
        {
            try
            {
                ApplicationRecordDataType record = ApplicationIdTextBox.Tag as ApplicationRecordDataType;

                if (record != null)
                {
                    m_gds.UnregisterApplication(record.ApplicationId);

                    ApplicationIdTextBox.Text = null;
                    ApplicationIdTextBox.Tag = null;

                    MessageBox.Show(
                       Parent,
                       "The application has been unregistered.",
                       Parent.Text,
                       MessageBoxButtons.OK,
                       MessageBoxIcon.Information);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void RegistrationTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                DiscoveryUrlsLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                DiscoveryUrlsTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                DiscoveryUrlsButton.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                ServerCapabilitiesButton.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                ServerCapabilitiesLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                ServerCapabilitiesTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                ConfigurationFileLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                ConfigurationFileTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                ConfigurationFileButton.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificateStorePathLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificateStorePathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificateStorePathButton.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificateSubjectNameLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificateSubjectNameTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificatePublicKeyPathLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificatePublicKeyPathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificatePublicKeyPathButton.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificatePrivateKeyPathLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificatePrivateKeyPathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                CertificatePrivateKeyPathButton.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                TrustListStorePathLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                TrustListStorePathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                TrustListStorePathButton.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                IssuerListStorePathLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                IssuerListStorePathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                IssuerListStorePathButton.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;
                HttpsCertificatePublicKeyPathLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                HttpsCertificatePublicKeyPathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                HttpsCertificatePublicKeyPathButton.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                HttpsCertificatePrivateKeyPathLabel.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                HttpsCertificatePrivateKeyPathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                HttpsCertificatePrivateKeyPathButton.Visible = RegistrationTypeComboBox.SelectedIndex != ClientPullManagement;
                HttpsTrustListStorePathLabel.Visible = RegistrationTypeComboBox.SelectedIndex == ClientPullManagement;
                HttpsTrustListStorePathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex == ClientPullManagement;
                HttpsTrustListStorePathButton.Visible = RegistrationTypeComboBox.SelectedIndex == ClientPullManagement;
                HttpsIssuerListStorePathLabel.Visible = RegistrationTypeComboBox.SelectedIndex == ClientPullManagement;
                HttpsIssuerListStorePathTextBox.Visible = RegistrationTypeComboBox.SelectedIndex == ClientPullManagement;
                HttpsIssuerListStorePathButton.Visible = RegistrationTypeComboBox.SelectedIndex == ClientPullManagement;
                OpenConfigurationButton.Visible = RegistrationTypeComboBox.SelectedIndex != ServerPushManagement;

                if (m_promptOnRegistrationTypeChange)
                {
                    if (RegistrationTypeComboBox.SelectedIndex != ServerPushManagement)
                    {
                        ConfigurationFileButton_Click(this, null);
                    }
                    else
                    {
                        InitializePushConfiguration();
                    }
                }

                PickServerButton.Visible = RegistrationTypeComboBox.SelectedIndex == ServerPushManagement;
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            try
            {
                ControlToData();

                string path = Utils.GetAbsoluteDirectoryPath(m_lastSavePath, true, false, true);
                DirectoryInfo directory = new DirectoryInfo(path);

                string name = ApplicationUriTextBox.Text.Trim();
                name = HostnameToLocalhost(name);

                if (name != null)
                {
                    StringBuilder buffer = new StringBuilder();

                    foreach (char ch in name)
                    {
                        if (Char.IsLetterOrDigit(ch) || ".-_".Contains(ch))
                        {
                            buffer.Append(ch);
                            continue;
                        }
                    }

                    name = buffer.ToString();
                }

                SaveFileDialog dialog = new SaveFileDialog();

                dialog.OverwritePrompt = true;
                dialog.CheckFileExists = false;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".xml";
                dialog.Filter = "Registration Files (*.xml)|*.xml|All Files (*.*)|*.*";
                dialog.ValidateNames = true;
                dialog.Title = "Save Registration File";
                dialog.FileName = name;
                dialog.InitialDirectory = directory.FullName;

                if (dialog.ShowDialog(Parent) != DialogResult.OK)
                {
                    return;
                }

                m_lastSavePath = new FileInfo(dialog.FileName).Directory.FullName;

                // save localhost instead of hostname so saved files will be portable.
                m_application.ApplicationName = HostnameToLocalhost(m_application.ApplicationName);
                m_application.ApplicationUri = HostnameToLocalhost(m_application.ApplicationUri);
                m_application.ProductUri = HostnameToLocalhost(m_application.ProductUri);

                if (m_application.DiscoveryUrl != null)
                {
                    StringCollection urls = new StringCollection();

                    foreach (var discoveryUrl in m_application.DiscoveryUrl)
                    {
                        urls.Add(HostnameToLocalhost(discoveryUrl));
                    }

                    m_application.DiscoveryUrl = urls.ToArray();
                }

                try
                {
                    using (Stream ostrm = File.Open(dialog.FileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(RegisteredApplication));
                        serializer.Serialize(ostrm, m_application);
                    }
                }
                finally
                {
                    m_application.ApplicationName = ReplaceLocalhost(m_application.ApplicationName);
                    m_application.ApplicationUri = ReplaceLocalhost(m_application.ApplicationUri);
                    m_application.ProductUri = ReplaceLocalhost(m_application.ProductUri);

                    if (m_application.DiscoveryUrl != null)
                    {
                        StringCollection urls = new StringCollection();

                        foreach (var discoveryUrl in m_application.DiscoveryUrl)
                        {
                            urls.Add(ReplaceLocalhost(discoveryUrl));
                        }

                        m_application.DiscoveryUrl = urls.ToArray();
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void LoadButton_Click(object sender, EventArgs e)
        {
            try
            {
                string path = Utils.GetAbsoluteDirectoryPath(m_lastDirPath, true, false, false);

                if (path == null)
                {
                    path = Utils.GetAbsoluteDirectoryPath(m_lastDirPath, true, false, false);
                }

                if (path == null)
                {
                    path = ".";
                }

                DirectoryInfo directory = new DirectoryInfo(path);

                OpenFileDialog dialog = new OpenFileDialog();

                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".xml";
                dialog.Filter = "All Data Files (*.xml;*.config;*.der)|*.xml;*.config;*.der|All Files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.ValidateNames = true;
                dialog.Title = "Load Registration File, Application Configuration File or Certificate";
                dialog.FileName = null;
                dialog.InitialDirectory = directory.FullName;

                if (dialog.ShowDialog(Parent) != DialogResult.OK)
                {
                    return;
                }

                m_lastDirPath = new FileInfo(dialog.FileName).Directory.FullName;
                               
                if (dialog.FileName.EndsWith(".der", StringComparison.OrdinalIgnoreCase))
                {
                    ConfigurationFileTextBox.Text = null;
                    SetCertificatePublicKey(dialog.FileName);
                }
                else
                {
                    InitializePullConfiguration(dialog.FileName);
                    ConfigurationFileTextBox.Text = AddSpecialFolders(dialog.FileName);
                }

                ControlToData();
                RaiseRegisteredApplicationChangedEvent(m_application);
               
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void OpenConfigurationButton_Click(object sender, EventArgs e)
        {
            try
            {
                var pathToFile = Utils.GetAbsoluteFilePath(ConfigurationFileTextBox.Text.Trim(), true, true, false);
                System.Diagnostics.Process.Start((m_externalEditor)??@"devenv.exe", "\"" + pathToFile + "\"");
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void Button_MouseEnter(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.CornflowerBlue;
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.MidnightBlue;
        }

        private void ConfigurationFileTextBox_TextChanged(object sender, EventArgs e)
        {
            OpenConfigurationButton.Enabled = !String.IsNullOrEmpty(ConfigurationFileTextBox.Text.Trim());
        }

        private void CertificateLocation_TextChanged(object sender, EventArgs e)
        {
            if (sender == CertificateStorePathTextBox || sender == CertificateSubjectNameTextBox)
            {
                CertificatePublicKeyPathButton.Enabled = CertificatePrivateKeyPathButton.Enabled = CertificatePrivateKeyPathTextBox.Enabled = CertificatePublicKeyPathTextBox.Enabled = (String.IsNullOrEmpty(CertificateStorePathTextBox.Text) && String.IsNullOrEmpty(CertificateSubjectNameTextBox.Text));
            }

            if (sender == CertificatePrivateKeyPathTextBox || sender == CertificatePublicKeyPathTextBox)
            {
                CertificateStorePathButton.Enabled = CertificateStorePathTextBox.Enabled = CertificateSubjectNameTextBox.Enabled = (String.IsNullOrEmpty(CertificatePrivateKeyPathTextBox.Text) && String.IsNullOrEmpty(CertificatePublicKeyPathTextBox.Text));
            }

            GenericField_TextChanged(sender, e);
        }

        private void GenericField_TextChanged(object sender, EventArgs e)
        {
            ApplyChangesButton.Enabled = true;
        }

        private void ApplicationUriTextBox_TextChanged(object sender, EventArgs e)
        {
            RegisterApplicationButton.Enabled = !String.IsNullOrEmpty(ApplicationUriTextBox.Text.Trim());
            ApplyChangesButton.Enabled = true;
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            try
            {
                ClearFields();
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private void PickServerButton_Click(object sender, EventArgs e)
        {
            InitializePushConfiguration();
        }
    }

    public class RegisteredApplicationChangedEventArgs : EventArgs
    {
        public RegisteredApplicationChangedEventArgs(RegisteredApplication application)
        {
            Application = application;
        }

        public RegisteredApplication Application { get; private set; }
    }

    public class SelectServerEventArgs : EventArgs
    {
        public SelectServerEventArgs(RegisteredApplication application)
        {
            Application = application;
        }

        public RegisteredApplication Application { get; private set; }
    }
}
