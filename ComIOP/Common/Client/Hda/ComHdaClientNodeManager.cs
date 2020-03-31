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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class ComHdaClientNodeManager : ComClientNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ComHdaClientNodeManager(IServerInternal server, string namespaceUri, ComHdaClientConfiguration configuration, bool ownsTypeModel)
        :
            base(server, namespaceUri, ownsTypeModel)
        {
            SystemContext.SystemHandle = m_system = new ComHdaClientManager();
            SystemContext.NodeIdFactory = this;

            // save the configuration for the node manager.
            m_configuration = configuration;

            // set the alias root.
            AliasRoot = m_configuration.ServerName;

            if (String.IsNullOrEmpty(AliasRoot))
            {
                AliasRoot = "HDA";
            }

            // set the default parser if none provided.
            if (configuration.ItemIdParser == null)
            {
                configuration.ItemIdParser = new ComItemIdParser();
            }

            // set default parameters.
            if (m_configuration.AttributeSamplingInterval == 0)
            {
                m_configuration.AttributeSamplingInterval = 1000;
            }

            // create the list of subscriptions.
            m_subscriptionManagers = new Dictionary<int, HdaSubscribeRequestManager>();
            m_subscriptionManagers[ComUtils.LOCALE_SYSTEM_DEFAULT] = new HdaSubscribeRequestManager(SystemContext, ComUtils.LOCALE_SYSTEM_DEFAULT, m_configuration);
            m_monitoredItems = new Dictionary<uint, HdaSubscribeRequestManager>();
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
                m_system.Dispose();
            }

            base.Dispose(disposing);
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

            if (instance != null && instance.Parent == null)
            {
                return instance.NodeId;
            }

            return HdaModelUtils.ConstructIdForComponent(node, NamespaceIndex);
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                // check if the type model needs to be loaded.
                if (NamespaceIndexes.Length > 1)
                {
                    LoadPredefinedNodes(SystemContext, externalReferences);
                }

                // create the root node.
                string serverName = m_configuration.ServerName;

                if (String.IsNullOrEmpty(serverName))
                {
                    serverName = "ComHdaServer";
                }

                HdaBranchState root = new HdaBranchState(String.Empty, serverName, NamespaceIndex);
                root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

                // link root to objects folder.
                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));

                // create the status node.
                ComServerStatusState status = new ComServerStatusState(root);
                status.ReferenceTypeId = ReferenceTypeIds.Organizes;

                // get the type namepace for the browse name.
                int typeNamepaceIndex = Server.NamespaceUris.GetIndex(Namespaces.ComInterop);

                if (typeNamepaceIndex < 0)
                {
                    typeNamepaceIndex = NamespaceIndex;
                }

                status.Create(
                    SystemContext,
                    HdaModelUtils.ConstructIdForInternalNode("ServerStatus", NamespaceIndex),
                    new QualifiedName("ServerStatus", (ushort)typeNamepaceIndex),
                    null,
                    true);

                root.AddChild(status);

                // store root folder in the pre-defined nodes.
                AddPredefinedNode(SystemContext, root);

                // create the server capabilities object.
                HistoryServerCapabilitiesState capabilities = m_capabilities = new HistoryServerCapabilitiesState(null);

                CreateNode(
                    SystemContext,
                    root.NodeId,
                    ReferenceTypeIds.Organizes,
                    new QualifiedName(Opc.Ua.BrowseNames.HistoryServerCapabilities),
                    capabilities);

                capabilities.AccessHistoryDataCapability.Value = true;
                capabilities.AccessHistoryEventsCapability.Value = false;
                capabilities.MaxReturnDataValues.Value = 0;
                capabilities.MaxReturnEventValues.Value = 0;
                capabilities.ReplaceDataCapability.Value = false;
                capabilities.UpdateDataCapability.Value = false;
                capabilities.InsertEventCapability.Value = false;
                capabilities.ReplaceEventCapability.Value = false;
                capabilities.UpdateEventCapability.Value = false;
                capabilities.InsertAnnotationCapability.Value = false;
                capabilities.InsertDataCapability.Value = false;
                capabilities.DeleteRawCapability.Value = false;
                capabilities.DeleteAtTimeCapability.Value = false;

                AddPredefinedNode(SystemContext, capabilities);

                // create the default aggregate configuration object.
                AggregateConfigurationState aggregateConfiguration = new AggregateConfigurationState(null);
                aggregateConfiguration.ReferenceTypeId = ReferenceTypeIds.Organizes;

                aggregateConfiguration.Create(
                    SystemContext,
                    HdaModelUtils.ConstructIdForInternalNode(Opc.Ua.BrowseNames.AggregateConfiguration, NamespaceIndex),
                    Opc.Ua.BrowseNames.AggregateConfiguration,
                    null,
                    true);

                aggregateConfiguration.TreatUncertainAsBad.Value = m_configuration.TreatUncertainAsBad;
                aggregateConfiguration.PercentDataBad.Value = m_configuration.PercentDataBad;
                aggregateConfiguration.PercentDataGood.Value = m_configuration.PercentDataGood;
                aggregateConfiguration.UseSlopedExtrapolation.Value = m_configuration.SteppedSlopedExtrapolation;

                AddPredefinedNode(SystemContext, aggregateConfiguration);
                                
                // create the COM server.
                m_system.Initialize(SystemContext, m_configuration, status, Lock, OnServerReconnected);
                StartMetadataUpdates(DoMetadataUpdate, null, 5000, m_configuration.MaxReconnectWait);
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                base.DeleteAddressSpace();
            }
        }

        /// <summary>
        /// Called to create the browser for the item configuration.
        /// </summary>
        private NodeBrowser OnCreateItemConfigurationBrowser(
            ISystemContext context,
            NodeState node,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            HdaParsedNodeId nodeId = HdaParsedNodeId.Parse(node.NodeId);

            if (nodeId == null)
            {
                return null;
            }

            return new HdaElementBrower(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly,
                nodeId.RootId,
                Opc.Ua.ObjectTypeIds.HistoricalDataConfigurationType,
                Opc.Ua.BrowseNames.HAConfiguration,
                NamespaceIndex);
        }

        /// <summary>
        /// Called when client manager has reconnected to the COM server.
        /// </summary>
        public void OnServerReconnected(object state)
        {
            try
            {
                foreach (HdaSubscribeRequestManager manager in m_subscriptionManagers.Values)
                {
                    try
                    {
                        manager.RecreateItems();
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Could not re-create subscription after reconnect for locale {0}.", new System.Globalization.CultureInfo(manager.LocaleId).DisplayName);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not re-create subscriptions after reconnect.");
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

                // check cache.
                if (cache != null)
                {
                    NodeState node = null;

                    if (cache.TryGetValue(nodeId, out node))
                    {
                        return new NodeHandle(nodeId, node);
                    }
                }

                NodeHandle handle = null;

                try
                {
                    // check for predefined nodes.
                    if (PredefinedNodes != null)
                    {
                        NodeState node = null;

                        if (PredefinedNodes.TryGetValue(nodeId, out node))
                        {
                            return handle = new NodeHandle(nodeId, node);
                        }
                    }

                    // parse the identifier.
                    HdaParsedNodeId parsedNodeId = HdaParsedNodeId.Parse(nodeId);

                    if (parsedNodeId != null)
                    {
                        handle = new NodeHandle();

                        handle.NodeId = nodeId;
                        handle.Validated = false;
                        handle.Node = null;
                        handle.ParsedNodeId = parsedNodeId;

                        return handle;
                    }
                }
                finally
                {
                    if (handle != null && handle.Node != null && cache != null)
                    {
                        cache.Add(nodeId, handle.Node);
                    }
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
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            NodeState target = null;

            // check if already in the cache.
            if (cache != null)
            {
                if (cache.TryGetValue(handle.NodeId, out target))
                {
                    // nulls mean a NodeId which was previously found to be invalid has been referenced again.
                    if (target == null)
                    {
                        return null;
                    }

                    handle.Node = target;
                    handle.Validated = true;
                    return handle.Node;
                }

                target = null;
            }

            try
            {
                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    return null;
                }

                ComHdaClient client = (ComHdaClient)m_system.SelectClient(context, false);

                switch (parsedNodeId.RootType)
                {
                    case HdaModelUtils.HdaBranch:
                    {
                        ComHdaBrowserClient browser = new ComHdaBrowserClient(client, null);
                        target = browser.FindBranch(context, parsedNodeId.RootId, NamespaceIndex);
                        browser.Dispose();
                        break;
                    }

                    case HdaModelUtils.HdaItem:
                    {
                        HdaItem[] items = client.GetItems(parsedNodeId.RootId);

                        if (items[0].Error < 0)
                        {
                            return null;
                        }

                        try
                        {
                            string browseName = null;

                            if (!m_configuration.ItemIdParser.Parse(client, m_configuration, parsedNodeId.RootId, out browseName))
                            {
                                HdaAttributeValue[] attributes = client.ReadAttributeValues(items[0].ServerHandle, OpcRcw.Hda.Constants.OPCHDA_ITEMID);
                                browseName = attributes[0].Value as string;
                            }

                            target = new HdaItemState(items[0].ItemId, browseName, NamespaceIndex);
                        }
                        finally
                        {
                            client.ReleaseItemHandles(items);
                        }

                        break;
                    }

                    case HdaModelUtils.HdaItemAttribute:
                    {
                        bool[] results = client.ValidateItemIds(parsedNodeId.RootId);

                        if (!results[0])
                        {
                            return null;
                        }

                        target = client.FindItemAttribute(parsedNodeId.RootId, parsedNodeId.AttributeId, NamespaceIndex);
                        break;
                    }

                    case HdaModelUtils.HdaItemAnnotations:
                    {
                        bool[] results = client.ValidateItemIds(parsedNodeId.RootId);

                        if (!results[0])
                        {
                            return null;
                        }

                        target = client.FindItemAnnotations(parsedNodeId.RootId, NamespaceIndex);
                        break;
                    }

                    case HdaModelUtils.HdaItemConfiguration:
                    {
                        bool[] results = client.ValidateItemIds(parsedNodeId.RootId);

                        if (results == null || !results[0])
                        {
                            return null;
                        }

                        target = HdaModelUtils.GetItemConfigurationNode(parsedNodeId.RootId, NamespaceIndex);
                        target.OnCreateBrowser = OnCreateItemConfigurationBrowser;
                        break;
                    }

                    case HdaModelUtils.HdaAggregate:
                    {
                        target = client.FindAggregate(parsedNodeId.AggregateId, NamespaceIndex);
                        break;
                    }
                }

                // check if found.
                if (target == null)
                {
                    return null;
                }

                // found a valid component.
                handle.Validated = true;
                handle.Node = target;

                return handle.Node;
            }
            finally
            {
                // store the node in the cache to optimize subsequent lookups.
                if (cache != null)
                {
                    cache.Add(handle.NodeId, target);
                }
            }
        }
        #endregion

        #region Overridden Methods
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
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                UpdateDataDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    errors[handle.Index] = StatusCodes.BadNodeIdInvalid;
                    continue;
                }

                // read the history of an item.
                if (parsedNodeId.RootType == HdaModelUtils.HdaItem)
                {
                    errors[handle.Index] = client.UpdateData(parsedNodeId.RootId, nodeToUpdate, results[handle.Index]);
                    continue;
                }

                // read the annotations of an item.
                if (parsedNodeId.RootType == HdaModelUtils.HdaItemAnnotations)
                {
                    errors[handle.Index] = client.InsertAnnotations(parsedNodeId.RootId, nodeToUpdate, results[handle.Index]);
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
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
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                DeleteRawModifiedDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    errors[handle.Index] = StatusCodes.BadNodeIdInvalid;
                    continue;
                }

                if (nodeToUpdate.IsDeleteModified)
                {
                    errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
                    continue;
                }

                if (parsedNodeId.RootType == HdaModelUtils.HdaItem)
                {
                    errors[handle.Index] = client.DeleteRaw(parsedNodeId.RootId, nodeToUpdate, results[handle.Index]);
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
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
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                DeleteAtTimeDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    errors[handle.Index] = StatusCodes.BadNodeIdInvalid;
                    continue;
                }

                if (parsedNodeId.RootType == HdaModelUtils.HdaItem)
                {
                    errors[handle.Index] = client.DeleteAtTime(parsedNodeId.RootId, nodeToUpdate, results[handle.Index]);
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Validates the nodes and reads the values from the underlying source.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="nodesToRead">The nodes to read.</param>
        /// <param name="values">The values.</param>
        /// <param name="errors">The errors.</param>
        /// <param name="nodesToValidate">The nodes to validate.</param>
        /// <param name="cache">The cache.</param>
        protected override void Read(
            ServerSystemContext context,
            IList<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache)
        {
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            HdaReadRequestCollection requests = new HdaReadRequestCollection();

            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];

                lock (Lock)
                {
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        continue;
                    }

                    DataValue value = values[handle.Index];
                    ReadValueId nodeToRead = nodesToRead[handle.Index];

                    // determine if request can be sent to the server.
                    bool queued = false;
                    errors[handle.Index] = requests.Add(source, nodeToRead, out queued);

                    if (queued)
                    {
                        continue;
                    }

                    // read built-in metadata.
                    errors[handle.Index] = source.ReadAttribute(
                        context,
                        nodeToRead.AttributeId,
                        nodeToRead.ParsedIndexRange,
                        nodeToRead.DataEncoding,
                        value);
                }
            }

            // read the attributes.
            if (requests.Count > 0)
            {
                client.Read(requests, false);
            }

            // extract the values from the results.
            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];
                DataValue value = values[handle.Index];
                ReadValueId nodeToRead = nodesToRead[handle.Index];

                lock (Lock)
                {
                    if (!requests.HasResult(nodeToRead))
                    {
                        continue;
                    }

                    errors[handle.Index] = requests.GetResult(context, handle.Node, nodeToRead, value, context.DiagnosticsMask);
                }
            }
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
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];

                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    errors[handle.Index] = StatusCodes.BadNodeIdInvalid;
                    continue;
                }

                // find the continuation point.
                HdaHistoryReadRequest request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

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
        /// Reads the history of an HDA item.
        /// </summary>
        private ServiceResult HistoryReadItem(
            ServerSystemContext context,
            ComHdaClient client,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HdaParsedNodeId parsedNodeId,
            HistoryReadResult result)
        { 
            // create the request or load it from a continuation point.
            HdaHistoryReadRawModifiedRequest request = null;

            if (nodeToRead.ContinuationPoint == null)
            {
                request = new HdaHistoryReadRawModifiedRequest(parsedNodeId.RootId, details, nodeToRead);
            }
            else
            {
                request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint) as HdaHistoryReadRawModifiedRequest;

                if (request == null)
                {
                    return StatusCodes.BadContinuationPointInvalid;
                }
            }

            // fetch the data.
            result.StatusCode = client.ReadHistory(request);

            // fill in the results.
            if (request.Results != null)
            {
                HistoryData data = (request.IsReadModified)?new HistoryModifiedData():new HistoryData();

                if (request.IsReadModified)
                {
                    ((HistoryModifiedData)data).ModificationInfos = request.ModificationInfos;
                }

                data.DataValues = request.Results;
                result.HistoryData = new ExtensionObject(data);
            }

            // create a new continuation point.
            if (!request.Completed)
            {
                result.ContinuationPoint = SaveContinuationPoint(context, request);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Reads the history of an HDA attribute.
        /// </summary>
        private ServiceResult HistoryReadAttribute(
            ServerSystemContext context,
            ComHdaClient client,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HdaParsedNodeId parsedNodeId,
            HistoryReadResult result)
        {
            // create the request or load it from a continuation point.
            HdaHistoryReadAttributeRequest request = null;

            if (nodeToRead.ContinuationPoint == null)
            {
                // create a new request.
                request = new HdaHistoryReadAttributeRequest(parsedNodeId.RootId, parsedNodeId.AttributeId, details, nodeToRead);

                // fetch all of the data at once.
                result.StatusCode = client.ReadAttributeHistory(request);
            }
            else
            {
                request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint) as HdaHistoryReadAttributeRequest;

                if (request == null)
                {
                    return StatusCodes.BadContinuationPointInvalid;
                }
            }

            // select a subset of the results.
            if (StatusCode.IsGood(result.StatusCode))
            {
                request.Results = new DataValueCollection();
                request.GetHistoryResults(context, nodeToRead, request.Results);
            }
            
            // fill in the results.
            if (request.Results != null)
            {
                HistoryData data = new HistoryData();                
                data.DataValues = request.Results;
                result.HistoryData = new ExtensionObject(data);
            }

            // create a new continuation point.
            if (!request.Completed)
            {
                result.ContinuationPoint = SaveContinuationPoint(context, request);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Reads the history of an HDA item annotations.
        /// </summary>
        private ServiceResult HistoryReadAnnotations(
            ServerSystemContext context,
            ComHdaClient client,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HdaParsedNodeId parsedNodeId,
            HistoryReadResult result)
        {
            // create the request or load it from a continuation point.
            HdaHistoryReadAnnotationRequest request = null;

            if (nodeToRead.ContinuationPoint == null)
            {
                // create a new request.
                request = new HdaHistoryReadAnnotationRequest(parsedNodeId.RootId, details, nodeToRead);

                // fetch all of the data at once.
                result.StatusCode = client.ReadAnnotationHistory(request);
            }
            else
            {
                request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint) as HdaHistoryReadAnnotationRequest;

                if (request == null)
                {
                    return StatusCodes.BadContinuationPointInvalid;
                }
            }

            // select a subset of the results.
            if (StatusCode.IsGood(result.StatusCode))
            {
                request.Results = new DataValueCollection();
                request.GetHistoryResults(context, nodeToRead, request.Results);
            }

            // fill in the results.
            if (request.Results != null)
            {
                HistoryData data = new HistoryData();
                data.DataValues = request.Results;
                result.HistoryData = new ExtensionObject(data);
            }

            // create a new continuation point.
            if (!request.Completed)
            {
                result.ContinuationPoint = SaveContinuationPoint(context, request);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Reads raw history data.
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
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];

                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    errors[handle.Index] = StatusCodes.BadNodeIdInvalid;
                    continue;
                }

                // read the history of an item.
                if (parsedNodeId.RootType == HdaModelUtils.HdaItem)
                {
                    errors[handle.Index] = HistoryReadItem(
                        context,
                        client,
                        details,
                        timestampsToReturn,
                        nodeToRead,
                        parsedNodeId,
                        result);

                    continue;
                }

                // read the history of an attribute.
                if (parsedNodeId.RootType == HdaModelUtils.HdaItemAttribute)
                {
                    errors[handle.Index] = HistoryReadAttribute(
                        context,
                        client,
                        details,
                        timestampsToReturn,
                        nodeToRead,
                        parsedNodeId,
                        result);

                    continue;
                }

                // read the annotations of an item.
                if (parsedNodeId.RootType == HdaModelUtils.HdaItemAnnotations)
                {
                    errors[handle.Index] = HistoryReadAnnotations(
                        context,
                        client,
                        details,
                        timestampsToReturn,
                        nodeToRead,
                        parsedNodeId,
                        result);

                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Reads processed history data.
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
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];

                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    errors[handle.Index] = StatusCodes.BadNodeIdInvalid;
                    continue;
                }

                // read the history of an item.
                if (parsedNodeId.RootType == HdaModelUtils.HdaItem)
                {
                    errors[handle.Index] = HistoryReadAtTime(
                        context,
                        client,
                        details,
                        timestampsToReturn,
                        nodeToRead,
                        parsedNodeId,
                        result);

                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Reads the history of an HDA item.
        /// </summary>
        private ServiceResult HistoryReadAtTime(
            ServerSystemContext context,
            ComHdaClient client,
            ReadAtTimeDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HdaParsedNodeId parsedNodeId,
            HistoryReadResult result)
        {
            // create the request or load it from a continuation point.
            HdaHistoryReadAtTimeRequest request = null;

            if (nodeToRead.ContinuationPoint == null)
            {
                request = new HdaHistoryReadAtTimeRequest(parsedNodeId.RootId, details, nodeToRead);
            }
            else
            {
                request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint) as HdaHistoryReadAtTimeRequest;

                if (request == null)
                {
                    return StatusCodes.BadContinuationPointInvalid;
                }
            }

            // fetch the data.
            result.StatusCode = client.ReadHistory(request);

            // fill in the results.
            if (request.Results != null)
            {
                HistoryData data = new HistoryData();
                data.DataValues = request.Results;
                result.HistoryData = new ExtensionObject(data);
            }

            // create a new continuation point.
            if (!request.Completed)
            {
                result.ContinuationPoint = SaveContinuationPoint(context, request);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Reads processed history data.
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
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];
                NodeId aggregateId = details.AggregateType[handle.Index];

                // check if the node id has been parsed.
                HdaParsedNodeId parsedNodeId = handle.ParsedNodeId as HdaParsedNodeId;

                if (parsedNodeId == null)
                {
                    errors[handle.Index] = StatusCodes.BadNodeIdInvalid;
                    continue;
                }

                // validate the aggregate.
                uint hdaAggregateId = HdaModelUtils.HdaAggregateToUaAggregate(aggregateId, NamespaceIndex);

                if (hdaAggregateId == 0)
                {
                    errors[handle.Index] = StatusCodes.BadAggregateNotSupported;
                    continue;
                }
                
                // read the history of an item.
                if (parsedNodeId.RootType == HdaModelUtils.HdaItem)
                {
                    errors[handle.Index] = HistoryReadProcessedItem(
                        context,
                        client,
                        details,
                        timestampsToReturn,
                        hdaAggregateId,
                        nodeToRead,
                        parsedNodeId,
                        result);

                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Reads the history of an HDA item.
        /// </summary>
        private ServiceResult HistoryReadProcessedItem(
            ServerSystemContext context,
            ComHdaClient client,
            ReadProcessedDetails details,
            TimestampsToReturn timestampsToReturn,
            uint aggregateId,
            HistoryReadValueId nodeToRead,
            HdaParsedNodeId parsedNodeId,
            HistoryReadResult result)
        {
            // create the request or load it from a continuation point.
            HdaHistoryReadProcessedRequest request = null;

            if (nodeToRead.ContinuationPoint == null)
            {
                request = new HdaHistoryReadProcessedRequest(parsedNodeId.RootId, aggregateId, details, nodeToRead);
            }
            else
            {
                request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint) as HdaHistoryReadProcessedRequest;

                if (request == null)
                {
                    return StatusCodes.BadContinuationPointInvalid;
                }
            }

            // fetch the data.
            result.StatusCode = client.ReadHistory(request);

            // fill in the results.
            if (request.Results != null)
            {
                HistoryData data = new HistoryData();
                data.DataValues = request.Results;
                result.HistoryData = new ExtensionObject(data);
            }

            // create a new continuation point.
            if (!request.Completed)
            {
                result.ContinuationPoint = SaveContinuationPoint(context, request);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Com.Common.Opc.Ua.Com.PredefinedNodes.uanodes", Assembly.GetAssembly(this.GetType()), true);
            return predefinedNodes;
        }

        /// <summary>
        /// Called when a batch of monitored items has been created.
        /// </summary>
        protected override void OnCreateMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient(context, false);

            // use locale for session to find a subscription manager.
            HdaSubscribeRequestManager manager = null;

            if (!m_subscriptionManagers.TryGetValue(client.LocaleId, out manager))
            {
                m_subscriptionManagers[client.LocaleId] = manager = new HdaSubscribeRequestManager(context, client.LocaleId, m_configuration);
            }

            manager.CreateItems(context, monitoredItems);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                m_monitoredItems[monitoredItems[ii].Id] = manager;
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has been modified.
        /// </summary>
        protected override void OnModifyMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient(context, false);

            // sort monitored items by the locale id used to create them.
            Dictionary<int, List<IMonitoredItem>> monitoredItemsByLocaleId = new Dictionary<int, List<IMonitoredItem>>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // look up the manager that was previously used to create the monitor item.
                HdaSubscribeRequestManager manager = null;

                if (!m_monitoredItems.TryGetValue(monitoredItems[ii].Id, out manager))
                {
                    manager = m_subscriptionManagers[client.LocaleId];
                }

                // add monitored item to a list of items for the locale of the manager.
                List<IMonitoredItem> subset = null;

                if (!monitoredItemsByLocaleId.TryGetValue(manager.LocaleId, out subset))
                {
                    monitoredItemsByLocaleId[manager.LocaleId] = subset = new List<IMonitoredItem>();
                }

                subset.Add(monitoredItems[ii]);
            }

            // modify the the item.
            foreach (KeyValuePair<int, List<IMonitoredItem>> entry in monitoredItemsByLocaleId)
            {
                HdaSubscribeRequestManager manager = null;

                if (m_subscriptionManagers.TryGetValue(entry.Key, out manager))
                {
                    manager.ModifyItems(context, entry.Value);
                }
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has been deleted.
        /// </summary>
        protected override void OnDeleteMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient(context, false);

            // sort monitored items by the locale id used to create them.
            Dictionary<int, List<IMonitoredItem>> monitoredItemsByLocaleId = new Dictionary<int, List<IMonitoredItem>>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // look up the manager that was previously used to create the monitor item.
                HdaSubscribeRequestManager manager = null;

                if (!m_monitoredItems.TryGetValue(monitoredItems[ii].Id, out manager))
                {
                    manager = m_subscriptionManagers[client.LocaleId];
                }

                // add monitored item to a list of items for the locale of the manager.
                List<IMonitoredItem> subset = null;

                if (!monitoredItemsByLocaleId.TryGetValue(manager.LocaleId, out subset))
                {
                    monitoredItemsByLocaleId[manager.LocaleId] = subset = new List<IMonitoredItem>();
                }

                subset.Add(monitoredItems[ii]);
            }

            // delete the items.
            foreach (KeyValuePair<int, List<IMonitoredItem>> entry in monitoredItemsByLocaleId)
            {
                HdaSubscribeRequestManager manager = null;

                if (m_subscriptionManagers.TryGetValue(entry.Key, out manager))
                {
                    manager.DeleteItems(context, entry.Value);
                }
            }
        }

        /// <summary>
        /// Called when a batch of monitored items has their monitoring mode changed.
        /// </summary>
        protected override void OnSetMonitoringModeComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            OnModifyMonitoredItemsComplete(context, monitoredItems);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Loads a history continuation point.
        /// </summary>
        private HdaHistoryReadRequest LoadContinuationPoint(
            ServerSystemContext context,
            byte[] continuationPoint)
        {
            Session session = context.OperationContext.Session;

            if (session == null)
            {
                return null;
            }

            HdaHistoryReadRequest request = session.RestoreHistoryContinuationPoint(continuationPoint) as HdaHistoryReadRequest;

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
            HdaHistoryReadRequest request)
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

        /// <summary>
        /// Updates the type cache.
        /// </summary>
        private void DoMetadataUpdate(object state)
        {
            try
            {
                if (!Server.IsRunning)
                {
                    return;
                }

                ComHdaClientManager system = (ComHdaClientManager)SystemContext.SystemHandle;
                ComHdaClient client = (ComHdaClient)system.SelectClient(SystemContext, true);

                client.UpdateServerMetadata();
                BaseObjectState[] aggregates = client.GetSupportedAggregates(NamespaceIndex);

                lock (Lock)
                {
                    m_capabilities.MaxReturnDataValues.Value = (uint)client.GetServerCapabilities(Opc.Ua.Variables.HistoryServerCapabilitiesType_MaxReturnDataValues, 0);
                    m_capabilities.InsertDataCapability.Value = (bool)client.GetServerCapabilities(Opc.Ua.Variables.HistoryServerCapabilitiesType_InsertDataCapability, false);
                    m_capabilities.ReplaceDataCapability.Value = (bool)client.GetServerCapabilities(Opc.Ua.Variables.HistoryServerCapabilitiesType_ReplaceDataCapability, false);
                    m_capabilities.UpdateDataCapability.Value = (bool)client.GetServerCapabilities(Opc.Ua.Variables.HistoryServerCapabilitiesType_UpdateDataCapability, false);
                    m_capabilities.InsertAnnotationCapability.Value = (bool)client.GetServerCapabilities(Opc.Ua.Variables.HistoryServerCapabilitiesType_ReplaceDataCapability, false);
                    m_capabilities.DeleteRawCapability.Value = (bool)client.GetServerCapabilities(Opc.Ua.Variables.HistoryServerCapabilitiesType_DeleteRawCapability, false);
                    m_capabilities.DeleteAtTimeCapability.Value = (bool)client.GetServerCapabilities(Opc.Ua.Variables.HistoryServerCapabilitiesType_DeleteAtTimeCapability, false);

                    if (m_aggregates == null)
                    {
                        m_aggregates = aggregates;
                        m_capabilities.AggregateFunctions.RemoveReferences(Opc.Ua.ReferenceTypeIds.Organizes, false);

                        if (m_aggregates != null)
                        {
                            for (int ii = 0; ii < m_aggregates.Length; ii++)
                            {
                                AddPredefinedNode(SystemContext, m_aggregates[ii]);
                                m_capabilities.AggregateFunctions.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, m_aggregates[ii].NodeId);
                                m_aggregates[ii].AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, m_capabilities.AggregateFunctions.NodeId);
                            }
                        }
                    }
                }

                lock (Server.DiagnosticsLock)
                {
                    HistoryServerCapabilitiesState capabilities = Server.DiagnosticsNodeManager.GetDefaultHistoryCapabilities();

                    capabilities.AccessHistoryDataCapability.Value = true;

                    if (capabilities.MaxReturnDataValues.Value < m_capabilities.MaxReturnDataValues.Value)
                    {
                        capabilities.MaxReturnDataValues.Value = m_capabilities.MaxReturnDataValues.Value;
                    }

                    if (m_capabilities.InsertDataCapability.Value)
                    {
                        capabilities.InsertDataCapability.Value = true;
                    }

                    if (m_capabilities.ReplaceDataCapability.Value)
                    {
                        capabilities.ReplaceDataCapability.Value = true;
                    }

                    if (m_capabilities.UpdateDataCapability.Value)
                    {
                        capabilities.UpdateDataCapability.Value = true;
                    }

                    if (m_capabilities.InsertAnnotationCapability.Value)
                    {
                        capabilities.InsertAnnotationCapability.Value = true;
                    }

                    if (m_capabilities.DeleteRawCapability.Value)
                    {
                        capabilities.DeleteRawCapability.Value = true;
                    }

                    if (m_capabilities.DeleteAtTimeCapability.Value)
                    {
                        capabilities.DeleteAtTimeCapability.Value = true;
                    }

                    if (m_aggregates != null)
                    {
                        for (int ii = 0; ii < m_aggregates.Length; ii++)
                        {
                            if (!capabilities.AggregateFunctions.ReferenceExists(Opc.Ua.ReferenceTypeIds.Organizes, false, aggregates[ii].NodeId))
                            {
                                capabilities.AggregateFunctions.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, aggregates[ii].NodeId);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating HDA server metadata.");
            }
        }
        #endregion

        #region Private Fields
        private ComHdaClientManager m_system;
        private ComHdaClientConfiguration m_configuration;
        private HistoryServerCapabilitiesState m_capabilities;
        private BaseObjectState[] m_aggregates;
        private Dictionary<int, HdaSubscribeRequestManager> m_subscriptionManagers;
        private Dictionary<uint, HdaSubscribeRequestManager> m_monitoredItems;
        #endregion
    }
}
