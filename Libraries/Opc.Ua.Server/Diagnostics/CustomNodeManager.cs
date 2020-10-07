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
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

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
    public class CustomNodeManager2 : INodeManager2, INodeIdFactory, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            params string[] namespaceUris)
        :
            this(server, (ApplicationConfiguration)null, namespaceUris)
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected CustomNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            params string[] namespaceUris)
        {
            // set defaults.
            m_maxQueueSize = 1000;

            if (configuration != null)
            {
                if (configuration.ServerConfiguration != null)
                {
                    m_maxQueueSize = (uint)configuration.ServerConfiguration.MaxNotificationQueueSize;
                }
            }

            // save a reference to the UA server instance that owns the node manager.
            m_server = server;

            // all operations require information about the system 
            m_systemContext = m_server.DefaultSystemContext.Copy();

            // the node id factory assigns new node ids to new nodes. 
            // the strategy used by a NodeManager depends on what kind of information it provides.
            m_systemContext.NodeIdFactory = this;

            // create the table of namespaces that are used by the NodeManager.
            m_namespaceUris = namespaceUris;

            // add the uris to the server's namespace table and cache the indexes.
            if (namespaceUris != null)
            {
                m_namespaceIndexes = new ushort[m_namespaceUris.Length];

                for (int ii = 0; ii < m_namespaceUris.Length; ii++)
                {
                    m_namespaceIndexes[ii] = m_server.NamespaceUris.GetIndexOrAppend(m_namespaceUris[ii]);
                }
            }

            // create the table of monitored items.
            // these are items created by clients when they subscribe to data or events.
            m_monitoredItems = new Dictionary<uint, IDataChangeMonitoredItem>();

            // create the table of monitored nodes.
            // these are created by the node manager whenever a client subscribe to an attribute of the node.
            m_monitoredNodes = new Dictionary<NodeId, MonitoredNode2>();
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
                    if (m_predefinedNodes != null)
                    {
                        foreach (NodeState node in m_predefinedNodes.Values)
                        {
                            Utils.SilentDispose(node);
                        }

                        m_predefinedNodes.Clear();
                    }
                }
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
        public virtual NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Acquires the lock on the node manager.
        /// </summary>
        public object Lock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// Gets the server that the node manager belongs to.
        /// </summary>
        public IServerInternal Server
        {
            get { return m_server; }
        }

        /// <summary>
        /// The default context to use.
        /// </summary>
        public ServerSystemContext SystemContext
        {
            get { return m_systemContext; }
        }

        /// <summary>
        /// Gets the default index for the node manager's namespace.
        /// </summary>
        public ushort NamespaceIndex
        {
            get { return m_namespaceIndexes[0]; }
        }

        /// <summary>
        /// Gets the namespace indexes owned by the node manager.
        /// </summary>
        /// <value>The namespace indexes.</value>
        public ushort[] NamespaceIndexes
        {
            get { return m_namespaceIndexes; }
        }

        /// <summary>
        /// Gets or sets the maximum size of a monitored item queue.
        /// </summary>
        /// <value>The maximum size of a monitored item queue.</value>
        public uint MaxQueueSize
        {
            get { return m_maxQueueSize; }
            set { m_maxQueueSize = value; }
        }

        /// <summary>
        /// The root for the alias assigned to the node manager.
        /// </summary>
        public string AliasRoot
        {
            get { return m_aliasRoot; }
            set { m_aliasRoot = value; }
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// The predefined nodes managed by the node manager.
        /// </summary>
        protected NodeIdDictionary<NodeState> PredefinedNodes
        {
            get { return m_predefinedNodes; }
        }

        /// <summary>
        /// The root notifiers for the node manager.
        /// </summary>
        protected List<NodeState> RootNotifiers
        {
            get { return m_rootNotifiers; }
        }

        /// <summary>
        /// Gets the table of monitored items.
        /// </summary>
        protected Dictionary<uint, IDataChangeMonitoredItem> MonitoredItems
        {
            get { return m_monitoredItems; }
        }

        /// <summary>
        /// Gets the table of nodes being monitored.
        /// </summary>
        protected Dictionary<NodeId, MonitoredNode2> MonitoredNodes
        {
            get { return m_monitoredNodes; }
        }

        /// <summary>
        /// Sets the namespaces supported by the NodeManager.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        protected void SetNamespaces(params string[] namespaceUris)
        {
            // create the table of namespaces that are used by the NodeManager.
            m_namespaceUris = namespaceUris;

            // add the uris to the server's namespace table and cache the indexes.
            m_namespaceIndexes = new ushort[m_namespaceUris.Length];

            for (int ii = 0; ii < m_namespaceUris.Length; ii++)
            {
                m_namespaceIndexes[ii] = m_server.NamespaceUris.GetIndexOrAppend(m_namespaceUris[ii]);
            }
        }

        /// <summary>
        /// Sets the namespace indexes supported by the NodeManager.
        /// </summary>
        protected void SetNamespaceIndexes(ushort[] namespaceIndexes)
        {
            m_namespaceIndexes = namespaceIndexes;
            m_namespaceUris = new string[namespaceIndexes.Length];

            for (int ii = 0; ii < namespaceIndexes.Length; ii++)
            {
                m_namespaceUris[ii] = m_server.NamespaceUris.GetString(namespaceIndexes[ii]);
            }
        }

        /// <summary>
        /// Returns true if the namespace for the node id is one of the namespaces managed by the node manager.
        /// </summary>
        /// <param name="nodeId">The node id to check.</param>
        /// <returns>True if the namespace is one of the nodes.</returns>
        protected virtual bool IsNodeIdInNamespace(NodeId nodeId)
        {
            // nulls are never a valid node.
            if (NodeId.IsNull(nodeId))
            {
                return false;
            }

            // quickly exclude nodes that not in the namespace.
            for (int ii = 0; ii < m_namespaceIndexes.Length; ii++)
            {
                if (nodeId.NamespaceIndex == m_namespaceIndexes[ii])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the node if the handle refers to a node managed by this manager.
        /// </summary>
        /// <param name="managerHandle">The handle to check.</param>
        /// <returns>Non-null if the handle belongs to the node manager.</returns>
        protected virtual NodeHandle IsHandleInNamespace(object managerHandle)
        {
            NodeHandle source = managerHandle as NodeHandle;

            if (source == null)
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
            lock (Lock)
            {
                if (PredefinedNodes == null)
                {
                    return null;
                }

                NodeState node = null;

                if (!PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    return null;
                }

                return node;
            }
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
        public NodeId CreateNode(
            ServerSystemContext context,
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            BaseInstanceState instance)
        {
            ServerSystemContext contextToUse = (ServerSystemContext)m_systemContext.Copy(context);

            lock (Lock)
            {
                if (m_predefinedNodes == null)
                {
                    m_predefinedNodes = new NodeIdDictionary<NodeState>();
                }

                instance.ReferenceTypeId = referenceTypeId;

                NodeState parent = null;

                if (parentId != null)
                {
                    if (!m_predefinedNodes.TryGetValue(parentId, out parent))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNodeIdUnknown,
                            "Cannot find parent with id: {0}",
                            parentId);
                    }

                    parent.AddChild(instance);
                }

                instance.Create(contextToUse, null, browseName, null, true);
                AddPredefinedNode(contextToUse, instance);

                return instance.NodeId;
            }
        }

        /// <summary>
        /// Deletes a node and all of its children.
        /// </summary>
        public bool DeleteNode(
            ServerSystemContext context,
            NodeId nodeId)
        {
            ServerSystemContext contextToUse = m_systemContext.Copy(context);

            bool found = false;
            List<LocalReference> referencesToRemove = new List<LocalReference>();

            lock (Lock)
            {
                if (m_predefinedNodes == null)
                {
                    return false;
                }

                NodeState node = null;

                if (PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    RemovePredefinedNode(contextToUse, node, referencesToRemove);
                    found = true;
                }

                RemoveRootNotifier(node);
            }

            // must release the lock before removing cross references to other node managers.
            if (referencesToRemove.Count > 0)
            {
                Server.NodeManager.RemoveReferences(referencesToRemove);
            }

            return found;
        }

        /// <summary>
        /// Searches the node id in all node managers 
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public NodeState FindNodeInAddressSpace(NodeId nodeId)
        {
            if (nodeId == null)
            {
                return null;
            }
            // search node id in all node managers
            foreach (INodeManager nodeManager in Server.NodeManager.NodeManagers)
            {
                NodeHandle handle = nodeManager.GetManagerHandle(nodeId) as NodeHandle;
                if (handle == null)
                {
                    continue;
                }
                return handle.Node;
            }
            return null;
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Returns the namespaces used by the node manager.
        /// </summary>
        /// <remarks>
        /// All NodeIds exposed by the node manager must be qualified by a namespace URI. This property
        /// returns the URIs used by the node manager. In this example all NodeIds use a single URI.
        /// </remarks>
        public virtual IEnumerable<string> NamespaceUris
        {
            get
            {
                return m_namespaceUris;
            }

            protected set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                List<string> namespaceUris = new List<string>(value);
                SetNamespaces(namespaceUris.ToArray());
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
        public virtual void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            LoadPredefinedNodes(m_systemContext, externalReferences);
        }

        #region CreateAddressSpace Support Functions
        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        public virtual void LoadPredefinedNodes(
            ISystemContext context,
            Assembly assembly,
            string resourcePath,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (m_predefinedNodes == null)
            {
                m_predefinedNodes = new NodeIdDictionary<NodeState>();
            }

            // load the predefined nodes from an XML document.
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromResource(context, resourcePath, assembly, true);

            // add the predefined nodes to the node manager.
            for (int ii = 0; ii < predefinedNodes.Count; ii++)
            {
                AddPredefinedNode(context, predefinedNodes[ii]);
            }

            // ensure the reverse refernces exist.
            AddReverseReferences(externalReferences);
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected virtual NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            return new NodeStateCollection();
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
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

            // ensure the reverse refernces exist.
            AddReverseReferences(externalReferences);
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected virtual NodeState AddBehaviourToPredefinedNode(ISystemContext context, NodeState predefinedNode)
        {
            BaseObjectState passiveNode = predefinedNode as BaseObjectState;

            if (passiveNode == null)
            {
                return predefinedNode;
            }

            return predefinedNode;
        }

        /// <summary>
        /// Recursively indexes the node and its children.
        /// </summary>
        protected virtual void AddPredefinedNode(ISystemContext context, NodeState node)
        {
            if (m_predefinedNodes == null)
            {
                m_predefinedNodes = new NodeIdDictionary<NodeState>();
            }

            NodeState activeNode = AddBehaviourToPredefinedNode(context, node);
            m_predefinedNodes[activeNode.NodeId] = activeNode;

            BaseTypeState type = activeNode as BaseTypeState;

            if (type != null)
            {
                AddTypesToTypeTree(type);
            }

            // update the root notifiers.
            if (m_rootNotifiers != null)
            {
                for (int ii = 0; ii < m_rootNotifiers.Count; ii++)
                {
                    if (m_rootNotifiers[ii].NodeId == activeNode.NodeId)
                    {
                        m_rootNotifiers[ii] = activeNode;

                        // need to prevent recursion with the server object.
                        if (activeNode.NodeId != ObjectIds.Server)
                        {
                            activeNode.OnReportEvent = OnReportEvent;

                            if (!activeNode.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server))
                            {
                                activeNode.AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
                            }
                        }

                        break;
                    }
                }
            }

            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
            if (m_predefinedNodes == null)
            {
                return;
            }

            m_predefinedNodes.Remove(node.NodeId);
            node.UpdateChangeMasks(NodeStateChangeMasks.Deleted);
            node.ClearChangeMasks(context, false);
            OnNodeRemoved(node);

            // remove from the parent.
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                instance.Parent.RemoveChild(instance);
            }

            // remove children.
            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
            BaseTypeState type = node as BaseTypeState;

            if (type != null)
            {
                m_server.TypeTree.Remove(type.NodeId);
            }

            // remove inverse references.
            List<IReference> references = new List<IReference>();
            node.GetReferences(context, references);

            for (int ii = 0; ii < references.Count; ii++)
            {
                IReference reference = references[ii];

                if (reference.TargetId.IsAbsolute)
                {
                    continue;
                }

                LocalReference referenceToRemove = new LocalReference(
                    (NodeId)reference.TargetId,
                    reference.ReferenceTypeId,
                    reference.IsInverse,
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
        protected virtual void AddReverseReferences(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (m_predefinedNodes == null)
            {
                return;
            }

            foreach (NodeState source in m_predefinedNodes.Values)
            {
                // assign a default value to any variable value.
                BaseVariableState variable = source as BaseVariableState;

                if (variable != null && variable.Value == null)
                {
                    variable.Value = TypeInfo.GetDefaultValue(variable.DataType, variable.ValueRank, Server.TypeTree);
                }

                IList<IReference> references = new List<IReference>();
                source.GetReferences(SystemContext, references);

                for (int ii = 0; ii < references.Count; ii++)
                {
                    IReference reference = references[ii];

                    // nothing to do with external nodes.
                    if (reference.TargetId == null || reference.TargetId.IsAbsolute)
                    {
                        continue;
                    }

                    // no need to add HasSubtype references since these are handled via the type table.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                    {
                        continue;
                    }

                    NodeId targetId = (NodeId)reference.TargetId;

                    // check for data type encoding references.
                    if (reference.IsInverse && reference.ReferenceTypeId == ReferenceTypeIds.HasEncoding)
                    {
                        Server.TypeTree.AddEncoding(targetId, source.NodeId);
                    }

                    // add inverse reference to internal targets.
                    NodeState target = null;

                    if (m_predefinedNodes.TryGetValue(targetId, out target))
                    {
                        if (!target.ReferenceExists(reference.ReferenceTypeId, !reference.IsInverse, source.NodeId))
                        {
                            target.AddReference(reference.ReferenceTypeId, !reference.IsInverse, source.NodeId);
                        }

                        continue;
                    }

                    // check for inverse references to external notifiers.
                    if (reference.IsInverse && reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier)
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
            IList<IReference> referencesToAdd = null;

            if (!externalReferences.TryGetValue(sourceId, out referencesToAdd))
            {
                externalReferences[sourceId] = referencesToAdd = new List<IReference>();
            }

            // add reserve reference from external node.
            ReferenceNode referenceToAdd = new ReferenceNode();

            referenceToAdd.ReferenceTypeId = referenceTypeId;
            referenceToAdd.IsInverse = isInverse;
            referenceToAdd.TargetId = targetId;

            referencesToAdd.Add(referenceToAdd);
        }

        /// <summary>
        /// Recursively adds the types to the type tree.
        /// </summary>
        protected void AddTypesToTypeTree(BaseTypeState type)
        {
            if (!NodeId.IsNull(type.SuperTypeId))
            {
                if (!Server.TypeTree.IsKnown(type.SuperTypeId))
                {
                    AddTypesToTypeTree(type.SuperTypeId);
                }
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
            NodeState node = null;

            if (!PredefinedNodes.TryGetValue(typeId, out node))
            {
                return;
            }

            BaseTypeState type = node as BaseTypeState;

            if (type == null)
            {
                return;
            }

            AddTypesToTypeTree(type);
        }

        /// <summary>
        /// Finds the specified and checks if it is of the expected type. 
        /// </summary>
        /// <returns>Returns null if not found or not of the correct type.</returns>
        public NodeState FindPredefinedNode(NodeId nodeId, Type expectedType)
        {
            if (nodeId == null)
            {
                return null;
            }

            NodeState node = null;

            if (!PredefinedNodes.TryGetValue(nodeId, out node))
            {
                return null;
            }

            if (expectedType != null)
            {
                if (!expectedType.IsInstanceOfType(node))
                {
                    return null;
                }
            }

            return node;
        }
        #endregion

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public virtual void DeleteAddressSpace()
        {
            lock (m_lock)
            {
                if (m_predefinedNodes != null)
                {
                    foreach (NodeState node in m_predefinedNodes.Values)
                    {
                        Utils.SilentDispose(node);
                    }

                    m_predefinedNodes.Clear();
                }
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
            lock (Lock)
            {
                return GetManagerHandle(m_systemContext, nodeId, null);
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected virtual NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            if (!IsNodeIdInNamespace(nodeId))
            {
                return null;
            }

            if (m_predefinedNodes != null)
            {
                NodeState node = null;

                if (m_predefinedNodes.TryGetValue(nodeId, out node))
                {
                    NodeHandle handle = new NodeHandle();

                    handle.NodeId = nodeId;
                    handle.Node = node;
                    handle.Validated = true;

                    return handle;
                }
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
                    NodeHandle source = GetManagerHandle(m_systemContext, current.Key, null);

                    // only support external references to nodes that are stored in memory.
                    if (source == null || !source.Validated || source.Node == null)
                    {
                        continue;
                    }

                    // add reference to external target.
                    foreach (IReference reference in current.Value)
                    {
                        if (!source.Node.ReferenceExists(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId))
                        {
                            source.Node.AddReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This method is used to delete bi-directional references to nodes from other node managers.
        /// </summary>
        public virtual ServiceResult DeleteReference(
            object         sourceHandle, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            ExpandedNodeId targetId, 
            bool           deleteBiDirectional)
        {
            lock (Lock)
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

                // only support references to Source Areas.
                source.Node.RemoveReference(referenceTypeId, isInverse, targetId);

                if (deleteBiDirectional)
                {
                    // check if the target is also managed by this node manager.
                    if (!targetId.IsAbsolute)
                    {
                        NodeHandle target = GetManagerHandle(m_systemContext, (NodeId)targetId, null);

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
            object           targetHandle, 
            BrowseResultMask resultMask)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);

            lock (Lock)
            {
                // check for valid handle.
                NodeHandle handle = IsHandleInNamespace(targetHandle);

                if (handle == null)
                {
                    return null;
                }

                // validate node.
                NodeState target = ValidateNode(systemContext, handle, null);

                if (target == null)
                {
                    return null;
                }

                // read the attributes.
                List<object> values = target.ReadAttributes(
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
                NodeMetadata metadata = new NodeMetadata(target, target.NodeId);

                metadata.NodeClass = target.NodeClass;
                metadata.BrowseName = target.BrowseName;
                metadata.DisplayName = target.DisplayName;

                if (values[0] != null && values[1] != null)
                {
                    metadata.WriteMask = (AttributeWriteMask)(((uint)values[0]) & ((uint)values[1]));
                }

                metadata.DataType = (NodeId)values[2];

                if (values[3] != null)
                {
                    metadata.ValueRank = (int)values[3];
                }

                metadata.ArrayDimensions = (IList<uint>)values[4];

                if (values[5] != null && values[6] != null)
                {
                    metadata.AccessLevel = (byte)(((byte)values[5]) & ((byte)values[6]));
                }

                if (values[7] != null)
                {
                    metadata.EventNotifier = (byte)values[7];
                }

                if (values[8] != null && values[9] != null)
                {
                    metadata.Executable = (((bool)values[8]) && ((bool)values[9]));
                }

                if (values[10] != null)
                {
                    metadata.AccessRestrictions = (AccessRestrictionType)Enum.ToObject(typeof(AccessRestrictionType), values[10]);
                }

                if (values[11] != null)
                {
                    metadata.RolePermissions = new RolePermissionTypeCollection(ExtensionObject.ToList<RolePermissionType>(values[11]));
                }

                if (values[12] != null)
                {
                    metadata.UserRolePermissions = new RolePermissionTypeCollection(ExtensionObject.ToList<RolePermissionType>(values[12]));
                }

                // check if NamespaceMetadata is defined for NamespaceUri
                string namespaceUri = Server.NamespaceUris.GetString(target.NodeId.NamespaceIndex);
                NamespaceMetadataState namespaceMetadataState = Server.NodeManager.ConfigurationNodeManager.GetNamespaceMetadataState(namespaceUri);

                if (namespaceMetadataState != null)
                {
                    List<object> namespaceMetadataValues;

                    if (namespaceMetadataState.DefaultAccessRestrictions != null)
                    {
                        // get DefaultAccessRestrictions for Namespace
                        namespaceMetadataValues = namespaceMetadataState.DefaultAccessRestrictions.ReadAttributes(systemContext, Attributes.Value);

                        if (namespaceMetadataValues[0] != null)
                        {
                            metadata.DefaultAccessRestrictions = (AccessRestrictionType)Enum.ToObject(typeof(AccessRestrictionType), namespaceMetadataValues[0]);
                        }
                    }

                    if (namespaceMetadataState.DefaultRolePermissions != null)
                    {
                        // get DefaultRolePermissions for Namespace
                        namespaceMetadataValues = namespaceMetadataState.DefaultRolePermissions.ReadAttributes(systemContext, Attributes.Value);

                        if (namespaceMetadataValues[0] != null)
                        {
                            metadata.DefaultRolePermissions = new RolePermissionTypeCollection(ExtensionObject.ToList<RolePermissionType>(namespaceMetadataValues[0]));
                        }
                    }

                    if (namespaceMetadataState.DefaultUserRolePermissions != null)
                    {
                        // get DefaultUserRolePermissions for Namespace
                        namespaceMetadataValues = namespaceMetadataState.DefaultUserRolePermissions.ReadAttributes(systemContext, Attributes.Value);

                        if (namespaceMetadataValues[0] != null)
                        {
                            metadata.DefaultUserRolePermissions = new RolePermissionTypeCollection(ExtensionObject.ToList<RolePermissionType>(namespaceMetadataValues[0]));
                        }
                    }
                }

                // get instance references.
                BaseInstanceState instance = target as BaseInstanceState;

                if (instance != null)
                {
                    metadata.TypeDefinition = instance.TypeDefinitionId;
                    metadata.ModellingRule = instance.ModellingRuleId;
                }

                // fill in the common attributes.
                return metadata;
            }
        }

        /// <summary>
        /// Browses the references from a node managed by the node manager.
        /// </summary>
        /// <remarks>
        /// The continuation point is created for every browse operation and contains the browse parameters.
        /// The node manager can store its state information in the Data and Index properties.
        /// </remarks>
        public virtual void Browse(
            OperationContext            context, 
            ref ContinuationPoint       continuationPoint, 
            IList<ReferenceDescription> references)
        {
            if (continuationPoint == null) throw new ArgumentNullException(nameof(continuationPoint));
            if (references == null) throw new ArgumentNullException(nameof(references));

            ServerSystemContext systemContext = m_systemContext.Copy(context);

            // check for valid view.
            ValidateViewDescription(systemContext, continuationPoint.View);

            INodeBrowser browser = null;

            lock (Lock)
            {
                // check for valid handle.
                NodeHandle handle = IsHandleInNamespace(continuationPoint.NodeToBrowse);

                if (handle == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                // validate node.
                NodeState source = ValidateNode(systemContext, handle, null);

                if (source == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

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
                        null,
                        null,
                        false);
                }
            }
            // prevent multiple access the browser object.
            lock (browser)
            {
                // apply filters to references.
                Dictionary<NodeId, NodeState> cache = new Dictionary<NodeId, NodeState>();

                for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
                {
                    // validate Browse permission
                    ServiceResult serviceResult = ValidateRolePermissions(context,
                        ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris),
                        PermissionType.Browse);
                    if (ServiceResult.IsBad(serviceResult))
                    {
                        // ignore reference
                        continue;
                    }
                    // create the type definition reference.        
                    ReferenceDescription description = GetReferenceDescription(systemContext, cache, reference, continuationPoint);

                    if (description == null)
                    {
                        continue;
                    }

                    // check if limit reached.
                    if (continuationPoint.MaxResultsToReturn != 0 && references.Count >= continuationPoint.MaxResultsToReturn)
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

        #region Browse Support Functions
        /// <summary>
        /// Validates the view description passed to a browse request (throws on error).
        /// </summary>
        protected virtual void ValidateViewDescription(ServerSystemContext context, ViewDescription view)
        {
            if (ViewDescription.IsDefault(view))
            {
                return;
            }

            ViewState node = (ViewState)FindPredefinedNode(view.ViewId, typeof(ViewState));

            if (node == null)
            {
                throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
            }

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
        protected virtual bool IsNodeInView(ServerSystemContext context, ContinuationPoint continuationPoint, NodeState node)
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
        protected virtual bool IsNodeInView(ServerSystemContext context, NodeId viewId, NodeState node)
        {
            ViewState view = (ViewState)FindPredefinedNode(viewId, typeof(ViewState));

            if (view != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if the reference is in the view.
        /// </summary>
        protected virtual bool IsReferenceInView(ServerSystemContext context, ContinuationPoint continuationPoint, IReference reference)
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
            ServerSystemContext systemContext = m_systemContext.Copy(context);

            // create the type definition reference.        
            ReferenceDescription description = new ReferenceDescription();

            description.NodeId = reference.TargetId;
            description.SetReferenceType(continuationPoint.ResultMask, reference.ReferenceTypeId, !reference.IsInverse);

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
            NodeStateReference referenceInfo = reference as NodeStateReference;

            if (referenceInfo != null)
            {
                target = referenceInfo.Target;
            }

            // check for internal reference.
            if (target == null)
            {
                NodeHandle handle = GetManagerHandle(context, (NodeId)reference.TargetId, null) as NodeHandle;

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
            if (continuationPoint.NodeClassMask != 0 && ((continuationPoint.NodeClassMask & (uint)target.NodeClass) == 0))
            {
                return null;
            }

            // check if target is in the view.
            if (!IsNodeInView(context, continuationPoint, target))
            {
                return null;
            }

            // look up the type definition.
            NodeId typeDefinition = null;

            BaseInstanceState instance = target as BaseInstanceState;

            if (instance != null)
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
        #endregion

        /// <summary>
        /// Returns the target of the specified browse path fragment(s).
        /// </summary>
        /// <remarks>
        /// If reference exists but the node manager does not know the browse name it must 
        /// return the NodeId as an unresolvedTargetIds. The caller will try to check the
        /// browse name. 
        /// </remarks>
        public virtual void TranslateBrowsePath(
            OperationContext      context, 
            object                sourceHandle, 
            RelativePathElement   relativePath, 
            IList<ExpandedNodeId> targetIds, 
            IList<NodeId>         unresolvedTargetIds)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();

            lock (Lock)
            {
                // check for valid handle.
                NodeHandle handle = IsHandleInNamespace(sourceHandle);

                if (handle == null)
                {
                    return;
                }

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
                    (relativePath.IsInverse) ? BrowseDirection.Inverse : BrowseDirection.Forward,
                    relativePath.TargetName,
                    null,
                    false);

                // check the browse names.
                try
                {
                    for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
                    {
                        // ignore unknown external references.
                        if (reference.TargetId.IsAbsolute)
                        {
                            continue;
                        }

                        NodeState target = null;

                        // check for local reference.
                        NodeStateReference referenceInfo = reference as NodeStateReference;

                        if (referenceInfo != null)
                        {
                            target = referenceInfo.Target;
                        }

                        if (target == null)
                        {
                            NodeId targetId = (NodeId)reference.TargetId;

                            // the target may be a reference to a node in another node manager.
                            if (!IsNodeIdInNamespace(targetId))
                            {
                                unresolvedTargetIds.Add((NodeId)reference.TargetId);
                                continue;
                            }

                            // look up the target manually.
                            NodeHandle targetHandle = GetManagerHandle(systemContext, targetId, operationCache);

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
                        if (target.BrowseName == relativePath.TargetName)
                        {
                            if (!targetIds.Contains(reference.TargetId))
                            {
                                targetIds.Add(reference.TargetId);
                            }
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
            OperationContext     context, 
            double               maxAge, 
            IList<ReadValueId>   nodesToRead, 
            IList<DataValue>     values, 
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<NodeHandle> nodesToValidate = new List<NodeHandle>();

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
                    NodeHandle handle = GetManagerHandle(systemContext, nodeToRead.NodeId, operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    // create an initial value.
                    DataValue value = values[ii] = new DataValue();

                    value.Value = null;
                    value.ServerTimestamp = DateTime.UtcNow;
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
                }

                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }
            }

            // validates the nodes (reads values from the underlying data source if required).
            Read(
                systemContext,
                nodesToRead,
                values,
                errors,
                nodesToValidate,
                operationCache);
        }

        #region Read Support Functions
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
            NodeState target = null;

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
                if (cache.TryGetValue(handle.NodeId, out target))
                {
                    return target;
                }

                // lookup root in local cache for request.
                if (!String.IsNullOrEmpty(handle.ComponentPath))
                {
                    if (cache.TryGetValue(rootId, out target))
                    {
                        target = target.FindChildBySymbolicName(context, handle.ComponentPath);

                        // component exists.
                        if (target != null)
                        {
                            return target;
                        }
                    }
                }
            }

            // lookup component in shared cache.
            target = LookupNodeInComponentCache(context, handle);

            if (target != null)
            {
                return target;
            }

            return null;
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
        #endregion

        /// <summary>
        /// Writes the value for the specified attributes.
        /// </summary>
        public virtual void Write(
            OperationContext     context, 
            IList<WriteValue>    nodesToWrite, 
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<NodeHandle> nodesToValidate = new List<NodeHandle>();

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
                    NodeHandle handle = GetManagerHandle(systemContext, nodeToWrite.NodeId, operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToWrite.Processed = true;

                    // index range is not supported.
                    if (nodeToWrite.AttributeId != Attributes.Value)
                    {
                        if (!String.IsNullOrEmpty(nodeToWrite.IndexRange))
                        {
                            errors[ii] = StatusCodes.BadWriteNotSupported;
                            continue;
                        }
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

                    // check if the node is AnalogItem and the value is outside the InstrumentRange.
                    AnalogItemState analogItemState = handle.Node as AnalogItemState;
                    if (analogItemState != null && analogItemState.InstrumentRange != null)
                    {
                        try
                        {
                            double newValue = System.Convert.ToDouble(nodeToWrite.Value.Value);

                            if (newValue > analogItemState.InstrumentRange.Value.High ||
                                newValue < analogItemState.InstrumentRange.Value.Low)
                            {
                                errors[ii] = StatusCodes.BadOutOfRange;
                                continue;
                            }
                        }
                        catch
                        {
                            //skip the InstrumentRange check if the transformation isn't possible.
                        }
                    }

                    Utils.TraceDebug("WRITE: Value={0} Range={1}", nodeToWrite.Value.WrappedValue, nodeToWrite.IndexRange);

                    PropertyState propertyState = handle.Node as PropertyState;
                    object previousPropertyValue = null;

                    if (propertyState != null)
                    {
                        ExtensionObject extension = propertyState.Value as ExtensionObject;
                        if (extension != null)
                        {
                            previousPropertyValue = extension.Body;
                        }
                        else
                        {
                            previousPropertyValue = propertyState.Value;
                        }
                    }

                    // write the attribute value.
                    errors[ii] = handle.Node.WriteAttribute(
                        systemContext,
                        nodeToWrite.AttributeId,
                        nodeToWrite.ParsedIndexRange,
                        nodeToWrite.Value);

                    if (!ServiceResult.IsGood(errors[ii]))
                    {
                        continue;
                    }

                    if (propertyState != null)
                    {
                        object propertyValue;
                        ExtensionObject extension = nodeToWrite.Value.Value as ExtensionObject;

                        if (extension != null)
                        {
                            propertyValue = extension.Body;
                        }
                        else
                        {
                            propertyValue = nodeToWrite.Value.Value;
                        }

                        CheckIfSemanticsHaveChanged(systemContext, propertyState, propertyValue, previousPropertyValue);
                    }

                    // updates to source finished - report changes to monitored items.
                    handle.Node.ClearChangeMasks(systemContext, false);
                }

                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }
            }

            // validates the nodes and writes the value to the underlying system.
            Write(
                systemContext,
                nodesToWrite,
                errors,
                nodesToValidate,
                operationCache);
        }

        private void CheckIfSemanticsHaveChanged(ServerSystemContext systemContext, PropertyState property, object newPropertyValue, object previousPropertyValue)
        {
            // check if the changed property is one that can trigger semantic changes
            string propertyName = property.BrowseName.Name;

            if (propertyName != BrowseNames.EURange &&
                propertyName != BrowseNames.InstrumentRange &&
                propertyName != BrowseNames.EngineeringUnits &&
                propertyName != BrowseNames.Title &&
                propertyName != BrowseNames.AxisDefinition &&
                propertyName != BrowseNames.FalseState &&
                propertyName != BrowseNames.TrueState &&
                propertyName != BrowseNames.EnumStrings &&
                propertyName != BrowseNames.XAxisDefinition &&
                propertyName != BrowseNames.YAxisDefinition &&
                propertyName != BrowseNames.ZAxisDefinition)
            {
                return;
            }

            //look for the Parent and its monitoring items
            foreach (var monitoredNode in m_monitoredNodes.Values)
            {
                var propertyState = monitoredNode.Node.FindChild(systemContext, property.BrowseName);

                if (propertyState != null && property != null && propertyState.NodeId == property.NodeId && !Utils.IsEqual(newPropertyValue, previousPropertyValue))
                {
                    foreach (var monitoredItem in monitoredNode.DataChangeMonitoredItems)
                    {
                        if (monitoredItem.AttributeId == Attributes.Value)
                        {
                            NodeState node = monitoredNode.Node;

                            if ((node is AnalogItemState && (propertyName == BrowseNames.EURange || propertyName == BrowseNames.EngineeringUnits)) ||
                                (node is TwoStateDiscreteState && (propertyName == BrowseNames.FalseState || propertyName == BrowseNames.TrueState)) ||
                                (node is MultiStateDiscreteState && (propertyName == BrowseNames.EnumStrings)) ||
                                (node is ArrayItemState && (propertyName == BrowseNames.InstrumentRange || propertyName == BrowseNames.EURange || propertyName == BrowseNames.EngineeringUnits || propertyName == BrowseNames.Title)) ||
                                ((node is YArrayItemState || node is XYArrayItemState) && (propertyName == BrowseNames.InstrumentRange || propertyName == BrowseNames.EURange || propertyName == BrowseNames.EngineeringUnits || propertyName == BrowseNames.Title || propertyName == BrowseNames.XAxisDefinition)) ||
                                (node is ImageItemState && (propertyName == BrowseNames.InstrumentRange || propertyName == BrowseNames.EURange || propertyName == BrowseNames.EngineeringUnits || propertyName == BrowseNames.Title || propertyName == BrowseNames.XAxisDefinition || propertyName == BrowseNames.YAxisDefinition)) ||
                                (node is CubeItemState && (propertyName == BrowseNames.InstrumentRange || propertyName == BrowseNames.EURange || propertyName == BrowseNames.EngineeringUnits || propertyName == BrowseNames.Title || propertyName == BrowseNames.XAxisDefinition || propertyName == BrowseNames.YAxisDefinition || propertyName == BrowseNames.ZAxisDefinition)) ||
                                (node is NDimensionArrayItemState && (propertyName == BrowseNames.InstrumentRange || propertyName == BrowseNames.EURange || propertyName == BrowseNames.EngineeringUnits || propertyName == BrowseNames.Title || propertyName == BrowseNames.AxisDefinition)))
                            {
                                monitoredItem.SetSemanticsChanged();

                                DataValue value = new DataValue();
                                value.ServerTimestamp = DateTime.UtcNow;

                                monitoredNode.Node.ReadAttribute(systemContext, Attributes.Value, monitoredItem.IndexRange, null, value);

                                monitoredItem.QueueValue(value, ServiceResult.Good, true);
                            }
                        }
                    }
                }
            }
        }

        #region Write Support Functions
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
        #endregion

        /// <summary>
        /// Reads the history for the specified nodes.
        /// </summary>
        public virtual void HistoryRead(
            OperationContext          context, 
            HistoryReadDetails        details, 
            TimestampsToReturn        timestampsToReturn, 
            bool                      releaseContinuationPoints, 
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult>  results, 
            IList<ServiceResult>      errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<NodeHandle> nodesToProcess = new List<NodeHandle>();

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
                    NodeHandle handle = GetManagerHandle(systemContext, nodeToRead.NodeId, operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    // create an initial result.
                    HistoryReadResult result = results[ii] = new HistoryReadResult();

                    result.HistoryData       = null;
                    result.ContinuationPoint = null;
                    result.StatusCode        = StatusCodes.Good;

                    // check if the node is a area in memory.
                    if (handle.Node == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;

                        // must validate node in a seperate operation
                        handle.Index = ii;
                        nodesToProcess.Add(handle);

                        continue;
                    }

                    errors[ii] = StatusCodes.BadHistoryOperationUnsupported;

                    // check for data history variable.
                    BaseVariableState variable = handle.Node as BaseVariableState;

                    if (variable != null)
                    {
                        if ((variable.AccessLevel & AccessLevels.HistoryRead) != 0)
                        {
                            handle.Index = ii;
                            nodesToProcess.Add(handle);
                            continue;
                        }
                    }

                    // check for event history object.
                    BaseObjectState notifier = handle.Node as BaseObjectState;

                    if (notifier != null)
                    {
                        if ((notifier.EventNotifier & EventNotifiers.HistoryRead) != 0)
                        {
                            handle.Index = ii;
                            nodesToProcess.Add(handle);
                            continue;
                        }
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

        #region HistoryRead Support Functions
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
            if (timestampsToReturn < TimestampsToReturn.Source || timestampsToReturn > TimestampsToReturn.Neither)
            {
                throw new ServiceResultException(StatusCodes.BadTimestampsToReturnInvalid);
            }

            // handle raw data request.
            ReadRawModifiedDetails readRawModifiedDetails = details as ReadRawModifiedDetails;

            if (readRawModifiedDetails != null)
            {
                // at least one must be provided.
                if (readRawModifiedDetails.StartTime == DateTime.MinValue && readRawModifiedDetails.EndTime == DateTime.MinValue)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                }

                // if one is null the num values must be provided.
                if (readRawModifiedDetails.StartTime == DateTime.MinValue || readRawModifiedDetails.EndTime == DateTime.MinValue)
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
            ReadProcessedDetails readProcessedDetails = details as ReadProcessedDetails;

            if (readProcessedDetails != null)
            {
                // check the list of aggregates.
                if (readProcessedDetails.AggregateType == null || readProcessedDetails.AggregateType.Count != nodesToRead.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadAggregateListMismatch);
                }

                // check start/end time.
                if (readProcessedDetails.StartTime == DateTime.MinValue || readProcessedDetails.EndTime == DateTime.MinValue)
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
            ReadAtTimeDetails readAtTimeDetails = details as ReadAtTimeDetails;

            if (readAtTimeDetails != null)
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
            ReadEventDetails readEventDetails = details as ReadEventDetails;

            if (readEventDetails != null)
            {
                // check start/end time and max values.
                if (readEventDetails.NumValuesPerNode == 0)
                {
                    if (readEventDetails.StartTime == DateTime.MinValue || readEventDetails.EndTime == DateTime.MinValue)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                    }
                }
                else
                {
                    if (readEventDetails.StartTime == DateTime.MinValue && readEventDetails.EndTime == DateTime.MinValue)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidTimestampArgument);
                    }
                }

                // validate the event filter.
                EventFilter.Result result = readEventDetails.Filter.Validate(new FilterContext(m_server.NamespaceUris, m_server.TypeTree, context));

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

                return;
            }
        }
        #endregion

        /// <summary>
        /// Updates the history for the specified nodes.
        /// </summary>
        public virtual void HistoryUpdate(
            OperationContext            context, 
            Type                        detailsType, 
            IList<HistoryUpdateDetails> nodesToUpdate, 
            IList<HistoryUpdateResult>  results,
            IList<ServiceResult>        errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<NodeHandle> nodesToProcess = new List<NodeHandle>();

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
                    NodeHandle handle = GetManagerHandle(systemContext, nodeToUpdate.NodeId, operationCache);

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

                        // must validate node in a seperate operation
                        handle.Index = ii;
                        nodesToProcess.Add(handle);
                        continue;
                    }

                    errors[ii] = StatusCodes.BadHistoryOperationUnsupported;

                    // check for data history variable.
                    BaseVariableState variable = handle.Node as BaseVariableState;

                    if (variable != null)
                    {
                        if ((variable.AccessLevel & AccessLevels.HistoryWrite) != 0)
                        {
                            handle.Index = ii;
                            nodesToProcess.Add(handle);
                            continue;
                        }
                    }

                    // check for event history object.
                    BaseObjectState notifier = handle.Node as BaseObjectState;

                    if (notifier != null)
                    {
                        if ((notifier.EventNotifier & EventNotifiers.HistoryWrite) != 0)
                        {
                            handle.Index = ii;
                            nodesToProcess.Add(handle);
                            continue;
                        }
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

        #region HistoryUpdate Support Functions
        /// <summary>
        /// Validates the nodes and updates the history.
        /// </summary>
        protected virtual void HistoryUpdate(
            ServerSystemContext context,
            Type                           detailsType, 
            IList<HistoryUpdateDetails>    nodesToUpdate, 
            IList<HistoryUpdateResult>     results,
            IList<ServiceResult>           errors,
            List<NodeHandle>               nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            // handle update data request.
            if (detailsType == typeof(UpdateDataDetails))
            {
                UpdateDataDetails[] details = new UpdateDataDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (UpdateDataDetails)nodesToUpdate[ii];
                }

                HistoryUpdateData(
                    context,
                    details,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle update structure data request.
            if (detailsType == typeof(UpdateStructureDataDetails))
            {
                UpdateStructureDataDetails[] details = new UpdateStructureDataDetails[nodesToUpdate.Count];

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
                UpdateEventDetails[] details = new UpdateEventDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (UpdateEventDetails)nodesToUpdate[ii];
                }

                HistoryUpdateEvents(
                    context,
                    details,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle delete raw data request.
            if (detailsType == typeof(DeleteRawModifiedDetails))
            {
                DeleteRawModifiedDetails[] details = new DeleteRawModifiedDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (DeleteRawModifiedDetails)nodesToUpdate[ii];
                }

                HistoryDeleteRawModified(
                    context,
                    details,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle delete at time request.
            if (detailsType == typeof(DeleteAtTimeDetails))
            {
                DeleteAtTimeDetails[] details = new DeleteAtTimeDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (DeleteAtTimeDetails)nodesToUpdate[ii];
                }

                HistoryDeleteAtTime(
                    context,
                    details,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
            }

            // handle delete at time request.
            if (detailsType == typeof(DeleteEventDetails))
            {
                DeleteEventDetails[] details = new DeleteEventDetails[nodesToUpdate.Count];

                for (int ii = 0; ii < details.Length; ii++)
                {
                    details[ii] = (DeleteEventDetails)nodesToUpdate[ii];
                }

                HistoryDeleteEvents(
                    context,
                    details,
                    results,
                    errors,
                    nodesToProcess,
                    cache);

                return;
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
        #endregion

        /// <summary>
        /// Calls a method on the specified nodes.
        /// </summary>
        public virtual void Call(
            OperationContext         context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult>  results,
            IList<ServiceResult>     errors)
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

                lock (Lock)
                {
                    // check for valid handle.
                    NodeHandle handle = GetManagerHandle(systemContext, methodToCall.ObjectId, operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

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
                        if (source.ReferenceExists(ReferenceTypeIds.HasComponent, false, methodToCall.MethodId))
                        {
                            method = (MethodState)FindPredefinedNode(methodToCall.MethodId, typeof(MethodState));
                        }

                        if (method == null)
                        {
                            errors[ii] = StatusCodes.BadMethodInvalid;
                            continue;
                        }
                    }
                }

                // call the method.
                CallMethodResult result = results[ii] = new CallMethodResult();

                errors[ii] = Call(
                    systemContext,
                    methodToCall,
                    method,
                    result);
            }
        }

        /// <summary>
        /// Calls a method on an object.
        /// </summary>
        protected virtual ServiceResult Call(
            ISystemContext    context,
            CallMethodRequest methodToCall,
            MethodState       method,
            CallMethodResult  result)
        {
            ServerSystemContext systemContext = context as ServerSystemContext;
            List<ServiceResult> argumentErrors = new List<ServiceResult>();
            VariantCollection outputArguments = new VariantCollection();

            ServiceResult error = method.Call(
                context,
                methodToCall.ObjectId,
                methodToCall.InputArguments,
                argumentErrors,
                outputArguments);

            if (ServiceResult.IsBad(error))
            {
                return error;
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
                }
                else
                {
                    result.InputArgumentResults.Add(StatusCodes.Good);
                }

                // only fill in diagnostic info if it is requested.
                if (systemContext.OperationContext != null)
                {
                    if ((systemContext.OperationContext.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        if (ServiceResult.IsBad(argumentError))
                        {
                            argumentsValid = false;
                            result.InputArgumentDiagnosticInfos.Add(new DiagnosticInfo(argumentError, systemContext.OperationContext.DiagnosticsMask, false, systemContext.OperationContext.StringTable));
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

            // do not return diagnostics if there are no errors.
            result.InputArgumentDiagnosticInfos.Clear();

            // return output arguments.
            result.OutputArguments = outputArguments;

            return ServiceResult.Good;
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
            OperationContext    context, 
            object              sourceId, 
            uint                subscriptionId, 
            IEventMonitoredItem monitoredItem, 
            bool                unsubscribe)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            lock (Lock)
            {
                // check for valid handle.
                NodeHandle handle = IsHandleInNamespace(sourceId);

                if (handle == null)
                {
                    return StatusCodes.BadNodeIdInvalid;
                }

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
            OperationContext    context, 
            uint                subscriptionId, 
            IEventMonitoredItem monitoredItem, 
            bool                unsubscribe)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            lock (Lock)
            {
                // A client has subscribed to the Server object which means all events produced
                // by this manager must be reported. This is done by incrementing the monitoring
                // reference count for all root notifiers.
                if (m_rootNotifiers != null)
                {
                    for (int ii = 0; ii < m_rootNotifiers.Count; ii++)
                    {
                        SubscribeToEvents(systemContext, m_rootNotifiers[ii], monitoredItem, unsubscribe);
                    }
                }

                return ServiceResult.Good;
            }
        }

        #region SubscribeToEvents Support Functions
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
            if (m_rootNotifiers == null)
            {
                m_rootNotifiers = new List<NodeState>();
            }

            bool mustAdd = true;

            for (int ii = 0; ii < m_rootNotifiers.Count; ii++)
            {
                if (Object.ReferenceEquals(notifier, m_rootNotifiers[ii]))
                {
                    return;
                }

                if (m_rootNotifiers[ii].NodeId == notifier.NodeId)
                {
                    m_rootNotifiers[ii] = notifier;
                    mustAdd = false;
                    break;
                }
            }

            if (mustAdd)
            {
                m_rootNotifiers.Add(notifier);
            }

            // need to prevent recursion with the server object.
            if (notifier.NodeId != ObjectIds.Server)
            {
                notifier.OnReportEvent = OnReportEvent;

                if (!notifier.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server))
                {
                    notifier.AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
                }
            }

            // subscribe to existing events.
            if (m_server.EventManager != null)
            {
                IList<IEventMonitoredItem> monitoredItems = m_server.EventManager.GetMonitoredItems();

                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    if (monitoredItems[ii].MonitoringAllEvents)
                    {
                        SubscribeToEvents(
                            SystemContext,
                            notifier,
                            monitoredItems[ii],
                            true);
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
            if (m_rootNotifiers != null)
            {
                for (int ii = 0; ii < m_rootNotifiers.Count; ii++)
                {
                    if (Object.ReferenceEquals(notifier, m_rootNotifiers[ii]))
                    {
                        notifier.OnReportEvent = null;
                        notifier.RemoveReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
                        m_rootNotifiers.RemoveAt(ii);
                        break;
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
            NodeState           source,
            IEventMonitoredItem monitoredItem, 
            bool                unsubscribe)
        {
            MonitoredNode2 monitoredNode = null;

            // handle unsubscribe.
            if (unsubscribe)
            {
                // check for existing monitored node.
                if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                monitoredNode.Remove(monitoredItem);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(source.NodeId);
                }

                // update flag.
                source.SetAreEventsMonitored(context, !unsubscribe, true);

                // call subclass.
                OnSubscribeToEvents(context, monitoredNode, unsubscribe);

                // all done.
                return ServiceResult.Good;
            }

            // only objects or views can be subscribed to.
            BaseObjectState instance = source as BaseObjectState;

            if (instance == null || (instance.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
            {
                ViewState view = source as ViewState;

                if (view == null || (view.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return StatusCodes.BadNotSupported;
                }
            }

            // check for existing monitored node.
            if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
            {
                MonitoredNodes[source.NodeId] = monitoredNode = new MonitoredNode2(this, source);
            }

            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            monitoredNode.Add(monitoredItem);

            // This call recursively updates a reference count all nodes in the notifier
            // hierarchy below the area. Sources with a reference count of 0 do not have 
            // any active subscriptions so they do not need to report events.
            source.SetAreEventsMonitored(context, !unsubscribe, true);

            // signal update.
            OnSubscribeToEvents(context, monitoredNode, unsubscribe);

            // all done.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called after subscribing/unsubscribing to events.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoredNode">The monitored node.</param>
        /// <param name="unsubscribe">if set to <c>true</c> unsubscribing.</param>
        protected virtual void OnSubscribeToEvents(
            ServerSystemContext context,
            MonitoredNode2       monitoredNode, 
            bool                unsubscribe)
        {
            // defined by the sub-class
        }
        #endregion

        /// <summary>
        /// Tells the node manager to refresh any conditions associated with the specified monitored items.
        /// </summary>
        /// <remarks>
        /// This method is called when the condition refresh method is called for a subscription.
        /// The node manager must create a refresh event for each condition monitored by the subscription.
        /// </remarks>
        public virtual ServiceResult ConditionRefresh(
            OperationContext           context,
            IList<IEventMonitoredItem> monitoredItems)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // the IEventMonitoredItem should always be MonitoredItems since they are created by the MasterNodeManager.
                MonitoredItem monitoredItem = monitoredItems[ii] as MonitoredItem;

                if (monitoredItem == null)
                {
                    continue;
                }

                List<IFilterTarget> events = new List<IFilterTarget>();
                List<NodeState> nodesToRefresh = new List<NodeState>();

                lock (Lock)
                {
                    // check for server subscription.
                    if (monitoredItem.NodeId == ObjectIds.Server)
                    {
                        if (m_rootNotifiers != null)
                        {
                            nodesToRefresh.AddRange(m_rootNotifiers);
                        }
                    }
                    else
                    {
                        // check for existing monitored node.
                        MonitoredNode2 monitoredNode = null;

                        if (!MonitoredNodes.TryGetValue(monitoredItem.NodeId, out monitoredNode))
                        {
                            continue;
                        }

                        // get the refresh events.
                        nodesToRefresh.Add(monitoredNode.Node);
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
                    monitoredItem.QueueEvent(events[jj]);
                }
            }

            // all done.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new set of monitored items for a set of variables.
        /// </summary>
        /// <remarks>
        /// This method only handles data change subscriptions. Event subscriptions are created by the SDK.
        /// </remarks>
        public virtual void CreateMonitoredItems(
            OperationContext                  context, 
            uint                              subscriptionId, 
            double                            publishingInterval, 
            TimestampsToReturn                timestampsToReturn, 
            IList<MonitoredItemCreateRequest> itemsToCreate, 
            IList<ServiceResult>              errors, 
            IList<MonitoringFilterResult>     filterResults, 
            IList<IMonitoredItem>             monitoredItems,
            ref long                          globalIdCounter)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<NodeHandle> nodesToValidate = new List<NodeHandle>();
            List<IMonitoredItem> createdItems = new List<IMonitoredItem>();

            lock (Lock)
            {
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
                    NodeHandle handle = GetManagerHandle(systemContext, itemToMonitor.NodeId, operationCache);

                    if (handle == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    itemToCreate.Processed = true;

                    // must validate node in a seperate operation.
                    errors[ii] = StatusCodes.BadNodeIdUnknown;

                    handle.Index = ii;
                    nodesToValidate.Add(handle);
                }

                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }
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
                        ref globalIdCounter,
                        out filterResult,
                        out monitoredItem);
                }

                // save any filter error details.
                filterResults[handle.Index] = filterResult;

                if (ServiceResult.IsBad(errors[handle.Index]))
                {
                    continue;
                }

                // save the monitored item.
                monitoredItems[handle.Index] = monitoredItem;
                createdItems.Add(monitoredItem);
            }

            // do any post processing.
            OnCreateMonitoredItemsComplete(systemContext, createdItems);
        }

        #region CreateMonitoredItem Support Functions
        /// <summary>
        /// Called when a batch of monitored items has been created.
        /// </summary>
        protected virtual void OnCreateMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
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
            ref long globalIdCounter,
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

            NodeState cachedNode = AddNodeToComponentCache(context, handle, handle.Node);

            // check if the node is already being monitored.
            MonitoredNode2 monitoredNode = null;

            if (!m_monitoredNodes.TryGetValue(handle.Node.NodeId, out monitoredNode))
            {
                m_monitoredNodes[handle.Node.NodeId] = monitoredNode = new MonitoredNode2(this, cachedNode);
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

            if (queueSize > m_maxQueueSize)
            {
                queueSize = m_maxQueueSize;
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
            MonitoredItem datachangeItem = new MonitoredItem(
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
            m_monitoredItems.Add(monitoredItemId, datachangeItem);
            monitoredNode.Add(datachangeItem);

            // report change.
            OnMonitoredItemCreated(context, handle, datachangeItem);

            return error;
        }

        /// <summary>
        /// Reads the initial value for a monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The item handle.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected virtual void ReadInitialValue(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem)
        {
            DataValue initialValue = new DataValue();

            initialValue.Value = null;
            initialValue.ServerTimestamp = DateTime.UtcNow;
            initialValue.SourceTimestamp = DateTime.MinValue;
            initialValue.StatusCode = StatusCodes.BadWaitingForInitialData;

            ServiceResult error = handle.Node.ReadAttribute(
                context,
                monitoredItem.AttributeId,
                monitoredItem.IndexRange,
                monitoredItem.DataEncoding,
                initialValue);

            monitoredItem.QueueValue(initialValue, error);
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
            MonitoredItem monitoredItem)
        {
            // overridden by the sub-class.
        }

        /// <summary>
        /// Validates Role permissions for the specified NodeId 
        /// </summary>
        /// <param name="operationContext"></param>
        /// <param name="nodeId"></param>
        /// <param name="requestedPermission"></param>
        /// <returns></returns>
        public ServiceResult ValidateRolePermissions(OperationContext operationContext, NodeId nodeId, PermissionType requestedPermission)
        {
            if (operationContext.Session == null || requestedPermission == PermissionType.None)
            {
                // no permission is required hence the validation passes.
                return StatusCodes.Good;
            }

            INodeManager nodeManager = null;
            object nodeHandle = Server.NodeManager.GetManagerHandle(nodeId, out nodeManager);

            if (nodeHandle == null || nodeManager == null)
            {
                // ignore unknown nodes.
                return StatusCodes.Good;
            }

            NodeMetadata nodeMetadata = nodeManager.GetNodeMetadata(operationContext, nodeHandle, BrowseResultMask.All);

            return MasterNodeManager.ValidateRolePermissions(operationContext, nodeMetadata, requestedPermission);
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
            DataChangeFilter deadbandFilter = ExtensionObject.ToEncodeable(filter) as DataChangeFilter;

            if (deadbandFilter == null)
            {
                AggregateFilter aggregateFilter = ExtensionObject.ToEncodeable(filter) as AggregateFilter;

                if (aggregateFilter == null || attributeId != Attributes.Value)
                {
                    return StatusCodes.BadFilterNotAllowed;
                }

                if (!Server.AggregateManager.IsSupported(aggregateFilter.AggregateType))
                {
                    return StatusCodes.BadAggregateNotSupported;
                }

                ServerAggregateFilter revisedFilter = new ServerAggregateFilter();
                revisedFilter.AggregateType = aggregateFilter.AggregateType;
                revisedFilter.StartTime = aggregateFilter.StartTime;
                revisedFilter.ProcessingInterval = aggregateFilter.ProcessingInterval;
                revisedFilter.AggregateConfiguration = aggregateFilter.AggregateConfiguration;
                revisedFilter.Stepped = false;

                StatusCode error = ReviseAggregateFilter(context, handle, samplingInterval, queueSize, revisedFilter);

                if (StatusCode.IsBad(error))
                {
                    return error;
                }

                AggregateFilterResult aggregateFilterResult = new AggregateFilterResult();
                aggregateFilterResult.RevisedProcessingInterval = aggregateFilter.ProcessingInterval;
                aggregateFilterResult.RevisedStartTime = aggregateFilter.StartTime;
                aggregateFilterResult.RevisedAggregateConfiguration = aggregateFilter.AggregateConfiguration;

                filterToUse = revisedFilter;
                result = aggregateFilterResult;
                return StatusCodes.Good;
            }

            // deadband filters only allowed for variable values.
            if (attributeId != Attributes.Value)
            {
                return StatusCodes.BadFilterNotAllowed;
            }

            BaseVariableState variable = handle.Node as BaseVariableState;

            if (variable == null)
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
                PropertyState property = handle.Node.FindChild(context, Opc.Ua.BrowseNames.EURange) as PropertyState;

                if (property == null)
                {
                    return StatusCodes.BadMonitoredItemFilterUnsupported;
                }

                range = property.Value as Range;

                if (range == null)
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

            DateTime earliestStartTime = DateTime.UtcNow.AddMilliseconds(-(queueSize - 1) * filterToUse.ProcessingInterval);

            if (earliestStartTime > filterToUse.StartTime)
            {
                filterToUse.StartTime = earliestStartTime;
            }

            if (filterToUse.AggregateConfiguration.UseServerCapabilitiesDefaults)
            {
                filterToUse.AggregateConfiguration = Server.AggregateManager.GetDefaultConfiguration(null);
            }

            return StatusCodes.Good;
        }
        #endregion

        /// <summary>
        /// Modifies the parameters for a set of monitored items.
        /// </summary>
        public virtual void ModifyMonitoredItems(
            OperationContext                  context, 
            TimestampsToReturn                timestampsToReturn, 
            IList<IMonitoredItem>             monitoredItems, 
            IList<MonitoredItemModifyRequest> itemsToModify, 
            IList<ServiceResult>              errors, 
            IList<MonitoringFilterResult>     filterResults)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            List<IMonitoredItem> modifiedItems = new List<IMonitoredItem>();

            lock (Lock)
            {
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

                    // owned by this node manager.
                    itemToModify.Processed = true;

                    // modify the monitored item.
                    MonitoringFilterResult filterResult = null;

                    errors[ii] = ModifyMonitoredItem(
                        systemContext,
                        context.DiagnosticsMask,
                        timestampsToReturn,
                        monitoredItems[ii],
                        itemToModify,
                        handle,
                        out filterResult);

                    // save any filter error details.
                    filterResults[ii] = filterResult;

                    // save the modified item.
                    if (ServiceResult.IsGood(errors[ii]))
                    {
                        modifiedItems.Add(monitoredItems[ii]);
                    }
                }
            }

            // do any post processing.
            OnModifyMonitoredItemsComplete(systemContext, modifiedItems);
        }

        #region ModifyMonitoredItem Support Functions
        /// <summary>
        /// Called when a batch of monitored items has been modified.
        /// </summary>
        protected virtual void OnModifyMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
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
            filterResult = null;

            // check for valid monitored item.
            MonitoredItem datachangeItem = monitoredItem as MonitoredItem;

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
            if (datachangeItem.AttributeId == Attributes.Value)
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
            uint queueSize = itemToModify.RequestedParameters.QueueSize;

            if (queueSize > m_maxQueueSize)
            {
                queueSize = m_maxQueueSize;
            }

            // validate the monitoring filter.
            Range euRange = null;
            MonitoringFilter filterToUse = null;

            ServiceResult error = ValidateMonitoringFilter(
                context,
                handle,
                datachangeItem.AttributeId,
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

            // modify the monitored item parameters.
            error = datachangeItem.ModifyAttributes(
                diagnosticsMasks,
                timestampsToReturn,
                itemToModify.RequestedParameters.ClientHandle,
                filterToUse,
                filterToUse,
                euRange,
                samplingInterval,
                queueSize,
                itemToModify.RequestedParameters.DiscardOldest);

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
            MonitoredItem monitoredItem)
        {
            // overridden by the sub-class.
        }
        #endregion

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        public virtual void DeleteMonitoredItems(
            OperationContext      context, 
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>           processedItems, 
            IList<ServiceResult>  errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            List<IMonitoredItem> deletedItems = new List<IMonitoredItem>();

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

                    errors[ii] = DeleteMonitoredItem(
                        systemContext,
                        monitoredItems[ii],
                        handle);

                    // save the modified item.
                    if (ServiceResult.IsGood(errors[ii]))
                    {
                        deletedItems.Add(monitoredItems[ii]);
                        RemoveNodeFromComponentCache(systemContext, handle);
                    }
                }
            }

            // do any post processing.
            OnDeleteMonitoredItemsComplete(systemContext, deletedItems);
        }

        #region DeleteMonitoredItems Support Functions
        /// <summary>
        /// Called when a batch of monitored items has been modified.
        /// </summary>
        protected virtual void OnDeleteMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
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
            // check for valid monitored item.
            MonitoredItem datachangeItem = monitoredItem as MonitoredItem;

            // check if the node is already being monitored.
            MonitoredNode2 monitoredNode = null;

            if (m_monitoredNodes.TryGetValue(handle.NodeId, out monitoredNode))
            {
                monitoredNode.Remove(datachangeItem);

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(handle.NodeId);
                }
            }

            // remove the monitored item.
            m_monitoredItems.Remove(monitoredItem.Id);

            // report change.
            OnMonitoredItemDeleted(context, handle, datachangeItem);

            return ServiceResult.Good;
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
            MonitoredItem monitoredItem)
        {
            // overridden by the sub-class.
        }
        #endregion

        /// <summary>
        /// Changes the monitoring mode for a set of monitored items.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="monitoringMode">The monitoring mode.</param>
        /// <param name="monitoredItems">The set of monitoring items to update.</param>
        /// <param name="processedItems">Flags indicating which items have been processed.</param>
        /// <param name="errors">Any errors.</param>
        public virtual void SetMonitoringMode(
            OperationContext      context, 
            MonitoringMode        monitoringMode, 
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>           processedItems, 
            IList<ServiceResult>  errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            List<IMonitoredItem> changedItems = new List<IMonitoredItem>();

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

            // do any post processing.
            OnSetMonitoringModeComplete(systemContext, changedItems);
        }

        #region SetMonitoringMode Support Functions
        /// <summary>
        /// Called when a batch of monitored items has their monitoring mode changed.
        /// </summary>
        protected virtual void OnSetMonitoringModeComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
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
            // check for valid monitored item.
            MonitoredItem datachangeItem = monitoredItem as MonitoredItem;

            // update monitoring mode.
            MonitoringMode previousMode = datachangeItem.SetMonitoringMode(monitoringMode);

            // must send the latest value after enabling a disabled item.
            if (monitoringMode == MonitoringMode.Reporting && previousMode == MonitoringMode.Disabled)
            {
                handle.MonitoredNode.QueueValue(context, handle.Node, datachangeItem);
            }

            // report change.
            if (previousMode != monitoringMode)
            {
                OnMonitoringModeChanged(
                    context,
                    handle,
                    datachangeItem,
                    previousMode,
                    monitoringMode);
            }

            return ServiceResult.Good;
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
            MonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode)
        {
            // overridden by the sub-class.
        }
        #endregion
        #endregion

        #region INodeManager2 Members
        /// <summary>
        /// Called when a session is closed.
        /// </summary>
        public virtual void SessionClosing(OperationContext context, NodeId sessionId, bool deleteSubscriptions)
        {
        }

        /// <summary>
        /// Returns true if a node is in a view.
        /// </summary>
        public virtual bool IsNodeInView(OperationContext context, NodeId viewId, object nodeHandle)
        {
            NodeHandle handle = nodeHandle as NodeHandle;

            if (handle == null)
            {
                return false;
            }

            if (handle.Node != null)
            {
                return IsNodeInView(context, viewId, handle.Node);
            }

            return false;
        }
        #endregion

        #region ComponentCache Functions
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

                if (!String.IsNullOrEmpty(handle.ComponentPath))
                {
                    if (m_componentCache.TryGetValue(handle.RootId, out entry))
                    {
                        return entry.Entry.FindChildBySymbolicName(context, handle.ComponentPath);
                    }
                }
                else
                {
                    if (m_componentCache.TryGetValue(handle.NodeId, out entry))
                    {
                        return entry.Entry;
                    }
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

                    if (!String.IsNullOrEmpty(handle.ComponentPath))
                    {
                        nodeId = handle.RootId;
                    }

                    CacheEntry entry = null;

                    if (m_componentCache.TryGetValue(nodeId, out entry))
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
        protected NodeState AddNodeToComponentCache(ISystemContext context, NodeHandle handle, NodeState node)
        {
            lock (Lock)
            {
                if (handle == null)
                {
                    return node;
                }

                if (m_componentCache == null)
                {
                    m_componentCache = new Dictionary<NodeId, CacheEntry>();
                }

                // check if a component is actually specified.
                if (!String.IsNullOrEmpty(handle.ComponentPath))
                {
                    CacheEntry entry = null;

                    if (m_componentCache.TryGetValue(handle.RootId, out entry))
                    {
                        entry.RefCount++;

                        if (!String.IsNullOrEmpty(handle.ComponentPath))
                        {
                            return entry.Entry.FindChildBySymbolicName(context, handle.ComponentPath);
                        }

                        return entry.Entry;
                    }

                    NodeState root = node.GetHierarchyRoot();

                    if (root != null)
                    {
                        entry = new CacheEntry();
                        entry.RefCount = 1;
                        entry.Entry = root;
                        m_componentCache.Add(handle.RootId, entry);
                    }
                }

                // simply add the node to the cache.
                else
                {
                    CacheEntry entry = null;

                    if (m_componentCache.TryGetValue(handle.NodeId, out entry))
                    {
                        entry.RefCount++;
                        return entry.Entry;
                    }

                    entry = new CacheEntry();
                    entry.RefCount = 1;
                    entry.Entry = node;
                    m_componentCache.Add(handle.NodeId, entry);
                }

                return node;
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private IServerInternal m_server;
        private ServerSystemContext m_systemContext;
        private string[] m_namespaceUris;
        private ushort[] m_namespaceIndexes;
        private Dictionary<uint, IDataChangeMonitoredItem> m_monitoredItems;
        private Dictionary<NodeId, MonitoredNode2> m_monitoredNodes;
        private Dictionary<NodeId, CacheEntry> m_componentCache;
        private NodeIdDictionary<NodeState> m_predefinedNodes;
        private List<NodeState> m_rootNotifiers;
        private uint m_maxQueueSize;
        private string m_aliasRoot;
        #endregion
    }
}
