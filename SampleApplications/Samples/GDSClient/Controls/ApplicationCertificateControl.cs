using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Client;
using Opc.Ua.Gds;

namespace Opc.Ua.GdsClient
{
    public partial class ApplicationCertificateControl : UserControl
    {
        public ApplicationCertificateControl()
        {
            InitializeComponent();
        }

        private GlobalDiscoveryClientConfiguration m_configuration;
        private GlobalDiscoveryServer m_gds;
        private PushConfigurationServer m_server;
        private RegisteredApplication m_application;
        private X509Certificate2 m_certificate;

        public async void Initialize(
            GlobalDiscoveryClientConfiguration configuration,
            GlobalDiscoveryServer gds,
            PushConfigurationServer server,
            RegisteredApplication application,
            bool isHttps)
        {
            m_configuration = configuration;
            m_gds = gds;
            m_server = server;
            m_application = application;
            m_certificate = null;

            CertificateRequestTimer.Enabled = false;
            RequestProgressLabel.Visible = false;
            ApplyChangesButton.Enabled = false;

            CertificateControl.ShowNothing();

            X509Certificate2 certificate = null;

            if (!isHttps)
            {
                if (server.Endpoint != null && server.Endpoint.Description.ServerCertificate != null)
                {
                    certificate = new X509Certificate2(server.Endpoint.Description.ServerCertificate);
                }
                else if (application != null)
                {
                    if (!String.IsNullOrEmpty(application.CertificatePublicKeyPath))
                    {
                        string file = Utils.GetAbsoluteFilePath(application.CertificatePublicKeyPath, true, false, false);

                        if (file != null)
                        {
                            certificate = new X509Certificate2(file);
                        }
                    }
                    else if (!String.IsNullOrEmpty(application.CertificateStorePath))
                    {
                        CertificateIdentifier id = new CertificateIdentifier();

                        id.StorePath = application.CertificateStorePath;
                        id.StoreType = CertificateStoreIdentifier.DetermineStoreType(id.StoreType);
                        id.SubjectName = application.CertificateSubjectName.Replace("localhost", System.Net.Dns.GetHostName());

                        certificate = await id.Find(true);
                    }
                }
            }
            else
            {
                if (application != null)
                {
                    if (!String.IsNullOrEmpty(application.HttpsCertificatePublicKeyPath))
                    {
                        string file = Utils.GetAbsoluteFilePath(application.HttpsCertificatePublicKeyPath, true, false, false);

                        if (file != null)
                        {
                            certificate = new X509Certificate2(file);
                        }
                    }
                    else
                    {
                        foreach (string disoveryUrl in application.DiscoveryUrl)
                        {
                            if (Uri.IsWellFormedUriString(disoveryUrl, UriKind.Absolute))
                            {
                                Uri url = new Uri(disoveryUrl);

                                CertificateIdentifier id = new CertificateIdentifier()
                                {
                                    StoreType = CertificateStoreType.X509Store,
                                    StorePath = "LocalMachine\\My",
                                    SubjectName = "CN=" + url.DnsSafeHost
                                };

                                certificate = await id.Find();
                            }
                        }
                    }
                }
            }

            if (certificate != null)
            {
                try
                {
                    CertificateControl.Tag = certificate.Thumbprint;
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        Parent,
                        "The certificate does not appear to be valid. Please check configuration settings.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    certificate = null;
                }
            }

            WarningLabel.Visible = certificate == null;

            if (certificate != null)
            {
                m_certificate = certificate;
                CertificateControl.ShowValue(null, "Application Certificate", new CertificateWrapper() { Certificate = certificate }, true);
            }
        }

        private string GetPrivateKeyFormat()
        {
            string privateKeyFormat = "PFX";

            if (m_application.RegistrationType != RegistrationType.ServerPush)
            {
                if (!String.IsNullOrEmpty(m_application.CertificatePrivateKeyPath))
                {
                    if (m_application.CertificatePrivateKeyPath.EndsWith("PEM", StringComparison.OrdinalIgnoreCase))
                    {
                        privateKeyFormat = "PEM";
                    }
                }
            }
            else
            {
                string[] privateKeyFormats = m_server.GetSupportedKeyFormats();

                if (privateKeyFormats == null || !privateKeyFormats.Contains("PFX"))
                {
                    privateKeyFormat = "PEM";
                }
            }

            return privateKeyFormat;
        }

