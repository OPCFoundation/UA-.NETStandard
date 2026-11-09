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
using System.Linq;
using System.Text.Json.Serialization;
using Opc.Ua;

namespace UaLens.Plugins.PubSub
{
    /// <summary>
    /// Portable structure declaration, not a CLR type or an executable provider reference.
    /// </summary>
    internal sealed record PubSubStructureConfiguration
    {
        /// <summary>
        /// Gets the namespace-qualified type identity.
        /// </summary>
        public string DataTypeId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the namespace-qualified default binary encoding identity.
        /// </summary>
        public string BinaryEncodingId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the namespace-qualified base type identity.
        /// </summary>
        public string BaseDataTypeId { get; init; } = "i=22";

        /// <summary>
        /// Gets the unqualified type browse name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the concrete structure layout and presence-mask contract.
        /// </summary>
        public StructureType StructureType { get; init; }

        /// <summary>
        /// Gets the wire-ordered field definitions, including inherited fields.
        /// </summary>
        [JsonConverter(typeof(PubSubStructureFieldsJsonConverter))]
        public ArrayOf<PubSubStructureFieldConfiguration> Fields { get; init; } = [];
    }

    /// <summary>
    /// One declared field with exact type identity, shape and optionality.
    /// </summary>
    internal sealed record PubSubStructureFieldConfiguration
    {
        /// <summary>
        /// Gets the unique field name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the namespace-qualified field element type.
        /// </summary>
        public string DataTypeId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the concrete scalar, array or matrix rank.
        /// </summary>
        public int ValueRank { get; init; } = ValueRanks.Scalar;

        /// <summary>
        /// Gets the optional per-dimension element maxima.
        /// </summary>
        [JsonConverter(typeof(PubSubDimensionsJsonConverter))]
        public ArrayOf<uint> ArrayDimensions { get; init; } = [];

        /// <summary>
        /// Gets whether an optional structure's encoding mask controls this field.
        /// </summary>
        public bool IsOptional { get; init; }

        /// <summary>
        /// Gets the declared string or byte-string length bound.
        /// </summary>
        public uint MaxStringLength { get; init; }

        /// <summary>
        /// Gets the optional description text.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Gets the description locale.
        /// </summary>
        public string? DescriptionLocale { get; init; }
    }

