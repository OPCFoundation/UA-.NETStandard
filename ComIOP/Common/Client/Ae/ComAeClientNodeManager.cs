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
    public class ComAeClientNodeManager : ComClientNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ComAeClientNodeManager(IServerInternal server, string namespaceUri, ComAeClientConfiguration configuration, bool ownsTypeModel)
        :
            base(server, namespaceUri, ownsTypeModel)
        {
            SystemContext.SystemHandle = m_system = new ComAeClientManager();
            SystemContext.NodeIdFactory = this;

            // save the configuration for the node manager.
            m_configuration = configuration;
            
            // set the alias root.
            AliasRoot = m_configuration.ServerName;

            if (String.IsNullOrEmpty(AliasRoot))
            {
                AliasRoot = "AE";
            }

            m_subscriptions = new Dictionary<SubscriptionIndex,ComAeSubscriptionClient>();
            m_monitoredItems = new Dictionary<uint,ComAeSubscriptionClient>();
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
            if (node is ServerStatusState)
            {
                return node.NodeId;
            }

            return ParsedNodeId.ConstructIdForComponent(node, NamespaceIndex);
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

                IList<IReference> references = null;

                // create the root node.
                string serverName = m_configuration.ServerName;

                if (String.IsNullOrEmpty(serverName))
                {
                    serverName = "ComAeServer";
                }

                AeAreaState root = new AeAreaState(SystemContext, String.Empty, serverName, NamespaceIndex);
                root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

                // link root to objects folder.
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));

                // link root to server object.
                if (!externalReferences.TryGetValue(ObjectIds.Server, out references))
                {
                    externalReferences[ObjectIds.Server] = references = new List<IReference>();
                }

                references.Add(new NodeStateReference(ReferenceTypeIds.HasNotifier, false, root.NodeId));

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
                    AeModelUtils.ConstructIdForInternalNode("ServerStatus", NamespaceIndex),
                    new QualifiedName("ServerStatus", (ushort)typeNamepaceIndex),
                    null,
                    true);

                root.AddChild(status);

                // store root folder in the pre-defined nodes.
                AddPredefinedNode(SystemContext, root);
                AddRootNotifier(root);

                // create the COM server.
                m_system.Initialize(SystemContext, m_configuration, status, Lock, OnServerReconnected);

                // create a template condition that can be used to initialize static metadata.
                m_templateAlarm = new AlarmConditionState(null);
                m_templateAlarm.SymbolicName = "TemplateAlarm";

                m_templateAlarm.Create(
                    SystemContext,
                    null,
                    new QualifiedName(m_templateAlarm.SymbolicName, NamespaceIndex),
                    null,
                    false);

                m_templateAlarm.Acknowledge.OnCall = OnAcknowledge;
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
                    // check if node already being monitored.
                    if (MonitoredNodes != null)
                    {
                        MonitoredNode2 monitoredNode2 = null;

                        if (MonitoredNodes.TryGetValue(nodeId, out monitoredNode2))
                        {
                            handle = new NodeHandle(nodeId, monitoredNode2.Node);
                            handle.MonitoredNode = monitoredNode2;
                            return handle;
                        }
                    }

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
                    AeParsedNodeId parsedNodeId = AeParsedNodeId.Parse(nodeId);

                    if (parsedNodeId != null)
                    {
                        if (parsedNodeId.RootType == AeModelUtils.AeEventTypeMapping && m_typeCache != null)
                        {
                            AeEventTypeMappingState mappingNode = m_typeCache.GetMappingNode(SystemContext, nodeId);
                            
                            if (mappingNode != null)
                            {
                                return handle = new NodeHandle(nodeId, mappingNode);
                            }

                            return null;
                        }
                                
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
                AeParsedNodeId parsedNodeId = handle.ParsedNodeId as AeParsedNodeId;

                if (parsedNodeId == null)
                {
                    return null;
                }

                ComAeClient client = m_system.SelectClient(context, false);

                switch (parsedNodeId.RootType)
                {
                    case AeModelUtils.AeSimpleEventType:
                    case AeModelUtils.AeTrackingEventType:
                    case AeModelUtils.AeConditionEventType:
                    {
                        if (m_typeCache == null)
                        {
                            return null;
                        }

                        BaseObjectTypeState eventTypeNode = null;
                        NodeId rootId = AeParsedNodeId.Construct(parsedNodeId.RootType, parsedNodeId.CategoryId, parsedNodeId.ConditionName, parsedNodeId.NamespaceIndex);
                        
                        if (!m_typeCache.EventTypeNodes.TryGetValue(rootId, out eventTypeNode))
                        {
                            return null;
                        }

                        target = eventTypeNode;
                        break;
                    }

                    case AeModelUtils.AeArea:
                    {
                        ComAeBrowserClient browser = new ComAeBrowserClient(client, null);
                        target = browser.FindArea(context, parsedNodeId.RootId, NamespaceIndex);
                        browser.Dispose();

                        handle.Validated = true;
                        handle.Node = target;
                        return handle.Node;
                    }

                    case AeModelUtils.AeSource:
                    {
                        ComAeBrowserClient browser = new ComAeBrowserClient(client, null);
                        target = browser.FindSource(context, parsedNodeId.RootId, parsedNodeId.ComponentPath, NamespaceIndex);
                        browser.Dispose();

                        handle.Validated = true;
                        handle.Node = target;
                        return handle.Node;
                    }

                    case AeModelUtils.AeCondition:
                    {
                        target = new AeConditionState(context, handle, m_templateAlarm.Acknowledge);
                        break;
                    }
                }

                // node does not exist.
                if (target == null)
                {
                    return null;
                }

                if (!String.IsNullOrEmpty(parsedNodeId.ComponentPath))
                {
                    // validate component.
                    NodeState component = target.FindChildBySymbolicName(context, parsedNodeId.ComponentPath);

                    // component does not exist.
                    if (component == null)
                    {
                        return null;
                    }

                    target = component;
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

        /// <summary>
        /// Called when client manager has reconnected to the COM server.
        /// </summary>
        public void OnServerReconnected(object state)
        {
            try
            {
                // refetch the type information.
                DoMetadataUpdate(null);
                
                lock (Lock)
                {
                    foreach (ComAeSubscriptionClient subscription in m_subscriptions.Values)
                    {
                        subscription.Create();
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not re-create subscription after reconnect.");
            }
        }

        /// <summary>
        /// Called when the alarm is acknowledged.
        /// </summary>
        private ServiceResult OnAcknowledge(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] eventId,
            LocalizedText comment)
        {
            ComAeClientManager system = (ComAeClientManager)this.SystemContext.SystemHandle;
            ComAeClient client = (ComAeClient)system.SelectClient((ServerSystemContext)context, false);

            try
            {
                return client.Acknowledge((ServerSystemContext)context, eventId, comment); 
            }
            catch (Exception e)
            {
                return ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Could not acknowledge event.");
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
        /// Waits for the type cache to be initialized.
        /// </summary>
        private bool WaitForTypeCache()
        {
            // need to wait until the cache is refreshed for the first time.
            for (int ii = 0; Object.ReferenceEquals(m_typeCache, null) && ii < 200 && Server.IsRunning; ii++)
            {
                Thread.Sleep(100);
            }

            return !Object.ReferenceEquals(m_typeCache, null);
        }

        /// <summary>
        /// Subscribes or unsubscribes to events produced by all event sources.
        /// </summary>
        /// <remarks>
        /// This method is called when a event subscription is created or deleted. The node 
        /// manager must start/stop reporting events for all objects that it manages.
        /// </remarks>
        public override ServiceResult SubscribeToAllEvents(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            ComAeClientManager system = (ComAeClientManager)this.SystemContext.SystemHandle;
            ComAeClient client = (ComAeClient)system.SelectClient(systemContext, false);
            
            // need to wait until the cache is refreshed for the first time.
            if (!WaitForTypeCache())
            {
                return StatusCodes.BadOutOfService;
            }

            lock (Lock)
            {
                SubscriptionIndex index = new SubscriptionIndex();
                index.NodeId = Opc.Ua.ObjectIds.Server;
                index.LocaleId = client.LocaleId;

                if (unsubscribe)
                {
                    ComAeSubscriptionClient subscription = null;

                    if (!m_monitoredItems.TryGetValue(monitoredItem.Id, out subscription))
                    {
                        return ServiceResult.Good;
                    }

                    m_monitoredItems.Remove(monitoredItem.Id);
                    // Utils.Trace("REMOVED ITEM {0}", monitoredItem.Id);

                    if (subscription.RemoveItem(monitoredItem as MonitoredItem) == 0)
                    {
                        subscription.Delete();
                        m_subscriptions.Remove(index);
                        // Utils.Trace("DELETED SUBSCRIPTION {0}", index.NodeId);
                    }
                }
                else
                {
                    ComAeSubscriptionClient subscription = null;

                    if (!m_subscriptions.TryGetValue(index, out subscription))
                    {
                        subscription = new ComAeSubscriptionClient(systemContext, m_configuration, m_typeCache, NamespaceIndex, system, monitoredItem as MonitoredItem);
                        m_subscriptions.Add(index, subscription);
                        subscription.Create();
                        // Utils.Trace("ADDED NEW SUBSCRIPTION {0}", index.NodeId);
                    }
                    else
                    {
                        subscription.AddItem(monitoredItem as MonitoredItem);
                    }

                    m_monitoredItems[monitoredItem.Id] = subscription;
                    // Utils.Trace("ADDED NEW ITEM {0}", monitoredItem.Id);
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Subscribes to events.
        /// </summary>
        protected override ServiceResult SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            ComAeClientManager system = (ComAeClientManager)this.SystemContext.SystemHandle;
            ComAeClient client = (ComAeClient)system.SelectClient(context, false);

            // need to wait until the cache is refreshed for the first time.
            if (!WaitForTypeCache())
            {
                return StatusCodes.BadOutOfService;
            }

            lock (Lock)
            {
                SubscriptionIndex index = new SubscriptionIndex();
                index.NodeId = source.NodeId;
                index.LocaleId = client.LocaleId;

                if (unsubscribe)
                {
                    ComAeSubscriptionClient subscription = null;

                    if (!m_monitoredItems.TryGetValue(monitoredItem.Id, out subscription))
                    {
                        return ServiceResult.Good;
                    }

                    m_monitoredItems.Remove(monitoredItem.Id);
                    // Utils.Trace("REMOVED ITEM {0}", monitoredItem.Id);

                    if (subscription.RemoveItem(monitoredItem as MonitoredItem) == 0)
                    {
                        subscription.Delete();
                        m_subscriptions.Remove(index);
                        // Utils.Trace("DELETED SUBSCRIPTION {0}", index.NodeId);
                    }
                }
                else
                {
                    ComAeSubscriptionClient subscription = null;

                    if (!m_subscriptions.TryGetValue(index, out subscription))
                    {
                        subscription = new ComAeSubscriptionClient(context, m_configuration, m_typeCache, NamespaceIndex, system, monitoredItem as MonitoredItem);
                        m_subscriptions.Add(index, subscription);
                        subscription.Create();
                        // Utils.Trace("ADDED NEW SUBSCRIPTION {0}", index.NodeId);
                    }
                    else
                    {
                        subscription.AddItem(monitoredItem as MonitoredItem);
                    }

                    m_monitoredItems[monitoredItem.Id] = subscription;
                    // Utils.Trace("ADDED NEW ITEM {0}", monitoredItem.Id);
                }
            }

            // all done.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Tells the node manager to refresh any conditions associated with the specified monitored items.
        /// </summary>
        public override ServiceResult ConditionRefresh(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems)
        {
            List<MonitoredItem> itemsToRefresh = new List<MonitoredItem>();
            List<ComAeSubscriptionClient> subscriptionsToRefresh = new List<ComAeSubscriptionClient>();

            lock (Lock)
            {
                // build list of subscriptions that have to be refreshed.
                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                    if (monitoredItem == null)
                    {
                        continue;
                    }

                    ComAeSubscriptionClient subscription = null;

                    if (!m_monitoredItems.TryGetValue(monitoredItem.Id, out subscription))
                    {
                        continue;
                    }

                    itemsToRefresh.Add(monitoredItem);
                    subscriptionsToRefresh.Add(subscription);
                }
            }

            for (int ii = 0; ii < subscriptionsToRefresh.Count; ii++)
            {
                // collect the events.
                List<IFilterTarget> events = new List<IFilterTarget>();
                subscriptionsToRefresh[ii].Refresh(events);

                // queue the events.
                for (int jj = 0; jj < events.Count; jj++)
                {
                    itemsToRefresh[ii].QueueEvent(events[jj]);
                }
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Private Methods
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

                ComAeClientManager system = (ComAeClientManager)SystemContext.SystemHandle;
                ComAeClient client = (ComAeClient)system.SelectClient(SystemContext, true);

                AeTypeCache cache = new AeTypeCache();
                cache.LoadEventTypes(client);

                lock (Lock)
                {
                    if (m_typeCache == null)
                    {
                        m_typeCache = cache;
                    }

                    m_typeCache.EventTypes = cache.EventTypes;
                    m_typeCache.UpdateCache(SystemContext, NamespaceIndex);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating event type cache.");
            }
        }
        #endregion

        #region SubscriptionIndex Class
        /// <summary>
        /// Used to maintain an index of current subscriptions.
        /// </summary>
        private class SubscriptionIndex
        {
            /// <summary>
            /// The locale id for the subscription.
            /// </summary>
            public int LocaleId { get; set; }

            /// <summary>
            /// The node id for the subscription.
            /// </summary>
            public NodeId NodeId { get; set; }

            /// <summary>
            /// Returns true if the object is equal to the instance.
            /// </summary>
            public override bool Equals(object obj)
            {
                if (Object.ReferenceEquals(this, obj))
                {
                    return true;
                }

                SubscriptionIndex index = obj as SubscriptionIndex;

                if (index == null)
                {
                    return false;
                }

                if (index.LocaleId != this.LocaleId)
                {
                    return false;
                }

                if (index.NodeId != this.NodeId)
                {
                    return false;
                }
                
                return true;
            }

            /// <summary>
            /// Returns a hash code for the instantce.
            /// </summary>
            public override int GetHashCode()
            {
                int hash = LocaleId.GetHashCode();

                if (NodeId != null)
                {
                    hash ^= NodeId.GetHashCode();
                }

                return hash;
            }
        }
        #endregion

        #region Private Fields
        private ComAeClientManager m_system;
        private ComAeClientConfiguration m_configuration;
        private AlarmConditionState m_templateAlarm;
        private Dictionary<SubscriptionIndex,ComAeSubscriptionClient> m_subscriptions;
        private Dictionary<uint,ComAeSubscriptionClient> m_monitoredItems;
        private AeTypeCache m_typeCache;
        #endregion
    }
}
