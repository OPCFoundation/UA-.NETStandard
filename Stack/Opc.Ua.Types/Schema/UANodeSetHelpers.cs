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
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua.Export
{
    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    public partial class UANodeSet
    {
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
            using var reader = new StreamReader(istrm);
            using var xmlReader = XmlReader.Create(reader, CoreUtils.DefaultXmlReaderSettings());
            var serializer = new XmlSerializer(typeof(UANodeSet));
            return serializer.Deserialize(xmlReader) as UANodeSet;
        }

        /// <summary>
        /// Write a nodeset to a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        public void Write(Stream istrm)
        {
            XmlWriterSettings setting = CoreUtils.DefaultXmlWriterSettings();
            var writer = XmlWriter.Create(istrm, setting);

            try
            {
                var serializer = new XmlSerializer(typeof(UANodeSet));
                serializer.Serialize(writer, this, null);
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }
        }

        /// <summary>
        /// Adds an alias to the node set.
        /// </summary>
        public void AddAlias(ISystemContext context, string alias, NodeId nodeId)
        {
            int count = 1;

            if (Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Alias == alias)
                    {
                        Aliases[ii].Value = Export(nodeId, context.NamespaceUris);
                        return;
                    }
                }

                count += Aliases.Length;
            }

            var aliases = new NodeIdAlias[count];

            if (Aliases != null)
            {
                Array.Copy(Aliases, aliases, Aliases.Length);
            }

            aliases[count - 1] = new NodeIdAlias
            {
                Alias = alias,
                Value = Export(nodeId, context.NamespaceUris)
            };
            Aliases = aliases;
        }

        /// <summary>
        /// Imports a node from the set.
        /// </summary>
        public void Import(ISystemContext context, NodeStateCollection nodes)
        {
            for (int ii = 0; ii < Items.Length; ii++)
            {
                UANode node = Items[ii];
                NodeState importedNode = Import(context, node);
                nodes.Add(importedNode);
            }
        }

        /// <summary>
        /// Adds a node to the set.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public void Export(ISystemContext context, NodeState node, bool outputRedundantNames = true)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (NodeId.IsNull(node.NodeId))
            {
                throw new ArgumentException("A non-null NodeId must be specified.");
            }

            UANode exportedNode = null;

            switch (node.NodeClass)
            {
                case NodeClass.Object:
                {
                    var o = (BaseObjectState)node;
                    var value = new UAObject
                    {
                        EventNotifier = o.EventNotifier,
                        DesignToolOnly = node.DesignToolOnly
                    };

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    exportedNode = value;
                    break;
                }
                case NodeClass.Variable:
                {
                    var o = (BaseVariableState)node;
                    var value = new UAVariable
                    {
                        DataType = ExportAlias(o.DataType, context.NamespaceUris),
                        ValueRank = o.ValueRank,
                        ArrayDimensions = Export(o.ArrayDimensions),
                        AccessLevel = o.AccessLevelEx,
                        MinimumSamplingInterval = o.MinimumSamplingInterval,
                        Historizing = o.Historizing,
                        DesignToolOnly = node.DesignToolOnly
                    };

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    if (o.Value != null)
                    {
                        using XmlEncoder encoder = CreateEncoder(context);
                        var variant = new Variant(o.Value);
                        encoder.WriteVariantContents(variant.Value, variant.TypeInfo);

                        var document = new XmlDocument();
                        document.LoadInnerXml(encoder.CloseAndReturnText());
                        value.Value = document.DocumentElement;
                    }

                    exportedNode = value;
                    break;
                }
                case NodeClass.Method:
                {
                    var o = (MethodState)node;
                    var value = new UAMethod
                    {
                        Executable = o.Executable,
                        DesignToolOnly = node.DesignToolOnly
                    };

                    if (o.MethodDeclarationId != null &&
                        !o.MethodDeclarationId.IsNullNodeId &&
                        o.MethodDeclarationId != o.NodeId)
                    {
                        value.MethodDeclarationId = Export(
                            o.MethodDeclarationId,
                            context.NamespaceUris);
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
                    var o = (ViewState)node;
                    exportedNode = new UAView
                    {
                        ContainsNoLoops = o.ContainsNoLoops,
                        DesignToolOnly = node.DesignToolOnly
                    };
                    break;
                }
                case NodeClass.ObjectType:
                {
                    var o = (BaseObjectTypeState)node;
                    exportedNode = new UAObjectType { IsAbstract = o.IsAbstract };
                    break;
                }
                case NodeClass.VariableType:
                {
                    var o = (BaseVariableTypeState)node;
                    var value = new UAVariableType
                    {
                        IsAbstract = o.IsAbstract,
                        DataType = ExportAlias(o.DataType, context.NamespaceUris),
                        ValueRank = o.ValueRank,
                        ArrayDimensions = Export(o.ArrayDimensions)
                    };

                    if (o.Value != null)
                    {
                        using XmlEncoder encoder = CreateEncoder(context);
                        var variant = new Variant(o.Value);
                        encoder.WriteVariantContents(variant.Value, variant.TypeInfo);

                        var document = new XmlDocument();
                        document.LoadInnerXml(encoder.CloseAndReturnText());
                        value.Value = document.DocumentElement;
                    }

                    exportedNode = value;
                    break;
                }
                case NodeClass.DataType:
                {
                    var o = (DataTypeState)node;
                    exportedNode = new UADataType
                    {
                        IsAbstract = o.IsAbstract,
                        Definition = Export(
                            o,
                            o.DataTypeDefinition,
                            context.NamespaceUris,
                            outputRedundantNames),
                        Purpose = o.Purpose
                    };
                    break;
                }
                case NodeClass.ReferenceType:
                {
                    var o = (ReferenceTypeState)node;
                    var value = new UAReferenceType { IsAbstract = o.IsAbstract };

                    if (!Ua.LocalizedText.IsNullOrEmpty(o.InverseName))
                    {
                        value.InverseName = Export([o.InverseName]);
                    }

                    value.Symmetric = o.Symmetric;
                    exportedNode = value;
                    break;
                }
                case NodeClass.Unspecified:
                    // Unexpected?
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass {node.NodeClass}");
            }

            exportedNode.NodeId = Export(node.NodeId, context.NamespaceUris);
            exportedNode.BrowseName = Export(node.BrowseName, context.NamespaceUris);

            if (outputRedundantNames || node.DisplayName.Text != node.BrowseName.Name)
            {
                exportedNode.DisplayName = Export([node.DisplayName]);
            }
            else
            {
                exportedNode.DisplayName = null;
            }

            if (node.Description != null && !string.IsNullOrEmpty(node.Description.Text))
            {
                exportedNode.Description = Export([node.Description]);
            }
            else
            {
                exportedNode.Description = [];
            }

            exportedNode.Documentation = node.NodeSetDocumentation;
            exportedNode.Category =
                node.Categories != null && node.Categories.Count > 0 ? [.. node.Categories] : null;
            exportedNode.ReleaseStatus = node.ReleaseStatus;
            exportedNode.WriteMask = (uint)node.WriteMask;
            exportedNode.UserWriteMask = (uint)node.UserWriteMask;
            exportedNode.Extensions = node.Extensions;
            exportedNode.RolePermissions = null;
            exportedNode.AccessRestrictions = 0;
            exportedNode.AccessRestrictionsSpecified = false;

            if (node.RolePermissions != null)
            {
                var permissions = new List<RolePermission>();

                foreach (RolePermissionType ii in node.RolePermissions)
                {
                    var permission = new RolePermission
                    {
                        Permissions = ii.Permissions,
                        Value = ExportAlias(ii.RoleId, context.NamespaceUris)
                    };

                    permissions.Add(permission);
                }

                exportedNode.RolePermissions = [.. permissions];
            }

            if (node.AccessRestrictions != null)
            {
                exportedNode.AccessRestrictions = (ushort)node.AccessRestrictions;
                exportedNode.AccessRestrictionsSpecified = true;
            }

            if (!string.IsNullOrEmpty(node.SymbolicName) &&
                node.SymbolicName != node.BrowseName.Name)
            {
                exportedNode.SymbolicName = node.SymbolicName;
            }

            // export references.
            INodeBrowser browser = node.CreateBrowser(
                context,
                null,
                null,
                true,
                BrowseDirection.Both,
                null,
                null,
                true);
            var exportedReferences = new List<Reference>();
            IReference reference = browser.Next();

            while (reference != null)
            {
                if (node.NodeClass == NodeClass.Method &&
                    !reference.IsInverse &&
                    reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                {
                    reference = browser.Next();
                    continue;
                }

                var exportedReference = new Reference
                {
                    ReferenceType = ExportAlias(reference.ReferenceTypeId, context.NamespaceUris),
                    IsForward = !reference.IsInverse,
                    Value = Export(reference.TargetId, context.NamespaceUris, context.ServerUris)
                };
                exportedReferences.Add(exportedReference);

                reference = browser.Next();
            }

            exportedNode.References = [.. exportedReferences];

            int count = 1;

            // add node to list.
            UANode[] nodes;
            if (Items == null)
            {
                nodes = new UANode[count];
            }
            else
            {
                count += Items.Length;
                nodes = new UANode[count];
                Array.Copy(Items, nodes, Items.Length);
            }

            nodes[count - 1] = exportedNode;

            Items = nodes;

            // recursively process children.
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                Export(context, children[ii], outputRedundantNames);
            }
        }

        /// <summary>
        /// Creates an encoder to save Variant values.
        /// </summary>
        private XmlEncoder CreateEncoder(ISystemContext context)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext(context.Telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            var encoder = new XmlEncoder(messageContext);

            var namespaceUris = new NamespaceTable();

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    namespaceUris.GetIndexOrAppend(NamespaceUris[ii]);
                }
            }

            var serverUris = new StringTable();

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
            IServiceMessageContext messageContext = new ServiceMessageContext(context.Telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };

            var decoder = new XmlDecoder(source, messageContext);

            var namespaceUris = new NamespaceTable();

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    namespaceUris.GetIndexOrAppend(NamespaceUris[ii]);
                }
            }

            var serverUris = new StringTable();

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
        /// <exception cref="ServiceResultException"></exception>
        private NodeState Import(ISystemContext context, UANode node)
        {
            NodeState importedNode = null;

            NodeClass nodeClass = NodeClass.Unspecified;

            if (node is UAObject)
            {
                nodeClass = NodeClass.Object;
            }
            else if (node is UAVariable)
            {
                nodeClass = NodeClass.Variable;
            }
            else if (node is UAMethod)
            {
                nodeClass = NodeClass.Method;
            }
            else if (node is UAObjectType)
            {
                nodeClass = NodeClass.ObjectType;
            }
            else if (node is UAVariableType)
            {
                nodeClass = NodeClass.VariableType;
            }
            else if (node is UADataType)
            {
                nodeClass = NodeClass.DataType;
            }
            else if (node is UAReferenceType)
            {
                nodeClass = NodeClass.ReferenceType;
            }
            else if (node is UAView)
            {
                nodeClass = NodeClass.View;
            }

            switch (nodeClass)
            {
                case NodeClass.Object:
                {
                    var o = (UAObject)node;
                    importedNode = new BaseObjectState(null)
                    {
                        EventNotifier = o.EventNotifier,
                        DesignToolOnly = o.DesignToolOnly
                    };
                    break;
                }
                case NodeClass.Variable:
                {
                    var o = (UAVariable)node;

                    NodeId typeDefinitionId = null;

                    if (node.References != null)
                    {
                        for (int ii = 0; ii < node.References.Length; ii++)
                        {
                            NodeId referenceTypeId = ImportNodeId(
                                node.References[ii].ReferenceType,
                                context.NamespaceUris,
                                true);
                            bool isInverse = !node.References[ii].IsForward;
                            ExpandedNodeId targetId = ImportExpandedNodeId(
                                node.References[ii].Value,
                                context.NamespaceUris,
                                context.ServerUris);

                            if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition && !isInverse)
                            {
                                typeDefinitionId = ExpandedNodeId.ToNodeId(
                                    targetId,
                                    context.NamespaceUris);
                                break;
                            }
                        }
                    }

                    BaseVariableState value;
                    if (typeDefinitionId == VariableTypeIds.PropertyType)
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
                    value.DesignToolOnly = o.DesignToolOnly;

                    if (o.Value != null)
                    {
                        using XmlDecoder decoder = CreateDecoder(context, o.Value);
                        value.Value = decoder.ReadVariantContents(out TypeInfo typeInfo);
                        decoder.Close();
                    }

                    importedNode = value;
                    break;
                }
                case NodeClass.Method:
                {
                    var o = (UAMethod)node;
                    importedNode = new MethodState(null)
                    {
                        Executable = o.Executable,
                        UserExecutable = o.Executable,
                        MethodDeclarationId = ImportNodeId(
                            o.MethodDeclarationId,
                            context.NamespaceUris,
                            true),
                        DesignToolOnly = o.DesignToolOnly
                    };
                    break;
                }
                case NodeClass.View:
                {
                    var o = (UAView)node;
                    importedNode = new ViewState
                    {
                        ContainsNoLoops = o.ContainsNoLoops,
                        DesignToolOnly = o.DesignToolOnly
                    };
                    break;
                }
                case NodeClass.ObjectType:
                {
                    var o = (UAObjectType)node;
                    importedNode = new BaseObjectTypeState { IsAbstract = o.IsAbstract };
                    break;
                }
                case NodeClass.VariableType:
                {
                    var o = (UAVariableType)node;
                    BaseVariableTypeState value = new BaseDataVariableTypeState
                    {
                        IsAbstract = o.IsAbstract,
                        DataType = ImportNodeId(o.DataType, context.NamespaceUris, true),
                        ValueRank = o.ValueRank,
                        ArrayDimensions = ImportArrayDimensions(o.ArrayDimensions)
                    };

                    if (o.Value != null)
                    {
                        using XmlDecoder decoder = CreateDecoder(context, o.Value);
                        value.Value = decoder.ReadVariantContents(out TypeInfo typeInfo);
                        decoder.Close();
                    }

                    importedNode = value;
                    break;
                }
                case NodeClass.DataType:
                {
                    var o = (UADataType)node;
                    var value = new DataTypeState { IsAbstract = o.IsAbstract };
                    Ua.DataTypeDefinition dataTypeDefinition = Import(
                        o.Definition,
                        context.NamespaceUris);
                    value.DataTypeDefinition = new ExtensionObject(dataTypeDefinition);
                    value.Purpose = o.Purpose;
                    importedNode = value;
                    break;
                }
                case NodeClass.ReferenceType:
                {
                    var o = (UAReferenceType)node;
                    importedNode = new ReferenceTypeState
                    {
                        IsAbstract = o.IsAbstract,
                        InverseName = Import(o.InverseName),
                        Symmetric = o.Symmetric
                    };
                    break;
                }
                case NodeClass.Unspecified:
                    break;
                default:
                    throw ServiceResultException.Unexpected($"Unexpected NodeClass {nodeClass}");
            }

            importedNode.NodeId = ImportNodeId(node.NodeId, context.NamespaceUris, false);
            importedNode.BrowseName = ImportQualifiedName(node.BrowseName, context.NamespaceUris);
            importedNode.DisplayName = Import(node.DisplayName) ??
                new Ua.LocalizedText(importedNode.BrowseName.Name);

            importedNode.Description = Import(node.Description);
            importedNode.NodeSetDocumentation = node.Documentation;
            importedNode.Categories = node.Category != null && node.Category.Length > 0
                ? node.Category
                : null;
            importedNode.ReleaseStatus = node.ReleaseStatus;
            importedNode.WriteMask = (AttributeWriteMask)node.WriteMask;
            importedNode.UserWriteMask = (AttributeWriteMask)node.UserWriteMask;
            importedNode.Extensions = node.Extensions;

            if (node.RolePermissions != null)
            {
                var permissions = new RolePermissionTypeCollection();

                foreach (RolePermission ii in node.RolePermissions)
                {
                    var permission = new RolePermissionType
                    {
                        Permissions = ii.Permissions,
                        RoleId = ImportNodeId(ii.Value, context.NamespaceUris, true)
                    };

                    permissions.Add(permission);
                }

                importedNode.RolePermissions = permissions;
            }

            if (node.AccessRestrictionsSpecified)
            {
                importedNode.AccessRestrictions = (AccessRestrictionType?)node.AccessRestrictions;
            }

            if (!string.IsNullOrEmpty(node.SymbolicName))
            {
                importedNode.SymbolicName = node.SymbolicName;
            }

            if (node.References != null)
            {
                for (int ii = 0; ii < node.References.Length; ii++)
                {
                    NodeId referenceTypeId = ImportNodeId(
                        node.References[ii].ReferenceType,
                        context.NamespaceUris,
                        true);
                    bool isInverse = !node.References[ii].IsForward;
                    ExpandedNodeId targetId = ImportExpandedNodeId(
                        node.References[ii].Value,
                        context.NamespaceUris,
                        context.ServerUris);

                    if (importedNode is BaseInstanceState instance)
                    {
                        if (referenceTypeId == ReferenceTypeIds.HasModellingRule && !isInverse)
                        {
                            instance.ModellingRuleId = ExpandedNodeId.ToNodeId(
                                targetId,
                                context.NamespaceUris);
                            continue;
                        }

                        if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition && !isInverse)
                        {
                            instance.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                                targetId,
                                context.NamespaceUris);
                            continue;
                        }
                    }

                    if (importedNode is BaseTypeState type &&
                        referenceTypeId == ReferenceTypeIds.HasSubtype &&
                        isInverse)
                    {
                        type.SuperTypeId = ExpandedNodeId.ToNodeId(targetId, context.NamespaceUris);
                        continue;
                    }

                    importedNode.AddReference(referenceTypeId, isInverse, targetId);
                }
            }

            string parentNodeId = (node as UAInstance)?.ParentNodeId;

            if (!string.IsNullOrEmpty(parentNodeId))
            {
                // set parent NodeId in Handle property.
                importedNode.Handle = ImportNodeId(parentNodeId, context.NamespaceUris, true);
            }

            return importedNode;
        }

        /// <summary>
        /// Exports a NodeId as an alias.
        /// </summary>
        private string ExportAlias(NodeId source, NamespaceTable namespaceUris)
        {
            string nodeId = Export(source, namespaceUris);

            if (!string.IsNullOrEmpty(nodeId) && Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Value == nodeId)
                    {
                        return Aliases[ii].Alias;
                    }
                }
            }

            return nodeId;
        }

        /// <summary>
        /// Exports a NodeId
        /// </summary>
        private string Export(NodeId source, NamespaceTable namespaceUris)
        {
            if (NodeId.IsNull(source))
            {
                return string.Empty;
            }

            if (source.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
                source = new NodeId(source.Identifier, namespaceIndex);
            }

            return source.ToString();
        }

        /// <summary>
        ///  Imports a NodeId
        /// </summary>
        private NodeId ImportNodeId(string source, NamespaceTable namespaceUris, bool lookupAlias)
        {
            if (string.IsNullOrEmpty(source))
            {
                return NodeId.Null;
            }

            // lookup alias.
            if (lookupAlias && Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Alias == source)
                    {
                        source = Aliases[ii].Value;
                        break;
                    }
                }
            }

            // parse the string.
            var nodeId = NodeId.Parse(source);

            if (nodeId.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(nodeId.NamespaceIndex, namespaceUris);
                nodeId = new NodeId(nodeId.Identifier, namespaceIndex);
            }

            return nodeId;
        }

        /// <summary>
        /// Exports a ExpandedNodeId
        /// </summary>
        private string Export(
            ExpandedNodeId source,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            if (NodeId.IsNull(source))
            {
                return string.Empty;
            }

            if (source.ServerIndex <= 0 &&
                source.NamespaceIndex <= 0 &&
                string.IsNullOrEmpty(source.NamespaceUri))
            {
                return source.ToString();
            }

            ushort namespaceIndex;
            if (string.IsNullOrEmpty(source.NamespaceUri))
            {
                namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
            }
            else
            {
                namespaceIndex = ExportNamespaceUri(source.NamespaceUri, namespaceUris);
            }

            uint serverIndex = ExportServerIndex(source.ServerIndex, serverUris);
            source = new ExpandedNodeId(source.Identifier, namespaceIndex, null, serverIndex);
            return source.ToString();
        }

        /// <summary>
        /// Imports a ExpandedNodeId
        /// </summary>
        private ExpandedNodeId ImportExpandedNodeId(
            string source,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            if (string.IsNullOrEmpty(source))
            {
                return ExpandedNodeId.Null;
            }
            // lookup aliases
            if (Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Alias == source)
                    {
                        source = Aliases[ii].Value;
                        break;
                    }
                }
            }

            // parse the node.
            var nodeId = ExpandedNodeId.Parse(source);

            if (nodeId.ServerIndex <= 0 &&
                nodeId.NamespaceIndex <= 0 &&
                string.IsNullOrEmpty(nodeId.NamespaceUri))
            {
                return nodeId;
            }

            uint serverIndex = ImportServerIndex(nodeId.ServerIndex, serverUris);
            ushort namespaceIndex = ImportNamespaceIndex(nodeId.NamespaceIndex, namespaceUris);

            if (serverIndex > 0)
            {
                string namespaceUri = nodeId.NamespaceUri;

                if (string.IsNullOrEmpty(nodeId.NamespaceUri))
                {
                    namespaceUri = namespaceUris.GetString(namespaceIndex);
                }

                return new ExpandedNodeId(nodeId.Identifier, 0, namespaceUri, serverIndex);
            }

            return new ExpandedNodeId(nodeId.Identifier, namespaceIndex, null, 0);
        }

        /// <summary>
        /// Exports a QualifiedName
        /// </summary>
        private string Export(QualifiedName source, NamespaceTable namespaceUris)
        {
            if (QualifiedName.IsNull(source))
            {
                return string.Empty;
            }

            if (source.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
                source = new QualifiedName(source.Name, namespaceIndex);
            }

            return source.ToString();
        }

        /// <summary>
        /// Exports a DataTypeDefinition
        /// </summary>
        private DataTypeDefinition Export(
            DataTypeState dataType,
            ExtensionObject source,
            NamespaceTable namespaceUris,
            bool outputRedundantNames)
        {
            if (source == null || source.Body == null)
            {
                return null;
            }

            var definition = new DataTypeDefinition();

            if (outputRedundantNames || dataType.BrowseName != null)
            {
                definition.Name = Export(dataType.BrowseName, namespaceUris);
            }

            if (dataType.BrowseName.Name != dataType.SymbolicName)
            {
                definition.SymbolicName = dataType.SymbolicName;
            }

            if (source.Body is StructureDefinition sd)
            {
                if (sd
                    .StructureType is StructureType.Union or StructureType.UnionWithSubtypedValues)
                {
                    definition.IsUnion = true;
                }

                if (sd.Fields != null)
                {
                    var fields = new List<DataTypeField>();

                    for (int ii = sd.FirstExplicitFieldIndex; ii < sd.Fields.Count; ii++)
                    {
                        StructureField field = sd.Fields[ii];

                        var output = new DataTypeField
                        {
                            Name = field.Name,
                            Description = Export([field.Description])
                        };

                        if (sd.StructureType == StructureType.StructureWithOptionalFields)
                        {
                            output.IsOptional = field.IsOptional;
                            output.AllowSubTypes = false;
                        }
                        else if (sd.StructureType
                            is StructureType.StructureWithSubtypedValues
                                or StructureType.UnionWithSubtypedValues)
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
                                output.ArrayDimensions = BaseVariableState.ArrayDimensionsToXml(
                                    field.ArrayDimensions);
                            }
                        }

                        output.MaxStringLength = field.MaxStringLength;

                        fields.Add(output);
                    }

                    definition.Field = [.. fields];
                }
            }

            if (source.Body is EnumDefinition ed)
            {
                definition.IsOptionSet = ed.IsOptionSet;

                if (ed.Fields != null)
                {
                    var fields = new List<DataTypeField>();

                    foreach (EnumField field in ed.Fields)
                    {
                        var output = new DataTypeField { Name = field.Name };

                        if (field.DisplayName != null && output.Name != field.DisplayName.Text)
                        {
                            output.DisplayName = Export([field.DisplayName]);
                        }
                        else
                        {
                            output.DisplayName = [];
                        }

                        output.Description = Export([field.Description]);
                        output.ValueRank = ValueRanks.Scalar;
                        output.Value = (int)field.Value;

                        fields.Add(output);
                    }

                    definition.Field = [.. fields];
                }
            }

            return definition;
        }

        /// <summary>
        /// Imports a DataTypeDefinition
        /// </summary>
        private Ua.DataTypeDefinition Import(
            DataTypeDefinition source,
            NamespaceTable namespaceUris)
        {
            if (source == null)
            {
                return null;
            }

            Ua.DataTypeDefinition definition = null;

            if (source.Field != null)
            {
                // check if definition is for enumeration or structure.
                bool isEnumeration = Array.Exists(
                    source.Field,
                    fieldLookup => fieldLookup.Value != -1);

                if (!isEnumeration)
                {
                    var sd = new StructureDefinition
                    {
                        BaseDataType = ImportNodeId(source.BaseType, namespaceUris, true)
                    };

                    if (source.IsUnion)
                    {
                        sd.StructureType = StructureType.Union;
                    }

                    if (source.Field != null)
                    {
                        var fields = new List<StructureField>();

                        foreach (DataTypeField field in source.Field)
                        {
                            if (sd.StructureType is StructureType.Structure or StructureType.Union)
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
                                        sd.StructureType
                                            = StructureType.StructureWithSubtypedValues;
                                    }
                                }
                            }

                            var output = new StructureField
                            {
                                Name = field.Name,
                                Description = Import(field.Description),
                                DataType = ImportNodeId(field.DataType, namespaceUris, true),
                                ValueRank = field.ValueRank
                            };
                            if (!string.IsNullOrWhiteSpace(field.ArrayDimensions))
                            {
                                if (output.ValueRank > 1 || field.ArrayDimensions[0] > 0)
                                {
                                    output.ArrayDimensions =
                                    [
                                        .. BaseVariableState.ArrayDimensionsFromXml(
                                            field.ArrayDimensions)
                                    ];
                                }
                            }

                            output.MaxStringLength = field.MaxStringLength;

                            if (sd.StructureType is StructureType.Structure or StructureType.Union)
                            {
                                output.IsOptional = false;
                            }
                            else if (sd.StructureType
                                is StructureType.StructureWithSubtypedValues
                                    or StructureType.UnionWithSubtypedValues)
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
                    var ed = new EnumDefinition { IsOptionSet = source.IsOptionSet };

                    if (source.Field != null)
                    {
                        var fields = new List<EnumField>();

                        foreach (DataTypeField field in source.Field)
                        {
                            var output = new EnumField
                            {
                                Name = field.Name,
                                DisplayName = Import(field.DisplayName),
                                Description = Import(field.Description),
                                Value = field.Value
                            };

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
        private QualifiedName ImportQualifiedName(string source, NamespaceTable namespaceUris)
        {
            if (string.IsNullOrEmpty(source))
            {
                return QualifiedName.Null;
            }

            var qname = QualifiedName.Parse(source);

            if (qname.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(qname.NamespaceIndex, namespaceUris);
                qname = new QualifiedName(qname.Name, namespaceIndex);
            }

            return qname;
        }

        /// <summary>
        /// Exports the array dimensions.
        /// </summary>
        private static string Export(ReadOnlyList<uint> arrayDimensions)
        {
            if (arrayDimensions == null)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder();

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
        private static uint[] ImportArrayDimensions(string arrayDimensions)
        {
            if (string.IsNullOrEmpty(arrayDimensions))
            {
                return null;
            }

            string[] fields = arrayDimensions.Split(',');
            uint[] dimensions = new uint[fields.Length];

            for (int ii = 0; ii < fields.Length; ii++)
            {
                try
                {
                    dimensions[ii] = Convert.ToUInt32(fields[ii], CultureInfo.InvariantCulture);
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
        private static LocalizedText[] Export(Ua.LocalizedText[] input)
        {
            if (input == null)
            {
                return null;
            }

            var output = new List<LocalizedText>();

            for (int ii = 0; ii < input.Length; ii++)
            {
                if (input[ii] != null)
                {
                    var text = new LocalizedText
                    {
                        Locale = input[ii].Locale,
                        Value = input[ii].Text
                    };
                    output.Add(text);
                }
            }

            return [.. output];
        }

#if UNUSED
        /// <summary>
        /// Exports localized text.
        /// </summary>
        private static LocalizedText Export(Ua.LocalizedText input)
        {
            if (input == null)
            {
                return null;
            }

            return new LocalizedText { Locale = input.Locale, Value = input.Text };
        }
#endif

        /// <summary>
        /// Imports localized text.
        /// </summary>
        private static Ua.LocalizedText Import(params LocalizedText[] input)
        {
            if (input == null)
            {
                return null;
            }

            for (int ii = 0; ii < input.Length; ii++)
            {
                if (input[ii] != null)
                {
                    return new Ua.LocalizedText(input[ii].Locale, input[ii].Value);
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
                return ushort.MaxValue;
            }

            // find an existing index.
            int count = 1;
            string targetUri = namespaceUris.GetString(namespaceIndex);

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    if (NamespaceUris[ii] == targetUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += NamespaceUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (NamespaceUris != null)
            {
                Array.Copy(NamespaceUris, uris, count - 1);
            }

            uris[count - 1] = targetUri;
            NamespaceUris = uris;

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
            if (namespaceUris == null ||
                NamespaceUris == null ||
                NamespaceUris.Length <= namespaceIndex - 1)
            {
                return ushort.MaxValue;
            }

            // find or append uri.
            return namespaceUris.GetIndexOrAppend(NamespaceUris[namespaceIndex - 1]);
        }

        /// <summary>
        /// Exports a namespace uri.
        /// </summary>
        private ushort ExportNamespaceUri(string namespaceUri, NamespaceTable namespaceUris)
        {
            // return a bad value if parameters are bad.
            if (namespaceUris == null)
            {
                return ushort.MaxValue;
            }

            int namespaceIndex = namespaceUris.GetIndex(namespaceUri);

            // nothing special required for the first two URIs.
            if (namespaceIndex == 0)
            {
                return (ushort)namespaceIndex;
            }

            // find an existing index.
            int count = 1;

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    if (NamespaceUris[ii] == namespaceUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += NamespaceUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (NamespaceUris != null)
            {
                Array.Copy(NamespaceUris, uris, count - 1);
            }

            uris[count - 1] = namespaceUri;
            NamespaceUris = uris;

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
                return ushort.MaxValue;
            }

            // find an existing index.
            int count = 1;
            string targetUri = serverUris.GetString(serverIndex);

            if (ServerUris != null)
            {
                for (int ii = 0; ii < ServerUris.Length; ii++)
                {
                    if (ServerUris[ii] == targetUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += ServerUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (ServerUris != null)
            {
                Array.Copy(ServerUris, uris, count - 1);
            }

            uris[count - 1] = targetUri;
            ServerUris = uris;

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
            if (serverUris == null || ServerUris == null || ServerUris.Length <= serverIndex - 1)
            {
                return ushort.MaxValue;
            }

            // find or append uri.
            return serverUris.GetIndexOrAppend(ServerUris[serverIndex - 1]);
        }
    }
}