    /// <summary>
    /// Validates and translates the reviewed metadata graph without loading assemblies or contacting a server.
    /// </summary>
    internal static class PubSubStructureSchema
    {
        /// <summary>
        /// Compares the wire-significant reviewed field schema, including type definitions and versions.
        /// </summary>
        public static bool Matches(DataSetMetaDataType expected, DataSetMetaDataType actual)
        {
            if (expected.DataSetClassId != actual.DataSetClassId ||
                expected.ConfigurationVersion?.MajorVersion != actual.ConfigurationVersion?.MajorVersion ||
                expected.ConfigurationVersion?.MinorVersion != actual.ConfigurationVersion?.MinorVersion ||
                !expected.Namespaces.Span.SequenceEqual(actual.Namespaces.Span) ||
                expected.Fields.Count != actual.Fields.Count ||
                expected.StructureDataTypes.Count != actual.StructureDataTypes.Count ||
                !actual.EnumDataTypes.IsEmpty ||
                !actual.SimpleDataTypes.IsEmpty)
            {
                return false;
            }
            for (int index = 0; index < expected.Fields.Count; index++)
            {
                FieldMetaData left = expected.Fields[index];
                FieldMetaData right = actual.Fields[index];
                if (right is null ||
                    left.Name != right.Name ||
                    left.BuiltInType != right.BuiltInType ||
                    left.DataType != right.DataType ||
                    left.ValueRank != right.ValueRank ||
                    left.DataSetFieldId != right.DataSetFieldId ||
                    left.FieldFlags != right.FieldFlags ||
                    left.MaxStringLength != right.MaxStringLength ||
                    !left.ArrayDimensions.Span.SequenceEqual(right.ArrayDimensions.Span))
                {
                    return false;
                }
            }
            for (int index = 0; index < expected.StructureDataTypes.Count; index++)
            {
                if (!expected.StructureDataTypes[index].IsEqual(actual.StructureDataTypes[index]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Builds an isolated namespace table from the reviewed portable configuration.
        /// </summary>
        public static NamespaceTable NamespaceTable(PubSubConfiguration configuration)
        {
            return configuration.Namespaces.IsEmpty
                ? new NamespaceTable() : new NamespaceTable(configuration.Namespaces.ToList());
        }

        /// <summary>
        /// Rejects unsupported, ambiguous, unbounded or unresolved type graphs before creating adapters.
        /// </summary>
        public static void Validate(PubSubConfiguration configuration)
        {
            if (configuration.Namespaces.Count > 32 ||
                configuration.Structures.Count > 32 ||
                (!configuration.Namespaces.IsEmpty && configuration.Namespaces[0] != Namespaces.OpcUa))
            {
                throw new ArgumentException(
                    "Use at most 32 namespace URIs and 32 structures; namespace zero is OPC UA.");
            }
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (string uri in configuration.Namespaces)
            {
                if (string.IsNullOrWhiteSpace(uri) ||
                    uri.Length > 1024 ||
                    !Uri.TryCreate(uri, UriKind.Absolute, out _) ||
                    !namespaces.Add(uri))
                {
                    throw new ArgumentException("Metadata namespaces must be distinct bounded absolute URIs.");
                }
            }
            NamespaceTable table = NamespaceTable(configuration);
            var structures = new Dictionary<NodeId, PubSubStructureConfiguration>();
            var encodings = new HashSet<NodeId>();
            int total = 0;
            foreach (PubSubStructureConfiguration structure in configuration.Structures)
            {
                if (structure is null ||
                    string.IsNullOrWhiteSpace(structure.Name) ||
                    structure.Name.Length > 128 ||
                    PubSubConfigurationValidation.HasControlCharacters(structure.Name) ||
                    structure.Fields.IsNull ||
                    structure.Fields.Count > 64 ||
                    structure.StructureType is not (StructureType.Structure or
                        StructureType.StructureWithOptionalFields or StructureType.Union))
                {
                    throw new ArgumentException("Use bounded concrete structures, optional structures or unions.");
                }
                NodeId id = Resolve(structure.DataTypeId, table);
                NodeId encoding = Resolve(structure.BinaryEncodingId, table);
                if (id.NamespaceIndex == 0 ||
                    encoding.NamespaceIndex == 0 ||
                    !structures.TryAdd(id, structure) ||
                    !encodings.Add(encoding))
                {
                    throw new ArgumentException(
                        "Custom structures require distinct nonstandard type and encoding IDs.");
                }
                total += structure.Fields.Count;
                if (total > 512)
                {
                    throw new ArgumentException("The schema graph exceeds 512 structure fields.");
                }
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                int optional = 0;
                foreach (PubSubStructureFieldConfiguration field in structure.Fields)
                {
                    if (field is null ||
                        string.IsNullOrWhiteSpace(field.Name) ||
                        field.Name.Length > 128 ||
                        PubSubConfigurationValidation.HasControlCharacters(field.Name) ||
                        !fieldNames.Add(field.Name) ||
                        !PubSubFieldSchema.IsValidShape(field.ValueRank, field.ArrayDimensions) ||
                        field.MaxStringLength > PubSubConfigurationValidation.MaxValueCharacters ||
                        field.Description is { Length: > 512 } ||
                        field.DescriptionLocale is { Length: > 64 } ||
                        (field.IsOptional && structure.StructureType != StructureType.StructureWithOptionalFields))
                    {
                        throw new ArgumentException("A structure field has invalid type shape, text or optionality.");
                    }
                    if (field.IsOptional && ++optional > 32)
                    {
                        throw new ArgumentException("At most 32 optional fields are supported.");
                    }
                }
            }
            if (structures.Keys.Any(encodings.Contains))
            {
                throw new ArgumentException("DataType and binary encoding identities must not collide.");
            }
            var complete = new HashSet<NodeId>();
            var visiting = new HashSet<NodeId>();
            foreach (NodeId id in structures.Keys)
            {
                Visit(id, 0);
            }
            foreach (PubSubFieldConfiguration field in configuration.Fields)
            {
                if (field is null)
                {
                    continue;
                }
                NodeId id = FieldType(field, table);
                if (field.Type == BuiltInType.ExtensionObject ? !structures.ContainsKey(id)
                    : id != new NodeId((uint)field.Type))
                {
                    throw new ArgumentException(
                        "Each custom dataset field needs its exact reviewed structure definition.");
                }
            }

            void Visit(NodeId id, int depth)
            {
                if (id.NamespaceIndex == 0 && PubSubConfigurationValidation.IsScalarType(TypeInfo.GetBuiltInType(id)))
                {
                    return;
                }
                if (complete.Contains(id))
                {
                    return;
                }
                if (depth >= 16 ||
                    !visiting.Add(id) ||
                    !structures.TryGetValue(id, out PubSubStructureConfiguration? type))
                {
                    throw new ArgumentException(
                        "The type graph is recursive, unresolved or exceeds 16 nested structures.");
                }
                NodeId baseType = Resolve(type.BaseDataTypeId, table);
                if (baseType != DataTypeIds.Structure && baseType != DataTypeIds.Union)
                {
                    Visit(baseType, depth + 1);
                }
                foreach (PubSubStructureFieldConfiguration field in type.Fields)
                {
                    Visit(Resolve(field.DataTypeId, table), depth + 1);
                }
                visiting.Remove(id);
                complete.Add(id);
            }
        }

        /// <summary>
        /// Captures portable definitions from supported metadata without retaining the mutable source graph.
        /// </summary>
        public static ArrayOf<PubSubStructureConfiguration> Capture(DataSetMetaDataType metadata)
        {
            if (metadata.StructureDataTypes.Count > 32)
            {
                throw new ArgumentException("Metadata exceeds 32 structure definitions.");
            }
            if (!metadata.EnumDataTypes.IsEmpty || !metadata.SimpleDataTypes.IsEmpty)
            {
                throw new ArgumentException(
                    "Enumeration and scalar-alias metadata require an explicit supported type adapter.");
            }
            var configuration = new PubSubConfiguration { Namespaces = metadata.Namespaces, Fields = [] };
            Validate(configuration);
            NamespaceTable table = NamespaceTable(configuration);
            var types = new List<PubSubStructureConfiguration>();
            foreach (StructureDescription description in metadata.StructureDataTypes)
            {
                if (description is null ||
                    description.StructureDefinition is null ||
                    description.Name.NamespaceIndex != description.DataTypeId.NamespaceIndex ||
                    description.StructureDefinition.Fields.Count > 64 ||
                    description.StructureDefinition.Fields.Contains(field => field is null))
                {
                    throw new ArgumentException("Each structure requires coherent name and type metadata.");
                }
                StructureDefinition definition = description.StructureDefinition;
                types.Add(new PubSubStructureConfiguration
                {
                    Name = description.Name.Name ?? string.Empty,
                    DataTypeId = Portable(description.DataTypeId, table),
                    BinaryEncodingId = Portable(definition.DefaultEncodingId, table),
                    BaseDataTypeId = Portable(definition.BaseDataType, table),
                    StructureType = definition.StructureType,
                    Fields = [.. definition.Fields.ToList().Select(field => new PubSubStructureFieldConfiguration
                    {
                        Name = field.Name ?? string.Empty, DataTypeId = Portable(field.DataType, table),
                        ValueRank = field.ValueRank, ArrayDimensions = CoreUtils.Clone(field.ArrayDimensions),
                        IsOptional = field.IsOptional, MaxStringLength = field.MaxStringLength,
                        Description = field.Description.Text, DescriptionLocale = field.Description.Locale
                    })]
                });
            }
            configuration = configuration with { Structures = [.. types] };
            Validate(configuration);
            return configuration.Structures;
        }

        /// <summary>
        /// Projects a portable structure into one explicitly selected namespace table.
        /// </summary>
        public static StructureDefinition Definition(PubSubStructureConfiguration structure, NamespaceTable namespaces)
        {
            return new StructureDefinition
            {
                BaseDataType = Resolve(structure.BaseDataTypeId, namespaces),
                DefaultEncodingId = Resolve(structure.BinaryEncodingId, namespaces),
                StructureType = structure.StructureType,
                Fields = [.. structure.Fields.ToList().Select(field => new StructureField
                {
                    Name = field.Name, DataType = Resolve(field.DataTypeId, namespaces),
                    ValueRank = field.ValueRank, ArrayDimensions = CoreUtils.Clone(field.ArrayDimensions),
                    IsOptional = field.IsOptional, MaxStringLength = field.MaxStringLength,
                    Description = new LocalizedText(field.DescriptionLocale, field.Description)
                })]
            };
        }

        /// <summary>
        /// Resolves the exact declared dataset field type.
        /// </summary>
        public static NodeId FieldType(PubSubFieldConfiguration field, NamespaceTable namespaces)
        {
            if (field.DataTypeId is null)
            {
                throw new ArgumentException("A field DataTypeId cannot be null.");
            }
            return field.DataTypeId.Length == 0 ? new NodeId((uint)field.Type) : Resolve(field.DataTypeId, namespaces);
        }

        /// <summary>
        /// Resolves a portable local identity and rejects unlisted namespaces.
        /// </summary>
        public static NodeId Resolve(string identifier, NamespaceTable namespaces)
        {
            if (!PubSubConfigurationValidation.IsPortableNodeId(identifier))
            {
                throw new ArgumentException("A schema type requires a portable local ExpandedNodeId.");
            }
            var id = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(identifier), namespaces);
            if (id.IsNull)
            {
                throw new ArgumentException("A schema type references an unlisted namespace.");
            }
            return id;
        }

        /// <summary>
        /// Captures a namespace-qualified local identity instead of persisting a transient index.
        /// </summary>
        public static string Portable(NodeId node, NamespaceTable namespaces)
        {
            if (node.IsNull || node.NamespaceIndex >= namespaces.Count)
            {
                throw new ArgumentException("A metadata type references an invalid namespace or null identity.");
            }
            return node.NamespaceIndex == 0 ? node.ToString() : NodeId.ToExpandedNodeId(node, namespaces).ToString();
        }
    }
}
