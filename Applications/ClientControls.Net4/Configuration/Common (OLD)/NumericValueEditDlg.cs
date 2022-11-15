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
    /// A dialog to edit a numeric value.
    /// </summary>
    public partial class NumericValueEditDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="NumericValueEditDlg"/> class.
        /// </summary>
        public NumericValueEditDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public object ShowDialog(object value, Type type)
        {
            if ((type == null || type == typeof(Variant)) && value != null)
            {
                type = value.GetType();
            }

            if (type == typeof(Variant))
            {
                type = typeof(double);
            }

            SetLimits(type);

            ValueCTRL.Value = Convert.ToDecimal(value);

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return Convert.ChangeType(ValueCTRL.Value, type);
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Sets the limits according to the data type.
        /// </summary>
        private void SetLimits(Type type)
        {
            if (type == typeof(sbyte))
            {
                ValueCTRL.Minimum = SByte.MinValue;
                ValueCTRL.Maximum = SByte.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(byte))
            {
                ValueCTRL.Minimum = Byte.MinValue;
                ValueCTRL.Maximum = Byte.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(short))
            {
                ValueCTRL.Minimum = Int16.MinValue;
                ValueCTRL.Maximum = Int16.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(ushort))
            {
                ValueCTRL.Minimum = UInt16.MinValue;
                ValueCTRL.Maximum = UInt16.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(int))
            {
                ValueCTRL.Minimum = Int32.MinValue;
                ValueCTRL.Maximum = Int32.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(uint))
            {
                ValueCTRL.Minimum = UInt32.MinValue;
                ValueCTRL.Maximum = UInt32.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(long))
            {
                ValueCTRL.Minimum = Int64.MinValue;
                ValueCTRL.Maximum = Int64.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(ulong))
            {
                ValueCTRL.Minimum = UInt64.MinValue;
                ValueCTRL.Maximum = UInt64.MaxValue;
                ValueCTRL.DecimalPlaces = 0;
            }

            if (type == typeof(float))
            {
                ValueCTRL.Minimum = Decimal.MinValue;
                ValueCTRL.Maximum = Decimal.MaxValue;
                ValueCTRL.DecimalPlaces = 6;
            }

            if (type == typeof(double))
            {
                ValueCTRL.Minimum = Decimal.MinValue;
                ValueCTRL.Maximum = Decimal.MaxValue;
                ValueCTRL.DecimalPlaces = 15;
            }
        }
        #endregion
    }
}
