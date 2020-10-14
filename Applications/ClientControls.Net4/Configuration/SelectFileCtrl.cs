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
    /// A control with button that displays an open file dialog.
    /// </summary>
    public partial class SelectFileCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public SelectFileCtrl()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private event EventHandler m_FileSelected;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Gets or sets the default file extension.
        /// </summary>
        public string DefaultExt { get; set; }

        /// <summary>
        /// Gets or sets the file filters.
        /// </summary>
        public string Filter { get; set; }

        /// <summary>
        /// Gets or sets the current directory.
        /// </summary>
        public string CurrentDirectory { get; set; }

        /// <summary>
        /// Gets or sets the control that is stores with the current file path.
        /// </summary>
        public Control FilePathControl { get; set; }

        /// <summary>
        /// Raised when a new file is selected.
        /// </summary>
        public event EventHandler FileSelected
        {
            add { m_FileSelected += value; }
            remove { m_FileSelected -= value; }
        }
        #endregion

        #region Event Handlers
        private void BrowseBTN_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(Filter))
            {
                Filter = "All Files (*.*)|*.*";
            }

            // set the current directory.
            if (!String.IsNullOrEmpty(FilePathControl.Text))
            {
                FileInfo info = new FileInfo(FilePathControl.Text);

                if (info.Exists)
                {
                    CurrentDirectory = info.DirectoryName;
                }
            }

            // open file dialog.
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.CheckFileExists = true;
            dialog.CheckPathExists = true;
            dialog.DefaultExt = DefaultExt;
            dialog.Filter = Filter;
            dialog.Multiselect = false;
            dialog.ValidateNames = true;
            dialog.FileName = null;
            dialog.InitialDirectory = CurrentDirectory;
            dialog.RestoreDirectory = true;

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            FilePathControl.Text = dialog.FileName;

            if (m_FileSelected != null)
            {
                m_FileSelected(this, new EventArgs());
            }
        }
        #endregion
    }
}
