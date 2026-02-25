/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A sample implementation of the INodeManager interface.
    /// </summary>
    /// <remarks>
    /// This node manager is a base class used in multiple samples. It implements the INodeManager
    /// interface and allows sub-classes to override only the methods that they need. This example
    /// is not part of the SDK because most real implementations of a INodeManager will need to
    /// modify the behavior of the base class.
    /// </remarks>
    public partial class CustomNodeManager2 : INodeManager3, INodeIdFactory, IDisposable
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            params string[] namespaceUris)
            : this(server, null, false, namespaceUris)
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            ILogger logger,
            params string[] namespaceUris)
            : this(server, null, logger, namespaceUris)
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            params string[] namespaceUris)
            : this(server, configuration, false, namespaceUris)
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            params string[] namespaceUris)
            : this(server, configuration, false, logger, namespaceUris)
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            params string[] namespaceUris)
            : this(
                  server,
                  configuration,
                  useSamplingGroups,
                  server.Telemetry.CreateLogger<CustomNodeManager2>(),
                  namespaceUris)
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            ILogger logger,
            params string[] namespaceUris)
        {
            // set defaults.
            MaxQueueSize = 1000;
            MaxDurableQueueSize = 200000; //default value in deprecated Conformance Unit Subscription Durable StorageLevel High

            if (configuration?.ServerConfiguration != null)
            {
                MaxQueueSize = (uint)configuration.ServerConfiguration.MaxNotificationQueueSize;
                MaxDurableQueueSize = (uint)configuration.ServerConfiguration
                    .MaxDurableNotificationQueueSize;
            }

            // save a reference to the UA server instance that owns the node manager.
            Server = server;
            m_logger = logger;

            // all operations require information about the system
            SystemContext = Server.DefaultSystemContext.Copy();

            // the node id factory assigns new node ids to new nodes.
            // the strategy used by a NodeManager depends on what kind of information it provides.
            SystemContext.NodeIdFactory = this;

            // add the uris to the server's namespace table and cache the indexes.
            ushort[] namespaceIndexes = [];
            if (namespaceUris != null)
            {
                namespaceIndexes = new ushort[namespaceUris.Length];

                for (int ii = 0; ii < namespaceUris.Length; ii++)
                {
                    namespaceIndexes[ii] = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[ii]);
                }
            }

            // add the table of namespaces that are used by the NodeManager.
            m_namespaceUris = namespaceUris;
            m_namespaceIndexes = namespaceIndexes;

            // create a monitored item manager that owns sampling groups / monitoredNodes
            if (useSamplingGroups)
            {
                m_monitoredItemManager = new SamplingGroupMonitoredItemManager(
                    this,
                    server,
                    configuration);
            }
            else
            {
                m_monitoredItemManager = new MonitoredNodeMonitoredItemManager(this, server);
            }

            PredefinedNodes = [];
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (Lock)
                {
                    Utils.SilentDispose(m_monitoredItemManager);
                    foreach (NodeState node in PredefinedNodes.Values)
                    {
                        Utils.SilentDispose(node);
                    }

                    PredefinedNodes.Clear();
                }
            }
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        public virtual NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId;
        }

        /// <summary>
        /// Acquires the lock on the node manager.
        /// </summary>
        public object Lock { get; } = new object();

        /// <summary>
        /// Gets the server that the node manager belongs to.
        /// </summary>
        public IServerInternal Server { get; }

        /// <summary>
        /// The default context to use.
        /// </summary>
        public ServerSystemContext SystemContext { get; }

        /// <summary>
        /// Gets the default index for the node manager's namespace.
        /// </summary>
        public ushort NamespaceIndex => m_namespaceIndexes[0];

        /// <summary>
        /// Gets the namespace indexes owned by the node manager.
        /// </summary>
        /// <value>The namespace indexes.</value>
        public IReadOnlyList<ushort> NamespaceIndexes => m_namespaceIndexes;

        /// <summary>
        /// Gets or sets the maximum size of a monitored item queue.
        /// </summary>
        /// <value>The maximum size of a monitored item queue.</value>
        public uint MaxQueueSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum size of a durable monitored item queue.
        /// </summary>
        /// <value>The maximum size of a durable monitored item queue.</value>
        public uint MaxDurableQueueSize { get; set; }

        /// <summary>
        /// The root for the alias assigned to the node manager.
        /// </summary>
        public string AliasRoot { get; set; }

        /// <summary>
        /// The predefined nodes managed by the node manager.
        /// </summary>
        protected NodeIdDictionary<NodeState> PredefinedNodes { get; }

        /// <summary>
        /// The root notifiers for the node manager.
        /// </summary>
        protected List<NodeState> RootNotifiers { get; private set; }

        /// <summary>
        /// Gets the table of nodes being monitored.
        /// </summary>
        protected NodeIdDictionary<MonitoredNode2> MonitoredNodes
            => m_monitoredItemManager.MonitoredNodes;

        /// <summary>
        /// Gets the table of monitored items managed by the node manager.
        /// </summary>
        protected ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems
            => m_monitoredItemManager.MonitoredItems;

        /// <summary>
        /// Sets the namespaces supported by the NodeManager.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        protected void SetNamespaces(params string[] namespaceUris)
        {
            // add the uris to the server's namespace table and cache the indexes.
            ushort[] namespaceIndexes = new ushort[namespaceUris.Length];

            for (int ii = 0; ii < namespaceUris.Length; ii++)
            {
                namespaceIndexes[ii] = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[ii]);
            }

            // create the immutable table of namespaces that are used by the NodeManager.
            m_namespaceUris = namespaceUris;
            m_namespaceIndexes = namespaceIndexes;
        }

        /// <summary>
        /// Sets the namespace indexes supported by the NodeManager.
        /// </summary>
        protected void SetNamespaceIndexes(ushort[] namespaceIndexes)
        {
            string[] namespaceUris = new string[namespaceIndexes.Length];

            for (int ii = 0; ii < namespaceIndexes.Length; ii++)
            {
                namespaceUris[ii] = Server.NamespaceUris.GetString(namespaceIndexes[ii]);
            }

            // create the immutable table of namespaces that are used by the NodeManager.
            m_namespaceUris = namespaceUris;
            m_namespaceIndexes = namespaceIndexes;
        }

        /// <summary>
        /// Returns true if the namespace for the node id is one of the namespaces managed by the node manager.
        /// </summary>
        /// <remarks>
        /// It is thread safe to call this method outside the node manager lock.
        /// </remarks>
        /// <param name="nodeId">The node id to check.</param>
        /// <returns>True if the namespace is one of the nodes.</returns>
        protected virtual bool IsNodeIdInNamespace(NodeId nodeId)
        {
            // nulls are never a valid node.
            if (nodeId.IsNull)
            {
                return false;
            }

            // quickly exclude nodes that not in the namespace.
            return m_namespaceIndexes.Contains(nodeId.NamespaceIndex);
        }

        /// <summary>
        /// Returns the node if the handle refers to a node managed by this manager.
        /// </summary>
        /// <remarks>
        /// It is thread safe to call this method outside the node manager lock.
        /// </remarks>
        /// <param name="managerHandle">The handle to check.</param>
        /// <returns>Non-null if the handle belongs to the node manager.</returns>
        protected virtual NodeHandle IsHandleInNamespace(object managerHandle)
        {
            if (managerHandle is not NodeHandle source)
            {
                return null;
            }

            if (!IsNodeIdInNamespace(source.NodeId))
            {
                return null;
            }

            return source;
        }

        /// <summary>
        /// Returns the state object for the specified node if it exists.
        /// </summary>
        public NodeState Find(NodeId nodeId)
        {
            if (PredefinedNodes.TryGetValue(nodeId, out NodeState node))
            {
                return node;
            }

            return null;
        }

        /// <summary>
        /// Creates a new instance and assigns unique identifiers to all children.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="parentId">An optional parent identifier.</param>
        /// <param name="referenceTypeId">The reference type from the parent.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="instance">The instance to create.</param>
        /// <returns>The new node id.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public NodeId CreateNode(
            ServerSystemContext context,
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            BaseInstanceState instance)
        {
            ServerSystemContext contextToUse = SystemContext.Copy(context);

            lock (Lock)
            {
                instance.ReferenceTypeId = referenceTypeId;

                NodeState parent = null;

                if (!parentId.IsNull)
                {
                    if (!PredefinedNodes.TryGetValue(parentId, out parent))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNodeIdUnknown,
                            "Cannot find parent with id: {0}",
                            parentId);
                    }

                    parent.AddChild(instance);
                }

                instance.Create(contextToUse, default, browseName, default, true);
                AddPredefinedNode(contextToUse, instance);

                return instance.NodeId;
            }
        }

        /// <summary>
        /// Deletes a node and all of its children.
        /// </summary>
        public bool DeleteNode(ServerSystemContext context, NodeId nodeId)
        {
            ServerSystemContext contextToUse = SystemContext.Copy(context);

            var referencesToRemove = new List<LocalReference>();

            if (!PredefinedNodes.TryGetValue(nodeId, out NodeState node))
            {
                return false;
            }

            RemovePredefinedNode(contextToUse, node, referencesToRemove);
            RemoveRootNotifier(node);

            if (referencesToRemove.Count > 0)
            {
                Server.NodeManager.RemoveReferences(referencesToRemove);
            }

            return true;
        }

        /// <summary>
        /// Searches the node id in all node managers
        /// </summary>
        [Obsolete("Use IServerInteral.IMasterNodeManager.FindNodeInAddressSpaceAsync instead.")]
        public NodeState FindNodeInAddressSpace(NodeId nodeId)
        {
            return Server.NodeManager.FindNodeInAddressSpaceAsync(nodeId).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns the namespaces used by the node manager.
        /// </summary>
        /// <remarks>
        /// All NodeIds exposed by the node manager must be qualified by a namespace URI. This property
        /// returns the URIs used by the node manager. In this example all NodeIds use a single URI.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        public virtual IEnumerable<string> NamespaceUris
        {
            get => m_namespaceUris;
            protected set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                var namespaceUris = new List<string>(value);
                SetNamespaces([.. namespaceUris]);
            }
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public virtual void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            LoadPredefinedNodes(SystemContext, externalReferences);
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        public virtual void LoadPredefinedNodes(
            ISystemContext context,
            Assembly assembly,
            string resourcePath,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // load the predefined nodes from an XML document.
            var predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromResource(context, resourcePath, assembly, true);

            // add the predefined nodes to the node manager.
            for (int ii = 0; ii < predefinedNodes.Count; ii++)
            {
                AddPredefinedNode(context, predefinedNodes[ii]);
            }

            // ensure the reverse references exist.
            AddReverseReferences(externalReferences);
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected virtual NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            return [];
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected virtual void LoadPredefinedNodes(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // load the predefined nodes from an XML document.
            NodeStateCollection predefinedNodes = LoadPredefinedNodes(context);

            // add the predefined nodes to the node manager.
            for (int ii = 0; ii < predefinedNodes.Count; ii++)
            {
                AddPredefinedNode(context, predefinedNodes[ii]);
            }

            // ensure the reverse references exist.
            AddReverseReferences(externalReferences);
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected virtual NodeState AddBehaviourToPredefinedNode(
            ISystemContext context,
            NodeState predefinedNode)
        {
            return predefinedNode;
        }

        /// <summary>
        /// Recursively indexes the node and its children.
        /// </summary>
        protected virtual void AddPredefinedNode(ISystemContext context, NodeState node)
        {
            // assign a default value to any variable in namespace 0
            if (node is BaseVariableState nodeStateVar &&
                nodeStateVar.NodeId.NamespaceIndex == 0 &&
                nodeStateVar.Value.IsNull)
            {
                nodeStateVar.Value = TypeInfo.GetDefaultVariantValue(
                    nodeStateVar.DataType,
                    nodeStateVar.ValueRank,
                    Server.TypeTree);
            }

            NodeState activeNode = AddBehaviourToPredefinedNode(context, node);
            PredefinedNodes.AddOrUpdate(activeNode.NodeId, activeNode, (key, _) => activeNode);

            if (activeNode is BaseTypeState type)
            {
                AddTypesToTypeTree(type);
            }
            lock (Lock)
            {
                // update the root notifiers.
                if (RootNotifiers != null)
                {
                    for (int ii = 0; ii < RootNotifiers.Count; ii++)
                    {
                        if (RootNotifiers[ii].NodeId == activeNode.NodeId)
                        {
                            RootNotifiers[ii] = activeNode;

                            // need to prevent recursion with the server object.
                            if (activeNode.NodeId != ObjectIds.Server)
                            {
                                activeNode.OnReportEvent = OnReportEvent;

                                if (!activeNode.ReferenceExists(
                                    ReferenceTypeIds.HasNotifier,
                                    true,
                                    ObjectIds.Server))
                                {
                                    activeNode.AddReference(
                                        ReferenceTypeIds.HasNotifier,
                                        true,
                                        ObjectIds.Server);
                                }
                            }

                            break;
                        }
                    }
                }
            }

            var children = new List<BaseInstanceState>();
            activeNode.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                AddPredefinedNode(context, children[ii]);
            }
        }

        /// <summary>
        /// Recursively indexes the node and its children.
        /// </summary>
        protected virtual void RemovePredefinedNode(
            ISystemContext context,
            NodeState node,
            List<LocalReference> referencesToRemove)
        {
            if (!PredefinedNodes.TryRemove(node.NodeId, out _))
            {
                return;
            }
            node.UpdateChangeMasks(NodeStateChangeMasks.Deleted);
            node.ClearChangeMasks(context, false);
            OnNodeRemoved(node);

            // remove from the parent.
            if (node is BaseInstanceState instance && instance.Parent != null)
            {
                instance.Parent.RemoveChild(instance);
            }

            // remove children.
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                node.RemoveChild(children[ii]);
            }

            for (int ii = 0; ii < children.Count; ii++)
            {
                RemovePredefinedNode(context, children[ii], referencesToRemove);
            }

            // remove from type table.

            if (node is BaseTypeState type)
            {
                Server.TypeTree.Remove(type.NodeId);
            }

            // remove inverse references.
            var references = new List<IReference>();
            node.GetReferences(context, references);

            for (int ii = 0; ii < references.Count; ii++)
            {
                IReference reference = references[ii];

                if (reference.TargetId.IsAbsolute)
                {
                    continue;
                }

                var referenceToRemove = new LocalReference(
                    (NodeId)reference.TargetId,
                    reference.ReferenceTypeId,
                    !reference.IsInverse,
                    node.NodeId);

                referencesToRemove.Add(referenceToRemove);
            }
        }

        /// <summary>
        /// Called after a node has been deleted.
        /// </summary>
        protected virtual void OnNodeRemoved(NodeState node)
        {
            // overridden by the sub-class.
        }

        /// <summary>
        /// Ensures that all reverse references exist.
        /// </summary>
        /// <param name="externalReferences">A list of references to add to external targets.</param>
        protected virtual void AddReverseReferences(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            foreach (NodeState source in PredefinedNodes.Values)
            {
                var references = new List<IReference>();
                source.GetReferences(SystemContext, references);

                for (int ii = 0; ii < references.Count; ii++)
                {
                    IReference reference = references[ii];

                    // nothing to do with external nodes.
                    if (reference.TargetId.IsNull || reference.TargetId.IsAbsolute)
                    {
                        continue;
                    }

                    // no need to add HasSubtype references since these are handled via the type table.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                    {
                        continue;
                    }

                    var targetId = (NodeId)reference.TargetId;

                    // check for data type encoding references.
                    if (reference.IsInverse &&
                        reference.ReferenceTypeId == ReferenceTypeIds.HasEncoding)
                    {
                        Server.TypeTree.AddEncoding(targetId, source.NodeId);
                    }

                    // add inverse reference to internal targets.
                    if (PredefinedNodes.TryGetValue(targetId, out NodeState target))
                    {
                        if (!target.ReferenceExists(
                            reference.ReferenceTypeId,
                            !reference.IsInverse,
                            source.NodeId))
                        {
                            target.AddReference(
                                reference.ReferenceTypeId,
                                !reference.IsInverse,
                                source.NodeId);
                        }

                        continue;
                    }

                    // check for inverse references to external notifiers.
                    if (reference.IsInverse &&
                        reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier)
                    {
                        AddRootNotifier(source);
                    }

                    // nothing more to do for references to nodes managed by this manager.
                    if (IsNodeIdInNamespace(targetId))
                    {
                        continue;
                    }

                    // add external reference.
                    AddExternalReference(
                        targetId,
                        reference.ReferenceTypeId,
                        !reference.IsInverse,
                        source.NodeId,
                        externalReferences);
                }
            }
        }

        /// <summary>
        /// Adds an external reference to the dictionary.
        /// </summary>
        protected void AddExternalReference(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // get list of references to external nodes.
            if (!externalReferences.TryGetValue(sourceId, out IList<IReference> referencesToAdd))
            {
                externalReferences[sourceId] = referencesToAdd = [];
            }

            // add reserve reference from external node.
            var referenceToAdd = new ReferenceNode
            {
                ReferenceTypeId = referenceTypeId,
                IsInverse = isInverse,
                TargetId = targetId
            };

            referencesToAdd.Add(referenceToAdd);
        }

        /// <summary>
        /// Recursively adds the types to the type tree.
        /// </summary>
        protected void AddTypesToTypeTree(BaseTypeState type)
        {
            if (!type.SuperTypeId.IsNull && !Server.TypeTree.IsKnown(type.SuperTypeId))
            {
                AddTypesToTypeTree(type.SuperTypeId);
            }

            if (type.NodeClass != NodeClass.ReferenceType)
            {
                Server.TypeTree.AddSubtype(type.NodeId, type.SuperTypeId);
            }
            else
            {
                Server.TypeTree.AddReferenceSubtype(type.NodeId, type.SuperTypeId, type.BrowseName);
            }
        }

        /// <summary>
        /// Recursively adds the types to the type tree.
        /// </summary>
        protected void AddTypesToTypeTree(NodeId typeId)
        {
            if (!PredefinedNodes.TryGetValue(typeId, out NodeState node))
            {
                return;
            }

            if (node is not BaseTypeState type)
            {
                return;
            }

            AddTypesToTypeTree(type);
        }

        /// <summary>
        /// Finds the specified and checks if it is of the expected type.
        /// </summary>
        /// <returns>Returns null if not found or not of the correct type.</returns>
        [Obsolete("Use FindPredefinedNode<T> instead.")]
        public NodeState FindPredefinedNode(NodeId nodeId, Type expectedType)
        {
            if (nodeId.IsNull)
            {
                return null;
            }

            if (!PredefinedNodes.TryGetValue(nodeId, out NodeState node))
            {
                return null;
            }

            if (expectedType != null && !expectedType.IsInstanceOfType(node))
            {
                return null;
            }

            return node;
        }

        /// <summary>
        /// Finds the specified and checks if it is of the expected type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>Returns null if not found or not of the correct type.</returns>
        public T FindPredefinedNode<T>(NodeId nodeId) where T : NodeState
        {
            if (nodeId.IsNull)
            {
                return null;
            }

            if (!PredefinedNodes.TryGetValue(nodeId, out NodeState node))
            {
                return null;
            }

            if (typeof(T) != null && !typeof(T).IsInstanceOfType(node))
            {
                return null;
            }

            return node is T typedNode ? typedNode : null;
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public virtual void DeleteAddressSpace()
        {
            NodeState[] nodes = [.. PredefinedNodes.Values];
            PredefinedNodes.Clear();

            foreach (NodeState node in nodes)
            {
                Utils.SilentDispose(node);
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        /// <remarks>
        /// This must efficiently determine whether the node belongs to the node manager. If it does belong to
        /// NodeManager it should return a handle that does not require the NodeId to be validated again when
        /// the handle is passed into other methods such as 'Read' or 'Write'.
        /// </remarks>
        public virtual object GetManagerHandle(NodeId nodeId)
        {
            return GetManagerHandle(SystemContext, nodeId, null);
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        /// <remarks>
        /// It is thread safe to call this method outside the node manager lock.
        /// </remarks>
        protected virtual NodeHandle GetManagerHandle(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache)
        {
            if (!IsNodeIdInNamespace(nodeId))
            {
                return null;
            }

            if (PredefinedNodes.TryGetValue(nodeId, out NodeState node))
            {
                return new NodeHandle
                {
                    NodeId = nodeId,
                    Node = node,
                    Validated = true
                };
            }
            return null;
        }

        /// <summary>
        /// This method is used to add bi-directional references to nodes from other node managers.
        /// </summary>
        /// <remarks>
        /// The additional references are optional, however, the NodeManager should support them.
        /// </remarks>
        public virtual void AddReferences(IDictionary<NodeId, IList<IReference>> references)
        {
            lock (Lock)
            {
                foreach (KeyValuePair<NodeId, IList<IReference>> current in references)
                {
                    // get the handle.
                    NodeHandle source = GetManagerHandle(SystemContext, current.Key, null);

                    // only support external references to nodes that are stored in memory.
                    if (source?.Node == null || !source.Validated)
                    {
                        continue;
                    }

                    // add reference to external target.
                    foreach (IReference reference in current.Value)
                    {
                        if (!source.Node.ReferenceExists(
                                reference.ReferenceTypeId,
                                reference.IsInverse,
                                reference.TargetId))
                        {
                            source.Node.AddReference(
                                reference.ReferenceTypeId,
                                reference.IsInverse,
                                reference.TargetId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This method is used to delete bi-directional references to nodes from other node managers.
        /// </summary>
        public virtual ServiceResult DeleteReference(
            object sourceHandle,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional)
        {
            // get the handle.
            NodeHandle source = IsHandleInNamespace(sourceHandle);

            if (source == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // only support external references to nodes that are stored in memory.
            if (!source.Validated || source.Node == null)
            {
                return StatusCodes.BadNotSupported;
            }

            lock (Lock)
            {
                // only support references to Source Areas.
                source.Node.RemoveReference(referenceTypeId, isInverse, targetId);

                if (deleteBidirectional)
                {
                    // check if the target is also managed by this node manager.
                    if (!targetId.IsAbsolute)
                    {
                        NodeHandle target = GetManagerHandle(SystemContext, (NodeId)targetId, null);

                        if (target != null && target.Validated && target.Node != null)
                        {
                            target.Node.RemoveReference(referenceTypeId, !isInverse, source.NodeId);
                        }
                    }
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Returns the basic metadata for the node. Returns null if the node does not exist.
        /// </summary>
        /// <remarks>
        /// This method validates any placeholder handle.
        /// </remarks>
        public virtual NodeMetadata GetNodeMetadata(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            // check for valid handle.
            NodeHandle handle = IsHandleInNamespace(targetHandle);

            if (handle == null)
            {
                return null;
            }

            lock (Lock)
            {
                // validate node.
                NodeState target = ValidateNode(systemContext, handle, null);

                if (target == null)
                {
                    return null;
                }

                // read the attributes.
                ArrayOf<Variant> values = target.ReadAttributes(
                    systemContext,
                    Attributes.WriteMask,
                    Attributes.UserWriteMask,
                    Attributes.DataType,
                    Attributes.ValueRank,
                    Attributes.ArrayDimensions,
                    Attributes.AccessLevel,
                    Attributes.UserAccessLevel,
                    Attributes.EventNotifier,
                    Attributes.Executable,
                    Attributes.UserExecutable,
                    Attributes.AccessRestrictions,
                    Attributes.RolePermissions,
                    Attributes.UserRolePermissions);

                // construct the meta-data object.
                var metadata = new NodeMetadata(target, target.NodeId)
                {
                    NodeClass = target.NodeClass,
                    BrowseName = target.BrowseName,
                    DisplayName = target.DisplayName
                };


                if (values[0].TryGet(out uint writeMask) &&
                    values[1].TryGet(out uint userWriteMask))
                {
                    metadata.WriteMask = (AttributeWriteMask)(writeMask & userWriteMask);
                }
                if (values[2].TryGet(out NodeId dataType))
                {
                    metadata.DataType = dataType;
                }
                if (values[3].TryGet(out int valueRank))
                {
                    metadata.ValueRank = valueRank;
                }
                if (values[4].TryGet(out ArrayOf<uint> arrayDimensions))
                {
                    metadata.ArrayDimensions = arrayDimensions;
                }
                if (values[5].TryGet(out byte accessLevel) &&
                    values[6].TryGet(out byte userAccessLevel))
                {
                    metadata.AccessLevel = (byte)(accessLevel & userAccessLevel);
                }

                if (values[7].TryGet(out byte eventNotifier))
                {
                    metadata.EventNotifier = eventNotifier;
                }

                if (values[8].TryGet(out bool executable) &&
                    values[9].TryGet(out bool userExecutable))
                {
                    metadata.Executable = executable && userExecutable;
                }

                if (values[10].TryGet(out ushort accessRestriction))
                {
                    metadata.AccessRestrictions = (AccessRestrictionType)accessRestriction;
                }

                if (values[11].TryGetStructure(
                    out ArrayOf<RolePermissionType> rolePermissions))
                {
                    metadata.RolePermissions = rolePermissions;
                }

                if (values[12].TryGetStructure(
                    out ArrayOf<RolePermissionType> userRolePermissions))
                {
                    metadata.UserRolePermissions = userRolePermissions;
                }

                SetDefaultPermissions(systemContext, target, metadata);

                // get instance references.
                if (target is BaseInstanceState instance)
                {
                    metadata.TypeDefinition = instance.TypeDefinitionId;
                    metadata.ModellingRule = instance.ModellingRuleId;
                }

                // fill in the common attributes.
                return metadata;
            }
        }

        /// <summary>
        /// Sets the AccessRestrictions, RolePermissions and UserRolePermissions values in the metadata
        /// </summary>
        private static void SetAccessAndRolePermissions(ArrayOf<Variant> values, NodeMetadata metadata)
        {
            if (values[0].TryGet(out ushort accessRestrictions))
            {
                metadata.AccessRestrictions = (AccessRestrictionType)accessRestrictions;
            }

            if (values[1].TryGetStructure(
                out ArrayOf<RolePermissionType> rolePermissions))
            {
                metadata.RolePermissions = rolePermissions;
            }

            if (values[2].TryGetStructure(
                out ArrayOf<RolePermissionType> userRolePermissions))
            {
                metadata.UserRolePermissions = userRolePermissions;
            }
        }

        /// <summary>
        /// Reads and caches the Attributes used by the AccessRestrictions and RolePermission validation process
        /// </summary>
        /// <param name="uniqueNodesServiceAttributes">The cache used to save the attributes</param>
        /// <param name="systemContext">The context</param>
        /// <param name="target">The target for which the attributes are read and cached</param>
        /// <param name="key">The key representing the NodeId for which the cache is kept</param>
        /// <returns>The values of the attributes</returns>
        private static ArrayOf<Variant> ReadAndCacheValidationAttributes(
            Dictionary<NodeId, ArrayOf<Variant>> uniqueNodesServiceAttributes,
            ServerSystemContext systemContext,
            NodeState target,
            NodeId key)
        {
            ArrayOf<Variant> values = ReadValidationAttributes(systemContext, target);
            uniqueNodesServiceAttributes[key] = values;

            return values;
        }

        /// <summary>
        /// Reads the Attributes used by the AccessRestrictions and RolePermission validation process
        /// </summary>
        /// <param name="systemContext">The context</param>
        /// <param name="target">The target for which the attributes are read and cached</param>
        /// <returns>The values of the attributes</returns>
        private static ArrayOf<Variant> ReadValidationAttributes(
            ServerSystemContext systemContext,
            NodeState target)
        {
            // This is the list of attributes to be populated by GetNodeMetadata from CustomNodeManagers.
            // The are originating from services in the context of AccessRestrictions and RolePermission validation.
            // For such calls the other attributes are ignored since reading them might trigger unnecessary callbacks
            return target.ReadAttributes(
                systemContext,
                Attributes.AccessRestrictions,
                Attributes.RolePermissions,
                Attributes.UserRolePermissions);
        }

        /// <summary>
        /// Browses the references from a node managed by the node manager.
        /// </summary>
        /// <remarks>
        /// The continuation point is created for every browse operation and contains the browse parameters.
        /// The node manager can store its state information in the Data and Index properties.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="continuationPoint"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void Browse(
            OperationContext context,
            ref ContinuationPoint continuationPoint,
            IList<ReferenceDescription> references)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            ServerSystemContext systemContext = SystemContext.Copy(context);

            // check for valid view.
            ValidateViewDescription(systemContext, continuationPoint.View);

            INodeBrowser browser = null;

            // check for valid handle.
            NodeHandle handle =
                IsHandleInNamespace(continuationPoint.NodeToBrowse)
                ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            lock (Lock)
            {
                // validate node.
                NodeState source =
                    ValidateNode(systemContext, handle, null)
                    ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);

                // check if node is in the view.
                if (!IsNodeInView(systemContext, continuationPoint, source))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeNotInView);
                }

                // check for previous continuation point.
                browser = continuationPoint.Data as INodeBrowser;

                // fetch list of references.
                if (browser == null)
                {
                    // create a new browser.
                    continuationPoint.Data = browser = source.CreateBrowser(
                        systemContext,
                        continuationPoint.View,
                        continuationPoint.ReferenceTypeId,
                        continuationPoint.IncludeSubtypes,
                        continuationPoint.BrowseDirection,
                        default,
                        null,
                        false);
                }
            }
            // prevent multiple access the browser object.
            lock (browser)
            {
                // apply filters to references.
                var cache = new Dictionary<NodeId, NodeState>();

                for (IReference reference = browser.Next();
                    reference != null;
                    reference = browser.Next())
                {
                    // validate Browse permission
                    ServiceResult serviceResult = ValidateRolePermissions(
                        context,
                        ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris),
                        PermissionType.Browse);
                    if (ServiceResult.IsBad(serviceResult))
                    {
                        // ignore reference
                        continue;
                    }
                    // create the type definition reference.
                    ReferenceDescription description = GetReferenceDescription(
                        systemContext,
                        cache,
                        reference,
                        continuationPoint);

                    if (description == null)
                    {
                        continue;
                    }

                    // check if limit reached.
                    if (continuationPoint.MaxResultsToReturn != 0 &&
                        references.Count >= continuationPoint.MaxResultsToReturn)
                    {
                        browser.Push(reference);
                        return;
                    }

                    references.Add(description);
                }

                // release the continuation point if all done.
                continuationPoint.Dispose();
                continuationPoint = null;
            }
        }

        /// <summary>
        /// Validates the view description passed to a browse request (throws on error).
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void ValidateViewDescription(
            ServerSystemContext context,
            ViewDescription view)
        {
            if (ViewDescription.IsDefault(view))
            {
                return;
            }

            _ =
                FindPredefinedNode<ViewState>(view.ViewId)
                ?? throw new ServiceResultException(StatusCodes.BadViewIdUnknown);

            if (view.Timestamp != DateTime.MinValue)
            {
                throw new ServiceResultException(StatusCodes.BadViewTimestampInvalid);
            }

            if (view.ViewVersion != 0)
            {
                throw new ServiceResultException(StatusCodes.BadViewVersionInvalid);
            }
        }

        /// <summary>
        /// Checks if the node is in the view.
        /// </summary>
        protected virtual bool IsNodeInView(
            ServerSystemContext context,
            ContinuationPoint continuationPoint,
            NodeState node)
        {
            if (continuationPoint == null || ViewDescription.IsDefault(continuationPoint.View))
            {
                return true;
            }

            return IsNodeInView(context, continuationPoint.View.ViewId, node);
        }

        /// <summary>
        /// Checks if the node is in the view.
        /// </summary>
        protected virtual bool IsNodeInView(
            ServerSystemContext context,
            NodeId viewId,
            NodeState node)
        {
            ViewState view = FindPredefinedNode<ViewState>(viewId);

            return view != null;
        }

        /// <summary>
        /// Checks if the reference is in the view.
        /// </summary>
        protected virtual bool IsReferenceInView(
            ServerSystemContext context,
            ContinuationPoint continuationPoint,
            IReference reference)
        {
            return true;
        }

        /// <summary>
        /// Returns the references for the node that meets the criteria specified.
        /// </summary>
        protected virtual ReferenceDescription GetReferenceDescription(
            ServerSystemContext context,
            Dictionary<NodeId, NodeState> cache,
            IReference reference,
            ContinuationPoint continuationPoint)
        {
            _ = SystemContext.Copy(context);

            // create the type definition reference.
            var description = new ReferenceDescription { NodeId = reference.TargetId };
            description.SetReferenceType(
                continuationPoint.ResultMask,
                reference.ReferenceTypeId,
                !reference.IsInverse);

            // check if reference is in the view.
            if (!IsReferenceInView(context, continuationPoint, reference))
            {
                return null;
            }

            // do not cache target parameters for remote nodes.
            if (reference.TargetId.IsAbsolute)
            {
                // only return remote references if no node class filter is specified.
                if (continuationPoint.NodeClassMask != 0)
                {
                    return null;
                }

                return description;
            }

            NodeState target = null;

            // check for local reference.

            if (reference is NodeStateReference referenceInfo)
            {
                target = referenceInfo.Target;
            }

            // check for internal reference.
            if (target == null)
            {
                NodeHandle handle = GetManagerHandle(context, (NodeId)reference.TargetId, null);

                if (handle != null)
                {
                    target = ValidateNode(context, handle, null);
                }
            }

            // the target may be a reference to a node in another node manager. In these cases
            // the target attributes must be fetched by the caller. The Unfiltered flag tells the
            // caller to do that.
            if (target == null)
            {
                description.Unfiltered = true;
                return description;
            }

            // apply node class filter.
            if (continuationPoint.NodeClassMask != 0 &&
                ((continuationPoint.NodeClassMask & (uint)target.NodeClass) == 0))
            {
                return null;
            }

            // check if target is in the view.
            if (!IsNodeInView(context, continuationPoint, target))
            {
                return null;
            }

            // look up the type definition.
            NodeId typeDefinition = default;

            if (target is BaseInstanceState instance)
            {
                typeDefinition = instance.TypeDefinitionId;
            }

            // set target attributes.
            description.SetTargetAttributes(
                continuationPoint.ResultMask,
                target.NodeClass,
                target.BrowseName,
                target.DisplayName,
                typeDefinition);

            return description;
        }

        /// <summary>
        /// Returns the target of the specified browse path fragment(s).
        /// </summary>
        /// <remarks>
        /// If reference exists but the node manager does not know the browse name it must
        /// return the NodeId as an unresolvedTargetIds. The caller will try to check the
        /// browse name.
        /// </remarks>
        public virtual void TranslateBrowsePath(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();

            // check for valid handle.
            NodeHandle handle = IsHandleInNamespace(sourceHandle);

            if (handle == null)
            {
                return;
            }

            lock (Lock)
            {
                // validate node.
                NodeState source = ValidateNode(systemContext, handle, operationCache);

                if (source == null)
                {
                    return;
                }

                // get list of references that relative path.
                INodeBrowser browser = source.CreateBrowser(
                    systemContext,
                    null,
                    relativePath.ReferenceTypeId,
                    relativePath.IncludeSubtypes,
                    relativePath.IsInverse ? BrowseDirection.Inverse : BrowseDirection.Forward,
                    relativePath.TargetName,
                    null,
                    false);

                // check the browse names.
                try
                {
                    for (IReference reference = browser.Next();
                        reference != null;
                        reference = browser.Next())
                    {
                        // ignore unknown external references.
                        if (reference.TargetId.IsAbsolute)
                        {
                            continue;
                        }

                        NodeState target = null;

                        // check for local reference.

                        if (reference is NodeStateReference referenceInfo)
                        {
                            target = referenceInfo.Target;
                        }

                        if (target == null)
                        {
                            var targetId = (NodeId)reference.TargetId;

                            // the target may be a reference to a node in another node manager.
                            if (!IsNodeIdInNamespace(targetId))
                            {
                                unresolvedTargetIds.Add((NodeId)reference.TargetId);
                                continue;
                            }

                            // look up the target manually.
                            NodeHandle targetHandle = GetManagerHandle(
                                systemContext,
                                targetId,
                                operationCache);

                            if (targetHandle == null)
                            {
                                continue;
                            }

                            // validate target.
                            target = ValidateNode(systemContext, targetHandle, operationCache);

                            if (target == null)
                            {
                                continue;
                            }
                        }

                        // check browse name.
                        if (target.BrowseName == relativePath.TargetName &&
                            !targetIds.Contains(reference.TargetId))
                        {
                            targetIds.Add(reference.TargetId);
                        }
                    }
                }
                finally
                {
                    browser.Dispose();
                }
            }
        }

        /// <summary>
        /// Reads the value for the specified attribute.
        /// </summary>
        public virtual void Read(
            OperationContext context,
            double maxAge,
            IList<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            var nodesToValidate = new List<NodeHandle>();

            lock (Lock)
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    ReadValueId nodeToRead = nodesToRead[ii];

                    // skip items that have already been processed.
                    if (nodeToRead.Processed)
                    {
                        continue;
                    }

                    // check for valid handle.
                    NodeHandle handle = GetManagerHandle(
                        systemContext,
                        nodeToRead.NodeId,
                        operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    // create an initial value.
                    DataValue value = values[ii] = new DataValue();

                    value.Value = null;
                    value.ServerTimestamp = DateTime.MinValue; // Will be set after ReadAttribute
                    value.SourceTimestamp = DateTime.MinValue;
                    value.StatusCode = StatusCodes.Good;

                    // check if the node is a area in memory.
                    if (handle.Node == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;

                        // must validate node in a separate operation
                        handle.Index = ii;
                        nodesToValidate.Add(handle);

                        continue;
                    }

                    // read the attribute value.
                    errors[ii] = handle.Node.ReadAttribute(
                        systemContext,
                        nodeToRead.AttributeId,
                        nodeToRead.ParsedIndexRange,
                        nodeToRead.DataEncoding,
                        value);

                    // Set timestamps after ReadAttribute to ensure consistency
                    // For Value attributes, match ServerTimestamp to SourceTimestamp
                    // For other attributes, just ensure ServerTimestamp is set
                    if (nodeToRead.AttributeId == Attributes.Value)
                    {
                        if (value.SourceTimestamp == DateTime.MinValue)
                        {
                            value.SourceTimestamp = DateTime.UtcNow;
                        }
                        value.ServerTimestamp = value.SourceTimestamp;
                    }
                    else
                    {
                        // For non-value attributes, only ServerTimestamp is relevant
                        if (value.ServerTimestamp == DateTime.MinValue)
                        {
                            value.ServerTimestamp = DateTime.UtcNow;
                        }
                    }
#if DEBUG
                    if (nodeToRead.AttributeId == Attributes.Value)
                    {
                        m_logger.LogTrace(
                            Utils.TraceMasks.ServiceDetail,
                            "READ: NodeId={NodeId} Value={Value} Range={Range}",
                            nodeToRead.NodeId,
                            value.WrappedValue,
                            nodeToRead.IndexRange);
                    }
#endif
                }

                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }
            }

            // validates the nodes (reads values from the underlying data source if required).
            Read(systemContext, nodesToRead, values, errors, nodesToValidate, operationCache);
        }

        /// <summary>
        /// Finds a node in the dynamic cache.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="handle">The node handle.</param>
        /// <param name="cache">The cache to search.</param>
        /// <returns>The node if found. Null otherwise.</returns>
        protected virtual NodeState FindNodeInCache(
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

            // construct id for root node.
            NodeId rootId = handle.RootId;

            if (cache != null)
            {
                // lookup component in local cache for request.
                if (cache.TryGetValue(handle.NodeId, out NodeState target))
                {
                    return target;
                }

                // lookup root in local cache for request.
                if (!string.IsNullOrEmpty(handle.ComponentPath) &&
                    cache.TryGetValue(rootId, out target))
                {
                    target = target.FindChildBySymbolicName(context, handle.ComponentPath);

                    // component exists.
                    if (target != null)
                    {
                        return target;
                    }
                }
            }

            // lookup component in shared cache.
            return LookupNodeInComponentCache(context, handle);
        }

        /// <summary>
        /// Marks the handle as validated and saves the node in the dynamic cache.
        /// </summary>
        protected virtual NodeState ValidationComplete(
            ServerSystemContext context,
            NodeHandle handle,
            NodeState node,
            IDictionary<NodeId, NodeState> cache)
        {
            handle.Node = node;
            handle.Validated = true;

            if (cache != null && handle != null)
            {
                cache[handle.NodeId] = node;
            }

            return node;
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected virtual NodeState ValidateNode(
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

            // return default.
            return handle.Node;
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
        protected virtual void Read(
            ServerSystemContext context,
            IList<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache)
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
        /// Writes the value for the specified attributes.
        /// </summary>
        public virtual void Write(
            OperationContext context,
            IList<WriteValue> nodesToWrite,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            var nodesToValidate = new List<NodeHandle>();

            lock (Lock)
            {
                for (int ii = 0; ii < nodesToWrite.Count; ii++)
                {
                    WriteValue nodeToWrite = nodesToWrite[ii];

                    // skip items that have already been processed.
                    if (nodeToWrite.Processed)
                    {
                        continue;
                    }

                    // check for valid handle.
                    NodeHandle handle = GetManagerHandle(
                        systemContext,
                        nodeToWrite.NodeId,
                        operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToWrite.Processed = true;

                    // index range is not supported.
                    if (nodeToWrite.AttributeId != Attributes.Value &&
                        !string.IsNullOrEmpty(nodeToWrite.IndexRange))
                    {
                        errors[ii] = StatusCodes.BadWriteNotSupported;
                        continue;
                    }

                    // check if the node is a area in memory.
                    if (handle.Node == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;

                        // must validate node in a separate operation.
                        handle.Index = ii;
                        nodesToValidate.Add(handle);

                        continue;
                    }

                    // check if the node is AnalogItem and the values are outside the InstrumentRange.
                    if (handle.Node is AnalogItemState analogItemState &&
                        analogItemState.InstrumentRange != null)
                    {
                        try
                        {
                            if (nodeToWrite.Value.Value is Array array)
                            {
                                bool isOutOfRange = false;
                                foreach (object arrayValue in array)
                                {
                                    double newValue = Convert.ToDouble(
                                        arrayValue,
                                        CultureInfo.InvariantCulture);
                                    if (newValue > analogItemState.InstrumentRange.Value.High ||
                                        newValue < analogItemState.InstrumentRange.Value.Low)
                                    {
                                        isOutOfRange = true;
                                        break;
                                    }
                                }
                                if (isOutOfRange)
                                {
                                    errors[ii] = StatusCodes.BadOutOfRange;
                                    continue;
                                }
                            }
                            else
                            {
                                double newValue = Convert.ToDouble(
                                    nodeToWrite.Value.Value,
                                    CultureInfo.InvariantCulture);

                                if (newValue > analogItemState.InstrumentRange.Value.High ||
                                    newValue < analogItemState.InstrumentRange.Value.Low)
                                {
                                    errors[ii] = StatusCodes.BadOutOfRange;
                                    continue;
                                }
                            }
                        }
                        catch
                        {
                            //skip the InstrumentRange check if the transformation isn't possible.
                        }
                    }

#if DEBUG
                    m_logger.LogTrace(
                        Utils.TraceMasks.ServiceDetail,
                        "WRITE: NodeId={NodeId} Value={Value} Range={Range}",
                        nodeToWrite.NodeId,
                        nodeToWrite.Value.WrappedValue,
                        nodeToWrite.IndexRange);
#endif
                    var propertyState = handle.Node as PropertyState;
                    Variant previousPropertyValue = propertyState?.Value ?? default;

                    DataValue oldValue = null;

                    if (Server?.Auditing == true)
                    {
                        //current server supports auditing
                        oldValue = new DataValue();
                        // read the old value for the purpose of auditing
                        handle.Node.ReadAttribute(
                            systemContext,
                            nodeToWrite.AttributeId,
                            nodeToWrite.ParsedIndexRange,
                            default,
                            oldValue);
                    }

                    // write the attribute value.
                    errors[ii] = handle.Node.WriteAttribute(
                        systemContext,
                        nodeToWrite.AttributeId,
                        nodeToWrite.ParsedIndexRange,
                        nodeToWrite.Value);

                    // report the write value audit event
                    Server.ReportAuditWriteUpdateEvent(
                        systemContext,
                        nodeToWrite,
                        oldValue,
                        errors[ii]?.StatusCode ?? StatusCodes.Good,
                        m_logger);

                    if (!ServiceResult.IsGood(errors[ii]))
                    {
                        continue;
                    }

                    if (propertyState != null)
                    {
                        CheckIfSemanticsHaveChanged(
                            systemContext,
                            propertyState,
                            nodeToWrite.Value,
                            previousPropertyValue);
                    }

                    //not needed for sampling groups
                    if (m_monitoredItemManager is MonitoredNodeMonitoredItemManager)
                    {
                        // updates to source finished - report changes to monitored items.
                        handle.Node.ClearChangeMasks(systemContext, true);
                    }
                }

                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }
            }

            // validates the nodes and writes the value to the underlying system.
            Write(systemContext, nodesToWrite, errors, nodesToValidate, operationCache);
        }

        private void CheckIfSemanticsHaveChanged(
            ServerSystemContext systemContext,
            PropertyState property,
            Variant newPropertyValue,
            Variant previousPropertyValue)
        {
            // check if the changed property is one that can trigger semantic changes
            string propertyName = property.BrowseName.Name;

            if (propertyName
                is not BrowseNames.EURange
                    and not BrowseNames.InstrumentRange
                    and not BrowseNames.EngineeringUnits
                    and not BrowseNames.Title
                    and not BrowseNames.AxisDefinition
                    and not BrowseNames.FalseState
                    and not BrowseNames.TrueState
                    and not BrowseNames.EnumStrings
                    and not BrowseNames.XAxisDefinition
                    and not BrowseNames.YAxisDefinition
                    and not BrowseNames.ZAxisDefinition)
            {
                return;
            }

            // ceck if property value changed
            if (Utils.IsEqual(newPropertyValue, previousPropertyValue))
            {
                return;
            }

            foreach (KeyValuePair<uint, IMonitoredItem> kvp in MonitoredItems)
            {
                if (kvp.Value is not ISampledDataChangeMonitoredItem monitoredItem ||
                    monitoredItem.AttributeId != Attributes.Value)
                {
                    continue;
                }

                // Try to get the node from the handle
                if (monitoredItem.ManagerHandle is not NodeHandle handle || handle.Node == null)
                {
                    continue;
                }

                NodeState node = handle.Node;
                BaseInstanceState propertyState = node.FindChild(
                    systemContext,
                    property.BrowseName);

                if (propertyState != null &&
                    property != null &&
                    propertyState.NodeId == property.NodeId)
                {
                    if ((
                            node is AnalogItemState &&
                            (propertyName == BrowseNames.EURange ||
                                propertyName == BrowseNames.EngineeringUnits)
                        ) ||
                        (
                            node is TwoStateDiscreteState &&
                            (propertyName == BrowseNames.FalseState ||
                                propertyName == BrowseNames.TrueState)
                        ) ||
                        (node is MultiStateDiscreteState &&
                            (propertyName == BrowseNames.EnumStrings)) ||
                        (
                            node is ArrayItemState &&
                            (
                                propertyName == BrowseNames.InstrumentRange ||
                                propertyName == BrowseNames.EURange ||
                                propertyName == BrowseNames.EngineeringUnits ||
                                propertyName == BrowseNames.Title)
                        ) ||
                        (
                            (node is YArrayItemState || node is XYArrayItemState) &&
                            (
                                propertyName == BrowseNames.InstrumentRange ||
                                propertyName == BrowseNames.EURange ||
                                propertyName == BrowseNames.EngineeringUnits ||
                                propertyName == BrowseNames.Title ||
                                propertyName == BrowseNames.XAxisDefinition)
                        ) ||
                        (
                            node is ImageItemState &&
                            (
                                propertyName == BrowseNames.InstrumentRange ||
                                propertyName == BrowseNames.EURange ||
                                propertyName == BrowseNames.EngineeringUnits ||
                                propertyName == BrowseNames.Title ||
                                propertyName == BrowseNames.XAxisDefinition ||
                                propertyName == BrowseNames.YAxisDefinition)
                        ) ||
                        (
                            node is CubeItemState &&
                            (
                                propertyName == BrowseNames.InstrumentRange ||
                                propertyName == BrowseNames.EURange ||
                                propertyName == BrowseNames.EngineeringUnits ||
                                propertyName == BrowseNames.Title ||
                                propertyName == BrowseNames.XAxisDefinition ||
                                propertyName == BrowseNames.YAxisDefinition ||
                                propertyName == BrowseNames.ZAxisDefinition)
                        ) ||
                        (
                            node is NDimensionArrayItemState &&
                            (
                                propertyName == BrowseNames.InstrumentRange ||
                                propertyName == BrowseNames.EURange ||
                                propertyName == BrowseNames.EngineeringUnits ||
                                propertyName == BrowseNames.Title ||
                                propertyName == BrowseNames.AxisDefinition)))
                    {
                        monitoredItem.SetSemanticsChanged();

                        var value = new DataValue { ServerTimestamp = DateTime.UtcNow };

                        node.ReadAttribute(
                            systemContext,
                            Attributes.Value,
                            monitoredItem.IndexRange,
                            default,
                            value);

                        monitoredItem.QueueValue(value, ServiceResult.Good, true);
                    }
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
        protected virtual void Write(
            ServerSystemContext context,
            IList<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToValidate,
            IDictionary<NodeId, NodeState> cache)
        {
            // validates the nodes (reads values from the underlying data source if required).
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

                    WriteValue nodeToWrite = nodesToWrite[handle.Index];

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
        }

        /// <summary>
        /// Reads the history for the specified nodes.
        /// </summary>
        public virtual void HistoryRead(
            OperationContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            var nodesToProcess = new List<NodeHandle>();

            lock (Lock)
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    HistoryReadValueId nodeToRead = nodesToRead[ii];

                    // skip items that have already been processed.
                    if (nodeToRead.Processed)
                    {
                        continue;
                    }

                    // check for valid handle.
                    NodeHandle handle = GetManagerHandle(
                        systemContext,
                        nodeToRead.NodeId,
                        operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    // create an initial result.
                    HistoryReadResult result = results[ii] = new HistoryReadResult();

                    result.HistoryData = default;
                    result.ContinuationPoint = default;
                    result.StatusCode = StatusCodes.Good;

                    // check if the node is a area in memory.
                    if (handle.Node == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;

                        // must validate node in a separate operation
                        handle.Index = ii;
                        nodesToProcess.Add(handle);

                        continue;
                    }

                    errors[ii] = StatusCodes.BadHistoryOperationUnsupported;

                    // check for data history variable.

                    if (handle.Node is BaseVariableState variable &&
                        (variable.AccessLevel & AccessLevels.HistoryRead) != 0)
                    {
                        handle.Index = ii;
                        nodesToProcess.Add(handle);
                        continue;
                    }

                    // check for event history object.

                    if (handle.Node is BaseObjectState notifier &&
                        (notifier.EventNotifier & EventNotifiers.HistoryRead) != 0)
                    {
                        handle.Index = ii;
                        nodesToProcess.Add(handle);
                    }
                }

                // check for nothing to do.
                if (nodesToProcess.Count == 0)
                {
                    return;
                }
            }

            // validates the nodes (reads values from the underlying data source if required).
            HistoryRead(
                systemContext,
                details,
                timestampsToReturn,
                releaseContinuationPoints,
                nodesToRead,
                results,
                errors,
                nodesToProcess,
                operationCache);
        }

        /// <summary>
        /// Releases the continuation points.
        /// </summary>
        protected virtual void HistoryReleaseContinuationPoints(
            ServerSystemContext context,
            IList<HistoryReadValueId> nodesToRead,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
            }
        }

        /// <summary>
        /// Reads raw history data.
        /// </summary>
        protected virtual void HistoryReadRawModified(
            ServerSystemContext context,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Reads processed history data.
        /// </summary>
        protected virtual void HistoryReadProcessed(
            ServerSystemContext context,
            ReadProcessedDetails details,
            TimestampsToReturn timestampsToReturn,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Reads history data at specified times.
        /// </summary>
        protected virtual void HistoryReadAtTime(
            ServerSystemContext context,
            ReadAtTimeDetails details,
            TimestampsToReturn timestampsToReturn,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Reads history events.
        /// </summary>
        protected virtual void HistoryReadEvents(
            ServerSystemContext context,
            ReadEventDetails details,
            TimestampsToReturn timestampsToReturn,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Validates the nodes and reads the values from the underlying source.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void HistoryRead(
            ServerSystemContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            // check if continuation points are being released.
            if (releaseContinuationPoints)
            {
                HistoryReleaseContinuationPoints(
                    context,
                    nodesToRead,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // check timestamps to return.
            if (timestampsToReturn is < TimestampsToReturn.Source or > TimestampsToReturn.Neither)
            {
                throw new ServiceResultException(StatusCodes.BadTimestampsToReturnInvalid);
            }

            // handle raw data request.

            if (details is ReadRawModifiedDetails readRawModifiedDetails)
            {
                // at least one must be provided.
                if (readRawModifiedDetails.StartTime == DateTime.MinValue &&
                    readRawModifiedDetails.EndTime == DateTime.MinValue)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                }

                // if one is null the num values must be provided.
                if (readRawModifiedDetails.StartTime == DateTime.MinValue ||
                    readRawModifiedDetails.EndTime == DateTime.MinValue)
                {
                    if (readRawModifiedDetails.NumValuesPerNode == 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                    }
                }

                HistoryReadRawModified(
                    context,
                    readRawModifiedDetails,
                    timestampsToReturn,
                    nodesToRead,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle processed data request.

            if (details is ReadProcessedDetails readProcessedDetails)
            {
                // check the list of aggregates.
                if (readProcessedDetails.AggregateType == null ||
                    readProcessedDetails.AggregateType.Count != nodesToRead.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadAggregateListMismatch);
                }

                // check start/end time.
                if (readProcessedDetails.StartTime == DateTime.MinValue ||
                    readProcessedDetails.EndTime == DateTime.MinValue)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                }

                HistoryReadProcessed(
                    context,
                    readProcessedDetails,
                    timestampsToReturn,
                    nodesToRead,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle raw data at time request.

            if (details is ReadAtTimeDetails readAtTimeDetails)
            {
                HistoryReadAtTime(
                    context,
                    readAtTimeDetails,
                    timestampsToReturn,
                    nodesToRead,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle read events request.

            if (details is ReadEventDetails readEventDetails)
            {
                // check start/end time and max values.
                if (readEventDetails.NumValuesPerNode == 0)
                {
                    if (readEventDetails.StartTime == DateTime.MinValue ||
                        readEventDetails.EndTime == DateTime.MinValue)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                    }
                }
                else if (readEventDetails.StartTime == DateTime.MinValue &&
                    readEventDetails.EndTime == DateTime.MinValue)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                }

                // validate the event filter.
                EventFilter.Result result = readEventDetails.Filter.Validate(
                    new FilterContext(Server.NamespaceUris, Server.TypeTree, context, Server.Telemetry));

                if (ServiceResult.IsBad(result.Status))
                {
                    throw new ServiceResultException(result.Status);
                }

                // read the event history.
                HistoryReadEvents(
                    context,
                    readEventDetails,
                    timestampsToReturn,
                    nodesToRead,
                    results,
                    errors,
                    nodesToProcess,
                    cache);
            }
        }

        /// <summary>
        /// Updates the history for the specified nodes.
        /// </summary>
        public virtual void HistoryUpdate(
            OperationContext context,
            Type detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            var nodesToProcess = new List<NodeHandle>();

            lock (Lock)
            {
                for (int ii = 0; ii < nodesToUpdate.Count; ii++)
                {
                    HistoryUpdateDetails nodeToUpdate = nodesToUpdate[ii];

                    // skip items that have already been processed.
                    if (nodeToUpdate.Processed)
                    {
                        continue;
                    }

                    // check for valid handle.
                    NodeHandle handle = GetManagerHandle(
                        systemContext,
                        nodeToUpdate.NodeId,
                        operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToUpdate.Processed = true;

                    // create an initial result.
                    HistoryUpdateResult result = results[ii] = new HistoryUpdateResult();
                    result.StatusCode = StatusCodes.Good;

                    // check if the node is a area in memory.
                    if (handle.Node == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;

                        // must validate node in a separate operation
                        handle.Index = ii;
                        nodesToProcess.Add(handle);
                        continue;
                    }

                    errors[ii] = StatusCodes.BadHistoryOperationUnsupported;

                    // check for data history variable.

                    if (handle.Node is BaseVariableState variable &&
                        (variable.AccessLevel & AccessLevels.HistoryWrite) != 0)
                    {
                        handle.Index = ii;
                        nodesToProcess.Add(handle);
                        continue;
                    }

                    // check for event history object.

                    if (handle.Node is BaseObjectState notifier &&
                        (notifier.EventNotifier & EventNotifiers.HistoryWrite) != 0)
                    {
                        handle.Index = ii;
                        nodesToProcess.Add(handle);
                    }
                }

                // check for nothing to do.
                if (nodesToProcess.Count == 0)
                {
                    return;
                }
            }

            // validates the nodes and updates.
            HistoryUpdate(
                systemContext,
                detailsType,
                nodesToUpdate,
                results,
                errors,
                nodesToProcess,
                operationCache);
        }

        /// <summary>
        /// Validates the nodes and updates the history.
        /// </summary>
        protected virtual void HistoryUpdate(
            ServerSystemContext context,
            Type detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            // handle update data request.
            if (detailsType == typeof(UpdateDataDetails))
            {
                var details = new UpdateDataDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (UpdateDataDetails)nodesToUpdate[ii];
                }

                HistoryUpdateData(context, details, results, errors, nodesToProcess, cache);

                return;
            }

            // handle update structure data request.
            if (detailsType == typeof(UpdateStructureDataDetails))
            {
                var details = new UpdateStructureDataDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (UpdateStructureDataDetails)nodesToUpdate[ii];
                }

                HistoryUpdateStructureData(
                    context,
                    details,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle update events request.
            if (detailsType == typeof(UpdateEventDetails))
            {
                var details = new UpdateEventDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (UpdateEventDetails)nodesToUpdate[ii];
                }

                HistoryUpdateEvents(context, details, results, errors, nodesToProcess, cache);

                return;
            }

            // handle delete raw data request.
            if (detailsType == typeof(DeleteRawModifiedDetails))
            {
                var details = new DeleteRawModifiedDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (DeleteRawModifiedDetails)nodesToUpdate[ii];
                }

                HistoryDeleteRawModified(context, details, results, errors, nodesToProcess, cache);

                return;
            }

            // handle delete at time request.
            if (detailsType == typeof(DeleteAtTimeDetails))
            {
                var details = new DeleteAtTimeDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (DeleteAtTimeDetails)nodesToUpdate[ii];
                }

                HistoryDeleteAtTime(context, details, results, errors, nodesToProcess, cache);

                return;
            }

            // handle delete at time request.
            if (detailsType == typeof(DeleteEventDetails))
            {
                var details = new DeleteEventDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (DeleteEventDetails)nodesToUpdate[ii];
                }

                HistoryDeleteEvents(context, details, results, errors, nodesToProcess, cache);
            }
        }

        /// <summary>
        /// Updates the data history for one or more nodes.
        /// </summary>
        protected virtual void HistoryUpdateData(
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

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Updates the structured data history for one or more nodes.
        /// </summary>
        protected virtual void HistoryUpdateStructureData(
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

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Updates the event history for one or more nodes.
        /// </summary>
        protected virtual void HistoryUpdateEvents(
            ServerSystemContext context,
            IList<UpdateEventDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Deletes the data history for one or more nodes.
        /// </summary>
        protected virtual void HistoryDeleteRawModified(
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

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Deletes the data history for one or more nodes.
        /// </summary>
        protected virtual void HistoryDeleteAtTime(
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

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Deletes the event history for one or more nodes.
        /// </summary>
        protected virtual void HistoryDeleteEvents(
            ServerSystemContext context,
            IList<DeleteEventDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];

                // validate node.
                NodeState source = ValidateNode(context, handle, cache);

                if (source == null)
                {
                    continue;
                }

                errors[handle.Index] = StatusCodes.BadHistoryOperationUnsupported;
            }
        }

        /// <summary>
        /// Calls a method on the specified nodes.
        /// </summary>
        public virtual void Call(
            OperationContext context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors)
        {
#pragma warning disable CA2012 // Use ValueTasks correctly
            _ = CallInternalAsync(
                context,
                methodsToCall,
                results,
                errors,
                sync: true);
#pragma warning restore CA2012 // Use ValueTasks correctly
        }

        /// <summary>
        /// Calls a method on the specified nodes.
        /// </summary>
        protected virtual async ValueTask CallInternalAsync(
            OperationContext context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            bool sync,
            CancellationToken cancellationToken = default)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();

            for (int ii = 0; ii < methodsToCall.Count; ii++)
            {
                CallMethodRequest methodToCall = methodsToCall[ii];

                // skip items that have already been processed.
                if (methodToCall.Processed)
                {
                    continue;
                }

                MethodState method = null;

                // check for valid handle.
                NodeHandle handle = GetManagerHandle(
                    systemContext,
                    methodToCall.ObjectId,
                    operationCache);

                if (handle == null)
                {
                    continue;
                }

                lock (Lock)
                {
                    // owned by this node manager.
                    methodToCall.Processed = true;

                    // validate the source node.
                    NodeState source = ValidateNode(systemContext, handle, operationCache);

                    if (source == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        continue;
                    }

                    // find the method.
                    method = source.FindMethod(systemContext, methodToCall.MethodId);

                    if (method == null)
                    {
                        // check for loose coupling.
                        if (source.ReferenceExists(
                            ReferenceTypeIds.HasComponent,
                            false,
                            methodToCall.MethodId))
                        {
                            method = FindPredefinedNode<MethodState>(
                                methodToCall.MethodId);
                        }

                        if (method == null)
                        {
                            errors[ii] = StatusCodes.BadMethodInvalid;
                            continue;
                        }
                    }

                    // validate the role permissions for method to be executed,
                    // it may be a different MethodState that does not have the MethodId specified in the method call
                    errors[ii] = ValidateRolePermissions(
                        context,
                        method.NodeId,
                        PermissionType.Call);

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }
                }

                // call the method.
                CallMethodResult result = results[ii] = new CallMethodResult();

                if (sync)
                {
                    errors[ii] = Call(systemContext, methodToCall, method, result);
                }
                else
                {
                    errors[ii] = await CallAsync(
                        systemContext,
                        methodToCall,
                        method,
                        result,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Calls a method on an object.
        /// </summary>
        protected virtual async ValueTask<ServiceResult> CallInternalAsync(
            ISystemContext context,
            CallMethodRequest methodToCall,
            MethodState method,
            CallMethodResult result,
            bool sync,
            CancellationToken cancellationToken = default)
        {
            var systemContext = context as ServerSystemContext;
            var argumentErrors = new List<ServiceResult>();
            var outputArguments = new VariantCollection();

            ServiceResult callResult;

            if (sync)
            {
                callResult = method.Call(
                    context,
                    methodToCall.ObjectId,
                    methodToCall.InputArguments,
                    argumentErrors,
                    outputArguments);
            }
            else
            {
                callResult = await method.CallAsync(
                   context,
                   methodToCall.ObjectId,
                   methodToCall.InputArguments,
                   argumentErrors,
                   outputArguments,
                   cancellationToken
                ).ConfigureAwait(false);
            }

            if (ServiceResult.IsBad(callResult))
            {
                return callResult;
            }

            // check for argument errors.
            bool argumentsValid = true;

            for (int jj = 0; jj < argumentErrors.Count; jj++)
            {
                ServiceResult argumentError = argumentErrors[jj];

                if (argumentError != null)
                {
                    result.InputArgumentResults.Add(argumentError.StatusCode);

                    if (ServiceResult.IsBad(argumentError))
                    {
                        argumentsValid = false;
                    }

                    // only fill in diagnostic info if it is requested.
                    if (systemContext.OperationContext != null &&
                        (systemContext.OperationContext.DiagnosticsMask &
                            DiagnosticsMasks.OperationAll) != 0)
                    {
                        if (ServiceResult.IsBad(argumentError))
                        {
                            result.InputArgumentDiagnosticInfos.Add(
                                new DiagnosticInfo(
                                    argumentError,
                                    systemContext.OperationContext.DiagnosticsMask,
                                    false,
                                    systemContext.OperationContext.StringTable,
                                    m_logger));
                        }
                        else
                        {
                            result.InputArgumentDiagnosticInfos.Add(null);
                        }
                    }
                }
            }

            // check for validation errors.
            if (!argumentsValid)
            {
                result.StatusCode = StatusCodes.BadInvalidArgument;
                return result.StatusCode;
            }

            // Per OPC UA Part 4, Section 5.12: InputArgumentResults must be empty when StatusCode is Good.
            // Clear diagnostics and argument results if there are no errors.
            result.InputArgumentDiagnosticInfos.Clear();
            result.InputArgumentResults.Clear();

            // return output arguments.
            result.OutputArguments = outputArguments;

            // return the actual result of the original call
            return callResult;
        }

        /// <summary>
        /// Asynchronously calls a method on an object.
        /// </summary>
        protected virtual ValueTask<ServiceResult> CallAsync(
            ISystemContext context,
            CallMethodRequest methodToCall,
            MethodState method,
            CallMethodResult result,
            CancellationToken cancellationToken = default)
        {
            return CallInternalAsync(
                context,
                methodToCall,
                method,
                result,
                sync: false,
                cancellationToken);
        }

        /// <summary>
        /// Calls a method on an object.
        /// </summary>
        protected virtual ServiceResult Call(
            ISystemContext context,
            CallMethodRequest methodToCall,
            MethodState method,
            CallMethodResult result)
        {
#pragma warning disable CA2012 // Use ValueTasks correctly
            ValueTask<ServiceResult> syncResult = CallInternalAsync(
                context,
                methodToCall,
                method,
                result,
                sync: true);
#pragma warning restore CA2012 // Use ValueTasks correctly
            return syncResult.Result;
        }

        /// <summary>
        /// Subscribes or unsubscribes to events produced by the specified source.
        /// </summary>
        /// <remarks>
        /// This method is called when a event subscription is created or deletes. The node manager
        /// must  start/stop reporting events for the specified object and all objects below it in
        /// the notifier hierarchy.
        /// </remarks>
        public virtual ServiceResult SubscribeToEvents(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            // check for valid handle.
            NodeHandle handle = IsHandleInNamespace(sourceId);

            if (handle == null)
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            lock (Lock)
            {
                // check for valid node.
                NodeState source = ValidateNode(systemContext, handle, null);

                if (source == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                // subscribe to events.
                return SubscribeToEvents(systemContext, source, monitoredItem, unsubscribe);
            }
        }

        /// <summary>
        /// Subscribes or unsubscribes to events produced by all event sources.
        /// </summary>
        /// <remarks>
        /// This method is called when a event subscription is created or deleted. The node
        /// manager must start/stop reporting events for all objects that it manages.
        /// </remarks>
        public virtual ServiceResult SubscribeToAllEvents(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            lock (Lock)
            {
                // A client has subscribed to the Server object which means all events produced
                // by this manager must be reported. This is done by incrementing the monitoring
                // reference count for all root notifiers.
                if (RootNotifiers != null)
                {
                    for (int ii = 0; ii < RootNotifiers.Count; ii++)
                    {
                        SubscribeToEvents(
                            systemContext,
                            RootNotifiers[ii],
                            monitoredItem,
                            unsubscribe);
                    }
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Adds a root notifier.
        /// </summary>
        /// <param name="notifier">The notifier.</param>
        /// <remarks>
        /// A root notifier is a notifier owned by the NodeManager that is not the target of a
        /// HasNotifier reference. These nodes need to be linked directly to the Server object.
        /// </remarks>
        protected virtual void AddRootNotifier(NodeState notifier)
        {
            lock (Lock)
            {
                RootNotifiers ??= [];

                bool mustAdd = true;

                for (int ii = 0; ii < RootNotifiers.Count; ii++)
                {
                    if (ReferenceEquals(notifier, RootNotifiers[ii]))
                    {
                        return;
                    }

                    if (RootNotifiers[ii].NodeId == notifier.NodeId)
                    {
                        RootNotifiers[ii] = notifier;
                        mustAdd = false;
                        break;
                    }
                }

                if (mustAdd)
                {
                    RootNotifiers.Add(notifier);
                }

                // need to prevent recursion with the server object.
                if (notifier.NodeId != ObjectIds.Server)
                {
                    notifier.OnReportEvent = OnReportEvent;

                    if (!notifier.ReferenceExists(
                        ReferenceTypeIds.HasNotifier,
                        true,
                        ObjectIds.Server))
                    {
                        notifier.AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
                    }
                }

                // subscribe to existing events.
                if (Server.EventManager != null)
                {
                    IList<IEventMonitoredItem> monitoredItems = Server.EventManager
                        .GetMonitoredItems();

                    for (int ii = 0; ii < monitoredItems.Count; ii++)
                    {
                        if (monitoredItems[ii].MonitoringAllEvents)
                        {
                            SubscribeToEvents(SystemContext, notifier, monitoredItems[ii], true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Removes a root notifier previously added with AddRootNotifier.
        /// </summary>
        /// <param name="notifier">The notifier.</param>
        protected virtual void RemoveRootNotifier(NodeState notifier)
        {
            lock (Lock)
            {
                if (RootNotifiers != null)
                {
                    for (int ii = 0; ii < RootNotifiers.Count; ii++)
                    {
                        if (ReferenceEquals(notifier, RootNotifiers[ii]))
                        {
                            notifier.OnReportEvent = null;
                            notifier.RemoveReference(
                                ReferenceTypeIds.HasNotifier,
                                true,
                                ObjectIds.Server);
                            RootNotifiers.RemoveAt(ii);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reports an event for a root notifier.
        /// </summary>
        protected virtual void OnReportEvent(
            ISystemContext context,
            NodeState node,
            IFilterTarget e)
        {
            Server.ReportEvent(context, e);
        }

        /// <summary>
        /// Subscribes to events.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="source">The source.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="unsubscribe">if set to <c>true</c> [unsubscribe].</param>
        /// <returns>Any error code.</returns>
        protected virtual ServiceResult SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            (MonitoredNode2 monitoredNode, ServiceResult serviceResult) = m_monitoredItemManager
                .SubscribeToEvents(
                    context,
                    source,
                    monitoredItem,
                    unsubscribe);

            // This call recursively updates a reference count all nodes in the notifier
            // hierarchy below the area. Sources with a reference count of 0 do not have
            // any active subscriptions so they do not need to report events.
            source.SetAreEventsMonitored(context, !unsubscribe, true);

            // signal update.
            OnSubscribeToEvents(context, monitoredNode, unsubscribe);

            // all done.
            return serviceResult;
        }

        /// <summary>
        /// Called after subscribing/unsubscribing to events.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredNode">The monitored node.</param>
        /// <param name="unsubscribe">if set to <c>true</c> unsubscribing.</param>
        protected virtual void OnSubscribeToEvents(
            ServerSystemContext context,
            MonitoredNode2 monitoredNode,
            bool unsubscribe)
        {
            // defined by the sub-class
        }

        /// <summary>
        /// Tells the node manager to refresh any conditions associated with the specified monitored items.
        /// </summary>
        /// <remarks>
        /// This method is called when the condition refresh method is called for a subscription.
        /// The node manager must create a refresh event for each condition monitored by the subscription.
        /// </remarks>
        public virtual ServiceResult ConditionRefresh(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // the IEventMonitoredItem should always be MonitoredItems since they are created by the MasterNodeManager.
                IEventMonitoredItem monitoredItem = monitoredItems[ii];

                if (monitoredItem == null)
                {
                    continue;
                }

                var events = new List<IFilterTarget>();
                var nodesToRefresh = new List<NodeState>();

                lock (Lock)
                {
                    // check for server subscription.
                    if (monitoredItem.NodeId == ObjectIds.Server)
                    {
                        if (RootNotifiers != null)
                        {
                            nodesToRefresh.AddRange(RootNotifiers);
                        }
                    }
                    else
                    {
                        // check if monitored Item is managed by this node manager
                        if (!MonitoredItems.ContainsKey(monitoredItem.Id))
                        {
                            continue;
                        }

                        // get the refresh events.
                        nodesToRefresh.Add(((NodeHandle)monitoredItem.ManagerHandle).Node);
                    }
                }

                // block and wait for the refresh.
                for (int jj = 0; jj < nodesToRefresh.Count; jj++)
                {
                    nodesToRefresh[jj].ConditionRefresh(systemContext, events, true);
                }

                // queue the events.
                for (int jj = 0; jj < events.Count; jj++)
                {
                    // verify if the event can be received by the current monitored item
                    ServiceResult result = ValidateEventRolePermissions(monitoredItem, events[jj]);
                    if (ServiceResult.IsBad(result))
                    {
                        continue;
                    }
                    monitoredItem.QueueEvent(events[jj]);
                }
            }

            // all done.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Restore a set of monitored items after a restart.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="itemsToRestore"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual void RestoreMonitoredItems(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity)
        {
            if (itemsToRestore == null)
            {
                throw new ArgumentNullException(nameof(itemsToRestore));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (Server.IsRunning)
            {
                throw new InvalidOperationException(
                    "Subscription restore can only occur on startup");
            }

            ServerSystemContext systemContext = SystemContext.Copy();
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            var nodesToValidate = new List<NodeHandle>();

            for (int ii = 0; ii < itemsToRestore.Count; ii++)
            {
                IStoredMonitoredItem itemToCreate = itemsToRestore[ii];

                // skip items that have already been processed.
                if (itemToCreate.IsRestored)
                {
                    continue;
                }

                // check for valid handle.
                NodeHandle handle = GetManagerHandle(
                    systemContext,
                    itemToCreate.NodeId,
                    operationCache);

                if (handle == null)
                {
                    continue;
                }

                // owned by this node manager.
                itemToCreate.IsRestored = true;

                handle.Index = ii;
                nodesToValidate.Add(handle);
            }

            // check for nothing to do.
            if (nodesToValidate.Count == 0)
            {
                return;
            }

            // validates the nodes (reads values from the underlying data source if required).
            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];

                bool success = false;
                IMonitoredItem monitoredItem = null;

                lock (Lock)
                {
                    // validate node.
                    NodeState source = ValidateNode(systemContext, handle, operationCache);

                    if (source == null)
                    {
                        continue;
                    }

                    IStoredMonitoredItem itemToCreate = itemsToRestore[handle.Index];

                    // create monitored item.
                    success = RestoreMonitoredItem(
                        systemContext,
                        handle,
                        itemToCreate,
                        savedOwnerIdentity,
                        out monitoredItem);
                }

                if (!success)
                {
                    continue;
                }

                // save the monitored item.
                monitoredItems[handle.Index] = monitoredItem;
            }

            lock (Lock)
            {
                m_monitoredItemManager.ApplyChanges();
            }

            // do any post processing.
            OnCreateMonitoredItemsComplete(systemContext, monitoredItems);
        }

        /// <summary>
        /// Restore a single monitored Item after a restart
        /// </summary>
        /// <returns>true if sucesfully restored</returns>
        protected virtual bool RestoreMonitoredItem(
            ServerSystemContext context,
            NodeHandle handle,
            IStoredMonitoredItem storedMonitoredItem,
            IUserIdentity savedOwnerIdentity,
            out IMonitoredItem monitoredItem)
        {
            monitoredItem = null;

            // validate attribute.
            if (!Attributes.IsValid(handle.Node.NodeClass, storedMonitoredItem.AttributeId))
            {
                return false;
            }

            // put an upper limit on queue size.
            storedMonitoredItem.QueueSize = SubscriptionManager.CalculateRevisedQueueSize(
                storedMonitoredItem.IsDurable,
                storedMonitoredItem.QueueSize,
                MaxQueueSize,
                MaxDurableQueueSize);

            bool success = m_monitoredItemManager.RestoreMonitoredItem(
                Server,
                this,
                context,
                handle,
                storedMonitoredItem,
                savedOwnerIdentity,
                AddNodeToComponentCache,
                out ISampledDataChangeMonitoredItem restoredItem);

            monitoredItem = restoredItem;

            // report change.
            OnMonitoredItemCreated(context, handle, restoredItem);

            return true;
        }

        /// <summary>
        /// Creates a new set of monitored items for a set of variables.
        /// </summary>
        /// <remarks>
        /// This method only handles data change subscriptions. Event subscriptions are created by the SDK.
        /// </remarks>
        public virtual void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            var nodesToValidate = new List<NodeHandle>();
            var createdItems = new List<IMonitoredItem>();

            for (int ii = 0; ii < itemsToCreate.Count; ii++)
            {
                MonitoredItemCreateRequest itemToCreate = itemsToCreate[ii];

                // skip items that have already been processed.
                if (itemToCreate.Processed)
                {
                    continue;
                }

                ReadValueId itemToMonitor = itemToCreate.ItemToMonitor;

                // check for valid handle.
                NodeHandle handle = GetManagerHandle(
                    systemContext,
                    itemToMonitor.NodeId,
                    operationCache);

                if (handle == null)
                {
                    continue;
                }

                // owned by this node manager.
                itemToCreate.Processed = true;

                // must validate node in a separate operation.
                errors[ii] = StatusCodes.BadNodeIdUnknown;

                handle.Index = ii;
                nodesToValidate.Add(handle);
            }

            // check for nothing to do.
            if (nodesToValidate.Count == 0)
            {
                return;
            }

            // validates the nodes (reads values from the underlying data source if required).
            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];

                MonitoringFilterResult filterResult = null;
                IMonitoredItem monitoredItem = null;

                lock (Lock)
                {
                    // validate node.
                    NodeState source = ValidateNode(systemContext, handle, operationCache);

                    if (source == null)
                    {
                        continue;
                    }

                    MonitoredItemCreateRequest itemToCreate = itemsToCreate[handle.Index];

                    // create monitored item.
                    errors[handle.Index] = CreateMonitoredItem(
                        systemContext,
                        handle,
                        subscriptionId,
                        publishingInterval,
                        context.DiagnosticsMask,
                        timestampsToReturn,
                        itemToCreate,
                        createDurable,
                        monitoredItemIdFactory,
                        out filterResult,
                        out monitoredItem);
                }

                // save any filter error details.
                filterErrors[handle.Index] = filterResult;

                if (ServiceResult.IsBad(errors[handle.Index]))
                {
                    continue;
                }

                // save the monitored item.
                monitoredItems[handle.Index] = monitoredItem;
                createdItems.Add(monitoredItem);
            }

            lock (Lock)
            {
                m_monitoredItemManager.ApplyChanges();
            }

            // do any post processing.
            OnCreateMonitoredItemsComplete(systemContext, createdItems);
        }

        /// <summary>
        /// Called when a batch of monitored items has been created.
        /// </summary>
        protected virtual void OnCreateMonitoredItemsComplete(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems)
        {
            // defined by the sub-class
        }

        /// <summary>
        /// Creates a new set of monitored items for a set of variables.
        /// </summary>
        /// <remarks>
        /// This method only handles data change subscriptions. Event subscriptions are created by the SDK.
        /// </remarks>
        protected virtual ServiceResult CreateMonitoredItem(
            ServerSystemContext context,
            NodeHandle handle,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest itemToCreate,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemId,
            out MonitoringFilterResult filterResult,
            out IMonitoredItem monitoredItem)
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

            // determine the sampling interval.
            double samplingInterval = itemToCreate.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = publishingInterval;
            }

            // ensure minimum sampling interval is not exceeded.
            if (itemToCreate.ItemToMonitor.AttributeId == Attributes.Value &&
                handle.Node is BaseVariableState variable &&
                samplingInterval < variable.MinimumSamplingInterval)
            {
                samplingInterval = variable.MinimumSamplingInterval;
            }

            // put a large upper limit on sampling.
            if (samplingInterval == double.MaxValue)
            {
                samplingInterval = 365 * 24 * 3600 * 1000.0;
            }

            // put an upper limit on queue size.
            uint revisedQueueSize = SubscriptionManager.CalculateRevisedQueueSize(
                createDurable,
                itemToCreate.RequestedParameters.QueueSize,
                MaxQueueSize,
                MaxDurableQueueSize);

            // validate the monitoring filter.

            ServiceResult error = ValidateMonitoringFilter(
                context,
                handle,
                itemToCreate.ItemToMonitor.AttributeId,
                samplingInterval,
                revisedQueueSize,
                parameters.Filter,
                out MonitoringFilter filterToUse,
                out Range euRange,
                out filterResult);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            ISampledDataChangeMonitoredItem dataChangeMonitoredItem =
                m_monitoredItemManager.CreateMonitoredItem(
                    Server,
                    this,
                    context,
                    handle,
                    subscriptionId,
                    publishingInterval,
                    diagnosticsMasks,
                    timestampsToReturn,
                    itemToCreate,
                    euRange,
                    filterToUse,
                    samplingInterval,
                    revisedQueueSize,
                    createDurable,
                    monitoredItemId,
                    AddNodeToComponentCache);

            monitoredItem = dataChangeMonitoredItem;

            // report the initial value.
            error = ReadInitialValue(context, handle, dataChangeMonitoredItem);
            if (ServiceResult.IsBad(error))
            {
                if (error.StatusCode == StatusCodes.BadAttributeIdInvalid ||
                    error.StatusCode == StatusCodes.BadDataEncodingInvalid ||
                    error.StatusCode == StatusCodes.BadDataEncodingUnsupported)
                {
                    return error;
                }
                error = StatusCodes.Good;
            }

            // report change.
            OnMonitoredItemCreated(context, handle, dataChangeMonitoredItem);

            return error;
        }

        /// <summary>
        /// Reads the initial value for a monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The item handle.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected virtual ServiceResult ReadInitialValue(
            ISystemContext context,
            NodeHandle handle,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            var initialValue = new DataValue
            {
                Value = null,
                ServerTimestamp = DateTime.UtcNow,
                SourceTimestamp = DateTime.MinValue,
                StatusCode = StatusCodes.BadWaitingForInitialData
            };

            ServiceResult error = handle.Node.ReadAttribute(
                context,
                monitoredItem.AttributeId,
                monitoredItem.IndexRange,
                monitoredItem.DataEncoding,
                initialValue);

            monitoredItem.QueueValue(initialValue, error, true);

            return error;
        }

        /// <summary>
        /// Called after creating a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected virtual void OnMonitoredItemCreated(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            // overridden by the sub-class.
        }

        /// <summary>
        /// Validates Role permissions for the specified NodeId
        /// </summary>
        public virtual ServiceResult ValidateRolePermissions(
            OperationContext operationContext,
            NodeId nodeId,
            PermissionType requestedPermission)
        {
            if (requestedPermission == PermissionType.None)
            {
                // no permission is required hence the validation passes.
                return StatusCodes.Good;
            }

            object nodeHandle = Server.NodeManager
                .GetManagerHandle(nodeId, out INodeManager nodeManager);

            if (nodeHandle == null || nodeManager == null)
            {
                // ignore unknown nodes.
                return StatusCodes.Good;
            }

            NodeMetadata nodeMetadata = nodeManager.GetNodeMetadata(
                operationContext,
                nodeHandle,
                BrowseResultMask.All);

            return MasterNodeManager.ValidateRolePermissions(
                operationContext,
                nodeMetadata,
                requestedPermission);
        }

        /// <summary>
        /// Validates if the specified event monitored item has enough permissions to receive the specified event
        /// </summary>
        public ServiceResult ValidateEventRolePermissions(
            IEventMonitoredItem monitoredItem,
            IFilterTarget filterTarget)
        {
            NodeId eventTypeId = default;
            NodeId sourceNodeId = default;
            var baseEventState = filterTarget as BaseEventState;

            if (baseEventState == null && filterTarget is InstanceStateSnapshot snapshot)
            {
                // try to get the event instance from snapshot object
                baseEventState = snapshot.Handle as BaseEventState;
            }

            if (baseEventState != null)
            {
                eventTypeId = baseEventState.EventType?.Value ?? default;
                sourceNodeId = baseEventState.SourceNode?.Value ?? default;
            }

            var operationContext = new OperationContext(monitoredItem);

            // validate the event type id permissions as specified
            ServiceResult result = ValidateRolePermissions(
                operationContext,
                eventTypeId,
                PermissionType.ReceiveEvents);

            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            // validate the source node id permissions as specified
            return ValidateRolePermissions(
                operationContext,
                sourceNodeId,
                PermissionType.ReceiveEvents);
        }

        /// <summary>
        /// Validates the monitoring filter specified by the client.
        /// </summary>
        protected virtual StatusCode ValidateMonitoringFilter(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            out MonitoringFilter filterToUse,
            out Range range,
            out MonitoringFilterResult result)
        {
            range = null;
            filterToUse = null;
            result = null;

            // nothing to do if the filter is not specified.
            if (ExtensionObject.IsNull(filter))
            {
                return StatusCodes.Good;
            }

            // extension objects wrap any data structure. must check that the client provided the correct structure.

            if (ExtensionObject.ToEncodeable(filter) is not DataChangeFilter deadbandFilter)
            {
                if (ExtensionObject.ToEncodeable(filter) is not AggregateFilter aggregateFilter ||
                    attributeId != Attributes.Value)
                {
                    return StatusCodes.BadFilterNotAllowed;
                }

                if (!Server.AggregateManager.IsSupported(aggregateFilter.AggregateType))
                {
                    return StatusCodes.BadAggregateNotSupported;
                }

                var revisedFilter = new ServerAggregateFilter
                {
                    AggregateType = aggregateFilter.AggregateType,
                    StartTime = aggregateFilter.StartTime,
                    ProcessingInterval = aggregateFilter.ProcessingInterval,
                    AggregateConfiguration = aggregateFilter.AggregateConfiguration,
                    Stepped = false
                };

                StatusCode error = ReviseAggregateFilter(
                    context,
                    handle,
                    samplingInterval,
                    queueSize,
                    revisedFilter);

                if (StatusCode.IsBad(error))
                {
                    return error;
                }

                var aggregateFilterResult = new AggregateFilterResult
                {
                    RevisedProcessingInterval = aggregateFilter.ProcessingInterval,
                    RevisedStartTime = aggregateFilter.StartTime,
                    RevisedAggregateConfiguration = aggregateFilter.AggregateConfiguration
                };

                filterToUse = revisedFilter;
                result = aggregateFilterResult;
                return StatusCodes.Good;
            }

            // deadband filters only allowed for variable values.
            if (attributeId != Attributes.Value)
            {
                return StatusCodes.BadFilterNotAllowed;
            }

            if (handle.Node is not BaseVariableState variable)
            {
                return StatusCodes.BadFilterNotAllowed;
            }

            // check for status filter.
            if (deadbandFilter.DeadbandType == (uint)DeadbandType.None)
            {
                filterToUse = deadbandFilter;
                return StatusCodes.Good;
            }

            // deadband filters can only be used for numeric values.
            if (!Server.TypeTree.IsTypeOf(variable.DataType, DataTypeIds.Number))
            {
                return StatusCodes.BadFilterNotAllowed;
            }

            // nothing more to do for absolute filters.
            if (deadbandFilter.DeadbandType == (uint)DeadbandType.Absolute)
            {
                filterToUse = deadbandFilter;
                return StatusCodes.Good;
            }

            // need to look up the EU range if a percent filter is requested.
            if (deadbandFilter.DeadbandType == (uint)DeadbandType.Percent)
            {
                if (handle.Node.FindChild(
                    context,
                    QualifiedName.From(BrowseNames.EURange)) is not PropertyState property)
                {
                    return StatusCodes.BadMonitoredItemFilterUnsupported;
                }

                if (!property.Value.TryGetStructure(out range))
                {
                    return StatusCodes.BadMonitoredItemFilterUnsupported;
                }

                filterToUse = deadbandFilter;

                return StatusCodes.Good;
            }

            // no other type of filter supported.
            return StatusCodes.BadFilterNotAllowed;
        }

        /// <summary>
        /// Revises an aggregate filter (may require knowledge of the variable being used).
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle.</param>
        /// <param name="samplingInterval">The sampling interval for the monitored item.</param>
        /// <param name="queueSize">The queue size for the monitored item.</param>
        /// <param name="filterToUse">The filter to revise.</param>
        /// <returns>Good if the </returns>
        protected virtual StatusCode ReviseAggregateFilter(
            ServerSystemContext context,
            NodeHandle handle,
            double samplingInterval,
            uint queueSize,
            ServerAggregateFilter filterToUse)
        {
            if (filterToUse.ProcessingInterval < samplingInterval)
            {
                filterToUse.ProcessingInterval = samplingInterval;
            }

            if (filterToUse.ProcessingInterval < Server.AggregateManager.MinimumProcessingInterval)
            {
                filterToUse.ProcessingInterval = Server.AggregateManager.MinimumProcessingInterval;
            }

            DateTime earliestStartTime = DateTime.UtcNow.AddMilliseconds(
                -(queueSize - 1) * filterToUse.ProcessingInterval);

            if (earliestStartTime > filterToUse.StartTime)
            {
                filterToUse.StartTime = earliestStartTime;
            }

            if (filterToUse.AggregateConfiguration.UseServerCapabilitiesDefaults)
            {
                filterToUse.AggregateConfiguration = Server.AggregateManager
                    .GetDefaultConfiguration(default);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Modifies the parameters for a set of monitored items.
        /// </summary>
        public virtual void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            var nodesInNamespace = new List<(int, NodeHandle)>(monitoredItems.Count);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                MonitoredItemModifyRequest itemToModify = itemsToModify[ii];

                // skip items that have already been processed.
                if (itemToModify.Processed || monitoredItems[ii] == null)
                {
                    continue;
                }

                // check handle.
                NodeHandle handle = IsHandleInNamespace(monitoredItems[ii].ManagerHandle);

                if (handle == null)
                {
                    continue;
                }

                nodesInNamespace.Add((ii, handle));
            }

            if (nodesInNamespace.Count == 0)
            {
                return;
            }

            var modifiedItems = new List<IMonitoredItem>();

            lock (Lock)
            {
                foreach ((int, NodeHandle) nodeInNamespace in nodesInNamespace)
                {
                    int ii = nodeInNamespace.Item1;
                    NodeHandle handle = nodeInNamespace.Item2;
                    MonitoredItemModifyRequest itemToModify = itemsToModify[ii];

                    // owned by this node manager.
                    itemToModify.Processed = true;

                    // modify the monitored item.
                    errors[ii] = ModifyMonitoredItem(
                        systemContext,
                        context.DiagnosticsMask,
                        timestampsToReturn,
                        monitoredItems[ii],
                        itemToModify,
                        handle,
                        out MonitoringFilterResult filterResult);

                    // save any filter error details.
                    filterErrors[ii] = filterResult;

                    // save the modified item.
                    if (ServiceResult.IsGood(errors[ii]))
                    {
                        modifiedItems.Add(monitoredItems[ii]);
                    }
                }
            }

            lock (Lock)
            {
                m_monitoredItemManager.ApplyChanges();
            }

            // do any post processing.
            OnModifyMonitoredItemsComplete(systemContext, modifiedItems);
        }

        /// <summary>
        /// Called when a batch of monitored items has been modified.
        /// </summary>
        protected virtual void OnModifyMonitoredItemsComplete(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems)
        {
            // defined by the sub-class
        }

        /// <summary>
        /// Modifies the parameters for a monitored item.
        /// </summary>
        protected virtual ServiceResult ModifyMonitoredItem(
            ServerSystemContext context,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            IMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify,
            NodeHandle handle,
            out MonitoringFilterResult filterResult)
        {
            // check for valid monitored item.
            var datachangeItem = monitoredItem as ISampledDataChangeMonitoredItem;

            // validate parameters.
            MonitoringParameters parameters = itemToModify.RequestedParameters;

            double previousSamplingInterval = datachangeItem.SamplingInterval;

            // check if the variable needs to be sampled.
            double samplingInterval = itemToModify.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = previousSamplingInterval;
            }

            // ensure minimum sampling interval is not exceeded.
            if (datachangeItem.AttributeId == Attributes.Value &&
                handle.Node is BaseVariableState variable &&
                samplingInterval < variable.MinimumSamplingInterval)
            {
                samplingInterval = variable.MinimumSamplingInterval;
            }

            // put a large upper limit on sampling.
            if (samplingInterval == double.MaxValue)
            {
                samplingInterval = 365 * 24 * 3600 * 1000.0;
            }

            // put an upper limit on queue size.
            uint revisedQueueSize = SubscriptionManager.CalculateRevisedQueueSize(
                monitoredItem.IsDurable,
                itemToModify.RequestedParameters.QueueSize,
                MaxQueueSize,
                MaxDurableQueueSize);

            // validate the monitoring filter.

            ServiceResult error = ValidateMonitoringFilter(
                context,
                handle,
                datachangeItem.AttributeId,
                samplingInterval,
                revisedQueueSize,
                parameters.Filter,
                out MonitoringFilter filterToUse,
                out Range euRange,
                out filterResult);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // modify the monitored item parameters.
            error = m_monitoredItemManager.ModifyMonitoredItem(
                context,
                diagnosticsMasks,
                timestampsToReturn,
                filterToUse,
                euRange,
                samplingInterval,
                revisedQueueSize,
                datachangeItem,
                itemToModify);

            // report change.
            if (ServiceResult.IsGood(error))
            {
                OnMonitoredItemModified(context, handle, datachangeItem);
            }

            return error;
        }

        /// <summary>
        /// Called after modifying a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected virtual void OnMonitoredItemModified(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            // overridden by the sub-class.
        }

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        public virtual void DeleteMonitoredItems(
            OperationContext context,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            var nodesInNamespace = new List<(int, NodeHandle)>(monitoredItems.Count);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // skip items that have already been processed.
                if (processedItems[ii] || monitoredItems[ii] == null)
                {
                    continue;
                }

                // check handle.
                NodeHandle handle = IsHandleInNamespace(monitoredItems[ii].ManagerHandle);

                if (handle == null)
                {
                    continue;
                }

                nodesInNamespace.Add((ii, handle));
            }

            if (nodesInNamespace.Count == 0)
            {
                return;
            }

            var deletedItems = new List<IMonitoredItem>();

            lock (Lock)
            {
                foreach ((int, NodeHandle) nodeInNamespace in nodesInNamespace)
                {
                    int ii = nodeInNamespace.Item1;
                    NodeHandle handle = nodeInNamespace.Item2;

                    // owned by this node manager.
                    processedItems[ii] = true;

                    errors[ii] = DeleteMonitoredItem(systemContext, monitoredItems[ii], handle);

                    // save the modified item.
                    if (ServiceResult.IsGood(errors[ii]))
                    {
                        deletedItems.Add(monitoredItems[ii]);
                        RemoveNodeFromComponentCache(systemContext, handle);
                    }
                }
            }

            lock (Lock)
            {
                m_monitoredItemManager.ApplyChanges();
            }

            // do any post processing.
            OnDeleteMonitoredItemsComplete(systemContext, deletedItems);
        }

        /// <summary>
        /// Called when a batch of monitored items has been modified.
        /// </summary>
        protected virtual void OnDeleteMonitoredItemsComplete(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems)
        {
            // defined by the sub-class
        }

        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        protected virtual ServiceResult DeleteMonitoredItem(
            ServerSystemContext context,
            IMonitoredItem monitoredItem,
            NodeHandle handle)
        {
            var sampledDataChangeMonitoredItem = monitoredItem as ISampledDataChangeMonitoredItem;

            StatusCode statusCode = m_monitoredItemManager.DeleteMonitoredItem(
                context,
                sampledDataChangeMonitoredItem,
                handle);

            // report change.
            OnMonitoredItemDeleted(context, handle, sampledDataChangeMonitoredItem);

            return statusCode;
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected virtual void OnMonitoredItemDeleted(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            // overridden by the sub-class.
        }

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="sendInitialValues">Whether the subscription should send initial values after transfer.</param>
        /// <param name="monitoredItems">The set of monitoring items to update.</param>
        /// <param name="processedItems">The list of bool with items that were already processed.</param>
        /// <param name="errors">Any errors.</param>
        public virtual void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            var transferredItems = new List<IMonitoredItem>();
            lock (Lock)
            {
                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }

                    // check handle.
                    NodeHandle handle = IsHandleInNamespace(monitoredItems[ii].ManagerHandle);
                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    processedItems[ii] = true;
                    transferredItems.Add(monitoredItems[ii]);
                    if (sendInitialValues)
                    {
                        monitoredItems[ii].SetupResendDataTrigger();
                    }
                    errors[ii] = StatusCodes.Good;
                }
            }

            // do any post processing.
            OnMonitoredItemsTransferred(systemContext, transferredItems);
        }

        /// <summary>
        /// Called after transfer of MonitoredItems.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredItems">The transferred monitored items.</param>
        protected virtual void OnMonitoredItemsTransferred(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems)
        {
            // defined by the sub-class
        }

        /// <summary>
        /// Changes the monitoring mode for a set of monitored items.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoringMode">The monitoring mode.</param>
        /// <param name="monitoredItems">The set of monitoring items to update.</param>
        /// <param name="processedItems">Flags indicating which items have been processed.</param>
        /// <param name="errors">Any errors.</param>
        public virtual void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            var nodesInNamespace = new List<(int, NodeHandle)>(monitoredItems.Count);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // skip items that have already been processed.
                if (processedItems[ii] || monitoredItems[ii] == null)
                {
                    continue;
                }

                // check handle.
                NodeHandle handle = IsHandleInNamespace(monitoredItems[ii].ManagerHandle);

                if (handle == null)
                {
                    continue;
                }

                nodesInNamespace.Add((ii, handle));
            }

            if (nodesInNamespace.Count == 0)
            {
                return;
            }

            var changedItems = new List<IMonitoredItem>();

            lock (Lock)
            {
                foreach ((int, NodeHandle) nodeInNamespace in nodesInNamespace)
                {
                    int ii = nodeInNamespace.Item1;
                    NodeHandle handle = nodeInNamespace.Item2;

                    // indicate whether it was processed or not.
                    processedItems[ii] = true;

                    // update monitoring mode.
                    errors[ii] = SetMonitoringMode(
                        systemContext,
                        monitoredItems[ii],
                        monitoringMode,
                        handle);

                    // save the modified item.
                    if (ServiceResult.IsGood(errors[ii]))
                    {
                        changedItems.Add(monitoredItems[ii]);
                    }
                }
            }

            lock (Lock)
            {
                m_monitoredItemManager.ApplyChanges();
            }

            // do any post processing.
            OnSetMonitoringModeComplete(systemContext, changedItems);
        }

        /// <summary>
        /// Called when a batch of monitored items has their monitoring mode changed.
        /// </summary>
        protected virtual void OnSetMonitoringModeComplete(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems)
        {
            // defined by the sub-class
        }

        /// <summary>
        /// Changes the monitoring mode for an item.
        /// </summary>
        protected virtual ServiceResult SetMonitoringMode(
            ServerSystemContext context,
            IMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle)
        {
            var sampledDataChangeMonitoredItem = monitoredItem as ISampledDataChangeMonitoredItem;

            (ServiceResult result, MonitoringMode? previousMode) = m_monitoredItemManager
                .SetMonitoringMode(
                    context,
                    sampledDataChangeMonitoredItem,
                    monitoringMode,
                    handle);

            // report change.
            if (ServiceResult.IsGood(result) && previousMode != monitoringMode)
            {
                OnMonitoringModeChanged(
                    context,
                    handle,
                    sampledDataChangeMonitoredItem,
                    (MonitoringMode)previousMode,
                    monitoringMode);
            }

            return result;
        }

        /// <summary>
        /// Called after changing the MonitoringMode for a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="previousMode">The previous monitoring mode.</param>
        /// <param name="monitoringMode">The current monitoring mode.</param>
        protected virtual void OnMonitoringModeChanged(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode)
        {
            // overridden by the sub-class.
        }

        /// <summary>
        /// Called when a session is closed.
        /// </summary>
        public virtual void SessionClosing(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions)
        {
        }

        /// <summary>
        /// Returns true if a node is in a view.
        /// </summary>
        public virtual bool IsNodeInView(OperationContext context, NodeId viewId, object nodeHandle)
        {
            if (nodeHandle is not NodeHandle handle)
            {
                return false;
            }

            if (handle.Node != null)
            {
                return IsNodeInView(context, viewId, handle.Node);
            }

            return false;
        }

        /// <summary>
        /// Returns the metadata containing the AccessRestrictions, RolePermissions and UserRolePermissions for the node.
        /// Returns null if the node does not exist.
        /// </summary>
        /// <remarks>
        /// This method validates any placeholder handle.
        /// </remarks>
        public virtual NodeMetadata GetPermissionMetadata(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, ArrayOf<Variant>> uniqueNodesServiceAttributesCache,
            bool permissionsOnly)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            // check for valid handle.
            NodeHandle handle = IsHandleInNamespace(targetHandle);

            if (handle == null)
            {
                return null;
            }

            lock (Lock)
            {
                // validate node.
                NodeState target = ValidateNode(systemContext, handle, null);

                if (target == null)
                {
                    return null;
                }

                ArrayOf<Variant> values = default;

                // construct the meta-data object.
                var metadata = new NodeMetadata(target, target.NodeId);

                // Treat the case of calls originating from the optimized services that use the cache (Read, Browse and Call services)
                if (uniqueNodesServiceAttributesCache != null)
                {
                    NodeId key = handle.NodeId;
                    if (uniqueNodesServiceAttributesCache.ContainsKey(key))
                    {
                        if (uniqueNodesServiceAttributesCache[key].Count == 0)
                        {
                            values = ReadAndCacheValidationAttributes(
                                uniqueNodesServiceAttributesCache,
                                systemContext,
                                target,
                                key);
                        }
                        else
                        {
                            // Retrieve value from cache
                            values = uniqueNodesServiceAttributesCache[key];
                        }
                    }
                    else
                    {
                        values = ReadAndCacheValidationAttributes(
                            uniqueNodesServiceAttributesCache,
                            systemContext,
                            target,
                            key);
                    }

                    SetAccessAndRolePermissions(values, metadata);
                } // All other calls that do not use the cache
                else if (permissionsOnly)
                {
                    values = ReadValidationAttributes(systemContext, target);
                    SetAccessAndRolePermissions(values, metadata);
                }

                SetDefaultPermissions(systemContext, target, metadata);

                return metadata;
            }
        }

        /// <summary>
        /// Set the metadata default permission values for DefaultAccessRestrictions, DefaultRolePermissions and DefaultUserRolePermissions
        /// </summary>
        private void SetDefaultPermissions(
            ServerSystemContext systemContext,
            NodeState target,
            NodeMetadata metadata)
        {
            // check if NamespaceMetadata is defined for NamespaceUri
            string namespaceUri = Server.NamespaceUris.GetString(target.NodeId.NamespaceIndex);
            NamespaceMetadataState namespaceMetadataState =
                Server.NodeManager.ConfigurationNodeManager.GetNamespaceMetadataState(namespaceUri);

            if (namespaceMetadataState != null)
            {
                ArrayOf<Variant> namespaceMetadataValues;

                if (namespaceMetadataState.DefaultAccessRestrictions != null)
                {
                    // get DefaultAccessRestrictions for Namespace
                    namespaceMetadataValues = namespaceMetadataState.DefaultAccessRestrictions
                        .ReadAttributes(systemContext, Attributes.Value);

                    if (namespaceMetadataValues[0].TryGet(out AccessRestrictionType accessRestrictions))
                    {
                        metadata.DefaultAccessRestrictions = accessRestrictions;
                    }
                }

                if (namespaceMetadataState.DefaultRolePermissions != null)
                {
                    // get DefaultRolePermissions for Namespace
                    namespaceMetadataValues = namespaceMetadataState.DefaultRolePermissions
                        .ReadAttributes(systemContext, Attributes.Value);

                    if (namespaceMetadataValues[0].TryGetStructure(
                            out ArrayOf<RolePermissionType> rolePermissions))
                    {
                        metadata.DefaultRolePermissions = rolePermissions;
                    }
                }

                if (namespaceMetadataState.DefaultUserRolePermissions != null)
                {
                    // get DefaultUserRolePermissions for Namespace
                    namespaceMetadataValues = namespaceMetadataState.DefaultUserRolePermissions
                        .ReadAttributes(systemContext, Attributes.Value);

                    if (namespaceMetadataValues[0].TryGetStructure(
                            out ArrayOf<RolePermissionType> rolePermissions))
                    {
                        metadata.DefaultUserRolePermissions = rolePermissions;
                    }
                }
            }
        }

        /// <summary>
        /// Stores a reference count for entries in the component cache.
        /// </summary>
        private class CacheEntry
        {
            public int RefCount;
            public NodeState Entry;
        }

        /// <summary>
        /// Looks up a component in cache.
        /// </summary>
        protected NodeState LookupNodeInComponentCache(ISystemContext context, NodeHandle handle)
        {
            lock (Lock)
            {
                if (m_componentCache == null)
                {
                    return null;
                }

                CacheEntry entry = null;

                if (!string.IsNullOrEmpty(handle.ComponentPath))
                {
                    if (m_componentCache.TryGetValue(handle.RootId, out entry))
                    {
                        return entry.Entry.FindChildBySymbolicName(context, handle.ComponentPath);
                    }
                }
                else if (m_componentCache.TryGetValue(handle.NodeId, out entry))
                {
                    return entry.Entry;
                }

                return null;
            }
        }

        /// <summary>
        /// Removes a reference to a component in thecache.
        /// </summary>
        protected void RemoveNodeFromComponentCache(ISystemContext context, NodeHandle handle)
        {
            lock (Lock)
            {
                if (handle == null)
                {
                    return;
                }

                if (m_componentCache != null)
                {
                    NodeId nodeId = handle.NodeId;

                    if (!string.IsNullOrEmpty(handle.ComponentPath))
                    {
                        nodeId = handle.RootId;
                    }

                    if (m_componentCache.TryGetValue(nodeId, out CacheEntry entry))
                    {
                        entry.RefCount--;

                        if (entry.RefCount == 0)
                        {
                            m_componentCache.Remove(nodeId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a node to the component cache.
        /// </summary>
        protected NodeState AddNodeToComponentCache(
            ISystemContext context,
            NodeHandle handle,
            NodeState node)
        {
            lock (Lock)
            {
                if (handle == null)
                {
                    return node;
                }

                m_componentCache ??= [];

                // check if a component is actually specified.
                if (!string.IsNullOrEmpty(handle.ComponentPath))
                {
                    if (m_componentCache.TryGetValue(handle.RootId, out CacheEntry entry))
                    {
                        entry.RefCount++;

                        if (!string.IsNullOrEmpty(handle.ComponentPath))
                        {
                            return entry.Entry
                                .FindChildBySymbolicName(context, handle.ComponentPath);
                        }

                        return entry.Entry;
                    }

                    NodeState root = node.GetHierarchyRoot();

                    if (root != null)
                    {
                        entry = new CacheEntry { RefCount = 1, Entry = root };
                        m_componentCache.Add(handle.RootId, entry);
                    }
                }
                // simply add the node to the cache.
                else
                {
                    if (m_componentCache.TryGetValue(handle.NodeId, out CacheEntry entry))
                    {
                        entry.RefCount++;
                        return entry.Entry;
                    }

                    entry = new CacheEntry { RefCount = 1, Entry = node };
                    m_componentCache.Add(handle.NodeId, entry);
                }

                return node;
            }
        }

        private IReadOnlyList<string> m_namespaceUris;
        private ushort[] m_namespaceIndexes;
        private NodeIdDictionary<CacheEntry> m_componentCache;

        /// <summary>
        /// A logger to use
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        protected ILogger m_logger { get; }
#pragma warning restore IDE1006 // Naming Styles

        /// <summary>
        /// the monitored item manager of the NodeManager
        /// </summary>
        protected IMonitoredItemManager m_monitoredItemManager;
    }
}
