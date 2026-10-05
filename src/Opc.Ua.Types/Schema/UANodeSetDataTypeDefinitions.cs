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

using System;
using System.Collections.Generic;
using Opc.Ua.Types;

namespace Opc.Ua.Export
{
    public partial class UANodeSet
    {
        private static readonly NodeId s_unionDataTypeId = new(DataTypes.Union);

        /// <summary>
        /// Completes the StructureDefinitions of imported DataTypes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A NodeSet <c>Definition</c> lists only the fields a DataType adds to its
        /// supertype (Part 6 F.12), while the DataTypeDefinition attribute begins with
        /// the fields of the baseDataType followed by the own fields (Part 3 8.48). This
        /// follows the HasSubtype chain through <paramref name="nodes"/> and, for
        /// supertypes outside of it, through <paramref name="resolveExternalDefinition"/>
        /// and the context's encodeable factory, and prepends the inherited fields.
        /// </para>
        /// <para>
        /// A Structure subtype without a definition (its NodeSet Definition has no
        /// fields) receives one, the StructureType of a subtype follows the optional or
        /// subtyped fields it inherits, and the DefaultEncodingId of a concrete
        /// Structure is set to its "Default Binary" encoding when that node is part of
        /// <paramref name="nodes"/>.
        /// </para>
        /// <para>
        /// The operation is idempotent: <see cref="StructureDefinition.FirstExplicitFieldIndex"/>
        /// records the inherited part, and a definition which already begins with the
        /// inherited fields is not extended again. DataTypes whose supertype chain cannot
        /// be resolved are left unchanged.
        /// </para>
        /// </remarks>
        /// <param name="context">The system context of the imported nodes.</param>
        /// <param name="nodes">The imported nodes.</param>
        /// <param name="resolveExternalDefinition">
        /// Optional resolver returning the complete DataTypeDefinition of a DataType
        /// which is not part of <paramref name="nodes"/>, or <c>null</c> if unknown.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> or <paramref name="nodes"/> is <c>null</c>.
        /// </exception>
        public static void CompleteDataTypeDefinitions(
            ISystemContext context,
            IEnumerable<NodeState> nodes,
            Func<NodeId, Ua.DataTypeDefinition?>? resolveExternalDefinition = null)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            new DataTypeDefinitionCompleter(context, nodes, resolveExternalDefinition).Run();
        }

