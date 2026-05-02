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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
    [KnownType(typeof(ObjectNode))]
    [KnownType(typeof(ObjectTypeNode))]
    [KnownType(typeof(VariableNode))]
    [KnownType(typeof(VariableTypeNode))]
    [KnownType(typeof(MethodNode))]
    [KnownType(typeof(DataTypeNode))]
    [KnownType(typeof(ReferenceTypeNode))]
    [KnownType(typeof(ViewNode))]
    public class NodeSet : IEnumerable<Node>
    {
        /// <summary>
        /// Creates an empty nodeset.
        /// </summary>
        public NodeSet()
        {
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();
            m_nodes = [];
        }

        /// <summary>
        /// Loads a nodeset from a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        /// <returns>The set of nodes</returns>
        [Obsolete("Use UANodeSet (NodeSet2 format) instead.")]
        [RequiresUnreferencedCode("Uses DataContractSerializer which might need unreferenced code.")]
        [RequiresDynamicCode("Uses DataContractSerializer which might need unreferenced code.")]
        public static NodeSet? Read(Stream istrm)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<NodeSet>();
#pragma warning restore CS0618 // Type or member is obsolete
            using var reader = XmlReader.Create(istrm, CoreUtils.DefaultXmlReaderSettings());
            return serializer.ReadObject(reader) as NodeSet;
        }

        /// <summary>
        /// Write a nodeset to a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        [Obsolete("Use UANodeSet (NodeSet2 format) instead.")]
        [RequiresUnreferencedCode("Uses DataContractSerializer which might need unreferenced code.")]
        [RequiresDynamicCode("Uses DataContractSerializer which might need unreferenced code.")]
        public void Write(Stream istrm)
        {
            var writer = XmlWriter.Create(istrm, CoreUtils.DefaultXmlWriterSettings());
            try
            {
#pragma warning disable CS0618 // Type or member is obsolete
                DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<NodeSet>();
#pragma warning restore CS0618 // Type or member is obsolete
                serializer.WriteObject(writer, this);
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="IEnumerator{T}"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<Node> GetEnumerator()
        {
            return new List<Node>(m_nodes.Values).GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Adds a node to the set.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <remarks>
        /// The NodeId must reference the strings for the node set.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public void Add(Node node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.NodeId.IsNull)
            {
                throw new ArgumentException("A non-null NodeId must be specified.");
            }

            if (m_nodes.ContainsKey(node.NodeId))
            {
                throw new ArgumentException(
                    CoreUtils.Format("NodeID {0} already exists for node: {1}", node.NodeId, node));
            }

            m_nodes.Add(node.NodeId, node);
        }

        /// <summary>
        /// Translates a value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        /// <returns>Translated value.</returns>
        private Variant TranslateValue(
            Variant value,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            TypeInfo typeInfo = value.TypeInfo;

            // do nothing for unknown types.
            if (typeInfo.IsUnknown)
            {
                return value;
            }

            if (typeInfo.IsScalar)
            {
                // check for values containing namespace indexes.
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.NodeId:
                        return Translate(value.GetNodeId(), m_namespaceUris, namespaceUris);
                    case BuiltInType.ExpandedNodeId:
                        return Translate(
                            value.GetExpandedNodeId(),
                            m_namespaceUris,
                            m_serverUris,
                            namespaceUris,
                            serverUris);
                    case BuiltInType.QualifiedName:
                        return Translate(value.GetQualifiedName(), m_namespaceUris, namespaceUris);
                    case BuiltInType.ExtensionObject:
                        return Translate(value.GetExtensionObject(), m_namespaceUris, namespaceUris);
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        return value;
                    default:
                        Debug.Fail($"Unexpected built-in type {typeInfo.BuiltInType}");
                        return value;
                }
            }

            if (typeInfo.IsArray)
            {
                // check for values containing namespace indexes.
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.NodeId:
                        return Translate(value.GetNodeIdArray(), m_namespaceUris, namespaceUris);
                    case BuiltInType.ExpandedNodeId:
                        return Translate(
                            value.GetExpandedNodeIdArray(),
                            m_namespaceUris,
                            m_serverUris,
                            namespaceUris,
                            serverUris);
                    case BuiltInType.QualifiedName:
                        return Translate(value.GetQualifiedNameArray(), m_namespaceUris, namespaceUris);
                    case BuiltInType.ExtensionObject:
                        return Translate(value.GetExtensionObjectArray(), m_namespaceUris, namespaceUris);
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        return value;
                    default:
                        Debug.Fail($"Unexpected built-in type {typeInfo.BuiltInType}");
                        return value;
                }
            }

            // check for values containing namespace indexes.
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.NodeId:
                    return Translate(value.GetNodeIdMatrix(), m_namespaceUris, namespaceUris);
                case BuiltInType.ExpandedNodeId:
                    return Translate(
                        value.GetExpandedNodeIdMatrix(),
                        m_namespaceUris,
                        m_serverUris,
                        namespaceUris,
                        serverUris);
                case BuiltInType.QualifiedName:
                    return Translate(value.GetQualifiedNameMatrix(), m_namespaceUris, namespaceUris);
                case BuiltInType.ExtensionObject:
                    return Translate(value.GetExtensionObjectMatrix(), m_namespaceUris, namespaceUris);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return value;
                default:
                    Debug.Fail($"Unexpected built-in type {typeInfo.BuiltInType}");
                    return value;
            }
        }

        /// <summary>
        /// Adds a node to the set after translating all namespace/server indexes in attributes or references.
        /// </summary>
        /// <param name="nodeToExport">The node to export.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        /// <returns>The node.</returns>
        /// <remarks>
        /// Does not add references.
        /// </remarks>
        public Node Add(
            ILocalNode nodeToExport,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            var node = Node.Copy(nodeToExport)!;

            node.NodeId = Translate(nodeToExport.NodeId, m_namespaceUris, namespaceUris);
            node.BrowseName = Translate(nodeToExport.BrowseName, m_namespaceUris, namespaceUris);

            switch (nodeToExport)
            {
                case VariableNode variableToExport:
                    var variableNode = (VariableNode)node;
                    variableNode.Value = TranslateValue(
                        variableNode.Value,
                        namespaceUris,
                        serverUris);

                    variableNode.DataType = Translate(
                        variableToExport.DataType,
                        m_namespaceUris,
                        namespaceUris);
                    break;
                case VariableTypeNode variableTypeToExport:
                    var variableTypeNode = (VariableTypeNode)node;
                    variableTypeNode.Value = TranslateValue(
                        variableTypeNode.Value,
                        namespaceUris,
                        serverUris);

                    variableTypeNode.DataType = Translate(
                        variableTypeToExport.DataType,
                        m_namespaceUris,
                        namespaceUris);
                    break;
            }

            var referenceList = node.References.ToList();
            foreach (IReference referenceToExport in nodeToExport.References)
            {
                var reference = new ReferenceNode
                {
                    ReferenceTypeId = Translate(
                        referenceToExport.ReferenceTypeId,
                        m_namespaceUris,
                        namespaceUris),
                    IsInverse = referenceToExport.IsInverse,
                    TargetId = Translate(
                        referenceToExport.TargetId,
                        m_namespaceUris,
                        m_serverUris,
                        namespaceUris,
                        serverUris)
                };
                referenceList.Add(reference);
            }

            node.References = referenceList;
            Add(node);

            return node;
        }

        /// <summary>
        /// Translates a reference and adds it to the specified node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="referenceToExport">The reference to export.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        public void AddReference(
            Node node,
            ReferenceNode referenceToExport,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            var reference = new ReferenceNode
            {
                ReferenceTypeId = Translate(
                    referenceToExport.ReferenceTypeId,
                    m_namespaceUris,
                    namespaceUris),
                IsInverse = referenceToExport.IsInverse,
                TargetId = Translate(
                    referenceToExport.TargetId,
                    m_namespaceUris,
                    m_serverUris,
                    namespaceUris,
                    serverUris)
            };

            node.References = node.References.AddItem(reference);
        }

        /// <summary>
        /// Removes a node from the set.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>The result of the removal.</returns>
        /// <remarks>
        /// The NodeId must reference the strings for the node set.
        /// </remarks>
        public bool Remove(NodeId nodeId)
        {
            return m_nodes.Remove(nodeId);
        }

        /// <summary>
        /// Returns true if the node exists in the nodeset.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>
        /// <c>true</c> if the node exists in the nodeset;
        /// otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// The NodeId must reference the strings for the node set.
        /// </remarks>
        public bool Contains(NodeId nodeId)
        {
            return m_nodes.ContainsKey(nodeId);
        }

        /// <summary>
        /// Returns the node in the set.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>The node in the set.</returns>
        /// <remarks>
        /// The NodeId must reference the strings for the node set.
        /// </remarks>
        public Node? Find(NodeId nodeId)
        {
            if (m_nodes.TryGetValue(nodeId, out Node? node))
            {
                return node;
            }

            return null;
        }

        /// <summary>
        /// Returns the node in the set.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <returns>The node in the set.</returns>
        /// <remarks>
        /// The NodeId namespace is translated before the node is looked up.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodeId"/> is <c>null</c>.</exception>
        public Node? Find(NodeId nodeId, NamespaceTable namespaceUris)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }

            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            // check for unknown namespace index.
            string? ns = namespaceUris.GetString(nodeId.NamespaceIndex);

            if (ns == null)
            {
                return null;
            }

            // check for unknown namespace uri.
            int nsIndex = m_namespaceUris.GetIndex(ns);

            if (nsIndex < 0)
            {
                return null;
            }

            // create translated node identifier.
            NodeId localId = nodeId.WithNamespaceIndex((ushort)nsIndex);

            // look up node.
            if (m_nodes.TryGetValue(localId, out Node? node))
            {
                return node;
            }

            return null;
        }

        /// <summary>
        /// Translates all namespace/server indexes in attributes or references and returns a copy of the node.
        /// </summary>
        /// <param name="nodeToImport">The node to import.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        /// <returns>Translated copy of the node.</returns>
        /// <remarks>
        /// Only imports references stored in the References collection.
        /// </remarks>
        public Node Copy(Node nodeToImport, NamespaceTable namespaceUris, StringTable serverUris)
        {
            var node = Node.Copy(nodeToImport)!;

            node.NodeId = Translate(nodeToImport.NodeId, namespaceUris, m_namespaceUris);
            node.BrowseName = Translate(nodeToImport.BrowseName, namespaceUris, m_namespaceUris);

            if (nodeToImport is VariableNode variableToImport)
            {
                var variable = (VariableNode)node;

                variable.DataType = Translate(
                    variableToImport.DataType,
                    namespaceUris,
                    m_namespaceUris);

                if (!variableToImport.Value.IsNull)
                {
                    variable.Value = ImportValue(
                        variableToImport.Value,
                        namespaceUris,
                        serverUris);
                }
            }

            if (nodeToImport is VariableTypeNode variableTypeToImport)
            {
                var variableType = (VariableTypeNode)node;

                variableType.DataType = Translate(
                    variableTypeToImport.DataType,
                    namespaceUris,
                    m_namespaceUris);

                if (!variableTypeToImport.Value.IsNull)
                {
                    variableType.Value = ImportValue(
                        variableTypeToImport.Value,
                        namespaceUris,
                        serverUris);
                }
            }

            var referenceList = node.References.ToList();
            foreach (ReferenceNode referenceToImport in nodeToImport.References)
            {
                var reference = new ReferenceNode
                {
                    ReferenceTypeId = Translate(
                        referenceToImport.ReferenceTypeId,
                        namespaceUris,
                        m_namespaceUris),
                    IsInverse = referenceToImport.IsInverse,
                    TargetId = Translate(
                        referenceToImport.TargetId,
                        namespaceUris,
                        serverUris,
                        m_namespaceUris,
                        m_serverUris)
                };

                referenceList.Add(reference);
            }
            node.References = referenceList;
            return node;
        }

        /// <summary>
        /// Recursively imports any NodeIds or ExpandedNodeIds contained in a value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        private Variant ImportValue(
            Variant value,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            TypeInfo typeInfo = value.TypeInfo;

            // do nothing for unknown types.
            if (typeInfo.IsUnknown)
            {
                return value;
            }

            if (typeInfo.IsScalar)
            {
                // check for values containing namespace indexes.
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.NodeId:
                        return Import(value.GetNodeId(), namespaceUris);
                    case BuiltInType.ExpandedNodeId:
                        return Import(
                            value.GetExpandedNodeId(),
                            namespaceUris,
                            serverUris);
                    case BuiltInType.QualifiedName:
                        return Translate(value.GetQualifiedName(), namespaceUris, m_namespaceUris);
                    case BuiltInType.ExtensionObject:
                        return Translate(value.GetExtensionObject(), namespaceUris, m_namespaceUris);
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        return value;
                    default:
                        Debug.Fail($"Unexpected built-in type {typeInfo.BuiltInType}");
                        return value;
                }
            }
            if (typeInfo.IsArray)
            {
                // check for values containing namespace indexes.
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.NodeId:
                        return Translate(value.GetNodeIdArray(), namespaceUris, m_namespaceUris);
                    case BuiltInType.ExpandedNodeId:
                        return Translate(
                            value.GetExpandedNodeIdArray(),
                            namespaceUris,
                            serverUris,
                            m_namespaceUris,
                            m_serverUris);
                    case BuiltInType.QualifiedName:
                        return Translate(value.GetQualifiedNameArray(), namespaceUris, m_namespaceUris);
                    case BuiltInType.ExtensionObject:
                        return Translate(value.GetExtensionObjectArray(), namespaceUris, m_namespaceUris);
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        return value;
                    default:
                        Debug.Fail($"Unexpected built-in type {typeInfo.BuiltInType}");
                        return value;
                }
            }

            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.NodeId:
                    return Translate(value.GetNodeIdMatrix(), namespaceUris, m_namespaceUris);
                case BuiltInType.ExpandedNodeId:
                    return Translate(
                        value.GetExpandedNodeIdMatrix(),
                        namespaceUris,
                        serverUris,
                        m_namespaceUris,
                        m_serverUris);
                case BuiltInType.QualifiedName:
                    return Translate(value.GetQualifiedNameMatrix(), namespaceUris, m_namespaceUris);
                case BuiltInType.ExtensionObject:
                    return Translate(value.GetExtensionObjectMatrix(), namespaceUris, m_namespaceUris);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return value;
                default:
                    Debug.Fail($"Unexpected built-in type {typeInfo.BuiltInType}");
                    return value;
            }
        }

        /// <summary>
        /// Updates the nodeset string tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        public NodeId Export(NodeId nodeId, NamespaceTable namespaceUris)
        {
            return Translate(nodeId, m_namespaceUris, namespaceUris);
        }

        /// <summary>
        /// Updates the specified namespace tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        public NodeId Import(NodeId nodeId, NamespaceTable namespaceUris)
        {
            return Translate(nodeId, namespaceUris, m_namespaceUris);
        }

        /// <summary>
        /// Updates the nodeset string tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        /// <returns>A  NodeId that references those tables.</returns>
        public ExpandedNodeId Export(
            ExpandedNodeId nodeId,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            return Translate(nodeId, m_namespaceUris, m_serverUris, namespaceUris, serverUris);
        }

        /// <summary>
        /// Updates the specified string tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        public ExpandedNodeId Import(
            ExpandedNodeId nodeId,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            return Translate(nodeId, namespaceUris, serverUris, m_namespaceUris, m_serverUris);
        }

        /// <summary>
        /// The table of namespaces.
        /// </summary>
        [DataMember(Name = "NamespaceUris", Order = 1)]
        internal ArrayOf<string> NamespaceUris
        {
            get => [.. m_namespaceUris.ToArrayOf()];
            set
            {
                if (value.IsNull)
                {
                    m_namespaceUris = new NamespaceTable();
                }
                else
                {
                    m_namespaceUris = new NamespaceTable(value.ToArray()!);
                }
            }
        }

        /// <summary>
        /// The table of servers.
        /// </summary>
        [DataMember(Name = "ServerUris", Order = 2)]
        internal ArrayOf<string> ServerUris
        {
            get => [.. m_serverUris.ToArrayOf()];
            set
            {
                if (value.IsNull)
                {
                    m_serverUris = new StringTable();
                }
                else
                {
                    m_serverUris = new StringTable(value.ToArray()!);
                }
            }
        }

        /// <summary>
        /// The table of nodes.
        /// </summary>
        [DataMember(Name = "Nodes", Order = 3)]
        internal ArrayOf<Node> Nodes
        {
            get => [.. m_nodes.Values];
            set
            {
                m_nodes = [];
                foreach (Node node in value)
                {
                    m_nodes[node.NodeId] = node;
                }
            }
        }

        /// <summary>
        /// Updates the nodeset string tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static NodeId Translate(
            NodeId nodeId,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            if (nodeId.IsNull)
            {
                return nodeId;
            }

            ushort namespaceIndex = 0;

            if (nodeId.NamespaceIndex > 0)
            {
                string? uri = sourceNamespaceUris.GetString(nodeId.NamespaceIndex);

                int index = targetNamespaceUris.GetIndex(uri!);

                if (index == -1)
                {
                    index = targetNamespaceUris.Append(uri!);
                }

                namespaceIndex = (ushort)index;
            }

            return nodeId.WithNamespaceIndex(namespaceIndex);
        }

        /// <summary>
        /// Updates the nodeset tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="qname">The qualified name.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static QualifiedName Translate(
            QualifiedName qname,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            if (qname.IsNull)
            {
                return qname;
            }

            ushort namespaceIndex = 0;

            if (qname.NamespaceIndex > 0)
            {
                string? uri = sourceNamespaceUris.GetString(qname.NamespaceIndex);

                if (uri == null)
                {
                    return qname;
                }

                int index = targetNamespaceUris.GetIndex(uri);

                if (index == -1)
                {
                    index = targetNamespaceUris.Append(uri);
                }

                namespaceIndex = (ushort)index;
            }

            return new QualifiedName(qname.Name, namespaceIndex);
        }

        /// <summary>
        /// Updates the nodeset tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="targetServerUris">The target server URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <param name="sourceServerUris">The source server URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static ExpandedNodeId Translate(
            ExpandedNodeId nodeId,
            NamespaceTable targetNamespaceUris,
            StringTable targetServerUris,
            NamespaceTable sourceNamespaceUris,
            StringTable sourceServerUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            if (nodeId.ServerIndex > 0)
            {
                if (targetServerUris == null)
                {
                    throw new ArgumentNullException(nameof(targetServerUris));
                }

                if (sourceServerUris == null)
                {
                    throw new ArgumentNullException(nameof(sourceServerUris));
                }
            }

            if (nodeId.IsNull)
            {
                return nodeId;
            }

            if (!nodeId.IsAbsolute)
            {
                return Translate((NodeId)nodeId, targetNamespaceUris, sourceNamespaceUris);
            }

            string? namespaceUri = nodeId.NamespaceUri;

            if (nodeId.ServerIndex > 0)
            {
                if (string.IsNullOrEmpty(namespaceUri))
                {
                    namespaceUri = sourceNamespaceUris.GetString(nodeId.NamespaceIndex);
                }

                string? serverUri = sourceServerUris.GetString(nodeId.ServerIndex);

                int index = targetServerUris.GetIndex(serverUri!);

                if (index == -1)
                {
                    index = targetServerUris.Append(serverUri!);
                }

                return new ExpandedNodeId(
                    nodeId.InnerNodeId.WithNamespaceIndex(0),
                    namespaceUri,
                    (uint)index);
            }

            ushort namespaceIndex = 0;

            if (!string.IsNullOrEmpty(namespaceUri))
            {
                int index = targetNamespaceUris.GetIndex(namespaceUri!);

                if (index == -1)
                {
                    index = targetNamespaceUris.Append(namespaceUri!);
                }

                namespaceIndex = (ushort)index;
            }

            return nodeId.InnerNodeId.WithNamespaceIndex(namespaceIndex);
        }

        /// <summary>
        /// Updates the nodeset tables and returns an extension object that
        /// references the updated tables
        /// </summary>
        /// <param name="extensionObject">The extension object.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private ExtensionObject Translate(
            ExtensionObject extensionObject,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (extensionObject.TryGetValue(out Argument argument))
            {
                argument.DataType = Translate(
                    argument.DataType,
                    sourceNamespaceUris,
                    targetNamespaceUris);
                return new ExtensionObject(argument);
            }
