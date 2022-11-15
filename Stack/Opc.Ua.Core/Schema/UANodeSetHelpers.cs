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
using System.Text;
using System.Xml;
using System.Xml.Serialization;


namespace Opc.Ua.Export
{
    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    public partial class UANodeSet
    {
        #region Constructors
        /// <summary>
        /// Creates an empty nodeset.
        /// </summary>
        public UANodeSet()
        {
        }

        /// <summary>
        /// Loads a nodeset from a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        /// <returns>The set of nodes</returns>
        public static UANodeSet Read(Stream istrm)
        {
            using (StreamReader reader = new StreamReader(istrm))
            using (XmlReader xmlReader = XmlReader.Create(reader, Utils.DefaultXmlReaderSettings()))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(UANodeSet));
                return serializer.Deserialize(xmlReader) as UANodeSet;
            }
        }

        /// <summary>
        /// Write a nodeset to a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        public void Write(Stream istrm)
        {
            XmlWriterSettings setting = Utils.DefaultXmlWriterSettings();
            XmlWriter writer = XmlWriter.Create(istrm, setting);

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(UANodeSet));
                serializer.Serialize(writer, this, null);
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Adds an alias to the node set.
        /// </summary>
        public void AddAlias(ISystemContext context, string alias, Opc.Ua.NodeId nodeId)
        {
            int count = 1;

            if (this.Aliases != null)
            {
                for (int ii = 0; ii < this.Aliases.Length; ii++)
                {
                    if (this.Aliases[ii].Alias == alias)
                    {
                        this.Aliases[ii].Value = Export(nodeId, context.NamespaceUris);
                        return;
                    }
                }

                count += this.Aliases.Length;
            }

            NodeIdAlias[] aliases = new NodeIdAlias[count];

            if (this.Aliases != null)
            {
                Array.Copy(this.Aliases, aliases, this.Aliases.Length);
            }

            aliases[count - 1] = new NodeIdAlias() { Alias = alias, Value = Export(nodeId, context.NamespaceUris) };
            this.Aliases = aliases;
        }

        /// <summary>
        /// Imports a node from the set.
        /// </summary>
        public void Import(ISystemContext context, NodeStateCollection nodes)
        {
            for (int ii = 0; ii < this.Items.Length; ii++)
            {
                UANode node = this.Items[ii];
                NodeState importedNode = Import(context, node);
                nodes.Add(importedNode);
            }
        }

        /// <summary>
        /// Adds a node to the set.
        /// </summary>
        public void Export(ISystemContext context, NodeState node, bool outputRedundantNames = true)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            if (Opc.Ua.NodeId.IsNull(node.NodeId))
            {
                throw new ArgumentException("A non-null NodeId must be specified.");
            }

            UANode exportedNode = null;

            switch (node.NodeClass)
            {
                case NodeClass.Object:
                {
                    BaseObjectState o = (BaseObjectState)node;
                    UAObject value = new UAObject();
                    value.EventNotifier = o.EventNotifier;

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    exportedNode = value;
                    break;
                }

                case NodeClass.Variable:
                {
                    BaseVariableState o = (BaseVariableState)node;
                    UAVariable value = new UAVariable();
                    value.DataType = ExportAlias(o.DataType, context.NamespaceUris);
                    value.ValueRank = o.ValueRank;
                    value.ArrayDimensions = Export(o.ArrayDimensions);
                    value.AccessLevel = o.AccessLevelEx;
                    value.MinimumSamplingInterval = o.MinimumSamplingInterval;
                    value.Historizing = o.Historizing;

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    if (o.Value != null)
                    {
                        XmlEncoder encoder = CreateEncoder(context);

                        Variant variant = new Variant(o.Value);
                        encoder.WriteVariantContents(variant.Value, variant.TypeInfo);

                        XmlDocument document = new XmlDocument();
                        document.LoadInnerXml(encoder.Close());
                        value.Value = document.DocumentElement;
                    }

                    exportedNode = value;
                    break;
                }

                case NodeClass.Method:
                {
                    MethodState o = (MethodState)node;
                    UAMethod value = new UAMethod();
                    value.Executable = o.Executable;

                    if (o.MethodDeclarationId != null && !o.MethodDeclarationId.IsNullNodeId && o.MethodDeclarationId != o.NodeId)
                    {
                        value.MethodDeclarationId = Export(o.MethodDeclarationId, context.NamespaceUris);
                    }

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    exportedNode = value;
                    break;
                }

                case NodeClass.View:
                {
                    ViewState o = (ViewState)node;
                    UAView value = new UAView();
                    value.ContainsNoLoops = o.ContainsNoLoops;
                    exportedNode = value;
                    break;
                }

                case NodeClass.ObjectType:
                {
                    BaseObjectTypeState o = (BaseObjectTypeState)node;
                    UAObjectType value = new UAObjectType();
                    value.IsAbstract = o.IsAbstract;
                    exportedNode = value;
                    break;
                }

                case NodeClass.VariableType:
                {
                    BaseVariableTypeState o = (BaseVariableTypeState)node;
                    UAVariableType value = new UAVariableType();
                    value.IsAbstract = o.IsAbstract;
                    value.DataType = ExportAlias(o.DataType, context.NamespaceUris);
                    value.ValueRank = o.ValueRank;
                    value.ArrayDimensions = Export(o.ArrayDimensions);

                    if (o.Value != null)
                    {
                        XmlEncoder encoder = CreateEncoder(context);

                        Variant variant = new Variant(o.Value);
                        encoder.WriteVariantContents(variant.Value, variant.TypeInfo);

                        XmlDocument document = new XmlDocument();
                        document.LoadInnerXml(encoder.Close());
                        value.Value = document.DocumentElement;
                    }

                    exportedNode = value;
                    break;
                }

                case NodeClass.DataType:
                {
                    DataTypeState o = (DataTypeState)node;
                    UADataType value = new UADataType();
                    value.IsAbstract = o.IsAbstract;
                    value.Definition = Export(o, o.DataTypeDefinition, context.NamespaceUris, outputRedundantNames);
                    value.Purpose = o.Purpose;
                    exportedNode = value;
                    break;
                }

                case NodeClass.ReferenceType:
                {
                    ReferenceTypeState o = (ReferenceTypeState)node;
                    UAReferenceType value = new UAReferenceType();
                    value.IsAbstract = o.IsAbstract;

                    if (!Opc.Ua.LocalizedText.IsNullOrEmpty(o.InverseName))
                    {
                        value.InverseName = Export(new Opc.Ua.LocalizedText[] { o.InverseName });
                    }

                    value.Symmetric = o.Symmetric;
                    exportedNode = value;
                    break;
                }
            }

            exportedNode.NodeId = Export(node.NodeId, context.NamespaceUris);
            exportedNode.BrowseName = Export(node.BrowseName, context.NamespaceUris);

            if (outputRedundantNames || node.DisplayName.Text != node.BrowseName.Name)
            {
                exportedNode.DisplayName = Export(new Opc.Ua.LocalizedText[] { node.DisplayName });
            }
            else
            {
                exportedNode.DisplayName = null;
            }

            if (node.Description != null && !String.IsNullOrEmpty(node.Description.Text))
            {
                exportedNode.Description = Export(new Opc.Ua.LocalizedText[] { node.Description });
            }
            else
            {
                exportedNode.Description = Array.Empty<LocalizedText>();
            }

            exportedNode.Documentation = node.NodeSetDocumentation;
            exportedNode.Category = (node.Categories != null && node.Categories.Count > 0) ? new List<string>(node.Categories).ToArray() : null;
            exportedNode.ReleaseStatus = node.ReleaseStatus;
            exportedNode.WriteMask = (uint)node.WriteMask;
            exportedNode.UserWriteMask = (uint)node.UserWriteMask;
            exportedNode.Extensions = node.Extensions;

            if (!String.IsNullOrEmpty(node.SymbolicName) && node.SymbolicName != node.BrowseName.Name)
            {
                exportedNode.SymbolicName = node.SymbolicName;
            }

            // export references.
            INodeBrowser browser = node.CreateBrowser(context, null, null, true, BrowseDirection.Both, null, null, true);
            List<Reference> exportedReferences = new List<Reference>();
            IReference reference = browser.Next();

            while (reference != null)
            {
                if (node.NodeClass == NodeClass.Method)
                {
                    if (!reference.IsInverse && reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                    {
                        reference = browser.Next();
                        continue;
                    }
                }

                Reference exportedReference = new Reference();

                exportedReference.ReferenceType = ExportAlias(reference.ReferenceTypeId, context.NamespaceUris);
                exportedReference.IsForward = !reference.IsInverse;
                exportedReference.Value = Export(reference.TargetId, context.NamespaceUris, context.ServerUris);
                exportedReferences.Add(exportedReference);

                reference = browser.Next();
            }

            exportedNode.References = exportedReferences.ToArray();

            // add node to list.
            UANode[] nodes = null;

            int count = 1;

            if (this.Items == null)
            {
                nodes = new UANode[count];
            }
            else
            {
                count += this.Items.Length;
                nodes = new UANode[count];
                Array.Copy(this.Items, nodes, this.Items.Length);
            }

            nodes[count - 1] = exportedNode;

            this.Items = nodes;

            // recusively process children.
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            node.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                Export(context, children[ii], outputRedundantNames);
            }
        }
        #endregion

        #region Private Members
        /// <summary>
        /// Creates an encoder to save Variant values.
        /// </summary>
        private XmlEncoder CreateEncoder(ISystemContext context)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext() {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            XmlEncoder encoder = new XmlEncoder(messageContext);

            NamespaceTable namespaceUris = new NamespaceTable();

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    namespaceUris.GetIndexOrAppend(NamespaceUris[ii]);
                }
            }

            StringTable serverUris = new StringTable();

            if (ServerUris != null)
            {
                for (int ii = 0; ii < ServerUris.Length; ii++)
                {
                    serverUris.GetIndexOrAppend(ServerUris[ii]);
                }
            }

            encoder.SetMappingTables(namespaceUris, serverUris);

            return encoder;
        }

        /// <summary>
        /// Creates an decoder to restore Variant values.
        /// </summary>
        private XmlDecoder CreateDecoder(ISystemContext context, XmlElement source)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext() {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            XmlDecoder decoder = new XmlDecoder(source, messageContext);

            NamespaceTable namespaceUris = new NamespaceTable();

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    namespaceUris.GetIndexOrAppend(NamespaceUris[ii]);
                }
            }

            StringTable serverUris = new StringTable();

            if (ServerUris != null)
            {
                for (int ii = 0; ii < ServerUris.Length; ii++)
                {
                    serverUris.GetIndexOrAppend(ServerUris[ii]);
                }
            }

            decoder.SetMappingTables(namespaceUris, serverUris);

            return decoder;
        }

        /// <summary>
        /// Imports a node from the set.
        /// </summary>
        private NodeState Import(ISystemContext context, UANode node)
        {
            NodeState importedNode = null;

            NodeClass nodeClass = NodeClass.Unspecified;

            if (node is UAObject) nodeClass = NodeClass.Object;
            else if (node is UAVariable) nodeClass = NodeClass.Variable;
            else if (node is UAMethod) nodeClass = NodeClass.Method;
            else if (node is UAObjectType) nodeClass = NodeClass.ObjectType;
            else if (node is UAVariableType) nodeClass = NodeClass.VariableType;
            else if (node is UADataType) nodeClass = NodeClass.DataType;
            else if (node is UAReferenceType) nodeClass = NodeClass.ReferenceType;
            else if (node is UAView) nodeClass = NodeClass.View;

            switch (nodeClass)
            {
                case NodeClass.Object:
                {
                    UAObject o = (UAObject)node;
                    BaseObjectState value = new BaseObjectState(null);
                    value.EventNotifier = o.EventNotifier;
                    importedNode = value;
                    break;
                }

                case NodeClass.Variable:
                {
                    UAVariable o = (UAVariable)node;

                    NodeId typeDefinitionId = null;

                    if (node.References != null)
                    {
                        for (int ii = 0; ii < node.References.Length; ii++)
                        {
                            Opc.Ua.NodeId referenceTypeId = ImportNodeId(node.References[ii].ReferenceType, context.NamespaceUris, true);
                            bool isInverse = !node.References[ii].IsForward;
                            Opc.Ua.ExpandedNodeId targetId = ImportExpandedNodeId(node.References[ii].Value, context.NamespaceUris, context.ServerUris);

                            if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition && !isInverse)
                            {
                                typeDefinitionId = Opc.Ua.ExpandedNodeId.ToNodeId(targetId, context.NamespaceUris);
                                break;
                            }
                        }
                    }

                    BaseVariableState value = null;

                    if (typeDefinitionId == Opc.Ua.VariableTypeIds.PropertyType)
                    {
                        value = new PropertyState(null);
                    }
                    else
                    {
                        value = new BaseDataVariableState(null);
                    }

                    value.DataType = ImportNodeId(o.DataType, context.NamespaceUris, true);
                    value.ValueRank = o.ValueRank;
                    value.ArrayDimensions = ImportArrayDimensions(o.ArrayDimensions);
                    value.AccessLevelEx = o.AccessLevel;
                    value.UserAccessLevel = (byte)(o.AccessLevel & 0xFF);
                    value.MinimumSamplingInterval = o.MinimumSamplingInterval;
                    value.Historizing = o.Historizing;

                    if (o.Value != null)
                    {
                        XmlDecoder decoder = CreateDecoder(context, o.Value);
                        TypeInfo typeInfo = null;
                        value.Value = decoder.ReadVariantContents(out typeInfo);
                        decoder.Close();
                    }

                    importedNode = value;
                    break;
                }

                case NodeClass.Method:
                {
                    UAMethod o = (UAMethod)node;
                    MethodState value = new MethodState(null);
                    value.Executable = o.Executable;
                    value.UserExecutable = o.Executable;
                    value.MethodDeclarationId = ImportNodeId(o.MethodDeclarationId, context.NamespaceUris, true);
                    importedNode = value;
                    break;
                }

                case NodeClass.View:
                {
                    UAView o = (UAView)node;
                    ViewState value = new ViewState();
                    value.ContainsNoLoops = o.ContainsNoLoops;
                    importedNode = value;
                    break;
                }

                case NodeClass.ObjectType:
                {
                    UAObjectType o = (UAObjectType)node;
                    BaseObjectTypeState value = new BaseObjectTypeState();
                    value.IsAbstract = o.IsAbstract;
                    importedNode = value;
                    break;
                }

                case NodeClass.VariableType:
                {
                    UAVariableType o = (UAVariableType)node;
                    BaseVariableTypeState value = new BaseDataVariableTypeState();
                    value.IsAbstract = o.IsAbstract;
                    value.DataType = ImportNodeId(o.DataType, context.NamespaceUris, true);
                    value.ValueRank = o.ValueRank;
                    value.ArrayDimensions = ImportArrayDimensions(o.ArrayDimensions);

                    if (o.Value != null)
                    {
                        XmlDecoder decoder = CreateDecoder(context, o.Value);
                        TypeInfo typeInfo = null;
                        value.Value = decoder.ReadVariantContents(out typeInfo);
                        decoder.Close();
                    }

                    importedNode = value;
                    break;
                }

                case NodeClass.DataType:
                {
                    UADataType o = (UADataType)node;
                    DataTypeState value = new DataTypeState();
                    value.IsAbstract = o.IsAbstract;
                    Opc.Ua.DataTypeDefinition dataTypeDefinition = Import(o, o.Definition, context.NamespaceUris);
                    value.DataTypeDefinition = new ExtensionObject(dataTypeDefinition);
                    value.Purpose = o.Purpose;
                    importedNode = value;
                    break;
                }

                case NodeClass.ReferenceType:
                {
                    UAReferenceType o = (UAReferenceType)node;
                    ReferenceTypeState value = new ReferenceTypeState();
                    value.IsAbstract = o.IsAbstract;
                    value.InverseName = Import(o.InverseName);
                    value.Symmetric = o.Symmetric;
                    importedNode = value;
                    break;
                }
            }

            importedNode.NodeId = ImportNodeId(node.NodeId, context.NamespaceUris, false);
            importedNode.BrowseName = ImportQualifiedName(node.BrowseName, context.NamespaceUris);
            importedNode.DisplayName = Import(node.DisplayName);

            if (importedNode.DisplayName == null)
            {
                importedNode.DisplayName = new Ua.LocalizedText(importedNode.BrowseName.Name);
            }

            importedNode.Description = Import(node.Description);
            importedNode.NodeSetDocumentation = node.Documentation;
            importedNode.Categories = (node.Category != null && node.Category.Length > 0) ? node.Category : null;
            importedNode.ReleaseStatus = node.ReleaseStatus;
            importedNode.WriteMask = (AttributeWriteMask)node.WriteMask;
            importedNode.UserWriteMask = (AttributeWriteMask)node.UserWriteMask;
            importedNode.Extensions = node.Extensions;

            if (!String.IsNullOrEmpty(node.SymbolicName))
            {
                importedNode.SymbolicName = node.SymbolicName;
            }

            if (node.References != null)
            {
                BaseInstanceState instance = importedNode as BaseInstanceState;
                BaseTypeState type = importedNode as BaseTypeState;

                for (int ii = 0; ii < node.References.Length; ii++)
                {
                    Opc.Ua.NodeId referenceTypeId = ImportNodeId(node.References[ii].ReferenceType, context.NamespaceUris, true);
                    bool isInverse = !node.References[ii].IsForward;
                    Opc.Ua.ExpandedNodeId targetId = ImportExpandedNodeId(node.References[ii].Value, context.NamespaceUris, context.ServerUris);

                    if (instance != null)
                    {
                        if (referenceTypeId == ReferenceTypeIds.HasModellingRule && !isInverse)
                        {
                            instance.ModellingRuleId = Opc.Ua.ExpandedNodeId.ToNodeId(targetId, context.NamespaceUris);
                            continue;
                        }

                        if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition && !isInverse)
                        {
                            instance.TypeDefinitionId = Opc.Ua.ExpandedNodeId.ToNodeId(targetId, context.NamespaceUris);
                            continue;
                        }
                    }

                    if (type != null)
                    {
                        if (referenceTypeId == ReferenceTypeIds.HasSubtype && isInverse)
                        {
                            type.SuperTypeId = Opc.Ua.ExpandedNodeId.ToNodeId(targetId, context.NamespaceUris);
                            continue;
                        }
                    }

                    importedNode.AddReference(referenceTypeId, isInverse, targetId);
                }
            }

            string parentNodeId = (node as UAInstance)?.ParentNodeId;

            if (!String.IsNullOrEmpty(parentNodeId))
            {
                // set parent NodeId in Handle property.
                importedNode.Handle = ImportNodeId(parentNodeId, context.NamespaceUris, true);
            }

            return importedNode;
        }

        /// <summary>
        /// Exports a NodeId as an alias.
        /// </summary>
        private string ExportAlias(Opc.Ua.NodeId source, NamespaceTable namespaceUris)
        {
            string nodeId = Export(source, namespaceUris);

            if (!String.IsNullOrEmpty(nodeId))
            {
                if (this.Aliases != null)
                {
                    for (int ii = 0; ii < this.Aliases.Length; ii++)
                    {
                        if (this.Aliases[ii].Value == nodeId)
                        {
                            return this.Aliases[ii].Alias;
                        }
                    }
                }
            }

            return nodeId;
        }

        /// <summary>
        /// Exports a NodeId
        /// </summary>
        private string Export(Opc.Ua.NodeId source, NamespaceTable namespaceUris)
        {
            if (Opc.Ua.NodeId.IsNull(source))
            {
                return String.Empty;
            }

            if (source.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
                source = new Opc.Ua.NodeId(source.Identifier, namespaceIndex);
            }

            return source.ToString();
        }

        /// <summary>
        ///  Imports a NodeId
        /// </summary>
        private Opc.Ua.NodeId ImportNodeId(string source, NamespaceTable namespaceUris, bool lookupAlias)
        {
            if (String.IsNullOrEmpty(source))
            {
                return Opc.Ua.NodeId.Null;
            }

            // lookup alias.
            if (lookupAlias && this.Aliases != null)
            {
                for (int ii = 0; ii < this.Aliases.Length; ii++)
                {
                    if (this.Aliases[ii].Alias == source)
                    {
                        source = this.Aliases[ii].Value;
                        break;
                    }
                }
            }

            // parse the string.
            Opc.Ua.NodeId nodeId = Opc.Ua.NodeId.Parse(source);

            if (nodeId.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(nodeId.NamespaceIndex, namespaceUris);
                nodeId = new Opc.Ua.NodeId(nodeId.Identifier, namespaceIndex);
            }

            return nodeId;
        }

        /// <summary>
        /// Exports a ExpandedNodeId
        /// </summary>
        private string Export(Opc.Ua.ExpandedNodeId source, NamespaceTable namespaceUris, StringTable serverUris)
        {
            if (Opc.Ua.NodeId.IsNull(source))
            {
                return String.Empty;
            }

            if (source.ServerIndex <= 0 && source.NamespaceIndex <= 0 && String.IsNullOrEmpty(source.NamespaceUri))
            {
                return source.ToString();
            }

            ushort namespaceIndex = 0;

            if (String.IsNullOrEmpty(source.NamespaceUri))
            {
                namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
            }
            else
            {
                namespaceIndex = ExportNamespaceUri(source.NamespaceUri, namespaceUris);
            }

            uint serverIndex = ExportServerIndex(source.ServerIndex, serverUris);
            source = new Opc.Ua.ExpandedNodeId(source.Identifier, namespaceIndex, null, serverIndex);
            return source.ToString();
        }

        /// <summary>
        /// Imports a ExpandedNodeId
        /// </summary>
        private Opc.Ua.ExpandedNodeId ImportExpandedNodeId(string source, NamespaceTable namespaceUris, StringTable serverUris)
        {
            if (String.IsNullOrEmpty(source))
            {
                return Opc.Ua.ExpandedNodeId.Null;
            }
            // lookup aliases
            if (this.Aliases != null)
            {
                for (int ii = 0; ii < this.Aliases.Length; ii++)
                {
                    if (this.Aliases[ii].Alias == source)
                    {
                        source = this.Aliases[ii].Value;
                        break;
                    }
                }
            }

            // parse the node.
            Opc.Ua.ExpandedNodeId nodeId = Opc.Ua.ExpandedNodeId.Parse(source);

            if (nodeId.ServerIndex <= 0 && nodeId.NamespaceIndex <= 0 && String.IsNullOrEmpty(nodeId.NamespaceUri))
            {
                return nodeId;
            }

            uint serverIndex = ImportServerIndex(nodeId.ServerIndex, serverUris);
            ushort namespaceIndex = ImportNamespaceIndex(nodeId.NamespaceIndex, namespaceUris);

            if (serverIndex > 0)
            {
                string namespaceUri = nodeId.NamespaceUri;

                if (String.IsNullOrEmpty(nodeId.NamespaceUri))
                {
                    namespaceUri = namespaceUris.GetString(namespaceIndex);
                }

                nodeId = new Opc.Ua.ExpandedNodeId(nodeId.Identifier, 0, namespaceUri, serverIndex);
                return nodeId;
            }


            nodeId = new Opc.Ua.ExpandedNodeId(nodeId.Identifier, namespaceIndex, null, 0);
            return nodeId;
        }

        /// <summary>
        /// Exports a QualifiedName
        /// </summary>
        private string Export(Opc.Ua.QualifiedName source, NamespaceTable namespaceUris)
        {
            if (Opc.Ua.QualifiedName.IsNull(source))
            {
                return String.Empty;
            }

            if (source.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
                source = new Opc.Ua.QualifiedName(source.Name, namespaceIndex);
            }

            return source.ToString();
        }

        /// <summary>
        /// Exports a DataTypeDefinition
        /// </summary>
        private Opc.Ua.Export.DataTypeDefinition Export(
            DataTypeState dataType,
            ExtensionObject source,
            NamespaceTable namespaceUris,
            bool outputRedundantNames)
        {
            if (source == null || source.Body == null)
            {
                return null;
            }

            DataTypeDefinition definition = new DataTypeDefinition();

            if (outputRedundantNames || dataType.BrowseName != null)
            {
                definition.Name = Export(dataType.BrowseName, namespaceUris);
            }

            if (dataType.BrowseName.Name != dataType.SymbolicName)
            {
                definition.SymbolicName = dataType.SymbolicName;
            }

            StructureDefinition sd = source.Body as StructureDefinition;

            if (sd != null)
            {
                if (sd.StructureType == StructureType.Union || sd.StructureType == StructureType.UnionWithSubtypedValues)
                {
                    definition.IsUnion = true;
                }

                if (sd.Fields != null)
                {
                    List<Opc.Ua.Export.DataTypeField> fields = new List<DataTypeField>();

                    for (int ii = sd.FirstExplicitFieldIndex; ii < sd.Fields.Count; ii++)
                    {
                        StructureField field = sd.Fields[ii];

                        Opc.Ua.Export.DataTypeField output = new Opc.Ua.Export.DataTypeField();

                        output.Name = field.Name;
                        output.Description = Export(new Opc.Ua.LocalizedText[] { field.Description });

                        if (sd.StructureType == StructureType.StructureWithOptionalFields)
                        {
                            output.IsOptional = field.IsOptional;
                            output.AllowSubTypes = false;
                        }
                        else if (sd.StructureType == StructureType.StructureWithSubtypedValues ||
                                 sd.StructureType == StructureType.UnionWithSubtypedValues)
                        {
                            output.IsOptional = false;
                            output.AllowSubTypes = field.IsOptional;
                        }
                        else
                        {
                            output.IsOptional = false;
                            output.AllowSubTypes = false;
                        }

                        if (NodeId.IsNull(field.DataType))
                        {
                            output.DataType = Export(DataTypeIds.BaseDataType, namespaceUris);
                        }
                        else
                        {
                            output.DataType = Export(field.DataType, namespaceUris);
                        }

                        output.ValueRank = field.ValueRank;

                        if (field.ArrayDimensions != null && field.ArrayDimensions.Count != 0)
                        {
                            if (output.ValueRank > 1 || field.ArrayDimensions[0] > 0)
                            {
                                output.ArrayDimensions = BaseVariableState.ArrayDimensionsToXml(field.ArrayDimensions);
                            }
                        }

                        output.MaxStringLength = field.MaxStringLength;

                        fields.Add(output);
                    }

                    definition.Field = fields.ToArray();
                }
            }

            EnumDefinition ed = source.Body as EnumDefinition;

            if (ed != null)
            {
                definition.IsOptionSet = ed.IsOptionSet;

                if (ed.Fields != null)
                {
                    List<Opc.Ua.Export.DataTypeField> fields = new List<DataTypeField>();

                    foreach (EnumField field in ed.Fields)
                    {
                        Opc.Ua.Export.DataTypeField output = new Opc.Ua.Export.DataTypeField();

                        output.Name = field.Name;

                        if (field.DisplayName != null && output.Name != field.DisplayName.Text)
                        {
                            output.DisplayName = Export(new Opc.Ua.LocalizedText[] { field.DisplayName });
                        }
                        else
                        {
                            output.DisplayName = Array.Empty<LocalizedText>();
                        }

                        output.Description = Export(new Opc.Ua.LocalizedText[] { field.Description });
                        output.ValueRank = ValueRanks.Scalar;
                        output.Value = (int)field.Value;

                        fields.Add(output);
                    }

                    definition.Field = fields.ToArray();
                }
            }

            return definition;
        }

        /// <summary>
        /// Imports a DataTypeDefinition
        /// </summary>
        private Opc.Ua.DataTypeDefinition Import(UADataType dataType, Opc.Ua.Export.DataTypeDefinition source, NamespaceTable namespaceUris)
        {
            if (source == null)
            {
                return null;
            }

            Opc.Ua.DataTypeDefinition definition = null;

            if (source.Field != null)
            {
                // check if definition is for enumeration or structure.
                bool isEnumeration = Array.Exists<DataTypeField>(source.Field, delegate (DataTypeField fieldLookup) {
                    return fieldLookup.Value != -1;
                });

                if (!isEnumeration)
                {
                    StructureDefinition sd = new StructureDefinition();
                    sd.BaseDataType = ImportNodeId(source.BaseType, namespaceUris, true);

                    if (source.IsUnion)
                    {
                        sd.StructureType = StructureType.Union;
                    }

                    if (source.Field != null)
                    {
                        List<StructureField> fields = new List<StructureField>();

                        foreach (DataTypeField field in source.Field)
                        {
                            if (sd.StructureType == StructureType.Structure ||
                                sd.StructureType == StructureType.Union)
                            {
                                if (field.IsOptional)
                                {
                                    sd.StructureType = StructureType.StructureWithOptionalFields;
                                }
                                else if (field.AllowSubTypes)
                                {
                                    if (source.IsUnion)
                                    {
                                        sd.StructureType = StructureType.UnionWithSubtypedValues;
                                    }
                                    else
                                    {
                                        sd.StructureType = StructureType.StructureWithSubtypedValues;
                                    }
                                }
                            }

                            StructureField output = new StructureField();

                            output.Name = field.Name;
                            output.Description = Import(field.Description);
                            output.DataType = ImportNodeId(field.DataType, namespaceUris, true);
                            output.ValueRank = field.ValueRank;
                            if (!String.IsNullOrWhiteSpace(field.ArrayDimensions))
                            {
                                if (output.ValueRank > 1 || field.ArrayDimensions[0] > 0)
                                {
                                    output.ArrayDimensions = new UInt32Collection(BaseVariableState.ArrayDimensionsFromXml(field.ArrayDimensions));
                                }
                            }

                            output.MaxStringLength = field.MaxStringLength;

                            if (sd.StructureType == StructureType.Structure ||
                                sd.StructureType == StructureType.Union)
                            {
                                output.IsOptional = false;
                            }
                            else if (sd.StructureType == StructureType.StructureWithSubtypedValues ||
                                    sd.StructureType == StructureType.UnionWithSubtypedValues)
                            {
                                output.IsOptional = field.AllowSubTypes;
                            }
                            else
                            {
                                output.IsOptional = field.IsOptional;
                            }

                            fields.Add(output);
                        }

                        sd.Fields = fields.ToArray();
                    }

                    definition = sd;
                }
                else
                {
                    EnumDefinition ed = new EnumDefinition();
                    ed.IsOptionSet = source.IsOptionSet;

                    if (source.Field != null)
                    {
                        List<EnumField> fields = new List<EnumField>();

                        foreach (DataTypeField field in source.Field)
                        {
                            EnumField output = new EnumField();

                            output.Name = field.Name;
                            output.DisplayName = Import(field.DisplayName);
                            output.Description = Import(field.Description);
                            output.Value = field.Value;

                            fields.Add(output);
                        }

                        ed.Fields = fields.ToArray();
                    }

                    definition = ed;
                }
            }

            return definition;
        }

        /// <summary>
        /// Imports a QualifiedName
        /// </summary>
        private Opc.Ua.QualifiedName ImportQualifiedName(string source, NamespaceTable namespaceUris)
        {
            if (String.IsNullOrEmpty(source))
            {
                return Opc.Ua.QualifiedName.Null;
            }

            Opc.Ua.QualifiedName qname = Opc.Ua.QualifiedName.Parse(source);

            if (qname.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(qname.NamespaceIndex, namespaceUris);
                qname = new Opc.Ua.QualifiedName(qname.Name, namespaceIndex);
            }

            return qname;
        }

        /// <summary>
        /// Exports the array dimensions.
        /// </summary>
        private string Export(IList<uint> arrayDimensions)
        {
            if (arrayDimensions == null)
            {
                return String.Empty;
            }

            StringBuilder buffer = new StringBuilder();

            for (int ii = 0; ii < arrayDimensions.Count; ii++)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(',');
                }

                buffer.Append(arrayDimensions[ii]);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Imports the array dimensions.
        /// </summary>
        private uint[] ImportArrayDimensions(string arrayDimensions)
        {
            if (String.IsNullOrEmpty(arrayDimensions))
            {
                return null;
            }

            string[] fields = arrayDimensions.Split(',');
            uint[] dimensions = new uint[fields.Length];

            for (int ii = 0; ii < fields.Length; ii++)
            {
                try
                {
                    dimensions[ii] = Convert.ToUInt32(fields[ii]);
                }
                catch
                {
                    dimensions[ii] = 0;
                }
            }

            return dimensions;
        }

        /// <summary>
        /// Exports localized text.
        /// </summary>
        private Opc.Ua.Export.LocalizedText[] Export(Opc.Ua.LocalizedText[] input)
        {
            if (input == null)
            {
                return null;
            }

            List<Opc.Ua.Export.LocalizedText> output = new List<LocalizedText>();

            for (int ii = 0; ii < input.Length; ii++)
            {
                if (input[ii] != null)
                {
                    Opc.Ua.Export.LocalizedText text = new LocalizedText();
                    text.Locale = input[ii].Locale;
                    text.Value = input[ii].Text;
                    output.Add(text);
                }
            }

            return output.ToArray();
        }

        /// <summary>
        /// Exports localized text.
        /// </summary>
        private Opc.Ua.Export.LocalizedText Export(Opc.Ua.LocalizedText input)
        {
            if (input == null)
            {
                return null;
            }

            Opc.Ua.Export.LocalizedText text = new LocalizedText();
            text.Locale = input.Locale;
            text.Value = input.Text;
            return text;
        }

        /// <summary>
        /// Imports localized text.
        /// </summary>
        private Opc.Ua.LocalizedText Import(params Opc.Ua.Export.LocalizedText[] input)
        {
            if (input == null)
            {
                return null;
            }

            for (int ii = 0; ii < input.Length; ii++)
            {
                if (input[ii] != null)
                {
                    return new Opc.Ua.LocalizedText(input[ii].Locale, input[ii].Value);
                }
            }

            return null;
        }

        /// <summary>
        /// Exports a namespace index.
        /// </summary>
        private ushort ExportNamespaceIndex(ushort namespaceIndex, NamespaceTable namespaceUris)
        {
            // nothing special required for indexes 0.
            if (namespaceIndex < 1)
            {
                return namespaceIndex;
            }

            // return a bad value if parameters are bad.
            if (namespaceUris == null || namespaceUris.Count <= namespaceIndex)
            {
                return UInt16.MaxValue;
            }

            // find an existing index.
            int count = 1;
            string targetUri = namespaceUris.GetString(namespaceIndex);

            if (this.NamespaceUris != null)
            {
                for (int ii = 0; ii < this.NamespaceUris.Length; ii++)
                {
                    if (this.NamespaceUris[ii] == targetUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += this.NamespaceUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (this.NamespaceUris != null)
            {
                Array.Copy(this.NamespaceUris, uris, count - 1);
            }

            uris[count - 1] = targetUri;
            this.NamespaceUris = uris;

            // return the new index.
            return (ushort)count;
        }

        /// <summary>
        /// Exports a namespace index.
        /// </summary>
        private ushort ImportNamespaceIndex(ushort namespaceIndex, NamespaceTable namespaceUris)
        {
            // nothing special required for indexes 0 and 1.
            if (namespaceIndex < 1)
            {
                return namespaceIndex;
            }

            // return a bad value if parameters are bad.
            if (namespaceUris == null || this.NamespaceUris == null || this.NamespaceUris.Length <= namespaceIndex - 1)
            {
                return UInt16.MaxValue;
            }

            // find or append uri.
            return namespaceUris.GetIndexOrAppend(this.NamespaceUris[namespaceIndex - 1]);
        }

        /// <summary>
        /// Exports a namespace uri.
        /// </summary>
        private ushort ExportNamespaceUri(string namespaceUri, NamespaceTable namespaceUris)
        {
            // return a bad value if parameters are bad.
            if (namespaceUris == null)
            {
                return UInt16.MaxValue;
            }

            int namespaceIndex = namespaceUris.GetIndex(namespaceUri);

            // nothing special required for the first two URIs.
            if (namespaceIndex == 0)
            {
                return (ushort)namespaceIndex;
            }

            // find an existing index.
            int count = 1;

            if (this.NamespaceUris != null)
            {
                for (int ii = 0; ii < this.NamespaceUris.Length; ii++)
                {
                    if (this.NamespaceUris[ii] == namespaceUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += this.NamespaceUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (this.NamespaceUris != null)
            {
                Array.Copy(this.NamespaceUris, uris, count - 1);
            }

            uris[count - 1] = namespaceUri;
            this.NamespaceUris = uris;

            // return the new index.
            return (ushort)count;
        }

        /// <summary>
        /// Exports a server index.
        /// </summary>
        private uint ExportServerIndex(uint serverIndex, StringTable serverUris)
        {
            // nothing special required for indexes 0.
            if (serverIndex <= 0)
            {
                return serverIndex;
            }

            // return a bad value if parameters are bad.
            if (serverUris == null || serverUris.Count < serverIndex)
            {
                return UInt16.MaxValue;
            }

            // find an existing index.
            int count = 1;
            string targetUri = serverUris.GetString(serverIndex);

            if (this.ServerUris != null)
            {
                for (int ii = 0; ii < this.ServerUris.Length; ii++)
                {
                    if (this.ServerUris[ii] == targetUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += this.ServerUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (this.ServerUris != null)
            {
                Array.Copy(this.ServerUris, uris, count - 1);
            }

            uris[count - 1] = targetUri;
            this.ServerUris = uris;

            // return the new index.
            return (ushort)count;
        }

        /// <summary>
        /// Exports a server index.
        /// </summary>
        private uint ImportServerIndex(uint serverIndex, StringTable serverUris)
        {
            // nothing special required for indexes 0.
            if (serverIndex <= 0)
            {
                return serverIndex;
            }

            // return a bad value if parameters are bad.
            if (serverUris == null || this.ServerUris == null || this.ServerUris.Length <= serverIndex - 1)
            {
                return UInt16.MaxValue;
            }

            // find or append uri.
            return serverUris.GetIndexOrAppend(this.ServerUris[serverIndex - 1]);
        }
        #endregion

        #region Private Fields
        #endregion
    }
}
