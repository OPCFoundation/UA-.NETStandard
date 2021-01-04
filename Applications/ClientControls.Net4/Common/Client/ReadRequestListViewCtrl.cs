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
    public partial class ReadRequestListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public ReadRequestListViewCtrl()
        {
            InitializeComponent();
            ResultsDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;
            
            m_dataset = new DataSet();
            m_dataset.Tables.Add("Requests");

            m_dataset.Tables[0].Columns.Add("ReadValueId", typeof(ReadValueId));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("NodeName", typeof(string));
            m_dataset.Tables[0].Columns.Add("Attribute", typeof(string));
            m_dataset.Tables[0].Columns.Add("IndexRange", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataEncoding", typeof(QualifiedName));
            m_dataset.Tables[0].Columns.Add("DataValue", typeof(DataValue));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("StatusCode", typeof(StatusCode));
            m_dataset.Tables[0].Columns.Add("SourceTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerTimestamp", typeof(string));

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private Session m_session;
        private bool m_showResults;
        #endregion

        #region Public Members
        /// <summary>
        /// Changes the session used for the read request.
        /// </summary>
        public void ChangeSession(Session session)
        {
            m_session = session;
        }

        /// <summary>
        /// Adds a node to the read request.
        /// </summary>
        public void AddNodes(params ReadValueId[] nodesToRead)
        {
            if (nodesToRead != null)
            {
                for (int ii = 0; ii < nodesToRead.Length; ii++)
                {
                    if (nodesToRead[ii] == null)
                    {
                        continue;
                    }

                    DataRow row = m_dataset.Tables[0].NewRow();
                    UpdateRow(row, nodesToRead[ii]);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
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
                ReadValueId value = (ReadValueId)source.Row[0];
                row.Selected = false;
                nodesToRead.Add(value);
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

            IndexRangeCH.Visible = false;
            DataEncodingCH.Visible = false;
            DataTypeCH.Visible = true;
            ValueCH.Visible = true;
            StatusCodeCH.Visible = true;
            SourceTimestampCH.Visible = true;
            ServerTimestampCH.Visible = true;
            m_showResults = true;

            // add the results to the display.
            for (int ii = 0; ii < results.Count; ii++)
            {
                DataRowView source = ResultsDV.Rows[ii].DataBoundItem as DataRowView;
                UpdateRow(source.Row, results[ii]);
            }
        }

        /// <summary>
        /// Returns the grid to edit ReadValueIds state.
        /// </summary>
        public void Back()
        {
            IndexRangeCH.Visible = true;
            DataEncodingCH.Visible = true;
            DataTypeCH.Visible = false;
            ValueCH.Visible = false;
            StatusCodeCH.Visible = false;
            SourceTimestampCH.Visible = false;
            ServerTimestampCH.Visible = false;
            m_showResults = false;

            // clear any selection.
            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                row.Selected = false;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the row with the node to read.
        /// </summary>
        public void UpdateRow(DataRow row, DataValue value)
        {
            row[6] = value;
            row[7] = (value.WrappedValue.TypeInfo != null) ? value.WrappedValue.TypeInfo.ToString() : String.Empty;
            row[8] = value.WrappedValue;
            row[9] = value.StatusCode;
            row[10] = (value.SourceTimestamp != DateTime.MinValue) ? Utils.Format("{0:hh:mm:ss.fff}", value.SourceTimestamp.ToLocalTime()) : String.Empty;
            row[11] = (value.ServerTimestamp != DateTime.MinValue) ? Utils.Format("{0:hh:mm:ss.fff}", value.ServerTimestamp.ToLocalTime()) : String.Empty;
        }

        /// <summary>
        /// Updates the row with the node to read.
        /// </summary>
        public void UpdateRow(DataRow row, ReadValueId nodeToRead)
        {
            row[0] = nodeToRead;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(nodeToRead.AttributeId, null)];
            row[2] = (m_session != null) ? m_session.NodeCache.GetDisplayText(nodeToRead.NodeId) : Utils.ToString(nodeToRead.NodeId);
            row[3] = Attributes.GetBrowseName(nodeToRead.AttributeId);
            row[4] = nodeToRead.IndexRange;
            row[5] = (nodeToRead.DataEncoding != null) ? nodeToRead.DataEncoding : QualifiedName.Null;
        }
        #endregion

        #region Event Handlers
        private void PopupMenu_Opening(object sender, CancelEventArgs e)
        {
            NewMI.Visible = !m_showResults;
            EditMI.Enabled = ResultsDV.SelectedRows.Count > 0;
            DeleteMI.Enabled = ResultsDV.SelectedRows.Count > 0;
            DeleteMI.Visible = !m_showResults;
        }

        private void NewMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_showResults)
                {
                    ReadValueId nodeToRead = null;

                    // use the first selected row as a template.
                    foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                    {
                        DataRowView source = row.DataBoundItem as DataRowView;
                        ReadValueId value = (ReadValueId)source.Row[0];
                        nodeToRead = (ReadValueId)value.MemberwiseClone();
                        break;
                    }

                    if (nodeToRead == null)
                    {
                        nodeToRead = new ReadValueId() { AttributeId = Attributes.Value };
                    }

                    // edit the parameters.
                    ReadValueId[] results = new EditReadValueIdDlg().ShowDialog(m_session, nodeToRead);

                    if (results != null)
                    {
                        // add the new rows.
                        for (int ii = 0; ii < results.Length; ii++)
                        {
                            DataRow row = m_dataset.Tables[0].NewRow();
                            UpdateRow(row, results[ii]);
                            m_dataset.Tables[0].Rows.Add(row);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void EditMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_showResults)
                {
                    foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                    {
                        DataRowView source = row.DataBoundItem as DataRowView;
                        ReadValueId nodeToRead = (ReadValueId)source.Row[0];
                        DataValue value = (DataValue)source.Row[6];

                        new EditComplexValueDlg().ShowDialog(
                            m_session,
                            null,
                            0,
                            null, 
                            value,
                            true,
                            "View Read Result");

                        break;
                    }
                }
                else
                {
                    List<ReadValueId> nodesToRead = new List<ReadValueId>();

                    foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                    {
                        DataRowView source = row.DataBoundItem as DataRowView;
                        ReadValueId value = (ReadValueId)source.Row[0];
                        nodesToRead.Add(value);
                    }

                    ReadValueId[] results = new EditReadValueIdDlg().ShowDialog(m_session, nodesToRead.ToArray());

                    if (results != null)
                    {
                        for (int ii = 0; ii < results.Length; ii++)
                        {
                            DataRowView source = ResultsDV.SelectedRows[ii].DataBoundItem as DataRowView;
                            UpdateRow(source.Row, results[ii]);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (!m_showResults)
                {
                    foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                    {
                        DataRowView source = row.DataBoundItem as DataRowView;
                        source.Row.Delete();
                    }

                    m_dataset.AcceptChanges();
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
