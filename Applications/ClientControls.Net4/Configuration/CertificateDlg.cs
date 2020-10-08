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
using System.Windows.Forms;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a ApplicationDescription.
    /// </summary>
    public partial class CertificateDlg : Form
    {
        /// <summary>
        /// Contructs the object.
        /// </summary>
        public CertificateDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            PrivateKeyCB.Items.Add("No");
            PrivateKeyCB.Items.Add("Yes");
            PrivateKeyCB.SelectedIndex = 0;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task<bool> ShowDialog(CertificateIdentifier certificateIdentifier)
        {
            CertificateStoreCTRL.StoreType = null;
            CertificateStoreCTRL.StorePath = null;
            PrivateKeyCB.SelectedIndex = 0;
            PropertiesCTRL.Initialize((X509Certificate2)null);

            if (certificateIdentifier != null)
            {
                X509Certificate2 certificate = await certificateIdentifier.Find();

                CertificateStoreCTRL.StoreType = certificateIdentifier.StoreType;
                CertificateStoreCTRL.StorePath = certificateIdentifier.StorePath;

                if (certificate != null && certificateIdentifier.Find(true) != null)
                {
                    PrivateKeyCB.SelectedIndex = 1;
                }
                else
                {
                    PrivateKeyCB.SelectedIndex = 0;
                }

                PropertiesCTRL.Initialize(certificate);
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(X509Certificate2 certificate)
        {
            CertificateStoreCTRL.StoreType = null;
            CertificateStoreCTRL.StorePath = null;
            PrivateKeyCB.SelectedIndex = 0;
            PropertiesCTRL.Initialize(certificate);

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }
  
            return true;
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
    }
}
