/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the type tree for a server. 
    /// </summary>
    public class TypeTable : ITypeTable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        public TypeTable(NamespaceTable namespaceUris)
        {
            m_namespaceUris  = namespaceUris;
            m_referenceTypes = new SortedDictionary<QualifiedName,TypeInfo>();
            m_nodes = new NodeIdDictionary<TypeInfo>();
            m_encodings = new NodeIdDictionary<TypeInfo>();
        }
        #endregion

        #region ITypeTable Methods
        /// <summary cref="ITypeTable.IsKnown(ExpandedNodeId)" />
        public bool IsKnown(ExpandedNodeId typeId)
        {            
            if (NodeId.IsNull(typeId) || typeId.ServerIndex != 0)
            {
                return false;
            }

            NodeId localId = ExpandedNodeId.ToNodeId(typeId, m_namespaceUris);
            
            if (localId == null)
            {
                return false;
            }

            lock (m_lock)
            {
                return m_nodes.ContainsKey(localId);
            }
        }

        /// <summary cref="ITypeTable.IsKnown(NodeId)" />
        public bool IsKnown(NodeId typeId)
        {            
            if (NodeId.IsNull(typeId))
            {
                return false;
            }

            lock (m_lock)
            {
                return m_nodes.ContainsKey(typeId);
            }
        }                
                
        /// <summary cref="ITypeTable.FindSuperType(ExpandedNodeId)" />
        public NodeId FindSuperType(ExpandedNodeId typeId)
        {
            if (NodeId.IsNull(typeId) || typeId.ServerIndex != 0)
            {
                return NodeId.Null;
            }
            
            NodeId localId = ExpandedNodeId.ToNodeId(typeId, m_namespaceUris);
            
            if (localId == null)
            {
                return NodeId.Null;
            }

            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(localId, out typeInfo))
                {
                    return NodeId.Null;
                }

                if (typeInfo.SuperType != null)
                {
                    return typeInfo.SuperType.NodeId;
                }

                return NodeId.Null;
            }
        }
                
        /// <summary cref="ITypeTable.FindSuperType(NodeId)" />
        public NodeId FindSuperType(NodeId typeId)
        {            
            if (typeId == null)
            {
                return NodeId.Null;
            }

            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(typeId, out typeInfo))
                {
                    return NodeId.Null;
                }

                if (typeInfo.SuperType != null)
                {
                    return typeInfo.SuperType.NodeId;
                }

                return NodeId.Null;
            }
        }

        /// <summary cref="ITypeTable.FindSubTypes(ExpandedNodeId)" />
        public IList<NodeId> FindSubTypes(ExpandedNodeId typeId)
        {
            List<NodeId> subtypes = new List<NodeId>();

            if (typeId == null)
            {
                return subtypes;
            }

            NodeId localId = ExpandedNodeId.ToNodeId(typeId, m_namespaceUris);

            if (localId == null)
            {
                return subtypes;
            }

            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (m_nodes.TryGetValue(localId, out typeInfo))
                {
                    typeInfo.GetSubtypes(subtypes);
                }

                return subtypes;
            }
        }

        /// <summary cref="ITypeTable.IsTypeOf(ExpandedNodeId, ExpandedNodeId)" />
        public bool IsTypeOf(ExpandedNodeId subTypeId, ExpandedNodeId superTypeId)
        {
            if (NodeId.IsNull(subTypeId) || subTypeId.ServerIndex != 0)
            {
                return false;
            }
            
            if (NodeId.IsNull(superTypeId) || superTypeId.ServerIndex != 0)
            {
                return false;
            }

            // check for exact match.
            if (subTypeId == superTypeId)
            {
                return true;
            }

            NodeId startId = ExpandedNodeId.ToNodeId(subTypeId, m_namespaceUris);
            
            if (startId == null)
            {
                return false;
            }

            NodeId targetId = ExpandedNodeId.ToNodeId(superTypeId, m_namespaceUris);
            
            if (targetId == null)
            {
                return false;
            }

            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(startId, out typeInfo))
                {
                    return false;
                }

                return typeInfo.IsTypeOf(targetId);
            }
        }

        /// <summary cref="ITypeTable.IsTypeOf(NodeId,NodeId)" />
        public bool IsTypeOf(NodeId subTypeId, NodeId superTypeId)
        {
            // check for null.
            if (subTypeId == null || superTypeId == null)
            {
                return false;
            }

            // check for exact match.
            if (subTypeId == superTypeId)
            {
                return true;
            }
            
            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(subTypeId, out typeInfo))
                {
                    return false;
                }

                return typeInfo.IsTypeOf(superTypeId);
            }
        }
        
        
        /// <summary cref="ITypeTable.FindReferenceTypeName" />
        public QualifiedName FindReferenceTypeName(NodeId referenceTypeId)
        {
            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(referenceTypeId, out typeInfo))
                {
                    return null;
                }

                return typeInfo.BrowseName;
            }
        }

        /// <summary cref="ITypeTable.FindReferenceType" />
        public NodeId FindReferenceType(QualifiedName browseName)
        {
            // check for empty name.
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_referenceTypes.TryGetValue(browseName, out typeInfo))
                {
                    return null;
                }

                return typeInfo.NodeId;
            }
        }
        
        /// <summary cref="ITypeTable.IsEncodingOf(ExpandedNodeId, ExpandedNodeId)" />
        public bool IsEncodingOf(ExpandedNodeId encodingId, ExpandedNodeId datatypeId)
        {
            // check for invalid ids.
            if (NodeId.IsNull(encodingId) || NodeId.IsNull(datatypeId))
            {
                return false;
            }
            
            NodeId localId = ExpandedNodeId.ToNodeId(encodingId, m_namespaceUris);

            if (localId == null)
            {
                return false;
            }

            NodeId localTypeId = ExpandedNodeId.ToNodeId(datatypeId, m_namespaceUris);
            
            if (localTypeId == null)
            {
                return false;
            }

            lock (m_lock)
            {
                // lookup the immediate basetype of the subtype.
                TypeInfo typeInfo = null;

                if (!m_encodings.TryGetValue(localId, out typeInfo))
                {
                    return false;
                }

                // the encoding is a representation of the expected datatype id.
                if (localTypeId == typeInfo.NodeId)
                {
                    return true;
                }

                // check if the encoding is a representation of a subtype of the expected datatype id.
                TypeInfo superTypeInfo = typeInfo.SuperType;

                while (superTypeInfo != null)
                {
                    if (!superTypeInfo.Deleted && superTypeInfo.NodeId == localTypeId)
                    {
                        return true;
                    }

                    superTypeInfo = superTypeInfo.SuperType;
                }

                // no match.
                return false;
            }
        }
        
        /// <summary cref="ITypeTable.IsEncodingFor(NodeId, ExtensionObject)" />
        public bool IsEncodingFor(NodeId expectedTypeId, ExtensionObject value)
        {
            // no match on null values.
            if (value == null)
            {
                return false;
            }

            // may still match if the extension type is an encoding for the expected type.
            if (IsEncodingOf(value.TypeId, expectedTypeId))
            {
                return true;
            }

            // no match.
            return false;
        }

        /// <summary cref="ITypeTable.IsEncodingFor(NodeId, object)" />
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
            NodeId actualTypeId = Opc.Ua.TypeInfo.GetDataTypeId(value);

            // value is valid if the expected datatype is same as or a supertype of the actual datatype
            // for example: expected datatype of 'Integer' matches an actual datatype of 'UInt32'.
            if (IsTypeOf(actualTypeId, expectedTypeId))
            {
                return true;
            }

            // allow matches non-structure values where the actual datatype is a supertype of the expected datatype.
            // for example: expected datatype of 'UtcTime' matches an actual datatype of 'DateTime'.
            if (actualTypeId != DataTypeIds.Structure)
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
        
        /// <summary cref="ITypeTable.FindDataTypeId(ExpandedNodeId)" />
        public NodeId FindDataTypeId(ExpandedNodeId encodingId)            
        {            
            NodeId localId = ExpandedNodeId.ToNodeId(encodingId, m_namespaceUris);

            if (localId == null)
            {
                return NodeId.Null;
            }

            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_encodings.TryGetValue(localId, out typeInfo))
                {
                    return NodeId.Null;
                }

                return typeInfo.NodeId;
            }
        }

        /// <summary cref="ITypeTable.FindDataTypeId(NodeId)" />
        public NodeId FindDataTypeId(NodeId encodingId)            
        {
            lock (m_lock)
            {
                TypeInfo typeInfo = null;

                if (!m_encodings.TryGetValue(encodingId, out typeInfo))
                {
                    return NodeId.Null;
                }

                return typeInfo.NodeId;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Removes all types from the tree.
        /// </summary>
        public void Clear()
        {
            lock (m_lock)
            {
                m_nodes.Clear();
                m_encodings.Clear();
                m_referenceTypes.Clear();
            }
        }

        /// <summary>
        /// Adds a node to the type table if it is a type and does not already exist. If it exists references are updated.
        /// </summary>
        /// <param name="node">The node.</param>
        public void Add(ILocalNode node)
        {
            // ignore null.
            if (node == null || NodeId.IsNull(node.NodeId))
            {
                return;
            }
            
            // ignore non-types.
            if ((node.NodeClass & (NodeClass.ObjectType | NodeClass.VariableType | NodeClass.ReferenceType | NodeClass.DataType)) == 0)
            {
                return;
            }

            NodeId localsuperTypeId = null;

            // find the supertype.
            ExpandedNodeId superTypeId = node.References.FindTarget(ReferenceTypeIds.HasSubtype, true, false, null, 0);

            if (superTypeId != null)
            {
                localsuperTypeId = ExpandedNodeId.ToNodeId(superTypeId, m_namespaceUris);
                
                if (localsuperTypeId == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "A valid supertype identifier is required.");
                }              
            }

            lock (m_lock)
            {
                // lookup the supertype.
                TypeInfo superTypeInfo = null;

                if (localsuperTypeId != null)
                {
                    if (!m_nodes.TryGetValue(localsuperTypeId, out superTypeInfo))
                    {
                        throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "A valid supertype identifier is required.");
                    }
                }

                // create the type info.
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(node.NodeId, out typeInfo))
                {
                    typeInfo = new TypeInfo();
                    m_nodes.Add(node.NodeId, typeInfo);
                }
                
                // update the info.
                typeInfo.NodeId = node.NodeId;
                typeInfo.SuperType = superTypeInfo;
                typeInfo.Deleted = false;

                // add to supertype.
                if (superTypeInfo != null)
                {
                    superTypeInfo.AddSubType(typeInfo);
                }
                                
                // remove the encodings.
                if (typeInfo.Encodings != null)
                {
                    foreach (NodeId encoding in typeInfo.Encodings)
                    {
                        m_encodings.Remove(encoding);
                    }
                }

                // any new encodings.            
                IList<IReference> encodings = node.References.Find(ReferenceTypeIds.HasEncoding, false, false, null);

                if (encodings.Count > 0)
                {
                    typeInfo.Encodings = new NodeId[encodings.Count];

                    for (int ii = 0; ii < encodings.Count; ii++)
                    {
                        typeInfo.Encodings[ii] = ExpandedNodeId.ToNodeId(encodings[ii].TargetId, m_namespaceUris);
                        m_encodings[typeInfo.Encodings[ii]] = typeInfo;
                    }
                }
                
                // add reference type.
                if ((node.NodeClass & NodeClass.ReferenceType) != 0)
                {
                    if (!QualifiedName.IsNull(typeInfo.BrowseName))
                    {
                        m_referenceTypes.Remove(typeInfo.BrowseName);
                    }
                    
                    typeInfo.BrowseName = node.BrowseName;

                    m_referenceTypes[node.BrowseName] = typeInfo;
                }
            }
        }
        
        /// <summary>
        /// Adds type to the table. A browse name is only required if it is a ReferenceType.
        /// </summary>
        /// <param name="subTypeId">The sub type identifier.</param>
        /// <param name="superTypeId">The super type identifier.</param>
        /// <remarks>
        /// Updates the any existing entry.
        /// </remarks>
        public void AddSubtype(NodeId subTypeId, NodeId superTypeId)
        {
            AddSubtype(subTypeId, superTypeId, null);
        }

        /// <summary>
        /// Adds type to the table. A browse name is only required if it is a ReferenceType.
        /// </summary>
        /// <param name="subTypeId">The sub type identifier.</param>
        /// <param name="superTypeId">The super type identifier.</param>
        /// <param name="browseName">Name of the browse.</param>
        /// <remarks>
        /// Updates the any existing entry.
        /// </remarks>
        public void AddReferenceSubtype(NodeId subTypeId, NodeId superTypeId, QualifiedName browseName)
        {
            AddSubtype(subTypeId, superTypeId, browseName);
        }

        /// <summary>
        /// Adds an encoding for an existing data type.
        /// </summary>
        public bool AddEncoding(NodeId dataTypeId, ExpandedNodeId encodingId)
        {
            NodeId localId = ExpandedNodeId.ToNodeId(encodingId, m_namespaceUris);

            if (localId == null)
            {
                return false;
            }

            lock (m_lock)
            {
                TypeInfo typeInfo = null;
                
                if (!m_nodes.TryGetValue(dataTypeId, out typeInfo))
                {
                    return false;
                }

                if (typeInfo.Encodings == null)
                {
                    typeInfo.Encodings = new NodeId[] { localId };
                }
                else
                {
                    NodeId[] encodings = new NodeId[typeInfo.Encodings.Length + 1];
                    System.Array.Copy(typeInfo.Encodings, encodings, typeInfo.Encodings.Length);
                    encodings[encodings.Length - 1] = localId;
                    typeInfo.Encodings = encodings;
                }

                m_encodings[localId] = typeInfo;
                return true;
            }
        }

        /// <summary>
        /// Adds type to the table. A browse name is only required if it is a ReferenceType.
        /// </summary>
        /// <param name="subTypeId">The sub type identifier.</param>
        /// <param name="superTypeId">The super type identifier.</param>
        /// <param name="browseName">Name of the browse.</param>
        /// <remarks>
        /// Updates the any existing entry.
        /// </remarks>
        private void AddSubtype(NodeId subTypeId, NodeId superTypeId, QualifiedName browseName)
        {
            lock (m_lock)
            {
                // lookup the supertype.
                TypeInfo superTypeInfo = null;

                if (!NodeId.IsNull(superTypeId))
                {
                    if (!m_nodes.TryGetValue(superTypeId, out superTypeInfo))
                    {
                        throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "A valid supertype identifier is required.");
                    }
                }

                // create the type info.
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(subTypeId, out typeInfo))
                {
                    typeInfo = new TypeInfo();
                    m_nodes.Add(subTypeId, typeInfo);
                }
                
                // update the info.
                typeInfo.NodeId = subTypeId;
                typeInfo.SuperType = superTypeInfo;
                typeInfo.Deleted = false;

                // add to supertype.
                if (superTypeInfo != null)
                {
                    superTypeInfo.AddSubType(typeInfo);
                }

                // remove the encodings.
                if (typeInfo.Encodings != null)
                {
                    foreach (NodeId encoding in typeInfo.Encodings)
                    {
                        m_encodings.Remove(encoding);
                    }
                }
                
                // add reference type.
                if (!QualifiedName.IsNull(browseName))
                {
                    typeInfo.BrowseName = browseName;
                    m_referenceTypes[browseName] = typeInfo;
                }
            }
        }
        /// <summary>
        /// Removes a subtype.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        public void Remove(ExpandedNodeId typeId)
        {
            if (NodeId.IsNull(typeId) || typeId.ServerIndex != 0)
            {
                return;
            }
            
            NodeId localId = ExpandedNodeId.ToNodeId(typeId, m_namespaceUris);

            if (localId == null)
            {
                return;
            }

            lock (m_lock)
            {
                // remove type.
                TypeInfo typeInfo = null;

                if (!m_nodes.TryGetValue(localId, out typeInfo))
                {
                    return;               
                }

                m_nodes.Remove(localId);

                // setting the flag to deleted ensures references from subtypes are not broken.
                typeInfo.Deleted = true;

                // remove from subtype list.
                if (typeInfo.SuperType != null)
                {
                    typeInfo.SuperType.RemoveSubType(localId);
                }

                // remove encodings.
                if (typeInfo.Encodings != null)
                {
                    for (int ii = 0; ii < typeInfo.Encodings.Length; ii++)
                    {
                        m_encodings.Remove(typeInfo.Encodings[ii]);
                    }
                }
                        
                // remove reference type.
                if (!QualifiedName.IsNull(typeInfo.BrowseName))
                {
                    m_referenceTypes.Remove(typeInfo.BrowseName);
                }
            }
        }
        #endregion

        #region TypeInfo Class
        /// <summary>
        /// Stores the information about an indexed type.
        /// </summary>
        private class TypeInfo
        {
            public bool Deleted;
            public NodeId NodeId;
            public QualifiedName BrowseName;
            public TypeInfo SuperType;
            public NodeId[] Encodings;
            public NodeIdDictionary<TypeInfo> SubTypes;

            /// <summary>
            /// Checks if the type is a subtype of the specified node.
            /// </summary>
            /// <param name="nodeId">The node identifier.</param>
            /// <returns>
            /// 	<c>true</c> if this node is type of the specified NodeId otherwise, <c>false</c>.
            /// </returns>
            public bool IsTypeOf(NodeId nodeId)
            {
                TypeInfo typeInfo = SuperType;
                
                while (typeInfo != null)
                {
                    if (!typeInfo.Deleted && typeInfo.NodeId == nodeId)
                    {
                        return true;
                    }

                    typeInfo = typeInfo.SuperType;
                }

                return false;
            }

            /// <summary>
            /// Adds a subtype to the object.
            /// </summary>
            /// <param name="subType">The subtype</param>
            public void AddSubType(TypeInfo subType)
            {
                if (subType != null)
                {
                    if (SubTypes == null)
                    {
                        SubTypes = new NodeIdDictionary<TypeInfo>();
                    }

                    SubTypes[subType.NodeId] = subType;
                }
            }

            /// <summary>
            /// Remove subtype.
            /// </summary>
            /// <param name="subtypeId">The subtype identifier.</param>
            public void RemoveSubType(NodeId subtypeId)
            {
                if (subtypeId != null && SubTypes != null)
                {
                    SubTypes.Remove(subtypeId);

                    if (SubTypes.Count == 0)
                    {
                        SubTypes = null;
                    }
                }
            }

            /// <summary>
            /// Adds the subtypes to the list.
            /// </summary>
            /// <param name="nodeIds">The node identifiers.</param>
            public void GetSubtypes(List<NodeId> nodeIds)
            {
                if (SubTypes == null)
                {
                    return;
                }

                nodeIds.AddRange(SubTypes.Keys);
            }
        }
        #endregion
        
        #region Private Fields
        private object m_lock = new object();
        private NamespaceTable m_namespaceUris;
        private SortedDictionary<QualifiedName,TypeInfo> m_referenceTypes;
        private NodeIdDictionary<TypeInfo> m_nodes;
        private NodeIdDictionary<TypeInfo> m_encodings;
        #endregion
    }
}
