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
using System.Xml;
using System.Text;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a collection of nodes.
    /// </summary>
    public class NodeStateCollection : List<NodeState>
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeStateCollection"/> class.
        /// </summary>
        public NodeStateCollection()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeStateCollection"/> class.
        /// </summary>
        /// <param name="capacity">The initial capacity.</param>
        public NodeStateCollection(int capacity) : base(capacity)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeStateCollection"/> class.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new list.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public NodeStateCollection(IEnumerable<NodeState> collection) : base(collection)
        {
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Writes the collection to a stream using the NodeSet schema.
        /// </summary>
        public void SaveAsNodeSet(ISystemContext context, Stream ostrm)
        {
            NodeTable nodeTable = new NodeTable(context.NamespaceUris, context.ServerUris, null);
            
            for (int ii = 0; ii < this.Count; ii++)
            {
                this[ii].Export(context, nodeTable);
            }

            NodeSet nodeSet = new NodeSet();
            
            foreach (ILocalNode node in nodeTable)
            {
                nodeSet.Add(node, nodeTable.NamespaceUris, nodeTable.ServerUris);
            }

            XmlWriterSettings settings = new XmlWriterSettings();

            settings.Encoding = Encoding.UTF8;
            settings.CloseOutput = true;
            settings.ConformanceLevel = ConformanceLevel.Document;
            settings.Indent = true;

            using (XmlWriter writer = XmlWriter.Create(ostrm, settings))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(NodeSet));
                serializer.WriteObject(writer, nodeSet);
            }
        }

        #region Well-Known Aliases
        /// <summary>
        /// Stores a well known alias.
        /// </summary>
        private struct AliasToUse
        {
            public AliasToUse(string alias, NodeId nodeId)
            {
                Alias = alias;
                NodeId = nodeId;
            }

            public string Alias;
            public NodeId NodeId;
        }

        /// <summary>
        /// The list of aliases to use.
        /// </summary>
        private AliasToUse[] s_AliasesToUse = new AliasToUse[]
        {
            new AliasToUse(BrowseNames.Boolean, DataTypeIds.Boolean),
            new AliasToUse(BrowseNames.SByte, DataTypeIds.SByte),
            new AliasToUse(BrowseNames.Byte, DataTypeIds.Byte),
            new AliasToUse(BrowseNames.Int16, DataTypeIds.Int16),
            new AliasToUse(BrowseNames.UInt16, DataTypeIds.UInt16),
            new AliasToUse(BrowseNames.Int32, DataTypeIds.Int32),
            new AliasToUse(BrowseNames.UInt32, DataTypeIds.UInt32),
            new AliasToUse(BrowseNames.Int64, DataTypeIds.Int64),
            new AliasToUse(BrowseNames.UInt64, DataTypeIds.UInt64),
            new AliasToUse(BrowseNames.Float, DataTypeIds.Float),
            new AliasToUse(BrowseNames.Double, DataTypeIds.Double),
            new AliasToUse(BrowseNames.DateTime, DataTypeIds.DateTime),
            new AliasToUse(BrowseNames.String, DataTypeIds.String),
            new AliasToUse(BrowseNames.ByteString, DataTypeIds.ByteString),
            new AliasToUse(BrowseNames.Guid, DataTypeIds.Guid),
            new AliasToUse(BrowseNames.XmlElement, DataTypeIds.XmlElement),
            new AliasToUse(BrowseNames.NodeId, DataTypeIds.NodeId),
            new AliasToUse(BrowseNames.ExpandedNodeId, DataTypeIds.ExpandedNodeId),
            new AliasToUse(BrowseNames.QualifiedName, DataTypeIds.QualifiedName),
            new AliasToUse(BrowseNames.LocalizedText, DataTypeIds.LocalizedText),
            new AliasToUse(BrowseNames.StatusCode, DataTypeIds.StatusCode),
            new AliasToUse(BrowseNames.Structure, DataTypeIds.Structure),
            new AliasToUse(BrowseNames.Number, DataTypeIds.Number),
            new AliasToUse(BrowseNames.Integer, DataTypeIds.Integer),
            new AliasToUse(BrowseNames.UInteger, DataTypeIds.UInteger),
            new AliasToUse(BrowseNames.HasComponent, ReferenceTypeIds.HasComponent),
            new AliasToUse(BrowseNames.HasProperty, ReferenceTypeIds.HasProperty),
            new AliasToUse(BrowseNames.Organizes, ReferenceTypeIds.Organizes),
            new AliasToUse(BrowseNames.HasEventSource, ReferenceTypeIds.HasEventSource),
            new AliasToUse(BrowseNames.HasNotifier, ReferenceTypeIds.HasNotifier),
            new AliasToUse(BrowseNames.HasSubtype, ReferenceTypeIds.HasSubtype),
            new AliasToUse(BrowseNames.HasTypeDefinition, ReferenceTypeIds.HasTypeDefinition),
            new AliasToUse(BrowseNames.HasModellingRule, ReferenceTypeIds.HasModellingRule),
            new AliasToUse(BrowseNames.HasEncoding, ReferenceTypeIds.HasEncoding),
            new AliasToUse(BrowseNames.HasDescription, ReferenceTypeIds.HasDescription)
        };
        #endregion
        
        /// <summary>
        /// Writes the collection to a stream using the Opc.Ua.Schema.UANodeSet schema.
        /// </summary>
        public void SaveAsNodeSet2(ISystemContext context, Stream ostrm)
        {
            SaveAsNodeSet2(context, ostrm, null);
        }

        /// <summary>
        /// Writes the collection to a stream using the Opc.Ua.Schema.UANodeSet schema.
        /// </summary>
        public void SaveAsNodeSet2(ISystemContext context, Stream ostrm, string version)
        {
            Opc.Ua.Export.UANodeSet nodeSet = new Opc.Ua.Export.UANodeSet();
            nodeSet.LastModified = DateTime.UtcNow;
            nodeSet.LastModifiedSpecified = true;

            for (int ii = 0; ii < s_AliasesToUse.Length; ii++)
            {
                nodeSet.AddAlias(context, s_AliasesToUse[ii].Alias, s_AliasesToUse[ii].NodeId);
            }

            for (int ii = 0; ii < this.Count; ii++)
            {
                nodeSet.Export(context, this[ii], true);
            }

            nodeSet.Write(ostrm);
        }

        /// <summary>
        /// Writes the schema information to a static XML export file.
        /// </summary>
        public void SaveAsXml(ISystemContext context, Stream ostrm)
        {
            SaveAsXml(context, ostrm, false);
        }

        /// <summary>
        /// Writes the schema information to a static XML export file.
        /// </summary>
        public void SaveAsXml(ISystemContext context, Stream ostrm, bool keepStreamOpen)
        {
            XmlWriterSettings settings = new XmlWriterSettings();

            settings.Encoding = Encoding.UTF8;
            settings.CloseOutput = !keepStreamOpen;
            settings.ConformanceLevel = ConformanceLevel.Document;
            settings.Indent = true;

            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            using (XmlWriter writer = XmlWriter.Create(ostrm, settings))
            {
                XmlQualifiedName root = new XmlQualifiedName("ListOfNodeState", Namespaces.OpcUaXsd);
                XmlEncoder encoder = new XmlEncoder(root, writer, messageContext);

                encoder.SaveStringTable("NamespaceUris", "NamespaceUri", context.NamespaceUris);
                encoder.SaveStringTable("ServerUris", "ServerUri", context.ServerUris);

                for (int ii = 0; ii < this.Count; ii++)
                {
                    NodeState state = this[ii];

                    if (state != null)
                    {
                        state.SaveAsXml(context, encoder);
                    }
                }

                encoder.Close();
            }
        }

        /// <summary>
        /// Writes the collection to a binary stream. The stream is closed by this method.
        /// </summary>
        public void SaveAsBinary(ISystemContext context, Stream ostrm)
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            BinaryEncoder encoder = new BinaryEncoder(ostrm, messageContext);

            encoder.SaveStringTable(context.NamespaceUris);
            encoder.SaveStringTable(context.ServerUris);

            encoder.WriteInt32(null, this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                NodeState state = this[ii];
                state.SaveAsBinary(context, encoder);
            }

            encoder.Close();
        }

        /// <summary>
        /// Reads the schema information from a XML document.
        /// </summary>
        public void LoadFromBinary(ISystemContext context, Stream istrm, bool updateTables)
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            using (BinaryDecoder decoder = new BinaryDecoder(istrm, messageContext))
            {
                // check if a namespace table was provided.
                NamespaceTable namespaceUris = new NamespaceTable();

                if (!decoder.LoadStringTable(namespaceUris))
                {
                    namespaceUris = null;
                }

                // update namespace table.
                if (updateTables)
                {
                    if (namespaceUris != null && context.NamespaceUris != null)
                    {
                        for (int ii = 0; ii < namespaceUris.Count; ii++)
                        {
                            context.NamespaceUris.GetIndexOrAppend(namespaceUris.GetString((uint)ii));
                        }
                    }
                }

                // check if a server uri table was provided.
                StringTable serverUris = new StringTable();

                if (namespaceUris != null && namespaceUris.Count > 1)
                {
                    serverUris.Append(namespaceUris.GetString(1));
                }

                if (!decoder.LoadStringTable(serverUris))
                {
                    serverUris = null;
                }

                // update server table.
                if (updateTables)
                {
                    if (serverUris != null && context.ServerUris != null)
                    {
                        for (int ii = 0; ii < serverUris.Count; ii++)
                        {
                            context.ServerUris.GetIndexOrAppend(serverUris.GetString((uint)ii));
                        }
                    }
                }

                // setup the mappings to use during decoding.
                decoder.SetMappingTables(namespaceUris, serverUris);

                int count = decoder.ReadInt32(null);

                for (int ii = 0; ii < count; ii++)
                {
                    NodeState state = NodeState.LoadNode(context, decoder);
                    this.Add(state);
                }
            }
        }

        /// <summary>
        /// Reads the schema information from a XML document.
        /// </summary>
        public void LoadFromXml(ISystemContext context, Stream istrm, bool updateTables)
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            using (XmlReader reader = XmlReader.Create(istrm))
            {
                XmlQualifiedName root = new XmlQualifiedName("ListOfNodeState", Namespaces.OpcUaXsd);
                XmlDecoder decoder = new XmlDecoder(null, reader, messageContext);

                NamespaceTable namespaceUris = new NamespaceTable();

                if (!decoder.LoadStringTable("NamespaceUris", "NamespaceUri", namespaceUris))
                {
                    namespaceUris = null;
                }

                // update namespace table.
                if (updateTables)
                {
                    if (namespaceUris != null && context.NamespaceUris != null)
                    {
                        for (int ii = 0; ii < namespaceUris.Count; ii++)
                        {
                            context.NamespaceUris.GetIndexOrAppend(namespaceUris.GetString((uint)ii));
                        }
                    }
                }

                StringTable serverUris = new StringTable();

                if (!decoder.LoadStringTable("ServerUris", "ServerUri", context.ServerUris))
                {
                    serverUris = null;
                }

                // update server table.
                if (updateTables)
                {
                    if (serverUris != null && context.ServerUris != null)
                    {
                        for (int ii = 0; ii < serverUris.Count; ii++)
                        {
                            context.ServerUris.GetIndexOrAppend(serverUris.GetString((uint)ii));
                        }
                    }
                }

                // set mapping.
                decoder.SetMappingTables(namespaceUris, serverUris);

                decoder.PushNamespace(Namespaces.OpcUaXsd);

                NodeState state = NodeState.LoadNode(context, decoder);

                while (state != null)
                {
                    this.Add(state);

                    state = NodeState.LoadNode(context, decoder);
                }

                decoder.Close();
            }
        }

        /// <summary>
        /// Loads the nodes from an embedded resource.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="resourcePath">The resource path.</param>
        /// <param name="assembly">The assembly containing the resource.</param>
        /// <param name="updateTables">if set to <c>true</c> the namespace and server tables are updated with any new URIs.</param>
        public void LoadFromResource(ISystemContext context, string resourcePath, Assembly assembly, bool updateTables)
        {
            if (resourcePath == null) throw new ArgumentNullException(nameof(resourcePath));

            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            
            Stream istrm = assembly.GetManifestResourceStream(resourcePath);
            if (istrm == null)
            {
                // try to load from app directory
                FileInfo file = new FileInfo(resourcePath);
                istrm = file.OpenRead();
                if (istrm == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not load nodes from resource: {0}", resourcePath);
                }
            }

            LoadFromXml(context, istrm, updateTables);
        }

        /// <summary>
        /// Loads the nodes from an embedded resource.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="resourcePath">The resource path.</param>
        /// <param name="assembly">The assembly containing the resource.</param>
        /// <param name="updateTables">if set to <c>true</c> the namespace and server tables are updated with any new URIs.</param>
        public void LoadFromBinaryResource(ISystemContext context, string resourcePath, Assembly assembly, bool updateTables)
        {
            if (resourcePath == null) throw new ArgumentNullException(nameof(resourcePath));

            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            
            Stream istrm = assembly.GetManifestResourceStream(resourcePath);
            if (istrm == null)
            {
                // try to load from app directory
                FileInfo file = new FileInfo(resourcePath);
                istrm = file.OpenRead();
                if (istrm == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not load nodes from resource: {0}", resourcePath);
                }
            }

            LoadFromBinary(context, istrm, updateTables);
        }
