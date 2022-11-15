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
using System.Data;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class ViewNodeStateDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public ViewNodeStateDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            ResultsDV.AutoGenerateColumns = false;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Results");

            m_dataset.Tables[0].Columns.Add("Index", typeof(int));
            m_dataset.Tables[0].Columns.Add("BrowsePath", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));

            m_dataset.Tables[0].DefaultView.Sort = "Index";

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private DataSet m_dataset;
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit a value.
        /// </summary>
        public bool ShowDialog(Session session, NodeState node, string caption)
        {
            m_session = session;

            if (caption != null)
            {
                this.Text = caption;
            }

            PopulateDataView(m_session.SystemContext, node, String.Empty);
            m_dataset.AcceptChanges();

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Recursively populates the data view.
        /// </summary>
        private void PopulateDataView(
            ISystemContext context,
            NodeState parent,
            string parentPath)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                StringBuilder childPath = new StringBuilder();

                if (!String.IsNullOrEmpty(parentPath))
                {
                    childPath.Append(parentPath);
                    childPath.Append("/");
                }

                childPath.Append(child.GetDisplayText());

                if (child.NodeClass == NodeClass.Variable)
                {
                    BaseVariableState variable = (BaseVariableState)child;

                    if (StatusCode.IsGood(variable.StatusCode))
                    {
                        string dataType = m_session.NodeCache.GetDisplayText(variable.DataType);

                        if (variable.ValueRank >= 0)
                        {
                            dataType += "[]"; 
                        }

                        DataRow row = m_dataset.Tables[0].NewRow();
                        row[0] = m_dataset.Tables[0].Rows.Count;
                        row[1] = childPath.ToString();
                        row[2] = dataType;
                        row[3] = variable.WrappedValue;
                        m_dataset.Tables[0].Rows.Add(row);
                    }
                }

                PopulateDataView(context, child, childPath.ToString());
            }
        }
        #endregion
        
        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
