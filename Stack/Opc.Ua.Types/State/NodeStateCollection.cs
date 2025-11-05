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

            var messageContext = new ServiceMessageContext(context.Telemetry)
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
            var messageContext = new ServiceMessageContext(context.Telemetry)
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
        public void LoadFromXml(ISystemContext context, Stream istrm, bool updateTables)
        {
            var messageContext = new ServiceMessageContext(context.Telemetry)
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
        /// <exception cref="ServiceResultException"></exception>
        public virtual NodeState CreateInstance(
            ISystemContext context,
            NodeState parent,
            NodeClass nodeClass,
            QualifiedName browseName,
            NodeId referenceTypeId,
            NodeId typeDefinitionId)
        {
            NodeState child;

            {
                switch (nodeClass)
                {
                    case NodeClass.Variable:
                        if (context.TypeTable != null &&
                            context.TypeTable.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasProperty))
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
                    case NodeClass.Unspecified:
                        child = null;
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected NodeClass {nodeClass}");
                }
            }
            return child;
        }
    }
}
