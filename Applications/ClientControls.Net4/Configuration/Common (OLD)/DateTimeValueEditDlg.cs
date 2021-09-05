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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A dialog to edit a date/time value
    /// </summary>
    public partial class DateTimeValueEditDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="DateTimeValueEditDlg"/> class.
        /// </summary>
        public DateTimeValueEditDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(ref DateTime value)
        {
            if (value < ValueCTRL.MinDate)
            {
                ValueCTRL.Value = ValueCTRL.MinDate;
            }
            else if (value > ValueCTRL.MaxDate)
            {
                ValueCTRL.Value = ValueCTRL.MaxDate;
            }
            else
            {
                ValueCTRL.Value = value;
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }
            
            value = ValueCTRL.Value;

            if (value == ValueCTRL.MinDate)
            {
                value = DateTime.MinValue;
            }

            if (value == ValueCTRL.MaxDate)
            {
                value = DateTime.MaxValue;
            }

            return true;
        }
        #endregion
    }
}
