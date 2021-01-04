/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to specify a new access rule for a file.
    /// </summary>
    public partial class ViewCertificateDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes the dialog.
        /// </summary>
        public ViewCertificateDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private string m_currentDirectory;
        private CertificateIdentifier m_certificate;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task<bool> ShowDialog(CertificateIdentifier certificate)
        {
            m_certificate = certificate;

            CertificateStoreCTRL.StoreType = null;
            CertificateStoreCTRL.StorePath = null;
            CertificateStoreCTRL.ReadOnly = true;
            ApplicationNameTB.Text = null;
            ApplicationUriTB.Text = null;
            OrganizationTB.Text = null;
            DomainsTB.Text = System.Net.Dns.GetHostName();
            SubjectNameTB.Text = null;
            IssuerNameTB.Text = null;
            ValidFromTB.Text = null;
            ValidToTB.Text = null;
            ThumbprintTB.Text = null;

            if (certificate != null)
            {
                CertificateStoreCTRL.StoreType = certificate.StoreType;
                CertificateStoreCTRL.StorePath = certificate.StorePath;
                SubjectNameTB.Text = certificate.SubjectName;
                ThumbprintTB.Text = certificate.Thumbprint;

                X509Certificate2 data = await certificate.Find();

                if (data != null)
                {
                    // fill in subject name.
                    StringBuilder buffer = new StringBuilder();

                    foreach (string element in X509Utils.ParseDistinguishedName(data.Subject))
                    {
                        if (element.StartsWith("CN="))
                        {
                            ApplicationNameTB.Text = element.Substring(3);
                        }

                        if (element.StartsWith("O="))
                        {

                            OrganizationTB.Text = element.Substring(2);
                        }

                        if (buffer.Length > 0)
                        {
                            buffer.Append('/');
                        }

                        buffer.Append(element);
                    }

                    if (buffer.Length > 0)
                    {
                        SubjectNameTB.Text = buffer.ToString();
                    }

                    // fill in issuer name.
                    buffer = new StringBuilder();

                    foreach (string element in X509Utils.ParseDistinguishedName(data.Issuer))
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Append('/');
                        }

                        buffer.Append(element);
                    }

                    if (buffer.Length > 0)
                    {
                        IssuerNameTB.Text = buffer.ToString();
                    }

                    // fill in application uri.
                    string applicationUri = X509Utils.GetApplicationUriFromCertificate(data);

                    if (!String.IsNullOrEmpty(applicationUri))
                    {
                        ApplicationUriTB.Text = applicationUri;
                    }

                    // fill in domains.
                    buffer = new StringBuilder();

                    foreach (string domain in X509Utils.GetDomainsFromCertficate(data))
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Append(", ");
                        }

                        buffer.Append(domain);
                    }

                    if (buffer.Length > 0)
                    {
                        DomainsTB.Text = buffer.ToString();
                    }

                    ValidFromTB.Text = data.NotBefore.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    ValidToTB.Text = data.NotAfter.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    ThumbprintTB.Text = data.Thumbprint;
                }
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, System.Reflection.MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void DetailsBTN_Click(object sender, EventArgs e)
        {
            try
            {
                await new CertificateDlg().ShowDialog(m_certificate);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, System.Reflection.MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ExportBTN_Click(object sender, EventArgs e)
        {
            try
            {
                const string caption = "Export Certificate";

                if (m_currentDirectory == null)
                {
                    m_currentDirectory = Utils.GetAbsoluteDirectoryPath("%LocalApplicationData%", false, false, false);
                }

                if (m_currentDirectory == null)
                {
                    m_currentDirectory = Environment.CurrentDirectory;
                }

                X509Certificate2 certificate = await m_certificate.Find();

                if (certificate == null)
                {
                    MessageBox.Show("Cannot export an invalid certificate.", caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string displayName = null;

                foreach (string element in X509Utils.ParseDistinguishedName(certificate.Subject))
                {
                    if (element.StartsWith("CN="))
                    {
                        displayName = element.Substring(3);
                        break;
                    }
                }

                StringBuilder filePath = new StringBuilder();

                if (!String.IsNullOrEmpty(displayName))
                {
                    filePath.Append(displayName);
                    filePath.Append(" ");
                }

                filePath.Append("[");
                filePath.Append(certificate.Thumbprint);
                filePath.Append("].der");

                SaveFileDialog dialog = new SaveFileDialog();

                dialog.CheckFileExists = false;
                dialog.CheckPathExists = true;
                dialog.DefaultExt = ".der";
                dialog.Filter = "Certificate Files (*.der)|*.der|All Files (*.*)|*.*";
                dialog.ValidateNames = true;
                dialog.Title = "Save Certificate File";
                dialog.FileName = filePath.ToString();
                dialog.InitialDirectory = m_currentDirectory;

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                FileInfo fileInfo = new FileInfo(dialog.FileName);
                m_currentDirectory = fileInfo.DirectoryName;

                // save the file.
                using (Stream ostrm = fileInfo.Open(FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    byte[] data = certificate.RawData;
                    ostrm.Write(data, 0, data.Length);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, System.Reflection.MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