        private string[] GetDomainNames()
        {
            List<string> domainNames = new List<string>();

            if (m_application.DiscoveryUrl != null)
            {
                foreach (var discoveryUrl in m_application.DiscoveryUrl)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        string name = new Uri(discoveryUrl).DnsSafeHost;

                        if (name == "localhost")
                        {
                            name = System.Net.Dns.GetHostName();
                        }

                        bool found = false;

                        foreach (var domainName in domainNames)
                        {
                            if (String.Compare(domainName, name, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            domainNames.Add(name);
                        }
                    }
                }
            }

            if (domainNames != null && domainNames.Count > 0)
            {
                return domainNames.ToArray();
            }

            if (m_server != null && m_server.Endpoint != null)
            {
                domainNames.Add(m_server.Endpoint.EndpointUrl.DnsSafeHost);
                return domainNames.ToArray();
            }

            if (m_certificate != null)
            {
                var names = Utils.GetDomainsFromCertficate(m_certificate);

                if (names != null && names.Count > 0)
                {
                    domainNames.AddRange(names);
                    return domainNames.ToArray();
                }

                var fields = Utils.ParseDistinguishedName(m_certificate.Subject);

                string name = null;

                foreach (var field in fields)
                {
                    if (field.StartsWith("DC="))
                    {
                        if (name != null)
                        {
                            name += ".";
                        }

                        name += field.Substring(3);
                    }
                }

                if (names != null)
                {
                    domainNames.AddRange(names);
                    return domainNames.ToArray();
                }
            }

            return new string[] { System.Net.Dns.GetHostName() };
        }

        private string GetSubjectName(string[] domainNames)
        {
            if (m_certificate == null)
            {
                return null;
            }

            if (domainNames != null && domainNames.Length > 0)
            {
                StringBuilder buffer = new StringBuilder();

                var fields = Utils.ParseDistinguishedName(m_certificate.Subject);

                foreach (var field in fields)
                {
                    if (field.StartsWith("DC="))
                    {
                        continue;
                    }

                    if (buffer.Length > 0)
                    {
                        buffer.Append("/");
                    }

                    buffer.Append(field);
                }

                if (buffer.Length > 0)
                {
                    buffer.Append("/DC=");
                }

                buffer.Append(domainNames[0]);

                return buffer.ToString();
            }

            return m_certificate.Subject;
        }

        private void RequestNewButton_Click(object sender, EventArgs e)
        {
            try
            {
                NodeId requestId = null;

                bool newPrivateKeyRequired = false;

                if (!String.IsNullOrEmpty(m_application.CertificatePrivateKeyPath))
                {
                    string path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false);
                    newPrivateKeyRequired = path == null;
                }
                else if (!String.IsNullOrEmpty(m_application.CertificateStorePath))
                {
                    CertificateIdentifier id = new CertificateIdentifier();

                    id.StoreType = CertificateStoreIdentifier.DetermineStoreType(m_application.CertificateStorePath);
                    id.StorePath = m_application.CertificateStorePath;
                    id.SubjectName = m_application.CertificateSubjectName.Replace("localhost", System.Net.Dns.GetHostName());

                    newPrivateKeyRequired = id.Find(true) == null;
                }
                else if (!String.IsNullOrEmpty(m_application.ServerUrl))
                {
                    newPrivateKeyRequired = true;
                }

                byte[] nonce = new byte[32];
                System.Security.Cryptography.RandomNumberGenerator.Create().GetBytes(nonce);
                byte[] certificateRequest = m_server.CreateCertificateRequest(null, null, null, false, nonce, m_application.ServerUrl);
                newPrivateKeyRequired = false;

                if (m_server.Endpoint != null && m_server.Endpoint.Description.ServerCertificate != null)
                {
                    m_certificate = new X509Certificate2(m_server.Endpoint.Description.ServerCertificate);
                }
  
                if (newPrivateKeyRequired || m_certificate == null)
                {
                    string privateKeyFormat = GetPrivateKeyFormat();
                    string[] domainNames = GetDomainNames();
                    string subjectName = GetSubjectName(domainNames);

                    requestId = m_gds.StartNewKeyPairRequest(
                        m_application.ApplicationId,
                        null,
                        null,
                        subjectName,
                        domainNames,
                        privateKeyFormat,
                        null);
                }
                else
                {
                    if (certificateRequest == null)
                    {
                        var certificate = m_certificate;
                        byte[] privateKey = null;
                        bool isPemKey = false;

                        if (!certificate.HasPrivateKey)
                        {
                            if (!String.IsNullOrEmpty(m_application.CertificatePrivateKeyPath))
                            {
                                string path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false);

                                if (path != null)
                                {
                                    privateKey = File.ReadAllBytes(path);
                                    isPemKey = path.EndsWith("PEM", StringComparison.OrdinalIgnoreCase);
                                }
                            }
                        }

                        certificateRequest = CertificateAuthority.CreateRequest(
                            certificate,
                            privateKey,
                            isPemKey,
                            256);
                    }

                    requestId = m_gds.StartSigningRequest(m_application.ApplicationId, null, null, certificateRequest);
                }

