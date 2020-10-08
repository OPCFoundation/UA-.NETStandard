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
    /// A control with button that displays edit array dialog.
    /// </summary>
    public partial class SelectUrlsCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public SelectUrlsCtrl()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private event EventHandler m_UrlsChanged;
        private List<Uri> m_urls;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// The list of urls.
        /// </summary>
        public List<Uri> Urls 
        {
            get 
            {
                return m_urls; 
            }

            set
            {
                if (CurrentUrlsControl != null)
                {
                    StringBuilder builder = new StringBuilder();

                    if (value != null)
                    {
                        for (int ii = 0; ii < value.Count; ii++)
                        {
                            if (builder.Length > 0)
                            {
                                builder.Append(", ");
                            }

                            builder.Append(value[ii].Scheme);

                            if (value[ii].Port > 0)
                            {
                                builder.Append(":");
                                builder.Append(value[ii].Port);
                            }
                        }
                    }

                    CurrentUrlsControl.Text = builder.ToString();
                }

                m_urls = value;
            }
        }
        
        /// <summary>
        /// Gets or sets the control that is stores with the current file path.
        /// </summary>
        public Control CurrentUrlsControl { get; set; }

        /// <summary>
        /// Raised when the profiles are changed.
        /// </summary>
        public event EventHandler UrlsChanged
        {
            add { m_UrlsChanged += value; }
            remove { m_UrlsChanged -= value; }
        }
        #endregion

        #region Event Handlers
        private void BrowseBTN_Click(object sender, EventArgs e)
        {
            if (CurrentUrlsControl == null)
            {
                return;
            }

            string[] strings = null;

            if (m_urls != null)
            {
                strings = new string[m_urls.Count];

                for (int ii = 0; ii < m_urls.Count; ii++)
                {
                    strings[ii] = m_urls[ii].ToString();
                }
            }

            strings = new EditArrayDlg().ShowDialog(strings, BuiltInType.String, false, null) as string[];

            if (strings == null)
            {
                return;
            }

            List<Uri> urls = new List<Uri>();

            for (int ii = 0; ii < strings.Length; ii++)
            {
                Uri url = Utils.ParseUri(strings[ii]);

                if (url != null)
                {
                    urls.Add(url);
                }
            }

            Urls = urls;

            if (m_UrlsChanged != null)
            {
                m_UrlsChanged(this, e);
            }
        }
        #endregion
    }
}
