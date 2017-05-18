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
        private RegisteredApplication m_application;
        private string m_trustListStorePath;
        private string m_issuerListStorePath;

        public void Initialize(GlobalDiscoveryServer gds, RegisteredApplication application, bool isHttps)
        {
            m_gds = gds;
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
                    CertificateStoreControl.Initialize(m_trustListStorePath, m_issuerListStorePath, null);
                }
                else
                {
                    CertificateStoreControl.Initialize(null, null, null);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(Parent.Text + ": " + exception.Message);
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

        private async void DeleteExistingFromStore(string storePath)
        {
            if (String.IsNullOrEmpty(storePath))
            {
                return;
            }

            using (DirectoryCertificateStore store = (DirectoryCertificateStore) CertificateStoreIdentifier.OpenStore(storePath))
            {
                X509Certificate2Collection certificates = await store.Enumerate();
                foreach (var certificate in certificates)
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

                    await store.Delete(certificate.Thumbprint);
                }
            }
        }

        private void PullFromGds(bool deleteBeforeAdd)
        {
            try
            {
                NodeId trustListId = m_gds.GetTrustList(m_application.ApplicationId, null);

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
    }
}
