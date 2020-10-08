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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a list of certificates.
    /// </summary>
    public partial class CertificateListDlg : Form
    {
        /// <summary>
        /// Contructs the object.
        /// </summary>
        public CertificateListDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public CertificateIdentifier ShowDialog(CertificateStoreIdentifier store, bool allowStoreChange)
        {
            CertificateStoreCTRL.StoreType = CertificateStoreType.Directory;
            CertificateStoreCTRL.StorePath = String.Empty;
            CertificateStoreCTRL.ReadOnly = !allowStoreChange;
            CertificatesCTRL.Initialize(null);
            OkBTN.Enabled = false;

            if (store != null)
            {
                CertificateStoreCTRL.StoreType = store.StoreType;
                CertificateStoreCTRL.StorePath = store.StorePath;
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            CertificateIdentifier id = new CertificateIdentifier();
            id.StoreType = CertificateStoreCTRL.StoreType;
            id.StorePath = CertificateStoreCTRL.StorePath;
            id.Certificate = CertificatesCTRL.SelectedCertificate;
            return id;
        }
        
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void FilterBTN_Click(object sender, EventArgs e)
        {
            try
            {
                CertificateListFilter filter = new CertificateListFilter();
                filter.SubjectName = SubjectNameTB.Text.Trim();
                filter.IssuerName = IssuerNameTB.Text.Trim();
                filter.Domain = DomainTB.Text.Trim();
                filter.PrivateKey = PrivateKeyCK.Checked;

                List<CertificateListFilterType> types = new List<CertificateListFilterType>();

                if (ApplicationCK.Checked)
                {
                    types.Add(CertificateListFilterType.Application);
                }

                if (CertificateAuthorityCK.Checked)
                {
                    types.Add(CertificateListFilterType.CA);
                }

                if (SelfSignedCK.Checked)
                {
                    types.Add(CertificateListFilterType.SelfSigned);
                }

                if (IssuedCK.Checked)
                {
                    types.Add(CertificateListFilterType.Issued);
                }

                if (types.Count > 0)
                {
                    filter.CertificateTypes = types.ToArray();
                }

                CertificatesCTRL.SetFilter(filter);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CertificatesCTRL_ItemsSelected(object sender, ListItemActionEventArgs e)
        {
            try
            {
                OkBTN.Enabled = e.Items.Count == 1;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CertificateStoreCTRL_StoreChanged(object sender, EventArgs e)
        {
            try
            {
                CertificateStoreIdentifier store = new CertificateStoreIdentifier();
                store.StoreType = CertificateStoreCTRL.StoreType;
                store.StorePath = CertificateStoreCTRL.StorePath;
                CertificatesCTRL.Initialize(store, null).Wait();

                FilterBTN_Click(sender, e);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
