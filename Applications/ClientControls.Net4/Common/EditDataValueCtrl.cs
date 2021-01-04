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
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    public partial class EditDataValueCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public EditDataValueCtrl()
        {
            InitializeComponent();

            for (BuiltInType ii = BuiltInType.Null; ii < BuiltInType.Variant; ii++)
            {
                DataTypeCB.Items.Add(ii);
            }

            DataTypeCB.SelectedIndex = 0;
            DataTypeCB.Enabled = false;

            for (ValueRankOptions ii = ValueRankOptions.Scalar; ii <= ValueRankOptions.OneDimension; ii++)
            {
                ValueRankCB.Items.Add(ii);
            }

            ValueRankCB.SelectedIndex = 0;
            ValueRankCB.Enabled = false;

            SetShowStatusTimestamp(false);

            StatusCodeCB.Items.Add(new StatusCode(StatusCodes.Good));
            StatusCodeCB.Items.Add(new StatusCode(StatusCodes.GoodLocalOverride));
            StatusCodeCB.Items.Add(new StatusCode(StatusCodes.Uncertain));
            StatusCodeCB.Items.Add(new StatusCode(StatusCodes.UncertainInitialValue));
            StatusCodeCB.Items.Add(new StatusCode(StatusCodes.Bad));
            StatusCodeCB.Items.Add(new StatusCode(StatusCodes.BadDeviceFailure));

            StatusCodeCB.SelectedIndex = 0;

            ServerTimestampDP.Value = DateTime.UtcNow;
            SourceTimestampDP.Value = DateTime.UtcNow;
        }
        #endregion

        #region Value Rank Class
        /// <summary>
        /// The value ranks supported by the control.
        /// </summary>
        private enum ValueRankOptions
        {
            Scalar = -1,
            OneDimension = 1
        }
        #endregion

        #region Private Methods
        #endregion

        #region Public Members
        /// <summary>
        /// Returns the data value displayed in the control.
        /// </summary>
        public DataValue GetDataValue()
        {
            DataValue value = new DataValue();
            value.WrappedValue = GetValue();

            if (ShowStatusTimestamp)
            {
                value.StatusCode = StatusCode;
                value.SourceTimestamp = SourceTimestamp;
                value.ServerTimestamp = ServerTimestamp;
            }

            return value;
        }

        /// <summary>
        /// Returns the data value displayed in the control.
        /// </summary>
        public void SetDataValue(DataValue value, TypeInfo targetType)
        {
            DataTypeCB.SelectedItem = BuiltInType.Null;
            ValueRankCB.SelectedItem = ValueRankOptions.Scalar;

            StatusCode = StatusCodes.Good;
            SourceTimestamp = DateTime.MinValue;
            ServerTimestamp = DateTime.MinValue;

            if (value != null)
            {
                SetValue(value.WrappedValue);

                StatusCode = value.StatusCode;
                SourceTimestamp = value.SourceTimestamp;
                ServerTimestamp = value.ServerTimestamp;

                StatusCodeCK.Checked = true;
                SourceTimestampCK.Checked = true;
                ServerTimestampCK.Checked = true;
            }

            // allow data type to be changed by default.
            DataTypeCB.Enabled = true;

            if (targetType != null)
            {
                DataType = targetType.BuiltInType;
                ValueRank = targetType.ValueRank;

                DataTypeCB.Enabled = false;
                ValueRankCB.Enabled = false;
            }
        }

        /// <summary>
        /// The value displayed in the control.
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
        /// The data type of the value displayed in the control.
        /// </summary>
        public BuiltInType DataType
        {
            get
            {
                return (BuiltInType)DataTypeCB.SelectedItem;
            }

            set
            {
                DataTypeCB.SelectedItem = value;
            }
        }
        
        /// <summary>
        /// The value rank of the value displayed in the control.
        /// </summary>
        public int ValueRank
        {
            get
            {
                return (int)ValueRankCB.SelectedItem;
            }

            set
            {
                ValueRankCB.SelectedItem = (ValueRankOptions)value;
            }
        }

        /// <summary>
        /// The status code associated with the value.
        /// </summary>
        public StatusCode StatusCode
        {
            get
            {
                if (!StatusCodeCK.Checked)
                {
                    return StatusCodes.Good;
                }

                return (StatusCode)StatusCodeCB.SelectedItem;
            }

            set
            {
                ValueRankCB.SelectedItem = value;
            }
        }

        /// <summary>
        /// The source timestamp associated with the value.
        /// </summary>
        public DateTime SourceTimestamp
        {
            get
            {
                if (!SourceTimestampCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return SourceTimestampDP.Value;
            }

            set
            {
                if (value < SourceTimestampDP.MinDate)
                {
                    SourceTimestampCK.Checked = false;
                    return;
                }

                SourceTimestampDP.Value = value;
            }
        }

        /// <summary>
        /// The server timestamp associated with the value.
        /// </summary>
        public DateTime ServerTimestamp
        {
            get
            {
                if (!ServerTimestampCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return ServerTimestampDP.Value;
            }

            set
            {
                if (value < ServerTimestampDP.MinDate)
                {
                    ServerTimestampCK.Checked = false;
                    return;
                }

                ServerTimestampDP.Value = value;
            }
        }

        /// <summary>
        /// If true the status code, server timestamp and source timestamp fields are displayed.
        /// </summary>
        public bool ShowStatusTimestamp
        {
            get
            {
                return StatusCodeCB.Visible;
            }

            set
            {
                SetShowStatusTimestamp(value);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the value shown in the control.
        /// </summary>
        private Variant GetValue()
        {
            BuiltInType targetType = (BuiltInType)DataTypeCB.SelectedItem;
            int valueRank = (int)ValueRankCB.SelectedItem;

            // TBD - Add Support for Arrays
            if (valueRank != ValueRanks.Scalar)
            {
                return Variant.Null;
            }

            // cast the value to the requested data type.
            object value = TypeInfo.Cast(ValueTB.Text, TypeInfo.Scalars.String, targetType);

            return new Variant(value, new TypeInfo(targetType, valueRank));
        }

        /// <summary>
        /// Sets the value shown in the control.
        /// </summary>
        private void SetValue(Variant value)
        {
            BuiltInType targetType = BuiltInType.Null;
            int valueRank = ValueRanks.Scalar;

            if (value.TypeInfo != null && value.TypeInfo.BuiltInType != BuiltInType.Null)
            {
                targetType = value.TypeInfo.BuiltInType;
                valueRank = value.TypeInfo.ValueRank;
            }

            DataTypeCB.SelectedItem = targetType;
            ValueRankCB.SelectedItem = (ValueRankOptions)valueRank;

            if (value.Value == null)
            {
                ValueTB.Text = String.Empty;
                return;
            }

            // check for arrays.
            if (valueRank != ValueRanks.Scalar)
            {
                ValueTB.Text = value.ToString();
                ValueTB.ReadOnly = true;
                return;
            }

            // cast the value to the requested data type.
            ValueTB.Text = (string)TypeInfo.Cast(value.Value, value.TypeInfo, BuiltInType.String);
            ValueTB.ReadOnly = false;
        }

        /// <summary>
        /// Shows or hides the status and timestamp fields.
        /// </summary>
        private void SetShowStatusTimestamp(bool show)
        {
            StatusCodeCB.Visible = show;
            SourceTimestampDP.Visible = show;
            ServerTimestampDP.Visible = show;
            StatusCodeLB.Visible = show;
            SourceTimestampLB.Visible = show;
            ServerTimestampLB.Visible = show;
            StatusCodeCK.Visible = show;
            SourceTimestampCK.Visible = show;
            ServerTimestampCK.Visible = show;
        }
        #endregion

        #region Event Handlers
        private void StatusCodeCK_CheckedChanged(object sender, EventArgs e)
        {
            StatusCodeCB.Enabled = StatusCodeCK.Checked;
        }

        private void SourceTimestampCK_CheckedChanged(object sender, EventArgs e)
        {
            SourceTimestampDP.Enabled = SourceTimestampCK.Checked;
        }

        private void ServerTimestampCK_CheckedChanged(object sender, EventArgs e)
        {
            ServerTimestampDP.Enabled = ServerTimestampCK.Checked;
        }
        #endregion
    }
}