                m_application.CertificateRequestId = requestId.ToString();
                CertificateRequestTimer.Enabled = true;
                RequestProgressLabel.Visible = true;
                WarningLabel.Visible = false;
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
            }
        }

        private async void CertificateRequestTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                NodeId requestId = NodeId.Parse(m_application.CertificateRequestId);

                byte[] privateKey = null;
                byte[][] issuerCertificates = null;

                byte[] certificate = m_gds.FinishRequest(
                    m_application.ApplicationId,
                    requestId,
                    out privateKey,
                    out issuerCertificates);

                if (certificate == null)
                {
                    return;
                }

                CertificateRequestTimer.Enabled = false;
                RequestProgressLabel.Visible = false;

                if (m_application.RegistrationType != RegistrationType.ServerPush)
                {
                    // save public key.
                    if (!String.IsNullOrEmpty(m_application.CertificatePublicKeyPath))
                    {
                        string file = Utils.GetAbsoluteFilePath(m_application.CertificatePublicKeyPath, true, false, true);
                        File.WriteAllBytes(file, certificate);
                    }

                    // check if the private was re-used.
                    if (privateKey == null || privateKey.Length == 0)
                    {
                        if (!String.IsNullOrEmpty(m_application.CertificatePrivateKeyPath))
                        {
                            string path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, true, true);

                            if (path != null)
                            {
                                if (!m_application.CertificatePrivateKeyPath.EndsWith("PEM", StringComparison.OrdinalIgnoreCase))
                                {
                                    var x509 = new X509Certificate2(certificate);
                                    var oldPfx = new X509Certificate2(path, (string)null, X509KeyStorageFlags.Exportable);
                                    var newPfx = CertificateAuthority.Replace(x509, oldPfx);
                                    var bytes = newPfx.Export(X509ContentType.Pfx);
                                    File.WriteAllBytes(path, bytes);
                                }
                            }
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(m_application.CertificateStorePath) && !String.IsNullOrEmpty(m_application.CertificateSubjectName))
                            {
                                var x509 = new X509Certificate2(certificate);

                                var cid = new CertificateIdentifier()
                                {
                                    StorePath = m_application.CertificateStorePath,
                                    SubjectName = m_application.CertificateSubjectName.Replace("localhost", System.Net.Dns.GetHostName())
                                };

                                var oldPfx = await cid.Find(true);

                                if (oldPfx != null)
                                {
                                    var newPfx = CertificateAuthority.Replace(x509, oldPfx);

                                    using (var store = CertificateStoreIdentifier.OpenStore(m_application.CertificateStorePath))
                                    {
                                        await store.Delete(oldPfx.Thumbprint);
                                        await store.Add(newPfx);
                                    }
                                }
                            }
                        }
                    }

                    // save private key.
                    else
                    {
                        if (!String.IsNullOrEmpty(m_application.CertificatePrivateKeyPath))
                        {
                            string path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, true, true);

                            if (path != null)
                            {
                                File.WriteAllBytes(path, privateKey);
                            }
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(m_application.CertificateStorePath) && !String.IsNullOrEmpty(m_application.CertificateSubjectName))
                            {
                                var cid = new CertificateIdentifier()
                                {
                                    StorePath = m_application.CertificateStorePath,
                                    SubjectName = m_application.CertificateSubjectName
                                };

                                var oldCertificate = await cid.Find();

                                using (var store = CertificateStoreIdentifier.OpenStore(m_application.CertificateStorePath))
                                {
                                    if (oldCertificate != null)
                                    {
                                        await store.Delete(oldCertificate.Thumbprint);
                                    }

                                    var x509 = new X509Certificate2(privateKey, new System.Security.SecureString(), X509KeyStorageFlags.Exportable);
                                    x509 = CertificateFactory.Load(x509, true);
                                    await store.Add(x509);
                                }
                            }
                        }
                    }

                    // update trust list.
                    if (!String.IsNullOrEmpty(m_application.TrustListStorePath))
                    {
                        using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_application.TrustListStorePath))
                        {
                            foreach (var issuerCertificate in issuerCertificates)
                            {
                                var x509 = new X509Certificate2(issuerCertificate);

                                if (store.FindByThumbprint(x509.Thumbprint) == null)
                                {
                                    await store.Add(new X509Certificate2(issuerCertificate));
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (privateKey != null && privateKey.Length > 0)
                    {
                        var x509 = new X509Certificate2(privateKey, string.Empty, X509KeyStorageFlags.Exportable);
                        privateKey = x509.Export(X509ContentType.Pfx);
                    }

                    bool applyChanges = m_server.UpdateCertificate(null, null, certificate, GetPrivateKeyFormat(), privateKey, issuerCertificates);

                    if (applyChanges)
                    {
                        MessageBox.Show(
                            Parent,
                            "The certificate was updated, however, the apply changes command must be sent before the server will use the new certificate.",
                            Parent.Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        ApplyChangesButton.Enabled = true;
                    }
                }

                m_certificate = new X509Certificate2(certificate);
                CertificateControl.ShowValue(null, "Application Certificate", new CertificateWrapper() { Certificate = m_certificate }, true);
            }
            catch (Exception exception)
            {
                var sre = exception as ServiceResultException;

                if (sre != null && sre.StatusCode == StatusCodes.BadNothingToDo)
                {
                    return;
                }

                MessageBox.Show(Parent.Text + ": " + exception.Message);
                CertificateRequestTimer.Enabled = false;
            }
        }

        private void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            try
            {
                m_server.ApplyChanges();
            }
            catch (Exception exception)
            {
                var se = exception as ServiceResultException;

                if (se == null || se.StatusCode != StatusCodes.BadServerHalted)
                {
                    MessageBox.Show(Parent.Text + ": " + exception.Message);
                }
            }

            try
            {
                m_server.Disconnect();
            }
            catch (Exception)
            {
                // ignore.
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

    }
}
