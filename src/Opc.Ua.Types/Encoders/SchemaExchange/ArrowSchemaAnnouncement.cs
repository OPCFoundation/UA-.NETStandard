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
#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Carries an Arrow SchemaId and serialized Arrow schema bytes.
    /// </summary>
    /// <param name = "SchemaId">The raw 8-byte SHA-256-prefix schema identifier.</param>
    /// <param name = "Schema">The serialized Arrow schema bytes.</param>
    /// <param name = "SchemaEpoch">The optional operational schema epoch.</param>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_1")]
    public sealed record ArrowSchemaAnnouncement(ByteString SchemaId, ByteString Schema, long? SchemaEpoch)
    {
        private static readonly MemoryAllocator s_allocator = MemoryAllocator.Default.Value;

        /// <summary>
        /// Encodes the announcement as a one-row Arrow IPC stream matching the descriptor.
        /// </summary>
        /// <returns>The encoded Arrow IPC stream.</returns>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Arrow arrays are owned by the RecordBatch until it is written."
        )]
        public byte[] Encode()
        {
            Apache.Arrow.Schema schema = new Apache.Arrow.Schema.Builder()
                .Metadata("opcua.mapping", "arrow-schema-announcement")
                .Field(new Field("SchemaId", new FixedSizeBinaryType(8), nullable: false, metadata: null))
                .Field(new Field("Schema", BinaryType.Default, nullable: false, metadata: null))
                .Field(new Field("SchemaEpoch", Int64Type.Default, nullable: true, metadata: null))
                .Build();
            IArrowArray[] arrays = [BuildFixed8([SchemaId]), BuildBinary([Schema]), BuildInt64([SchemaEpoch])];
            using RecordBatch batch = new(schema, arrays, 1);
            return WriteBatch(schema, batch);
        }

        /// <summary>
        /// Decodes an announcement from a one-row Arrow IPC stream.
        /// </summary>
        /// <param name = "payload">The encoded Arrow IPC stream.</param>
        /// <returns>The decoded announcement.</returns>
        public static ArrowSchemaAnnouncement Decode(ReadOnlyMemory<byte> payload)
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);
            using ArrowStreamReader reader = new(stream, leaveOpen: true);
            using RecordBatch? batch = reader.ReadNextRecordBatch();
            if (batch is null || batch.Length != 1)
            {
                throw new FormatException("ArrowSchemaAnnouncement requires one row.");
            }

            ByteString schemaId = ReadFixed8((FixedSizeBinaryArray)batch.Column(0), 0);
            ByteString schema = ByteString.From(((BinaryArray)batch.Column(1)).GetBytes(0).ToArray());
            var epochs = (Int64Array)batch.Column(2);
            long? epoch = epochs.IsNull(0) ? null : epochs.GetValue(0);
            return new ArrowSchemaAnnouncement(schemaId, schema, epoch);
        }

        /// <summary>
        /// Computes the SchemaId bytes for serialized Arrow schema bytes.
        /// </summary>
        /// <param name = "schema">The serialized Arrow schema bytes.</param>
        /// <returns>The raw 8-byte SHA-256-prefix SchemaId.</returns>
        public static ByteString ComputeSchemaId(ByteString schema)
        {
            if (schema.IsNull)
            {
                throw new ArgumentException("Schema bytes are required.", nameof(schema));
            }

            return ByteString.From(global::Opc.Ua.SchemaId.Sha256Id(schema.Span, 8));
        }

        internal static byte[] WriteBatch(Apache.Arrow.Schema schema, RecordBatch batch)
        {
            using MemoryStream stream = new();
            using (ArrowStreamWriter writer = new(stream, schema, leaveOpen: true))
            {
                writer.WriteStart();
                writer.WriteRecordBatch(batch);
                writer.WriteEnd();
            }

            return stream.ToArray();
        }

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "ArrayData ownership is transferred to the Arrow array."
        )]
        internal static FixedSizeBinaryArray BuildFixed8(IReadOnlyList<ByteString> values)
        {
            var bytes = new List<byte>(values.Count * 8);
            var validity = new ArrowBuffer.BitmapBuilder(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                bool valid = !values[i].IsNull;
                if (valid && values[i].Length != 8)
                {
                    throw new ArgumentException("SchemaId values must be 8 bytes.", nameof(values));
                }

                validity.Append(valid);
                bytes.AddRange(valid ? values[i].Span.ToArray() : new byte[8]);
            }

            return new FixedSizeBinaryArray(
                new ArrayData(
                    new FixedSizeBinaryType(8),
                    values.Count,
                    values.Count - validity.SetBitCount,
                    0,
                    [validity.Build(s_allocator), BuildBuffer(bytes.ToArray())],
                    []
                )
            );
        }

        internal static BinaryArray BuildBinary(IReadOnlyList<ByteString> values)
        {
            var builder = new BinaryArray.Builder();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(values[i].Span);
                }
            }

            return builder.Build(s_allocator);
        }

        internal static Int64Array BuildInt64(IReadOnlyList<long?> values)
        {
            var builder = new Int64Array.Builder();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].HasValue)
                {
                    builder.Append(values[i].GetValueOrDefault());
                }
                else
                {
                    builder.AppendNull();
                }
            }

            return builder.Build(s_allocator);
        }

        internal static ByteString ReadFixed8(FixedSizeBinaryArray array, int index)
        {
            if (array.IsNull(index))
            {
                return default;
            }

            return ByteString.From(array.GetBytes(index).ToArray());
        }

        private static ArrowBuffer BuildBuffer<T>(params T[] values)
            where T : struct
        {
            var builder = new ArrowBuffer.Builder<T>(values.Length);
            builder.Append(values.AsSpan());
            return builder.Build(s_allocator);
        }
    }
}
#endif
