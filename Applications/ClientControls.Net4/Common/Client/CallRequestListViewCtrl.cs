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
    public partial class CallRequestListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public CallRequestListViewCtrl()
        {
            InitializeComponent();
            ResultsDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;
            
            m_dataset = new DataSet();
            m_dataset.Tables.Add("Arguments");

            m_dataset.Tables[0].Columns.Add("Argument", typeof(Argument));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("Name", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("Result", typeof(string));

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private Session m_session;
        private NodeId m_objectId;
        private NodeId m_methodId;
        private Argument[] m_inputArguments;
        private Argument[] m_outputArguments;
        #endregion

        #region Public Members
        /// <summary>
        /// Changes the session used for the call request.
        /// </summary>
        public void ChangeSession(Session session)
        {
            m_session = session;
        }

        /// <summary>
        /// Sets the method for the call request.
        /// </summary>
        public void SetMethod(NodeId objectId, NodeId methodId)
        {
            if (objectId == null)
            {
                new ArgumentNullException("objectId");
            }

            if (methodId == null)
            {
                new ArgumentNullException("methodId");
            }

            m_objectId = objectId;
            m_methodId = methodId;

            ReadArguments(methodId);
            DisplayInputArguments();
        }

        /// <summary>
        /// Calls the method.
        /// </summary>
        public void Call()
        {
            // build list of methods to call.
            CallMethodRequestCollection methodsToCall = new CallMethodRequestCollection();

            CallMethodRequest methodToCall = new CallMethodRequest();

            methodToCall.ObjectId = m_objectId;
            methodToCall.MethodId = m_methodId;

            foreach (DataRow row in m_dataset.Tables[0].Rows)
            {
                Argument argument = (Argument)row[0];
                Variant value = (Variant)row[4];
                argument.Value = value.Value;
                methodToCall.InputArguments.Add(value);
            }

            methodsToCall.Add(methodToCall);

            // call the method.
            CallMethodResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = m_session.Call(
                null,
                methodsToCall,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, methodsToCall);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);
            
            for (int ii = 0; ii < results.Count; ii++)
            {
                // display any input argument errors.
                if (results[ii].InputArgumentResults != null)
                {
                    for (int jj = 0; jj < results[ii].InputArgumentResults.Count; jj++)
                    {
                        if (StatusCode.IsBad(results[ii].InputArgumentResults[jj]))
                        {
                            DataRow row = m_dataset.Tables[0].Rows[jj];
                            row[5] = results[ii].InputArgumentResults[jj].ToString();
                            ResultCH.Visible = true;
                        }
                    }
                }

                // throw an exception on error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    throw ServiceResultException.Create(results[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                }

                // display the output arguments
                ResultCH.Visible = false;
                NoArgumentsLB.Visible = m_outputArguments == null || m_outputArguments.Length == 0;
                NoArgumentsLB.Text = "Method invoked successfully.\r\nNo output arguments to display.";
                m_dataset.Tables[0].Rows.Clear();

                if (m_outputArguments != null)
                {
                    for (int jj = 0; jj < m_outputArguments.Length; jj++)
                    {
                        DataRow row = m_dataset.Tables[0].NewRow();

                        if (results[ii].OutputArguments.Count > jj)
                        {
                            UpdateRow(row, m_outputArguments[jj], results[ii].OutputArguments[jj], true);
                        }
                        else
                        {
                            UpdateRow(row, m_outputArguments[jj], Variant.Null, true);
                        }
                        
                        m_dataset.Tables[0].Rows.Add(row);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the grid to the enter input arguments state.
        /// </summary>
        public void Back()
        {
            DisplayInputArguments();

            // clear any selection.
            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                row.Selected = false;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Displays the input arguments.
        /// </summary>
        private void DisplayInputArguments()
        {
            ResultCH.Visible = false;
            NoArgumentsLB.Visible = m_inputArguments == null || m_inputArguments.Length == 0;
            NoArgumentsLB.Text = "No input arguments to display.";

            m_dataset.Tables[0].Rows.Clear();
            
            if (m_inputArguments != null)
            {
                foreach (Argument argument in m_inputArguments)
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    UpdateRow(row, argument, new Variant(argument.Value), false);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
        }

        /// <summary>
        /// Updates the row with an argument and its value.
        /// </summary>
        private void UpdateRow(DataRow row, Argument argument, Variant value, bool isOutputArgument)
        {
            string dataType = m_session.NodeCache.GetDisplayText(argument.DataType);

            if (argument.ValueRank >= 0)
            {
                dataType += "[]";
            }

            row[0] = argument;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(isOutputArgument, value.Value)];
            row[2] = argument.Name;
            row[3] = dataType;
            row[4] = value;
            row[5] = String.Empty;
        }

        /// <summary>
        /// Reads the arguments for the method.
        /// </summary>
        private void ReadArguments(NodeId nodeId)
        {
            m_inputArguments = null;
            m_outputArguments = null;

            // build list of references to browse.
            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)NodeClass.Variable;
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.BrowseName;

            nodesToBrowse.Add(nodeToBrowse);

            // find properties.
            ReferenceDescriptionCollection references = ClientUtils.Browse(m_session, null, nodesToBrowse, false);

            // build list of properties to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; references != null && ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                // ignore out of server references.
                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                // ignore other properties.
                if (reference.BrowseName != Opc.Ua.BrowseNames.InputArguments && reference.BrowseName != Opc.Ua.BrowseNames.OutputArguments)
                {
                    continue;
                }

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = (NodeId)reference.NodeId;
                nodeToRead.AttributeId = Attributes.Value;
                nodeToRead.Handle = reference;
                nodesToRead.Add(nodeToRead);
            }

            // method has no arguments.
            if (nodesToRead.Count == 0)
            {
                return;
            }

            // read the arguments.
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

            // save the results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                ReferenceDescription reference = (ReferenceDescription)nodesToRead[ii].Handle;

                if (StatusCode.IsGood(results[ii].StatusCode))
                {
                    if (reference.BrowseName == Opc.Ua.BrowseNames.InputArguments)
                    {
                        m_inputArguments = (Argument[])ExtensionObject.ToArray(results[ii].GetValue<ExtensionObject[]>(null), typeof(Argument));
                    }

                    if (reference.BrowseName == Opc.Ua.BrowseNames.OutputArguments)
                    {
                        m_outputArguments = (Argument[])ExtensionObject.ToArray(results[ii].GetValue<ExtensionObject[]>(null), typeof(Argument));
                    }
                }
            }

            // set default values for input arguments.
            if (m_inputArguments != null)
            {
                foreach (Argument argument in m_inputArguments)
                {
                    argument.Value = TypeInfo.GetDefaultValue(argument.DataType, argument.ValueRank, m_session.TypeTree);
                }
            }
        }
        #endregion

        #region Event Handlers
        private void EditMI_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    Argument argument = (Argument)source.Row[0];

                    BuiltInType builtInType = TypeInfo.GetBuiltInType(argument.DataType, m_session.TypeTree);

                    object result = new EditComplexValueDlg().ShowDialog(
                        new TypeInfo(builtInType, argument.ValueRank),
                        argument.Name,
                        argument.Value,
                        "Edit Input Argument");

                    if (result != null)
                    {
                        argument.Value = result;
                        UpdateRow(source.Row, argument, new Variant(result), false);
                    }

                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
