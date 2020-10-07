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

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Opc.Ua
{    
    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix"), DataContract(Namespace = Namespaces.OpcUaXsd)]
    [KnownType(typeof(ObjectNode))]
    [KnownType(typeof(ObjectTypeNode))]
    [KnownType(typeof(VariableNode))]
    [KnownType(typeof(VariableTypeNode))]
    [KnownType(typeof(MethodNode))]
    [KnownType(typeof(DataTypeNode))]
    [KnownType(typeof(ReferenceTypeNode))]
    [KnownType(typeof(ViewNode))]
    public partial class NodeSet  : IEnumerable<Node>
    {
        #region Constructors
        /// <summary>
        /// Creates an empty nodeset.
        /// </summary>
        public NodeSet()
        {
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();
            m_nodes = new Dictionary<NodeId,Node>();
        }
        
        /// <summary>
        /// Loads a nodeset from a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        /// <returns>The set of nodes</returns>
        public static NodeSet Read(Stream istrm)
        {
            XmlReader reader = XmlReader.Create(istrm);
            
            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(NodeSet));
                return serializer.ReadObject(reader) as NodeSet;
            }
            finally
            {
                reader.Dispose();
            }
        }
        
        /// <summary>
        /// Write a nodeset to a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        public void Write(Stream istrm)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = Encoding.UTF8;
            settings.Indent = true;
            XmlWriter writer = XmlWriter.Create(istrm, settings);
                        
            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(NodeSet));
                serializer.WriteObject(writer, this);
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }
        }
        #endregion
        
        #region IEnumerable<IReference> Members
        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<Node> GetEnumerator()
        {
            return new List<Node>(m_nodes.Values).GetEnumerator();
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
        /// Adds a node to the set.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <remarks>
        /// The NodeId must reference the strings for the node set.
        /// </remarks>
        public void Add(Node node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            if (NodeId.IsNull(node.NodeId))
            {
                throw new ArgumentException("A non-null NodeId must be specified.");
            }
            
            if (m_nodes.ContainsKey(node.NodeId))
            {
                throw new ArgumentException(Utils.Format("NodeID {0} already exists for node: {1}", node.NodeId, node));
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
        private void TranslateArrayValue(Array array, BuiltInType elementType, NamespaceTable namespaceUris, StringTable serverUris)
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
                    indexes[jj] = (ii/divisor)%dimensions[jj];
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
        private object TranslateValue(object value, NamespaceTable namespaceUris, StringTable serverUris)
        {            
            TypeInfo typeInfo = TypeInfo.Construct(value);

            // do nothing for unknown types.
            if (typeInfo == null)
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
                {
                    return Translate((NodeId)value, m_namespaceUris, namespaceUris);
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return Translate((ExpandedNodeId)value, m_namespaceUris, m_serverUris, namespaceUris, serverUris);
                }

                case BuiltInType.QualifiedName:
                {
                    return Translate((QualifiedName)value, m_namespaceUris, namespaceUris);
                }

                case BuiltInType.ExtensionObject:
                {
                    Argument argument = ExtensionObject.ToEncodeable((ExtensionObject)value) as Argument;

                    if (argument != null)
                    {
                        argument.DataType = Translate(argument.DataType, m_namespaceUris, namespaceUris);
                    }

                    return value;
                }
            }
                   
            return value;
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
        public Node Add(ILocalNode nodeToExport, NamespaceTable namespaceUris, StringTable serverUris)
        {
            Node node = Node.Copy(nodeToExport);

            node.NodeId = Translate(nodeToExport.NodeId, m_namespaceUris, namespaceUris);
            node.BrowseName = Translate(nodeToExport.BrowseName, m_namespaceUris, namespaceUris);
            
            VariableNode variableToExport = nodeToExport as VariableNode;

            if (variableToExport != null)
            {
                VariableNode variableNode = (VariableNode)node;

                object value = TranslateValue(variableNode.Value.Value, namespaceUris, serverUris);
                variableNode.Value = new Variant(value);

                variableNode.DataType = Translate(variableToExport.DataType, m_namespaceUris, namespaceUris);
            }
            
            VariableTypeNode variableTypeToExport = nodeToExport as VariableTypeNode;

            if (variableTypeToExport != null)
            {
                VariableTypeNode variableTypeNode = (VariableTypeNode)node;

                object value = TranslateValue(variableTypeNode.Value.Value, namespaceUris, serverUris);
                variableTypeNode.Value = new Variant(value);

                variableTypeNode.DataType = Translate(variableTypeToExport.DataType, m_namespaceUris, namespaceUris);
            }

            foreach (IReference referenceToExport in nodeToExport.References)
            {
                ReferenceNode reference = new ReferenceNode();

                reference.ReferenceTypeId = Translate(referenceToExport.ReferenceTypeId, m_namespaceUris, namespaceUris);
                reference.IsInverse       = referenceToExport.IsInverse;
                reference.TargetId        = Translate(referenceToExport.TargetId, m_namespaceUris, m_serverUris, namespaceUris, serverUris); 

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
        public void AddReference(Node node, ReferenceNode referenceToExport, NamespaceTable namespaceUris, StringTable serverUris)
        {
            ReferenceNode reference = new ReferenceNode();

            reference.ReferenceTypeId = Translate(referenceToExport.ReferenceTypeId, m_namespaceUris, namespaceUris);
            reference.IsInverse = referenceToExport.IsInverse;
            reference.TargetId = Translate(referenceToExport.TargetId, m_namespaceUris, m_serverUris, namespaceUris, serverUris); 

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
            Node node = null;

            if (m_nodes.TryGetValue(nodeId, out node))
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
        public Node Find(NodeId nodeId, NamespaceTable namespaceUris)
        {
            if (nodeId == null) throw new ArgumentNullException(nameof(nodeId));
            if (namespaceUris == null) throw new ArgumentNullException(nameof(namespaceUris));

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
            NodeId localId = new NodeId(nodeId.Identifier, (ushort)nsIndex);

            // look up node.
            Node node = null;

            if (m_nodes.TryGetValue(localId, out node))
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
            Node node = Node.Copy(nodeToImport);

            node.NodeId = Translate(nodeToImport.NodeId, namespaceUris, m_namespaceUris);
            node.BrowseName = Translate(nodeToImport.BrowseName, namespaceUris, m_namespaceUris);

            VariableNode variableToImport = nodeToImport as VariableNode;

            if (variableToImport != null)
            {
                VariableNode variable = (VariableNode)node;

                variable.DataType = Translate(variableToImport.DataType, namespaceUris, m_namespaceUris); 

                if (variableToImport.Value.Value != null)
                {
                    variable.Value = new Variant(ImportValue(variableToImport.Value.Value, namespaceUris, serverUris));
                }
            }
            
            VariableTypeNode variableTypeToImport = nodeToImport as VariableTypeNode;

            if (variableTypeToImport != null)
            {
                VariableTypeNode variableType = (VariableTypeNode)node;

                variableType.DataType = Translate(variableTypeToImport.DataType, namespaceUris, m_namespaceUris); 
                
                if (variableTypeToImport.Value.Value != null)
                {
                    variableType.Value = new Variant(ImportValue(variableTypeToImport.Value.Value, namespaceUris, serverUris));
                }
            }

            foreach (ReferenceNode referenceToImport in nodeToImport.References)
            {
                ReferenceNode reference = new ReferenceNode();

                reference.ReferenceTypeId = Translate(referenceToImport.ReferenceTypeId, namespaceUris, m_namespaceUris);
                reference.IsInverse       = referenceToImport.IsInverse;
                reference.TargetId        = Translate(referenceToImport.TargetId, namespaceUris, serverUris, m_namespaceUris, m_serverUris); 

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
        /// <returns></returns>
        private object ImportValue(object value, NamespaceTable namespaceUris, StringTable serverUris)
        {
            Array array = value as Array;

            if (array != null)
            {
                Type elementType = array.GetType().GetElementType();

                if (elementType != typeof(NodeId) && elementType != typeof(ExpandedNodeId) && elementType != typeof(object) && elementType != typeof(ExtensionObject))
                {
                    return array;
                }

                Array copy = Array.CreateInstance(elementType, array.Length);

                for (int ii = 0; ii < array.Length; ii++)
                {
                    copy.SetValue(ImportValue(array.GetValue(ii), namespaceUris, serverUris), ii);
                }

                return copy;
            }

            NodeId nodeId = value as NodeId; 

            if (nodeId != null)
            {
                return Import(nodeId, namespaceUris);
            }

            ExpandedNodeId expandedNodeId = value as ExpandedNodeId; 

            if (expandedNodeId != null)
            {
                return Import(expandedNodeId, namespaceUris, serverUris);
            }

            ExtensionObject extension = value as ExtensionObject; 

            if (extension != null)
            {
                Argument argument = ExtensionObject.ToEncodeable(extension) as Argument;

                if (argument != null)
                {
                    argument.DataType = Import(argument.DataType, namespaceUris);
                }
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
        public ExpandedNodeId Export(ExpandedNodeId nodeId, NamespaceTable namespaceUris, StringTable serverUris)
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
        public ExpandedNodeId Import(ExpandedNodeId nodeId, NamespaceTable namespaceUris, StringTable serverUris)
        {
            return Translate(nodeId, namespaceUris, serverUris, m_namespaceUris, m_serverUris);
        }
        #endregion
        
        #region Private Members
		/// <summary>
		/// The table of namespaces.
		/// </summary>
		[DataMember(Name = "NamespaceUris", Order = 1)]
        internal StringCollection NamespaceUris
        {
            get 
            { 
                return new StringCollection(m_namespaceUris.ToArray());
            }
            
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
            get 
            { 
                return new StringCollection(m_serverUris.ToArray());
            }
            
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
            get 
            { 
                return new NodeCollection(m_nodes.Values);
            }
            
            set
            {
                m_nodes = new Dictionary<NodeId,Node>();

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
        private static NodeId Translate(
            NodeId nodeId, 
            NamespaceTable targetNamespaceUris, 
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null) throw new ArgumentNullException(nameof(targetNamespaceUris));
            if (sourceNamespaceUris == null) throw new ArgumentNullException(nameof(sourceNamespaceUris));

            if (NodeId.IsNull(nodeId))
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

            return new NodeId(nodeId.Identifier, namespaceIndex);
        }
        
        /// <summary>
        /// Updates the nodeset string tables and returns a NodeId that references those tables.
        /// </summary>
        /// <param name="qname">The qualified name.</param>
        /// <param name="targetNamespaceUris">The target namespace URIs.</param>
        /// <param name="sourceNamespaceUris">The source namespace URIs.</param>
        /// <returns>A NodeId that references those tables.</returns>
        private static QualifiedName Translate(
            QualifiedName  qname, 
            NamespaceTable targetNamespaceUris, 
            NamespaceTable sourceNamespaceUris)
        {
            if (targetNamespaceUris == null) throw new ArgumentNullException(nameof(targetNamespaceUris));
            if (sourceNamespaceUris == null) throw new ArgumentNullException(nameof(sourceNamespaceUris));

            if (QualifiedName.IsNull(qname))
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
        private static ExpandedNodeId Translate(
            ExpandedNodeId nodeId, 
            NamespaceTable targetNamespaceUris, 
            StringTable    targetServerUris, 
            NamespaceTable sourceNamespaceUris, 
            StringTable    sourceServerUris)
        {
            if (targetNamespaceUris == null) throw new ArgumentNullException(nameof(targetNamespaceUris));
            if (sourceNamespaceUris == null) throw new ArgumentNullException(nameof(sourceNamespaceUris));
        
            if (nodeId.ServerIndex > 0)
            {
                if (targetServerUris == null) throw new ArgumentNullException(nameof(targetServerUris));
                if (sourceServerUris == null) throw new ArgumentNullException(nameof(sourceServerUris));
            }

            if (NodeId.IsNull(nodeId))
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
                if (String.IsNullOrEmpty(namespaceUri))
                {
                    namespaceUri = sourceNamespaceUris.GetString(nodeId.NamespaceIndex);
                }

                string serverUri = sourceServerUris.GetString(nodeId.ServerIndex);

                int index = targetServerUris.GetIndex(serverUri);

                if (index == -1)
                {
                    index = targetServerUris.Append(serverUri);
                }
                
                return new ExpandedNodeId(new NodeId(nodeId.Identifier, 0), namespaceUri, (uint)index);
            }
            
            ushort namespaceIndex = 0;

            if (!String.IsNullOrEmpty(namespaceUri))
            {
                int index = targetNamespaceUris.GetIndex(namespaceUri);

                if (index == -1)
                {
                    index = targetNamespaceUris.Append(namespaceUri);
                }

                namespaceIndex = (ushort)index;
            }

            return new NodeId(nodeId.Identifier, namespaceIndex);
        }
        #endregion
        
        #region Private Fields
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private Dictionary<NodeId,Node> m_nodes;
        #endregion
    }
}
