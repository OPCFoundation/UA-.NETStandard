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
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Opc.Ua.Client
{
    /// <summary>
    /// An implementation of a client side nodecache.
    /// </summary>
    public partial class NodeCache : INodeCache, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public NodeCache(ISession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            m_session = session;
            m_typeTree = new TypeTable(m_session.NamespaceUris);
            m_nodes = new NodeTable(m_session.NamespaceUris, m_session.ServerUris, m_typeTree);
            m_uaTypesLoaded = false;
            m_cacheLock = new ReaderWriterLockSlim();
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_session = null;
                m_cacheLock?.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region INodeTable Members
        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_session.NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris => m_session.ServerUris;

        /// <inheritdoc/>
        public ITypeTable TypeTree => this;

        /// <inheritdoc/>
        public bool Exists(ExpandedNodeId nodeId)
        {
            return Find(nodeId) != null;
        }

        /// <inheritdoc/>
        public INode Find(ExpandedNodeId nodeId)
        {
            // check for null.
            if (NodeId.IsNull(nodeId))
            {
                return null;
            }

            INode node;
            try
            {
                m_cacheLock.EnterReadLock();

                // check if node alredy exists.
                node = m_nodes.Find(nodeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            if (node != null)
            {
                // do not return temporary nodes created after a Browse().
                if (node.GetType() != typeof(Node))
                {
                    return node;
                }
            }

            // fetch node from server.
            try
            {
                return FetchNode(nodeId);
            }
            catch (Exception e)
            {
                Utils.LogError("Could not fetch node from server: NodeId={0}, Reason='{1}'.", nodeId, e.Message);
                // m_nodes[nodeId] = null;
                return null;
            }
        }

        /// <inheritdoc/>
        public IList<INode> Find(IList<ExpandedNodeId> nodeIds)
        {
            // check for null.
            if (nodeIds == null || nodeIds.Count == 0)
            {
                return new List<INode>();
            }

            int count = nodeIds.Count;
            IList<INode> nodes = new List<INode>(count);
            var fetchNodeIds = new ExpandedNodeIdCollection();

            int ii;
            for (ii = 0; ii < count; ii++)
            {
                INode node;
                try
                {
                    m_cacheLock.EnterReadLock();

                    // check if node already exists.
                    node = m_nodes.Find(nodeIds[ii]);
                }
                finally
                {
                    m_cacheLock.ExitReadLock();
                }

                // do not return temporary nodes created after a Browse().
                if (node != null &&
                    node?.GetType() != typeof(Node))
                {
                    nodes.Add(node);
                }
                else
                {
                    nodes.Add(null);
                    fetchNodeIds.Add(nodeIds[ii]);
                }
            }

            if (fetchNodeIds.Count == 0)
            {
                return nodes;
            }

            // fetch missing nodes from server.
            IList<Node> fetchedNodes;
            try
            {
                fetchedNodes = FetchNodes(fetchNodeIds);
            }
            catch (Exception e)
            {
                Utils.LogError("Could not fetch nodes from server: Reason='{0}'.", e.Message);
                // m_nodes[nodeId] = null;
                return nodes;
            }

            ii = 0;
            foreach (Node fetchedNode in fetchedNodes)
            {
                while (ii < count && nodes[ii] != null)
                {
                    ii++;
                }
                if (ii < count && nodes[ii] == null)
                {
                    nodes[ii++] = fetchedNode;
                }
                else
                {
                    Utils.LogError("Inconsistency fetching nodes from server. Not all nodes could be assigned.");
                    break;
                }
            }

            return nodes;
        }

        /// <inheritdoc/>
        public INode Find(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            // find the source.
            Node source = Find(sourceId) as Node;
            if (source == null)
            {
                return null;
            }

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                // find all references.
                references = source.ReferenceTable.Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }


            foreach (IReference reference in references)
            {
                INode target = Find(reference.TargetId);

                if (target == null)
                {
                    continue;
                }

                if (target.BrowseName == browseName)
                {
                    return target;
                }
            }

            // target not found.
            return null;
        }

        /// <inheritdoc/>
        public IList<INode> Find(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes)
        {
            List<INode> hits = new List<INode>();

            // find the source.
            Node source = Find(sourceId) as Node;

            if (source == null)
            {
                return hits;
            }

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                // find all references.
                references = source.ReferenceTable.Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }


            foreach (IReference reference in references)
            {
                INode target = Find(reference.TargetId);

                if (target == null)
                {
                    continue;
                }

                hits.Add(target);
            }

            return hits;
        }
        #endregion

        #region ITypeTable Methods
        /// <inheritdoc/>
        public bool IsKnown(ExpandedNodeId typeId)
        {
            INode type = Find(typeId);

            if (type == null)
            {
                return false;
            }

            try
            {
                m_cacheLock.EnterReadLock();

                return m_typeTree.IsKnown(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public bool IsKnown(NodeId typeId)
        {
            INode type = Find(typeId);

            if (type == null)
            {
                return false;
            }

            try
            {
                m_cacheLock.EnterReadLock();

                return m_typeTree.IsKnown(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public NodeId FindSuperType(ExpandedNodeId typeId)
        {
            INode type = Find(typeId);

            if (type == null)
            {
                return null;
            }

            try
            {
                m_cacheLock.EnterReadLock();

                return m_typeTree.FindSuperType(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

        }

        /// <inheritdoc/>
        public NodeId FindSuperType(NodeId typeId)
        {
            INode type = Find(typeId);

            if (type == null)
            {
                return null;
            }

            try
            {
                m_cacheLock.EnterReadLock();

                return m_typeTree.FindSuperType(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

        }

        /// <inheritdoc/>
        public IList<NodeId> FindSubTypes(ExpandedNodeId typeId)
        {
            ILocalNode type = Find(typeId) as ILocalNode;

            if (type == null)
            {
                return new List<NodeId>();
            }

            List<NodeId> subtypes = new List<NodeId>();
            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                references = type.References.Find(ReferenceTypeIds.HasSubtype, false, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                if (!reference.TargetId.IsAbsolute)
                {
                    subtypes.Add((NodeId)reference.TargetId);
                }
            }

            return subtypes;
        }

        /// <inheritdoc/>
        public bool IsTypeOf(ExpandedNodeId subTypeId, ExpandedNodeId superTypeId)
        {
            if (subTypeId == superTypeId)
            {
                return true;
            }

            ILocalNode subtype = Find(subTypeId) as ILocalNode;

            if (subtype == null)
            {
                return false;
            }

            ILocalNode supertype = subtype;

            while (supertype != null)
            {
                ExpandedNodeId currentId;
                try
                {
                    m_cacheLock.EnterReadLock();

                    currentId = supertype.References.FindTarget(ReferenceTypeIds.HasSubtype, true, true, m_typeTree, 0);
                }
                finally
                {
                    m_cacheLock.ExitReadLock();
                }

                if (currentId == superTypeId)
                {
                    return true;
                }

                supertype = Find(currentId) as ILocalNode;
            }

            return false;
        }

        /// <inheritdoc/>
        public bool IsTypeOf(NodeId subTypeId, NodeId superTypeId)
        {
            if (subTypeId == superTypeId)
            {
                return true;
            }

            ILocalNode subtype = Find(subTypeId) as ILocalNode;

            if (subtype == null)
            {
                return false;
            }

            ILocalNode supertype = subtype;

            while (supertype != null)
            {
                ExpandedNodeId currentId;
                try
                {
                    m_cacheLock.EnterReadLock();

                    currentId = supertype.References.FindTarget(ReferenceTypeIds.HasSubtype, true, true, m_typeTree, 0);
                }
                finally
                {
                    m_cacheLock.ExitReadLock();
                }

                if (currentId == superTypeId)
                {
                    return true;
                }

                supertype = Find(currentId) as ILocalNode;
            }

            return false;
        }

        /// <inheritdoc/>
        public QualifiedName FindReferenceTypeName(NodeId referenceTypeId)
        {
            try
            {
                m_cacheLock.EnterReadLock();

                return m_typeTree.FindReferenceTypeName(referenceTypeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public NodeId FindReferenceType(QualifiedName browseName)
        {
            try
            {
                m_cacheLock.EnterReadLock();

                return m_typeTree.FindReferenceType(browseName);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public bool IsEncodingOf(ExpandedNodeId encodingId, ExpandedNodeId datatypeId)
        {
            ILocalNode encoding = Find(encodingId) as ILocalNode;

            if (encoding == null)
            {
                return false;
            }

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                references = encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                if (reference.TargetId == datatypeId)
                {
                    return true;
                }
            }

            // no match.
            return false;
        }

        /// <inheritdoc/>
        public bool IsEncodingFor(NodeId expectedTypeId, ExtensionObject value)
        {
            // no match on null values.
            if (value == null)
            {
                return false;
            }

            // check for exact match.
            if (expectedTypeId == value.TypeId)
            {
                return true;
            }

            // find the encoding.
            ILocalNode encoding = Find(value.TypeId) as ILocalNode;

            if (encoding == null)
            {
                return false;
            }

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                references = encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            // find data type.
            foreach (IReference reference in references)
            {
                if (reference.TargetId == expectedTypeId)
                {
                    return true;
                }
            }

            // no match.
            return false;
        }

        /// <inheritdoc/>
        public bool IsEncodingFor(NodeId expectedTypeId, object value)
        {
            // null actual datatype matches nothing.
            if (value == null)
            {
                return false;
            }

            // null expected datatype matches everything.
            if (NodeId.IsNull(expectedTypeId))
            {
                return true;
            }

            // get the actual datatype.
            NodeId actualTypeId = TypeInfo.GetDataTypeId(value);

            // value is valid if the expected datatype is same as or a supertype of the actual datatype
            // for example: expected datatype of 'Integer' matches an actual datatype of 'UInt32'.
            if (IsTypeOf(actualTypeId, expectedTypeId))
            {
                return true;
            }

            // allow matches non-structure values where the actual datatype is a supertype of the expected datatype.
            // for example: expected datatype of 'UtcTime' matches an actual datatype of 'DateTime'.
            if (actualTypeId != DataTypes.Structure)
            {
                return IsTypeOf(expectedTypeId, actualTypeId);
            }

            // for structure types must try to determine the subtype.
            ExtensionObject extension = value as ExtensionObject;

            if (extension != null)
            {
                return IsEncodingFor(expectedTypeId, extension);
            }

            // every element in an array must match.
            ExtensionObject[] extensions = value as ExtensionObject[];

            if (extensions != null)
            {
                for (int ii = 0; ii < extensions.Length; ii++)
                {
                    if (!IsEncodingFor(expectedTypeId, extensions[ii]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // can only get here if the value is an unrecognized data type.
            return false;
        }

        /// <inheritdoc/>
        public NodeId FindDataTypeId(ExpandedNodeId encodingId)
        {
            ILocalNode encoding = Find(encodingId) as ILocalNode;

            if (encoding == null)
            {
                return NodeId.Null;
            }

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                references = encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            if (references.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(references[0].TargetId, m_session.NamespaceUris);
            }

            return NodeId.Null;
        }

        /// <inheritdoc/>
        public NodeId FindDataTypeId(NodeId encodingId)
        {
            ILocalNode encoding = Find(encodingId) as ILocalNode;

            if (encoding == null)
            {
                return NodeId.Null;
            }

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                references = encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            if (references.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(references[0].TargetId, m_session.NamespaceUris);
            }

            return NodeId.Null;
        }
        #endregion

        #region INodeCache Methods
        /// <inheritdoc/>
        public void LoadUaDefinedTypes(ISystemContext context)
        {
            if (m_uaTypesLoaded)
            {
                return;
            }

            NodeStateCollection predefinedNodes = new NodeStateCollection();
            var assembly = typeof(ArgumentCollection).GetTypeInfo().Assembly;
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Stack.Generated.Opc.Ua.PredefinedNodes.uanodes", assembly, true);

            try
            {
                m_cacheLock.EnterWriteLock();

                for (int ii = 0; ii < predefinedNodes.Count; ii++)
                {
                    BaseTypeState type = predefinedNodes[ii] as BaseTypeState;

                    if (type == null)
                    {
                        continue;
                    }

                    type.Export(context, m_nodes);
                }
            }
            finally
            {
                m_cacheLock.ExitWriteLock();
            }
            m_uaTypesLoaded = true;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            m_uaTypesLoaded = false;
            try
            {
                m_cacheLock.EnterWriteLock();

                m_nodes.Clear();
            }
            finally
            {
                m_cacheLock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public Node FetchNode(ExpandedNodeId nodeId)
        {
            NodeId localId = ExpandedNodeId.ToNodeId(nodeId, m_session.NamespaceUris);

            if (localId == null)
            {
                return null;
            }

            // fetch node from server.
            Node source = m_session.ReadNode(localId);

            try
            {
                // fetch references from server.
                ReferenceDescriptionCollection references = m_session.FetchReferences(localId);

                try
                {
                    m_cacheLock.EnterUpgradeableReadLock();

                    foreach (ReferenceDescription reference in references)
                    {
                        // create a placeholder for the node if it does not already exist.
                        if (!m_nodes.Exists(reference.NodeId))
                        {
                            // transform absolute identifiers.
                            if (reference.NodeId != null && reference.NodeId.IsAbsolute)
                            {
                                reference.NodeId = ExpandedNodeId.ToNodeId(reference.NodeId, NamespaceUris);
                            }

                            Node target = new Node(reference);

                            InternalWriteLockedAttach(target);
                        }

                        // add the reference.
                        source.ReferenceTable.Add(reference.ReferenceTypeId, !reference.IsForward, reference.NodeId);
                    }
                }
                finally
                {
                    m_cacheLock.ExitUpgradeableReadLock();
                }
            }
            catch (Exception e)
            {
                Utils.LogError("Could not fetch references for valid node with NodeId = {0}. Error = {1}", nodeId, e.Message);
            }

            InternalWriteLockedAttach(source);

            return source;
        }

        /// <inheritdoc/>
        public IList<Node> FetchNodes(IList<ExpandedNodeId> nodeIds)
        {
            int count = nodeIds.Count;
            if (count == 0)
            {
                return new List<Node>();
            }

            NodeIdCollection localIds = new NodeIdCollection(
                nodeIds.Select(nodeId => ExpandedNodeId.ToNodeId(nodeId, m_session.NamespaceUris)));

            // fetch nodes and references from server.
            m_session.ReadNodes(localIds, out IList<Node> sourceNodes, out IList<ServiceResult> readErrors);
            m_session.FetchReferences(localIds, out IList<ReferenceDescriptionCollection> referenceCollectionList, out IList<ServiceResult> fetchErrors);

            int ii = 0;
            for (ii = 0; ii < count; ii++)
            {
                if (ServiceResult.IsBad(readErrors[ii]))
                {
                    continue;
                }

                if (!ServiceResult.IsBad(fetchErrors[ii]))
                {
                    // fetch references from server.
                    ReferenceDescriptionCollection references = referenceCollectionList[ii];

                    foreach (ReferenceDescription reference in references)
                    {
                        try
                        {
                            m_cacheLock.EnterUpgradeableReadLock();

                            // create a placeholder for the node if it does not already exist.
                            if (!m_nodes.Exists(reference.NodeId))
                            {
                                // transform absolute identifiers.
                                if (reference.NodeId != null && reference.NodeId.IsAbsolute)
                                {
                                    reference.NodeId = ExpandedNodeId.ToNodeId(reference.NodeId, NamespaceUris);
                                }

                                Node target = new Node(reference);

                                InternalWriteLockedAttach(target);
                            }
                        }
                        finally
                        {
                            m_cacheLock.ExitUpgradeableReadLock();
                        }

                        // add the reference.
                        sourceNodes[ii].ReferenceTable.Add(reference.ReferenceTypeId, !reference.IsForward, reference.NodeId);
                    }
                }

                InternalWriteLockedAttach(sourceNodes[ii]);
            }

            return sourceNodes;
        }

        /// <inheritdoc/>
        public void FetchSuperTypes(ExpandedNodeId nodeId)
        {
            // find the target node,
            ILocalNode source = Find(nodeId) as ILocalNode;

            if (source == null)
            {
                return;
            }

            // follow the tree.
            ILocalNode subType = source;

            while (subType != null)
            {
                ILocalNode superType = null;

                IList<IReference> references = subType.References.Find(ReferenceTypeIds.HasSubtype, true, true, this);

                if (references != null && references.Count > 0)
                {
                    superType = Find(references[0].TargetId) as ILocalNode;
                }

                subType = superType;
            }
        }

        /// <inheritdoc/>
        public IList<INode> FindReferences(
            ExpandedNodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes)
        {
            IList<INode> targets = new List<INode>();

            Node source = Find(nodeId) as Node;

            if (source == null)
            {
                return targets;
            }

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                references = source.ReferenceTable.Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            var targetIds = new ExpandedNodeIdCollection(
                references.Select(reference => reference.TargetId));

            IList<INode> result = Find(targetIds);

            foreach (INode target in result)
            {
                if (target != null)
                {
                    targets.Add(target);
                }
            }
            return targets;
        }

        /// <inheritdoc/>
        public IList<INode> FindReferences(
            IList<ExpandedNodeId> nodeIds,
            IList<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes)
        {
            IList<INode> targets = new List<INode>();
            if (nodeIds.Count == 0 || referenceTypeIds.Count == 0)
            {
                return targets;
            }
            ExpandedNodeIdCollection targetIds = new ExpandedNodeIdCollection();
            IList<INode> sources = Find(nodeIds);
            foreach (INode source in sources)
            {
                if (!(source is Node node))
                {
                    continue;
                }

                foreach (var referenceTypeId in referenceTypeIds)
                {
                    IList<IReference> references;
                    try
                    {
                        m_cacheLock.EnterReadLock();

                        references = node.ReferenceTable.Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
                    }
                    finally
                    {
                        m_cacheLock.ExitReadLock();
                    }

                    targetIds.AddRange(
                        references.Select(reference => reference.TargetId));
                }
            }

            IList<INode> result = Find(targetIds);
            foreach (INode target in result)
            {
                if (target != null)
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        /// <inheritdoc/>
        public string GetDisplayText(INode node)
        {
            // check for null.
            if (node == null)
            {
                return String.Empty;
            }

            // check for remote node.
            Node target = node as Node;

            if (target == null)
            {
                return node.ToString();
            }

            string displayText = null;

            // use the modelling rule to determine which parent to follow.
            NodeId modellingRule = target.ModellingRule;

            IList<IReference> references;
            try
            {
                m_cacheLock.EnterReadLock();

                references = target.ReferenceTable.Find(ReferenceTypeIds.Aggregates, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                Node parent = Find(reference.TargetId) as Node;

                // use the first parent if modelling rule is new.
                if (modellingRule == Objects.ModellingRule_Mandatory)
                {
                    displayText = GetDisplayText(parent);
                    break;
                }

                // use the type node as the parent for other modelling rules.
                if (parent is VariableTypeNode || parent is ObjectTypeNode)
                {
                    displayText = GetDisplayText(parent);
                    break;
                }
            }

            // prepend the parent display name.
            if (displayText != null)
            {
                return Utils.Format("{0}.{1}", displayText, node);
            }

            // simply use the node name.
            return node.ToString();
        }

        /// <inheritdoc/>
        public string GetDisplayText(ExpandedNodeId nodeId)
        {
            if (NodeId.IsNull(nodeId))
            {
                return String.Empty;
            }

            INode node = Find(nodeId);

            if (node != null)
            {
                return GetDisplayText(node);
            }

            return Utils.Format("{0}", nodeId);
        }

        /// <inheritdoc/>
        public string GetDisplayText(ReferenceDescription reference)
        {
            if (reference == null || NodeId.IsNull(reference.NodeId))
            {
                return String.Empty;
            }

            INode node = Find(reference.NodeId);

            if (node != null)
            {
                return GetDisplayText(node);
            }

            return reference.ToString();
        }

        /// <inheritdoc/>
        public NodeId BuildBrowsePath(ILocalNode node, IList<QualifiedName> browsePath)
        {
            NodeId typeId = null;

            browsePath.Add(node.BrowseName);

            return typeId;
        }
        #endregion

        #region Private Methods
        private void InternalWriteLockedAttach(ILocalNode node)
        {
            try
            {
                m_cacheLock.EnterWriteLock();

                // add to cache.
                m_nodes.Attach(node);
            }
            finally
            {
                m_cacheLock.ExitWriteLock();
            }
        }
        #endregion

        #region Private Fields
        private ReaderWriterLockSlim m_cacheLock = new ReaderWriterLockSlim();
        private ISession m_session;
        private TypeTable m_typeTree;
        private NodeTable m_nodes;
        private bool m_uaTypesLoaded;
        #endregion
    }
}