        /// <summary>
        /// Returns the direct supertype a NodeSet DataType declares through its inverse
        /// HasSubtype reference.
        /// </summary>
        private NodeId FindSuperTypeId(UANode node, NamespaceTable namespaceUris)
        {
            if (node.References == null)
            {
                return NodeId.Null;
            }

            foreach (Reference reference in node.References)
            {
                if (reference == null || reference.IsForward)
                {
                    continue;
                }

                NodeId referenceTypeId = ImportNodeId(
                    reference.ReferenceType,
                    namespaceUris,
                    true);
                if (referenceTypeId == ReferenceTypeIds.HasSubtype)
                {
                    return ImportNodeId(reference.Value, namespaceUris, true);
                }
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Merges inherited structure fields into the definitions of one import batch.
        /// </summary>
        private sealed class DataTypeDefinitionCompleter
        {
            public DataTypeDefinitionCompleter(
                ISystemContext context,
                IEnumerable<NodeState> nodes,
                Func<NodeId, Ua.DataTypeDefinition?>? resolveExternalDefinition)
            {
                m_context = context;
                m_resolveExternalDefinition = resolveExternalDefinition;
                foreach (NodeState node in nodes)
                {
                    if (node == null || node.NodeId.IsNull)
                    {
                        continue;
                    }

                    if (!m_nodes.ContainsKey(node.NodeId))
                    {
                        m_nodes[node.NodeId] = node;
                    }
                    if (node is DataTypeState dataType && !m_dataTypes.ContainsKey(node.NodeId))
                    {
                        m_dataTypes[node.NodeId] = dataType;
                    }
                }
            }

            public void Run()
            {
                if (m_dataTypes.Count == 0)
                {
                    return;
                }

                foreach (DataTypeState dataType in m_dataTypes.Values)
                {
                    Resolve(dataType.NodeId);
                }

                Dictionary<NodeId, NodeId>? inverseEncodings = null;
                foreach (DataTypeState dataType in m_dataTypes.Values)
                {
                    if (dataType.IsAbstract ||
                        !dataType.DataTypeDefinition.TryGetValue(out StructureDefinition? structure) ||
                        structure == null ||
                        !structure.DefaultEncodingId.IsNull)
                    {
                        continue;
                    }

                    // Part 3 8.48: a concrete Structure names its Default Binary encoding.
                    NodeId encodingId = FindDefaultBinaryEncoding(dataType);
                    if (encodingId.IsNull)
                    {
                        inverseEncodings ??= CollectInverseDefaultBinaryEncodings();
                        inverseEncodings.TryGetValue(dataType.NodeId, out encodingId);
                    }
                    if (!encodingId.IsNull)
                    {
                        structure.DefaultEncodingId = encodingId;
                    }
                }
            }

            private NodeId FindDefaultBinaryEncoding(DataTypeState dataType)
            {
                var references = new List<IReference>();
                dataType.GetReferences(
                    m_context,
                    references,
                    ReferenceTypeIds.HasEncoding,
                    false);
                foreach (IReference reference in references)
                {
                    NodeId targetId = ExpandedNodeId.ToNodeId(
                        reference.TargetId,
                        m_context.NamespaceUris);
                    if (!targetId.IsNull &&
                        m_nodes.TryGetValue(targetId, out NodeState? encoding) &&
                        IsDefaultBinary(encoding))
                    {
                        return targetId;
                    }
                }
                return NodeId.Null;
            }

            private Dictionary<NodeId, NodeId> CollectInverseDefaultBinaryEncodings()
            {
                var encodings = new Dictionary<NodeId, NodeId>();
                var references = new List<IReference>();
                foreach (NodeState node in m_nodes.Values)
                {
                    if (!IsDefaultBinary(node))
                    {
                        continue;
                    }

                    references.Clear();
                    node.GetReferences(
                        m_context,
                        references,
                        ReferenceTypeIds.HasEncoding,
                        true);
                    foreach (IReference reference in references)
                    {
                        NodeId dataTypeId = ExpandedNodeId.ToNodeId(
                            reference.TargetId,
                            m_context.NamespaceUris);
                        if (!dataTypeId.IsNull && !encodings.ContainsKey(dataTypeId))
                        {
                            encodings[dataTypeId] = node.NodeId;
                        }
                    }
                }
                return encodings;
            }

            private static bool IsDefaultBinary(NodeState node)
            {
                return node.NodeClass == NodeClass.Object &&
                    node.BrowseName.NamespaceIndex == 0 &&
                    node.BrowseName.Name == BrowseNames.DefaultBinary;
            }

            private Resolution Resolve(NodeId dataTypeId)
            {
                if (dataTypeId.IsNull)
                {
                    return Resolution.NotStructure;
                }
                if (dataTypeId == DataTypeIds.Structure)
                {
                    return new Resolution(ResolutionKind.Structure, s_structureRoot);
                }
                if (dataTypeId == s_unionDataTypeId)
                {
                    return new Resolution(ResolutionKind.Structure, s_unionRoot);
                }
                if (m_results.TryGetValue(dataTypeId, out Resolution resolved))
                {
                    return resolved;
                }

                if (m_dataTypes.TryGetValue(dataTypeId, out DataTypeState? dataType))
                {
                    if (!m_inProgress.Add(dataTypeId))
                    {
                        // A HasSubtype cycle; the layout is undefined.
                        return Resolution.Unknown;
                    }
                    try
                    {
                        resolved = Complete(dataType);
                    }
                    finally
                    {
                        m_inProgress.Remove(dataTypeId);
                    }
                }
                else
                {
                    resolved = ResolveExternal(dataTypeId);
                }

                m_results[dataTypeId] = resolved;
                return resolved;
            }

            private Resolution ResolveExternal(NodeId dataTypeId)
            {
                // Built-in types, BaseDataType and the abstract Number, Integer,
                // UInteger and Enumeration types have no structure layout.
                if (dataTypeId.NamespaceIndex == 0 &&
                    dataTypeId.TryGetValue(out uint id) &&
                    id <= DataTypes.Enumeration)
                {
                    return Resolution.NotStructure;
                }

                Ua.DataTypeDefinition? definition = null;
                try
                {
                    definition = m_resolveExternalDefinition?.Invoke(dataTypeId);
                    if (definition == null && m_context.EncodeableFactory is { } factory)
                    {
                        var typeId = NodeId.ToExpandedNodeId(dataTypeId, m_context.NamespaceUris);
                        if (factory.TryGetEncodeableType(typeId, out IEncodeableType? encodeableType) &&
                            encodeableType is IDataTypeDefinitionSource structureSource)
                        {
                            definition = structureSource.GetDataTypeDefinition(m_context.NamespaceUris);
                        }
                        else if (factory.TryGetEnumeratedType(typeId, out IEnumeratedType? enumeratedType) &&
                            enumeratedType is IDataTypeDefinitionSource enumSource)
                        {
                            definition = enumSource.GetDataTypeDefinition(m_context.NamespaceUris);
                        }
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // An external definition which cannot be produced leaves the
                    // subtypes unchanged rather than failing the import.
                    definition = null;
                }

                return definition switch
                {
                    StructureDefinition structure => new Resolution(ResolutionKind.Structure, structure),
                    EnumDefinition => Resolution.NotStructure,
                    _ => Resolution.Unknown
                };
            }

            private Resolution Complete(DataTypeState dataType)
            {
                dataType.DataTypeDefinition.TryGetValue(out Ua.DataTypeDefinition? definition);
                if (definition is not null and not StructureDefinition)
                {
                    return Resolution.NotStructure;
                }

                Resolution baseResolution = Resolve(dataType.SuperTypeId);
                if (baseResolution.Kind != ResolutionKind.Structure)
                {
                    // An unresolved supertype leaves the definition as imported. A
                    // StructureDefinition below a non-structure supertype is malformed.
                    return definition is StructureDefinition
                        ? Resolution.Unknown
                        : baseResolution;
                }

                StructureDefinition baseDefinition = baseResolution.Definition!;
                var structure = definition as StructureDefinition;
                bool created = structure == null;
                structure ??= new StructureDefinition
                {
                    BaseDataType = dataType.SuperTypeId,
                    StructureType = baseDefinition.StructureType,
                    Fields = []
                };
                if (structure.BaseDataType.IsNull)
                {
                    structure.BaseDataType = dataType.SuperTypeId;
                }

                ArrayOf<StructureField> baseFields = baseDefinition.Fields;
                int baseCount = baseFields.IsNull ? 0 : baseFields.Count;
                List<StructureField> ownFields = GetOwnFields(structure, baseFields);

                bool clearInheritedOptional = false;
                if (baseCount > 0)
                {
                    int derivedKind = GetOptionalityKind(structure.StructureType);
                    int baseKind = GetOptionalityKind(baseDefinition.StructureType);
                    if (baseKind != 0 && derivedKind == 0)
                    {
                        bool isUnion = structure.StructureType == StructureType.Union;
                        if (baseKind == 2)
                        {
                            structure.StructureType = isUnion
                                ? StructureType.UnionWithSubtypedValues
                                : StructureType.StructureWithSubtypedValues;
                        }
                        else if (isUnion)
                        {
                            // A union cannot carry optional fields.
                            clearInheritedOptional = true;
                        }
                        else
                        {
                            structure.StructureType = StructureType.StructureWithOptionalFields;
                        }
                    }
                    else if (baseKind != 0 && derivedKind != baseKind)
                    {
                        // Optional and subtyped fields cannot be combined; IsOptional
                        // keeps the meaning of the derived StructureType.
                        clearInheritedOptional = true;
                    }
                }

                var fields = new List<StructureField>(baseCount + ownFields.Count);
                for (int ii = 0; ii < baseCount; ii++)
                {
                    var field = (StructureField)baseFields[ii].Clone();
                    if (clearInheritedOptional)
                    {
                        field.IsOptional = false;
                    }
                    fields.Add(field);
                }
                fields.AddRange(ownFields);

                structure.Fields = fields;
                structure.FirstExplicitFieldIndex = baseCount;
                if (created)
                {
                    dataType.DataTypeDefinition = new ExtensionObject(structure);
                }

                return new Resolution(ResolutionKind.Structure, structure);
            }

            /// <summary>
            /// Returns the fields a definition declares itself. A definition completed
            /// before records them after FirstExplicitFieldIndex; an imported one may
            /// already start with the inherited fields (detected by their names).
            /// </summary>
            private static List<StructureField> GetOwnFields(
                StructureDefinition structure,
                ArrayOf<StructureField> baseFields)
            {
                var ownFields = new List<StructureField>();
                ArrayOf<StructureField> fields = structure.Fields;
                if (fields.IsNull)
                {
                    return ownFields;
                }

                int start = Math.Min(Math.Max(structure.FirstExplicitFieldIndex, 0), fields.Count);
                int baseCount = baseFields.IsNull ? 0 : baseFields.Count;
                if (start == 0 && baseCount > 0 && fields.Count >= baseCount)
                {
                    bool includesBaseFields = true;
                    for (int ii = 0; ii < baseCount; ii++)
                    {
                        if (!string.Equals(fields[ii].Name, baseFields[ii].Name, StringComparison.Ordinal))
                        {
                            includesBaseFields = false;
                            break;
                        }
                    }
                    if (includesBaseFields)
                    {
                        start = baseCount;
                    }
                }

                for (int ii = start; ii < fields.Count; ii++)
                {
                    ownFields.Add(fields[ii]);
                }
                return ownFields;
            }

            /// <summary>
            /// 0 = plain structure or union, 1 = optional fields, 2 = subtyped values.
            /// </summary>
            private static int GetOptionalityKind(StructureType structureType)
            {
                return structureType switch
                {
                    StructureType.StructureWithOptionalFields => 1,
                    StructureType.StructureWithSubtypedValues or
                    StructureType.UnionWithSubtypedValues => 2,
                    _ => 0
                };
            }

            private enum ResolutionKind
            {
                Unknown,
                NotStructure,
                Structure
            }

            private readonly struct Resolution
            {
                public Resolution(ResolutionKind kind, StructureDefinition? definition)
                {
                    Kind = kind;
                    Definition = definition;
                }

                public static Resolution Unknown => new(ResolutionKind.Unknown, null);

                public static Resolution NotStructure => new(ResolutionKind.NotStructure, null);

                public ResolutionKind Kind { get; }

                public StructureDefinition? Definition { get; }
            }

            private static readonly StructureDefinition s_structureRoot = new()
            {
                BaseDataType = DataTypeIds.BaseDataType,
                StructureType = StructureType.Structure,
                Fields = []
            };

            private static readonly StructureDefinition s_unionRoot = new()
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Union,
                Fields = []
            };

            private readonly ISystemContext m_context;
            private readonly Func<NodeId, Ua.DataTypeDefinition?>? m_resolveExternalDefinition;
            private readonly Dictionary<NodeId, NodeState> m_nodes = [];
            private readonly Dictionary<NodeId, DataTypeState> m_dataTypes = [];
            private readonly Dictionary<NodeId, Resolution> m_results = [];
            private readonly HashSet<NodeId> m_inProgress = [];
        }
    }
}
