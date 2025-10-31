/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the type tree for a server.
    /// </summary>
    public class TypeTable : ITypeTable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        public TypeTable(NamespaceTable namespaceUris)
        {
            m_namespaceUris = namespaceUris;
            m_referenceTypes = [];
            m_nodes = [];
            m_encodings = [];
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public NodeId FindSuperType(NodeId typeId)
        {
            if (typeId == null)
            {
                return NodeId.Null;
            }

            lock (m_lock)
            {
                if (!m_nodes.TryGetValue(typeId, out TypeInfo typeInfo))
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

        /// <inheritdoc/>
        public IList<NodeId> FindSubTypes(ExpandedNodeId typeId)
        {
            var subtypes = new List<NodeId>();

            if (typeId == null)
            {
                return subtypes;
            }

            var localId = ExpandedNodeId.ToNodeId(typeId, m_namespaceUris);

            if (localId == null)
            {
                return subtypes;
            }

            lock (m_lock)
            {
                if (m_nodes.TryGetValue(localId, out TypeInfo typeInfo))
                {
                    typeInfo.GetSubtypes(subtypes);
                }

                return subtypes;
            }
        }

        /// <inheritdoc/>
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
                if (!m_nodes.TryGetValue(subTypeId, out TypeInfo typeInfo))
                {
                    return false;
                }

                return typeInfo.IsTypeOf(superTypeId);
            }
        }

        /// <inheritdoc/>
        public NodeId FindDataTypeId(ExpandedNodeId encodingId)
        {
            var localId = ExpandedNodeId.ToNodeId(encodingId, m_namespaceUris);

            if (localId == null)
            {
                return NodeId.Null;
            }

            lock (m_lock)
            {
                if (!m_encodings.TryGetValue(localId, out TypeInfo typeInfo))
                {
                    return NodeId.Null;
                }

                return typeInfo.NodeId;
            }
        }

        /// <summary>
        /// Adds a node to the type table if it is a type and does not already exist. If it exists references are updated.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void Add(ILocalNode node)
        {
            // ignore null.
            if (node == null || NodeId.IsNull(node.NodeId))
            {
                return;
            }

            // ignore non-types.
            if ((
                    (int)node.NodeClass &
                    (
                        (int)NodeClass.ObjectType |
                        (int)NodeClass.VariableType |
                        (int)NodeClass.ReferenceType |
                        (int)NodeClass.DataType)
                ) == 0)
            {
                return;
            }

            NodeId localsuperTypeId = null;

            // find the supertype.
            ExpandedNodeId superTypeId = node.References
                .FindTarget(ReferenceTypeIds.HasSubtype, true, false, null, 0);

            if (superTypeId != null)
            {
                localsuperTypeId = ExpandedNodeId.ToNodeId(superTypeId, m_namespaceUris);

                if (localsuperTypeId == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdInvalid,
                        "A valid supertype identifier is required.");
                }
            }

            lock (m_lock)
            {
                // lookup the supertype.
                TypeInfo superTypeInfo = null;

                if (localsuperTypeId != null &&
                    !m_nodes.TryGetValue(localsuperTypeId, out superTypeInfo))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdInvalid,
                        "A valid supertype identifier is required.");
                }

                // create the type info.
                if (!m_nodes.TryGetValue(node.NodeId, out TypeInfo typeInfo))
                {
                    typeInfo = new TypeInfo();
                    m_nodes.Add(node.NodeId, typeInfo);
                }

                // update the info.
                typeInfo.NodeId = node.NodeId;
                typeInfo.SuperType = superTypeInfo;
                typeInfo.Deleted = false;

                // add to supertype.
                superTypeInfo?.AddSubType(typeInfo);

                // remove the encodings.
                if (typeInfo.Encodings != null)
                {
                    foreach (NodeId encoding in typeInfo.Encodings)
                    {
                        m_encodings.Remove(encoding);
                    }
                }

                // any new encodings.
                IList<IReference> encodings = node.References
                    .Find(ReferenceTypeIds.HasEncoding, false, false, null);

                if (encodings.Count > 0)
                {
                    typeInfo.Encodings = new NodeId[encodings.Count];

                    for (int ii = 0; ii < encodings.Count; ii++)
                    {
                        typeInfo.Encodings[ii] = ExpandedNodeId.ToNodeId(
                            encodings[ii].TargetId,
                            m_namespaceUris);
                        m_encodings[typeInfo.Encodings[ii]] = typeInfo;
                    }
                }

                // add reference type.
                if (((int)node.NodeClass & (int)NodeClass.ReferenceType) != 0)
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
        public void AddReferenceSubtype(
            NodeId subTypeId,
            NodeId superTypeId,
            QualifiedName browseName)
        {
            AddSubtype(subTypeId, superTypeId, browseName);
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
        /// <exception cref="ServiceResultException"></exception>
        private void AddSubtype(NodeId subTypeId, NodeId superTypeId, QualifiedName browseName)
        {
            lock (m_lock)
            {
                // lookup the supertype.
                TypeInfo superTypeInfo = null;

                if (!NodeId.IsNull(superTypeId) &&
                    !m_nodes.TryGetValue(superTypeId, out superTypeInfo))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdInvalid,
                        "A valid supertype identifier is required.");
                }

                // create the type info.
                if (!m_nodes.TryGetValue(subTypeId, out TypeInfo typeInfo))
                {
                    typeInfo = new TypeInfo();
                    m_nodes.Add(subTypeId, typeInfo);
                }

                // update the info.
                typeInfo.NodeId = subTypeId;
                typeInfo.SuperType = superTypeInfo;
                typeInfo.Deleted = false;

                // add to supertype.
                superTypeInfo?.AddSubType(typeInfo);

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
                    SubTypes ??= [];

                    SubTypes[subType.NodeId] = subType;
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

        private readonly Lock m_lock = new();
        private readonly NamespaceTable m_namespaceUris;
        private readonly SortedDictionary<QualifiedName, TypeInfo> m_referenceTypes;
        private readonly NodeIdDictionary<TypeInfo> m_nodes;
        private readonly NodeIdDictionary<TypeInfo> m_encodings;
    }
}
