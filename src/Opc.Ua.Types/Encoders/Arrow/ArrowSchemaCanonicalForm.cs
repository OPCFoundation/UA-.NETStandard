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
                    .Append(field.Name)
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

        private static string TypeCode(IArrowType type)
        {
            switch (type.TypeId)
            {
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
                        f => f.Name + ":" + TypeCode(f.DataType) + ":" + (f.IsNullable ? "1" : "0"))) + ">";
                case ArrowTypeId.List:
                    var list = (ListType)type;
                    return "list<" + TypeCode(list.ValueDataType) + ":"
                        + (list.ValueField.IsNullable ? "1" : "0") + ">";
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
                builder.Append(prefix).Append(key).Append('=').Append(metadata[key]);
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
                .Select(k => k + "=" + metadata[k]));
        }
    }
}
#endif
