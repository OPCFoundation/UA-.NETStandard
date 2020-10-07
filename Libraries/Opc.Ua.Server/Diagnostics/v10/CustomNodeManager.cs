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
using System.Text;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A node manager for a variety of test data.
    /// </summary>
    [Obsolete("Class replaced by Opc.Ua.Server.CustomNodeManager2")]
    public class CustomNodeManager : INodeManager, INodeIdFactory, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public CustomNodeManager(IServerInternal server)
        {
            // save a reference to the server that owns the node manager.
            m_server = server;  
            
            // create the default context.
            m_systemContext = m_server.DefaultSystemContext.Copy();

            m_systemContext.SystemHandle = null;
            m_systemContext.NodeIdFactory = this;

            // create the table of nodes. 
            m_predefinedNodes = new NodeIdDictionary<NodeState>();
            m_rootNotifiers = new List<NodeState>();
            m_sampledItems = new List<DataChangeMonitoredItem>();
            m_minimumSamplingInterval = 100;
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
                    Utils.SilentDispose(m_samplingTimer);
                    m_samplingTimer = null;

                    if (m_predefinedNodes != null)
                    {
                        foreach (NodeState node in m_predefinedNodes.Values)
                        {
                            Utils.SilentDispose(node);
                        }
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
        #endregion

        #region Protected Members
        /// <summary>
        /// The server that the node manager belongs to.
        /// </summary>
        protected IServerInternal Server
        {
            get { return m_server; }
        }

        /// <summary>
        /// The default context to use.
        /// </summary>
        protected ServerSystemContext SystemContext
        {
            get { return m_systemContext; }
        }

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
        /// Returns true if the namespace for the node id is one of the namespaces managed by the node manager.
        /// </summary>
        /// <param name="nodeId">The node id to check.</param>
        /// <returns>True if the namespace is one of the nodes.</returns>
        protected virtual bool IsNodeIdInNamespace(NodeId nodeId)
        {
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
        protected virtual NodeState IsHandleInNamespace(object managerHandle)
        {
            NodeState source = managerHandle as NodeState;

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
                instance.ReferenceTypeId = referenceTypeId;

                NodeState parent = null;

                if (parentId != null)
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
                if (value != null)
                {
                    m_namespaceUris = new List<string>(value);
                }
                else
                {
                    m_namespaceUris = new List<string>();
                }

                m_namespaceIndexes = new ushort[m_namespaceUris.Count];
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
            lock (Lock)
            {
                // add the uris to the server's namespace table and cache the indexes.
                for (int ii = 0; ii < m_namespaceUris.Count; ii++)
                {
                    m_namespaceIndexes[ii] = m_server.NamespaceUris.GetIndexOrAppend(m_namespaceUris[ii]);
                }

                LoadPredefinedNodes(m_systemContext, externalReferences);
            }
        }

        #region CreateAddressSpace Support Functions
        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        public virtual void LoadPredefinedNodes(
            ISystemContext context, 
            Assembly assembly,
            string resourcePath,
            IDictionary<NodeId,IList<IReference>> externalReferences)
        {
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
            IDictionary<NodeId,IList<IReference>> externalReferences)
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
            NodeState activeNode = AddBehaviourToPredefinedNode(context, node);
            m_predefinedNodes[activeNode.NodeId] = activeNode;
            
            BaseTypeState type = activeNode as BaseTypeState;

            if (type != null)
            {
                AddTypesToTypeTree(type);
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
        /// Add the node to the set of root notifiers.
        /// </summary>
        protected virtual void AddRootNotifier(NodeState notifier)
        {
            for (int ii = 0; ii < m_rootNotifiers.Count; ii++)
            {
                if (Object.ReferenceEquals(notifier, m_rootNotifiers[ii]))
                {
                    return;
                }
            }

            m_rootNotifiers.Add(notifier);

            // subscribe to existing events.
            if (m_server.EventManager != null)
            {
                IList<IEventMonitoredItem> monitoredItems = m_server.EventManager.GetMonitoredItems();

                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    if (monitoredItems[ii].MonitoringAllEvents)
                    {
                        SubscribeToAllEvents(
                            SystemContext,
                            monitoredItems[ii],
                            true,
                            notifier);
                    }
                }
            }
        }

        /// <summary>
        /// Remove the node from the set of root notifiers.
        /// </summary>
        protected virtual void RemoveRootNotifier(NodeState notifier)
        {
            for (int ii = 0; ii < m_rootNotifiers.Count; ii++)
            {
                if (Object.ReferenceEquals(notifier, m_rootNotifiers[ii]))
                {
                    m_rootNotifiers.RemoveAt(ii);
                    break;
                }
            }
        }

        /// <summary>
        /// Ensures that all reverse references exist.
        /// </summary>
        /// <param name="externalReferences">A list of references to add to external targets.</param>
        protected virtual void AddReverseReferences(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            foreach (NodeState source in m_predefinedNodes.Values)
            {
                // assign a default value to any variable value.
                BaseVariableState variable = source as BaseVariableState; 
                
                if (variable != null && variable.Value == null)
                {
                    variable.Value = TypeInfo.GetDefaultValue(variable.DataType, variable.ValueRank, Server.TypeTree);
                }

                // add reference from supertype for type nodes.
                BaseTypeState type = source as BaseTypeState;

                if (type != null && !NodeId.IsNull(type.SuperTypeId))
                {
                    if (!IsNodeIdInNamespace(type.SuperTypeId))
                    {
                        AddExternalReference(
                            type.SuperTypeId,
                            ReferenceTypeIds.HasSubtype,
                            false,
                            type.NodeId,
                            externalReferences);
                    }
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

                    NodeId targetId = (NodeId)reference.TargetId;
                    
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
            BaseTypeState type = Find(typeId) as BaseTypeState;

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
            lock (Lock)
            {
                m_predefinedNodes.Clear();
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
        /// <remarks>
        /// This must efficiently determine whether the node belongs to the node manager. If it does belong to 
        /// NodeManager it should return a handle that does not require the NodeId to be validated again when
        /// the handle is passed into other methods such as 'Read' or 'Write'.
        /// </remarks>
        protected virtual object GetManagerHandle(ISystemContext context, NodeId nodeId, IDictionary<NodeId,NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                // lookup the node.
                NodeState node = null;

                if (!m_predefinedNodes.TryGetValue(nodeId, out node))
                {
                    return null;
                }

                return node;
            }
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
                    // check for valid handle.
                    NodeState source = GetManagerHandle(m_systemContext, current.Key, null) as NodeState;

                    if (source == null)
                    {
                        continue;
                    }

                    // add reference to external target.
                    foreach (IReference reference in current.Value)
                    {
                        source.AddReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
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
                // check for valid handle.
                NodeState source = IsHandleInNamespace(sourceHandle);

                if (source == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                source.RemoveReference(referenceTypeId, isInverse, targetId);

                if (deleteBiDirectional)
                {
                    // check if the target is also managed by the node manager.
                    if (!targetId.IsAbsolute)
                    {
                        NodeState target = GetManagerHandle(m_systemContext, (NodeId)targetId, null) as NodeState;

                        if (target != null)
                        {
                            target.RemoveReference(referenceTypeId, !isInverse, source.NodeId);
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
                NodeState target = IsHandleInNamespace(targetHandle);

                if (target == null)
                {
                    return null;                
                }

                // validate node.
                if (!ValidateNode(systemContext, target))
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
                    Attributes.UserExecutable);

                // construct the metadata object.

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

            // check for view.
            if (!ViewDescription.IsDefault(continuationPoint.View))
            {
                throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
            }

            ServerSystemContext systemContext = m_systemContext.Copy(context);

            lock (Lock)
            {
                // verify that the node exists.
                NodeState source = IsHandleInNamespace(continuationPoint.NodeToBrowse);

                if (source == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                // validate node.
                if (!ValidateNode(systemContext, source))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                // check for previous continuation point.
                INodeBrowser browser = continuationPoint.Data as INodeBrowser;

                // fetch list of references.
                if (browser == null)
                {
                    // create a new browser.
                    browser = source.CreateBrowser(
                        systemContext,
                        continuationPoint.View,
                        continuationPoint.ReferenceTypeId,
                        continuationPoint.IncludeSubtypes,
                        continuationPoint.BrowseDirection,
                        null,
                        null,
                        false);
                }

                // apply filters to references.
                for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
                {
                    // create the type definition reference.        
                    ReferenceDescription description = GetReferenceDescription(context, reference, continuationPoint);

                    if (description == null)
                    {
                        continue;
                    }

                    // check if limit reached.
                    if (continuationPoint.MaxResultsToReturn != 0 && references.Count >= continuationPoint.MaxResultsToReturn)
                    {
                        browser.Push(reference);
                        continuationPoint.Data = browser;
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
        /// Returns the references for the node that meets the criteria specified.
        /// </summary>
        private ReferenceDescription GetReferenceDescription(
            OperationContext context,
            IReference reference,
            ContinuationPoint continuationPoint)
        {
            // create the type definition reference.        
            ReferenceDescription description = new ReferenceDescription();

            description.NodeId = reference.TargetId;
            description.SetReferenceType(continuationPoint.ResultMask, reference.ReferenceTypeId, !reference.IsInverse);

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
                NodeId targetId = (NodeId)reference.TargetId;

                if (IsNodeIdInNamespace(targetId))
                {
                    if (!PredefinedNodes.TryGetValue(targetId, out target))
                    {
                        target = null;
                    }
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
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();

            lock (Lock)
            {
                // verify that the node exists.
                NodeState source = IsHandleInNamespace(sourceHandle);

                if (source == null)
                {
                    return;
                }

                // validate node.
                if (!ValidateNode(systemContext, source))
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
                            target = GetManagerHandle(systemContext, targetId, operationCache) as NodeState;

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
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<ReadWriteOperationState> nodesToValidate = new List<ReadWriteOperationState>();

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
                    NodeState source = GetManagerHandle(systemContext, nodeToRead.NodeId, operationCache) as NodeState;

                    if (source == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToRead.Processed = true;
                    
                    // create an initial value.
                    DataValue value = values[ii] = new DataValue();
                    
                    value.Value           = null;
                    value.ServerTimestamp = DateTime.UtcNow;
                    value.SourceTimestamp = DateTime.MinValue;
                    value.StatusCode      = StatusCodes.Good;

                    // check if the node is ready for reading.
                    if (source.ValidationRequired)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        
                        // must validate node in a seperate operation.
                        ReadWriteOperationState operation = new ReadWriteOperationState();
                        
                        operation.Source = source;
                        operation.Index = ii;

                        nodesToValidate.Add(operation);
                        
                        continue;
                    }

                    // read the attribute value.
                    errors[ii] = source.ReadAttribute(
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

                // validates the nodes (reads values from the underlying data source if required).
                for (int ii = 0; ii < nodesToValidate.Count; ii++)
                {
                    ReadWriteOperationState operation = nodesToValidate[ii];

                    if (!ValidateNode(systemContext, operation.Source))
                    {
                        continue;
                    }
                    
                    ReadValueId nodeToRead = nodesToRead[operation.Index];
                    DataValue value = values[operation.Index];

                    // update the attribute value.
                    errors[operation.Index] = operation.Source.ReadAttribute(
                        systemContext,
                        nodeToRead.AttributeId,
                        nodeToRead.ParsedIndexRange,
                        nodeToRead.DataEncoding,
                        value);
                }
            }
        }        
        
        /// <summary>
        /// Stores the state of a call method operation.
        /// </summary>
        private struct ReadWriteOperationState
        {
            public NodeState Source;
            public int Index;
        }        
        
        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected virtual bool ValidateNode(ServerSystemContext context, NodeState node)
        {
            // validate node only if required.
            if (node.ValidationRequired)
            {
                return node.Validate(context);
            }

            return true;
        }
        
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
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<ReadWriteOperationState> nodesToValidate = new List<ReadWriteOperationState>();
            List<ReadWriteOperationState> readsToComplete = new List<ReadWriteOperationState>();

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
                    NodeState source = GetManagerHandle(systemContext, nodeToRead.NodeId, operationCache) as NodeState;

                    if (source == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    // only variables supported.
                    BaseVariableState variable = source as BaseVariableState;

                    if (variable == null)
                    {
                        errors[ii] = StatusCodes.BadHistoryOperationUnsupported;
                        continue;
                    }
                    
                    results[ii] = new HistoryReadResult();
                    
                    ReadWriteOperationState operation = new ReadWriteOperationState();
                    
                    operation.Source = source;
                    operation.Index = ii;

                    // check if the node is ready for reading.
                    if (source.ValidationRequired)
                    {
                        // must validate node in a seperate operation.
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        nodesToValidate.Add(operation);                        
                        continue;
                    }

                    // read the data.
                    readsToComplete.Add(operation);
                }

                // validates the nodes (reads values from the underlying data source if required).
                for (int ii = 0; ii < nodesToValidate.Count; ii++)
                {
                    ReadWriteOperationState operation = nodesToValidate[ii];

                    if (!ValidateNode(systemContext, operation.Source))
                    {
                        continue;
                    }

                    readsToComplete.Add(operation);
                }
            }

            // reads the data without holding onto the lock.
            for (int ii = 0; ii < readsToComplete.Count; ii++)
            {
                ReadWriteOperationState operation = readsToComplete[ii];

                errors[operation.Index] = HistoryRead(
                    systemContext,
                    operation.Source,
                    details,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead[operation.Index],
                    results[operation.Index]);
            }
        }

        /// <summary>
        /// Reads the history for a single node which has already been validated.
        /// </summary>
        protected virtual ServiceResult HistoryRead(
            ISystemContext context, 
            NodeState           source,
            HistoryReadDetails  details, 
            TimestampsToReturn  timestampsToReturn, 
            bool                releaseContinuationPoints, 
            HistoryReadValueId  nodesToRead, 
            HistoryReadResult   result)
        {
            // check for variable.
            BaseVariableState variable = source as BaseVariableState;

            if (variable == null)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            // check for access.
            lock (Lock)
            {
                if ((variable.AccessLevel & AccessLevels.HistoryRead) == 0)
                {
                    return StatusCodes.BadNotReadable;
                }
            }

            // handle read raw.
            ReadRawModifiedDetails readRawDetails = details as ReadRawModifiedDetails;

            if (readRawDetails != null)
            {
                return HistoryReadRaw(
                    context,
                    variable,
                    readRawDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    result);
            }

            // handle read processed.
            ReadProcessedDetails readProcessedDetails = details as ReadProcessedDetails;

            if (readProcessedDetails != null)
            {
                return HistoryReadProcessed(
                    context,
                    variable,
                    readProcessedDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    result);
            }

            // handle read processed.
            ReadAtTimeDetails readAtTimeDetails = details as ReadAtTimeDetails;

            if (readAtTimeDetails != null)
            {
                return HistoryReadAtTime(
                    context,
                    variable,
                    readAtTimeDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    result);
            }
 
            return StatusCodes.BadHistoryOperationUnsupported;
        }
         
        /// <summary>
        /// Reads the raw history for the variable value.
        /// </summary>
        protected virtual ServiceResult HistoryReadRaw(
            ISystemContext context, 
            BaseVariableState      source,
            ReadRawModifiedDetails details, 
            TimestampsToReturn     timestampsToReturn, 
            bool                   releaseContinuationPoints, 
            HistoryReadValueId     nodeToRead, 
            HistoryReadResult      result)
        {
            return StatusCodes.BadHistoryOperationUnsupported;
        }
         
        /// <summary>
        /// Reads the processed history for the variable value.
        /// </summary>
        protected virtual ServiceResult HistoryReadProcessed(
            ISystemContext context, 
            BaseVariableState      source,
            ReadProcessedDetails   details, 
            TimestampsToReturn     timestampsToReturn, 
            bool                   releaseContinuationPoints, 
            HistoryReadValueId     nodeToRead, 
            HistoryReadResult      result)
        {
            return StatusCodes.BadHistoryOperationUnsupported;
        }
         
        /// <summary>
        /// Reads the history for the variable value.
        /// </summary>
        protected virtual ServiceResult HistoryReadAtTime(
            ISystemContext context, 
            BaseVariableState      source,
            ReadAtTimeDetails      details, 
            TimestampsToReturn     timestampsToReturn, 
            bool                   releaseContinuationPoints, 
            HistoryReadValueId     nodeToRead, 
            HistoryReadResult      result)
        {
            return StatusCodes.BadHistoryOperationUnsupported;
        }
        
        
        /// <summary>
        /// Writes the value for the specified attributes.
        /// </summary>
        public virtual void Write(
            OperationContext     context, 
            IList<WriteValue>    nodesToWrite, 
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<ReadWriteOperationState> nodesToValidate = new List<ReadWriteOperationState>();

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
                    NodeState source = GetManagerHandle(systemContext, nodeToWrite.NodeId, operationCache) as NodeState;

                    if (source == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToWrite.Processed = true;

                    // index range is not supported.
                    if (!String.IsNullOrEmpty(nodeToWrite.IndexRange))
                    {
                        errors[ii] = StatusCodes.BadIndexRangeInvalid;
                        continue;
                    }
                    
                    // check if the node is ready for reading.
                    if (source.ValidationRequired)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        
                        // must validate node in a seperate operation.
                        ReadWriteOperationState operation = new ReadWriteOperationState();
                        
                        operation.Source = source;
                        operation.Index = ii;

                        nodesToValidate.Add(operation);
                        
                        continue;
                    }

                    // write the attribute value.
                    errors[ii] = source.WriteAttribute(
                        systemContext,
                        nodeToWrite.AttributeId,
                        nodeToWrite.ParsedIndexRange,
                        nodeToWrite.Value);

                    // updates to source finished - report changes to monitored items.
                    source.ClearChangeMasks(systemContext, false);
                }

                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }

                // validates the nodes (reads values from the underlying data source if required).
                for (int ii = 0; ii < nodesToValidate.Count; ii++)
                {
                    ReadWriteOperationState operation = nodesToValidate[ii];

                    if (!ValidateNode(systemContext, operation.Source))
                    {
                        continue;
                    }
                    
                    WriteValue nodeToWrite = nodesToWrite[operation.Index];

                    // write the attribute value.
                    errors[operation.Index] = operation.Source.WriteAttribute(
                        systemContext,
                        nodeToWrite.AttributeId,
                        nodeToWrite.ParsedIndexRange,
                        nodeToWrite.Value);

                    // updates to source finished - report changes to monitored items.
                    operation.Source.ClearChangeMasks(systemContext, false);
                }
            }
        }
        
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
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<ReadWriteOperationState> nodesToValidate = new List<ReadWriteOperationState>();

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
                    NodeState source = GetManagerHandle(systemContext, nodeToUpdate.NodeId, operationCache) as NodeState;

                    if (source == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToUpdate.Processed = true;
                    
                    // check if the node is ready for reading.
                    if (source.ValidationRequired)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        
                        // must validate node in a seperate operation.
                        ReadWriteOperationState operation = new ReadWriteOperationState();
                        
                        operation.Source = source;
                        operation.Index = ii;

                        nodesToValidate.Add(operation);
                        
                        continue;
                    }

                    // historical data not available.
                    errors[ii] = StatusCodes.BadHistoryOperationUnsupported;
                }

                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }

                // validates the nodes (reads values from the underlying data source if required).
                for (int ii = 0; ii < nodesToValidate.Count; ii++)
                {
                    ReadWriteOperationState operation = nodesToValidate[ii];

                    if (!ValidateNode(systemContext, operation.Source))
                    {
                        continue;
                    }
                    
                    // historical data not available.
                    errors[ii] = StatusCodes.BadHistoryOperationUnsupported;
                }
            }
        }

        /// <summary>
        /// Calls a method on the specified nodes.
        /// </summary>
        public virtual void Call(
            OperationContext         context, 
            IList<CallMethodRequest> methodsToCall, 
            IList<CallMethodResult>  results, 
            IList<ServiceResult>     errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<CallOperationState> nodesToValidate = new List<CallOperationState>();

            lock (Lock)
            {
                for (int ii = 0; ii < methodsToCall.Count; ii++)
                {                    
                    CallMethodRequest methodToCall = methodsToCall[ii];

                    // skip items that have already been processed.
                    if (methodToCall.Processed)
                    {
                        continue;
                    }
                    
                    // check for valid handle.
                    NodeState source = GetManagerHandle(systemContext, methodToCall.ObjectId, operationCache) as NodeState;

                    if (source == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    methodToCall.Processed = true;

                    // check for valid method.
                    MethodState method = GetManagerHandle(systemContext, methodToCall.MethodId, operationCache) as MethodState;

                    if (method == null)
                    {
                        errors[ii] = StatusCodes.BadMethodInvalid;
                        continue;
                    }

                    // check if method belongs to the object.
                    if (!Object.ReferenceEquals(method.Parent, source))
                    {
                        errors[ii] = StatusCodes.BadMethodInvalid;
                        continue;
                    }
                    
                    CallMethodResult result = results[ii] = new CallMethodResult();

                    // check if the node is ready for reading.
                    if (source.ValidationRequired)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        
                        // must validate node in a seperate operation.
                        CallOperationState operation = new CallOperationState();
                        
                        operation.Source = source;
                        operation.Method = method;
                        operation.Index = ii;

                        nodesToValidate.Add(operation);

                        continue;
                    }
                    
                    // call the method.
                    errors[ii] = Call(
                        systemContext,
                        methodToCall,
                        source,
                        method,
                        result);
                }
                                
                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }

                // validates the nodes (reads values from the underlying data source if required).
                for (int ii = 0; ii < nodesToValidate.Count; ii++)
                {
                    CallOperationState operation = nodesToValidate[ii];

                    // validate the object.
                    if (!ValidateNode(systemContext, operation.Source))
                    {
                        continue;
                    }
                             
                    // call the method.
                    CallMethodResult result = results[operation.Index];

                    errors[operation.Index] = Call(
                        systemContext,
                        methodsToCall[operation.Index],
                        operation.Source,
                        operation.Method,
                        result);
                }
            }
        }
        
        /// <summary>
        /// Stores the state of a call method operation.
        /// </summary>
        private struct CallOperationState
        {
            public NodeState Source;
            public MethodState Method;
            public int Index;
        }
        
        /// <summary>
        /// Calls a method on an object.
        /// </summary>
        protected virtual ServiceResult Call(
            ISystemContext context,
            CallMethodRequest methodToCall,
            NodeState source,
            MethodState method,
            CallMethodResult result)
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
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();

            lock (Lock)
            {
                // check for valid handle.
                NodeState source = IsHandleInNamespace(sourceId);

                if (source == null)
                {
                    return StatusCodes.BadNodeIdInvalid;
                }

                // check if the object supports subscritions.
                BaseObjectState instance = sourceId as BaseObjectState;

                if (instance == null || instance.EventNotifier != EventNotifiers.SubscribeToEvents)
                {
                    return StatusCodes.BadNotSupported;
                }

                MonitoredNode monitoredNode = instance.Handle as MonitoredNode;

                // handle unsubscribe.
                if (unsubscribe)
                {
                    if (monitoredNode != null)
                    {
                        monitoredNode.UnsubscribeToEvents(systemContext, monitoredItem);

                        // do any post processing.
                        OnUnsubscribeToEvents(systemContext, monitoredNode, monitoredItem);
                    }

                    return ServiceResult.Good;
                }

                // subscribe to events.
                if (monitoredNode == null)
                {
                    instance.Handle = monitoredNode = new MonitoredNode(m_server, this, source);
                }

                monitoredNode.SubscribeToEvents(systemContext, monitoredItem);

                // do any post processing.
                OnSubscribeToEvents(systemContext, monitoredNode, monitoredItem);

                return ServiceResult.Good;
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
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();

            lock (Lock)
            {
                // update root notifiers.
                for (int ii = 0; ii < m_rootNotifiers.Count; ii++)
                {
                    SubscribeToAllEvents(
                        systemContext,
                        monitoredItem,
                        unsubscribe,
                        m_rootNotifiers[ii]);
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Subscribes/unsubscribes to all events produced by the specified node.
        /// </summary>
        protected void SubscribeToAllEvents(
            ISystemContext      systemContext, 
            IEventMonitoredItem monitoredItem, 
            bool                unsubscribe,
            NodeState           source)
        {
            MonitoredNode monitoredNode = source.Handle as MonitoredNode;

            // handle unsubscribe.
            if (unsubscribe)
            {
                if (monitoredNode != null)
                {
                    monitoredNode.UnsubscribeToEvents(systemContext, monitoredItem);

                    // do any post processing.
                    OnUnsubscribeToEvents(systemContext, monitoredNode, monitoredItem);
                }

                return;
            }

            // subscribe to events.
            if (monitoredNode == null)
            {
                source.Handle = monitoredNode = new MonitoredNode(m_server, this, source);
            }

            monitoredNode.SubscribeToEvents(systemContext, monitoredItem);

            // do any post processing.
            OnSubscribeToEvents(systemContext, monitoredNode, monitoredItem);
        }

        /// <summary>
        /// Does any processing after a monitored item is subscribed to.
        /// </summary>
        protected virtual void OnSubscribeToEvents(
            ISystemContext systemContext,
            MonitoredNode monitoredNode,
            IEventMonitoredItem monitoredItem)
        {
            // does nothing.
        }

        /// <summary>
        /// Does any processing after a monitored item is subscribed to.
        /// </summary>
        protected virtual void OnUnsubscribeToEvents(
            ISystemContext systemContext,
            MonitoredNode monitoredNode,
            IEventMonitoredItem monitoredItem)
        {
            // does nothing.
        }

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
            ServerSystemContext systemContext = m_systemContext.Copy(context);

            lock (Lock)
            {
                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    IEventMonitoredItem monitoredItem = monitoredItems[ii];

                    if (monitoredItem == null)
                    {
                        continue;
                    }
                    
                    // check for global subscription.
                    if (monitoredItem.MonitoringAllEvents)
                    {
                        for (int jj = 0; jj < m_rootNotifiers.Count; jj++)
                        {
                            MonitoredNode monitoredNode = m_rootNotifiers[jj].Handle as MonitoredNode;

                            if (monitoredNode == null)
                            {
                                continue;
                            }

                            monitoredNode.ConditionRefresh(systemContext, monitoredItem);
                        }
                    }

                    // check for subscription to local node.
                    else
                    {
                        NodeState source = IsHandleInNamespace(monitoredItem.ManagerHandle );

                        if (source == null)
                        {
                            continue;
                        }
                        
                        MonitoredNode monitoredNode = source.Handle as MonitoredNode;
                        
                        if (monitoredNode == null)
                        {
                            continue;
                        }
                                
                        monitoredNode.ConditionRefresh(systemContext, monitoredItem);
                    }
                }
            }

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
            IList<MonitoringFilterResult>     filterErrors, 
            IList<IMonitoredItem>             monitoredItems,
            ref long                          globalIdCounter)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);
            IDictionary<NodeId,NodeState> operationCache = new NodeIdDictionary<NodeState>();
            List<ReadWriteOperationState> nodesToValidate = new List<ReadWriteOperationState>();

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
                    NodeState source = GetManagerHandle(systemContext, itemToMonitor.NodeId, operationCache) as NodeState;

                    if (source == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    itemToCreate.Processed = true;

                    // check if the node is ready for reading.
                    if (source.ValidationRequired)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        
                        // must validate node in a seperate operation.
                        ReadWriteOperationState operation = new ReadWriteOperationState();
                        
                        operation.Source = source;
                        operation.Index = ii;

                        nodesToValidate.Add(operation);

                        continue;
                    }

                    MonitoringFilterResult filterError = null;
                    IMonitoredItem monitoredItem = null;

                    errors[ii] = CreateMonitoredItem(
                        systemContext,
                        source,
                        subscriptionId,
                        publishingInterval,
                        context.DiagnosticsMask,
                        timestampsToReturn,
                        itemToCreate,
                        ref globalIdCounter,
                        out filterError,
                        out monitoredItem);

                    // save any filter error details.
                    filterErrors[ii] = filterError;

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }

                    // save the monitored item.
                    monitoredItems[ii] = monitoredItem;
                }
                                
                // check for nothing to do.
                if (nodesToValidate.Count == 0)
                {
                    return;
                }

                // validates the nodes (reads values from the underlying data source if required).
                for (int ii = 0; ii < nodesToValidate.Count; ii++)
                {
                    ReadWriteOperationState operation = nodesToValidate[ii];

                    // validate the object.
                    if (!ValidateNode(systemContext, operation.Source))
                    {
                        continue;
                    }
                    
                    MonitoredItemCreateRequest itemToCreate = itemsToCreate[operation.Index];

                    MonitoringFilterResult filterError = null;
                    IMonitoredItem monitoredItem = null;

                    errors[operation.Index] = CreateMonitoredItem(
                        systemContext,
                        operation.Source,
                        subscriptionId,
                        publishingInterval,
                        context.DiagnosticsMask,
                        timestampsToReturn,
                        itemToCreate,
                        ref globalIdCounter,
                        out filterError,
                        out monitoredItem);

                    // save any filter error details.
                    filterErrors[operation.Index] = filterError;

                    if (ServiceResult.IsBad(errors[operation.Index]))
                    {
                        continue;
                    }

                    // save the monitored item.
                    monitoredItems[operation.Index] = monitoredItem;
                }
            }
        }

        /// <summary>
        /// Validates a data change filter provided by the client.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="source">The node being monitored.</param>
        /// <param name="attributeId">The attribute being monitored.</param>
        /// <param name="requestedFilter">The requested monitoring filter.</param>
        /// <param name="filter">The validated data change filter.</param>
        /// <param name="range">The EU range associated with the value if required by the filter.</param>
        /// <returns>Any error condition. Good if no errors occurred.</returns>
        protected ServiceResult ValidateDataChangeFilter(
            ISystemContext context,
            NodeState source,
            uint attributeId,
            ExtensionObject requestedFilter,
            out DataChangeFilter filter,
            out Range range)
        {
            filter = null;
            range = null;

            // check for valid filter type.
            filter = requestedFilter.Body as DataChangeFilter;

            if (filter == null)
            {
                return StatusCodes.BadMonitoredItemFilterUnsupported;
            }

            // only supported for value attributes.
            if (attributeId != Attributes.Value)
            {
                return StatusCodes.BadMonitoredItemFilterUnsupported;
            }

            // only supported for variables.
            BaseVariableState variable = source as BaseVariableState;
            
            if (variable == null)
            {
                return StatusCodes.BadMonitoredItemFilterUnsupported;
            }

            // check the datatype.
            if (filter.DeadbandType != (uint)DeadbandType.None)
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(variable.DataType, Server.TypeTree);

                if (!TypeInfo.IsNumericType(builtInType))
                {
                    return StatusCodes.BadMonitoredItemFilterUnsupported;
                }
            }

            // validate filter.
            ServiceResult error = filter.Validate();

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            if (filter.DeadbandType ==(uint)DeadbandType.Percent)
            {
                BaseVariableState euRange = variable.FindChild(context, BrowseNames.EURange) as BaseVariableState;

                if (euRange == null)
                {
                    return StatusCodes.BadMonitoredItemFilterUnsupported;
                }

                range = euRange.Value as Range;
                
                if (range == null)
                {
                    return StatusCodes.BadMonitoredItemFilterUnsupported;
                }
            }

            // all good.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new set of monitored items for a set of variables.
        /// </summary>
        /// <remarks>
        /// This method only handles data change subscriptions. Event subscriptions are created by the SDK.
        /// </remarks>
        protected virtual ServiceResult CreateMonitoredItem(
            ISystemContext context,
            NodeState source,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest itemToCreate,
            ref long globalIdCounter,
            out MonitoringFilterResult filterError,
            out IMonitoredItem monitoredItem)
        {
            filterError = null;
            monitoredItem = null;
            ServiceResult error = null;
            
            // read initial value.
            DataValue initialValue = new DataValue();

            initialValue.Value = null;
            initialValue.ServerTimestamp = DateTime.UtcNow;
            initialValue.SourceTimestamp = DateTime.MinValue;
            initialValue.StatusCode = StatusCodes.Good;

            error = source.ReadAttribute(
                context,
                itemToCreate.ItemToMonitor.AttributeId,
                itemToCreate.ItemToMonitor.ParsedIndexRange,
                itemToCreate.ItemToMonitor.DataEncoding,
                initialValue);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }
            
            // validate parameters.
            MonitoringParameters parameters = itemToCreate.RequestedParameters;

            // validate the data change filter.
            DataChangeFilter filter = null;
            Range range = null;

            if (!ExtensionObject.IsNull(parameters.Filter))
            {
                error = ValidateDataChangeFilter(
                    context,
                    source,
                    itemToCreate.ItemToMonitor.AttributeId,
                    parameters.Filter,
                    out filter,
                    out range);

                if (ServiceResult.IsBad(error))
                {
                    return error;
                }
            }

            // create monitored node.
            MonitoredNode monitoredNode = source.Handle as MonitoredNode;

            if (monitoredNode == null)
            {
                source.Handle = monitoredNode = new MonitoredNode(m_server, this, source);
            }

            // create a globally unique identifier.
            uint monitoredItemId = Utils.IncrementIdentifier(ref globalIdCounter);

            // determine the sampling interval.
            double samplingInterval = itemToCreate.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = publishingInterval;
            }

            // check if the variable needs to be sampled.
            bool samplingRequired = false;

            if (itemToCreate.ItemToMonitor.AttributeId == Attributes.Value)
            {
                BaseVariableState variable = source as BaseVariableState;

                if (variable.MinimumSamplingInterval > 0)
                {
                    samplingInterval = CalculateSamplingInterval(variable, samplingInterval);
                    samplingRequired = true;
                }
            }

            // create the item.
            DataChangeMonitoredItem datachangeItem = monitoredNode.CreateDataChangeItem(
                context,
                monitoredItemId,
                itemToCreate.ItemToMonitor.AttributeId,
                itemToCreate.ItemToMonitor.ParsedIndexRange,
                itemToCreate.ItemToMonitor.DataEncoding,
                diagnosticsMasks,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                samplingInterval,
                itemToCreate.RequestedParameters.QueueSize,
                itemToCreate.RequestedParameters.DiscardOldest,
                filter,
                range,
                false);

            if (samplingRequired)
            {
                CreateSampledItem(samplingInterval, datachangeItem);
            }

            // report the initial value.
            datachangeItem.QueueValue(initialValue, null);

            // do any post processing.
            OnCreateMonitoredItem(context, itemToCreate, monitoredNode, datachangeItem);

            // update monitored item list.
            monitoredItem = datachangeItem;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Calculates the sampling interval.
        /// </summary>
        private double CalculateSamplingInterval(BaseVariableState variable, double samplingInterval)
        {
            if (samplingInterval < variable.MinimumSamplingInterval)
            {
                samplingInterval = variable.MinimumSamplingInterval;
            }

            if ((samplingInterval % m_minimumSamplingInterval) != 0)
            {
                samplingInterval = Math.Truncate(samplingInterval/m_minimumSamplingInterval);
                samplingInterval += 1;
                samplingInterval *= m_minimumSamplingInterval;
            }

            return samplingInterval;
        }
        
        /// <summary>
        /// Creates a new sampled item.
        /// </summary>
        private void CreateSampledItem(double samplingInterval, DataChangeMonitoredItem monitoredItem)
        {
            m_sampledItems.Add(monitoredItem);

            if (m_samplingTimer == null)
            {
                m_samplingTimer = new Timer(DoSample, null, (int)m_minimumSamplingInterval, (int)m_minimumSamplingInterval);
            }
        }
        
        /// <summary>
        /// Deletes a sampled item.
        /// </summary>
        private void DeleteSampledItem(DataChangeMonitoredItem monitoredItem)
        {
            for (int ii = 0; ii < m_sampledItems.Count; ii++)
            {
                if (Object.ReferenceEquals(monitoredItem, m_sampledItems[ii]))
                {
                    m_sampledItems.RemoveAt(ii);
                    break;
                }
            }

            if (m_sampledItems.Count == 0)
            {
                if (m_samplingTimer != null)
                {
                    m_samplingTimer.Dispose();
                    m_samplingTimer = null;
                }
            }
        }

        /// <summary>
        /// Polls each monitored item which requires sample. 
        /// </summary>
        private void DoSample(object state)
        {
            try
            {
                lock (m_lock)
                {
                    for (int ii = 0; ii < m_sampledItems.Count; ii++)
                    {
                        DataChangeMonitoredItem monitoredItem = m_sampledItems[ii];

                        if (monitoredItem.TimeToNextSample < m_minimumSamplingInterval)
                        {
                            monitoredItem.ValueChanged(SystemContext);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error during diagnostics scan.");
            }
        }

        /// <summary>
        /// Does any processing after a monitored item is created.
        /// </summary>
        protected virtual void OnCreateMonitoredItem(
            ISystemContext systemContext,
            MonitoredItemCreateRequest itemToCreate,
            MonitoredNode monitoredNode, 
            DataChangeMonitoredItem monitoredItem)
        {
            // does nothing.
        }

        /// <summary>
        /// Modifies the parameters for a set of monitored items.
        /// </summary>
        public virtual void ModifyMonitoredItems(
            OperationContext                  context, 
            TimestampsToReturn                timestampsToReturn, 
            IList<IMonitoredItem>             monitoredItems, 
            IList<MonitoredItemModifyRequest> itemsToModify, 
            IList<ServiceResult>              errors, 
            IList<MonitoringFilterResult>     filterErrors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);

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

                    // modify the monitored item.
                    MonitoringFilterResult filterError = null;

                    errors[ii] = ModifyMonitoredItem(
                        systemContext,
                        context.DiagnosticsMask,
                        timestampsToReturn,
                        monitoredItems[ii],
                        itemToModify,
                        out filterError);

                    // save any filter error details.
                    filterErrors[ii] = filterError;
                }
            }
        }

        /// <summary>
        /// Modifies the parameters for a monitored item.
        /// </summary>
        protected virtual ServiceResult ModifyMonitoredItem(
            ISystemContext context,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            IMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify,
            out MonitoringFilterResult filterError)
        {
            filterError = null;
            ServiceResult error = null;

            // check for valid handle.
            MonitoredNode monitoredNode = monitoredItem.ManagerHandle as MonitoredNode;

            if (monitoredNode == null)
            {
                return ServiceResult.Good;
            }

            if (IsHandleInNamespace(monitoredNode.Node) == null)
            {
                return ServiceResult.Good;
            }

            // owned by this node manager.
            itemToModify.Processed = true;

            // check for valid monitored item.
            DataChangeMonitoredItem datachangeItem = monitoredItem as DataChangeMonitoredItem;

            // validate parameters.
            MonitoringParameters parameters = itemToModify.RequestedParameters;

            // validate the data change filter.
            DataChangeFilter filter = null;
            Range range = null;

            if (!ExtensionObject.IsNull(parameters.Filter))
            {
                error = ValidateDataChangeFilter(
                    context,
                    monitoredNode.Node,
                    datachangeItem.AttributeId,
                    parameters.Filter,
                    out filter,
                    out range);

                if (ServiceResult.IsBad(error))
                {
                    return error;
                }
            }

            double previousSamplingInterval = datachangeItem.SamplingInterval;
            
            // check if the variable needs to be sampled.
            double samplingInterval = itemToModify.RequestedParameters.SamplingInterval;

            if (datachangeItem.AttributeId == Attributes.Value)
            {
                BaseVariableState variable = monitoredNode.Node as BaseVariableState;

                if (variable.MinimumSamplingInterval > 0)
                {
                    samplingInterval = CalculateSamplingInterval(variable, samplingInterval);
                }
            }

            // modify the monitored item parameters.
            error = datachangeItem.Modify(
                diagnosticsMasks,
                timestampsToReturn,
                itemToModify.RequestedParameters.ClientHandle,
                samplingInterval,
                itemToModify.RequestedParameters.QueueSize,
                itemToModify.RequestedParameters.DiscardOldest,
                filter,
                range);

            // do any post processing.
            OnModifyMonitoredItem(
                context, 
                itemToModify, 
                monitoredNode, 
                datachangeItem,
                previousSamplingInterval);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Does any processing after a monitored item is created.
        /// </summary>
        protected virtual void OnModifyMonitoredItem(
            ISystemContext systemContext,
            MonitoredItemModifyRequest itemToModify,
            MonitoredNode monitoredNode,
            DataChangeMonitoredItem monitoredItem,
            double previousSamplingInterval)
        {
            // does nothing.
        }

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        public virtual void DeleteMonitoredItems(
            OperationContext     context, 
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>          processedItems, 
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);

            lock (Lock)
            {
                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }

                    // delete the monitored item.
                    bool processed = false;

                    errors[ii] = DeleteMonitoredItem(
                        systemContext,
                        monitoredItems[ii],
                        out processed);

                    // indicate whether it was processed or not.
                    processedItems[ii] = processed;
                }
            }
        }

        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        protected virtual ServiceResult DeleteMonitoredItem(
            ISystemContext context,
            IMonitoredItem monitoredItem,
            out bool processed)
        {
            processed = false;

            // check for valid handle.
            MonitoredNode monitoredNode = monitoredItem.ManagerHandle as MonitoredNode;

            if (monitoredNode == null)
            {
                return ServiceResult.Good;
            }

            if (IsHandleInNamespace(monitoredNode.Node) == null)
            {
                return ServiceResult.Good;
            }

            // owned by this node manager.
            processed = true;

            // get the  source.
            NodeState source = monitoredNode.Node;

            // check for valid monitored item.
            DataChangeMonitoredItem datachangeItem = monitoredItem as DataChangeMonitoredItem;
            
            // check if the variable needs to be sampled.
            if (datachangeItem.AttributeId == Attributes.Value)
            {
                BaseVariableState variable = monitoredNode.Node as BaseVariableState;

                if (variable.MinimumSamplingInterval > 0)
                {
                    DeleteSampledItem(datachangeItem);
                }
            }

            // remove item.
            monitoredNode.DeleteItem(datachangeItem);

            // do any post processing.
            OnDeleteMonitoredItem(context, monitoredNode, datachangeItem);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Does any processing after a monitored item is deleted.
        /// </summary>
        protected virtual void OnDeleteMonitoredItem(
            ISystemContext systemContext,
            MonitoredNode monitoredNode,
            DataChangeMonitoredItem monitoredItem)
        {
            // does nothing.
        }

        /// <summary>
        /// Changes the monitoring mode for a set of monitored items.
        /// </summary>
        public virtual void SetMonitoringMode(
            OperationContext     context, 
            MonitoringMode       monitoringMode, 
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>          processedItems, 
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = m_systemContext.Copy(context);

            lock (Lock)
            {
                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }

                    // update monitoring mode.
                    bool processed = false;

                    errors[ii] = SetMonitoringMode(
                        systemContext,
                        monitoredItems[ii],
                        monitoringMode,
                        out processed);

                    // indicate whether it was processed or not.
                    processedItems[ii] = processed;
                }
            }
        }

        /// <summary>
        /// Changes the monitoring mode for an item.
        /// </summary>
        protected virtual ServiceResult SetMonitoringMode(
            ISystemContext context,
            IMonitoredItem monitoredItem,
            MonitoringMode monitoringMode, 
            out bool processed)
        {
            processed = false;

            // check for valid handle.
            MonitoredNode monitoredNode = monitoredItem.ManagerHandle as MonitoredNode;

            if (monitoredNode == null)
            {
                return ServiceResult.Good;
            }

            if (IsHandleInNamespace(monitoredNode.Node) == null)
            {
                return ServiceResult.Good;
            }

            // owned by this node manager.
            processed = true;

            // check for valid monitored item.
            DataChangeMonitoredItem datachangeItem = monitoredItem as DataChangeMonitoredItem;

            // update monitoring mode.
            MonitoringMode previousMode = datachangeItem.SetMonitoringMode(monitoringMode);

            // need to provide an immediate update after enabling.
            if (previousMode == MonitoringMode.Disabled && monitoringMode != MonitoringMode.Disabled)
            {
                DataValue initialValue = new DataValue();

                initialValue.Value = null;
                initialValue.ServerTimestamp = DateTime.UtcNow;
                initialValue.SourceTimestamp = DateTime.MinValue;
                initialValue.StatusCode = StatusCodes.Good;

                ServiceResult error = monitoredNode.Node.ReadAttribute(
                    context,
                    datachangeItem.AttributeId,
                    datachangeItem.IndexRange,
                    datachangeItem.DataEncoding,
                    initialValue);

                datachangeItem.QueueValue(initialValue, error);
            }
            
            // do any post processing.
            OnSetMonitoringMode(context, monitoredNode, datachangeItem, previousMode, monitoringMode);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Does any processing after a monitored item is created.
        /// </summary>
        protected virtual void OnSetMonitoringMode(
            ISystemContext systemContext,
            MonitoredNode monitoredNode,
            DataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode currentMode)
        {
            // does nothing.
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private IServerInternal m_server;
        private ServerSystemContext m_systemContext;
        private IList<string> m_namespaceUris;
        private ushort[] m_namespaceIndexes;
        private NodeIdDictionary<NodeState> m_predefinedNodes;
        private List<NodeState> m_rootNotifiers;
        
        private Timer m_samplingTimer;
        private List<DataChangeMonitoredItem> m_sampledItems;
        private double m_minimumSamplingInterval;
        #endregion
    }
}
