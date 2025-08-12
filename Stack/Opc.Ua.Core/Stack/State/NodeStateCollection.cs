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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a collection of nodes.
    /// </summary>
    public partial class NodeStateCollection : List<NodeState>
    {
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
        public NodeStateCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeStateCollection"/> class.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new list.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public NodeStateCollection(IEnumerable<NodeState> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Writes the collection to a stream using the NodeSet schema.
        /// </summary>
        public void SaveAsNodeSet(ISystemContext context, Stream ostrm)
        {
            var nodeTable = new NodeTable(context.NamespaceUris, context.ServerUris, null);

            for (int ii = 0; ii < Count; ii++)
            {
                this[ii].Export(context, nodeTable);
            }

            var nodeSet = new NodeSet();

            foreach (ILocalNode node in nodeTable.OfType<ILocalNode>())
            {
                nodeSet.Add(node, nodeTable.NamespaceUris, nodeTable.ServerUris);
            }

            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.CloseOutput = true;
            using var writer = XmlWriter.Create(ostrm, settings);
            var serializer = new DataContractSerializer(typeof(NodeSet));
            serializer.WriteObject(writer, nodeSet);
        }

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
        private static readonly AliasToUse[] s_aliasesToUse =
        [
            new(BrowseNames.Boolean, DataTypeIds.Boolean),
            new(BrowseNames.SByte, DataTypeIds.SByte),
            new(BrowseNames.Byte, DataTypeIds.Byte),
            new(BrowseNames.Int16, DataTypeIds.Int16),
            new(BrowseNames.UInt16, DataTypeIds.UInt16),
            new(BrowseNames.Int32, DataTypeIds.Int32),
            new(BrowseNames.UInt32, DataTypeIds.UInt32),
            new(BrowseNames.Int64, DataTypeIds.Int64),
            new(BrowseNames.UInt64, DataTypeIds.UInt64),
            new(BrowseNames.Float, DataTypeIds.Float),
            new(BrowseNames.Double, DataTypeIds.Double),
            new(BrowseNames.DateTime, DataTypeIds.DateTime),
            new(BrowseNames.String, DataTypeIds.String),
            new(BrowseNames.ByteString, DataTypeIds.ByteString),
            new(BrowseNames.Guid, DataTypeIds.Guid),
            new(BrowseNames.XmlElement, DataTypeIds.XmlElement),
            new(BrowseNames.NodeId, DataTypeIds.NodeId),
            new(BrowseNames.ExpandedNodeId, DataTypeIds.ExpandedNodeId),
            new(BrowseNames.QualifiedName, DataTypeIds.QualifiedName),
            new(BrowseNames.LocalizedText, DataTypeIds.LocalizedText),
            new(BrowseNames.StatusCode, DataTypeIds.StatusCode),
            new(BrowseNames.Structure, DataTypeIds.Structure),
            new(BrowseNames.Number, DataTypeIds.Number),
            new(BrowseNames.Integer, DataTypeIds.Integer),
            new(BrowseNames.UInteger, DataTypeIds.UInteger),
            new(BrowseNames.HasComponent, ReferenceTypeIds.HasComponent),
            new(BrowseNames.HasProperty, ReferenceTypeIds.HasProperty),
            new(BrowseNames.Organizes, ReferenceTypeIds.Organizes),
            new(BrowseNames.HasEventSource, ReferenceTypeIds.HasEventSource),
            new(BrowseNames.HasNotifier, ReferenceTypeIds.HasNotifier),
            new(BrowseNames.HasSubtype, ReferenceTypeIds.HasSubtype),
            new(BrowseNames.HasTypeDefinition, ReferenceTypeIds.HasTypeDefinition),
            new(BrowseNames.HasModellingRule, ReferenceTypeIds.HasModellingRule),
            new(BrowseNames.HasEncoding, ReferenceTypeIds.HasEncoding),
            new(BrowseNames.HasDescription, ReferenceTypeIds.HasDescription),
            new(BrowseNames.HasCause, ReferenceTypeIds.HasCause),
            new(BrowseNames.ToState, ReferenceTypeIds.ToState),
            new(BrowseNames.FromState, ReferenceTypeIds.FromState),
            new(BrowseNames.HasEffect, ReferenceTypeIds.HasEffect),
            new(BrowseNames.HasTrueSubState, ReferenceTypeIds.HasTrueSubState),
            new(BrowseNames.HasFalseSubState, ReferenceTypeIds.HasFalseSubState),
            new(BrowseNames.HasDictionaryEntry, ReferenceTypeIds.HasDictionaryEntry),
            new(BrowseNames.HasCondition, ReferenceTypeIds.HasCondition),
            new(BrowseNames.HasGuard, ReferenceTypeIds.HasGuard),
            new(BrowseNames.HasAddIn, ReferenceTypeIds.HasAddIn),
            new(BrowseNames.HasInterface, ReferenceTypeIds.HasInterface),
            new(BrowseNames.GeneratesEvent, ReferenceTypeIds.GeneratesEvent),
            new(BrowseNames.AlwaysGeneratesEvent, ReferenceTypeIds.AlwaysGeneratesEvent),
            new(BrowseNames.HasOrderedComponent, ReferenceTypeIds.HasOrderedComponent),
            new(BrowseNames.HasAlarmSuppressionGroup, ReferenceTypeIds.HasAlarmSuppressionGroup),
            new(BrowseNames.AlarmGroupMember, ReferenceTypeIds.AlarmGroupMember),
            new(
                BrowseNames.AlarmSuppressionGroupMember,
                ReferenceTypeIds.AlarmSuppressionGroupMember)
        ];

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
            var nodeSet = new Export.UANodeSet
            {
                LastModified = DateTime.UtcNow,
                LastModifiedSpecified = true
            };

            for (int ii = 0; ii < s_aliasesToUse.Length; ii++)
            {
                nodeSet.AddAlias(context, s_aliasesToUse[ii].Alias, s_aliasesToUse[ii].NodeId);
            }

            for (int ii = 0; ii < Count; ii++)
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
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.CloseOutput = !keepStreamOpen;

            var messageContext = new ServiceMessageContext
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            using var writer = XmlWriter.Create(ostrm, settings);
            var root = new XmlQualifiedName("ListOfNodeState", Namespaces.OpcUaXsd);
            using var encoder = new XmlEncoder(root, writer, messageContext);
            encoder.SaveStringTable("NamespaceUris", "NamespaceUri", context.NamespaceUris);
            encoder.SaveStringTable("ServerUris", "ServerUri", context.ServerUris);

            for (int ii = 0; ii < Count; ii++)
            {
                NodeState state = this[ii];

                state?.SaveAsXml(context, encoder);
            }

            encoder.Close();
        }

        /// <summary>
        /// Writes the collection to a binary stream. The stream is closed by this method.
        /// </summary>
        public void SaveAsBinary(ISystemContext context, Stream ostrm)
        {
            var messageContext = new ServiceMessageContext
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            using var encoder = new BinaryEncoder(ostrm, messageContext, true);
            encoder.SaveStringTable(context.NamespaceUris);
            encoder.SaveStringTable(context.ServerUris);

            encoder.WriteInt32(null, Count);

            for (int ii = 0; ii < Count; ii++)
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
            var messageContext = new ServiceMessageContext
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            using var decoder = new BinaryDecoder(istrm, messageContext);
            // check if a namespace table was provided.
            var namespaceUris = new NamespaceTable();

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
            var serverUris = new StringTable();

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
                var state = NodeState.LoadNode(context, decoder);
                Add(state);
            }
        }

        /// <summary>
        /// Reads the schema information from a XML document.
        /// </summary>
        public void LoadFromXml(ISystemContext context, Stream istrm, bool updateTables)
        {
            var messageContext = new ServiceMessageContext
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            using var reader = XmlReader.Create(istrm, Utils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(null, reader, messageContext);
            var namespaceUris = new NamespaceTable();

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

            var serverUris = new StringTable();

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

            var state = NodeState.LoadNode(context, decoder);

            while (state != null)
            {
                Add(state);

                state = NodeState.LoadNode(context, decoder);
            }

            decoder.Close();
        }

        /// <summary>
        /// Loads the nodes from an embedded resource.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="resourcePath">The resource path.</param>
        /// <param name="assembly">The assembly containing the resource.</param>
        /// <param name="updateTables">if set to <c>true</c> the namespace and server tables are updated with any new URIs.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resourcePath"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void LoadFromResource(
            ISystemContext context,
            string resourcePath,
            Assembly assembly,
            bool updateTables)
        {
            if (resourcePath == null)
            {
                throw new ArgumentNullException(nameof(resourcePath));
            }

            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            Stream istrm = assembly.GetManifestResourceStream(resourcePath);
            if (istrm == null)
            {
                // try to load from app directory
                var file = new FileInfo(resourcePath);
                istrm = file.OpenRead();
                if (istrm == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Could not load nodes from resource: {0}",
                        resourcePath
                    );
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
        /// <exception cref="ArgumentNullException"><paramref name="resourcePath"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void LoadFromBinaryResource(
            ISystemContext context,
            string resourcePath,
            Assembly assembly,
            bool updateTables
        )
        {
            if (resourcePath == null)
            {
                throw new ArgumentNullException(nameof(resourcePath));
            }

            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            Stream istrm = assembly.GetManifestResourceStream(resourcePath);
            if (istrm == null)
            {
                // try to load from app directory
                var file = new FileInfo(resourcePath);
                istrm = file.OpenRead();
                if (istrm == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Could not load nodes from resource: {0}",
                        resourcePath
                    );
                }
            }

            LoadFromBinary(context, istrm, updateTables);
        }
    }

    /// <summary>
    /// A class that creates instances of nodes based on the parameters provided.
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
            NodeId typeDefinitionId
        )
        {
            if (
                m_types != null &&
                !NodeId.IsNull(typeDefinitionId) &&
                m_types.TryGetValue(typeDefinitionId, out Type type)
            )
            {
                return Activator.CreateInstance(type, parent) as NodeState;
            }

            NodeState child;
            switch (nodeClass)
            {
                case NodeClass.Variable:
                    if (
                        context.TypeTable != null &&
                        context.TypeTable.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasProperty)
                    )
                    {
                        child = new PropertyState(parent);
                        break;
                    }

                    child = new BaseDataVariableState(parent);
                    break;
                case NodeClass.Object:
                    child = new BaseObjectState(parent);
                    break;
                case NodeClass.Method:
                    child = new MethodState(parent);
                    break;
                case NodeClass.ReferenceType:
                    child = new ReferenceTypeState();
                    break;
                case NodeClass.ObjectType:
                    child = new BaseObjectTypeState();
                    break;
                case NodeClass.VariableType:
                    child = new BaseDataVariableTypeState();
                    break;
                case NodeClass.DataType:
                    child = new DataTypeState();
                    break;
                case NodeClass.View:
                    child = new ViewState();
                    break;
                default:
                    child = null;
                    break;
            }

            return child;
        }

        /// <summary>
        /// Registers a type with the factory.
        /// </summary>
        /// <param name="typeDefinitionId">The type definition.</param>
        /// <param name="type">The system type.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void RegisterType(NodeId typeDefinitionId, Type type)
        {
            if (NodeId.IsNull(typeDefinitionId))
            {
                throw new ArgumentNullException(nameof(typeDefinitionId));
            }

            m_types ??= [];

            m_types[typeDefinitionId] = type ?? throw new ArgumentNullException(nameof(type));
        }

        /// <summary>
        /// Unregisters a type with the factory.
        /// </summary>
        /// <param name="typeDefinitionId">The type definition.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void UnRegisterType(NodeId typeDefinitionId)
        {
            if (NodeId.IsNull(typeDefinitionId))
            {
                throw new ArgumentNullException(nameof(typeDefinitionId));
            }

            m_types?.Remove(typeDefinitionId);
        }

        private NodeIdDictionary<Type> m_types;
    }
}
