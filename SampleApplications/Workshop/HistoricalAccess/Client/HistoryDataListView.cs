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
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.HistoricalAccess.Client
{
    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    public partial class HistoryDataListView : UserControl
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public HistoryDataListView()
        {
            InitializeComponent();
            ResultsDV.AutoGenerateColumns = false;
            LeftPN.Enabled = false;

            ReadTypeCB.Items.Add(ReadType.Subscribe);
            ReadTypeCB.Items.Add(ReadType.Raw);
            ReadTypeCB.Items.Add(ReadType.Processed);
            ReadTypeCB.Items.Add(ReadType.Modified);
            ReadTypeCB.Items.Add(ReadType.AtTime);
            ReadTypeCB.SelectedIndex = 0;
            ResampleIntervalNP.Value = 5000;

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Results");

            m_dataset.Tables[0].Columns.Add("Index", typeof(int));
            m_dataset.Tables[0].Columns.Add("SourceTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("ServerTimestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("StatusCode", typeof(StatusCode));
            m_dataset.Tables[0].Columns.Add("HistoryInfo", typeof(string));
            m_dataset.Tables[0].Columns.Add("UpdateType", typeof(HistoryUpdateType));
            m_dataset.Tables[0].Columns.Add("UpdateTime", typeof(string));
            m_dataset.Tables[0].Columns.Add("UserName", typeof(string));

            m_dataset.Tables[0].DefaultView.Sort = "Index";

            ResultsDV.DataSource = m_dataset.Tables[0];
        }

        #region Private Methods
        /// <summary>
        /// The type history read operation.
        /// </summary>
        private enum ReadType
        {
            Subscribe,
            Raw,
            Modified,
            AtTime,
            Processed
        }

        /// <summary>
        /// An aggregate supported by server. 
        /// </summary>
        private class AvailableAggregate
        {
            public NodeId NodeId { get; set; }
            public string DisplayName { get; set; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private Session m_session;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private NodeId m_nodeId;
        private DataSet m_dataset;
        private int m_nextId;
        private bool m_isSubscribed;
        private HistoryReadDetails m_details;
        private HistoryReadValueId m_nodeToContinue;
        private bool m_timesChanged;
        #endregion

        #region Public Members
        /// <summary>
        /// Changes the session.
        /// </summary>
        public void ChangeSession(Session session, bool fetchRecent)
        {
            if (Object.ReferenceEquals(session, m_session))
            {
                return;
            }

            if (m_session != null)
            {
                DeleteSubscription();
                m_session = null;
            }

            m_session = session;
            m_dataset.Clear();
            LeftPN.Enabled = m_session != null;

            if (m_session != null)
            {
                AggregateCB.Items.Clear();
                AggregateCB.Items.Add(new AvailableAggregate() { NodeId = null, DisplayName = "None" });
                AggregateCB.SelectedIndex = 0;

                ILocalNode node = m_session.NodeCache.Find(ObjectIds.Server_ServerCapabilities_AggregateFunctions) as ILocalNode;

                if (node != null)
                {
                    foreach (IReference reference in node.References.Find(ReferenceTypeIds.HierarchicalReferences, false, true, m_session.TypeTree))
                    {
                        ILocalNode aggregate = m_session.NodeCache.Find(reference.TargetId) as ILocalNode;

                        if (aggregate != null || aggregate.TypeDefinitionId == ObjectTypeIds.AggregateFunctionType)
                        {
                            AvailableAggregate item = new AvailableAggregate();
                            item.NodeId = aggregate.NodeId;
                            item.DisplayName = m_session.NodeCache.GetDisplayText(aggregate);
                            AggregateCB.Items.Add(item);
                        }
                    }

                    AggregateCB.SelectedIndex = 1;
                }

                SubscriptionStateChanged();
            }
        }
        
        /// <summary>
        /// Updates the control after the session has reconnected.
        /// </summary>
        public void SessionReconnected(Session session)
        {
            m_session = session;

            if (m_session != null)
            {
                foreach (Subscription subscription in m_session.Subscriptions)
                {
                    if (Object.ReferenceEquals(subscription.Handle, this))
                    {
                        m_subscription = subscription;

                        foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
                        {
                            m_monitoredItem = monitoredItem;
                            break;
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Changes the node monitored by the control.
        /// </summary>
        public void ChangeNode(NodeId nodeId)
        {
            m_nodeId = nodeId;
            m_dataset.Clear();
            NodeIdTB.Text = m_session.NodeCache.GetDisplayText(m_nodeId);

            if (!m_timesChanged)
            {
                DateTime startTime = ReadFirstDate();

                if (startTime != DateTime.MinValue)
                {
                    StartTimeDP.Value = startTime;
                }

                DateTime endTime = ReadLastDate();

                if (endTime != DateTime.MinValue)
                {
                    EndTimeDP.Value = endTime;
                }
            }
            
            if (m_subscription != null)
            {
                MonitoredItem monitoredItem = new MonitoredItem(m_monitoredItem);
                monitoredItem.StartNodeId = nodeId;

                m_subscription.AddItem(monitoredItem);
                m_subscription.RemoveItem(m_monitoredItem);
                m_monitoredItem = monitoredItem;

                monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

                m_subscription.ApplyChanges();
                SubscriptionStateChanged();
            }
        }

        /// <summary>
        /// Clears the event history in the control.
        /// </summary>
        public void ClearHistory()
        {
            m_dataset.Clear();

            // adjust the width of the columns.
            for (int ii = 0; ii < ResultsDV.Columns.Count; ii++)
            {
                ResultsDV.Columns[ii].Width = -2;
            }
        }

        /// <summary>
        /// Sets the sort order for the control.
        /// </summary>
        /// <param name="mostRecentFirst">If true the most recent entries are displayed first.</param>
        public void SetSortOrder(bool mostRecentFirst)
        {
            if (m_dataset != null && m_dataset.Tables.Count > 0)
            {
                if (mostRecentFirst)
                {
                    m_dataset.Tables[0].DefaultView.Sort = "Index DESC";
                }
                else
                {
                    m_dataset.Tables[0].DefaultView.Sort = "Index";
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads the first date in the archive (truncates milliseconds and converts to local).
        /// </summary>
        private DateTime ReadFirstDate()
        {
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();
            details.StartTime = new DateTime(1970, 1, 1);
            details.EndTime = DateTime.MinValue;
            details.NumValuesPerNode = 1;
            details.IsReadModified = false;
            details.ReturnBounds = false;

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                return DateTime.MinValue;
            }

            HistoryData data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;

            if (results == null)
            {
                return DateTime.MinValue;
            }

            DateTime startTime = data.DataValues[0].SourceTimestamp;

            if (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                m_session.HistoryRead(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Source,
                    true,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                Session.ValidateResponse(results, nodesToRead);
                Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            }
            
            startTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, startTime.Minute, startTime.Second, 0, DateTimeKind.Utc);
            startTime = startTime.ToLocalTime();

            return startTime;
        }

        /// <summary>
        /// Reads the last date in the archive (truncates milliseconds and converts to local).
        /// </summary>
        private DateTime ReadLastDate()
        {
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();
            details.StartTime = DateTime.MinValue;
            details.EndTime = DateTime.UtcNow.AddDays(1);
            details.NumValuesPerNode = 1;
            details.IsReadModified = false;
            details.ReturnBounds = false;

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                return DateTime.MinValue;
            }

            HistoryData data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;

            if (results == null)
            {
                return DateTime.MinValue;
            }

            DateTime endTime = data.DataValues[0].SourceTimestamp;

            if (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                m_session.HistoryRead(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Source,
                    true,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                Session.ValidateResponse(results, nodesToRead);
                Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            }

            endTime = new DateTime(endTime.Year, endTime.Month, endTime.Day, endTime.Hour, endTime.Minute, endTime.Second, 0, DateTimeKind.Utc);
            endTime = endTime.AddSeconds(1);
            endTime = endTime.ToLocalTime();

            return endTime;
        }


        /// <summary>
        /// Creates the subscription.
        /// </summary>
        private void CreateSubscription()
        {
            if (m_session == null)
            {
                return;
            }

            m_subscription = new Subscription();
            m_subscription.Handle = this;
            m_subscription.DisplayName = null;
            m_subscription.PublishingInterval = 1000;
            m_subscription.KeepAliveCount = 10;
            m_subscription.LifetimeCount = 100;
            m_subscription.MaxNotificationsPerPublish = 1000;
            m_subscription.PublishingEnabled = true;
            m_subscription.TimestampsToReturn = TimestampsToReturn.Both;

            m_session.AddSubscription(m_subscription);
            m_subscription.Create();

            m_monitoredItem = new MonitoredItem();
            m_monitoredItem.StartNodeId = m_nodeId;
            m_monitoredItem.AttributeId = Attributes.Value;
            m_monitoredItem.SamplingInterval = (int)SamplingIntervalNP.Value;
            m_monitoredItem.QueueSize = 1000;
            m_monitoredItem.DiscardOldest = true;

            // specify aggregate filter.
            if (AggregateCB.SelectedItem != null)
            {
                AggregateFilter filter = new AggregateFilter();

                if (StartTimeCK.Checked)
                {
                    filter.StartTime = StartTimeDP.Value.ToUniversalTime();
                }
                else
                {
                    filter.StartTime = DateTime.UtcNow;
                }

                filter.ProcessingInterval = (double)ResampleIntervalNP.Value;
                filter.AggregateType = ((AvailableAggregate)AggregateCB.SelectedItem).NodeId;

                if (filter.AggregateType != null)
                {
                    m_monitoredItem.Filter = filter;
                }
            }

            m_monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

            m_subscription.AddItem(m_monitoredItem);
            m_subscription.ApplyChanges();
            SubscriptionStateChanged();
        }

        /// <summary>
        /// Deletes the subscription.
        /// </summary>
        private void DeleteSubscription()
        {
            if (m_subscription != null)
            {
                m_subscription.Delete(true);
                m_session.RemoveSubscription(m_subscription);
                m_subscription = null;
                m_monitoredItem = null;
            }

            SubscriptionStateChanged();
        }

        /// <summary>
        /// Updates the controls after the subscription state changes.
        /// </summary>
        private void SubscriptionStateChanged()
        {
            if (m_monitoredItem != null)
            {
                if (ServiceResult.IsBad(m_monitoredItem.Status.Error))
                {
                    StatusTB.Text = m_monitoredItem.Status.Error.ToString();
                    return;
                }

                StatusTB.Text = "Monitoring started.";
                m_isSubscribed = true;
                GoBTN.Enabled = false;
                StopBTN.Enabled = true;
                NextBTN.Visible = false;
            }
            else
            {
                StatusTB.Text = "Monitoring stopped.";
                m_isSubscribed = false;
                GoBTN.Enabled = true;
                StopBTN.Enabled = false;
                NextBTN.Visible = false;
            }
        }

        /// <summary>
        /// Adds a value to the grid.
        /// </summary>
        private void AddValue(DataValue value, ModificationInfo modificationInfo)
        {            
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = m_nextId++;
            row[1] = value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            row[2] = value.ServerTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            row[3] = value.WrappedValue;
            row[4] = new StatusCode(value.StatusCode.Code);
            row[5] = value.StatusCode.AggregateBits.ToString();

            if (modificationInfo != null)
            {
                row[6] = modificationInfo.UpdateType;
                row[7] = modificationInfo.ModificationTime.ToLocalTime().ToString("HH:mm:ss");
                row[8] = modificationInfo.UserName;
            }

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Updates the display with a new value for a monitored variable. 
        /// </summary>
        private void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MonitoredItemNotificationEventHandler(MonitoredItem_Notification), monitoredItem, e);
                return;
            }

            try
            {
                if (!Object.ReferenceEquals(monitoredItem.Subscription, m_subscription))
                {
                    return;
                }

                MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;

                if (notification == null)
                {
                    return;
                }

                AddValue(notification.Value, null);
                m_dataset.AcceptChanges();
                ResultsDV.FirstDisplayedCell = ResultsDV.Rows[ResultsDV.Rows.Count - 1].Cells[0];
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private void ReadRaw(bool isReadModified)
        {
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();
            details.StartTime =(StartTimeCK.Checked)?StartTimeDP.Value.ToUniversalTime():DateTime.MinValue;
            details.EndTime = (EndTimeCK.Checked)?EndTimeDP.Value.ToUniversalTime():DateTime.MinValue;
            details.NumValuesPerNode = (MaxReturnValuesCK.Checked)?(uint)MaxReturnValuesNP.Value:0;
            details.IsReadModified = isReadModified;
            details.ReturnBounds = (isReadModified)?false:ReturnBoundsCK.Checked;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
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

            HistoryData values = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;
            DisplayResults(values);

            // save any continuation point.
            SaveContinuationPoint(details, nodeToRead, results[0].ContinuationPoint);
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private void ReadAtTime()
        {
            ReadAtTimeDetails details = new ReadAtTimeDetails();
            
            // generate times
            DateTime startTime = StartTimeDP.Value.ToUniversalTime();

            for (int ii = 0; ii < MaxReturnValuesNP.Value; ii++)
            {
                details.ReqTimes.Add(startTime.AddMilliseconds((double)(ii*TimeStepNP.Value)));
            }

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
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

            HistoryData values = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;
            DisplayResults(values);

            // save any continuation point.
            SaveContinuationPoint(details, nodeToRead, results[0].ContinuationPoint);
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private void ReadProcessed()
        {
            AvailableAggregate aggregate = (AvailableAggregate)AggregateCB.SelectedItem;

            ReadProcessedDetails details = new ReadProcessedDetails();
            details.StartTime = StartTimeDP.Value.ToUniversalTime();
            details.EndTime = EndTimeDP.Value.ToUniversalTime();
            details.ProcessingInterval = (double)ResampleIntervalNP.Value;
            details.AggregateType.Add(aggregate.NodeId);
            details.AggregateConfiguration.UseServerCapabilitiesDefaults = true;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
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

            HistoryData values = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;
            DisplayResults(values);

            // save any continuation point.
            SaveContinuationPoint(details, nodeToRead, results[0].ContinuationPoint);
        }

        /// <summary>
        /// Saves a continuation point for later use.
        /// </summary>
        private void SaveContinuationPoint(HistoryReadDetails details, HistoryReadValueId nodeToRead, byte[] continuationPoint)
        {
            // clear existing continuation point.
            if (m_nodeToContinue != null)
            {
                HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
                nodesToRead.Add(m_nodeToContinue);
                                
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

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            }

            m_details = null;
            m_nodeToContinue = null;

            // save new continutation point.
            if (continuationPoint != null && continuationPoint.Length > 0)
            {
                m_details = details;
                m_nodeToContinue = nodeToRead;
                m_nodeToContinue.ContinuationPoint = continuationPoint;
            }

            // update controls.
            if (m_nodeToContinue != null)
            {
                GoBTN.Visible = false;
                NextBTN.Visible = true;
                NextBTN.Enabled = true;
                StopBTN.Enabled = true;
            }
            else
            {
                GoBTN.Visible = true;
                GoBTN.Enabled = true;
                NextBTN.Visible = false;
                StopBTN.Enabled = false;
            }
        }

        /// <summary>
        /// Displays the results of a history operation.
        /// </summary>
        private void DisplayResults(HistoryData values)
        {
            foreach (DataValue value in values.DataValues)
            {
                AddValue(value, null);
            }

            m_dataset.AcceptChanges();
        }

        private void NodeIdBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                NodeId nodeId = new SelectNodeDlg().ShowDialog(
                    m_session,
                    Opc.Ua.ObjectIds.ObjectsFolder,
                    "Select Variable",
                    Opc.Ua.ReferenceTypeIds.Organizes,
                    Opc.Ua.ReferenceTypeIds.Aggregates);

                if (nodeId == null)
                {
                    return;
                }

                if (nodeId != m_nodeId)
                {
                    ChangeNode(nodeId);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SubscribeCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (m_session != null)
                {
                    if (m_isSubscribed)
                    {
                        CreateSubscription();
                    }
                    else
                    {
                        DeleteSubscription();
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GoBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.Clear();

                switch ((ReadType)ReadTypeCB.SelectedItem)
                {
                    case ReadType.Subscribe:
                    {
                        CreateSubscription();
                        break;
                    }

                    case ReadType.Raw:
                    {
                        ReadRaw(false);
                        break;
                    }

                    case ReadType.Modified:
                    {
                        ReadRaw(true);
                        break;
                    }

                    case ReadType.Processed:
                    {
                        ReadProcessed();
                        break;
                    }

                    case ReadType.AtTime:
                    {
                        ReadProcessed();
                        break;
                    }
                }

            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DeleteSubscription();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReadTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ReadType readType = (ReadType)ReadTypeCB.SelectedItem;

                switch (readType)
                {
                    case ReadType.Subscribe:
                    {
                        SamplingIntervalLB.Visible = true;
                        SamplingIntervalNP.Visible = true;
                        SamplingIntervalUnitsLB.Visible = true;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = true;
                        AggregateCB.Visible = true;
                        ResampleIntervalLB.Visible = true;
                        ResampleIntervalNP.Visible = true;
                        ResampleIntervalUnitsLB.Visible = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        break;
                    }

                    case ReadType.Raw:
                    {
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = true;
                        ReturnBoundsLB.Visible = true;
                        ReturnBoundsCK.Visible = true;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ResampleIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        break;
                    }

                    case ReadType.Modified:
                    {
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = true;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ResampleIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        break;
                    }

                    case ReadType.Processed:
                    {
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = true;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = true;
                        AggregateCB.Visible = true;
                        ResampleIntervalLB.Visible = true;
                        ResampleIntervalNP.Visible = true;
                        ResampleIntervalUnitsLB.Visible = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        break;
                    }

                    case ReadType.AtTime:
                    {
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = true;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ResampleIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = true;
                        TimeStepNP.Visible = true;
                        TimeStepUnitsLB.Visible = true;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        private void StartTimeDP_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                m_timesChanged = true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DetectLimitsBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DateTime startTime = ReadFirstDate();

                if (startTime != DateTime.MinValue)
                {
                    StartTimeDP.Value = startTime;
                }

                DateTime endTime = ReadLastDate();

                if (endTime != DateTime.MinValue)
                {
                    EndTimeDP.Value = endTime;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                StartTimeDP.Enabled = StartTimeCK.Checked;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EndTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                EndTimeDP.Enabled = EndTimeCK.Checked;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MaxReturnValuesCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                MaxReturnValuesNP.Enabled = MaxReturnValuesCK.Checked;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
