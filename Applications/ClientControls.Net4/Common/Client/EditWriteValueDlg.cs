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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditWriteValueDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditWriteValueDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            // add the attributes in numerical order.
            foreach (uint attributeId in Attributes.GetIdentifiers())
            {
                AttributeCB.Items.Add(Attributes.GetBrowseName(attributeId));
            }
        }
        #endregion
      
        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit the write request parameters for the set of nodes provided.
        /// </summary>
        public WriteValue ShowDialog(Session session, WriteValue nodeToWrite)
        {
            NodeBTN.Session = session;
            NodeBTN.SelectedReference = null;

            // fill in the control.
            NodeBTN.SelectedNode = nodeToWrite.NodeId;
            AttributeCB.SelectedIndex = (int)nodeToWrite.AttributeId - 1;
            IndexRangeTB.Text = nodeToWrite.IndexRange;
            ValueBTN.Value = nodeToWrite.Value.WrappedValue;

            if (nodeToWrite.Value.StatusCode != StatusCodes.Good)
            {
                StatusCodeTB.Text = (string)TypeInfo.Cast(nodeToWrite.Value.StatusCode, BuiltInType.String);
                StatusCodeCK.Checked = true;
            }

            if (nodeToWrite.Value.SourceTimestamp != DateTime.MinValue)
            {
                SourceTimestampTB.Text = (string)TypeInfo.Cast(nodeToWrite.Value.SourceTimestamp, BuiltInType.String);
                SourceTimestampCK.Checked = true;
            }

            if (nodeToWrite.Value.ServerTimestamp != DateTime.MinValue)
            {
                ServerTimestampTB.Text = (string)TypeInfo.Cast(nodeToWrite.Value.ServerTimestamp, BuiltInType.String);
                ServerTimestampCK.Checked = true;
            }

            if (base.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            // create the result.
            WriteValue result = new WriteValue();

            result.NodeId = NodeBTN.SelectedNode;
            result.AttributeId = (uint)(AttributeCB.SelectedIndex + 1);
            result.ParsedIndexRange = NumericRange.Parse(IndexRangeTB.Text);
            result.Value.WrappedValue = ValueBTN.Value;

            if (StatusCodeCK.Checked)
            {
                result.Value.StatusCode = (StatusCode)TypeInfo.Cast(StatusCodeTB.Text, BuiltInType.StatusCode);
            }

            if (SourceTimestampCK.Checked)
            {
                result.Value.SourceTimestamp = (DateTime)TypeInfo.Cast(SourceTimestampTB.Text, BuiltInType.DateTime);
            }

            if (ServerTimestampCK.Checked)
            {
                result.Value.ServerTimestamp = (DateTime)TypeInfo.Cast(ServerTimestampTB.Text, BuiltInType.DateTime);
            }
            
            if (NumericRange.Empty != result.ParsedIndexRange)
            {
                result.IndexRange = result.ParsedIndexRange.ToString();
            }
            else
            {
                result.IndexRange = String.Empty;
            }

            return result;
        }
        #endregion
        
        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                IndexRangeTB.Text = IndexRangeTB.Text.Trim();

                if (String.IsNullOrEmpty(IndexRangeTB.Text))
                {
                    NumericRange.Parse(IndexRangeTB.Text);
                }

                StatusCodeTB.Text = StatusCodeTB.Text.Trim();

                if (StatusCodeCK.Checked)
                {
                    TypeInfo.Cast(StatusCodeTB.Text, BuiltInType.StatusCode);
                }

                SourceTimestampTB.Text = SourceTimestampTB.Text.Trim();

                if (SourceTimestampCK.Checked)
                {
                    TypeInfo.Cast(SourceTimestampTB.Text, BuiltInType.DateTime);
                }

                ServerTimestampTB.Text = ServerTimestampTB.Text.Trim();

                if (ServerTimestampCK.Checked)
                {
                    TypeInfo.Cast(ServerTimestampTB.Text, BuiltInType.DateTime);
                }

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void StatusCodeCK_CheckedChanged(object sender, EventArgs e)
        {
            StatusCodeTB.Enabled = StatusCodeCK.Checked;
        }

        private void SourceTimestampCK_CheckedChanged(object sender, EventArgs e)
        {
            SourceTimestampTB.Enabled = SourceTimestampCK.Checked;

            if (SourceTimestampTB.Enabled && String.IsNullOrEmpty(SourceTimestampTB.Text))
            {
                SourceTimestampTB.Text = (string)TypeInfo.Cast(DateTime.UtcNow, BuiltInType.String);
            }
        }

        private void ServerTimestampCK_CheckedChanged(object sender, EventArgs e)
        {
            ServerTimestampTB.Enabled = ServerTimestampCK.Checked;

            if (ServerTimestampTB.Enabled && String.IsNullOrEmpty(ServerTimestampTB.Text))
            {
                ServerTimestampTB.Text = (string)TypeInfo.Cast(DateTime.UtcNow, BuiltInType.String);
            }
        }
        #endregion
    }
}
