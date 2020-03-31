/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.HistoricalAccess.Client
{
    /// <summary>
    /// Prompts the user to specify a new value and then writes it to the server.
    /// </summary>
    public partial class WriteValueDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public WriteValueDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private NodeId m_nodeId;
        private uint m_attributeId;
        private DataValue m_value;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Prompts the user to enter a value to write.
        /// </summary>
        /// <param name="session">The session to use.</param>
        /// <param name="nodeId">The identifier for the node to write to.</param>
        /// <param name="attributeId">The attribute being written.</param>
        /// <returns>True if successful. False if the operation was cancelled.</returns>
        public bool ShowDialog(Session session, NodeId nodeId, uint attributeId)
        {
            m_session = session;
            m_nodeId  = nodeId;
            m_attributeId = attributeId;

            ReadValueId nodeToRead = new ReadValueId();
            nodeToRead.NodeId = nodeId;
            nodeToRead.AttributeId = attributeId;

            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
            nodesToRead.Add(nodeToRead);
            
            // read current value.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            
            m_value = results[0];
            ValueTB.Text = Utils.Format("{0}", m_value.Value);
            
            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Changes the value in the text box to the data type required for the write operation.
        /// </summary>
        /// <returns>A value with the correct type.</returns>
        private object ChangeType()
        {
            object value = m_value.Value;
                
            switch (m_value.WrappedValue.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                {
                    value = Convert.ToBoolean(ValueTB.Text);
                    break;
                }

                case BuiltInType.SByte:
                {
                    value = Convert.ToSByte(ValueTB.Text);
                    break;
                }

                case BuiltInType.Byte:
                {
                    value = Convert.ToByte(ValueTB.Text);
                    break;
                }

                case BuiltInType.Int16:
                {
                    value = Convert.ToInt16(ValueTB.Text);
                    break;
                }

                case BuiltInType.UInt16:
                {
                    value = Convert.ToUInt16(ValueTB.Text);
                    break;
                }

                case BuiltInType.Int32:
                {
                    value = Convert.ToInt32(ValueTB.Text);
                    break;
                }

                case BuiltInType.UInt32:
                {
                    value = Convert.ToUInt32(ValueTB.Text);
                    break;
                }

                case BuiltInType.Int64:
                {
                    value = Convert.ToInt64(ValueTB.Text);
                    break;
                }

                case BuiltInType.UInt64:
                {
                    value = Convert.ToUInt64(ValueTB.Text);
                    break;
                }

                case BuiltInType.Float:
                {
                    value = Convert.ToSingle(ValueTB.Text);
                    break;
                }

                case BuiltInType.Double:
                {
                    value = Convert.ToDouble(ValueTB.Text);
                    break;
                }

                default:
                {
                    value = ValueTB.Text;
                    break;
                }
            }

            return value;
        }
        #endregion
                
        #region Event Handlers
        /// <summary>
        /// Parses the value and writes it to server. Closes the dialog if successful.
        /// </summary>
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {                
                WriteValue valueToWrite = new WriteValue();

                valueToWrite.NodeId = m_nodeId;
                valueToWrite.AttributeId = m_attributeId;
                valueToWrite.Value.Value = ChangeType();
                valueToWrite.Value.StatusCode = StatusCodes.Good;
                valueToWrite.Value.ServerTimestamp = DateTime.MinValue;
                valueToWrite.Value.SourceTimestamp = DateTime.MinValue;
            
                WriteValueCollection valuesToWrite = new WriteValueCollection();
                valuesToWrite.Add(valueToWrite);

                // write current value.
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.Write(
                    null,
                    valuesToWrite,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, valuesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);
            
                if (StatusCode.IsBad(results[0]))
                {
                    throw new ServiceResultException(results[0]);
                }

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException("Error Writing Value", exception);
            }
        }
        #endregion
    }
}
