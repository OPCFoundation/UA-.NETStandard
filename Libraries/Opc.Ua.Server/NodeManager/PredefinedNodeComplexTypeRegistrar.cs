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

using System.Collections.Generic;
using System.Xml;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Creates runtime stand-in types for DataTypeDefinitions in predefined nodes and
    /// registers them in the encodeable factory.
    /// </summary>
    internal static class PredefinedNodeComplexTypeRegistrar
    {
        public static void RegisterDataTypes(
            ISystemContext context,
            NodeStateCollection predefinedNodes)
        {
            if (context?.EncodeableFactory == null || predefinedNodes == null || predefinedNodes.Count == 0)
            {
                return;
            }

            var flatNodes = new List<NodeState>();
            foreach (NodeState node in predefinedNodes)
            {
                FlattenNodeTree(context, node, flatNodes);
            }

            var nodesById = new Dictionary<NodeId, NodeState>();
            var dataTypes = new Dictionary<NodeId, DataTypeState>();
            foreach (NodeState node in flatNodes)
            {
                if (!node.NodeId.IsNull)
                {
                    nodesById[node.NodeId] = node;
                }

                if (node is DataTypeState dataType && !dataType.NodeId.IsNull)
                {
                    dataTypes[dataType.NodeId] = dataType;
                }
            }

            if (dataTypes.Count == 0)
            {
                return;
            }

            IEncodeableFactoryBuilder factoryBuilder = context.EncodeableFactory.Builder;
            var knownTypes = new Dictionary<NodeId, IType>();

            foreach (DataTypeState dataType in dataTypes.Values)
            {
                if (TryGetDefinition(dataType.DataTypeDefinition, out EnumDefinition enumDefinition))
                {
                    var enumType = new global::Opc.Ua.Encoders.Enumeration(
                        GetXmlName(context, dataType),
                        enumDefinition);
                    RegisterEnumeratedType(context, factoryBuilder, dataType, enumType, nodesById);
                    knownTypes[dataType.NodeId] = enumType;
                }
            }

            var pendingStructures = new List<DataTypeState>();
            foreach (DataTypeState dataType in dataTypes.Values)
            {
                if (TryGetDefinition(dataType.DataTypeDefinition, out StructureDefinition _))
                {
                    pendingStructures.Add(dataType);
                }
            }

            bool progress = true;
            while (progress && pendingStructures.Count > 0)
            {
                progress = false;

                for (int i = pendingStructures.Count - 1; i >= 0; i--)
                {
                    DataTypeState dataType = pendingStructures[i];
                    if (!TryCreateStructuredType(
                        context,
                        dataType,
                        dataTypes,
                        knownTypes,
                        nodesById,
                        out IEncodeableType encodeableType))
                    {
                        continue;
                    }

                    RegisterEncodeableType(context, factoryBuilder, dataType, encodeableType, nodesById);
                    knownTypes[dataType.NodeId] = encodeableType;
                    pendingStructures.RemoveAt(i);
                    progress = true;
                }
            }

            factoryBuilder.Commit();
        }

        private static bool TryCreateStructuredType(
            ISystemContext context,
            DataTypeState dataType,
            IDictionary<NodeId, DataTypeState> dataTypes,
            IDictionary<NodeId, IType> knownTypes,
            IDictionary<NodeId, NodeState> nodesById,
            out IEncodeableType encodeableType)
        {
            if (!TryGetDefinition(dataType.DataTypeDefinition, out StructureDefinition structureDefinition))
            {
                encodeableType = null;
                return false;
            }

            bool allowSubTypes = structureDefinition.StructureType is
                StructureType.StructureWithSubtypedValues or StructureType.UnionWithSubtypedValues;
            var fieldTypes = new Dictionary<string, BuiltInType>();

            foreach (StructureField field in structureDefinition.Fields)
            {
                if (!TryResolveFieldType(
                    field,
                    dataType.NodeId,
                    allowSubTypes,
                    dataTypes,
                    knownTypes,
                    out BuiltInType fieldType))
                {
                    encodeableType = null;
                    return false;
                }
                fieldTypes[field.Name] = fieldType;
            }

            (ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId)
                = GetBinaryAndXmlEncodingIds(context, dataType, nodesById);
            var xmlName = GetXmlName(context, dataType);
            ExpandedNodeId typeId = NodeId.ToExpandedNodeId(dataType.NodeId, context.NamespaceUris);

            encodeableType = structureDefinition.StructureType switch
            {
                StructureType.Structure or StructureType.StructureWithSubtypedValues =>
                    new global::Opc.Ua.Encoders.Structure(
                        xmlName,
                        typeId,
                        binaryEncodingId,
                        xmlEncodingId,
                        structureDefinition,
                        fieldTypes),
                StructureType.StructureWithOptionalFields =>
                    new global::Opc.Ua.Encoders.StructureWithOptionalFields(
                        xmlName,
                        typeId,
                        binaryEncodingId,
                        xmlEncodingId,
                        structureDefinition,
                        fieldTypes),
                StructureType.Union or StructureType.UnionWithSubtypedValues =>
                    // use the default union selector value.
                    new global::Opc.Ua.Encoders.Union(
                        xmlName,
                        typeId,
                        binaryEncodingId,
                        xmlEncodingId,
                        structureDefinition,
                        fieldTypes,
                        DefaultUnionSelector),
                _ => null
            };

            return encodeableType != null;
        }

        private static bool TryResolveFieldType(
            StructureField field,
            NodeId ownerTypeId,
            bool allowSubTypes,
            IDictionary<NodeId, DataTypeState> dataTypes,
            IDictionary<NodeId, IType> knownTypes,
            out BuiltInType fieldType)
        {
            if (IsRecursiveFieldType(ownerTypeId, field.DataType))
            {
                fieldType = BuiltInType.Null;
                return true;
            }

            if (TryResolveBuiltInType(field.DataType, out fieldType))
            {
                return true;
            }

            if (knownTypes.TryGetValue(field.DataType, out IType knownType))
            {
                if (knownType is IEnumeratedType)
                {
                    fieldType = BuiltInType.Enumeration;
                    return true;
                }

                if (knownType is IEncodeableType)
                {
                    fieldType = GetStructuredFieldBuiltInType(allowSubTypes, field.IsOptional);
                    return true;
                }
            }

            NodeId superTypeId = field.DataType;
            var visited = new HashSet<NodeId>();
            while (!superTypeId.IsNull && visited.Add(superTypeId))
            {
                if (TryResolveBuiltInType(superTypeId, out fieldType))
                {
                    return true;
                }

                if (superTypeId == DataTypeIds.Enumeration)
                {
                    fieldType = BuiltInType.UInt32;
                    return true;
                }

                if (superTypeId == DataTypeIds.Structure)
                {
                    fieldType = GetStructuredFieldBuiltInType(allowSubTypes, field.IsOptional);
                    return true;
                }

                if (!dataTypes.TryGetValue(superTypeId, out DataTypeState superType))
                {
                    break;
                }

                superTypeId = superType.SuperTypeId;
            }

            fieldType = BuiltInType.Null;
            return false;
        }

        private static bool TryResolveBuiltInType(NodeId dataTypeId, out BuiltInType builtInType)
        {
            builtInType = TypeInfo.GetBuiltInType(dataTypeId);
            return builtInType != BuiltInType.Null;
        }

        private static bool IsRecursiveFieldType(NodeId ownerTypeId, NodeId fieldTypeId)
        {
            return fieldTypeId == ownerTypeId;
        }

        private static BuiltInType GetStructuredFieldBuiltInType(bool allowSubTypes, bool isOptional)
        {
            return allowSubTypes && isOptional ? BuiltInType.ExtensionObject : BuiltInType.Null;
        }

        private static bool TryGetDefinition<TDefinition>(
            ExtensionObject definition,
            out TDefinition typeDefinition)
            where TDefinition : class, IEncodeable
        {
            return definition.TryGetEncodeable(out typeDefinition);
        }

        private static void RegisterEnumeratedType(
            ISystemContext context,
            IEncodeableFactoryBuilder factoryBuilder,
            DataTypeState dataType,
            IEnumeratedType enumType,
            IDictionary<NodeId, NodeState> nodesById)
        {
            ExpandedNodeId typeId = NodeId.ToExpandedNodeId(dataType.NodeId, context.NamespaceUris);
            factoryBuilder.AddEnumeratedType(typeId, enumType);

            (ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId, ExpandedNodeId jsonEncodingId)
                = GetEncodingIds(context, dataType, nodesById);

            if (!binaryEncodingId.IsNull)
            {
                factoryBuilder.AddEnumeratedType(binaryEncodingId, enumType);
            }
            if (!xmlEncodingId.IsNull)
            {
                factoryBuilder.AddEnumeratedType(xmlEncodingId, enumType);
            }
            if (!jsonEncodingId.IsNull)
            {
                factoryBuilder.AddEnumeratedType(jsonEncodingId, enumType);
            }
        }

        private static void RegisterEncodeableType(
            ISystemContext context,
            IEncodeableFactoryBuilder factoryBuilder,
            DataTypeState dataType,
            IEncodeableType encodeableType,
            IDictionary<NodeId, NodeState> nodesById)
        {
            ExpandedNodeId typeId = NodeId.ToExpandedNodeId(dataType.NodeId, context.NamespaceUris);
            factoryBuilder.AddEncodeableType(typeId, encodeableType);

            (ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId, ExpandedNodeId jsonEncodingId)
                = GetEncodingIds(context, dataType, nodesById);

            if (!binaryEncodingId.IsNull)
            {
                factoryBuilder.AddEncodeableType(binaryEncodingId, encodeableType);
            }
            if (!xmlEncodingId.IsNull)
            {
                factoryBuilder.AddEncodeableType(xmlEncodingId, encodeableType);
            }
            if (!jsonEncodingId.IsNull)
            {
                factoryBuilder.AddEncodeableType(jsonEncodingId, encodeableType);
            }
        }

        private static (ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId, ExpandedNodeId jsonEncodingId)
            GetEncodingIds(
                ISystemContext context,
                DataTypeState dataType,
                IDictionary<NodeId, NodeState> nodesById)
        {
            ExpandedNodeId binaryEncodingId = ExpandedNodeId.Null;
            ExpandedNodeId xmlEncodingId = ExpandedNodeId.Null;
            ExpandedNodeId jsonEncodingId = ExpandedNodeId.Null;

            var encodingReferences = new List<IReference>();
            dataType.GetReferences(context, encodingReferences, ReferenceTypeIds.HasEncoding, false);
            foreach (IReference reference in encodingReferences)
            {
                NodeId encodingNodeId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                if (encodingNodeId.IsNull)
                {
                    continue;
                }

                ExpandedNodeId expandedEncodingId = NodeId.ToExpandedNodeId(encodingNodeId, context.NamespaceUris);
                if (!nodesById.TryGetValue(encodingNodeId, out NodeState encodingNode))
                {
                    continue;
                }

                if (encodingNode.BrowseName == BrowseNames.DefaultBinary)
                {
                    binaryEncodingId = expandedEncodingId;
                }
                else if (encodingNode.BrowseName == BrowseNames.DefaultXml)
                {
                    xmlEncodingId = expandedEncodingId;
                }
                else if (encodingNode.BrowseName == BrowseNames.DefaultJson)
                {
                    jsonEncodingId = expandedEncodingId;
                }
            }

            return (binaryEncodingId, xmlEncodingId, jsonEncodingId);
        }

        private static (ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId)
            GetBinaryAndXmlEncodingIds(
                ISystemContext context,
                DataTypeState dataType,
                IDictionary<NodeId, NodeState> nodesById)
        {
            (ExpandedNodeId binaryEncodingId, ExpandedNodeId xmlEncodingId, _)
                = GetEncodingIds(context, dataType, nodesById);
            return (binaryEncodingId, xmlEncodingId);
        }

        private static XmlQualifiedName GetXmlName(ISystemContext context, DataTypeState dataType)
        {
            string namespaceUri = context.NamespaceUris?.GetString(dataType.NodeId.NamespaceIndex) ?? string.Empty;
            return new XmlQualifiedName(dataType.BrowseName.Name, namespaceUri);
        }

        private static void FlattenNodeTree(
            ISystemContext context,
            NodeState node,
            IList<NodeState> allNodes)
        {
            allNodes.Add(node);

            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                FlattenNodeTree(context, child, allNodes);
            }
        }

        private const uint DefaultUnionSelector = 0;
    }
}
