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
        {
            Name = name;
            BuiltInType = builtInType;
            ValueRank = valueRank;
            Encoding = encoding;
        }

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
        /// <returns>The Avro schema document as JSON.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public static string Build(
            string dataSetName,
            IReadOnlyList<AvroSchemaField> fields,
            IReadOnlyList<AvroSchemaField>? lineage = null)
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
                    .Append(FieldType(field, branchSources, emitted))
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
            HashSet<string> emitted)
        {
            switch (field.Encoding)
            {
                case PubSubFieldEncoding.DataValue:
                    return DataValueType(allFields, emitted);
                case PubSubFieldEncoding.RawData:
                    return RankedType(field.BuiltInType, field.ValueRank, emitted);
                case PubSubFieldEncoding.Variant:
                default:
                    return VariantType(allFields, emitted);
            }
        }

        private static string RankedType(BuiltInType builtInType, int valueRank, HashSet<string> emitted)
        {
            string element = ScalarType(builtInType, emitted);
            if (valueRank >= 2)
            {
                // §5.5 matrix: row-major values plus a dimensions vector, nullable as a whole.
                return "[\"null\",{\"type\":\"record\",\"name\":\""
                    + MatrixName(builtInType)
                    + "\",\"namespace\":\"" + AvroNamespace + "\",\"fields\":["
                    + "{\"name\":\"dimensions\",\"type\":{\"type\":\"array\",\"items\":\"int\"}},"
                    + "{\"name\":\"values\",\"type\":{\"type\":\"array\",\"items\":" + element + "}}]}]";
            }
            if (valueRank == 1)
            {
                // §5.4 array: nullable array of possibly-null elements.
                return "[\"null\",{\"type\":\"array\",\"items\":" + element + "}]";
            }
            return element;
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
