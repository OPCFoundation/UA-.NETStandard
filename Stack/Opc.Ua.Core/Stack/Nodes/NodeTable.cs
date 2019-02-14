/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Provides context information to used when evaluating filters.
    /// </summary>
    public interface INodeTable
    {
        /// <summary>
        /// The table of Namespace URIs used by the table.
        /// </summary>
        /// <value>The namespace URIs.</value>
        NamespaceTable NamespaceUris { get; }
        
        /// <summary>
        /// The table of Server URIs used by the table.
        /// </summary>
        /// <value>The server URIs.</value>
        StringTable ServerUris { get; }
        
        /// <summary>
        /// The type model that describes the nodes in the table.
        /// </summary>
        /// <value>The type tree.</value>
        ITypeTable TypeTree { get; }
        
        /// <summary>
        /// Returns true if the node is in the table.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>True if the node is in the table.</returns>
        bool Exists(ExpandedNodeId nodeId);

        /// <summary>
        /// Finds a node in the node set.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>Returns null if the node does not exist.</returns>
        INode Find(ExpandedNodeId nodeId);
        
        /// <summary>
        /// Follows the reference from the source and returns the first target with the specified browse name.
        /// </summary>
        /// <param name="sourceId">The source identifier.</param>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="browseName">Name of the browse.</param>
        /// <returns>
        /// Returns null if the source does not exist or if there is no matching target.
        /// </returns>
        INode Find(
            ExpandedNodeId sourceId, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            bool           includeSubtypes, 
            QualifiedName  browseName);

        /// <summary>
        /// Follows the reference from the source and returns all target nodes.
        /// </summary>
        /// <param name="sourceId">The source identifier.</param>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <returns>
        /// Returns an empty list if the source does not exist or if there are no matching targets.
        /// </returns>
        IList<INode> Find(
            ExpandedNodeId sourceId, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            bool           includeSubtypes);
    }
    
    /// <summary>
    /// A table of nodes.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public class NodeTable : INodeTable, IEnumerable<INode>
    {
        #region Constructors
        /// <summary>
        /// Initializes the object.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        /// <param name="typeTree">The type tree.</param>
        public NodeTable(
            NamespaceTable namespaceUris, 
            StringTable    serverUris, 
            TypeTable      typeTree)
        {
            m_namespaceUris = namespaceUris;
            m_serverUris    = serverUris;
            m_typeTree      = typeTree;
            m_localNodes    = new NodeIdDictionary<ILocalNode>();
            m_remoteNodes   = new SortedDictionary<ExpandedNodeId,RemoteNode>();
        }
        #endregion
 
        #region INodeTable Methods
        /// <summary>
        /// The table of Namespace URIs used by the table.
        /// </summary>
        /// <value>The namespace URIs.</value>
        public NamespaceTable NamespaceUris 
        { 
            get { return m_namespaceUris; } 
        }

        /// <summary>
        /// The table of Server URIs used by the table.
        /// </summary>
        /// <value>The server URIs.</value>
        public StringTable ServerUris 
        { 
            get { return m_serverUris; } 
        }

        /// <summary>
        /// The type model that describes the nodes in the table.
        /// </summary>
        /// <value>The type tree.</value>
        public ITypeTable TypeTree 
        { 
            get { return m_typeTree; } 
        }

        /// <summary>
        /// Returns true if the node is in the table.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns></returns>
        public bool Exists(ExpandedNodeId nodeId)
        {
            return InternalFind(nodeId) != null;
        }

        /// <summary>
        /// Finds a node in the node set.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>Returns null if the node does not exist.</returns>
        public INode Find(ExpandedNodeId nodeId)
        {
            return InternalFind(nodeId);
        }

        /// <summary>
        /// Follows the reference from the source and returns the first target with the specified browse name.
        /// </summary>
        /// <param name="sourceId">The source identifier.</param>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c>  this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="browseName">Name of the browse.</param>
        /// <returns>
        /// Returns null if the source does not exist or if there is no matching target.
        /// </returns>
        public INode Find(
            ExpandedNodeId sourceId, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            bool           includeSubtypes,
            QualifiedName  browseName)
        {
            // find the source.
            INode source = InternalFind(sourceId);

            if (source == null)
            {
                return null;
            }

            ILocalNode sourceNode = source as ILocalNode;
            
            // can't follow references for remote nodes.
            if (sourceNode == null)
            {
                return null;
            }

            // find the references.
            ICollection<IReference> references = sourceNode.References.Find(
                referenceTypeId, 
                isInverse, 
                includeSubtypes, 
                m_typeTree);

            // look for the target.
            foreach (IReference reference in references)
            {
                INode target = InternalFind(reference.TargetId);

                if (target == null)
                {
                    continue;
                }

                if (browseName == null)
                {
                    return target;
                }

                if (browseName == target.BrowseName)
                {
                    return target;
                }
            }

            // target not found.
            return null;
        }

        /// <summary>
        /// Follows the reference from the source and returns all target nodes.
        /// </summary>
        /// <param name="sourceId">The source identifier.</param>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c>  this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <returns>
        /// Returns an empty list if the source does not exist or if there are no matching targets.
        /// </returns>
        public IList<INode> Find(
            ExpandedNodeId sourceId, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            bool           includeSubtypes)
        {
            // create an empty list.
            IList<INode> nodes = new List<INode>();

            // find the source.
            INode source = InternalFind(sourceId);

            if (source == null)
            {
                return nodes;
            }
            
            ILocalNode sourceNode = source as ILocalNode;
            
            // can't follow references for remote nodes.
            if (sourceNode == null)
            {
                return nodes;
            }
            
            // find the references.
            ICollection<IReference> references = sourceNode.References.Find(
                referenceTypeId, 
                isInverse, 
                includeSubtypes, 
                m_typeTree);

            // look for the targets.
            foreach (IReference reference in references)
            {
                INode target = InternalFind(reference.TargetId);

                if (target != null)
                {
                    nodes.Add(target);
                }
            }

            // return list of nodes.
            return nodes;
        }
        #endregion
                
        #region IEnumerable<INode> Members
        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<INode> GetEnumerator()
        {
            List<INode> list = new List<INode>(Count);

            foreach (INode node in m_localNodes.Values)
            {
                list.Add(node);
            }
                        
            foreach (INode node in m_remoteNodes.Values)
            {
                list.Add(node);
            }

            return list.GetEnumerator();
        }
        #endregion

        #region IEnumerable Members
        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The number of nodes in the table.
        /// </summary>
        /// <value>The count.</value>
        public int Count
        {
            get
            {
                return m_localNodes.Count + m_remoteNodes.Count; 
            }
        }

        /// <summary>
        /// Adds a set of nodes to the table.
        /// </summary>
        /// <param name="nodeSet">The node set.</param>
        /// <param name="externalReferences">The external references.</param>
        /// <returns></returns>
        public List<Node> Import(NodeSet nodeSet, IDictionary<NodeId,IList<IReference>> externalReferences)
        {
            List<Node> importedNodes = new List<Node>();

            if (nodeSet == null)
            {
                return importedNodes;
            }

            // add the nodes.
            foreach (Node nodeToImport in nodeSet.Nodes)
            {
                // ignore empty nodes.
                if (nodeToImport == null || NodeId.IsNull(nodeToImport.NodeId))
                {
                    continue;
                }

                Node node = nodeSet.Copy(nodeToImport, m_namespaceUris, m_serverUris);

                // assign a browse name.
                if (QualifiedName.IsNull(node.BrowseName))
                {
                    node.BrowseName = new QualifiedName(node.NodeId.ToString(), 1);
                }

                // assign a display name.
                if (LocalizedText.IsNullOrEmpty(node.DisplayName))
                {
                    node.DisplayName = new LocalizedText(node.BrowseName.Name);
                }
                
                // index references (the node ids in the references were translated by the Copy() call above).
                foreach (ReferenceNode reference in node.References)
                {
                    // ignore invalid references.
                    if (NodeId.IsNull(reference.ReferenceTypeId) || NodeId.IsNull(reference.TargetId))
                    {
                        continue;
                    }

                    // ignore missing targets.
                    ExpandedNodeId targetId = reference.TargetId;

                    if (NodeId.IsNull(targetId))
                    {
                        continue;
                    }

                    // index reference.
                    node.ReferenceTable.Add(reference.ReferenceTypeId, reference.IsInverse, targetId);

                    // see if a remote node needs to be created.
                    if (targetId.ServerIndex != 0)
                    {
                        RemoteNode remoteNode = Find(targetId) as RemoteNode;
                        
                        if (remoteNode == null)
                        {
                            remoteNode = new RemoteNode(this, targetId);
                            InternalAdd(remoteNode);
                        }

                        remoteNode.AddRef();
                    }
                }

                // clear imported references.
                node.References.Clear();
                                
                // add the node.
                InternalAdd(node);
                importedNodes.Add(node);
            }
            
            // import the nodes.
            foreach (Node node in importedNodes)
            {
                // ignore invalid nodes.
                if (node == null || NodeId.IsNull(node.NodeId))
                {
                    continue;
                }

                // add reverse references.
                foreach (IReference reference in node.ReferenceTable)
                {
                    Node targetNode = Find(reference.TargetId) as Node;

                    if (targetNode == null)
                    {
                        if (reference.TargetId.ServerIndex != 0)
                        {
                            continue;
                        }

                        // return the reverse reference to a node outside the table.
                        if (externalReferences != null)
                        {
                            NodeId targetId = ExpandedNodeId.ToNodeId(reference.TargetId, m_namespaceUris);

                            if (targetId == null)
                            {
                                continue;
                            }

                            IList<IReference> referenceList = null;

                            if (!externalReferences.TryGetValue(targetId, out referenceList))
                            {
                                externalReferences[targetId] = referenceList = new List<IReference>();
                            }

                            ReferenceNode reverseReference = new ReferenceNode();

                            reverseReference.ReferenceTypeId = reference.ReferenceTypeId;
                            reverseReference.IsInverse       = !reference.IsInverse;
                            reverseReference.TargetId        = node.NodeId;

                            referenceList.Add(reverseReference);
                        }

                        continue;
                    }
                    
                    // type definition and modelling rule references are one way.
                    if (reference.ReferenceTypeId != ReferenceTypeIds.HasTypeDefinition && reference.ReferenceTypeId != ReferenceTypeIds.HasModellingRule)
                    {
                        targetNode.ReferenceTable.Add(reference.ReferenceTypeId, !reference.IsInverse, node.NodeId);
                    }
                }
                
                // see if it is a type.
                if (m_typeTree != null)
                {
                    m_typeTree.Add(node);
                }
            }

            return importedNodes;
        }
        
        /// <summary>
        /// Creates/updates a node from a ReferenceDescription.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <returns>Node</returns>
        public INode Import(ReferenceDescription reference)
        {
            // find any existing node.
            INode target = Find(reference.NodeId);
                            
            // create a new object.
            if (target == null)
            {
                if (reference.NodeId.ServerIndex != 0)
                {
                    RemoteNode node = new RemoteNode(this, reference.NodeId);
                    InternalAdd(node);
                    target = node;
                }
                else
                {
                    Node node = new Node();

                    node.NodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_namespaceUris);

                    InternalAdd(node);
                    target = node;
                }
            }
            
            // update local node attributes.
            Node targetNode = target as Node;

            if (targetNode != null)
            {
                targetNode.NodeClass   = reference.NodeClass;
                targetNode.BrowseName  = reference.BrowseName;
                targetNode.DisplayName = reference.DisplayName;

                if (!NodeId.IsNull(reference.TypeDefinition))
                {
                    targetNode.ReferenceTable.Add(ReferenceTypeIds.HasTypeDefinition, false, reference.TypeDefinition);
                }

                return targetNode;
            }           
            
            // update remote node attributes.
            RemoteNode remoteNode = target as RemoteNode;

            if (remoteNode != null)
            {
                remoteNode.NodeClass        = reference.NodeClass;
                remoteNode.BrowseName       = reference.BrowseName;
                remoteNode.DisplayName      = reference.DisplayName;
                remoteNode.TypeDefinitionId = reference.TypeDefinition;

                return remoteNode;
            }

            // should never get here.
            return null;
        }

        /// <summary>
        /// Adds a node to the table (takes ownership of the object passed in).
        /// </summary>
        /// <param name="node">The node.</param>
        /// <remarks>
        /// Any existing node is removed.
        /// </remarks>
        public void Attach(ILocalNode node)
        {
            // remove duplicates.
            if (Exists(node.NodeId))
            {
                Remove(node.NodeId);
            }

            // check if importing a node from a XML source (must copy references from References array to ReferenceTable).
            Node serializedNode = node as Node;

            if (serializedNode != null && serializedNode.References.Count > 0 && serializedNode.ReferenceTable.Count == 0)
            {
                // index references.
                foreach (ReferenceNode reference in node.References)
                {
                    // ignore invalid references.
                    if (NodeId.IsNull(reference.ReferenceTypeId) || NodeId.IsNull(reference.TargetId))
                    {
                        continue;
                    }

                    node.References.Add(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);

                    // see if a remote node needs to be created.
                    if (reference.TargetId.ServerIndex != 0)
                    {
                        RemoteNode remoteNode = Find(reference.TargetId) as RemoteNode;
                        
                        if (remoteNode == null)
                        {
                            remoteNode = new RemoteNode(this, reference.TargetId);
                            InternalAdd(remoteNode);
                        }

                        remoteNode.AddRef();
                    }
                }

                // clear unindexed reference list.
                node.References.Clear();
            }

            // add the node to the table.
            InternalAdd(node);
            
            // add reverse references.
            foreach (IReference reference in node.References)
            {
                ILocalNode targetNode = Find(reference.TargetId) as ILocalNode;

                if (targetNode == null)
                {
                    continue;
                }

                // type definition and modelling rule references are one way.
                if (reference.ReferenceTypeId != ReferenceTypeIds.HasTypeDefinition && reference.ReferenceTypeId != ReferenceTypeIds.HasModellingRule)
                {
                    targetNode.References.Add(reference.ReferenceTypeId, !reference.IsInverse, node.NodeId);
                }
            }

            // see if it is a type.
            if (m_typeTree != null)
            {
                m_typeTree.Add(node);
            }
        }

        /// <summary>
        /// Removes node from the table.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>The result of removal.</returns>
        public bool Remove(ExpandedNodeId nodeId)
        {
            // find the target.
            INode source = Find(nodeId);

            if (source == null)
            {
                return false;
            }

            ILocalNode sourceNode = source as ILocalNode;
            
            // can only directly remove local nodes.
            if (sourceNode == null)
            {
                return false;
            }
            
            // remove references.
            foreach (IReference reference in sourceNode.References)
            {
                INode target = InternalFind(reference.TargetId);

                if (target == null)
                {
                    continue;
                }

                // remove remote node if nothing else references it.
                RemoteNode remoteNode = target as RemoteNode;

                if (remoteNode != null)
                {
                    if (remoteNode.Release() == 0)
                    {
                        InternalRemove(remoteNode);
                    }
                    
                    continue;
                }

                // remote inverse references.                  
                ILocalNode targetNode = target as ILocalNode;
                
                if (targetNode != null)
                {
                    targetNode.References.Remove(reference.ReferenceTypeId, reference.IsInverse, sourceNode.NodeId);
                }
            }

            InternalRemove(sourceNode);

            return true;
        }

        /// <summary>
        /// Removes all references from the table.
        /// </summary>
        public void Clear()
        {
            m_localNodes.Clear();
            m_remoteNodes.Clear();
        }        
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Adds the node to the table.
        /// </summary>
        /// <param name="node">The node.</param>
        private void InternalAdd(ILocalNode node)
        {
            if (node == null || node.NodeId == null)
            {
                return;
            }

            m_localNodes.Add(node.NodeId, node);
        }
        
        /// <summary>
        /// Removes the node from the table.
        /// </summary>
        /// <param name="node">The node.</param>
        private void InternalRemove(ILocalNode node)
        {
            if (node == null || node.NodeId == null)
            {
                return;
            }

            m_localNodes.Remove(node.NodeId);
        }
        
        /// <summary>
        /// Adds the remote node to the table.
        /// </summary>
        /// <param name="node">The node.</param>
        private void InternalAdd(RemoteNode node)
        {
            if (node == null || node.NodeId == null)
            {
                return;
            }

            m_remoteNodes[node.NodeId] = node;
        }
                
        /// <summary>
        /// Removes the remote node from the table.
        /// </summary>
        /// <param name="node">The node.</param>
        private void InternalRemove(RemoteNode node)
        {
            if (node == null || node.NodeId == null)
            {
                return;
            }
            
            m_remoteNodes.Remove(node.NodeId);
        }

        /// <summary>
        /// Finds the specified node. Returns null if the node does node exist.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns></returns>
        private INode InternalFind(ExpandedNodeId nodeId)
        {
            if (nodeId == null)
            {
                return null;
            }

            // check for remote node.
            if (nodeId.ServerIndex != 0)
            {
                RemoteNode remoteNode = null;

                if (m_remoteNodes.TryGetValue(nodeId, out remoteNode))
                {
                    return remoteNode;
                }

                return null;
            }            
            

            // convert to locale node id.
            NodeId localId = ExpandedNodeId.ToNodeId(nodeId, m_namespaceUris);

            if (localId == null)
            {
                return null;
            }
            
            ILocalNode node = null;
            
            if (m_localNodes.TryGetValue(localId, out node))
            {
                return node;
            }
                 
            // node not found.
            return null;
        }
        #endregion
        
        #region RemoteNode Class
        /// <summary>
        /// Stores information for a node on a remote server.
        /// </summary>
        private class RemoteNode : INode
        {
            #region Public Interface
            /// <summary>
            /// Initializes the object.
            /// </summary>
            /// <param name="owner">The owner.</param>
            /// <param name="nodeId">The node identifier.</param>
            public RemoteNode(INodeTable owner, ExpandedNodeId nodeId)
            {
                m_nodeId           = nodeId;
                m_refs             = 0;
                m_nodeClass        = NodeClass.Unspecified;
                m_browseName       = new QualifiedName("(Unknown)");
                m_displayName      = new LocalizedText(m_browseName.Name);
                m_typeDefinitionId = null;
            }

            /// <summary>
            /// Adds a reference to the node.
            /// </summary>
            /// <returns>The number of references.</returns>
            public int AddRef()
            {
                return ++m_refs;
            }

            /// <summary>
            /// Removes a reference to the node.
            /// </summary>
            /// <returns>The number of references.</returns>
            public int Release()
            {
                if (m_refs == 0)
                {
                    throw new InvalidOperationException("Cannot decrement reference count below zero.");
                }

                return --m_refs;
            }
            
            /// <summary>
            /// The cached type definition id for the remote node.
            /// </summary>
            /// <value>The type definition identifier.</value>
            public ExpandedNodeId TypeDefinitionId
            {
                get { return m_typeDefinitionId; }
                internal set { m_typeDefinitionId = value; }
            }
            #endregion

            #region INode Members
            /// <summary>
            /// The node identifier.
            /// </summary>
            /// <value>The node identifier.</value>
            public ExpandedNodeId NodeId
            {
                get { return m_nodeId; }
            }

            /// <summary>
            /// The node class.
            /// </summary>
            /// <value>The node class.</value>
            public NodeClass NodeClass
            {
                get { return m_nodeClass; }
                internal set { m_nodeClass = value; }
            }

            /// <summary>
            /// The locale independent browse name.
            /// </summary>
            /// <value>The name of the browse.</value>
            public QualifiedName BrowseName
            {
                get { return m_browseName; }
                internal set { m_browseName = value; }
            }

            /// <summary>
            /// The localized display name.
            /// </summary>
            /// <value>The display name.</value>
            public LocalizedText DisplayName
            {
                get { return m_displayName; }
                internal set { m_displayName = value; }
            }
            #endregion

            #region Private Fields
            private ExpandedNodeId m_nodeId;
            private NodeClass m_nodeClass;
            private QualifiedName m_browseName;
            private LocalizedText m_displayName;
            private ExpandedNodeId m_typeDefinitionId;
            private int m_refs;
            #endregion
        }
        #endregion
                        
        #region Private Fields
        private NodeIdDictionary<ILocalNode> m_localNodes;
        private SortedDictionary<ExpandedNodeId,RemoteNode> m_remoteNodes;
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private TypeTable m_typeTree;
        #endregion
    }
}
