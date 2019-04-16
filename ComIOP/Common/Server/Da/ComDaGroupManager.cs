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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A class that manages the COM DA groups.
    /// </summary>
    public class ComDaGroupManager : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaGroupManager"/> class.
        /// </summary>
        /// <param name="mapper">The mapper.</param>
        /// <param name="browser">The browser.</param>
        public ComDaGroupManager(ComNamespaceMapper mapper, ComDaBrowseManager browser)
		{
            m_mapper = mapper;
            m_browser = browser;
            m_groups = new List<ComDaGroup>();
        }
        #endregion
        
        #region IDisposable Members        
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {  
            if (disposing)
            {
                lock (m_lock)
                {
                    for (int ii = 0; ii < m_groups.Count; ii++)
                    {
                        Utils.SilentDispose(m_groups[ii]);
                    }

                    m_groups.Clear();
                }
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the session.
        /// </summary>
        /// <value>The session.</value>
        public Session Session
        {
            get { return m_session; }
            set
            {
                bool isReplaced = !Object.ReferenceEquals(m_session, value);
                m_session = value;
                if (isReplaced) OnSessionReplaced(); 
            }
        }

        /// <summary>
        /// Gets the namespace mapper.
        /// </summary>
        /// <value>The namespace mapper.</value>
        public ComNamespaceMapper Mapper
        {
            get { return m_mapper; }
        }

        /// <summary>
        /// Gets the group count.
        /// </summary>
        /// <value>The group count.</value>
        public int GroupCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_groups.Count;
                }
            }
        }

        /// <summary>
        /// Gets the last update time.
        /// </summary>
        /// <value>The last update time.</value>
        public DateTime LastUpdateTime
        {
            get
            {
                lock (m_lock)
                {
                    return m_lastUpdateTime;
                }
            }
        }
        
        /// <summary>
        /// Sets the last update time to the current time.
        /// </summary>
        public void SetLastUpdateTime()
        {
            lock (m_lock)
            {
                m_lastUpdateTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Gets the group with the specified name.
        /// </summary>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>The group. Null if it does not exist.</returns>
        public ComDaGroup GetGroupByName(string groupName)
        {
            TraceState("GetGroupByName", groupName);

            lock (m_lock)
            {
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    ComDaGroup group = m_groups[ii];

                    if (group.Name == groupName)
                    {
                        return group;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the group by handle.
        /// </summary>
        /// <param name="serverHandle">The server handle.</param>
        /// <returns>The group.</returns>
        public ComDaGroup GetGroupByHandle(int serverHandle)
        {
            TraceState("GetGroupByHandle", serverHandle);

            lock (m_lock)
            {
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    ComDaGroup group = m_groups[ii];

                    if (group.ServerHandle == serverHandle)
                    {
                        return group;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Sets the name.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="groupName">Name of the group.</param>
        public void SetGroupName(ComDaGroup group, string groupName)
        {
            TraceState("SetGroupName", group.Name, groupName);

            if (String.IsNullOrEmpty(groupName))
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            lock (m_lock)
            {
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    ComDaGroup target = m_groups[ii];

                    if (target.ServerHandle != group.ServerHandle && target.Name == groupName)
                    {
                        throw ComUtils.CreateComException(ResultIds.E_DUPLICATENAME);
                    }

                    group.Name = groupName;
                }
            }
        }

        /// <summary>
        /// Adds the group.
        /// </summary>
        /// <param name="groupName">The group name.</param>
        /// <param name="active">if set to <c>true</c> the group is active.</param>
        /// <param name="updateRate">The update rate.</param>
        /// <param name="clientHandle">The client handle.</param>
        /// <param name="timeBias">The time bias.</param>
        /// <param name="deadband">The deadband.</param>
        /// <param name="lcid">The lcid.</param>
        /// <returns>The new group.</returns>
        public ComDaGroup AddGroup(
            string groupName,
            bool active,
            int updateRate,
            int clientHandle,
            int timeBias,
            float deadband,
            int lcid)
        {
            TraceState("AddGroup", groupName, active, updateRate, clientHandle, timeBias, deadband, lcid);

            // check for valid session.
            Session session = m_session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            ComDaGroup group = null;

            // check for duplicate name.
            lock (m_lock)
            {
                // ensure the name is unique.
                if (!String.IsNullOrEmpty(groupName))
                {
                    if (GetGroupByName(groupName) != null)
                    {
                        throw ComUtils.CreateComException(ResultIds.E_DUPLICATENAME);
                    }
                }

                // assign a unique name.
                else
                {
                    groupName = Utils.Format("Group{0}", m_groupCounter+1);
                }

                // validate the deadband.
                if (deadband < 0 || deadband > 100)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                // create the group.
                group = new ComDaGroup(this, groupName, ++m_groupCounter);
                m_groups.Add(group);

                group.ClientHandle = clientHandle;
                group.Active = active;
                group.UpdateRate = updateRate;
                group.TimeBias = timeBias;
                group.Deadband = deadband;
                group.Lcid = lcid;

                if (updateRate < 100)
                {
                    updateRate = 100;
                }

                // create a new subscription.
                Subscription subscription = new Subscription();
                subscription.DisplayName = groupName;
                subscription.PublishingInterval = updateRate/2;
                subscription.KeepAliveCount = 30;
                subscription.LifetimeCount = 600;
                subscription.MaxNotificationsPerPublish = 10000;
                subscription.Priority = 1;
                subscription.PublishingEnabled = active;
                subscription.DisableMonitoredItemCache = true;
                
                // create the subscription on the server.
                session.AddSubscription(subscription);

                try
                {
                    // create the initial subscription.
                    subscription.Create();

                    // set the keep alive interval to 30 seconds and the the lifetime interval to 5 minutes.
                    subscription.KeepAliveCount = (uint)((30000/(int)subscription.CurrentPublishingInterval)+1);
                    subscription.LifetimeCount = (uint)((600000/(int)subscription.CurrentPublishingInterval)+1);

                    // update the subscription.
                    subscription.Modify();
                }
                catch (Exception e)
                {
                    session.RemoveSubscription(subscription);
                    Utils.Trace((int)Utils.TraceMasks.Error, "Create subscription failed: {0}", e.Message);
                    throw ComUtils.CreateComException(e, ResultIds.E_FAIL);
                }

                // update the group.
                group.ActualUpdateRate = (int)(subscription.CurrentPublishingInterval*2);
                group.Subscription = subscription;
            }

            return group;
        }

        /// <summary>
        /// Removes the group.
        /// </summary>
        /// <param name="group">The group.</param>
        public void RemoveGroup(ComDaGroup group)
        {
            TraceState("RemoveGroup", group.Name);

            lock (m_lock)
            {
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    if (Object.ReferenceEquals(group, m_groups[ii]))
                    {
                        m_groups.RemoveAt(ii);

                        if (group.Subscription != null && group.Subscription.Session != null)
                        {
                            group.Subscription.Session.RemoveSubscription(group.Subscription);
                        }
                    }
                }

                group.Dispose();
            }
        }

        /// <summary>
        /// Returns the current set of groups.
        /// </summary>
        /// <returns>The list of groups.</returns>
        public ComDaGroup[] GetGroups()
        {
            TraceState("GetGroups");

            lock (m_lock)
            {
                ComDaGroup[] groups = new ComDaGroup[m_groups.Count];

                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    groups[ii] = m_groups[ii];
                }

                return groups;
            }
        }

        /// <summary>
        /// Updates the EUInfo for the items.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="items">The items. Null entries are ignored.</param>
        public void UpdateItemEuInfo(
            ComDaGroup group,
            IList<ComDaGroupItem> items)
        {
            // get the session to use for the operation.
            Session session = m_session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            // build list of properties that need to be read.
            BrowsePathCollection browsePaths = new BrowsePathCollection();

            for (int ii = 0; ii < items.Count; ii++)
            {
                ComDaGroupItem item = (ComDaGroupItem)items[ii];

                // ignore invalid items or items which have already checked their EU type.
                if (item == null || item.EuType >= 0)
                {
                    continue;
                }

                BrowsePath browsePath = new BrowsePath();
                browsePath.StartingNode = item.NodeId;
                RelativePathElement element = new RelativePathElement();
                element.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                element.IsInverse = false;
                element.IncludeSubtypes = false;
                element.TargetName = Opc.Ua.BrowseNames.EURange;
                browsePath.RelativePath.Elements.Add(element);
                browsePath.Handle = item;
                browsePaths.Add(browsePath);

                browsePath = new BrowsePath();
                browsePath.StartingNode = item.NodeId;
                element = new RelativePathElement();
                element.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                element.IsInverse = false;
                element.IncludeSubtypes = false;
                element.TargetName = Opc.Ua.BrowseNames.EnumStrings;
                browsePath.RelativePath.Elements.Add(element);
                browsePath.Handle = item;
                browsePaths.Add(browsePath);
            }

            // check if nothing to do.
            if (browsePaths.Count == 0)
            {
                return;
            }

            // translate browse paths.
            BrowsePathResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            try
            {
                session.TranslateBrowsePathsToNodeIds(
                    null,
                    browsePaths,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, browsePaths);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);
            }
            catch (Exception)
            {
                for (int ii = 0; ii < browsePaths.Count; ii++)
                {
                    ComDaGroupItem item = (ComDaGroupItem)browsePaths[ii].Handle;
                    item.EuType = 0;
                }

                return;
            }

            // build list of properties that need to be read.
            ReadValueIdCollection propertiesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < results.Count; ii++)
            {
                ComDaGroupItem item = (ComDaGroupItem)browsePaths[ii].Handle;
                BrowsePathResult result = results[ii];

                if (StatusCode.IsBad(result.StatusCode))
                {
                    if (item.EuType < 0 && result.StatusCode == StatusCodes.BadNoMatch)
                    {
                        item.EuType = (int)OpcRcw.Da.OPCEUTYPE.OPC_NOENUM;
                    }

                    continue;
                }

                if (result.Targets.Count == 0 || result.Targets[0].TargetId.IsAbsolute)
                {
                    if (item.EuType < 0)
                    {
                        item.EuType = (int)OpcRcw.Da.OPCEUTYPE.OPC_NOENUM;
                    }

                    continue;
                }

                ReadValueId propertyToRead = new ReadValueId();
                propertyToRead.NodeId = (NodeId)result.Targets[0].TargetId;
                propertyToRead.AttributeId = Attributes.Value;
                propertyToRead.Handle = item;
                propertiesToRead.Add(propertyToRead);

                if (browsePaths[ii].RelativePath.Elements[0].TargetName.Name == Opc.Ua.BrowseNames.EURange)
                {
                    item.EuType = (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG;
                }
                else
                {
                    item.EuType = (int)OpcRcw.Da.OPCEUTYPE.OPC_ENUMERATED;
                }
            }

            // check if nothing to do.
            if (propertiesToRead.Count == 0)
            {
                return;
            }

            // read attribute values from the server.
            DataValueCollection values = null;

            try
            {
                session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    propertiesToRead,
                    out values,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(values, propertiesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, propertiesToRead);
            }
            catch (Exception)
            {
                for (int ii = 0; ii < propertiesToRead.Count; ii++)
                {
                    ComDaGroupItem item = (ComDaGroupItem)propertiesToRead[ii].Handle;
                    item.EuType = 0;
                }

                return;
            }

            // process results.
            for (int ii = 0; ii < values.Count; ii++)
            {
                ComDaGroupItem item = (ComDaGroupItem)propertiesToRead[ii].Handle;

                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    item.EuType = 0;
                    continue;
                }

                if (item.EuType == (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG)
                {
                    Range range = (Range)values[ii].GetValue<Range>(null);

                    if (range == null)
                    {
                        item.EuType = 0;
                        continue;
                    }

                    item.EuInfo = new double[] { range.Low, range.High };
                    continue;
                }

                if (item.EuType == (int)OpcRcw.Da.OPCEUTYPE.OPC_ENUMERATED)
                {
                    LocalizedText[] texts = (LocalizedText[])values[ii].GetValue<LocalizedText[]>(null);

                    if (texts == null)
                    {
                        item.EuType = 0;
                        continue;
                    }

                    string[] strings = new string[texts.Length];

                    for (int jj = 0; jj < strings.Length; jj++)
                    {
                        if (!LocalizedText.IsNullOrEmpty(texts[jj]))
                        {
                            strings[jj] = texts[jj].Text;
                        }
                    }

                    item.EuInfo = strings;
                    continue;
                }
            }
        }

        /// <summary>
        /// Validates the items.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="requests">The requests.</param>
        /// <returns>The items. May contain null is validation failed.</returns>
        public ComDaGroupItem[] ValidateItems(ComDaGroup group, ComDaCreateItemRequest[] requests)
        {
            TraceState("ValidateItems", group.Name);

            // get the session to use for the operation.
            Session session = m_session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            // validate items.
            ComDaGroupItem[] items = new ComDaGroupItem[requests.Length];

            for (int ii = 0; ii < requests.Length; ii += 10000)
            {
                ValidateItems(session, group, requests, items, ii, 10000);
            }

            // process results.
            for (int ii = 0; ii < requests.Length; ii++)
            {
                // check for the results.
                ComDaCreateItemRequest request = requests[ii];

                if (request.Error < 0)
                {
                    items[ii] = null;
                    continue;
                }

                // check access path.
                if (!String.IsNullOrEmpty(request.AccessPath))
                {
                    items[ii] = null;
                    request.Error = ResultIds.E_UNKNOWNPATH;
                    continue;
                }

                ComDaGroupItem item = items[ii];

                // validate the datatype.
                if (request.RequestedDataType != 0)
                {
                    NodeId dataTypeId = ComUtils.GetDataTypeId(request.RequestedDataType);

                    if (NodeId.IsNull(dataTypeId))
                    {
                        items[ii] = null;
                        request.Error = ResultIds.E_BADTYPE;
                        continue;
                    }

                    bool reqTypeIsArray = (request.RequestedDataType & (short)VarEnum.VT_ARRAY) != 0;
                    bool actualTypeIsArray = (item.CanonicalDataType & (short)VarEnum.VT_ARRAY) != 0;

                    if (reqTypeIsArray != actualTypeIsArray)
                    {
                        items[ii] = null;
                        request.Error = ResultIds.E_BADTYPE;
                        continue;
                    }
                }

                // create a new monitored item.
                MonitoredItem monitoredItem = new MonitoredItem();

                monitoredItem.StartNodeId = item.NodeId;
                monitoredItem.RelativePath = null;
                monitoredItem.AttributeId = Attributes.Value;
                monitoredItem.MonitoringMode = (request.Active)?MonitoringMode.Reporting:MonitoringMode.Disabled;
                monitoredItem.SamplingInterval = group.UpdateRate/2;
                monitoredItem.QueueSize = 0;
                monitoredItem.DiscardOldest = true;
                monitoredItem.Filter = null;

                // update item.
                item.ServerHandle = (int)monitoredItem.ClientHandle;
                item.MonitoredItem = monitoredItem;

                // link the monitored item back to the group item.
                monitoredItem.Handle = item;
                
                // update return parameters.
                request.ServerHandle = item.ServerHandle;
                request.CanonicalDataType = item.CanonicalDataType;
                request.AccessRights = item.AccessRights;
                request.Error = ResultIds.S_OK;
            }

            return items;
        }

        /// <summary>
        /// Reads the attribute values from the server.
        /// </summary>
        /// <param name="valuesToRead">The values to read.</param>
        /// <returns>
        /// The values read.
        /// </returns>
        public DaValue[] Read(ReadValueIdCollection valuesToRead)
        {
            TraceState("Read", valuesToRead.Count);

            // check for valid session.
            Session session = m_session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            int masterError = ResultIds.E_FAIL;
            DaValue[] results = new DaValue[valuesToRead.Count];

            if (session != null)
            {
                try
                {
                    // read the values.
                    DataValueCollection values = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    session.Read(
                        null,
                        0,
                        TimestampsToReturn.Both,
                        valuesToRead,
                        out values,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(values, valuesToRead);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

                    // convert the response.
                    for (int ii = 0; ii < values.Count; ii++)
                    {
                        results[ii] = m_mapper.GetLocalDataValue(values[ii]);
                    }

                    // return the results.
                    return results;
                }
                catch (Exception e)
                {
                    masterError = ComUtils.GetErrorCode(e, ResultIds.E_FAIL);
                }
            }

            // report any unexpected errors.
            for (int ii = 0; ii < results.Length; ii++)
            {
                DaValue result = results[ii] = new DaValue();
                result.Error = masterError;
            }

            return results;
        }

        /// <summary>
        /// Reads the attribute values from the server.
        /// </summary>
        /// <param name="valuesToWrite">The values to write.</param>
        /// <returns>The results.</returns>
        public int[] Write(WriteValueCollection valuesToWrite)
        {
            TraceState("Write", valuesToWrite.Count);

            // check for valid session.
            Session session = m_session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            int masterError = ResultIds.E_FAIL;
            int[] errors = new int[valuesToWrite.Count];

            if (session != null)
            {
                try
                {
                    // write the values.
                    StatusCodeCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    session.Write(
                        null,
                        valuesToWrite,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, valuesToWrite);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

                    // convert the response.
                    for (int ii = 0; ii < results.Count; ii++)
                    {
                        errors[ii] = ComDaProxy.MapWriteStatusToErrorCode(valuesToWrite[ii].Value, results[ii]);
                    }

                    // return the results.
                    return errors;
                }
                catch (Exception e)
                {
                    masterError = ComUtils.GetErrorCode(e, ResultIds.E_FAIL);
                }
            }

            // report any unexpected errors.
            for (int ii = 0; ii < errors.Length; ii++)
            {
                errors[ii] = masterError;
            }

            return errors;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Validates the items by reading the attributes required to add them to the group.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="group">The group.</param>
        /// <param name="requests">The requests.</param>
        /// <param name="items">The items.</param>
        /// <param name="start">The start index.</param>
        /// <param name="count">The number of items to process.</param>
        private void ValidateItems(
            Session session,
            ComDaGroup group,
            ComDaCreateItemRequest[] requests,
            ComDaGroupItem[] items,
            int start,
            int count)
        {
            // build list of the UA attributes that need to be read.
            ReadValueIdCollection attributesToRead = new ReadValueIdCollection();

            for (int ii = start; ii < start + count && ii < requests.Length; ii++)
            {
                // create the group item.
                ComDaCreateItemRequest request = requests[ii];
                ComDaGroupItem item = items[ii] = new ComDaGroupItem(group, request.ItemId);

                item.NodeId = m_mapper.GetRemoteNodeId(request.ItemId);
                item.Active = request.Active;
                item.ClientHandle = request.ClientHandle;
                item.RequestedDataType = request.RequestedDataType;
                item.SamplingRate = -1;
                item.Deadband = -1;

                // add attributes.
                ReadValueId attributeToRead;

                attributeToRead = new ReadValueId();
                attributeToRead.NodeId = item.NodeId;
                attributeToRead.AttributeId = Attributes.NodeClass;
                attributesToRead.Add(attributeToRead);

                attributeToRead = new ReadValueId();
                attributeToRead.NodeId = item.NodeId;
                attributeToRead.AttributeId = Attributes.DataType;
                attributesToRead.Add(attributeToRead);

                attributeToRead = new ReadValueId();
                attributeToRead.NodeId = item.NodeId;
                attributeToRead.AttributeId = Attributes.ValueRank;
                attributesToRead.Add(attributeToRead);

                attributeToRead = new ReadValueId();
                attributeToRead.NodeId = item.NodeId;
                attributeToRead.AttributeId = Attributes.UserAccessLevel;
                attributesToRead.Add(attributeToRead);
            }

            // read attribute values from the server.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            try
            {
                session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    attributesToRead,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, attributesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error reading attributes for items.");

                // set default values on error.
                for (int ii = start; ii < start + count && ii < requests.Length; ii++)
                {
                    requests[ii].Error = ResultIds.E_INVALIDITEMID;
                }

                return;
            }

            // process results.
            int first = 0;

            for (int ii = start; ii < start + count && ii < requests.Length; ii++, first += 4)
            {
                ComDaGroupItem item = items[ii];

                // verify node class.
                NodeClass nodeClass = (NodeClass)results[first].GetValue<int>((int)NodeClass.Unspecified);

                if (nodeClass != NodeClass.Variable)
                {
                    requests[ii].Error = ResultIds.E_INVALIDITEMID;
                    continue;
                }

                // verify data type.
                NodeId dataTypeId = results[first+1].GetValue<NodeId>(null);

                if (dataTypeId == null)
                {
                    requests[ii].Error = ResultIds.E_INVALIDITEMID;
                    continue;
                }

                // get value rank.
                int valueRank = results[first+2].GetValue<int>(ValueRanks.Scalar);

                // update datatypes.
                BuiltInType builtInType = DataTypes.GetBuiltInType(dataTypeId, session.TypeTree);
                item.RemoteDataType = new TypeInfo(builtInType, valueRank);
                item.CanonicalDataType = (short)ComUtils.GetVarType(item.RemoteDataType);

                // update access rights.
                byte userAccessLevel = results[first+3].GetValue<byte>(0);

                if ((userAccessLevel & AccessLevels.CurrentRead) != 0)
                {
                    item.AccessRights |= OpcRcw.Da.Constants.OPC_READABLE;
                }

                if ((userAccessLevel & AccessLevels.CurrentWrite) != 0)
                {
                    item.AccessRights |= OpcRcw.Da.Constants.OPC_WRITEABLE;
                }
            }
        }

        /// <summary>
        /// Recovers the session context.
        /// </summary>
        /// <param name="group">The group.</param>
        public void RecoverSessionContext(ComDaGroup group)
        {
            // create a new subscription and copy existing one.
            Subscription discardSubscription = group.Subscription;
            Subscription subscription = new Subscription();
            subscription.DisplayName = discardSubscription.DisplayName;
            subscription.PublishingInterval = discardSubscription.PublishingInterval;
            subscription.KeepAliveCount = discardSubscription.KeepAliveCount;
            subscription.LifetimeCount = discardSubscription.LifetimeCount;
            subscription.MaxNotificationsPerPublish = discardSubscription.MaxNotificationsPerPublish;
            subscription.Priority = discardSubscription.Priority;
            subscription.PublishingEnabled = discardSubscription.PublishingEnabled;
            subscription.DisableMonitoredItemCache = discardSubscription.DisableMonitoredItemCache;

            try
            {
                discardSubscription.Dispose();
            }
            catch (Exception)
            {
            }

            m_session.AddSubscription(subscription);
            
            try
            {
                // create the initial subscription.
                subscription.Create();

                // set the keep alive interval to 30 seconds and the the lifetime interval to 5 minutes.
                subscription.KeepAliveCount = (uint)((30000 / (int)subscription.CurrentPublishingInterval) + 1);
                subscription.LifetimeCount = (uint)((600000 / (int)subscription.CurrentPublishingInterval) + 1);

                // update the subscription.
                subscription.Modify();
            }
            catch (Exception e)
            {
                m_session.RemoveSubscription(subscription);
                throw ComUtils.CreateComException(e, ResultIds.E_FAIL);
            }

            // update the group.
            group.ActualUpdateRate = (int)(subscription.CurrentPublishingInterval * 2);
            group.Subscription = subscription;
            group.RecreateItems();
        }

        /// <summary>
        /// Called when a session is replaced.
        /// </summary>
        public void OnSessionReplaced()
        {
            lock (m_lock)
            {
                foreach (ComDaGroup group in m_groups)
                {
                    RecoverSessionContext(group);
                }
            }
        }

        /// <summary>
        /// Called when a session is removed.
        /// </summary>
        public void OnSessionRemoved()
        {
            lock (m_lock)
            {
                if (m_groups.Count <= 0) return;

                foreach (ComDaGroup group in m_groups)
                {
                    group.UpdateCacheWithQuality(OpcRcw.Da.Qualities.OPC_QUALITY_COMM_FAILURE);
                }
            }
        }


        /// <summary>
        /// Dumps the current state of the browser.
        /// </summary>
        private void TraceState(string context, params object[] args)
        {
            #if TRACESTATE
            if ((Utils.TraceMask & Utils.TraceMasks.Information) == 0)
            {
                return;
            }

            StringBuilder buffer = new StringBuilder();

            buffer.AppendFormat("ComDaGroupManager::{0}", context);

            if (args != null)
            {
                buffer.Append("( ");

                for (int ii = 0; ii < args.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(new Variant(args[ii]));
                }

                buffer.Append(" )");
            }

            Utils.Trace("{0}", buffer.ToString());
            #endif
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Session m_session;
        private ComNamespaceMapper m_mapper;
        private ComDaBrowseManager m_browser;
        private int m_groupCounter;
        private List<ComDaGroup> m_groups;
        private DateTime m_lastUpdateTime;
        #endregion
	}
}
