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
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control with button that displays a select certificate store dialog.
    /// </summary>
    public partial class SelectCertificateStoreCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public SelectCertificateStoreCtrl()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private event EventHandler m_CertificateStoreSelected;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Gets or sets the control that is stores with the current certificate store.
        /// </summary>
        public Control CertificateStoreControl { get; set; }

        /// <summary>
        /// Raised when a new file is selected.
        /// </summary>
        public event EventHandler CertificateStoreSelected
        {
            add { m_CertificateStoreSelected += value; }
            remove { m_CertificateStoreSelected -= value; }
        }
        #endregion

        #region Event Handlers
        private void BrowseBTN_Click(object sender, EventArgs e)
        {
            CertificateStoreIdentifier store = new CertificateStoreIdentifier();
            store.StoreType = CertificateStoreIdentifier.DetermineStoreType(CertificateStoreControl.Text);
            store.StorePath = CertificateStoreControl.Text;

            store = new CertificateStoreDlg().ShowDialog(store);

            if (store == null)
            {
                return;
            }

            CertificateStoreControl.Text = store.StorePath;

            if (m_CertificateStoreSelected != null)
            {
                m_CertificateStoreSelected(this, new EventArgs());
            }
        }
        #endregion
    }
}
