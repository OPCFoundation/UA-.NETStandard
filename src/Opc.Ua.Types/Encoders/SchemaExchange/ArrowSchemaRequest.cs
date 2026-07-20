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
using Apache.Arrow.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Requests one or more Arrow schemas by their raw SchemaId values.
    /// </summary>
    /// <param name = "RequesterId">The optional receiver or session identifier.</param>
    /// <param name = "SchemaIds">The raw 8-byte SchemaId values requested by the receiver.</param>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Arrow")]
    public sealed record ArrowSchemaRequest(string? RequesterId, IReadOnlyList<ByteString> SchemaIds)
    {
        /// <summary>
        /// Encodes the request as a one-row Arrow IPC stream matching the descriptor.
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
                .Metadata("opcua.mapping", "arrow-schema-request")
                .Field(new Field("RequesterId", StringType.Default, nullable: true, metadata: null))
                .Field(
                    new Field(
                        "SchemaIds",
                        new ListType(new Field("item", new FixedSizeBinaryType(8), nullable: false, metadata: null)),
                        nullable: false,
                        metadata: null
                    )
                )
                .Build();
            using RecordBatch batch = new(schema, [BuildRequester(), BuildSchemaIds()], 1);
            return ArrowSchemaAnnouncement.WriteBatch(schema, batch);
        }

        /// <summary>
        /// Decodes a request from a one-row Arrow IPC stream.
        /// </summary>
        /// <param name = "payload">The encoded Arrow IPC stream.</param>
        /// <returns>The decoded request.</returns>
        public static ArrowSchemaRequest Decode(ReadOnlyMemory<byte> payload)
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);
            using ArrowStreamReader reader = new(stream, leaveOpen: true);
            using RecordBatch? batch = reader.ReadNextRecordBatch();
            if (batch is null || batch.Length != 1)
            {
                throw new FormatException("ArrowSchemaRequest requires one row.");
            }

            var requesterIds = (StringArray)batch.Column(0);
            string? requesterId = requesterIds.IsNull(0) ? null : requesterIds.GetString(0);
            var schemaIdsList = (ListArray)batch.Column(1);
            int start = schemaIdsList.ValueOffsets[0];
            int length = schemaIdsList.ValueOffsets[1] - start;
            var values = (FixedSizeBinaryArray)schemaIdsList.Values;
            var schemaIds = new List<ByteString>(length);
            for (int i = 0; i < length; i++)
            {
                schemaIds.Add(ArrowSchemaAnnouncement.ReadFixed8(values, start + i));
            }

            return new ArrowSchemaRequest(requesterId, schemaIds);
        }

        private StringArray BuildRequester()
        {
            var builder = new StringArray.Builder();
            if (RequesterId is null)
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(RequesterId);
            }

            return builder.Build(Apache.Arrow.Memory.MemoryAllocator.Default.Value);
        }

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The child array is owned by the ListArray."
        )]
        private ListArray BuildSchemaIds()
        {
            FixedSizeBinaryArray values = ArrowSchemaAnnouncement.BuildFixed8(SchemaIds);
            var offsets = new ArrowBuffer.Builder<int>(2);
            offsets.Append(0);
            offsets.Append(SchemaIds.Count);
            var validity = new ArrowBuffer.BitmapBuilder(1);
            validity.Append(true);
            var listType = new ListType(new Field("item", new FixedSizeBinaryType(8), nullable: false, metadata: null));
            return new ListArray(
                listType,
                1,
                offsets.Build(Apache.Arrow.Memory.MemoryAllocator.Default.Value),
                values,
                validity.Build(Apache.Arrow.Memory.MemoryAllocator.Default.Value),
                0,
                0
            );
        }
    }
}
#endif
