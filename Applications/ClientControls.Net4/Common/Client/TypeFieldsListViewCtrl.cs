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
    public partial class TypeFieldsListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public TypeFieldsListViewCtrl()
        {
            InitializeComponent();
            ResultsDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;
            
            m_dataset = new DataSet();
            m_dataset.Tables.Add("Requests");

            m_dataset.Tables[0].Columns.Add("InstanceDeclaration", typeof(InstanceDeclaration));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("BrowsePath", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Description", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private Session m_session;
        private List<InstanceDeclaration> m_declarations;
        #endregion

        #region Public Members
        /// <summary>
        /// Changes the session used by the control.
        /// </summary>
        public void ChangeSession(Session session)
        {
            m_session = session;
        }

        /// <summary>
        /// Displays the components of the type in the control.
        /// </summary>
        public void ShowType(NodeId typeId)
        {
            if (NodeId.IsNull(typeId))
            {
                m_dataset.Tables[0].Rows.Clear();
                return;
            }

            m_declarations = ClientUtils.CollectInstanceDeclarationsForType(m_session, typeId, false);

            // update existing rows.
            for (int ii = 0; ii < m_declarations.Count && ii < m_dataset.Tables[0].Rows.Count; ii++)
            {
                if (m_declarations[ii].NodeClass == NodeClass.Method)
                {
                    continue;
                }

                UpdateRow(m_dataset.Tables[0].Rows[ii], m_declarations[ii]);
            }

            // add new rows.
            for (int ii = m_dataset.Tables[0].Rows.Count; ii < m_declarations.Count; ii++)
            {
                if (m_declarations[ii].NodeClass == NodeClass.Method)
                {
                    continue;
                }

                DataRow row = m_dataset.Tables[0].NewRow();
                UpdateRow(row, m_declarations[ii]);
                m_dataset.Tables[0].Rows.Add(row);
            }

            // delete unused rows.
            for (int ii = m_declarations.Count; ii < m_dataset.Tables[0].Rows.Count; ii++)
            {
                m_dataset.Tables[0].Rows[ii].Delete();
            }

            // deselect all rows.
            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                row.Selected = false;
            }
        }

        /// <summary>
        /// Displays the current filter components in the control.
        /// </summary>
        public void ShowFilter(FilterDeclaration filter)
        {
            m_declarations = new List<InstanceDeclaration>();

            foreach (FilterDeclarationField declaration in filter.Fields)
            {
                DataRow row = m_dataset.Tables[0].NewRow();
                UpdateRow(row, declaration.InstanceDeclaration);
                m_dataset.Tables[0].Rows.Add(row);
                m_declarations.Add(declaration.InstanceDeclaration);
            }
        }

        /// <summary>
        /// Updates the row with the node to read.
        /// </summary>
        public void UpdateRow(DataRow row, InstanceDeclaration declaration)
        {
            row[0] = declaration;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex((declaration.NodeClass == NodeClass.Variable) ? Attributes.Value : Attributes.NodeId, null)];
            row[2] = declaration.DisplayPath;
            row[3] = declaration.DataTypeDisplayText;
            row[4] = declaration.Description;
        }

        /// <summary>
        /// Reads the values displayed in the control and moves to the display results state.
        /// </summary>
        public void Read()
        {
            if (m_session == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotConnected);
            }

            // build list of values to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                DataRowView source = row.DataBoundItem as DataRowView;
                InstanceDeclaration value = (InstanceDeclaration)source.Row[0];
                row.Selected = false;

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = value.NodeId;
                nodeToRead.AttributeId = (value.NodeClass == NodeClass.Variable) ? Attributes.Value : Attributes.NodeId;
                nodesToRead.Add(nodeToRead);
            }
            
            // read the values.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            DescriptionCH.Visible = false;
            ValueCH.Visible = true;

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                DataRowView source = ResultsDV.Rows[ii].DataBoundItem as DataRowView;
                UpdateRow(source.Row, results[ii]);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the row with the node to read.
        /// </summary>
        public void UpdateRow(DataRow row, DataValue value)
        {
            row[5] = value.WrappedValue;
        }
        #endregion

        #region Event Handlers
        #endregion
    }
}
