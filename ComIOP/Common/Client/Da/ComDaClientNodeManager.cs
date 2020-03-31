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
using System.Globalization;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class ComDaClientNodeManager : ComClientNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ComDaClientNodeManager(IServerInternal server, string namespaceUri, ComDaClientConfiguration configuration, bool ownsTypeModel)
        :
            base(server, namespaceUri, ownsTypeModel)
        {
            SystemContext.SystemHandle = m_system = new ComDaClientManager();
            SystemContext.NodeIdFactory = this;

            // save the configuration for the node manager.
            m_configuration = configuration;

            // set the alias root.
            AliasRoot = m_configuration.ServerName;

            if (String.IsNullOrEmpty(AliasRoot))
            {
                AliasRoot = "DA";
            }

            // set the default parser if none provided.
            if (configuration.ItemIdParser == null)
            {
                configuration.ItemIdParser = new ComItemIdParser();
            }

            // create the list of subscriptions.
            m_subscriptionManagers = new Dictionary<string, SubscribeRequestManager>();
            m_subscriptionManagers[String.Empty] = new SubscribeRequestManager(SystemContext, null, 1000);
            m_monitoredItems = new Dictionary<uint, SubscribeRequestManager>();
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
            // do not assign node id to server state nodes.
            if (node is ComServerStatusState)
            {
                return node.NodeId;
            }

            return DaModelUtils.ConstructIdForComponent(node, NamespaceIndex);
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
                    serverName = "ComDaServer";
                }

                DaElement element = new DaElement();
                element.ItemId = String.Empty;
                element.Name = serverName;
                element.ElementType = DaElementType.Branch;

                DaBranchState root = new DaBranchState(SystemContext, element, NamespaceIndex);
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
                    DaModelUtils.ConstructIdForInternalNode("ServerStatus", NamespaceIndex),
                    new QualifiedName("ServerStatus", (ushort)typeNamepaceIndex),
                    null,
                    true);

                root.AddChild(status);

                // store root folder in the pre-defined nodes.
                AddPredefinedNode(SystemContext, root);

                // create the COM server.
                m_system.Initialize(SystemContext, m_configuration, status, Lock, OnServerReconnected);
                StartMetadataUpdates(null, null, 5000, m_configuration.MaxReconnectWait);
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
        /// Called when client manager has reconnected to the COM server.
        /// </summary>
        public void OnServerReconnected(object state)
        {
            try
            {
                foreach (SubscribeRequestManager manager in m_subscriptionManagers.Values)
                {
                    try
                    {
                        manager.RecreateItems();
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Could not re-create subscription after reconnect for locale {0}.", new CultureInfo(manager.LocaleId).DisplayName);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not re-create subscription after reconnect.");
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

                // check for predefined nodes.
                if (PredefinedNodes != null)
                {
                    NodeState node = null;

                    if (PredefinedNodes.TryGetValue(nodeId, out node))
                    {
                        NodeHandle handle = new NodeHandle();

                        handle.NodeId = nodeId;
                        handle.Validated = true;
                        handle.Node = node;

                        return handle;
                    }
                }

                // parse the identifier.
                DaParsedNodeId parsedNodeId = DaParsedNodeId.Parse(nodeId);

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
                DaParsedNodeId parsedNodeId = handle.ParsedNodeId as DaParsedNodeId;

                if (parsedNodeId == null)
                {
                    return null;
                }

                NodeState root = null;
                DaElement element = null;
                ComDaClient client = m_system.SelectClient(context, false);

                // validate a branch or item.
                if (parsedNodeId.RootType == DaModelUtils.DaElement)
                {
                    element = client.FindElement(parsedNodeId.RootId);

                    // branch does not exist.
                    if (element == null)
                    {
                        return null;
                    }

                    // create a temporary object to use for the operation.
                    root = DaModelUtils.ConstructElement(context, element, NamespaceIndex);
                    root.Handle = element;

                    AddAdditionalElementReferences(SystemContext, root);
                }

                // validate an property.
                else if (parsedNodeId.RootType == DaModelUtils.DaProperty)
                {
                    element = client.FindElement(parsedNodeId.RootId);

                    // branch does not exist.
                    if (element == null)
                    {
                        return null;
                    }

                    // validate the property.
                    DaProperty property = client.FindProperty(parsedNodeId.RootId, parsedNodeId.PropertyId);

                    // property does not exist.
                    if (property == null)
                    {
                        return null;
                    }

                    // create a temporary object to use for the operation.
                    root = DaModelUtils.ConstructProperty(context, element.ItemId, property, NamespaceIndex);
                    root.Handle = property;

                    AddAdditionalElementReferences(SystemContext, root);
                }

                // unknown root type.
                else
                {
                    return null;
                }

                // all done if no components to validate.
                if (String.IsNullOrEmpty(parsedNodeId.ComponentPath))
                {
                    handle.Validated = true;
                    handle.Node = target = root;
                    return handle.Node;
                }

                // validate component.
                NodeState component = root.FindChildBySymbolicName(context, parsedNodeId.ComponentPath);

                // component does not exist.
                if (component == null)
                {
                    return null;
                }

                // found a valid component.
                handle.Validated = true;
                handle.Node = target = component;
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

        /// <summary>
        /// Allows sub-class to add additional references to a element node after validation.
        /// </summary>
        protected virtual void AddAdditionalElementReferences(ServerSystemContext context, NodeState node)
        {
            // TBD
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
            ComDaClientManager system = (ComDaClientManager)this.SystemContext.SystemHandle;
            ComDaClient client = system.SelectClient((ServerSystemContext)SystemContext, false);

            ReadRequestCollection requests = new ReadRequestCollection();

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
                    errors[handle.Index] = requests.Add(source, nodeToRead, value, out queued);

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

            // read the values from the server.
            client.Read(requests);

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
        /// Validates the nodes and writes the value to the underlying system.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="nodesToWrite">The nodes to write.</param>
        /// <param name="errors">The errors.</param>
        /// <param name="nodesToValidate">The nodes to validate.</param>
        /// <param name="cache">The cache.</param>
        protected override void Write(
            ServerSystemContext context,
            IList<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache)
        {
            ComDaClientManager system = (ComDaClientManager)this.SystemContext.SystemHandle;
            ComDaClient client = system.SelectClient((ServerSystemContext)SystemContext, false);

            WriteRequestCollection requests = new WriteRequestCollection();

            // validates the nodes and queues an write requests.
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
                    
                    // determine if request can be sent to the server.
                    bool queued = false;
                    WriteValue nodeToWrite = nodesToWrite[handle.Index];
                    errors[handle.Index] = requests.Add(source, nodeToWrite, handle.Index, out queued);

                    if (queued)
                    {
                        continue;
                    }

                    // write the attribute value.
                    errors[handle.Index] = source.WriteAttribute(
                        context,
                        nodeToWrite.AttributeId,
                        nodeToWrite.ParsedIndexRange,
                        nodeToWrite.Value);

                    // updates to source finished - report changes to monitored items.
                    source.ClearChangeMasks(context, false);
                }
            }
            
            // write to the server.
            client.Write(requests);
            
            // get the results from the requests sent to the server.
            for (int ii = 0; ii < requests.Count; ii++)
            {
                WriteRequest request = requests[ii];
                errors[request.Index] = request.GetResult();
            }
        }
        #endregion

        #region Overridden Methods
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
            ComDaClientManager system = (ComDaClientManager)this.SystemContext.SystemHandle;
            ComDaClient client = system.SelectClient(context, false);

            // use locale for session to find a subscription manager.
            SubscribeRequestManager manager = null;

            if (!m_subscriptionManagers.TryGetValue(client.Key, out manager))
            {
                m_subscriptionManagers[client.Key] = manager = new SubscribeRequestManager(context, client, 1000);
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
            ComDaClientManager system = (ComDaClientManager)this.SystemContext.SystemHandle;
            ComDaClient client = system.SelectClient(context, false);

            // sort monitored items by the locale id used to create them.
            Dictionary<string, List<IMonitoredItem>> monitoredItemsByLocaleId = new Dictionary<string, List<IMonitoredItem>>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // look up the manager that was previously used to create the monitor item.
                SubscribeRequestManager manager = null;

                if (!m_monitoredItems.TryGetValue(monitoredItems[ii].Id, out manager))
                {
                    manager = m_subscriptionManagers[client.Key];
                }

                // add monitored item to a list of items for the locale of the manager.
                List<IMonitoredItem> subset = null;

                if (!monitoredItemsByLocaleId.TryGetValue(manager.Key, out subset))
                {
                    monitoredItemsByLocaleId[manager.Key] = subset = new List<IMonitoredItem>();
                }

                subset.Add(monitoredItems[ii]);
            }

            // modify the the item.
            foreach (KeyValuePair<string,List<IMonitoredItem>> entry in monitoredItemsByLocaleId)
            {
                SubscribeRequestManager manager = null;

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
            ComDaClientManager system = (ComDaClientManager)this.SystemContext.SystemHandle;
            ComDaClient client = system.SelectClient(context, false);

            // sort monitored items by the locale id used to create them.
            Dictionary<string, List<IMonitoredItem>> monitoredItemsByLocaleId = new Dictionary<string, List<IMonitoredItem>>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // look up the manager that was previously used to create the monitor item.
                SubscribeRequestManager manager = null;

                if (!m_monitoredItems.TryGetValue(monitoredItems[ii].Id, out manager))
                {
                    manager = m_subscriptionManagers[client.Key];
                }

                // add monitored item to a list of items for the locale of the manager.
                List<IMonitoredItem> subset = null;

                if (!monitoredItemsByLocaleId.TryGetValue(manager.Key, out subset))
                {
                    monitoredItemsByLocaleId[manager.Key] = subset = new List<IMonitoredItem>();
                }

                subset.Add(monitoredItems[ii]);
            }

            // delete the items.
            foreach (KeyValuePair<string, List<IMonitoredItem>> entry in monitoredItemsByLocaleId)
            {
                SubscribeRequestManager manager = null;

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

        /// <summary>
        /// Creates a new set of monitored items for a set of variables.
        /// </summary>
        /// <remarks>
        /// This method only handles data change subscriptions. Event subscriptions are created by the SDK.
        /// </remarks>
        protected override ServiceResult CreateMonitoredItem(ServerSystemContext context, NodeHandle handle, uint subscriptionId, double publishingInterval, DiagnosticsMasks diagnosticsMasks, TimestampsToReturn timestampsToReturn, MonitoredItemCreateRequest itemToCreate, ref long globalIdCounter, out MonitoringFilterResult filterResult, out IMonitoredItem monitoredItem)
        {
            filterResult = null;
            monitoredItem = null;

            // validate parameters.
            MonitoringParameters parameters = itemToCreate.RequestedParameters;

            // validate attribute.
            if (!Attributes.IsValid(handle.Node.NodeClass, itemToCreate.ItemToMonitor.AttributeId))
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            NodeState cachedNode = AddNodeToComponentCache(context, handle, handle.Node);

            // check if the node is already being monitored.
            MonitoredNode2 monitoredNode = null;

            if (!MonitoredNodes.TryGetValue(handle.Node.NodeId, out monitoredNode))
            {
                MonitoredNodes[handle.Node.NodeId] = monitoredNode = new MonitoredNode2(this, cachedNode);
            }

            handle.Node = monitoredNode.Node;
            handle.MonitoredNode = monitoredNode;

            // create a globally unique identifier.
            uint monitoredItemId = Utils.IncrementIdentifier(ref globalIdCounter);

            // determine the sampling interval.
            double samplingInterval = itemToCreate.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = publishingInterval;
            }

            // ensure minimum sampling interval is not exceeded.
            if (itemToCreate.ItemToMonitor.AttributeId == Attributes.Value)
            {
                BaseVariableState variable = handle.Node as BaseVariableState;

                if (variable != null && samplingInterval < variable.MinimumSamplingInterval)
                {
                    samplingInterval = variable.MinimumSamplingInterval;
                }
            }

            // put a large upper limit on sampling.
            if (samplingInterval == Double.MaxValue)
            {
                samplingInterval = 365 * 24 * 3600 * 1000.0;
            }

            // put an upper limit on queue size.
            uint queueSize = itemToCreate.RequestedParameters.QueueSize;

            if (queueSize > MaxQueueSize)
            {
                queueSize = MaxQueueSize;
            }

            // validate the monitoring filter.
            Range euRange = null;
            MonitoringFilter filterToUse = null;

            ServiceResult error = ValidateMonitoringFilter(
                context,
                handle,
                itemToCreate.ItemToMonitor.AttributeId,
                samplingInterval,
                queueSize,
                parameters.Filter,
                out filterToUse,
                out euRange,
                out filterResult);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // create the item.
            MonitoredItem datachangeItem = new ComMonitoredItem(
                Server,
                this,
                handle,
                subscriptionId,
                monitoredItemId,
                context.OperationContext.Session,
                itemToCreate.ItemToMonitor,
                diagnosticsMasks,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                filterToUse,
                filterToUse,
                euRange,
                samplingInterval,
                queueSize,
                itemToCreate.RequestedParameters.DiscardOldest,
                0);

            // report the initial value.
            ReadInitialValue(context, handle, datachangeItem);

            // update monitored item list.
            monitoredItem = datachangeItem;

            // save the monitored item.
            MonitoredItems.Add(monitoredItemId, datachangeItem);
            monitoredNode.Add(datachangeItem);

            // report change.
            OnMonitoredItemCreated(context, handle, datachangeItem);

            return error;
        }

        #endregion

        #region Conversion Functions
        /// <summary>
        /// Converts a value to something the DA server can accept.
        /// </summary>
        /// <param name="srcValue">The source value.</param>
        /// <param name="dstValue">The converted value.</param>
        /// <returns>Any error from the conversion.</returns>
        public static int LocalToRemoteValue(Variant srcValue, out object dstValue)
        {
            dstValue = null;

            TypeInfo srcType = srcValue.TypeInfo;

            if (srcType == null)
            {
                srcType = TypeInfo.Construct(srcValue.Value);
            }

            if (srcType.BuiltInType <= BuiltInType.DateTime
                || srcType.BuiltInType == BuiltInType.ByteString) // OPC UA specification ver 1.02 compliant.
            {
                dstValue = srcValue.Value;
                return ResultIds.S_OK;
            }

            try
            {
                if (srcType.BuiltInType == BuiltInType.Variant && srcType.ValueRank >= 0)
                {
                    dstValue = TypeInfo.CastArray((Array)srcValue.Value, BuiltInType.Variant, BuiltInType.Null, LocalToRemoteValue);
                    return ResultIds.S_OK;
                }
            }
            catch (Exception)
            {
                return ResultIds.E_BADTYPE;
            }

            return ResultIds.E_BADTYPE;
        }

        /// <summary>
        /// Converts a DA value to a UA compatible type.
        /// </summary>
        /// <param name="srcValue">The source value.</param>
        /// <param name="dstValue">The converted value.</param>
        /// <returns>Any error from the conversion.</returns>
        public static int RemoteToLocalValue(object srcValue, out Variant dstValue)
        {
            object value = RemoteToLocalValue(srcValue, BuiltInType.Null, BuiltInType.Null);
            dstValue = new Variant(value);
            return ResultIds.S_OK;
        }

        /// <summary>
        /// Converts a UA value to something the DA server will accept.
        /// </summary>
        private static object LocalToRemoteValue(object srcValue, BuiltInType srcType, BuiltInType dstType)
        {
            if (srcType <= BuiltInType.DateTime)
            {
                return srcValue;
            }

            if (srcType == BuiltInType.Variant)
            {
                TypeInfo typeInfo = TypeInfo.Construct(srcValue);
                srcType = typeInfo.BuiltInType;

                if (typeInfo.ValueRank != ValueRanks.Scalar)
                {
                    return TypeInfo.CastArray((Array)srcValue, srcType, BuiltInType.Null, LocalToRemoteValue);
                }

                return LocalToRemoteValue(srcValue, srcType, dstType);
            }

            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        /// <summary>
        /// Converts a value to something the UA client can accept.
        /// </summary>
        private static object RemoteToLocalValue(object srcValue, BuiltInType srcType, BuiltInType dstType)
        {
            if (typeof(decimal).IsInstanceOfType(srcValue))
            {
                return ((decimal)srcValue).ToString();
            }

            if (typeof(decimal[]).IsInstanceOfType(srcValue))
            {
                return TypeInfo.CastArray((Array)srcValue, BuiltInType.Null, BuiltInType.String, RemoteToLocalValue);
            }

            return srcValue;
        }
        #endregion

        #region Private Methods
        #endregion

        #region Private Fields
        private ComDaClientManager m_system;
        private ComDaClientConfiguration m_configuration;
        private Dictionary<string,SubscribeRequestManager> m_subscriptionManagers;
        private Dictionary<uint,SubscribeRequestManager> m_monitoredItems;
        #endregion
    }
}