#pragma warning restore CS8602
#pragma warning restore CS8600

            return extensionObject;
        }

        /// <summary>
        /// Update the tables from all node ids provided
        /// </summary>
        /// <param name="nodeIds">The node identifiers.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static ArrayOf<NodeId> Translate(
            ArrayOf<NodeId> nodeIds,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return nodeIds.ConvertAll(nodeId => Translate(nodeId, targetNamespaceUris, sourceNamespaceUris));
        }

        /// <summary>
        /// Updates the nodeset tables and returns a name that conforms to the
        /// tables.
        /// </summary>
        /// <param name="qnames">The qualified names.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static ArrayOf<QualifiedName> Translate(
            ArrayOf<QualifiedName> qnames,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return qnames.ConvertAll(qname => Translate(qname, targetNamespaceUris, sourceNamespaceUris));
        }

        /// <summary>
        /// Updates the nodeset tables from the provided node ids and updates
        /// the node ids to reflect the indices in the table.
        /// </summary>
        /// <param name="nodeIds">The node identifiers.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="targetServerUris">The target server URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <param name="sourceServerUris">The source server URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static ArrayOf<ExpandedNodeId> Translate(
            ArrayOf<ExpandedNodeId> nodeIds,
            NamespaceTable targetNamespaceUris,
            StringTable targetServerUris,
            NamespaceTable sourceNamespaceUris,
            StringTable sourceServerUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return nodeIds.ConvertAll(nodeId => Translate(
                nodeId,
                targetNamespaceUris,
                targetServerUris,
                sourceNamespaceUris,
                sourceServerUris));
        }

        /// <summary>
        /// Updates the nodeset tables and returns extension objects that
        /// reference the updated tables
        /// </summary>
        /// <param name="extensionObjects">The extension objects.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private ArrayOf<ExtensionObject> Translate(
            ArrayOf<ExtensionObject> extensionObjects,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return extensionObjects.ConvertAll(extensionObject => Translate(
                extensionObject,
                targetNamespaceUris,
                sourceNamespaceUris));
        }

        /// <summary>
        /// Update the tables from all node ids provided
        /// </summary>
        /// <param name="nodeIds">The node identifiers.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static MatrixOf<NodeId> Translate(
            MatrixOf<NodeId> nodeIds,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return nodeIds.ConvertAll(nodeId => Translate(nodeId, targetNamespaceUris, sourceNamespaceUris));
        }

        /// <summary>
        /// Updates the nodeset tables and returns a name that conforms to the
        /// tables.
        /// </summary>
        /// <param name="qnames">The qualified names.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static MatrixOf<QualifiedName> Translate(
            MatrixOf<QualifiedName> qnames,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return qnames.ConvertAll(qname => Translate(qname, targetNamespaceUris, sourceNamespaceUris));
        }

        /// <summary>
        /// Updates the nodeset tables from the provided node ids and updates
        /// the node ids to reflect the indices in the table.
        /// </summary>
        /// <param name="nodeIds">The node identifiers.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="targetServerUris">The target server URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <param name="sourceServerUris">The source server URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private static MatrixOf<ExpandedNodeId> Translate(
            MatrixOf<ExpandedNodeId> nodeIds,
            NamespaceTable targetNamespaceUris,
            StringTable targetServerUris,
            NamespaceTable sourceNamespaceUris,
            StringTable sourceServerUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return nodeIds.ConvertAll(nodeId => Translate(
                nodeId,
                targetNamespaceUris,
                targetServerUris,
                sourceNamespaceUris,
                sourceServerUris));
        }

        /// <summary>
        /// Updates the nodeset tables and returns extension objects that
        /// reference the updated tables
        /// </summary>
        /// <param name="extensionObjects">The extension objects.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
        private MatrixOf<ExtensionObject> Translate(
            MatrixOf<ExtensionObject> extensionObjects,
            NamespaceTable targetNamespaceUris,
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(targetNamespaceUris));
            }

            if (sourceNamespaceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceNamespaceUris));
            }

            return extensionObjects.ConvertAll(extensionObject => Translate(
                extensionObject,
                targetNamespaceUris,
                sourceNamespaceUris));
        }

        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private Dictionary<NodeId, Node> m_nodes;
    }
}
