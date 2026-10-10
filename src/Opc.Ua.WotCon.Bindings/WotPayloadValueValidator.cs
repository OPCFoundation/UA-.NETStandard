/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Checks decoded payloads against native contracts without interpreting their wire representation.
    /// </summary>
    internal sealed class WotPayloadValueValidator
    {
        public WotPayloadValueValidator(IServiceMessageContext context)
        {
            m_context = context;
            m_namespaces = new NamespaceTable(context.NamespaceUris.ToArray());
            m_types = new TypeTable(m_namespaces);
        }

        public void Validate(in Variant value, ExpandedNodeId dataTypeId, TypeInfo typeInfo)
        {
            WotBindingValueMapper.ValidateContext(value, m_context);
            NodeId expected = RegisterType(dataTypeId, typeInfo.BuiltInType, 0);
            ValidateValue(value, expected, typeInfo.ValueRank, 0);
        }

        private void ValidateValue(in Variant value, NodeId expected, int rank, int depth)
        {
            CheckDepth(depth);
            if (value.TypeInfo.BuiltInType == BuiltInType.ExtensionObject && !value.TypeInfo.IsScalar)
            {
                BuiltInType type = TypeInfo.GetBuiltInType(expected, m_types);
                if (!ValueRanks.IsValid(value.TypeInfo.ValueRank, rank) ||
                    type is not BuiltInType.ExtensionObject and not BuiltInType.Variant)
                {
                    throw TypeMismatch(expected, rank);
                }
                foreach (Variant element in WotBindingValueMapper.Elements(value))
                {
                    ValidateValue(element, expected, ValueRanks.Scalar, depth + 1);
                }
                return;
            }
            if (expected != Ua.DataTypeIds.BaseDataType && expected != Ua.DataTypeIds.Structure)
            {
                RegisterValueTypes(value, depth);
            }
            // A typed null ExtensionObject has the same nullable native semantics as a null Variant.
            Variant checkedValue = value.TryGetValue(out ExtensionObject structure) && structure.IsNull
                ? Variant.Null : value;
            if (TypeInfo.IsInstanceOfDataType(checkedValue, expected, rank, m_namespaces, m_types).IsUnknown)
            {
                throw TypeMismatch(expected, rank);
            }
        }

        private void RegisterValueTypes(in Variant value, int depth)
        {
            CheckDepth(depth);
            if (value.TryGetValue(out ExtensionObject extension))
            {
                if (!extension.IsNull)
                {
                    if (!extension.TryGetValue(out IEncodeable? body, m_context))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "Native validation requires a decoded Structure.");
                    }
                    _ = RegisterType(body.TypeId, BuiltInType.Null, depth + 1);
                }
            }
            else if (value.TypeInfo.BuiltInType == BuiltInType.Variant && !value.TypeInfo.IsScalar)
            {
                foreach (Variant element in WotBindingValueMapper.Elements(value))
                {
                    RegisterValueTypes(element, depth + 1);
                }
            }
        }

        private NodeId RegisterType(ExpandedNodeId identity, BuiltInType fallback, int depth)
        {
            CheckDepth(depth);
            if (identity.IsNull || identity.ServerIndex != 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The native DataType is unresolved.");
            }
            ExpandedNodeId portable = identity.IsAbsolute
                ? identity : NodeId.ToExpandedNodeId(identity.InnerNodeId, m_context.NamespaceUris);
            if (portable.NamespaceUri is { Length: > 0 } uri)
            {
                m_namespaces.GetIndexOrAppend(uri);
            }
            var local = ExpandedNodeId.ToNodeId(portable, m_namespaces);
            if (m_types.IsKnown(local))
            {
                return local;
            }
            if (!m_registering.Add(local))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The native DataType ancestry is cyclic.");
            }
            try
            {
                if (local.NamespaceIndex == 0 && local.TryGetValue(out uint numeric) && numeric is > 0 and <= 29)
                {
                    m_types.AddSubtype(local, NodeId.Null);
                    return local;
                }
                ExpandedNodeId parent;
                if (m_context.Factory.TryGetEncodeableType(portable, out IEncodeableType? activator) &&
                    activator is IDataTypeDefinitionSource source &&
                    source.GetDataTypeDefinition(m_namespaces) is StructureDefinition definition)
                {
                    parent = NodeId.ToExpandedNodeId(definition.BaseDataType, m_namespaces);
                }
                else if (m_context.Factory.TryGetEnumeratedType(portable, out _))
                {
                    parent = Ua.DataTypeIds.Enumeration;
                }
                else
                {
                    BuiltInType builtIn = TypeInfo.GetBuiltInType(local);
                    if (builtIn == BuiltInType.Null)
                    {
                        builtIn = fallback;
                    }
                    if (builtIn is BuiltInType.Null or BuiltInType.ExtensionObject)
                    {
                        throw new ServiceResultException(StatusCodes.BadNotSupported,
                            $"The native DataType '{identity}' requires registered type metadata.");
                    }
                    parent = new ExpandedNodeId((uint)builtIn);
                }
                NodeId superType = RegisterType(parent, BuiltInType.Null, depth + 1);
                m_types.AddSubtype(local, superType);
                return local;
            }
            finally
            {
                m_registering.Remove(local);
            }
        }

        private void CheckDepth(int depth)
        {
            if (depth >= m_context.MaxEncodingNestingLevels)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The native type validation nesting limit was exceeded.");
            }
        }

        private static ServiceResultException TypeMismatch(NodeId dataTypeId, int rank)
        {
            return new ServiceResultException(StatusCodes.BadTypeMismatch,
                $"The codec value does not match native DataType '{dataTypeId}' and ValueRank '{rank}'.");
        }

        private readonly IServiceMessageContext m_context;
        private readonly NamespaceTable m_namespaces;
        private readonly TypeTable m_types;
        private readonly HashSet<NodeId> m_registering = [];
    }
}
