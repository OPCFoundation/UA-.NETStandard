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
using System.Text;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Data;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class HistoricalAccessServerNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public HistoricalAccessServerNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.HistoricalAccess)
        {
            this.AliasRoot = "HDA";

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<HistoricalAccessServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new HistoricalAccessServerConfiguration();
            }

            SystemContext.SystemHandle = m_system = new UnderlyingSystem(m_configuration, NamespaceIndex);
            SystemContext.NodeIdFactory = this;
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {  
            if (disposing)
            {
                // TBD
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        /// <remarks>
        /// This method is called by the NodeState.Create() method which initializes a Node from
        /// the type model. During initialization a number of child nodes are created and need to 
        /// have NodeIds assigned to them. This implementation constructs NodeIds by constructing
        /// strings. Other implementations could assign unique integers or Guids and save the new
        /// Node in a dictionary for later lookup.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                return NodeTypes.ConstructIdForComponent(instance, instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Server.DiagnosticsLock)
            {
                HistoryServerCapabilitiesState capabilities = Server.DiagnosticsNodeManager.GetDefaultHistoryCapabilities();
                capabilities.AccessHistoryDataCapability.Value = true;
                capabilities.InsertDataCapability.Value = true;
                capabilities.ReplaceDataCapability.Value = true;
                capabilities.UpdateDataCapability.Value = true;
                capabilities.DeleteRawCapability.Value = true;
                capabilities.DeleteAtTimeCapability.Value = true;
                capabilities.InsertAnnotationCapability.Value = true;
            }

            lock (Lock)
            {
                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                ArchiveFolderState root = m_system.GetFolderState(SystemContext, String.Empty);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
                root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

                CreateFolderFromResources(root, "Sample");
                CreateFolderFromResources(root, "Dynamic");
            }
        }

        /// <summary>
        /// Creates items from embedded resources.
        /// </summary>
        private void CreateFolderFromResources(NodeState root, string folderName)
        {
            FolderState dataFolder = new FolderState(root);
            dataFolder.ReferenceTypeId = ReferenceTypeIds.Organizes;
            dataFolder.TypeDefinitionId = ObjectTypeIds.FolderType;
            dataFolder.NodeId = new NodeId(folderName, NamespaceIndex);
            dataFolder.BrowseName = new QualifiedName(folderName, NamespaceIndex);
            dataFolder.DisplayName = dataFolder.BrowseName.Name;
            dataFolder.WriteMask = AttributeWriteMask.None;
            dataFolder.UserWriteMask = AttributeWriteMask.None;
            dataFolder.EventNotifier = EventNotifiers.None;
            root.AddChild(dataFolder);
            AddPredefinedNode(SystemContext, root);

            foreach (string resourcePath in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (!resourcePath.StartsWith("Quickstarts.HistoricalAccessServer.Data." + folderName))
                {
                    continue;
                }

                ArchiveItem item = new ArchiveItem(resourcePath, Assembly.GetExecutingAssembly(), resourcePath);
                ArchiveItemState node = new ArchiveItemState(SystemContext, item, NamespaceIndex);
                node.ReloadFromSource(SystemContext);

                dataFolder.AddReference(ReferenceTypeIds.Organizes, false, node.NodeId);
                node.AddReference(ReferenceTypeIds.Organizes, true, dataFolder.NodeId);

                AddPredefinedNode(SystemContext, node);
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                // check for check for nodes that are being currently monitored.
                MonitoredNode2 monitoredNode = null;

                if (MonitoredNodes.TryGetValue(nodeId, out monitoredNode))
                {
                    NodeHandle handle = new NodeHandle();

                    handle.NodeId = nodeId;
                    handle.Validated = true;
                    handle.Node = monitoredNode.Node;

                    return handle;
                }

                // check for predefined nodes,
                NodeState node = null;

                if (PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    NodeHandle handle = new NodeHandle();

                    handle.NodeId = nodeId;
                    handle.Node = node;
                    handle.Validated = true;

                    return handle;
                }

                // parse the identifier.
                ParsedNodeId parsedNodeId = ParsedNodeId.Parse(nodeId);

                if (parsedNodeId != null)
                {
                    NodeHandle handle = new NodeHandle();

                    handle.NodeId = nodeId;
                    handle.Validated = false;
                    handle.Node = null;
                    handle.ParsedNodeId = parsedNodeId;

                    return handle;
                }
                
                return null;
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {            
            // lookup in cache.
            NodeState target = FindNodeInCache(context, handle, cache);

            if (target != null)
            {
                handle.Node = target;
                handle.Validated = true;
                return handle.Node;
            }

            ParsedNodeId pnd = (ParsedNodeId)handle.ParsedNodeId;

            // check for a new node.
            switch (pnd.RootType)
            {
                case NodeTypes.Folder:
                {
                    target = m_system.GetFolderState(SystemContext, pnd.RootId);
                    break;
                }

                case NodeTypes.Item:
                {
                    ArchiveItemState item = m_system.GetItemState(SystemContext, pnd);
                    item.LoadConfiguration(context);
                    target = item;
                    break;
                }
            }

            // root is not valid.
            if (target == null)
            {
                return null;
            }

            // validate component.
            if (!String.IsNullOrEmpty(pnd.ComponentPath))
            {
                NodeState component = target.FindChildBySymbolicName(context, pnd.ComponentPath);

                // component does not exist.
                if (component == null)
                {
                    return null;
                }

                target = component;
            }

            // put root into cache.
            if (cache != null)
            {
                cache[handle.NodeId] = target;
            }

            handle.Node = target;
            handle.Validated = true;
            return handle.Node;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Validates the nodes and reads the values from the underlying source.
        /// </summary>
        protected override void Read(
            ServerSystemContext context,
            IList<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId,NodeState> cache)
        {
            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];

                lock (Lock)
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // check if the node needs to be initialized from disk.
                    ArchiveItemState item = source.GetHierarchyRoot() as ArchiveItemState;

                    if (item != null && item.ArchiveItem.LastLoadTime.AddMinutes(10) < DateTime.UtcNow)
                    {
                        item.LoadConfiguration(context);
                    }

                    ReadValueId nodeToRead = nodesToRead[handle.Index];
                    DataValue value = values[handle.Index];

                    // update the attribute value.
                    errors[handle.Index] = source.ReadAttribute(
                        context,
                        nodeToRead.AttributeId,
                        nodeToRead.ParsedIndexRange,
                        nodeToRead.DataEncoding,
                        value);
                }
            }
        }

        /// <summary>
        /// Reads the initial value for a monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The item handle.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected override void ReadInitialValue(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem)
        {
            ArchiveItemState item = handle.Node as ArchiveItemState;

            if (item == null || monitoredItem.AttributeId != Attributes.Value)
            {
                base.ReadInitialValue(context, handle, monitoredItem);
                return;
            }

            AggregateFilter filter = monitoredItem.Filter as AggregateFilter;

            if (filter == null || filter.StartTime >= DateTime.UtcNow.AddMilliseconds(-filter.ProcessingInterval))
            {
                base.ReadInitialValue(context, handle, monitoredItem);
                return;
            }

            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            details.StartTime = filter.StartTime;
            details.EndTime = DateTime.UtcNow;
            details.ReturnBounds = true;
            details.IsReadModified = false;
            details.NumValuesPerNode = 0;

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = handle.NodeId;
            nodeToRead.ParsedIndexRange = NumericRange.Empty;

            try
            {
                HistoryReadRequest request = CreateHistoryReadRequest(
                    context,
                    details,
                    handle,
                    nodeToRead);

                while (request.Values.Count > 0)
                {
                    if (request.Values.Count == 0)
                    {
                        break;
                    }

                    DataValue value = request.Values.First.Value;
                    request.Values.RemoveFirst();
                    monitoredItem.QueueValue(value, null);
                }
            }
            catch (Exception e)
            {
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error fetching initial values.");
                monitoredItem.QueueValue(null, error);
            }
        }

        /// <summary>
        /// Called after creating a MonitoredItem.
        /// </summary>
        protected override void OnMonitoredItemCreated(ServerSystemContext context, NodeHandle handle, MonitoredItem monitoredItem)
        {
            lock (Lock)
            {
                NodeState root = handle.Node.GetHierarchyRoot();

                if (root != null)
                {
                    ArchiveItemState item = root as ArchiveItemState;

                    if (item != null)
                    {
                        if (m_monitoredItems == null)
                        {
                            m_monitoredItems = new Dictionary<string, ArchiveItemState>();
                        }

                        if (!m_monitoredItems.ContainsKey(item.ArchiveItem.UniquePath))
                        {
                            m_monitoredItems.Add(item.ArchiveItem.UniquePath, item);
                        }
                        
                        item.SubscribeCount++;

                        if (m_simulationTimer == null)
                        {
                            m_simulationTimer = new Timer(DoSimulation, null, 500, 500);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Revises an aggregate filter (may require knowledge of the variable being used). 
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle.</param>
        /// <param name="samplingInterval">The sampling interval for the monitored item.</param>
        /// <param name="queueSize">The queue size for the monitored item.</param>
        /// <param name="filterToUse">The filter to revise.</param>
        /// <returns>Good if the filter is acceptable.</returns>
        protected override StatusCode ReviseAggregateFilter(
            ServerSystemContext context,
            NodeHandle handle,
            double samplingInterval,
            uint queueSize,
            ServerAggregateFilter filterToUse)
        {
            // use the sampling interval to limit the processing interval.
            if (filterToUse.ProcessingInterval < samplingInterval)
            {
                filterToUse.ProcessingInterval = samplingInterval;
            }

            // check if an archive item.
            ArchiveItemState item = handle.Node as ArchiveItemState;

            if (item == null)
            {
                // no historial data so must start in the future.
                while (filterToUse.StartTime < DateTime.UtcNow)
                {
                    filterToUse.StartTime = filterToUse.StartTime.AddMilliseconds(filterToUse.ProcessingInterval);
                }

                // use suitable defaults for values which are are not archived items.
                filterToUse.AggregateConfiguration.UseServerCapabilitiesDefaults = false;
                filterToUse.AggregateConfiguration.UseSlopedExtrapolation = false;
                filterToUse.AggregateConfiguration.TreatUncertainAsBad = false;
                filterToUse.AggregateConfiguration.PercentDataBad = 100;
                filterToUse.AggregateConfiguration.PercentDataGood = 100;
                filterToUse.Stepped = true;
            }
            else
            {
                // use the archive acquisition sampling interval to limit the processing interval.
                if (filterToUse.ProcessingInterval < item.ArchiveItem.SamplingInterval)
                {
                    filterToUse.ProcessingInterval = item.ArchiveItem.SamplingInterval;
                }

                // ensure the buffer does not get overfilled.
                while (filterToUse.StartTime.AddMilliseconds(queueSize*filterToUse.ProcessingInterval) < DateTime.UtcNow)
                {
                    filterToUse.StartTime = filterToUse.StartTime.AddMilliseconds(filterToUse.ProcessingInterval);
                }

                filterToUse.Stepped = item.ArchiveItem.Stepped;

                // revise the configration.
                ReviseAggregateConfiguration(context, item, filterToUse.AggregateConfiguration);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Revises the aggregate configuration.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="item"></param>
        /// <param name="configurationToUse"></param>
        private void ReviseAggregateConfiguration(
            ServerSystemContext context,
            ArchiveItemState item,
            AggregateConfiguration configurationToUse)
        {
            // set configuration from defaults.
            if (configurationToUse.UseServerCapabilitiesDefaults)
            {
                AggregateConfiguration configuration = item.ArchiveItem.AggregateConfiguration;

                if (configuration == null || configuration.UseServerCapabilitiesDefaults)
                {
                    configuration = Server.AggregateManager.GetDefaultConfiguration(null);
                }

                configurationToUse.UseSlopedExtrapolation = configuration.UseSlopedExtrapolation;
                configurationToUse.TreatUncertainAsBad = configuration.TreatUncertainAsBad;
                configurationToUse.PercentDataBad = configuration.PercentDataBad;
                configurationToUse.PercentDataGood = configuration.PercentDataGood;
            }

            // override configuration when it does not make sense for the item.
            configurationToUse.UseServerCapabilitiesDefaults = false;

            if (item.ArchiveItem.Stepped)
            {
                configurationToUse.UseSlopedExtrapolation = false;
            }
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        protected override void OnMonitoredItemDeleted(ServerSystemContext context, NodeHandle handle, MonitoredItem monitoredItem)
        {
            lock (Lock)
            {
                NodeState root = handle.Node.GetHierarchyRoot();

                if (root != null)
                {
                    ArchiveItemState item = root as ArchiveItemState;

                    if (item != null)
                    {
                        ArchiveItemState item2 = root as ArchiveItemState;

                        if (m_monitoredItems.TryGetValue(item.ArchiveItem.UniquePath, out item2))
                        {
                            item2.SubscribeCount--;

                            if (item2.SubscribeCount == 0)
                            {
                                m_monitoredItems.Remove(item.ArchiveItem.UniquePath);
                            }

                            if (m_monitoredItems.Count == 0)
                            {
                                if (m_simulationTimer != null)
                                {
                                    m_simulationTimer.Dispose();
                                    m_simulationTimer = null;
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Historian Functions
        /// <summary>
        /// Reads the raw data for an item.
        /// </summary>
        protected override void HistoryReadRawModified(
            ServerSystemContext context, 
            ReadRawModifiedDetails details, 
            TimestampsToReturn timestampsToReturn, 
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult> results, 
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess, 
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];

                HistoryReadRequest request = null;

                try
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // load an exising request.
                    if (nodeToRead.ContinuationPoint != null)
                    {
                        request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                        if (request == null)
                        {
                            errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                            continue;
                        }
                    }

                    // create a new request.
                    else
                    {
                        request = CreateHistoryReadRequest(
                            context,
                            details,
                            handle,
                            nodeToRead);
                    }

                    // process values until the max is reached.
                    HistoryData data = (details.IsReadModified) ? new HistoryModifiedData() : new HistoryData();
                    HistoryModifiedData modifiedData = data as HistoryModifiedData;

                    while (request.NumValuesPerNode == 0 || data.DataValues.Count < request.NumValuesPerNode)
                    {
                        if (request.Values.Count == 0)
                        {
                            break;
                        }

                        DataValue value = request.Values.First.Value;
                        request.Values.RemoveFirst();
                        data.DataValues.Add(value);

                        if (modifiedData != null)
                        {
                            ModificationInfo modificationInfo = null;

                            if (request.ModificationInfos != null && request.ModificationInfos.Count > 0)
                            {
                                modificationInfo = request.ModificationInfos.First.Value;
                                request.ModificationInfos.RemoveFirst();
                            }

                            modifiedData.ModificationInfos.Add(modificationInfo);
                        }
                    }

                    errors[handle.Index] = ServiceResult.Good;

                    // check if a continuation point is requred.
                    if (request.Values.Count > 0)
                    {
                        // only set if both end time and start time are specified.
                        if (details.StartTime != DateTime.MinValue && details.EndTime != DateTime.MinValue)
                        {
                            result.ContinuationPoint = SaveContinuationPoint(context, request);
                        }
                    }

                    // check if no data returned.
                    else
                    {
                        errors[handle.Index] = StatusCodes.GoodNoData;
                    }

                    // return the data.
                    result.HistoryData = new ExtensionObject(data);
                }
                catch (Exception e)
                {
                    errors[handle.Index] = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error processing request.");
                }
            }
        }

        /// <summary>
        /// Reads the processed data for an item.
        /// </summary>
        protected override void HistoryReadProcessed(
            ServerSystemContext context,
            ReadProcessedDetails details,
            TimestampsToReturn timestampsToReturn,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];

                HistoryReadRequest request = null;

                try
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // load an exising request.
                    if (nodeToRead.ContinuationPoint != null)
                    {
                        request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                        if (request == null)
                        {
                            errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                            continue;
                        }
                    }

                    // create a new request.
                    else
                    {
                        // validate aggregate type.
                        if (details.AggregateType.Count <= ii || !Server.AggregateManager.IsSupported(details.AggregateType[ii]))
                        {
                            errors[handle.Index] = StatusCodes.BadAggregateNotSupported;
                            continue;
                        }

                        request = CreateHistoryReadRequest(
                            context,
                            details,
                            handle,
                            nodeToRead,
                            details.AggregateType[ii]);
                    }

                    // process values until the max is reached.
                    HistoryData data = new HistoryData();

                    while (request.NumValuesPerNode == 0 || data.DataValues.Count < request.NumValuesPerNode)
                    {
                        if (request.Values.Count == 0)
                        {
                            break;
                        }

                        DataValue value = request.Values.First.Value;
                        request.Values.RemoveFirst();
                        data.DataValues.Add(value);
                    }

                    errors[handle.Index] = ServiceResult.Good;

                    // check if a continuation point is requred.
                    if (request.Values.Count > 0)
                    {
                        result.ContinuationPoint = SaveContinuationPoint(context, request);
                    }

                    // check if no data returned.
                    else
                    {
                        errors[handle.Index] = StatusCodes.GoodNoData;
                    }

                    // return the data.
                    result.HistoryData = new ExtensionObject(data);
                }
                catch (Exception e)
                {
                    errors[handle.Index] = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error processing request.");
                }
            }
        }

        /// <summary>
        /// Reads the data at the specified time for an item.
        /// </summary>
        protected override void HistoryReadAtTime(
            ServerSystemContext context,
            ReadAtTimeDetails details,
            TimestampsToReturn timestampsToReturn,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];

                HistoryReadRequest request = null;
                
                try
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // load an exising request.
                    if (nodeToRead.ContinuationPoint != null)
                    {
                        request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                        if (request == null)
                        {
                            errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                            continue;
                        }
                    }

                    // create a new request.
                    else
                    {
                        request = CreateHistoryReadRequest(
                            context,
                            details,
                            handle,
                            nodeToRead);
                    }

                    // process values until the max is reached.
                    HistoryData data = new HistoryData();

                    while (request.NumValuesPerNode == 0 || data.DataValues.Count < request.NumValuesPerNode)
                    {
                        if (request.Values.Count == 0)
                        {
                            break;
                        }

                        DataValue value = request.Values.First.Value;
                        request.Values.RemoveFirst();
                        data.DataValues.Add(value);
                    }

                    errors[handle.Index] = ServiceResult.Good;

                    // check if a continuation point is requred.
                    if (request.Values.Count > 0)
                    {
                        result.ContinuationPoint = SaveContinuationPoint(context, request);
                    }

                    // check if no data returned.
                    else
                    {
                        errors[handle.Index] = StatusCodes.GoodNoData;
                    }

                    // return the data.
                    result.HistoryData = new ExtensionObject(data);
                }
                catch (Exception e)
                {
                    errors[handle.Index] = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error processing request.");
                }
            }
        }

        /// <summary>
        /// Updates the data history for one or more nodes.
        /// </summary>
        protected override void HistoryUpdateData(
            ServerSystemContext context,
            IList<UpdateDataDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                UpdateDataDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                try
                {
                    // remove not supported.
                    if (nodeToUpdate.PerformInsertReplace == PerformUpdateType.Remove)
                    {
                        continue;
                    }

                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // load the archive.
                    ArchiveItemState item = handle.Node as ArchiveItemState;

                    if (item == null)
                    {
                        continue;
                    }

                    item.ReloadFromSource(context);

                    // process each item.
                    for (int jj = 0; jj < nodeToUpdate.UpdateValues.Count; jj++)
                    {
                        StatusCode error = item.UpdateHistory(context, nodeToUpdate.UpdateValues[jj], nodeToUpdate.PerformInsertReplace);
                        result.OperationResults.Add(error);
                    }

                    errors[handle.Index] = ServiceResult.Good;
                }
                catch (Exception e)
                {
                    errors[handle.Index] = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error processing request.");
                }
            }
        }

        /// <summary>
        /// Updates the data history for one or more nodes.
        /// </summary>
        protected override void HistoryUpdateStructureData(
            ServerSystemContext context,
            IList<UpdateStructureDataDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                UpdateStructureDataDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                try
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // only support annotations.
                    if (handle.Node.BrowseName != Opc.Ua.BrowseNames.Annotations)
                    {
                        continue;
                    }

                    // load the archive.
                    ArchiveItemState item = Reload(context, handle);

                    if (item == null)
                    {
                        continue;
                    }

                    // process each item.
                    for (int jj = 0; jj < nodeToUpdate.UpdateValues.Count; jj++)
                    {
                        Annotation annotation = ExtensionObject.ToEncodeable(nodeToUpdate.UpdateValues[jj].Value as ExtensionObject) as Annotation;

                        if (annotation == null)
                        {
                            result.OperationResults.Add(StatusCodes.BadTypeMismatch);
                            continue;
                        }

                        StatusCode error = item.UpdateAnnotations(
                            context, 
                            annotation,
                            nodeToUpdate.UpdateValues[jj], 
                            nodeToUpdate.PerformInsertReplace);

                        result.OperationResults.Add(error);
                    }

                    errors[handle.Index] = ServiceResult.Good;
                }
                catch (Exception e)
                {
                    errors[handle.Index] = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error processing request.");
                }
            }
        }

        /// <summary>
        /// Deletes the data history for one or more nodes.
        /// </summary>
        protected override void HistoryDeleteRawModified(
            ServerSystemContext context,
            IList<DeleteRawModifiedDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                DeleteRawModifiedDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                try
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // load the archive.
                    ArchiveItemState item = handle.Node as ArchiveItemState;

                    if (item == null)
                    {
                        continue;
                    }

                    item.ReloadFromSource(context);

                    // delete the history.
                    item.DeleteHistory(context, nodeToUpdate.StartTime, nodeToUpdate.EndTime, nodeToUpdate.IsDeleteModified);
                    errors[handle.Index] = ServiceResult.Good;
                }
                catch (Exception e)
                {
                    errors[handle.Index] = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Error deleting data from archive.");
                }
            }
        }

        /// <summary>
        /// Deletes the data history for one or more nodes.
        /// </summary>
        protected override void HistoryDeleteAtTime(
            ServerSystemContext context,
            IList<DeleteAtTimeDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                DeleteAtTimeDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                try
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    // load the archive.
                    ArchiveItemState item = handle.Node as ArchiveItemState;

                    if (item == null)
                    {
                        continue;
                    }

                    item.ReloadFromSource(context);

                    // process each item.
                    for (int jj = 0; jj < nodeToUpdate.ReqTimes.Count; jj++)
                    {
                        StatusCode error = item.DeleteHistory(context, nodeToUpdate.ReqTimes[jj]);
                        result.OperationResults.Add(error);
                    }

                    errors[handle.Index] = ServiceResult.Good;
                }
                catch (Exception e)
                {
                    errors[handle.Index] = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error processing request.");
                }
            }
        }

        #region History Helpers
        /// <summary>
        /// Loads the archive item state from the underlying source.
        /// </summary>
        private ArchiveItemState Reload(ServerSystemContext context, NodeHandle handle)
        {
            ArchiveItemState item = handle.Node as ArchiveItemState;

            if (item == null)
            {
                BaseInstanceState property = handle.Node as BaseInstanceState;

                if (property != null)
                {
                    item = property.Parent as ArchiveItemState;
                }
            }

            if (item != null)
            {
                item.ReloadFromSource(context);
            }

            return item;
        }

        /// <summary>
        /// Creates a new history request.
        /// </summary>
        private HistoryReadRequest CreateHistoryReadRequest(
            ServerSystemContext context,
            ReadRawModifiedDetails details,
            NodeHandle handle,
            HistoryReadValueId nodeToRead)
        {
            bool sizeLimited = (details.StartTime == DateTime.MinValue || details.EndTime == DateTime.MinValue);
            bool applyIndexRangeOrEncoding = (nodeToRead.ParsedIndexRange != NumericRange.Empty || !QualifiedName.IsNull(nodeToRead.DataEncoding));
            bool returnBounds = !details.IsReadModified && details.ReturnBounds;
            bool timeFlowsBackward = (details.StartTime == DateTime.MinValue) || (details.EndTime != DateTime.MinValue && details.EndTime < details.StartTime);

            // find the archive item.
            ArchiveItemState item = Reload(context, handle);

            if (item == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            LinkedList<DataValue> values = new LinkedList<DataValue>();
            LinkedList<ModificationInfo> modificationInfos = null;

            if (details.IsReadModified)
            {
                modificationInfos = new LinkedList<ModificationInfo>();
            }

            // read history. 
            DataView view = item.ReadHistory(details.StartTime, details.EndTime, details.IsReadModified, handle.Node.BrowseName);

            int startBound = -1;
            int endBound = -1;
            int ii = (timeFlowsBackward)?view.Count-1:0;

            while (ii >= 0 && ii < view.Count)
            {
                try
                {
                    DateTime timestamp = (DateTime)view[ii].Row[0];

                    // check if looking for start of data.
                    if (values.Count == 0)
                    {
                        if (timeFlowsBackward)
                        {
                            if ((details.StartTime != DateTime.MinValue && timestamp >= details.StartTime) || (details.StartTime == DateTime.MinValue &&  timestamp >= details.EndTime))
                            {
                                startBound = ii;

                                if (timestamp > details.StartTime)
                                {
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            if (timestamp <= details.StartTime)
                            {
                                startBound = ii;

                                if (timestamp < details.StartTime)
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    // check if absolute max values specified.
                    if (sizeLimited)
                    {
                        if (details.NumValuesPerNode > 0 && details.NumValuesPerNode < values.Count)
                        {
                            break;
                        }
                    }

                    // check for end bound.
                    if (details.EndTime != DateTime.MinValue && timestamp >= details.EndTime)
                    {
                        if (timeFlowsBackward)
                        {
                            if (timestamp <= details.EndTime)
                            {
                                endBound = ii;
                                break;
                            }
                        }
                        else
                        {
                            if (timestamp >= details.EndTime)
                            {
                                endBound = ii;
                                break;
                            }
                        }
                    }

                    // check if the start bound needs to be returned.
                    if (returnBounds && values.Count == 0 && startBound != ii && details.StartTime != DateTime.MinValue)
                    {
                        // add start bound.
                        if (startBound == -1)
                        {
                            values.AddLast(new DataValue(Variant.Null, StatusCodes.BadBoundNotFound, details.StartTime, details.StartTime));
                        }
                        else
                        {
                            values.AddLast(RowToDataValue(context, nodeToRead, view[startBound], applyIndexRangeOrEncoding));
                        }

                        // check if absolute max values specified.
                        if (sizeLimited)
                        {
                            if (details.NumValuesPerNode > 0 && details.NumValuesPerNode < values.Count)
                            {
                                break;
                            }
                        }
                    }

                    // add value.
                    values.AddLast(RowToDataValue(context, nodeToRead, view[ii], applyIndexRangeOrEncoding));

                    if (modificationInfos != null)
                    {
                        modificationInfos.AddLast((ModificationInfo)view[ii].Row[6]);
                    }
                }
                finally
                {
                    if (timeFlowsBackward)
                    {
                        ii--;
                    }
                    else
                    {
                        ii++;
                    }
                }
            }

            // add late bound.
            while (returnBounds && details.EndTime != DateTime.MinValue)
            {
                // add start bound.
                if (values.Count == 0)
                {
                    if (startBound == -1)
                    {
                        values.AddLast(new DataValue(Variant.Null, StatusCodes.BadBoundNotFound, details.StartTime, details.StartTime));
                    }
                    else
                    {
                        values.AddLast(RowToDataValue(context, nodeToRead, view[startBound], applyIndexRangeOrEncoding));
                    }
                }

                // check if absolute max values specified.
                if (sizeLimited)
                {
                    if (details.NumValuesPerNode > 0 && details.NumValuesPerNode < values.Count)
                    {
                        break;
                    }
                }

                // add end bound.
                if (endBound == -1)
                {
                    values.AddLast(new DataValue(Variant.Null, StatusCodes.BadBoundNotFound, details.EndTime, details.EndTime));
                }
                else
                {
                    values.AddLast(RowToDataValue(context, nodeToRead, view[endBound], applyIndexRangeOrEncoding));
                }

                break;
            }

            HistoryReadRequest request = new HistoryReadRequest();
            request.Values = values;
            request.ModificationInfos = modificationInfos;
            request.NumValuesPerNode = details.NumValuesPerNode;
            request.Filter = null;
            return request;
        }

        /// <summary>
        /// Creates a new history request.
        /// </summary>
        private HistoryReadRequest CreateHistoryReadRequest(
            ServerSystemContext context,
            ReadProcessedDetails details,
            NodeHandle handle,
            HistoryReadValueId nodeToRead,
            NodeId aggregateId)
        {
            bool applyIndexRangeOrEncoding = (nodeToRead.ParsedIndexRange != NumericRange.Empty || !QualifiedName.IsNull(nodeToRead.DataEncoding));
            bool timeFlowsBackward = (details.EndTime < details.StartTime);

            ArchiveItemState item = handle.Node as ArchiveItemState;

            if (item == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            item.ReloadFromSource(context);

            LinkedList<DataValue> values = new LinkedList<DataValue>();

            // read history. 
            DataView view = item.ReadHistory(details.StartTime, details.EndTime, false);

            int ii = (timeFlowsBackward) ? view.Count - 1 : 0;

            // choose the aggregate configuration.
            AggregateConfiguration configuration = (AggregateConfiguration)details.AggregateConfiguration.MemberwiseClone();
            ReviseAggregateConfiguration(context, item, configuration);

            // create the aggregate calculator.
            IAggregateCalculator calculator = Server.AggregateManager.CreateCalculator(
                aggregateId,
                details.StartTime,
                details.EndTime,
                details.ProcessingInterval,
                item.ArchiveItem.Stepped,
                configuration);

            while (ii >= 0 && ii < view.Count)
            {
                try
                {
                    DataValue value = (DataValue)view[ii].Row[2];
                    calculator.QueueRawValue(value);

                    // queue any processed values.
                    QueueProcessedValues(
                        context,
                        calculator,
                        nodeToRead.ParsedIndexRange,
                        nodeToRead.DataEncoding,
                        applyIndexRangeOrEncoding,
                        false,
                        values);
                }
                finally
                {
                    if (timeFlowsBackward)
                    {
                        ii--;
                    }
                    else
                    {
                        ii++;
                    }
                }
            }

            // queue any processed values beyond the end of the data.
            QueueProcessedValues(
                context,
                calculator,
                nodeToRead.ParsedIndexRange,
                nodeToRead.DataEncoding,
                applyIndexRangeOrEncoding,
                true,
                values);

            HistoryReadRequest request = new HistoryReadRequest();
            request.Values = values;
            request.NumValuesPerNode = 0;
            request.Filter = null;
            return request;
        }

        /// <summary>
        /// Creates a new history request.
        /// </summary>
        private HistoryReadRequest CreateHistoryReadRequest(
            ServerSystemContext context,
            ReadAtTimeDetails details,
            NodeHandle handle,
            HistoryReadValueId nodeToRead)
        {
            bool applyIndexRangeOrEncoding = (nodeToRead.ParsedIndexRange != NumericRange.Empty || !QualifiedName.IsNull(nodeToRead.DataEncoding));

            ArchiveItemState item = handle.Node as ArchiveItemState;

            if (item == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            item.ReloadFromSource(context);

            // find the start and end times.
            DateTime startTime = DateTime.MaxValue;
            DateTime endTime = DateTime.MinValue;

            for (int ii = 0; ii < details.ReqTimes.Count; ii++)
            {
                if (startTime > details.ReqTimes[ii])
                {
                    startTime = details.ReqTimes[ii];
                }

                if (endTime < details.ReqTimes[ii])
                {
                    endTime = details.ReqTimes[ii];
                }
            }
            
            DataView view = item.ReadHistory(startTime, endTime, false);

            LinkedList<DataValue> values = new LinkedList<DataValue>();

            for (int ii = 0; ii < details.ReqTimes.Count; ii++)
            {
                bool dataBeforeIgnored = false;
                bool dataAfterIgnored = false;

                // find the value at the time.
                int index = item.FindValueAtOrBefore(view, details.ReqTimes[ii], !details.UseSimpleBounds, out dataBeforeIgnored);

                if (index < 0)
                {
                    values.AddLast(new DataValue(StatusCodes.BadNoData, details.ReqTimes[ii]));
                    continue;
                }

                // nothing more to do if a raw value exists.
                if ((DateTime)view[index].Row[0] == details.ReqTimes[ii])
                {
                    values.AddLast((DataValue)view[index].Row[2]);
                    continue;
                }
                
                DataValue before = (DataValue)view[index].Row[2];
                DataValue value;

                // find the value after the time.
                int afterIndex = item.FindValueAfter(view, index, !details.UseSimpleBounds, out dataAfterIgnored);

                if (afterIndex < 0)
                {
                    // use stepped interpolation if no end bound exists.
                    value = AggregateCalculator.SteppedInterpolate(details.ReqTimes[ii], before);

                    if (StatusCode.IsNotBad(value.StatusCode) && dataBeforeIgnored)
                    {
                        value.StatusCode = value.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }

                    values.AddLast(value);
                    continue;
                }

                // use stepped or slopped interpolation depending on the value.
                if (item.ArchiveItem.Stepped)
                {
                    value = AggregateCalculator.SteppedInterpolate(details.ReqTimes[ii], before);

                    if (StatusCode.IsNotBad(value.StatusCode) && dataBeforeIgnored)
                    {
                        value.StatusCode = value.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }
                }
                else
                {
                    value = AggregateCalculator.SlopedInterpolate(details.ReqTimes[ii], before, (DataValue)view[afterIndex].Row[2]);

                    if (StatusCode.IsNotBad(value.StatusCode) && (dataBeforeIgnored || dataAfterIgnored))
                    {
                        value.StatusCode = value.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }
                }

                values.AddLast(value);
            }

            HistoryReadRequest request = new HistoryReadRequest();
            request.Values = values;
            request.NumValuesPerNode = 0;
            request.Filter = null;
            return request;
        }

        /// <summary>
        /// Extracts and queues any processed values.
        /// </summary>
        private void QueueProcessedValues(
            ServerSystemContext context, 
            IAggregateCalculator calculator, 
            NumericRange indexRange,
            QualifiedName dataEncoding,
            bool applyIndexRangeOrEncoding,
            bool returnPartial,
            LinkedList<DataValue> values)
        {
            DataValue proccessedValue = calculator.GetProcessedValue(returnPartial);

            while (proccessedValue != null)
            {
                // apply any index range or encoding.
                if (applyIndexRangeOrEncoding)
                {
                    object rawValue = proccessedValue.Value;
                    ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(context, indexRange, dataEncoding, ref rawValue);

                    if (ServiceResult.IsBad(result))
                    {
                        proccessedValue.Value = rawValue;
                    }
                    else
                    {
                        proccessedValue.Value = null;
                        proccessedValue.StatusCode = result.StatusCode;
                    }
                }

                // queue the result.
                values.AddLast(proccessedValue);
                proccessedValue = calculator.GetProcessedValue(returnPartial);
            }
        }

        /// <summary>
        /// Creates a new history request.
        /// </summary>
        private DataValue RowToDataValue(
            ServerSystemContext context,
            HistoryReadValueId nodeToRead,
            DataRowView row,
            bool applyIndexRangeOrEncoding)
        {
            DataValue value = (DataValue)row[2];

            // apply any index range or encoding.
            if (applyIndexRangeOrEncoding)
            {
                object rawValue = value.Value;
                ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(context, nodeToRead.ParsedIndexRange, nodeToRead.DataEncoding, ref rawValue);

                if (ServiceResult.IsBad(result))
                {
                    value.Value = rawValue;
                }
                else
                {
                    value.Value = null;
                    value.StatusCode = result.StatusCode;
                }
            }

            return value;
        }

        /// <summary>
        /// Stores a read history request.
        /// </summary>
        private class HistoryReadRequest
        {
            public byte[] ContinuationPoint;
            public LinkedList<DataValue> Values;
            public LinkedList<ModificationInfo> ModificationInfos;
            public uint NumValuesPerNode;
            public AggregateFilter Filter;
        }

        /// <summary>
        /// Releases the history continuation point.
        /// </summary>
        protected override void HistoryReleaseContinuationPoints(
            ServerSystemContext context,
            IList<HistoryReadValueId> nodesToRead,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];

                // find the continuation point.
                HistoryReadRequest request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                if (request == null)
                {
                    errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                    continue;
                }

                // all done.
                errors[handle.Index] = StatusCodes.Good;
            }
        }

        /// <summary>
        /// Loads a history continuation point.
        /// </summary>
        private HistoryReadRequest LoadContinuationPoint(
            ServerSystemContext context,
            byte[] continuationPoint)
        {
            Session session = context.OperationContext.Session;

            if (session == null)
            {
                return null;
            }

            HistoryReadRequest request = session.RestoreHistoryContinuationPoint(continuationPoint) as HistoryReadRequest;

            if (request == null)
            {
                return null;
            }

            return request;
        }

        /// <summary>
        /// Saves a history continuation point.
        /// </summary>
        private byte[] SaveContinuationPoint(
            ServerSystemContext context,
            HistoryReadRequest request)
        {
            Session session = context.OperationContext.Session;

            if (session == null)
            {
                return null;
            }

            Guid id = Guid.NewGuid();
            session.SaveHistoryContinuationPoint(id, request);
            request.ContinuationPoint = id.ToByteArray();
            return request.ContinuationPoint;
        }
        #endregion
        #endregion

        #region Private Methods
        /// <summary>
        /// Runs the simulation.
        /// </summary>
        private void DoSimulation(object state)
        {
            try
            {
                lock (Lock)
                {
                    foreach (ArchiveItemState item in m_monitoredItems.Values)
                    {
                        if (item.ArchiveItem.LastLoadTime.AddSeconds(10) < DateTime.UtcNow)
                        {
                            item.LoadConfiguration(SystemContext);
                        }

                        foreach (DataValue value in item.NewSamples(SystemContext))
                        {
                            item.WrappedValue = value.WrappedValue;
                            item.Timestamp = value.SourceTimestamp;
                            item.StatusCode = value.StatusCode;
                            item.ClearChangeMasks(SystemContext, true);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace("Unexpected error during simulation: {0}", e.Message);
            }
        }
        #endregion

        #region Private Fields
        private UnderlyingSystem m_system;
        private HistoricalAccessServerConfiguration m_configuration;
        private Timer m_simulationTimer;
        private Dictionary<string,ArchiveItemState> m_monitoredItems;
        #endregion
    }
}
