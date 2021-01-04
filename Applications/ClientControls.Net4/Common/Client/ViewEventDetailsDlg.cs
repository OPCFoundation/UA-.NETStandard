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
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays all fields associated with an event notification.
    /// </summary>
    public partial class ViewEventDetailsDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public ViewEventDetailsDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion
        
        #region Private Fields
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Shows all fields for the current condition.
        /// </summary>
        public bool ShowDialog(FilterDeclaration filter, VariantCollection fields)
        {
            // fill in dialog.
            for (int ii = 0; ii < filter.Fields.Count; ii++)
            {
                InstanceDeclaration instance = filter.Fields[ii].InstanceDeclaration;
                ListViewItem item = new ListViewItem(instance.DisplayPath);
                item.SubItems.Add(instance.DataTypeDisplayText);

                string text = null;

                // check for missing fields.
                if (fields.Count <= ii+1 || fields[ii+1].Value == null)
                {
                    text = String.Empty;
                }

                // use default string format.
                else
                {
                    text = fields[ii+1].ToString();
                }

                item.SubItems.Add(text);
                item.Tag = filter.Fields[ii];
                FieldsLV.Items.Add(item);
            }

            // adjust columns.
            for (int ii = 0; ii < FieldsLV.Columns.Count; ii++)
            {
                FieldsLV.Columns[ii].Width = -2;
            }

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion
    }
}
