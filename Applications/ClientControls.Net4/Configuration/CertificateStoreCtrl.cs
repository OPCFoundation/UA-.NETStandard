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
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Xml;
using System.IO;
using System.Runtime.Serialization;
using Opc.Ua.Configuration;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Allows a user to specify a certificate store.
    /// </summary>
    public partial class CertificateStoreCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs the object.
        /// </summary>
        public CertificateStoreCtrl()
        {
            InitializeComponent();

            StoreTypeCB.Items.Add(CertificateStoreType.Directory);
            StoreTypeCB.Items.Add(CertificateStoreType.X509Store);
            StoreTypeCB.SelectedIndex = 0;
        }
        #endregion
       
        #region Private Fields
        private event EventHandler m_StoreChanged;
        #endregion

        #region Public Interface
        /// <summary>
        /// Raised when the certificate store is changed in the control.
        /// </summary>
        public event EventHandler StoreChanged
        {
            add { m_StoreChanged += value; }
            remove { m_StoreChanged -= value; }
        }

        /// <summary>
        /// The width of the label in the control.
        /// </summary>
        [DefaultValue(75)]
        public int LabelWidth
        {
            get
            {
                return LeftPN.Width;
            }

            set
            {
                LeftPN.Width = value;
            }
        }

        /// <summary>
        /// Whether the control is read-only.
        /// </summary>
        [DefaultValue(false)]
        public bool ReadOnly
        {
            get
            {
                return !StoreTypeCB.Enabled;
            }

            set
            {
                StoreTypeCB.Enabled = !value;
                StorePathCB.Enabled = !value;
                BrowseBTN.Enabled = !value;
            }
        }

        /// <summary>
        /// The type of certificate store.
        /// </summary>
        [DefaultValue(Utils.DefaultStoreType)]
        public string StoreType
        {
            get 
            { 
                return StoreTypeCB.SelectedItem as string; 
            }

            set
            {
                if (value == null || StoreTypeCB.FindStringExact(value) == -1)
                {
                    StoreTypeCB.SelectedIndex = 0;
                    return;
                }

                StoreTypeCB.SelectedItem = value;
            }
        }

        /// <summary>
        /// The path to the certificate store.
        /// </summary>
        public string StorePath
        {
            get
            {
                if (StorePathCB.SelectedItem == null)
                {
                    return StorePathCB.Text;
                }

                return StorePathCB.SelectedItem as string;
            }

            set
            {
                StorePathCB.SelectedIndex = -1;
                StorePathCB.Text = value;
            }
        }
        #endregion

        #region Private Methods
        private List<string> GetListOfStores(string storeType)
        {
            List<string> stores = new List<string>();

            if (CertificateStoreType.Directory == storeType)
            {
                stores.Add(Path.DirectorySeparatorChar + "OPC Foundation" + Path.DirectorySeparatorChar + "CertificateStores" + Path.DirectorySeparatorChar + "MachineDefault");
                stores.Add(Path.DirectorySeparatorChar + "OPC Foundation" + Path.DirectorySeparatorChar + "CertificateStores" + Path.DirectorySeparatorChar + "UA Applications");
                stores.Add(Path.DirectorySeparatorChar + "OPC Foundation" + Path.DirectorySeparatorChar + "CertificateStores" + Path.DirectorySeparatorChar + "UA Certificate Authorities");
                stores.Add(Path.DirectorySeparatorChar + "OPC Foundation" + Path.DirectorySeparatorChar + "CertificateStores" + Path.DirectorySeparatorChar + "RejectedCertificates");
            }

            if (CertificateStoreType.X509Store == storeType)
            {
                stores.Add("CurrentUser\\UA_MachineDefault");
                stores.Add("CurrentUser\\UA_Applications");
                stores.Add("CurrentUser\\UA_Certificate_Authorities");
            }

            return stores;
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Populates the drop down with the recent file list.
        /// </summary>
        private void StorePathCB_DropDown(object sender, EventArgs e)
        {
            try
            {
                StorePathCB.Items.Clear();

                foreach (string storePath in GetListOfStores(StoreTypeCB.SelectedItem as string))
                {
                    // ignore duplicates.
                    bool found = false;

                    foreach (string item in StorePathCB.Items)
                    {
                        if (String.Compare(storePath, item, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            found = true;
                            break;
                        }
                    }

                    // add list.
                    if (!found)
                    {
                        StorePathCB.Items.Add(storePath);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Browses for new stores to manage.
        /// </summary>
        private void BrowseStoreBTN_Click(object sender, EventArgs e)
        {
            try
            {
                string storeType = StoreTypeCB.SelectedItem as string;
                string storePath = null;

                if (storeType == CertificateStoreType.Directory)
                {
                    FolderBrowserDialog dialog = new FolderBrowserDialog();

                    dialog.Description = "Select Certificate Store Directory";
                    dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                    dialog.ShowNewFolderButton = true;

                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }

                    storePath = dialog.SelectedPath;
                }

                if (storeType == CertificateStoreType.X509Store)
                {
                    CertificateStoreIdentifier store = new CertificateStoreTreeDlg().ShowDialog(null);

                    if (store == null)
                    {
                        return;
                    }

                    storePath = store.StorePath;
                }

                if (String.IsNullOrEmpty(storePath))
                {
                    return;
                }

                bool found = false;

                for (int ii = 0; ii < StorePathCB.Items.Count; ii++)
                {
                    if (String.Compare(storePath, StorePathCB.Items[ii] as string, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        StorePathCB.SelectedIndex = ii;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    StorePathCB.SelectedIndex = StorePathCB.Items.Add(storePath);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void StoreTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                StorePathCB_DropDown(sender, e);

                if (StorePathCB.Items.Count > 0)
                {
                    StorePathCB.SelectedIndex = 0;
                }

                if (m_StoreChanged != null)
                {
                    m_StoreChanged(null, e);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void StorePathCB_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (m_StoreChanged != null)
                {
                    m_StoreChanged(null, e);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }

        }

        private void StorePathCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                CertificateStoreIdentifier store = new CertificateStoreIdentifier();
                store.StoreType = StoreTypeCB.SelectedItem as string;
                store.StorePath = StorePathCB.Text;

                if (StorePathCB.SelectedIndex != -1)
                {
                    store.StorePath = StorePathCB.SelectedItem as string;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
