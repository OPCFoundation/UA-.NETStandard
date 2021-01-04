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
using System.Reflection;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A client side cache of the server's type model.
    /// </summary>
    public class NodeCache : INodeTable, ITypeTable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public NodeCache(Session session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            m_session  = session;
            m_typeTree = new TypeTable(m_session.NamespaceUris);
            m_nodes    = new NodeTable(m_session.NamespaceUris, m_session.ServerUris, m_typeTree);
        }
        #endregion

        #region INodeTable Members
        /// <summary cref="INodeTable.NamespaceUris" />
        public NamespaceTable NamespaceUris
        {
            get { return m_session.NamespaceUris; }
        }
        
        /// <summary cref="INodeTable.ServerUris" />
        public StringTable ServerUris
        {
            get { return m_session.ServerUris; }
        }
        
        /// <summary cref="INodeTable.TypeTree" />
        public ITypeTable TypeTree 
        {
            get { return this; }
        }

        /// <summary cref="INodeTable.Exists(ExpandedNodeId)" />
        public bool Exists(ExpandedNodeId nodeId)
        {
            return Find(nodeId) != null;
        }
        
        /// <summary cref="INodeTable.Find(ExpandedNodeId)" />
        public INode Find(ExpandedNodeId nodeId)
        {
            // check for null.
            if (NodeId.IsNull(nodeId))
            {
                return null;
            }

            // check if node alredy exists.
            INode node = m_nodes.Find(nodeId);

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
                Utils.Trace("Could not fetch node from server: NodeId={0}, Reason='{1}'.", nodeId, e.Message);
                // m_nodes[nodeId] = null;
                return null;
            }
        }
        
        /// <summary cref="INodeTable.Find(ExpandedNodeId,NodeId,bool,bool,QualifiedName)" />
        public INode Find(
            ExpandedNodeId sourceId, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            bool           includeSubtypes, 
            QualifiedName  browseName)
        {
            // find the source.
            Node source = Find(sourceId) as Node;

            if (source == null)
            {
                return null;
            }
            
            // find all references.
            IList<IReference> references = source.ReferenceTable.Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);

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

        /// <summary cref="INodeTable.Find(ExpandedNodeId,NodeId,bool,bool)" />
        public IList<INode> Find(
            ExpandedNodeId sourceId, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            bool           includeSubtypes)
        {            
            List<INode> hits = new List<INode>();

            // find the source.
            Node source = Find(sourceId) as Node;

            if (source == null)
            {
                return hits;
            }

            // find all references.
            IList<IReference> references = source.ReferenceTable.Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
            
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
        /// <summary>
        /// Determines whether a node id is a known type id.
        /// </summary>
        /// <param name="typeId">The type extended identifier.</param>
        /// <returns>
        /// 	<c>true</c> if the specified type id is known; otherwise, <c>false</c>.
        /// </returns>
        public bool IsKnown(ExpandedNodeId typeId)
        {            
            INode type = Find(typeId);

            if (type == null)
            {
                return false;
            }

            return m_typeTree.IsKnown(typeId);
        }

        /// <summary>
        /// Determines whether a node id is a known type id.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <returns>
        /// 	<c>true</c> if the specified type id is known; otherwise, <c>false</c>.
        /// </returns>
        public bool IsKnown(NodeId typeId)
        {            
            INode type = Find(typeId);

            if (type == null)
            {
                return false;
            }

            return m_typeTree.IsKnown(typeId);
        }

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <returns>
        /// A type identifier of the <paramref name="typeId "/>
        /// </returns>
        public NodeId FindSuperType(ExpandedNodeId typeId)
        {
            INode type = Find(typeId);

            if (type == null)
            {
                return null;
            }

            return m_typeTree.FindSuperType(typeId);
        }

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <returns>
        /// The immediate supertype idnetyfier for <paramref name="typeId"/>
        /// </returns>
        public NodeId FindSuperType(NodeId typeId)
        {
            INode type = Find(typeId);

            if (type == null)
            {
                return null;
            }

            return m_typeTree.FindSuperType(typeId);
        }

        /// <summary>
        /// Returns the immediate subtypes for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <returns>
        /// List of type identifiers for <paramref name="typeId"/>
        /// </returns>
        public IList<NodeId> FindSubTypes(ExpandedNodeId typeId)
        {
            ILocalNode type = Find(typeId) as ILocalNode;

            if (type == null)
            {
                return new List<NodeId>();
            }

            List<NodeId> subtypes = new List<NodeId>();

            foreach (IReference reference in type.References.Find(ReferenceTypeIds.HasSubtype, false, true, m_typeTree))
            {
                if (!reference.TargetId.IsAbsolute)
                {
                    subtypes.Add((NodeId)reference.TargetId);
                }
            }

            return subtypes;
        }

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identifier.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="superTypeId"/> is supertype of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
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
                ExpandedNodeId currentId = supertype.References.FindTarget(ReferenceTypeIds.HasSubtype, true, true, m_typeTree, 0);

                if (currentId == superTypeId)
                {
                    return true;
                }

                supertype = Find(currentId) as ILocalNode;
            }
            
            return false;
        }

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identyfier.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="superTypeId"/> is supertype of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
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
                ExpandedNodeId currentId = supertype.References.FindTarget(ReferenceTypeIds.HasSubtype, true, true, m_typeTree, 0);

                if (currentId == superTypeId)
                {
                    return true;
                }

                supertype = Find(currentId) as ILocalNode;
            }
            
            return false;
        }

        /// <summary>
        /// Returns the qualified name for the reference type id.
        /// </summary>
        /// <param name="referenceTypeId">The reference type</param>
        /// <returns>
        /// A name qualified with a namespace for the reference <paramref name="referenceTypeId"/>.
        /// </returns>
        public QualifiedName FindReferenceTypeName(NodeId referenceTypeId)
        {
            return m_typeTree.FindReferenceTypeName(referenceTypeId);
        }

        /// <summary>
        /// Returns the node identifier for the reference type with the specified browse name.
        /// </summary>
        /// <param name="browseName">Browse name of the reference.</param>
        /// <returns>
        /// The identifier for the <paramref name="browseName"/>
        /// </returns>
        public NodeId FindReferenceType(QualifiedName browseName)
        {
            return m_typeTree.FindReferenceType(browseName);
        }

        /// <summary>
        /// Checks if the identifier <paramref name="encodingId"/> represents a that provides encodings
        /// for the <paramref name="datatypeId "/>.
        /// </summary>
        /// <param name="encodingId">The id the encoding node .</param>
        /// <param name="datatypeId">The id of the DataType node.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="encodingId"/> is encoding of the <paramref name="datatypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        public bool IsEncodingOf(ExpandedNodeId encodingId, ExpandedNodeId datatypeId)
        {
            ILocalNode encoding = Find(encodingId) as ILocalNode;

            if (encoding == null)
            {
                return false;
            }
            
            foreach (IReference reference in encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree))
            {
                if (reference.TargetId == datatypeId)
                {
                    return true;
                }
            }

            // no match.
            return false;
        }

        /// <summary>
        /// Determines if the value contained in an extension object <paramref name="value"/> matches the expected data type.
        /// </summary>
        /// <param name="expectedTypeId">The identifier of the expected type .</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// 	<c>true</c> if the value contained in an extension object <paramref name="value"/> matches the
        /// expected data type; otherwise, <c>false</c>.
        /// </returns>
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

            // find data type.
            foreach (IReference reference in encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree))
            {
                if (reference.TargetId == expectedTypeId)
                {
                    return true;
                }
            }

            // no match.
            return false;
        }

        /// <summary>
        /// Determines if the value is an encoding of the <paramref name="value"/>
        /// </summary>
        /// <param name="expectedTypeId">The expected type id.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// 	<c>true</c> the value is an encoding of the <paramref name="value"/>; otherwise, <c>false</c>.
        /// </returns>
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

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <returns></returns>
        public NodeId FindDataTypeId(ExpandedNodeId encodingId)            
        {            
            ILocalNode encoding = Find(encodingId) as ILocalNode;

            if (encoding == null)
            {
                return NodeId.Null;
            }
            
            IList<IReference> references = encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);

            if (references.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(references[0].TargetId, m_session.NamespaceUris);
            }
                
            return NodeId.Null;
        }

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <returns>
        /// The data type for the <paramref name="encodingId"/>
        /// </returns>
        public NodeId FindDataTypeId(NodeId encodingId)            
        {
            ILocalNode encoding = Find(encodingId) as ILocalNode;

            if (encoding == null)
            {
                return NodeId.Null;
            }
            
            IList<IReference> references = encoding.References.Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);

            if (references.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(references[0].TargetId, m_session.NamespaceUris);
            } 
                
            return NodeId.Null;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Loads the UA defined types into the cache.
        /// </summary>
        /// <param name="context">The context.</param>
        public void LoadUaDefinedTypes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();

            var assembly = typeof(ArgumentCollection).GetTypeInfo().Assembly;
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Stack.Generated.Opc.Ua.PredefinedNodes.uanodes", assembly, true);
            
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

        /// <summary>
        /// Removes all nodes from the cache.
        /// </summary>
        public void Clear()
        {
            m_nodes.Clear();
        }

        /// <summary>
        /// Fetches a node from the server and updates the cache.
        /// </summary>
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
                        m_nodes.Attach(target);
                    }

                    // add the reference.
                    source.ReferenceTable.Add(reference.ReferenceTypeId, !reference.IsForward, reference.NodeId);
                }
            }
            catch (Exception e)
            {
                Utils.Trace("Could not fetch references for valid node with NodeId = {0}. Error = {1}", nodeId, e.Message);
            }

            // add to cache.
            m_nodes.Attach(source);

            return source;
        }

        /// <summary>
        /// Adds the supertypes of the node to the cache.
        /// </summary>
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
        
        /// <summary>
        /// Returns the references of the specified node that meet the criteria specified.
        /// </summary>
        public IList<INode> FindReferences(
            ExpandedNodeId nodeId, 
            NodeId         referenceTypeId, 
            bool           isInverse,
            bool           includeSubtypes)
        {            
            IList<INode> targets = new List<INode>();

            Node source = Find(nodeId) as Node;

            if (source == null)
            {
                return targets;
            }

            IList<IReference> references = source.ReferenceTable.Find(
                referenceTypeId, 
                isInverse, 
                includeSubtypes, 
                m_typeTree);

            foreach (IReference reference in references)
            {
                INode target = Find(reference.TargetId);

                if (target != null)
                {
                    targets.Add(target);
                }
            }

            return targets;
        }
        
        /// <summary>
        /// Returns a display name for a node.
        /// </summary>
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

            foreach (IReference reference in target.ReferenceTable.Find(ReferenceTypeIds.Aggregates, true, true, m_typeTree))
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

        /// <summary>
        /// Returns a display name for a node.
        /// </summary>
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

        /// <summary>
        /// Returns a display name for the target of a reference.
        /// </summary>
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

        /// <summary>
        /// Builds the relative path from a type to a node.
        /// </summary>
        public NodeId BuildBrowsePath(ILocalNode node, IList<QualifiedName> browsePath)
        {
            NodeId typeId = null;
           
            browsePath.Add(node.BrowseName);

            return typeId;
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private TypeTable m_typeTree;
        private NodeTable m_nodes;
        #endregion
    }
}
