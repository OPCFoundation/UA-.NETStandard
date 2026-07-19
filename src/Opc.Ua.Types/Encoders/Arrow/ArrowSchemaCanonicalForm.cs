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
#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using ArrowSchema = Apache.Arrow.Schema;

namespace Opc.Ua
{
    /// <summary>
    /// Computes an implementation-independent canonical string for an Apache Arrow
    /// <see cref="Schema"/> and the Arrow <c>SchemaId</c> (SHA-256[:8]) over it.
    /// <para>
    /// The Part 6 Arrow encoding originally defined the canonical form as the serialized Arrow
    /// Schema IPC bytes (<c>schema.serialize()</c>). That is deterministic <b>within</b> one Arrow
    /// implementation but is <b>not byte-identical across</b> Arrow implementations or versions —
    /// verified: pyarrow 24.0.0 serializes a one-field <c>value:int32</c> schema to 192 bytes
    /// while Apache.Arrow 18.1.0 serializes the same schema to 184 different bytes (FlatBuffers
    /// layout is not canonical). A SchemaId over the raw IPC bytes therefore differs between a
    /// pyarrow-based registry and a .NET one, breaking the Schema Registry's cross-registry
    /// de-duplication by SchemaId (Schema Registry §4.3, Annex B). This canonical form instead
    /// serializes the schema's logical content — ordered fields (name, portable type code,
    /// nullability, sorted field metadata) and sorted schema metadata — so the SchemaId is
    /// stable across implementations for the same logical schema.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Encoders")]
    public static class ArrowSchemaCanonicalForm
    {
        /// <summary>
        /// Computes the implementation-independent canonical string of an Arrow schema.
        /// </summary>
        /// <param name="schema">The Arrow schema.</param>
        /// <returns>The canonical string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="schema"/> is null.</exception>
        /// <exception cref="NotSupportedException">A field uses an unmapped Arrow type.</exception>
        public static string Compute(ArrowSchema schema)
        {
            if (schema is null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var builder = new StringBuilder();
            builder.Append("arrow-schema-v1");
            AppendSortedMetadata(builder, "\nM:", schema.Metadata);

            foreach (Field field in schema.FieldsList)
            {
                builder.Append("\nF:")
                    .Append(Quote(field.Name))
                    .Append(':')
                    .Append(TypeCode(field.DataType))
                    .Append(':')
                    .Append(field.IsNullable ? '1' : '0')
                    .Append(':')
                    .Append(InlineMetadata(field.Metadata));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Computes the leading bytes of the SHA-256 fingerprint of the canonical form.
        /// </summary>
        /// <param name="schema">The Arrow schema.</param>
        /// <param name="nbytes">The number of leading SHA-256 digest bytes to return.</param>
        /// <returns>The raw SchemaId bytes.</returns>
        public static byte[] ComputeSchemaId(ArrowSchema schema, int nbytes = 8)
        {
            if (nbytes < 0 || nbytes > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(nbytes));
            }

            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Compute(schema)));
            byte[] id = new byte[nbytes];
            System.Array.Copy(digest, id, nbytes);
            return id;
        }

        /// <summary>
        /// Reads an Arrow schema from a serialized Arrow IPC stream and computes the portable
        /// canonical <c>SchemaId</c> (SHA-256[:nbytes] over the canonical form). This is
        /// the wire-bytes entry point: it deliberately re-derives the SchemaId from the schema's
        /// logical content (see <see cref="Compute(Apache.Arrow.Schema)"/>) rather than hashing the
        /// raw IPC bytes, so the same logical schema yields the same SchemaId regardless of which
        /// Arrow implementation or version produced the IPC.
        /// </summary>
        /// <param name="ipcSchema">The serialized Arrow IPC bytes carrying the schema message.</param>
        /// <param name="nbytes">The number of leading SHA-256 digest bytes to return.</param>
        /// <returns>The portable raw SchemaId bytes.</returns>
        /// <exception cref="FormatException">The bytes are not a readable Arrow IPC schema.</exception>
        public static byte[] ComputeSchemaIdFromIpc(ReadOnlySpan<byte> ipcSchema, int nbytes = 8)
        {
            using var stream = new System.IO.MemoryStream(ipcSchema.ToArray(), writable: false);
            using var reader = new Apache.Arrow.Ipc.ArrowStreamReader(stream, leaveOpen: true);

            // Reading the first batch (or reaching end-of-stream on a schema-only stream) forces the
            // reader to consume the leading schema message and populate reader.Schema.
            using RecordBatch? batch = reader.ReadNextRecordBatch();
            ArrowSchema schema = batch?.Schema ?? reader.Schema
                ?? throw new FormatException("The Arrow IPC stream does not carry a schema.");
            return ComputeSchemaId(schema, nbytes);
        }

        private static string TypeCode(IArrowType type)
        {
            switch (type.TypeId)
            {
                case ArrowTypeId.Null: return "null";
                case ArrowTypeId.Boolean: return "bool";
                case ArrowTypeId.Int8: return "i8";
                case ArrowTypeId.Int16: return "i16";
                case ArrowTypeId.Int32: return "i32";
                case ArrowTypeId.Int64: return "i64";
                case ArrowTypeId.UInt8: return "u8";
                case ArrowTypeId.UInt16: return "u16";
                case ArrowTypeId.UInt32: return "u32";
                case ArrowTypeId.UInt64: return "u64";
                case ArrowTypeId.Float: return "f32";
                case ArrowTypeId.Double: return "f64";
                case ArrowTypeId.String: return "str";
                case ArrowTypeId.Binary: return "bin";
                case ArrowTypeId.FixedSizedBinary:
                    return "fsb" + ((FixedSizeBinaryType)type).ByteWidth
                        .ToString(CultureInfo.InvariantCulture);
                case ArrowTypeId.Struct:
                    return "struct<" + string.Join(",", ((StructType)type).Fields.Select(
                        f => Quote(f.Name) + ":" + TypeCode(f.DataType) + ":" + (f.IsNullable ? "1" : "0"))) + ">";
                case ArrowTypeId.List:
                    var list = (ListType)type;
                    return "list<" + TypeCode(list.ValueDataType) + ":"
                        + (list.ValueField.IsNullable ? "1" : "0") + ">";
                case ArrowTypeId.Union:
                    var union = (UnionType)type;
                    string mode = union.Mode == UnionMode.Dense ? "dense" : "sparse";
                    var branches = new List<string>(union.Fields.Count);
                    for (int ii = 0; ii < union.Fields.Count; ii++)
                    {
                        Field child = union.Fields[ii];
                        branches.Add(union.TypeIds[ii].ToString(CultureInfo.InvariantCulture)
                            + "=" + Quote(child.Name) + ":" + TypeCode(child.DataType)
                            + ":" + (child.IsNullable ? "1" : "0"));
                    }
                    return "union<" + mode + ";" + string.Join(",", branches) + ">";
                default:
                    throw new NotSupportedException("Unmapped Arrow type: " + type.TypeId);
            }
        }

        private static void AppendSortedMetadata(
            StringBuilder builder, string prefix, IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata is null)
            {
                return;
            }

            foreach (string key in metadata.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                builder.Append(prefix).Append(Quote(key)).Append('=').Append(Quote(metadata[key]));
            }
        }

        private static string InlineMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata is null || metadata.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(";", metadata.Keys
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => Quote(k) + "=" + Quote(metadata[k])));
        }

        // JSON string escaping so field names and metadata containing separators
        // (':', ',', ';', '<', '>', '=') cannot alias — the canonical form stays injective.
        private static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }
}
#endif
