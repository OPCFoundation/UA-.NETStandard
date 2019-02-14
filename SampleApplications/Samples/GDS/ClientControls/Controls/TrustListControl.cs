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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace Opc.Ua.Gds.Client.Controls
{
    public partial class CertificateStoreControl : UserControl
    {
        public CertificateStoreControl()
        {
            InitializeComponent();
            CertificateListGridView.AutoGenerateColumns = false;
            ImageList = new ImageListControl().ImageList;

            m_dataset = new DataSet();

            m_dataset.Tables.Add("Certificates");

            m_dataset.Tables[0].Columns.Add("Subject", typeof(string));
            m_dataset.Tables[0].Columns.Add("Issuer", typeof(string));
            m_dataset.Tables[0].Columns.Add("IsCA", typeof(string));
            m_dataset.Tables[0].Columns.Add("HasCrl", typeof(string));
            m_dataset.Tables[0].Columns.Add("Status", typeof(Status));
            m_dataset.Tables[0].Columns.Add("ValidTo", typeof(string));
            m_dataset.Tables[0].Columns.Add("Thumbprint", typeof(string));
            m_dataset.Tables[0].Columns.Add("Certificate", typeof(X509Certificate2));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("Crls", typeof(List<X509CRL>));

            CertificateListGridView.DataSource = m_dataset.Tables[0];
        }

        private DataSet m_dataset;
        private FileInfo m_certificateFile;
        private string m_trustedStorePath;
        private string m_issuerStorePath;
        private string m_rejectedStorePath;
        private DataTable CertificatesTable { get { return m_dataset.Tables[0]; } }

        private enum Status
        {
            Trusted,
            Issuer,
            Rejected,
            Deleted
        }

        private ICertificateStore CreateStore(string storePath)
        {
            ICertificateStore store = CertificateStoreIdentifier.CreateStore(CertificateStoreIdentifier.DetermineStoreType(storePath));
            store.Open(storePath);
            return store;
        }

        public async void Initialize(string trustedStorePath, string issuerStorePath, string rejectedStorePath)
        {
            CertificatesTable.Rows.Clear();

            m_trustedStorePath = trustedStorePath;
            m_issuerStorePath = issuerStorePath;
            m_rejectedStorePath = rejectedStorePath;

            if (!String.IsNullOrEmpty(trustedStorePath))
            {
                using (ICertificateStore store = CreateStore(trustedStorePath))
                {
                    X509CertificateCollection certificates = await store.Enumerate();
                    foreach (X509Certificate2 certificate in certificates)
                    {
                        List<X509CRL> crls = new List<X509CRL>();

                        if (store.SupportsCRLs)
                        {
                            foreach (X509CRL crl in store.EnumerateCRLs(certificate))
                            {
                                crls.Add(crl);
                            }
                        }

                        AddCertificate(certificate, Status.Trusted, crls);
                    }
                }
            }

            string path1 = Utils.GetAbsoluteDirectoryPath(trustedStorePath, true, false, false);
            string path2 = Utils.GetAbsoluteDirectoryPath(issuerStorePath, true, false, false);

            if (String.Compare(path1, path2, StringComparison.OrdinalIgnoreCase) != 0)
            {
                if (!String.IsNullOrEmpty(issuerStorePath))
                {
                    using (ICertificateStore store = CreateStore(issuerStorePath))
                    {
                        X509Certificate2Collection certificates = await store.Enumerate();
                        foreach (X509Certificate2 certificate in certificates)
                        {
                            List<X509CRL> crls = new List<X509CRL>();

                            if (store.SupportsCRLs)
                            {
                                foreach (X509CRL crl in store.EnumerateCRLs(certificate))
                                {
                                    crls.Add(crl);
                                }
                            }
                            
                            AddCertificate(certificate, Status.Issuer, crls);
                        }
                    }
                }
            }

            if (!String.IsNullOrEmpty(rejectedStorePath))
            {
                using (ICertificateStore store = CreateStore(rejectedStorePath))
                {
                    X509Certificate2Collection certificates = await store.Enumerate();
                    foreach (X509Certificate2 certificate in certificates)
                    {
                        AddCertificate(certificate, Status.Rejected, null);
                    }
                }
            }

            m_dataset.AcceptChanges();
            NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
        }

        public void Initialize(TrustListDataType trustList, X509Certificate2Collection rejectedList, bool deleteBeforeAdd)
        {
            if (deleteBeforeAdd)
            {
                CertificatesTable.Rows.Clear();
            }

            if (trustList != null)
            {
                if ((trustList.SpecifiedLists & (uint)TrustListMasks.TrustedCertificates) != 0 && trustList.TrustedCertificates != null)
                {
                    foreach (var certificateBytes in trustList.TrustedCertificates)
                    {
                        var certificate = new X509Certificate2(certificateBytes);

                        List<X509CRL> crls = new List<X509CRL>();

                        if ((trustList.SpecifiedLists & (uint)TrustListMasks.TrustedCrls) != 0 && trustList.TrustedCrls != null)
                        {
                            foreach (var crlBytes in trustList.TrustedCrls)
                            {
                                X509CRL crl = new X509CRL(crlBytes);
                                
                                if (Utils.CompareDistinguishedName(crl.Issuer, certificate.Subject) &&
                                    crl.VerifySignature(certificate, false))
                                {
                                    crls.Add(crl);
                                }
                            }
                        }

                        AddCertificate(certificate, Status.Trusted, crls);
                    }
                }

                if ((trustList.SpecifiedLists & (uint)TrustListMasks.IssuerCertificates) != 0 && trustList.IssuerCertificates != null)
                {
                    foreach (var certificateBytes in trustList.IssuerCertificates)
                    {
                        var certificate = new X509Certificate2(certificateBytes);

                        List<X509CRL> crls = new List<X509CRL>();

                        if ((trustList.SpecifiedLists & (uint)TrustListMasks.IssuerCrls) != 0 && trustList.IssuerCrls != null)
                        {
                            foreach (var crlBytes in trustList.IssuerCrls)
                            {
                                X509CRL crl = new X509CRL(crlBytes);

                                if (Utils.CompareDistinguishedName(crl.Issuer, certificate.Subject) &&
                                    crl.VerifySignature(certificate, false))
                                {
                                    crls.Add(crl);
                                }
                            }
                        }

                        AddCertificate(certificate, Status.Issuer, crls);
                    }
                }
            }

            if (rejectedList != null)
            {
                foreach (X509Certificate2 certificate in rejectedList)
                {
                    AddCertificate(certificate, Status.Rejected, null);
                }
            }

            m_dataset.AcceptChanges();
            NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
        }

        public TrustListDataType GetTrustLists()
        {
            ByteStringCollection trusted = new ByteStringCollection();
            ByteStringCollection trustedCrls = new ByteStringCollection();
            ByteStringCollection issuers = new ByteStringCollection();
            ByteStringCollection issuersCrls = new ByteStringCollection();

            foreach (DataGridViewRow row in CertificateListGridView.Rows)
            {
                DataRowView source = row.DataBoundItem as DataRowView;

                Status status = (Status)source.Row[4];
                X509Certificate2 certificate = source.Row[7] as X509Certificate2;
                List<X509CRL> crls = source.Row[9] as List<X509CRL>;

                if (certificate != null)
                {
                    if (status == Status.Trusted)
                    {
                        trusted.Add(certificate.RawData);

                        if (crls != null)
                        {
                            foreach (var crl in crls)
                            {
                                trustedCrls.Add(crl.RawData);
                            }
                        }
                    }
                    else if (status == Status.Issuer)
                    {
                        issuers.Add(certificate.RawData);

                        if (crls != null)
                        {
                            foreach (var crl in crls)
                            {
                                issuersCrls.Add(crl.RawData);
                            }
                        }
                    }
                }
            }

            TrustListDataType trustList = new TrustListDataType()
            {
                SpecifiedLists = (uint)(TrustListMasks.All),
                TrustedCertificates = trusted,
                TrustedCrls = trustedCrls,
                IssuerCertificates = issuers,
                IssuerCrls = issuersCrls
            };

            return trustList;
        }

        private void SetIcon(DataRow row, Status status)
        {
            switch (status)
            {
                default:
                case Status.Rejected: { row[8] = ImageList.Images[ImageIndex.RejectedCertificate]; break; }
                case Status.Trusted: { row[8] = ImageList.Images[ImageIndex.TrustedCertificate]; break; }
                case Status.Issuer: { row[8] = ImageList.Images[ImageIndex.UntrustedCertificate]; break; }
            }
        }

        private bool IsCA(X509Certificate2 certificate)
        {
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension is X509BasicConstraintsExtension basicContraints)
                {
                    if (basicContraints.CertificateAuthority)
                    {
                        return true;
                    }

                    break;
                }
            }

            return false;
        }

        private string GetCommonName(X509Certificate2 certificate)
        {
            foreach (string element in Utils.ParseDistinguishedName(certificate.Subject))
            {
                if (element.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                {
                    return element.Substring(3);
                }
            }

            return "(unknown)";
        }

        private void AddCertificate(X509Certificate2 certificate, Status status, List<X509CRL> crls)
        {
            DataRow row = CertificatesTable.NewRow();

            row[0] = certificate.Subject;
            row[1] = certificate.Issuer;
            row[2] = IsCA(certificate);
            row[3] = (crls != null && crls.Count > 0).ToString();
            row[4] = status;
            row[5] = certificate.NotAfter.ToString("yyy-MM-dd");
            row[6] = certificate.Thumbprint;
            row[7] = certificate;
            row[9] = crls;

            SetIcon(row, status);

            CertificatesTable.Rows.Add(row);
            NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
        }

        private void MoveCertificate(DataRow row, Status status)
        {
            Status oldStatus = (Status)row[4];
            row[4] = status;
            SetIcon(row, status);

            string targetStorePath = null;

            switch (status)
            {
                case Status.Trusted: { targetStorePath = m_trustedStorePath; break; }
                case Status.Issuer: { targetStorePath = m_issuerStorePath; break; }
                case Status.Rejected: { targetStorePath = m_rejectedStorePath; break; }
            }

            string oldStorePath = null;

            switch (oldStatus)
            {
                case Status.Trusted: { oldStorePath = m_trustedStorePath; break; }
                case Status.Issuer: { oldStorePath = m_issuerStorePath; break; }
                case Status.Rejected: { oldStorePath = m_rejectedStorePath; break; }
            }

            X509Certificate2 certificate = (X509Certificate2)row[7];

            if (oldStorePath != targetStorePath)
            {
                if (!String.IsNullOrEmpty(targetStorePath))
                {
                    using (ICertificateStore store = CreateStore(targetStorePath))
                    {
                        store.Add(certificate);
                    }
                }
                
                if (!String.IsNullOrEmpty(oldStorePath))
                {
                    using (ICertificateStore store = CreateStore(oldStorePath))
                    {
                        store.Delete(certificate.Thumbprint);
                    }
                }
            }
        }

        private void ViewMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewCell cell in CertificateListGridView.SelectedCells)
                {
                    DataRowView source = CertificateListGridView.Rows[cell.RowIndex].DataBoundItem as DataRowView;
                    EditValueDlg dialog = new EditValueDlg
                    {
                        Size = new Size(800, 400)
                    };
                    dialog.ShowDialog(null, "", new CertificateWrapper() { Certificate = (X509Certificate2)source.Row[7] }, true, this.Text);
                    break;
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void DeleteMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                List<DataRow> rows = new List<DataRow>();

                foreach (DataGridViewRow row in CertificateListGridView.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    MoveCertificate(source.Row, Status.Deleted);
                    rows.Add(source.Row);
                }

                foreach (var row in rows)
                {
                    row.Delete();
                }

                m_dataset.AcceptChanges();
                NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex); 
            }
        }

        private void TrustMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewCell cell in CertificateListGridView.SelectedCells)
                {
                    DataRowView source = CertificateListGridView.Rows[cell.RowIndex].DataBoundItem as DataRowView;
                    MoveCertificate(source.Row, Status.Trusted);
                }

                m_dataset.AcceptChanges();
                NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void Reject_MenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewCell cell in CertificateListGridView.SelectedCells)
                {
                    DataRowView source = CertificateListGridView.Rows[cell.RowIndex].DataBoundItem as DataRowView;
                    MoveCertificate(source.Row, Status.Rejected);
                }

                m_dataset.AcceptChanges();
                NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void UntrustMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewCell cell in CertificateListGridView.SelectedCells)
                {
                    DataRowView source = CertificateListGridView.Rows[cell.RowIndex].DataBoundItem as DataRowView;
                    MoveCertificate(source.Row, Status.Issuer);
                }

                m_dataset.AcceptChanges();
                NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void ImportMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                string directory = Environment.CurrentDirectory;

                if (m_certificateFile != null)
                {
                    directory = m_certificateFile.DirectoryName;
                }

                OpenFileDialog dialog = new OpenFileDialog
                {
                    CheckFileExists = true,
                    CheckPathExists = true,
                    DefaultExt = "*.der",
                    Filter = "Certificate Files (*.der)|*.der|All Files (*.*)|*.*",
                    Title = "Import Certificate",
                    Multiselect = false,
                    ValidateNames = true,
                    FileName = m_certificateFile?.Name,
                    InitialDirectory = directory,
                    RestoreDirectory = true
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                m_certificateFile = new FileInfo(dialog.FileName);

                X509Certificate2 certificate = CertificateFactory.Load(new X509Certificate2(File.ReadAllBytes(dialog.FileName)), false);

                if (certificate != null)
                {
                    if (!String.IsNullOrEmpty(m_trustedStorePath))
                    {
                        using (ICertificateStore store = CreateStore(m_trustedStorePath))
                        {
                            store.Add(certificate);
                        }
                    }
                }

                AddCertificate(certificate, Status.Trusted, null);
                NoDataWarningLabel.Visible = CertificatesTable.Rows.Count == 0;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void ExportMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (CertificateListGridView.SelectedCells.Count != 1)
                {
                    return;
                }

                X509Certificate2 certificate = null;

                foreach (DataGridViewCell cell in CertificateListGridView.SelectedCells)
                {
                    DataRowView source = CertificateListGridView.Rows[cell.RowIndex].DataBoundItem as DataRowView;
                    certificate = (X509Certificate2)source.Row[7];
                    break;
                }

                string directory = Environment.CurrentDirectory;

                if (m_certificateFile != null)
                {
                    directory = m_certificateFile.DirectoryName;
                }

                SaveFileDialog dialog = new SaveFileDialog
                {
                    CheckFileExists = false,
                    CheckPathExists = true,
                    DefaultExt = ".der",
                    Filter = "Certificate Files (*.der)|*.der|All Files (*.*)|*.*",
                    ValidateNames = true,
                    Title = "Export Certificate",
                    FileName = String.Format("{0} [{1}].der", GetCommonName(certificate), certificate.Thumbprint),
                    InitialDirectory = directory
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                m_certificateFile = new FileInfo(dialog.FileName);
                File.WriteAllBytes(dialog.FileName, certificate.RawData);
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(Text, ex);
            }
        }

        private void PopupMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            if (CertificateListGridView.SelectedCells.Count > 0)
            {
                Status? status = null;
                bool? isCa = null;

                foreach (DataGridViewCell cell in CertificateListGridView.SelectedCells)
                {
                    DataRowView source = CertificateListGridView.Rows[cell.RowIndex].DataBoundItem as DataRowView;

                    if (isCa == null)
                    {
                        isCa = IsCA((X509Certificate2)source.Row[7]);
                    }
                    else
                    {
                        if (!IsCA((X509Certificate2)source.Row[7]))
                        {
                            isCa = false;
                        }
                    }

                    if (status == null)
                    {
                        status = (Status)source.Row[4];
                    }
                    else
                    {
                        if (status != (Status)source.Row[4])
                        {
                            status = null;
                            break;
                        }
                    }
                }

                DeleteMenuItem.Visible = true;
                TrustMenuItem.Visible = status != null && status.Value != Status.Trusted;
                UntrustMenuItem.Visible = status != null && status.Value == Status.Trusted && isCa != null && isCa.Value;
                RejectMenuItem.Visible = !String.IsNullOrEmpty(m_rejectedStorePath) && status != null && status.Value != Status.Rejected;
                Seperator01MenuItem.Visible = isCa != null && isCa.Value && CertificateListGridView.SelectedCells.Count == 1;
                AddCrlMenuItem.Visible = isCa != null && isCa.Value && CertificateListGridView.SelectedCells.Count == 1;
                DeleteCrlMenuItem.Visible = isCa != null && isCa.Value && CertificateListGridView.SelectedCells.Count == 1;
                ExportMenuItem.Enabled = CertificateListGridView.SelectedCells.Count == 1;
            }
            else
            {
                DeleteMenuItem.Visible = false;
                TrustMenuItem.Visible = false;
                UntrustMenuItem.Visible = false;
                RejectMenuItem.Visible = false;
                Seperator01MenuItem.Visible = false;
                AddCrlMenuItem.Visible = false;
                DeleteCrlMenuItem.Visible = false;
                ExportMenuItem.Enabled = false;
            }
        }

        private void CertificateListGridView_DoubleClick(object sender, EventArgs e)
        {
            ViewMenuItem_Click(sender, e);
        }
    }
}
