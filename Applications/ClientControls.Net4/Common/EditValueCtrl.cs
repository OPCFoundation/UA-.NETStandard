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
using System.Text;
using System.Windows.Forms;
using Opc.Ua;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control which is used to edit a value.
    /// </summary>
    public partial class EditValueCtrl : UserControl
    {
        /// <summary>
        /// Initializes the object.
        /// </summary>
        public EditValueCtrl()
        {
            InitializeComponent();
        }

        private Variant m_value;
        private bool m_textChanged;

        /// <summary>
        /// The data type of the value to edit.
        /// </summary>
        public TypeInfo TargetType { get; set; }

        /// <summary>
        /// The value being edited in the control.
        /// </summary>
        public Variant Value 
        {
            get
            {
                return GetValue();
            }

            set
            {
                SetValue(value);
            }
        }

        /// <summary>
        /// Returns the value shown in the control.
        /// </summary>
        private Variant GetValue()
        {
            TypeInfo sourceType = m_value.TypeInfo;

            // check if the value needs to be updated.
            if (m_textChanged)
            {
                object value = TypeInfo.Cast(ValueTB.Text, TypeInfo.Scalars.String, sourceType.BuiltInType);
                m_value = new Variant(value, sourceType);
            }

            return m_value;
        }

        /// <summary>
        /// Sets the value shown in the control.
        /// </summary>
        private void SetValue(Variant value)
        {
            // check for null.
            if (Variant.Null == value)
            {
                ValueTB.Text = String.Empty;
                ValueTB.Enabled = true;
                m_value = Variant.Null;
                return;
            }

            // get the source type.
            TypeInfo sourceType = value.TypeInfo;

            if (sourceType == null)
            {
                sourceType = TypeInfo.Construct(value.Value);
            }

            // convert to target type.
            if (TargetType != null && TargetType.BuiltInType != sourceType.BuiltInType)
            {
                m_value = new Variant(TypeInfo.Cast(value.Value, sourceType, TargetType.BuiltInType), TargetType);
                sourceType = TargetType;
            }
            else
            {
                m_value = new Variant(value.Value, sourceType);
            }

            m_textChanged = false;

            // display arrays and structures as read only strings.
            if (sourceType.ValueRank >= 0 || sourceType.BuiltInType == BuiltInType.ExtensionObject)
            {
                ValueTB.Text = m_value.ToString();
                ValueTB.Enabled = false;
                return;
            }

            // display as editable text.
            ValueTB.Text = (string)TypeInfo.Cast(m_value.Value, sourceType, BuiltInType.String);
            ValueTB.Enabled = true;
        }

        private void ValueTB_TextChanged(object sender, EventArgs e)
        {
            m_textChanged = true;
        }

        private void ValueBTN_Click(object sender, EventArgs e)
        {

        }
    }
}
