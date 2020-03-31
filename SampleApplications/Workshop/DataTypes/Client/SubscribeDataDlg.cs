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
using System.Windows.Forms;
using System.Text;
using System.Data;
using Opc.Ua;
using Opc.Ua.Client;

namespace TutorialClient
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SubscribeDataDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SubscribeDataDlg()
        {
            InitializeComponent();
            CreateDataSet();
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private NodeId m_nodeId;
        private Subscription m_subscription;
        private DataSet m_dataset;
        private int m_nextId;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(Session session, NodeId nodeId)
        {
            m_session = session;
            m_nodeId = nodeId;

            Show();
        }

        /// <summary>
        /// Updates the dialog after a reconnect.
        /// </summary>
        public void ReconnectComplete(Session session)
        {
            m_session = session;

            // find the matching subscription.
            foreach (Subscription subscription in m_session.Subscriptions)
            {
                if (Object.ReferenceEquals(subscription.Handle, this))
                {
                    m_subscription = subscription;
                    break;
                }
            }
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Creates the dataset and initializes the view.
        /// </summary>
        private void CreateDataSet()
        {
            DataSet dataset = new DataSet();
            dataset.Tables.Add("Results");

            DataColumn key = dataset.Tables[0].Columns.Add("Index", typeof(int));
            dataset.Tables[0].PrimaryKey = new DataColumn[] { key };

            dataset.Tables[0].Columns.Add("Timestamp", typeof(string));
            dataset.Tables[0].Columns.Add("Value", typeof(string));
            dataset.Tables[0].Columns.Add("StatusCode", typeof(string));
            dataset.Tables[0].Columns.Add("DataType", typeof(string));

            ResultsDV.Columns.Clear();
            ResultsDV.AutoGenerateColumns = false;

            for (int ii = 1; ii < dataset.Tables[0].Columns.Count; ii++)
            {
                string columnName = dataset.Tables[0].Columns[ii].ColumnName;
                ResultsDV.Columns.Add(columnName, columnName);
                ResultsDV.Columns[ResultsDV.Columns.Count - 1].DataPropertyName = columnName;
            }

            dataset.Tables[0].DefaultView.Sort = "Index";

            m_dataset = dataset;
            ResultsDV.DataSource = dataset.Tables[0];
        }

        /// <summary>
        /// Adds a value to the grid.
        /// </summary>
        private void AddValue(DataValue value, ModificationInfo modificationInfo)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = m_nextId++;
            row[1] = value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            row[2] = value.WrappedValue;
            row[3] = new StatusCode(value.StatusCode.Code);

            if (value.WrappedValue.TypeInfo != null)
            {
                row[4] = value.WrappedValue.TypeInfo.BuiltInType.ToString();
            }
            else
            {
                row[4] = String.Empty;
            }

            m_dataset.Tables[0].Rows.Add(row);
        }

        #endregion

        #region Event Handlers
        #endregion
    }
}