#endregion
    }

    /// <summary>
    /// A class that creates instances of nodes based on the paramters provided.
    /// </summary>
    public class NodeStateFactory
    {
        /// <summary>
        /// Creates a new instance. 
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="nodeClass">The node class.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="referenceTypeId">The reference type between the parent and the node.</param>
        /// <param name="typeDefinitionId">The type definition.</param>
        /// <returns>Returns null if the type is not known.</returns>
        public virtual NodeState CreateInstance(
            ISystemContext context, 
            NodeState parent,
            NodeClass nodeClass,
            QualifiedName browseName, 
            NodeId referenceTypeId, 
            NodeId typeDefinitionId)
        {
            NodeState child = null;

            if (m_types != null && !NodeId.IsNull(typeDefinitionId))
            {
                Type type = null;

                if (m_types.TryGetValue(typeDefinitionId, out type))
                {
                    return Activator.CreateInstance(type, parent) as NodeState;
                }
            }

            switch (nodeClass)
            {
                case NodeClass.Variable:
                {
                    if (context.TypeTable != null && context.TypeTable.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasProperty))
                    {
                        child = new PropertyState(parent);
                        break;
                    }

                    child = new BaseDataVariableState(parent);
                    break;
                }

                case NodeClass.Object:
                {
                    child = new BaseObjectState(parent);
                    break;
                }

                case NodeClass.Method:
                {
                    child = new MethodState(parent);
                    break;
                }

                case NodeClass.ReferenceType:
                {
                    child = new ReferenceTypeState();
                    break;
                }

                case NodeClass.ObjectType:
                {
                    child = new BaseObjectTypeState();
                    break;
                }

                case NodeClass.VariableType:
                {
                    child = new BaseDataVariableTypeState();
                    break;
                }

                case NodeClass.DataType:
                {
                    child = new DataTypeState();
                    break;
                }

                case NodeClass.View:
                {
                    child = new ViewState();
                    break;
                }

                default:
                {
                    child = null;
                    break;
                }
            }

            return child;
        }

        /// <summary>
        /// Registers a type with the factory.
        /// </summary>
        /// <param name="typeDefinitionId">The type definition.</param>
        /// <param name="type">The system type.</param>
        public void RegisterType(NodeId typeDefinitionId, Type type)
        {
            if (NodeId.IsNull(typeDefinitionId)) throw new ArgumentNullException(nameof(typeDefinitionId));
            if (type == null) throw new ArgumentNullException(nameof(type));
            
            if (m_types == null)
            {
                m_types = new NodeIdDictionary<Type>();
            }

            m_types[typeDefinitionId] = type;
        }

        /// <summary>
        /// Unregisters a type with the factory.
        /// </summary>
        /// <param name="typeDefinitionId">The type definition.</param>
        public void UnRegisterType(NodeId typeDefinitionId)
        {
            if (NodeId.IsNull(typeDefinitionId)) throw new ArgumentNullException(nameof(typeDefinitionId));

            if (m_types != null)
            {
                m_types.Remove(typeDefinitionId);
            }
        }
        
        private NodeIdDictionary<Type> m_types;
    }
}
