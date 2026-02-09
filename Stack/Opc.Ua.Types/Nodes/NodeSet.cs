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
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
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
        public static NodeSet Read(Stream istrm)
        {
            DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<NodeSet>();
            using var reader = XmlReader.Create(istrm, CoreUtils.DefaultXmlReaderSettings());
            return serializer.ReadObject(reader) as NodeSet;
        }

        /// <summary>
        /// Write a nodeset to a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        public void Write(Stream istrm)
        {
            var writer = XmlWriter.Create(istrm, CoreUtils.DefaultXmlWriterSettings());
            try
            {
                DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<NodeSet>();
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

            if (node.NodeId.IsNullNodeId)
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
        /// Translates all elements in an array value.
        /// </summary>
        /// <param name="array">The array.</param>
        /// <param name="elementType">Type of the element.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        private void TranslateArrayValue(
            Array array,
            BuiltInType elementType,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            if (array == null)
            {
                return;
            }

            int[] dimensions = new int[array.Rank];

            for (int ii = 0; ii < dimensions.Length; ii++)
            {
                dimensions[ii] = array.GetLength(ii);
            }

            int length = array.Length;
            int[] indexes = new int[dimensions.Length];

            for (int ii = 0; ii < length; ii++)
            {
                int divisor = length;

                for (int jj = 0; jj < indexes.Length; jj++)
                {
                    divisor /= dimensions[jj];
                    indexes[jj] = ii / divisor % dimensions[jj];
                }

                object element = array.GetValue(indexes);

                if (element != null)
                {
                    if (elementType == BuiltInType.Variant)
                    {
                        element = ((Variant)element).Value;
                    }

                    element = TranslateValue(element, namespaceUris, serverUris);

                    if (elementType == BuiltInType.Variant)
                    {
                        element = new Variant(element);
                    }

                    array.SetValue(element, indexes);
                }
            }
        }

        /// <summary>
        /// Translates a value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        /// <returns>Translated value.</returns>
        private object TranslateValue(
            object value,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            var typeInfo = TypeInfo.Construct(value);

            // do nothing for unknown types.
            if (typeInfo.IsUnknown)
            {
                return value;
            }

            // recursively process array values.
            if (typeInfo.ValueRank > 0)
            {
                TranslateArrayValue((Array)value, typeInfo.BuiltInType, namespaceUris, serverUris);
                return value;
            }

            // check for values containing namespace indexes.
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.NodeId:
                    return Translate((NodeId)value, m_namespaceUris, namespaceUris);
                case BuiltInType.ExpandedNodeId:
                    return Translate(
                        (ExpandedNodeId)value,
                        m_namespaceUris,
                        m_serverUris,
                        namespaceUris,
                        serverUris);
                case BuiltInType.QualifiedName:
                    return Translate((QualifiedName)value, m_namespaceUris, namespaceUris);
                case BuiltInType.ExtensionObject:
                    if (ExtensionObject.ToEncodeable((ExtensionObject)value) is Argument argument)
                    {
                        argument.DataType = Translate(
                            argument.DataType,
                            m_namespaceUris,
                            namespaceUris);
                    }
                    return value;
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return value;
                default:
                    Debug.Fail("Unexpected built-in type {typeInfo.BuiltInType}");
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
            var node = Node.Copy(nodeToExport);

            node.NodeId = Translate(nodeToExport.NodeId, m_namespaceUris, namespaceUris);
            node.BrowseName = Translate(nodeToExport.BrowseName, m_namespaceUris, namespaceUris);

            if (nodeToExport is VariableNode variableToExport)
            {
                var variableNode = (VariableNode)node;

                object value = TranslateValue(variableNode.Value.Value, namespaceUris, serverUris);
                variableNode.Value = new Variant(value);

                variableNode.DataType = Translate(
                    variableToExport.DataType,
                    m_namespaceUris,
                    namespaceUris);
            }

            if (nodeToExport is VariableTypeNode variableTypeToExport)
            {
                var variableTypeNode = (VariableTypeNode)node;

                object value = TranslateValue(
                    variableTypeNode.Value.Value,
                    namespaceUris,
                    serverUris);
                variableTypeNode.Value = new Variant(value);

                variableTypeNode.DataType = Translate(
                    variableTypeToExport.DataType,
                    m_namespaceUris,
                    namespaceUris);
            }

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

                node.References.Add(reference);
            }

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

            node.References.Add(reference);
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
        /// 	<c>true</c> if the node exists in the nodeset; otherwise, <c>false</c>.
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
        public Node Find(NodeId nodeId)
        {
            if (m_nodes.TryGetValue(nodeId, out Node node))
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
        /// <exception cref="ArgumentNullException"><paramref name="nodeId"/> is <c>null</c>.</exception>
        public Node Find(NodeId nodeId, NamespaceTable namespaceUris)
        {
            if (nodeId.IsNullNodeId)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }

            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            // check for unknown namespace index.
            string ns = namespaceUris.GetString(nodeId.NamespaceIndex);

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
            if (m_nodes.TryGetValue(localId, out Node node))
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
            var node = Node.Copy(nodeToImport);

            node.NodeId = Translate(nodeToImport.NodeId, namespaceUris, m_namespaceUris);
            node.BrowseName = Translate(nodeToImport.BrowseName, namespaceUris, m_namespaceUris);

            if (nodeToImport is VariableNode variableToImport)
            {
                var variable = (VariableNode)node;

                variable.DataType = Translate(
                    variableToImport.DataType,
                    namespaceUris,
                    m_namespaceUris);

                if (variableToImport.Value.Value != null)
                {
                    variable.Value = new Variant(
                        ImportValue(variableToImport.Value.Value, namespaceUris, serverUris));
                }
            }

            if (nodeToImport is VariableTypeNode variableTypeToImport)
            {
                var variableType = (VariableTypeNode)node;

                variableType.DataType = Translate(
                    variableTypeToImport.DataType,
                    namespaceUris,
                    m_namespaceUris);

                if (variableTypeToImport.Value.Value != null)
                {
                    variableType.Value = new Variant(
                        ImportValue(variableTypeToImport.Value.Value, namespaceUris, serverUris));
                }
            }

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

                node.References.Add(reference);
            }

            return node;
        }

        /// <summary>
        /// Recursively imports any NodeIds or ExpandedNodeIds contained in a value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="serverUris">The server URIs.</param>
        private object ImportValue(
            object value,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            if (value is Array array)
            {
                Type elementType = array.GetType().GetElementType();

                if (elementType != typeof(NodeId) &&
                    elementType != typeof(ExpandedNodeId) &&
                    elementType != typeof(object) &&
                    elementType != typeof(ExtensionObject))
                {
                    return array;
                }

                var copy = Array.CreateInstance(elementType, array.Length);

                for (int ii = 0; ii < array.Length; ii++)
                {
                    copy.SetValue(ImportValue(array.GetValue(ii), namespaceUris, serverUris), ii);
                }

                return copy;
            }

            switch (value)
            {
                case NodeId nodeId:
                    if (!nodeId.IsNullNodeId)
                    {
                        return Import(nodeId, namespaceUris);
                    }
                    break;
                case ExpandedNodeId expandedNodeId:
                    return Import(expandedNodeId, namespaceUris, serverUris);
                case ExtensionObject extension when ExtensionObject.ToEncodeable(extension) is Argument argument:
                    argument.DataType = Import(argument.DataType, namespaceUris);
                    break;
            }
            return value;
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
        internal StringCollection NamespaceUris
        {
            get => [.. m_namespaceUris.ToArray()];
            set
            {
                if (value == null)
                {
                    m_namespaceUris = new NamespaceTable();
                }
                else
                {
                    m_namespaceUris = new NamespaceTable(value);
                }
            }
        }

        /// <summary>
        /// The table of servers.
        /// </summary>
        [DataMember(Name = "ServerUris", Order = 2)]
        internal StringCollection ServerUris
        {
            get => [.. m_serverUris.ToArray()];
            set
            {
                if (value == null)
                {
                    m_serverUris = new StringTable();
                }
                else
                {
                    m_serverUris = new StringTable(value);
                }
            }
        }

        /// <summary>
        /// The table of nodes.
        /// </summary>
        [DataMember(Name = "Nodes", Order = 3)]
        internal NodeCollection Nodes
        {
            get => [.. m_nodes.Values];
            set
            {
                m_nodes = [];

                if (value != null)
                {
                    foreach (Node node in value)
                    {
                        m_nodes[node.NodeId] = node;
                    }
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
        /// <exception cref="ArgumentNullException"><paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
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

            if (nodeId.IsNullNodeId)
            {
                return nodeId;
            }

            ushort namespaceIndex = 0;

            if (nodeId.NamespaceIndex > 0)
            {
                string uri = sourceNamespaceUris.GetString(nodeId.NamespaceIndex);

                int index = targetNamespaceUris.GetIndex(uri);

                if (index == -1)
                {
                    index = targetNamespaceUris.Append(uri);
                }

                namespaceIndex = (ushort)index;
            }

            return nodeId.WithNamespaceIndex(namespaceIndex);
        }

        /// <summary>
        /// Updates the nodeset string tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="qname">The qualified name.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
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

            if (qname.IsNullQn)
            {
                return qname;
            }

            ushort namespaceIndex = 0;

            if (qname.NamespaceIndex > 0)
            {
                string uri = sourceNamespaceUris.GetString(qname.NamespaceIndex);

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
        /// Updates the nodeset string tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="targetServerUris">The target server URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <param name="sourceServerUris">The source server URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="targetNamespaceUris"/> is <c>null</c>.</exception>
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

            string namespaceUri = nodeId.NamespaceUri;

            if (nodeId.ServerIndex > 0)
            {
                if (string.IsNullOrEmpty(namespaceUri))
                {
                    namespaceUri = sourceNamespaceUris.GetString(nodeId.NamespaceIndex);
                }

                string serverUri = sourceServerUris.GetString(nodeId.ServerIndex);

                int index = targetServerUris.GetIndex(serverUri);

                if (index == -1)
                {
                    index = targetServerUris.Append(serverUri);
                }

                return new ExpandedNodeId(
                    nodeId.InnerNodeId.WithNamespaceIndex(0),
                    namespaceUri,
                    (uint)index);
            }

            ushort namespaceIndex = 0;

            if (!string.IsNullOrEmpty(namespaceUri))
            {
                int index = targetNamespaceUris.GetIndex(namespaceUri);

                if (index == -1)
                {
                    index = targetNamespaceUris.Append(namespaceUri);
                }

                namespaceIndex = (ushort)index;
            }

            return nodeId.InnerNodeId.WithNamespaceIndex(namespaceIndex);
        }

        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private Dictionary<NodeId, Node> m_nodes;
    }
}
