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
    public partial class EventFilterListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public EventFilterListViewCtrl()
        {
            InitializeComponent();
            FilterDV.AutoGenerateColumns = false;
            ImageList = new ClientUtils().ImageList;
            
            m_dataset = new DataSet();
            m_dataset.Tables.Add("Events");

            m_dataset.Tables[0].Columns.Add("Field", typeof(FilterDeclarationField));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("BrowsePath", typeof(string));
            m_dataset.Tables[0].Columns.Add("SelectField", typeof(bool));
            m_dataset.Tables[0].Columns.Add("DisplayInList", typeof(bool));
            m_dataset.Tables[0].Columns.Add("FilterEnabled", typeof(bool));
            m_dataset.Tables[0].Columns.Add("FilterOperator", typeof(FilterOperator));
            m_dataset.Tables[0].Columns.Add("FilterValue", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("Index", typeof(int));

            m_dataset.Tables[0].DefaultView.Sort = "Index";

            FilterDV.DataSource = m_dataset.Tables[0].DefaultView;

        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private Session m_session;
        private int m_counter;
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
        /// Sets the filter to edit.
        /// </summary>
        public void SetFilter(FilterDeclaration filter)
        {
            m_dataset.Tables[0].Rows.Clear();

            if (filter != null)
            {
                foreach (FilterDeclarationField field in filter.Fields)
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    UpdateRow(row, field);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the row.
        /// </summary>
        public void UpdateRow(DataRow row, FilterDeclarationField field)
        {
            row[0] = field;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(m_session, field.InstanceDeclaration.NodeClass, field.InstanceDeclaration.RootTypeId, false)];
            row[2] = field.InstanceDeclaration.BrowsePathDisplayText;
            row[3] = field.Selected;
            row[4] = field.DisplayInList;
            row[5] = field.FilterEnabled;

            if (field.FilterEnabled)
            {
                row[6] = field.FilterOperator;
                row[7] = field.FilterValue;
            }

            row[8] = m_counter++;
        }
        #endregion

        #region Event Handlers
        private void FilterDV_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex == -1)
                {
                    return;
                }

                DataRowView source = FilterDV.Rows[e.RowIndex].DataBoundItem as DataRowView;
                FilterDeclarationField field = (FilterDeclarationField)source.Row[0];

                if (e.ColumnIndex == 5)
                {
                    FilterOperator filterOperator = field.FilterOperator;

                    if (new SetFilterOperatorDlg().ShowDialog(ref filterOperator))
                    {
                        field.FilterEnabled = true;
                        source.Row[5] = field.FilterEnabled;

                        field.FilterOperator = filterOperator;
                        source.Row[6] = field.FilterOperator;
                    }

                    return;
                }

                if (e.ColumnIndex == 6)
                {
                    if (field.FilterOperator == FilterOperator.IsNull)
                    {
                        field.FilterValue = Variant.Null;
                        return;
                    }

                    InstanceDeclaration declaration = field.InstanceDeclaration;

                    object result = new EditComplexValueDlg().ShowDialog(
                        m_session, 
                        declaration.DisplayName,
                        declaration.DataType, 
                        declaration.ValueRank, 
                        field.FilterValue.Value, 
                        "Edit Filter Value");

                    if (result != null)
                    {
                        field.FilterEnabled = true;
                        source.Row[5] = field.FilterEnabled;
                        source.Row[6] = field.FilterOperator;

                        field.FilterValue = new Variant(result);
                        source.Row[7] = field.FilterValue;
                    }

                    return;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void FilterDV_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex == -1)
                {
                    return;
                }

                DataRowView source = FilterDV.Rows[e.RowIndex].DataBoundItem as DataRowView;
                FilterDeclarationField field = (FilterDeclarationField)source.Row[0];

                if (e.ColumnIndex == 2)
                {
                    field.Selected = !field.Selected;
                    source.Row[3] = field.Selected;
                    return;
                }

                if (e.ColumnIndex == 3)
                {
                    field.DisplayInList = !field.DisplayInList;
                    source.Row[4] = field.DisplayInList;
                    return;
                }

                if (e.ColumnIndex == 4)
                {
                    field.FilterEnabled = !field.FilterEnabled;
                    source.Row[5] = field.FilterEnabled;
                    return;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void MoveUpMI_Click(object sender, EventArgs e)
        {
            try
            {
                // need to sort the rows by index.
                List<DataGridViewRow> rows = new List<DataGridViewRow>();

                foreach (DataGridViewRow row in FilterDV.SelectedRows)
                {
                    bool inserted = false;

                    for (int ii = 0; ii < rows.Count; ii++)
                    {
                        if (rows[ii].Index > row.Index)
                        {
                            rows.Insert(ii, row);
                            inserted = true;
                            break;
                        }
                    }

                    if (!inserted)
                    {
                        rows.Add(row);
                    }
                }

                // move all of the rows up one.
                for (int ii = 0; ii < rows.Count; ii++)
                {
                    DataRowView source = FilterDV.Rows[rows[ii].Index].DataBoundItem as DataRowView;

                    if (rows[ii].Index > 0)
                    {
                        DataRowView target = FilterDV.Rows[rows[ii].Index-1].DataBoundItem as DataRowView;
                        int index = (int)target.Row[8];
                        target.Row[8] = source.Row[8];
                        source.Row[8] = index;
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void FilterDV_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            try
            {
                if (e.ColumnIndex == 2)
                {
                    bool state = false;

                    if (m_dataset.Tables[0].DefaultView.Count > 0)
                    {
                        state = (bool)m_dataset.Tables[0].DefaultView[0].Row[3];
                    }

                    state = !state;

                    foreach (DataRowView row in m_dataset.Tables[0].DefaultView)
                    {
                        FilterDeclarationField field = (FilterDeclarationField)row.Row[0];                        
                        row.Row[3] = field.Selected = state;
                    }
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
