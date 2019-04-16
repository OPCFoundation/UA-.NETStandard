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
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// Displays a 
    /// </summary>
    public partial class ReadEventHistoryDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadHistoryDlg"/> class.
        /// </summary>
        public ReadEventHistoryDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeId m_areaId;
        private FilterDeclaration m_filter;
        private ReadEventDetails m_details;
        private HistoryReadValueId m_nodeToRead;
        #endregion

        #region Public Members
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(Session session, NodeId areaId, FilterDeclaration filter)
        {
            m_session = session;
            m_areaId = areaId;
            m_filter = filter;

            EventAreaTB.Text = session.NodeCache.GetDisplayText(m_areaId);
            EventTypeTB.Text = session.NodeCache.GetDisplayText(filter.EventTypeId);
            EventFilterTB.Text = GetFilterFields(m_filter);

            ResultsLV.IsSubscribed = false;
            ResultsLV.ChangeSession(session, false);
            ResultsLV.ChangeArea(areaId, false);
            ResultsLV.ChangeFilter(filter, false);
            
            // get the beginning of data.
            DateTime startTime;

            try
            {
                startTime = ReadFirstDate().ToLocalTime(); 
            }
            catch (Exception)
            {
                startTime = new DateTime(2000, 1, 1);
            }
            
            StartTimeDP.Value = startTime;
            StartTimeCK.Checked = true;
            EndTimeDP.Value = DateTime.Now;
            EndTimeCK.Checked = true;
            MaxReturnValuesNP.Value = 10;
            MaxReturnValuesCK.Checked = true;
            GoBTN.Visible = true;
            NextBTN.Visible = false;
            StopBTN.Enabled = false;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }
                       
            return true;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the filter fields formatted as a string.
        /// </summary>
        private string GetFilterFields(FilterDeclaration filter)
        {
            StringBuilder buffer = new StringBuilder();

            foreach (FilterDeclarationField field in filter.Fields)
            {
                if (field.FilterEnabled)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(field.InstanceDeclaration.DisplayName);
                }
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Releases any continuation points.
        /// </summary>
        private void ReleaseContinuationPoints()
        {
            if (m_details != null)
            {
                HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
                nodesToRead.Add(m_nodeToRead);

                HistoryReadResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.HistoryRead(
                    null,
                    new ExtensionObject(m_details),
                    TimestampsToReturn.Neither,
                    true,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                Session.ValidateResponse(results, nodesToRead);
                Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                NextBTN.Visible = false;
                StopBTN.Enabled = false;
                GoBTN.Visible = true;
                m_details = null;
                m_nodeToRead = null;
            }
        }

        /// <summary>
        /// Returns the UTC timestamp of the first event in the archive.
        /// </summary>
        private DateTime ReadFirstDate()
        {
            // read the time of the first event in the archive.
            ReadEventDetails details = new ReadEventDetails();
            details.StartTime = new DateTime(1970, 1, 1);
            details.EndTime = DateTime.MinValue;
            details.NumValuesPerNode = 1;
            details.Filter = new EventFilter();
            details.Filter.AddSelectClause(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.Time);

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_areaId;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
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

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            // get the data.
            HistoryEvent data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;

            // release the continuation point.
            if (results[0].ContinuationPoint != null)
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

                Session.ValidateResponse(results, nodesToRead);
                Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            }

            // check if an event found.
            if (data == null || data.Events.Count == 0 || data.Events[0].EventFields.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNoDataAvailable);
            }

            // get the event time.
            DateTime? eventTime = data.Events[0].EventFields[0].Value as DateTime?;

            if (eventTime == null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }

            // return time as UTC value.
            return eventTime.Value;
        }

        /// <summary>
        /// Starts a new read operation.
        /// </summary>
        private void ReadFirst()
        {
            ResultsLV.ClearEventHistory();

            // set up the request parameters.
            ReadEventDetails details = new ReadEventDetails();
            details.StartTime = DateTime.MinValue;
            details.EndTime = DateTime.MinValue;
            details.NumValuesPerNode = 0;
            details.Filter = m_filter.GetFilter();

            if (StartTimeCK.Checked)
            {
                details.StartTime = StartTimeDP.Value.ToUniversalTime();
            }

            if (EndTimeCK.Checked)
            {
                details.EndTime = EndTimeDP.Value.ToUniversalTime();
            }

            if (MaxReturnValuesCK.Checked)
            {
                details.NumValuesPerNode = (uint)MaxReturnValuesNP.Value;
            }
            
            // read the events from the server.
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_areaId;

            ReadNext(details, nodeToRead);
        }

        /// <summary>
        /// Continues a read operation.
        /// </summary>
        private void ReadNext(ReadEventDetails details, HistoryReadValueId nodeToRead)
        {
            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Neither,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
            }
            
            // display results.
            HistoryEvent data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;
            ResultsLV.AddEventHistory(data);

            // check if a continuation point exists.
            if (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                NextBTN.Visible = true;
                StopBTN.Enabled = true;
                GoBTN.Visible = false;
                m_details = details;
                m_nodeToRead = nodeToRead;
            }

            // all done.
            else
            {
                NextBTN.Visible = false;
                StopBTN.Enabled = false;
                GoBTN.Visible = true;
                m_details = null;
                m_nodeToRead = null;
            }
        }
        #endregion

        #region Event Handlers
        private void GoBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_details == null)
                {
                    ReadFirst();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException("Error Reading History", exception);
            }
        }

        private void NextBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_details != null)
                {
                    ReadNext(m_details, m_nodeToRead);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException("Error Reading History", exception);
            }
        }

        private void StopBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ReleaseContinuationPoints();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException("Error Reading History", exception);
            }
        }

        private void ReadTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ReleaseContinuationPoints();
            }
            catch (Exception)
            {
                // ignore is ok.
            }
        }

        private void StartTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            StartTimeDP.Enabled = StartTimeCK.Checked;
        }

        private void EndTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            EndTimeDP.Enabled = EndTimeCK.Checked;
        }

        private void MaxReturnValuesCK_CheckedChanged(object sender, EventArgs e)
        {
            MaxReturnValuesNP.Enabled = MaxReturnValuesCK.Checked;
        }

        private void EventAreaBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                NodeId areaId = new SelectNodeDlg().ShowDialog(m_session, Opc.Ua.ObjectIds.Server, "Select Event Area", Opc.Ua.ReferenceTypeIds.HasEventSource);

                if (areaId == null)
                {
                    return;
                }

                m_areaId = areaId;
                EventAreaTB.Text = m_session.NodeCache.GetDisplayText(m_areaId);
                ResultsLV.ChangeArea(areaId, false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void EventTypeBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                TypeDeclaration type = new SelectTypeDlg().ShowDialog(m_session, Opc.Ua.ObjectTypeIds.BaseEventType, "Select Event Type");

                if (type == null)
                {
                    return;
                }

                m_filter = new FilterDeclaration(type, m_filter);
                EventTypeTB.Text = m_session.NodeCache.GetDisplayText(m_filter.EventTypeId);
                EventFilterTB.Text = GetFilterFields(m_filter);
                ResultsLV.ChangeFilter(m_filter, false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void EventFilterBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                if (!new ModifyFilterDlg().ShowDialog(m_filter))
                {
                    return;
                }

                EventFilterTB.Text = GetFilterFields(m_filter);
                ResultsLV.ChangeFilter(m_filter, false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
