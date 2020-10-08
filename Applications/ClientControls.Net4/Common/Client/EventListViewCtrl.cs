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
    public partial class EventListViewCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public EventListViewCtrl()
        {
            InitializeComponent();
            EventsDV.AutoGenerateColumns = true;
            ImageList = new ClientUtils().ImageList;
        }
        #endregion

        #region Private Fields
        private DataSet m_dataset;
        private Session m_session;
        private FilterDeclaration m_filter;
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
            m_filter = filter;
            m_dataset = new DataSet();
            m_dataset.Tables.Add("Events");
            m_dataset.Tables[0].Columns.Add("Event", typeof(VariantCollection));

            if (m_filter != null)
            {
                foreach (FilterDeclarationField field in m_filter.Fields)
                {
                    if (field.DisplayInList)
                    {
                        m_dataset.Tables[0].Columns.Add(field.InstanceDeclaration.DisplayName, typeof(string));
                    }
                }
            }
            
            EventsDV.DataSource = m_dataset.Tables[0];
        }

        /// <summary>
        /// Displays the event.
        /// </summary>
        public void DisplayEvent(EventFieldList e)
        {
            if (e != null)
            {
                DisplayEvent(e.EventFields);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets the filter to edit.
        /// </summary>
        public void DisplayEvent(VariantCollection fields)
        {
            if (m_filter != null)
            {
                int index = 0;

                DataRow row = m_dataset.Tables[0].NewRow();
                row[index++] = fields;

                for (int ii = 0; ii < m_filter.Fields.Count; ii++)
                {
                    if (m_filter.Fields[ii].DisplayInList)
                    {
                        if (ii < fields.Count - 1)
                        {
                            // increment because the first field is always the event NodeId when using FilterDeclarations to create EventFilters.
                            row[index] = fields[ii + 1].ToString();
                        }

                        index++;
                    }
                }

                m_dataset.Tables[0].Rows.Add(row);
            }
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private void ReadHistory(ReadEventDetails details, NodeId areaId)
        {
            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = areaId;
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Neither,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            HistoryEvent events = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;
            
            foreach (HistoryEventFieldList e in events.Events)
            {
                DisplayEvent(e.EventFields);
            }

            // release continuation points.
            if (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                m_session.HistoryRead(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Neither,
                    true,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);
            }
        }

        /// <summary>
        /// Deletes the recent history.
        /// </summary>
        private void DeleteHistory(NodeId areaId, List<VariantCollection> events, FilterDeclaration filter)
        {
            // find the event id.
            int index = 0;

            foreach (FilterDeclarationField field in filter.Fields)
            {
                if (field.InstanceDeclaration.BrowseName == Opc.Ua.BrowseNames.EventId)
                {
                    break;
                }

                index++;
            }

            // can't delete events if no event id.
            if (index >= filter.Fields.Count)
            {
                throw ServiceResultException.Create(StatusCodes.BadEventIdUnknown, "Cannot delete events if EventId was not selected.");
            }

            // build list of nodes to delete.
            DeleteEventDetails details = new DeleteEventDetails();
            details.NodeId = areaId;

            foreach (VariantCollection e in events)
            {
                byte[] eventId = null;

                if (e.Count > index)
                {
                    eventId = e[index].Value as byte[];
                }

                details.EventIds.Add(eventId);
            }

            // delete the events.
            ExtensionObjectCollection nodesToUpdate = new ExtensionObjectCollection();
            nodesToUpdate.Add(new ExtensionObject(details));

            HistoryUpdateResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryUpdate(
                null,
                nodesToUpdate,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToUpdate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToUpdate);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            // check for item level errors.
            if (results[0].OperationResults.Count > 0)
            {
                int count = 0;

                for (int ii = 0; ii < results[0].OperationResults.Count; ii++)
                {
                    if (StatusCode.IsBad(results[0].OperationResults[ii]))
                    {
                        count++;
                    }
                }

                // raise an error.
                if (count > 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEventIdUnknown, 
                        "Error deleting events. Only {0} of {1} deletes succeeded.",
                        events.Count - count,
                        events.Count);
                }
            }
        }
        #endregion

        #region Event Handlers
        private void EventsDV_ColumnAdded(object sender, DataGridViewColumnEventArgs e)
        {
            if (e.Column.Index == 0)
            {
                EventsDV.Columns[0].Visible = false;
            }

            if (EventsDV.Columns.Count > 1)
            {
                EventsDV.Columns[EventsDV.Columns.Count-1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
        }

        private void DetailsMI_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in EventsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    EventFieldList e2 = (EventFieldList)source.Row[0];
                    new ViewEventDetailsDlg().ShowDialog(m_filter, e2.EventFields);
                    break;
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
                foreach (DataGridViewRow row in EventsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    source.Row.Delete();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ClearMI_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.Tables[0].Rows.Clear();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
