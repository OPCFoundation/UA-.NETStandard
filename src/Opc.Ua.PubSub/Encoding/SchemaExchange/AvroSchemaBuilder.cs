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
using System.Text;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Describes one DataSet field for Avro schema generation.
    /// </summary>
    internal readonly struct AvroSchemaField
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AvroSchemaField"/> struct.
        /// </summary>
        /// <param name="name">The DataSet field name.</param>
        /// <param name="builtInType">The OPC UA built-in type of the field value.</param>
        /// <param name="valueRank">The OPC UA ValueRank (-1 scalar, 1 array, >= 2 matrix).</param>
        /// <param name="encoding">The field framing selected by the DataSetFieldContentMask.</param>
        public AvroSchemaField(
            string? name,
            BuiltInType builtInType,
            int valueRank,
            PubSubFieldEncoding encoding)
            : this(name, builtInType, valueRank, encoding, default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AvroSchemaField"/> struct.
        /// </summary>
        /// <param name="name">The DataSet field name.</param>
        /// <param name="builtInType">The OPC UA built-in type of the field value.</param>
        /// <param name="valueRank">The OPC UA ValueRank (-1 scalar, 1 array, >= 2 matrix).</param>
        /// <param name="encoding">The field framing selected by the DataSetFieldContentMask.</param>
        /// <param name="dataType">The declared DataType NodeId, used to resolve custom types.</param>
        public AvroSchemaField(
            string? name,
            BuiltInType builtInType,
            int valueRank,
            PubSubFieldEncoding encoding,
            NodeId dataType)
        {
            Name = name;
            BuiltInType = builtInType;
            ValueRank = valueRank;
            Encoding = encoding;
            DataType = dataType;
        }

        /// <summary>Gets the declared DataType NodeId, when known.</summary>
        public NodeId DataType { get; }

        /// <summary>Gets the DataSet field name.</summary>
        public string? Name { get; }

        /// <summary>Gets the OPC UA built-in type of the field value.</summary>
        public BuiltInType BuiltInType { get; }

        /// <summary>Gets the OPC UA ValueRank.</summary>
        public int ValueRank { get; }

        /// <summary>Gets the field framing selected by the DataSetFieldContentMask.</summary>
        public PubSubFieldEncoding Encoding { get; }
    }

    /// <summary>
    /// Generates a self-contained Apache Avro schema document for one OPC UA DataSet, following
    /// the OPC UA Avro Encoding companion specification (§5 value mapping, §6.2 generation
    /// algorithm).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The emitted document is a real, parseable <c>.avsc</c>. That matters beyond tidiness: the
    /// SchemaId is defined as the CRC-64-AVRO Rabin fingerprint of the Avro <em>Parsing Canonical
    /// Form</em> (§6.3), which can only be computed from a document that actually parses as a
    /// schema. Emitting anything else silently degrades the SchemaId to a hash of arbitrary bytes.
    /// </para>
    /// <para>
    /// Determinism is a requirement, not a convenience (§6.2): fields are emitted in DataSet field
    /// order and are never sorted or omitted, and named types are inlined at first occurrence and
    /// referenced by full name afterwards (§6.2 step 9), so the same DataSet always produces
    /// byte-identical output and therefore the same SchemaId.
    /// </para>
    /// <para>
    /// Variant and ExtensionObject use the <em>shared</em> record form permitted by §6.6, which is
    /// the form used by the published reference schemas. The Variant <c>body</c> union carries the
    /// aggregated branch set for the DataSet, ordered by first appearance in field order, which
    /// makes growth append-only as required by §5.8.
    /// </para>
    /// <para>
    /// Scope note: this describes the <em>logical</em> DataSet as §5/§6.2 define it. The
    /// experimental PubSub wire framing writes each field value as an opaque nested Avro blob, so
    /// the announced schema is used for identity and for sharing the DataSet shape, while decoding
    /// remains structural. Emitting the typed body on the wire is the remaining step towards a
    /// fully schema-driven decoder (§7).
    /// </para>
    /// </remarks>
    internal static class AvroSchemaBuilder
    {
        /// <summary>
        /// The Avro namespace used for OPC UA base-model types (§6.5).
        /// </summary>
        public const string AvroNamespace = "org.opcfoundation.ua.avro";

        /// <summary>
        /// Builds the self-contained Avro schema document for a DataSet.
        /// </summary>
        /// <param name="dataSetName">The DataSet name used for the generated record.</param>
        /// <param name="fields">The DataSet fields in declaration order.</param>
        /// <param name="lineage">
        /// The accumulated field observations for this DataSet lineage, used to build the Variant
        /// body union. Growth is append-only (§5.8), so a branch that has been observed once keeps
        /// its union index in every later schema of the lineage. Pass <see langword="null"/> to use
        /// <paramref name="fields"/> alone.
        /// </param>
        /// <param name="resolver">
        /// Resolves custom DataTypes declared by a DataSetMetaData (§6.7). Pass
        /// <see langword="null"/> to map built-in types only.
        /// </param>
        /// <returns>The Avro schema document as JSON.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public static string Build(
            string dataSetName,
            IReadOnlyList<AvroSchemaField> fields,
            IReadOnlyList<AvroSchemaField>? lineage = null,
            AvroMetaDataTypeResolver? resolver = null)
        {
            if (fields is null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            IReadOnlyList<AvroSchemaField> branchSources = lineage ?? fields;
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            var builder = new StringBuilder(1024);

            builder.Append("{\"type\":\"record\",\"name\":\"")
                .Append(SanitizeName(dataSetName, "DataSet"))
                .Append("\",\"namespace\":\"")
                .Append(AvroNamespace)
                .Append("\",\"fields\":[");

            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AvroSchemaField field = fields[i];
                builder.Append("{\"name\":\"")
                    .Append(SanitizeName(field.Name, "Field" + i.ToString(CultureInfo.InvariantCulture)))
                    .Append("\",\"type\":")
                    .Append(FieldType(field, branchSources, emitted, resolver))
                    .Append('}');
            }

            builder.Append("]}");
            return builder.ToString();
        }

        /// <summary>
        /// Converts an OPC UA name to a legal Avro name (§6.2 step 2).
        /// </summary>
        /// <param name="name">The OPC UA name.</param>
        /// <param name="fallback">The name used when <paramref name="name"/> is empty.</param>
        /// <returns>A legal Avro name.</returns>
        public static string SanitizeName(string? name, string fallback)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }

            var builder = new StringBuilder(name!.Length + 2);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool legal = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '_';
                builder.Append(legal ? c : '_');
            }

            char first = builder[0];
            if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_'))
            {
                builder.Insert(0, "T_");
            }
            return builder.ToString();
        }

        private static string FieldType(
            AvroSchemaField field,
            IReadOnlyList<AvroSchemaField> allFields,
            HashSet<string> emitted,
            AvroMetaDataTypeResolver? resolver)
        {
            switch (field.Encoding)
            {
                case PubSubFieldEncoding.DataValue:
                    return DataValueType(allFields, emitted);
                case PubSubFieldEncoding.RawData:
                    return RankedType(field, emitted, resolver);
                case PubSubFieldEncoding.Variant:
                default:
                    return VariantType(allFields, emitted);
            }
        }

        private static string RankedType(
            AvroSchemaField field,
            HashSet<string> emitted,
            AvroMetaDataTypeResolver? resolver)
        {
            string element = CustomOrScalarType(field, emitted, resolver);
            if (field.ValueRank >= 2)
            {
                // §5.5 matrix: row-major values plus a dimensions vector, nullable as a whole.
                return "[\"null\",{\"type\":\"record\",\"name\":\""
                    + MatrixName(field.BuiltInType)
                    + "\",\"namespace\":\"" + AvroNamespace + "\",\"fields\":["
                    + "{\"name\":\"dimensions\",\"type\":{\"type\":\"array\",\"items\":\"int\"}},"
                    + "{\"name\":\"values\",\"type\":{\"type\":\"array\",\"items\":" + element + "}}]}]";
            }
            if (field.ValueRank == 1)
            {
                // §5.4 array: nullable array of possibly-null elements.
                return "[\"null\",{\"type\":\"array\",\"items\":" + element + "}]";
            }
            return element;
        }

        /// <summary>
        /// Maps a field to a custom type declared by the DataSetMetaData (§6.7) when one applies,
        /// and otherwise to the built-in mapping of §5.2.
        /// </summary>
        /// <param name="field">The field being mapped.</param>
        /// <param name="emitted">The named types already inlined in this document.</param>
        /// <param name="resolver">The metadata type resolver, when available.</param>
        /// <returns>The Avro type for the field element.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the declared DataType is neither a built-in nor declared by the metadata.
        /// </exception>
        private static string CustomOrScalarType(
            AvroSchemaField field,
            HashSet<string> emitted,
            AvroMetaDataTypeResolver? resolver)
        {
            if (resolver is not null && !field.DataType.IsNull && !IsBuiltInNodeId(field.DataType))
            {
                if (resolver.TryGetEnum(field.DataType, out EnumDescription? enumeration))
                {
                    // §5.3: enumerations carry the numeric value, never a symbolic Avro enum, so an
                    // unknown value stays forward-compatible.
                    _ = enumeration;
                    return "\"int\"";
                }
                if (resolver.TryGetSimpleType(field.DataType, out SimpleTypeDescription? simple))
                {
                    return ScalarType((BuiltInType)simple!.BuiltInType, emitted);
                }
                if (resolver.TryGetStructure(field.DataType, out StructureDescription? structure))
                {
                    return StructureRecord(structure!, emitted, resolver, new HashSet<string>(StringComparer.Ordinal));
                }
                if (field.BuiltInType is BuiltInType.ExtensionObject or BuiltInType.Null)
                {
                    // §6.7: a DataType that is neither a built-in nor declared by the metadata
                    // cannot be mapped. Substituting an opaque type here would produce a schema
                    // that looks correct and silently loses the structure, so fail instead.
                    throw new InvalidOperationException(
                        FormattableString.Invariant(
                            $"DataSetMetaData does not declare DataType '{field.DataType}' for field '{field.Name}', so no Avro schema can be generated for it."));
                }
            }
            return ScalarType(field.BuiltInType, emitted);
        }

        private static bool IsBuiltInNodeId(NodeId dataType)
        {
            return dataType.NamespaceIndex == 0
                && dataType.IdType == IdType.Numeric
                && Convert.ToUInt32(dataType.Identifier, CultureInfo.InvariantCulture) <= 25;
        }

        /// <summary>
        /// Emits an Avro record for a structured DataType declared by the metadata (§5.6, §5.7).
        /// </summary>
        /// <param name="structure">The structure description.</param>
        /// <param name="emitted">The named types already inlined in this document.</param>
        /// <param name="resolver">The metadata type resolver.</param>
        /// <param name="open">The structures currently being expanded, used to break cycles.</param>
        /// <returns>The Avro type for the structure.</returns>
        private static string StructureRecord(
            StructureDescription structure,
            HashSet<string> emitted,
            AvroMetaDataTypeResolver resolver,
            HashSet<string> open)
        {
            string name = SanitizeName(structure.Name.Name, "Structure");
            if (emitted.Contains(name) || !open.Add(name))
            {
                // A recursive structure references the enclosing record by name, which is what
                // keeps the generated document finite.
                return "\"" + AvroNamespace + "." + name + "\"";
            }
            emitted.Add(name);

            StructureDefinition definition = structure.StructureDefinition;
            bool isUnion = definition.StructureType is StructureType.Union
                or StructureType.UnionWithSubtypedValues;
            var builder = new StringBuilder();

            if (isUnion)
            {
                // §5.7: a Union DataType is a record with `switch` and `value`, where each branch is
                // wrapped in its own record so Avro branch resolution stays deterministic even when
                // two union fields share an Avro primitive type.
                builder.Append("{\"type\":\"record\",\"name\":\"").Append(name)
                    .Append("\",\"namespace\":\"").Append(AvroNamespace).Append("\",\"fields\":[")
                    .Append("{\"name\":\"switch\",\"type\":[\"null\",\"string\"],\"default\":null},")
                    .Append("{\"name\":\"value\",\"type\":[\"null\"");
                for (int i = 0; i < definition.Fields.Count; i++)
                {
                    StructureField field = definition.Fields[i];
                    string branch = name + "_" + SanitizeName(field.Name, "Field") + "_Branch";
                    emitted.Add(branch);
                    builder.Append(",{\"type\":\"record\",\"name\":\"").Append(branch)
                        .Append("\",\"namespace\":\"").Append(AvroNamespace).Append("\",\"fields\":[")
                        .Append("{\"name\":\"").Append(SanitizeName(field.Name, "Field"))
                        .Append("\",\"type\":")
                        .Append(StructureFieldType(field, emitted, resolver, open))
                        .Append("}]}");
                }
                builder.Append("],\"default\":null}]}");
                open.Remove(name);
                return builder.ToString();
            }

            builder.Append("{\"type\":\"record\",\"name\":\"").Append(name)
                .Append("\",\"namespace\":\"").Append(AvroNamespace).Append("\",\"fields\":[");
            for (int i = 0; i < definition.Fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                StructureField field = definition.Fields[i];
                string fieldName = SanitizeName(field.Name, "Field");
                string fieldType = StructureFieldType(field, emitted, resolver, open);
                if (field.IsOptional)
                {
                    // §5.6: an optional field uses a nullable wrapper record with a single `value`,
                    // so "absent" and "present but null" stay distinguishable.
                    string wrapper = name + "_" + fieldName + "_Optional";
                    emitted.Add(wrapper);
                    builder.Append("{\"name\":\"").Append(fieldName)
                        .Append("\",\"type\":[\"null\",{\"type\":\"record\",\"name\":\"").Append(wrapper)
                        .Append("\",\"namespace\":\"").Append(AvroNamespace)
                        .Append("\",\"fields\":[{\"name\":\"value\",\"type\":").Append(fieldType)
                        .Append("}]}],\"default\":null}");
                    continue;
                }
                builder.Append("{\"name\":\"").Append(fieldName)
                    .Append("\",\"type\":").Append(fieldType).Append('}');
            }
            builder.Append("]}");
            open.Remove(name);
            return builder.ToString();
        }

        private static string StructureFieldType(
            StructureField field,
            HashSet<string> emitted,
            AvroMetaDataTypeResolver resolver,
            HashSet<string> open)
        {
            string element;
            if (resolver.TryGetEnum(field.DataType, out _))
            {
                element = "\"int\"";
            }
            else if (resolver.TryGetSimpleType(field.DataType, out SimpleTypeDescription? simple))
            {
                element = ScalarType((BuiltInType)simple!.BuiltInType, emitted);
            }
            else if (resolver.TryGetStructure(field.DataType, out StructureDescription? nested))
            {
                element = StructureRecord(nested!, emitted, resolver, open);
            }
            else
            {
                element = ScalarType(BuiltInTypeOf(field.DataType), emitted);
            }

            if (field.ValueRank >= 2)
            {
                return "[\"null\",{\"type\":\"array\",\"items\":" + element + "}]";
            }
            if (field.ValueRank == 1)
            {
                return "{\"type\":\"array\",\"items\":" + element + "}";
            }
            return element;
        }

        private static BuiltInType BuiltInTypeOf(NodeId dataType)
        {
            if (dataType.IsNull
                || dataType.NamespaceIndex != 0
                || dataType.IdType != IdType.Numeric)
            {
                return BuiltInType.ExtensionObject;
            }
            uint id = Convert.ToUInt32(dataType.Identifier, CultureInfo.InvariantCulture);
            return id is >= 1 and <= 25 ? (BuiltInType)id : BuiltInType.ExtensionObject;
        }

        private static string MatrixName(BuiltInType builtInType)
        {
            return "Matrix" + builtInType;
        }

        private static string ScalarType(BuiltInType builtInType, HashSet<string> emitted)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    return "\"boolean\"";
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.StatusCode:
                case BuiltInType.Enumeration:
                    return "\"int\"";
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.DateTime:
                    return "\"long\"";
                case BuiltInType.Float:
                    return "\"float\"";
                case BuiltInType.Double:
                    return "\"double\"";
                case BuiltInType.String:
                case BuiltInType.XmlElement:
                    return "[\"null\",\"string\"]";
                case BuiltInType.ByteString:
                    return "[\"null\",\"bytes\"]";
                case BuiltInType.Guid:
                    return GuidType(emitted);
                case BuiltInType.NodeId:
                    return NodeIdType(emitted);
                case BuiltInType.ExpandedNodeId:
                    return ExpandedNodeIdType(emitted);
                case BuiltInType.QualifiedName:
                    return QualifiedNameType(emitted);
                case BuiltInType.LocalizedText:
                    return "[\"null\"," + LocalizedTextType(emitted) + "]";
                case BuiltInType.ExtensionObject:
                    return ExtensionObjectType(emitted);
                case BuiltInType.DiagnosticInfo:
                    return DiagnosticInfoType(emitted);
                default:
                    // Variant, DataValue and the abstract numeric types carry their runtime type
                    // inline, so they use the Variant record (§5.8).
                    return VariantRecord(Array.Empty<AvroSchemaField>(), emitted);
            }
        }

        private static string Named(HashSet<string> emitted, string name, string definition)
        {
            // §6.2 step 9: inline a named type at its first occurrence, then reference it by full
            // name. Without this the document is not self-contained and Avro rejects the redefinition.
            if (emitted.Add(name))
            {
                return definition;
            }
            return "\"" + AvroNamespace + "." + name + "\"";
        }

        private static string GuidType(HashSet<string> emitted)
        {
            return Named(
                emitted,
                "Guid",
                "{\"type\":\"fixed\",\"name\":\"Guid\",\"namespace\":\"" + AvroNamespace
                    + "\",\"size\":16,\"logicalType\":\"opcua-guid\"}");
        }

        private static string NodeIdType(HashSet<string> emitted)
        {
            if (emitted.Contains("NodeId"))
            {
                return "\"" + AvroNamespace + ".NodeId\"";
            }
            string guid = GuidType(emitted);
            emitted.Add("NodeId");
            return "{\"type\":\"record\",\"name\":\"NodeId\",\"namespace\":\"" + AvroNamespace
                + "\",\"fields\":["
                + "{\"name\":\"namespace\",\"type\":\"int\"},"
                + "{\"name\":\"idType\",\"type\":\"int\"},"
                + "{\"name\":\"numeric\",\"type\":[\"null\",\"long\"],\"default\":null},"
                + "{\"name\":\"string\",\"type\":[\"null\",\"string\"],\"default\":null},"
                + "{\"name\":\"guid\",\"type\":[\"null\"," + guid + "],\"default\":null},"
                + "{\"name\":\"opaque\",\"type\":[\"null\",\"bytes\"],\"default\":null}]}";
        }

        private static string ExpandedNodeIdType(HashSet<string> emitted)
        {
            if (emitted.Contains("ExpandedNodeId"))
            {
                return "\"" + AvroNamespace + ".ExpandedNodeId\"";
            }
            string nodeId = NodeIdType(emitted);
            emitted.Add("ExpandedNodeId");
            return "{\"type\":\"record\",\"name\":\"ExpandedNodeId\",\"namespace\":\"" + AvroNamespace
                + "\",\"fields\":["
                + "{\"name\":\"nodeId\",\"type\":" + nodeId + "},"
                + "{\"name\":\"namespaceUri\",\"type\":[\"null\",\"string\"],\"default\":null},"
                + "{\"name\":\"serverIndex\",\"type\":\"long\"}]}";
        }

        private static string QualifiedNameType(HashSet<string> emitted)
        {
            return Named(
                emitted,
                "QualifiedName",
                "{\"type\":\"record\",\"name\":\"QualifiedName\",\"namespace\":\"" + AvroNamespace
                    + "\",\"fields\":["
                    + "{\"name\":\"namespace\",\"type\":\"int\"},"
                    + "{\"name\":\"name\",\"type\":[\"null\",\"string\"],\"default\":null}]}");
        }

        private static string LocalizedTextType(HashSet<string> emitted)
        {
            return Named(
                emitted,
                "LocalizedText",
                "{\"type\":\"record\",\"name\":\"LocalizedText\",\"namespace\":\"" + AvroNamespace
                    + "\",\"fields\":["
                    + "{\"name\":\"locale\",\"type\":[\"null\",\"string\"],\"default\":null},"
                    + "{\"name\":\"text\",\"type\":[\"null\",\"string\"],\"default\":null}]}");
        }

        private static string ExtensionObjectType(HashSet<string> emitted)
        {
            if (emitted.Contains("ExtensionObject"))
            {
                return "\"" + AvroNamespace + ".ExtensionObject\"";
            }
            string nodeId = NodeIdType(emitted);
            emitted.Add("ExtensionObject");
            // §5.9: the known-struct branches grow append-only as concrete types are observed; the
            // opaque fallbacks are the starting point when no concrete type is known yet.
            return "{\"type\":\"record\",\"name\":\"ExtensionObject\",\"namespace\":\"" + AvroNamespace
                + "\",\"fields\":["
                + "{\"name\":\"typeId\",\"type\":" + nodeId + "},"
                + "{\"name\":\"body\",\"type\":[\"null\",\"bytes\",\"string\"],\"default\":null}]}";
        }

        private static string DiagnosticInfoType(HashSet<string> emitted)
        {
            return Named(
                emitted,
                "DiagnosticInfo",
                "{\"type\":\"record\",\"name\":\"DiagnosticInfo\",\"namespace\":\"" + AvroNamespace
                    + "\",\"fields\":["
                    + "{\"name\":\"symbolicId\",\"type\":[\"null\",\"int\"],\"default\":null},"
                    + "{\"name\":\"namespaceUri\",\"type\":[\"null\",\"int\"],\"default\":null},"
                    + "{\"name\":\"locale\",\"type\":[\"null\",\"int\"],\"default\":null},"
                    + "{\"name\":\"localizedText\",\"type\":[\"null\",\"int\"],\"default\":null},"
                    + "{\"name\":\"additionalInfo\",\"type\":[\"null\",\"string\"],\"default\":null},"
                    + "{\"name\":\"innerStatusCode\",\"type\":[\"null\",\"int\"],\"default\":null},"
                    + "{\"name\":\"innerDiagnosticInfo\",\"type\":[\"null\",\"" + AvroNamespace
                    + ".DiagnosticInfo\"],\"default\":null}]}");
        }

        private static string DataValueType(
            IReadOnlyList<AvroSchemaField> allFields,
            HashSet<string> emitted)
        {
            if (emitted.Contains("DataValue"))
            {
                return "\"" + AvroNamespace + ".DataValue\"";
            }
            string variant = VariantRecord(allFields, emitted);
            emitted.Add("DataValue");
            // §5.10: every member nullable and defaulting to null so absence stays distinct from a
            // present zero.
            return "{\"type\":\"record\",\"name\":\"DataValue\",\"namespace\":\"" + AvroNamespace
                + "\",\"fields\":["
                + "{\"name\":\"value\",\"type\":[\"null\"," + variant + "],\"default\":null},"
                + "{\"name\":\"status\",\"type\":[\"null\",\"int\"],\"default\":null},"
                + "{\"name\":\"sourceTimestamp\",\"type\":[\"null\",\"long\"],\"default\":null},"
                + "{\"name\":\"sourcePicoseconds\",\"type\":[\"null\",\"int\"],\"default\":null},"
                + "{\"name\":\"serverTimestamp\",\"type\":[\"null\",\"long\"],\"default\":null},"
                + "{\"name\":\"serverPicoseconds\",\"type\":[\"null\",\"int\"],\"default\":null}]}";
        }

        private static string VariantType(
            IReadOnlyList<AvroSchemaField> allFields,
            HashSet<string> emitted)
        {
            return VariantRecord(allFields, emitted);
        }

        private static string VariantRecord(
            IReadOnlyList<AvroSchemaField> allFields,
            HashSet<string> emitted)
        {
            if (emitted.Contains("Variant"))
            {
                return "\"" + AvroNamespace + ".Variant\"";
            }
            emitted.Add("Variant");

            // §5.8: the body union carries the aggregated branch set for this DataSet, ordered by
            // first appearance in field order. Ordering by appearance (rather than by name) is what
            // keeps growth append-only: an existing branch never changes index when a new one is
            // appended in a later MinorVersion.
            var branches = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < allFields.Count; i++)
            {
                AvroSchemaField field = allFields[i];
                if (field.Encoding == PubSubFieldEncoding.RawData)
                {
                    continue;
                }
                if (!IsConcreteBodyType(field.BuiltInType))
                {
                    continue;
                }
                string wrapper = VariantBranchName(field.BuiltInType, field.ValueRank);
                if (!seen.Add(wrapper))
                {
                    continue;
                }
                branches.Append(',').Append(VariantBranchRecord(field, wrapper, emitted));
            }

            return "{\"type\":\"record\",\"name\":\"Variant\",\"namespace\":\"" + AvroNamespace
                + "\",\"fields\":["
                + "{\"name\":\"builtInType\",\"type\":\"int\"},"
                + "{\"name\":\"dimensions\",\"type\":[\"null\",{\"type\":\"array\",\"items\":\"int\"}],\"default\":null},"
                + "{\"name\":\"body\",\"type\":[\"null\"" + branches + "],\"default\":null}]}";
        }

        private static bool IsConcreteBodyType(BuiltInType builtInType)
        {
            // §5.8: the body union excludes nested Variant, DataValue and DiagnosticInfo.
            switch (builtInType)
            {
                case BuiltInType.Null:
                case BuiltInType.Variant:
                case BuiltInType.DataValue:
                case BuiltInType.DiagnosticInfo:
                    return false;
                default:
                    return true;
            }
        }

        private static string VariantBranchName(BuiltInType builtInType, int valueRank)
        {
            string shape = valueRank >= 2 ? "MatrixBody" : valueRank == 1 ? "Array" : "Scalar";
            return "Variant" + builtInType + shape;
        }

        private static string VariantBranchRecord(
            AvroSchemaField field,
            string wrapper,
            HashSet<string> emitted)
        {
            emitted.Add(wrapper);
            string element = ScalarType(field.BuiltInType, emitted);
            if (field.ValueRank >= 2)
            {
                string matrix = "Variant" + field.BuiltInType + "Matrix";
                emitted.Add(matrix);
                return "{\"type\":\"record\",\"name\":\"" + wrapper + "\",\"namespace\":\"" + AvroNamespace
                    + "\",\"fields\":[{\"name\":\"matrix\",\"type\":"
                    + "{\"type\":\"record\",\"name\":\"" + matrix + "\",\"namespace\":\"" + AvroNamespace
                    + "\",\"fields\":["
                    + "{\"name\":\"dimensions\",\"type\":{\"type\":\"array\",\"items\":\"int\"}},"
                    + "{\"name\":\"values\",\"type\":{\"type\":\"array\",\"items\":" + element + "}}]}}]}";
            }
            if (field.ValueRank == 1)
            {
                return "{\"type\":\"record\",\"name\":\"" + wrapper + "\",\"namespace\":\"" + AvroNamespace
                    + "\",\"fields\":[{\"name\":\"values\",\"type\":{\"type\":\"array\",\"items\":"
                    + element + "}}]}";
            }
            return "{\"type\":\"record\",\"name\":\"" + wrapper + "\",\"namespace\":\"" + AvroNamespace
                + "\",\"fields\":[{\"name\":\"value\",\"type\":" + element + "}]}";
        }
    }
}
