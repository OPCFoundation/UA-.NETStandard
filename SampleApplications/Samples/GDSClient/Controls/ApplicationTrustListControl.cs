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
    public partial class ApplicationTrustListControl : UserControl
    {
        public ApplicationTrustListControl()
        {
            InitializeComponent();
        }

        private GlobalDiscoveryServer m_gds;
        private PushConfigurationServer m_server;
        private RegisteredApplication m_application;
        private string m_trustListStorePath;
        private string m_issuerListStorePath;

        public void Initialize(GlobalDiscoveryServer gds, PushConfigurationServer server, RegisteredApplication application, bool isHttps)
        {
            m_gds = gds;
            m_server = server;
            m_application = application;

            // display local trust list.
            if (application != null)
            {
                m_trustListStorePath = (isHttps) ? m_application.HttpsTrustListStorePath : m_application.TrustListStorePath;
                m_issuerListStorePath = (isHttps) ? m_application.HttpsIssuerListStorePath : m_application.IssuerListStorePath;
                CertificateStoreControl.Initialize(m_trustListStorePath, m_issuerListStorePath, null);
                MergeWithGdsButton.Enabled = !String.IsNullOrEmpty(m_trustListStorePath);
            }

            ApplyChangesButton.Enabled = false;
        }

        private void ReloadTrustListButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_application != null)
                {
                    if (m_application.RegistrationType == RegistrationType.ServerPush)
                    {
                        var trustList = m_server.ReadTrustList();
                        CertificateStoreControl.Initialize(trustList);
                    }
                    else
                    {
                        CertificateStoreControl.Initialize(m_trustListStorePath, m_issuerListStorePath, null);
                    }
                }
                else
                {
                    CertificateStoreControl.Initialize(null, null, null);
                }
            }
            catch (Exception exception)
            {
                Opc.Ua.Configuration.ExceptionDlg.Show(Parent.Text, exception);
            }
        }

        private void MergeWithGdsButton_Click(object sender, EventArgs e)
        {
            PullFromGds(false);
        }

        private void PullFromGdsButton_Click(object sender, EventArgs e)
        {
            PullFromGds(true);
        }

        private void DeleteExistingFromStore(string storePath)
        {
            if (String.IsNullOrEmpty(storePath))
            {
                return;
            }

            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
            {
                foreach (var certificate in store.Enumerate())
                {
                    if (store.GetPrivateKeyFilePath(certificate.Thumbprint) != null)
                    {
                        continue;
                    }

                    List<string> fields = Utils.ParseDistinguishedName(certificate.Subject);

                    if (fields.Contains("CN=UA Local Discovery Server"))
                    {
                        continue;
                    }

                    DirectoryCertificateStore ds = store as DirectoryCertificateStore;

                    if (ds != null)
                    {
                        string path = Utils.GetAbsoluteFilePath(m_application.CertificatePublicKeyPath, true, false, false);

                        if (path != null)
                        {
                            if (String.Compare(path, ds.GetPublicKeyFilePath(certificate.Thumbprint), StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                continue;
                            }
                        }

                        path = Utils.GetAbsoluteFilePath(m_application.CertificatePrivateKeyPath, true, false, false);

                        if (path != null)
                        {
                            if (String.Compare(path, ds.GetPrivateKeyFilePath(certificate.Thumbprint), StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                continue;
                            }
                        }
                    }

                    store.Delete(certificate.Thumbprint);
                }
            }
        }

        private void PullFromGds(bool deleteBeforeAdd)
        {
            try
            {
                NodeId trustListId = m_gds.GetTrustList(m_application.ApplicationId, 0);

                if (trustListId == null)
                {
                    CertificateStoreControl.Initialize(null, null, null);
                    return;
                }

                var trustList = m_gds.ReadTrustList(trustListId);

                if (m_application.RegistrationType == RegistrationType.ServerPush)
                {
                    CertificateStoreControl.Initialize(trustList);

                    MessageBox.Show(
                        Parent,
                        "The trust list (include CRLs) was downloaded from the GDS. It now has to be pushed to the Server.",
                        Parent.Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return;
                }

                if (!String.IsNullOrEmpty(m_trustListStorePath))
                {
                    if (deleteBeforeAdd)
                    {
                        DeleteExistingFromStore(m_trustListStorePath);
                        DeleteExistingFromStore(m_issuerListStorePath);
                    }
                }

                if (!String.IsNullOrEmpty(m_trustListStorePath))
                {
                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_trustListStorePath))
                    {
                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.TrustedCertificates) != 0)
                        {
                            foreach (var certificate in trustList.TrustedCertificates)
                            {
                                var x509 = new X509Certificate2(certificate);

                                if (store.FindByThumbprint(x509.Thumbprint) == null)
                                {
                                    store.Add(x509);
                                }
                            }
                        }

                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.TrustedCrls) != 0)
                        {
                            foreach (var crl in trustList.TrustedCrls)
                            {
                                store.AddCRL(new X509CRL(crl));
                            }
                        }
                    }
                }

                if (!String.IsNullOrEmpty(m_application.IssuerListStorePath))
                {
                    using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_application.IssuerListStorePath))
                    {
                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.IssuerCertificates) != 0)
                        {
                            foreach (var certificate in trustList.IssuerCertificates)
                            {
                                var x509 = new X509Certificate2(certificate);

                                if (store.FindByThumbprint(x509.Thumbprint) == null)
                                {
                                    store.Add(x509);
                                }
                            }
                        }

                        if ((trustList.SpecifiedLists & (uint)Opc.Ua.TrustListMasks.IssuerCrls) != 0)
                        {
                            foreach (var crl in trustList.IssuerCrls)
                            {
                                store.AddCRL(new X509CRL(crl));
                            }
                        }
                    }
                }

                CertificateStoreControl.Initialize(m_trustListStorePath, m_issuerListStorePath, null);

                MessageBox.Show(
                    Parent,
                    "The trust list (include CRLs) was downloaded from the GDS and saved locally.",
                    Parent.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                Opc.Ua.Configuration.ExceptionDlg.Show(Parent.Text, exception);
            }
        }

        private void PushToServerButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_application != null)
                {
                    if (m_application.RegistrationType == RegistrationType.ServerPush)
                    {
                        var trustList = CertificateStoreControl.GetTrustLists();

                        bool applyChanges = m_server.UpdateTrustList(trustList);

                        if (applyChanges)
                        {
                            MessageBox.Show(
                                Parent,
                                "The trust list was updated, however, the apply changes command must be sent before the server will use the new trust list.",
                                Parent.Text,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);

                            ApplyChangesButton.Enabled = true;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Opc.Ua.Configuration.ExceptionDlg.Show(Parent.Text, exception);
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
                    Opc.Ua.Configuration.ExceptionDlg.Show(Parent.Text, exception);
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
    }
}
