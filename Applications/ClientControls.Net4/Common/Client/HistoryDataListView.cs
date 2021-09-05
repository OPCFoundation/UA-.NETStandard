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
    public partial class HistoryDataListView : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public HistoryDataListView()
        {
            InitializeComponent();
            ResultsDV.AutoGenerateColumns = false;
            LeftPN.Enabled = false;

            ReadTypeCB.Items.Add(HistoryReadType.Raw);
            ReadTypeCB.Items.Add(HistoryReadType.Processed);
            ReadTypeCB.Items.Add(HistoryReadType.Modified);
            ReadTypeCB.Items.Add(HistoryReadType.AtTime);
            ReadTypeCB.Items.Add(HistoryReadType.Subscribe);
            ReadTypeCB.Items.Add(HistoryReadType.Insert);
            ReadTypeCB.Items.Add(HistoryReadType.InsertReplace);
            ReadTypeCB.Items.Add(HistoryReadType.Replace);
            ReadTypeCB.Items.Add(HistoryReadType.Remove);
            ReadTypeCB.Items.Add(HistoryReadType.DeleteRaw);
            ReadTypeCB.Items.Add(HistoryReadType.DeleteModified);
            ReadTypeCB.Items.Add(HistoryReadType.DeleteAtTime);
            ReadTypeCB.SelectedIndex = 0;
            
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
            m_dataset.Tables[0].Columns.Add("DataValue", typeof(DataValue));
            m_dataset.Tables[0].Columns.Add("UpdateResult", typeof(StatusCode));

            m_dataset.Tables[0].DefaultView.Sort = "Index";

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region HistoryReadType Class
        /// <summary>
        /// The type history read operation.
        /// </summary>
        public enum HistoryReadType
        {
            /// <summary>
            /// Subscribe to data changes.
            /// </summary>
            Subscribe,

            /// <summary>
            /// Read raw data.
            /// </summary>
            Raw,

            /// <summary>
            /// Read modified data.
            /// </summary>
            Modified,

            /// <summary>
            /// Read data at the specified times.
            /// </summary>
            AtTime,

            /// <summary>
            /// Read processed data.
            /// </summary>
            Processed,

            /// <summary>
            /// Insert data.
            /// </summary>
            Insert,

            /// <summary>
            /// Insert or replace data.
            /// </summary>
            InsertReplace,

            /// <summary>
            /// Replace data.
            /// </summary>
            Replace,

            /// <summary>
            /// Remove data.
            /// </summary>
            Remove,

            /// <summary>
            /// Delete raw data.
            /// </summary>
            DeleteRaw,

            /// <summary>
            /// Delete modified data.
            /// </summary>
            DeleteModified,

            /// <summary>
            /// Delete data at the specified times.
            /// </summary>
            DeleteAtTime
        }
        #endregion

        #region AvailableAggregate Class
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
        #endregion

        #region AvailableSession Class
        /// <summary>
        /// A session available in the conntrol.
        /// </summary>
        private class AvailableSession
        {
            public Session Session { get; set; }

            public override string ToString()
            {
                return Session.SessionName;
            }
        }
        #endregion

        #region PropertyWithHistory Class
        /// <summary>
        /// Stores the metadata about a property with history to read or update.
        /// </summary>
        private class PropertyWithHistory
        {
            public PropertyWithHistory()
            {
            }

            public PropertyWithHistory(ReferenceDescription reference, byte accessLevel)
            {
                DisplayText = reference.ToString();
                NodeId = (NodeId)reference.NodeId;
                BrowseName = reference.BrowseName;
                AccessLevel = accessLevel;
            }

            public override string ToString()
            {
                return DisplayText;
            }

            public string DisplayText;
            public NodeId NodeId;
            public QualifiedName BrowseName;
            public byte AccessLevel;
        }
        #endregion

        #region Private Fields
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
        private HistoricalDataConfigurationState m_configuration;
        private List<PropertyWithHistory> m_properties;
        #endregion

        #region Public Members
        /// <summary>
        /// The node id to use.
        /// </summary>        
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public NodeId NodeId
        {
            get 
            { 
                return m_nodeId; 
            }
            
            set
            {
                m_nodeId = value;

                if (m_session != null)
                {
                    NodeIdTB.Text = m_session.NodeCache.GetDisplayText(m_nodeId);
                }
                else
                {
                    if (NodeId.IsNull(m_nodeId))
                    {
                        NodeIdTB.Text = String.Empty;
                    }
                    else
                    {
                        NodeIdTB.Text = m_nodeId.ToString();
                    }
                }
            }
        }

        /// <summary>
        /// The type of read operation.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public HistoryReadType ReadType
        {
            get { return (HistoryReadType)ReadTypeCB.SelectedItem; }
            set { ReadTypeCB.SelectedItem = value; }
        }

        /// <summary>
        /// The start time for the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public DateTime StartTime
        {
            get
            {
                if (StartTimeCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return StartTimeDP.Value;
            }

            set
            {
                if (value < Utils.TimeBase)
                {
                    StartTimeCK.Checked = false;
                    return;
                }

                if (value.Kind == DateTimeKind.Local)
                {
                    value = value.ToUniversalTime();
                }

                StartTimeCK.Checked = true;
                StartTimeDP.Value = value;
            }
        }

        /// <summary>
        /// The end time for the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public DateTime EndTime
        {
            get
            {
                if (EndTimeCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return EndTimeDP.Value;
            }

            set
            {
                if (value < Utils.TimeBase)
                {
                    EndTimeCK.Checked = false;
                    return;
                }

                if (value.Kind == DateTimeKind.Local)
                {
                    value = value.ToUniversalTime();
                }

                EndTimeCK.Checked = true;
                EndTimeDP.Value = value;
            }
        }

        /// <summary>
        /// THe maximum number of values to return.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public uint MaxReturnValues
        {
            get
            {
                if (MaxReturnValuesCK.Checked)
                {
                    return 0;
                }

                return (uint)MaxReturnValuesNP.Value;
            }

            set
            {
                MaxReturnValuesCK.Checked = value != 0;
                MaxReturnValuesNP.Value = value;
            }
        }

        /// <summary>
        /// If true the bounds are returned in the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool ReturnBounds
        {
            get
            {
                return ReturnBoundsCK.Checked;
            }

            set
            {
                ReturnBoundsCK.Checked = value;
            }
        }

        /// <summary>
        /// The aggregate to calculate.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public NodeId Aggregate
        {
            get
            {
                AvailableAggregate aggregate = AggregateCB.SelectedItem as AvailableAggregate;

                if (aggregate == null)
                {
                    return NodeId.Null;
                }

                return aggregate.NodeId;
            }

            set
            {
                if (NodeId.IsNull(value))
                {
                    AggregateCB.SelectedIndex = -1;
                    return;
                }

                foreach (AvailableAggregate aggregate in AggregateCB.Items)
                {
                    if (aggregate.NodeId == value)
                    {
                        AggregateCB.SelectedItem = value;
                        return;

                    }
                }

                throw new ArgumentException("Aggregate does match one of the available aggregates.");
            }
        }

        /// <summary>
        /// The processing interval to use.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public double ProcessingInterval
        {
            get { return (double)ProcessingIntervalNP.Value; }
            set { ProcessingIntervalNP.Value = (decimal)value; }
        }

        /// <summary>
        /// Changes the session.
        /// </summary>
        public void ChangeSession(Session session)
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

            if (session == null)
            {
                return;
            }

            m_session = session;
            m_dataset.Clear();
            LeftPN.Enabled = m_session != null;

            if (m_session != null)
            {
                AggregateCB.Items.Clear();

                ILocalNode node = m_session.NodeCache.Find(ObjectIds.Server_ServerCapabilities_AggregateFunctions) as ILocalNode;

                if (node != null)
                {
                    foreach (IReference reference in node.References.Find(ReferenceTypeIds.HierarchicalReferences, false, true, m_session.TypeTree))
                    {
                        ILocalNode aggregate = m_session.NodeCache.Find(reference.TargetId) as ILocalNode;

                        if (aggregate != null && aggregate.TypeDefinitionId == ObjectTypeIds.AggregateFunctionType)
                        {
                            AvailableAggregate item = new AvailableAggregate();
                            item.NodeId = aggregate.NodeId;
                            item.DisplayName = m_session.NodeCache.GetDisplayText(aggregate);
                            AggregateCB.Items.Add(item);
                        }
                    }

                    if (AggregateCB.Items.Count == 0)
                    {
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Interpolative, DisplayName = BrowseNames.AggregateFunction_Interpolative });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Average, DisplayName = BrowseNames.AggregateFunction_Average });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_TimeAverage, DisplayName = BrowseNames.AggregateFunction_TimeAverage });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Total, DisplayName = BrowseNames.AggregateFunction_Total });
                        AggregateCB.Items.Add(new AvailableAggregate() { NodeId = ObjectIds.AggregateFunction_Count, DisplayName = BrowseNames.AggregateFunction_Count });
                    }

                    if (AggregateCB.Items.Count > 0)
                    {
                        AggregateCB.SelectedIndex = 0;
                    }
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
            m_configuration = null;
            m_properties = null;
            PropertyCB.Items.Clear();
            m_dataset.Clear();
            NodeIdTB.Text = m_session.NodeCache.GetDisplayText(m_nodeId);

            if (!NodeId.IsNull(nodeId))
            {
                m_properties = FindPropertiesWithHistory();

                if (m_properties == null || m_properties.Count <= 1)
                {
                    PropertyLB.Visible = false;
                    PropertyCB.Visible = false;
                }
                else
                {
                    PropertyCB.Items.AddRange((object[])m_properties.ToArray());
                    PropertyCB.SelectedIndex = 0;
                    PropertyLB.Visible = true;
                    PropertyCB.Visible = true;
                }

                m_configuration = ReadConfiguration();

                if (StatusCode.IsBad(m_configuration.Stepped.StatusCode))
                {
                    this.ReadTypeCB.Enabled = false;
                    this.ReadTypeCB.SelectedItem = HistoryReadType.Subscribe;
                }
                else
                {
                    this.ReadTypeCB.Enabled = true;

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

        /// <summary>
        /// A kludge to get around the stupid designer that keeps setting property values to bogus defaults.
        /// </summary>
        public void Reset()
        {
            NodeId = null;
            ReadType = HistoryReadType.Raw;
            StartTime = DateTime.MinValue;
            EndTime = DateTime.MinValue;
            Aggregate = null;

            StartTimeCK.Checked = true;
            EndTimeCK.Checked = false;
            MaxReturnValuesCK.Checked = true;
            MaxReturnValuesNP.Value = 10;
            m_timesChanged = false;
            ProcessingIntervalNP.Value = 5000;
        }

        /// <summary>
        /// Shows the configuration.
        /// </summary>
        public void ShowConfiguration()
        {
            if (m_session != null)
            {
                if (m_configuration != null)
                {
                    new ViewNodeStateDlg().ShowDialog(m_session, m_configuration, null);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Recursively collects the variables in a NodeState and returns a collection of BrowsePaths.
        /// </summary>
        public void GetBrowsePathFromNodeState(
            ISystemContext context,
            NodeId rootId,
            NodeState parent,
            RelativePath parentPath,
            BrowsePathCollection browsePaths)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                BrowsePath browsePath = new BrowsePath();
                browsePath.StartingNode = rootId;
                browsePath.Handle = child;

                if (parentPath != null)
                {
                    browsePath.RelativePath.Elements.AddRange(parentPath.Elements);
                }

                RelativePathElement element = new RelativePathElement();
                element.ReferenceTypeId = child.ReferenceTypeId;
                element.IsInverse = false;
                element.IncludeSubtypes = false;
                element.TargetName = child.BrowseName;

                browsePath.RelativePath.Elements.Add(element);

                if (child.NodeClass == NodeClass.Variable)
                {
                    browsePaths.Add(browsePath);
                }

                GetBrowsePathFromNodeState(context, rootId, child, browsePath.RelativePath, browsePaths); 
            }
        }

        /// <summary>
        /// Reads the historical configuration for the node.
        /// </summary>
        private List<PropertyWithHistory> FindPropertiesWithHistory()
        {
            BrowseDescription nodeToBrowse = new BrowseDescription();
            nodeToBrowse.NodeId = m_nodeId;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.NodeClassMask = 0;
            nodeToBrowse.ResultMask = (uint)(BrowseResultMask.DisplayName | BrowseResultMask.BrowseName);

            ReferenceDescriptionCollection references = ClientUtils.Browse(m_session, nodeToBrowse, false);

            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < references.Count; ii++)
            {
                if (references[ii].NodeId.IsAbsolute)
                {
                    continue;
                }

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = (NodeId)references[ii].NodeId;
                nodeToRead.AttributeId = Attributes.AccessLevel;
                nodeToRead.Handle = references[ii];
                nodesToRead.Add(nodeToRead);
            }

            List<PropertyWithHistory> properties = new List<PropertyWithHistory>();
            properties.Add(new PropertyWithHistory() { DisplayText = "(none)", NodeId = m_nodeId, AccessLevel = AccessLevels.HistoryReadOrWrite });

            if (nodesToRead.Count > 0)
            {
                DataValueCollection values = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    out values,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(values, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    byte accessLevel = values[ii].GetValue<byte>(0);

                    if ((accessLevel & AccessLevels.HistoryRead) != 0)
                    {
                        properties.Add(new PropertyWithHistory((ReferenceDescription)nodesToRead[ii].Handle, accessLevel));
                    }
                }
            }

            return properties;
        }

        /// <summary>
        /// Reads the historical configuration for the node.
        /// </summary>
        private HistoricalDataConfigurationState ReadConfiguration()
        {
            // load the defaults for the historical configuration object. 
            HistoricalDataConfigurationState configuration = new HistoricalDataConfigurationState(null);

            configuration.Definition = new PropertyState<string>(configuration);
            configuration.MaxTimeInterval = new PropertyState<double>(configuration);
            configuration.MinTimeInterval = new PropertyState<double>(configuration);
            configuration.ExceptionDeviation = new PropertyState<double>(configuration);
            configuration.ExceptionDeviationFormat = new PropertyState<ExceptionDeviationFormat>(configuration);
            configuration.StartOfArchive = new PropertyState<DateTime>(configuration);
            configuration.StartOfOnlineArchive = new PropertyState<DateTime>(configuration);

            configuration.Create(
                m_session.SystemContext,
                null,
                Opc.Ua.BrowseNames.HAConfiguration,
                null,
                false);
            
            // get the browse paths to query.
            RelativePathElement element = new RelativePathElement();
            element.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasHistoricalConfiguration;
            element.IsInverse = false;
            element.IncludeSubtypes = false;
            element.TargetName = Opc.Ua.BrowseNames.HAConfiguration;

            RelativePath relativePath = new RelativePath();
            relativePath.Elements.Add(element);

            BrowsePathCollection pathsToTranslate = new BrowsePathCollection();

            GetBrowsePathFromNodeState(
                m_session.SystemContext,
                m_nodeId,
                configuration,
                relativePath,
                pathsToTranslate);

            // translate browse paths.
            BrowsePathResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.TranslateBrowsePathsToNodeIds(
                null,
                pathsToTranslate,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, pathsToTranslate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, pathsToTranslate);

            // build list of values to read.
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < pathsToTranslate.Count; ii++)
            {
                BaseVariableState variable = (BaseVariableState)pathsToTranslate[ii].Handle;
                variable.Value = null;
                variable.StatusCode = StatusCodes.BadNotSupported;

                if (StatusCode.IsBad(results[ii].StatusCode) || results[ii].Targets.Count == 0)
                {
                    continue;
                }

                if (results[ii].Targets[0].RemainingPathIndex == UInt32.MaxValue && !results[ii].Targets[0].TargetId.IsAbsolute)
                {
                    variable.NodeId = (NodeId)results[ii].Targets[0].TargetId;

                    ReadValueId valueToRead = new ReadValueId();
                    valueToRead.NodeId = variable.NodeId;
                    valueToRead.AttributeId = Attributes.Value;
                    valueToRead.Handle = variable;
                    valuesToRead.Add(valueToRead);
                }
            }
            
            // read the values.
            if (valuesToRead.Count > 0)
            {
                DataValueCollection values = null;

                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    valuesToRead,
                    out values,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(values, valuesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

                for (int ii = 0; ii < valuesToRead.Count; ii++)
                {
                    BaseVariableState variable = (BaseVariableState)valuesToRead[ii].Handle;
                    variable.WrappedValue = values[ii].WrappedValue;
                    variable.StatusCode = values[ii].StatusCode;
                }
            }

            return configuration;
        }

        /// <summary>
        /// Reads the first date in the archive (truncates milliseconds and converts to local).
        /// </summary>
        private DateTime ReadFirstDate()
        {
            // use the historical data configuration if available.
            if (m_configuration != null)
            {
                if (StatusCode.IsGood(m_configuration.StartOfOnlineArchive.StatusCode))
                {
                    return m_configuration.StartOfOnlineArchive.Value.ToLocalTime();
                }

                if (StatusCode.IsGood(m_configuration.StartOfArchive.StatusCode))
                {
                    return m_configuration.StartOfArchive.Value.ToLocalTime();
                }
            }

            // do it the hard way (may take a long time with some servers).
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

            if (data == null || data.DataValues.Count == 0)
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

                filter.ProcessingInterval = (double)ProcessingIntervalNP.Value;
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
                GoBTN.Visible = true;
                StopBTN.Enabled = true;
                NextBTN.Visible = false;
            }
            else
            {
                StatusTB.Text = "Monitoring stopped.";
                m_isSubscribed = false;
                GoBTN.Enabled = true;
                GoBTN.Visible = true;
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

            m_nextId += 10000;

            row[0] = m_nextId;
            UpdateRow(row, value, modificationInfo);

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Updates a value in the grid.
        /// </summary>
        private void UpdateRow(DataRow row, DataValue value, ModificationInfo modificationInfo)
        {
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

            row[9] = value;
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

                if (ResultsDV.Rows.Count > 0)
                {
                    ResultsDV.FirstDisplayedCell = ResultsDV.Rows[ResultsDV.Rows.Count - 1].Cells[0];
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Fetches the next batch of history.
        /// </summary>
        private void ReadNext()
        {
            if (m_nodeToContinue == null)
            {
                return;
            }

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(m_nodeToContinue);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(m_details),
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
            SaveContinuationPoint(m_details, m_nodeToContinue, results[0].ContinuationPoint);
        }

        /// <summary>
        /// Returns the currently selected historical variable or property node id.
        /// </summary>
        private NodeId GetSelectedNode()
        {
            if (PropertyCB.SelectedIndex >= 0)
            {
                return ((PropertyWithHistory)PropertyCB.SelectedItem).NodeId;
            }

            return m_nodeId;
        }

        /// <summary>
        /// Fetches the recent history.
        /// </summary>
        private void ReadRaw(bool isReadModified)
        {
            m_dataset.Clear();

            ReadRawModifiedDetails details = new ReadRawModifiedDetails();
            details.StartTime =(StartTimeCK.Checked)?StartTimeDP.Value.ToUniversalTime():DateTime.MinValue;
            details.EndTime = (EndTimeCK.Checked)?EndTimeDP.Value.ToUniversalTime():DateTime.MinValue;
            details.NumValuesPerNode = (MaxReturnValuesCK.Checked)?(uint)MaxReturnValuesNP.Value:0;
            details.IsReadModified = isReadModified;
            details.ReturnBounds = (isReadModified)?false:ReturnBoundsCK.Checked;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = GetSelectedNode();
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
            m_dataset.Clear();

            ReadAtTimeDetails details = new ReadAtTimeDetails();
            details.UseSimpleBounds = UseSimpleBoundsCK.Checked;

            // generate times
            DateTime startTime = StartTimeDP.Value.ToUniversalTime();

            for (int ii = 0; ii < MaxReturnValuesNP.Value; ii++)
            {
                details.ReqTimes.Add(startTime.AddMilliseconds((double)(ii*TimeStepNP.Value)));
            }

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = GetSelectedNode();
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
            m_dataset.Clear();

            AvailableAggregate aggregate = (AvailableAggregate)AggregateCB.SelectedItem;

            if (aggregate == null)
            {
                return;
            }

            ReadProcessedDetails details = new ReadProcessedDetails();
            details.StartTime = StartTimeDP.Value.ToUniversalTime();
            details.EndTime = EndTimeDP.Value.ToUniversalTime();
            details.ProcessingInterval = (double)ProcessingIntervalNP.Value;
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
        /// Updates the history.
        /// </summary>
        private void InsertReplace(PerformUpdateType updateType)
        {
            DataValueCollection values = new DataValueCollection();

            foreach (DataRowView row in m_dataset.Tables[0].DefaultView)
            {
                DataValue value = (DataValue)row.Row[9];
                values.Add(value);
            }
            
            bool isStructured = false;

            PropertyWithHistory property = PropertyCB.SelectedItem as PropertyWithHistory;

            if (property != null && property.BrowseName == Opc.Ua.BrowseNames.Annotations)
            {
                isStructured = true;
            }

            HistoryUpdateResultCollection results = InsertReplace(GetSelectedNode(), updateType, isStructured, values);

            ResultsDV.Columns[ResultsDV.Columns.Count - 1].Visible = true;

            for (int ii = 0; ii < m_dataset.Tables[0].DefaultView.Count; ii++)
            {
                m_dataset.Tables[0].DefaultView[ii].Row[10] = results[0].OperationResults[ii];
            }

            m_dataset.AcceptChanges();
        }

        /// <summary>
        /// Updates the history.
        /// </summary>
        private HistoryUpdateResultCollection InsertReplace(NodeId nodeId, PerformUpdateType updateType, bool isStructure, IList<DataValue> values)
        {
            HistoryUpdateDetails details = null;

            if (isStructure)
            {
                UpdateStructureDataDetails details2 = new UpdateStructureDataDetails();
                details2.NodeId = nodeId;
                details2.PerformInsertReplace = updateType;
                details2.UpdateValues.AddRange(values);
                details = details2;
            }
            else
            {
                UpdateDataDetails details2 = new UpdateDataDetails();
                details2.NodeId = nodeId;
                details2.PerformInsertReplace = updateType;
                details2.UpdateValues.AddRange(values);
                details = details2;
            }

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

            return results;
        }

        /// <summary>
        /// Deletes the block of data.
        /// </summary>
        private void DeleteRaw(bool isModified)
        {
            DeleteRawModifiedDetails details = new DeleteRawModifiedDetails();
            details.NodeId = m_nodeId;
            details.IsDeleteModified = isModified;
            details.StartTime = StartTimeDP.Value;
            details.EndTime = EndTimeDP.Value;

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

            ResultsDV.Columns[ResultsDV.Columns.Count - 1].Visible = false;
            m_dataset.Clear();
        }

        /// <summary>
        /// Deletes the history.
        /// </summary>
        private void DeleteAtTime()
        {
            DeleteAtTimeDetails details = new DeleteAtTimeDetails();
            details.NodeId = m_nodeId;

            foreach (DataRowView row in m_dataset.Tables[0].DefaultView)
            {
                DateTime value = (DateTime)row.Row[1];
                details.ReqTimes.Add(value);
            }

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

            ResultsDV.Columns[ResultsDV.Columns.Count-1].Visible = true;

            for (int ii = 0; ii < m_dataset.Tables[0].DefaultView.Count; ii++)
            {
                m_dataset.Tables[0].DefaultView[ii].Row[10] = results[0].OperationResults[ii];
            }

            m_dataset.AcceptChanges();
        }

        /// <summary>
        /// Displays the results of a history operation.
        /// </summary>
        private void DisplayResults(HistoryData values)
        {
            HistoryModifiedData modifiedData = values as HistoryModifiedData;

            if (modifiedData != null)
            {
                ResultsDV.Columns[5].Visible = true;
                ResultsDV.Columns[6].Visible = true;
                ResultsDV.Columns[7].Visible = true;
                ResultsDV.Columns[8].Visible = false;

                for (int ii = 0; ii < modifiedData.DataValues.Count; ii++)
                {
                    AddValue(modifiedData.DataValues[ii], modifiedData.ModificationInfos[ii]);
                }
            }
            else
            {
                ResultsDV.Columns[5].Visible = false;
                ResultsDV.Columns[6].Visible = false;
                ResultsDV.Columns[7].Visible = false;
                ResultsDV.Columns[8].Visible = false;

                if (values != null)
                {
                    foreach (DataValue value in values.DataValues)
                    {
                        AddValue(value, null);
                    }
                }
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

                ReferenceDescription reference = new SelectNodeDlg().ShowDialog(
                    m_session,
                    Opc.Ua.ObjectIds.ObjectsFolder,
                    null,
                    "Select Variable",
                    Opc.Ua.ReferenceTypeIds.Organizes,
                    Opc.Ua.ReferenceTypeIds.Aggregates);

                if (reference == null)
                {
                    return;
                }

                if (reference.NodeId != m_nodeId)
                {
                    ChangeNode((NodeId)reference.NodeId);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void GoBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.Tables[0].Rows.Clear();

                switch ((HistoryReadType)ReadTypeCB.SelectedItem)
                {
                    case HistoryReadType.Subscribe:
                    {
                        CreateSubscription();
                        break;
                    }

                    case HistoryReadType.Raw:
                    {
                        ReadRaw(false);
                        break;
                    }

                    case HistoryReadType.Modified:
                    {
                        ReadRaw(true);
                        break;
                    }

                    case HistoryReadType.Processed:
                    {
                        ReadProcessed();
                        break;
                    }

                    case HistoryReadType.AtTime:
                    {
                        ReadAtTime();
                        break;
                    }

                    case HistoryReadType.Insert:
                    {
                        InsertReplace(PerformUpdateType.Insert);
                        break;
                    }

                    case HistoryReadType.Replace:
                    {
                        InsertReplace(PerformUpdateType.Replace);
                        break;
                    }

                    case HistoryReadType.InsertReplace:
                    {
                        InsertReplace(PerformUpdateType.Update);
                        break;
                    }

                    case HistoryReadType.Remove:
                    {
                        InsertReplace(PerformUpdateType.Remove);
                        break;
                    }

                    case HistoryReadType.DeleteRaw:
                    {
                        DeleteRaw(false);
                        break;
                    }

                    case HistoryReadType.DeleteModified:
                    {
                        DeleteRaw(true);
                        break;
                    }

                    case HistoryReadType.DeleteAtTime:
                    {
                        DeleteAtTime();
                        break;
                    }
                }

            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void NextBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ReadNext();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ReadTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                HistoryReadType readType = (HistoryReadType)ReadTypeCB.SelectedItem;

                switch (readType)
                {
                    case HistoryReadType.Subscribe:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = true;
                        SamplingIntervalNP.Visible = true;
                        SamplingIntervalUnitsLB.Visible = true;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = false;
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
                        ProcessingIntervalNP.Visible = true;
                        ResampleIntervalUnitsLB.Visible = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Raw:
                    {
                        PropertyLB.Visible = (m_properties != null && m_properties.Count > 1);
                        PropertyCB.Visible = (m_properties != null && m_properties.Count > 1);
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = true;
                        MaxReturnValuesCK.Checked = true;
                        ReturnBoundsLB.Visible = true;
                        ReturnBoundsCK.Visible = true;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Modified:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        MaxReturnValuesLB.Visible = true;
                        MaxReturnValuesNP.Visible = true;
                        MaxReturnValuesCK.Visible = true;
                        MaxReturnValuesCK.Enabled = true;
                        MaxReturnValuesCK.Checked = true;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Processed:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
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
                        ProcessingIntervalNP.Visible = true;
                        ResampleIntervalUnitsLB.Visible = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.AtTime:
                    {
                        PropertyLB.Visible = (m_properties != null && m_properties.Count > 1);
                        PropertyCB.Visible = (m_properties != null && m_properties.Count > 1);
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
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = false;
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
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = true;
                        TimeStepNP.Visible = true;
                        TimeStepUnitsLB.Visible = true;
                        UseSimpleBoundsLB.Visible = true;
                        UseSimpleBoundsCK.Visible = true;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.Insert:
                    case HistoryReadType.InsertReplace:
                    case HistoryReadType.Replace:
                    case HistoryReadType.Remove:
                    {
                        PropertyLB.Visible = (m_properties != null && m_properties.Count > 1);
                        PropertyCB.Visible = (m_properties != null && m_properties.Count > 1);
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = false;
                        StartTimeDP.Visible = false;
                        StartTimeCK.Visible = false;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = false;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = false;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = true;
                        TimeStepNP.Visible = true;
                        TimeStepUnitsLB.Visible = true;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = true;
                        break;
                    }

                    case HistoryReadType.DeleteAtTime:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
                        SamplingIntervalLB.Visible = false;
                        SamplingIntervalNP.Visible = false;
                        SamplingIntervalUnitsLB.Visible = false;
                        StartTimeLB.Visible = false;
                        StartTimeDP.Visible = false;
                        StartTimeCK.Visible = false;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = false;
                        EndTimeLB.Visible = false;
                        EndTimeDP.Visible = false;
                        EndTimeCK.Visible = false;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = false;
                        MaxReturnValuesLB.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryReadType.DeleteRaw:
                    case HistoryReadType.DeleteModified:
                    {
                        PropertyLB.Visible = false;
                        PropertyCB.Visible = false;
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
                        EndTimeCK.Visible = false;
                        MaxReturnValuesNP.Visible = false;
                        MaxReturnValuesCK.Visible = false;
                        MaxReturnValuesCK.Enabled = false;
                        MaxReturnValuesCK.Checked = false;
                        ReturnBoundsLB.Visible = false;
                        ReturnBoundsCK.Visible = false;
                        AggregateLB.Visible = false;
                        AggregateCB.Visible = false;
                        ResampleIntervalLB.Visible = false;
                        ProcessingIntervalNP.Visible = false;
                        ResampleIntervalUnitsLB.Visible = false;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        UseSimpleBoundsLB.Visible = false;
                        UseSimpleBoundsCK.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion

        #region Event Handlers
        private void StartTimeDP_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                m_timesChanged = true;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
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
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void TimeShiftBTN_Click(object sender, EventArgs e)
        {
            try
            {
                foreach (DataRowView row in m_dataset.Tables[0].DefaultView)
                {
                    DataValue value = (DataValue)row.Row[9];
                    value.SourceTimestamp = value.SourceTimestamp.AddMilliseconds((double)TimeStepNP.Value);
                    value.ServerTimestamp = value.ServerTimestamp.AddMilliseconds((double)TimeStepNP.Value);

                    row[1] = value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");
                    row[2] = value.ServerTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");
                }

                m_dataset.AcceptChanges();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void InsertAnnotationMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                NodeId propertyId = null;

                if (m_properties != null)
                {
                    foreach (PropertyWithHistory property in m_properties)
                    {
                        if (property.BrowseName == Opc.Ua.BrowseNames.Annotations)
                        {
                            propertyId = property.NodeId;
                            break;
                        }
                    }
                }

                if (propertyId == null)
                {
                    return;
                }

                Annotation annotation = new EditAnnotationDlg().ShowDialog(m_session, null, null);

                if (annotation != null)
                {
                    List<DataValue> valuesToUpdate = new List<DataValue>();

                    foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                    {
                        DataRowView source = row.DataBoundItem as DataRowView;
                        DataValue value = (DataValue)source.Row[9];

                    }

                    HistoryUpdateResultCollection results = InsertReplace(propertyId, PerformUpdateType.Insert, true, valuesToUpdate); 

                    ResultsDV.Columns[ResultsDV.Columns.Count - 1].Visible = true;

                    for (int ii = 0; ii < ResultsDV.SelectedRows.Count; ii++)
                    {
                        DataGridViewRow row = ResultsDV.SelectedRows[ii];
                        DataRowView source = row.DataBoundItem as DataRowView;
                        source.Row[10] = results[0].OperationResults[ii];
                    }

                    m_dataset.AcceptChanges();
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void EditValueMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }
                
                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    DataValue value = (DataValue)source.Row[9];

                    DataValue newValue = new EditDataValueDlg().ShowDialog(value, null, null);

                    if (newValue == null)
                    {
                        return;
                    }

                    UpdateRow(source.Row, newValue, null);
                    m_dataset.AcceptChanges();
                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ShowServerTimestampMI_CheckedChanged(object sender, EventArgs e)
        {
            ServerTimestampCH.Visible = ShowServerTimestampMI.Checked;
        }
        #endregion
    }
}
