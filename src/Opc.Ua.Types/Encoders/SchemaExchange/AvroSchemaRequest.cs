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
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// Requests one or more Avro schemas by their raw SchemaId values.
    /// </summary>
    /// <param name = "RequesterId">The optional receiver or session identifier.</param>
    /// <param name = "SchemaIds">The raw 8-byte SchemaId values requested by the receiver.</param>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Avro")]
    public sealed record AvroSchemaRequest(string? RequesterId, IReadOnlyList<ByteString> SchemaIds)
    {
        /// <summary>
        /// Encodes the request using the published Avro field order.
        /// </summary>
        /// <returns>The encoded Avro binary payload.</returns>
        public byte[] Encode()
        {
            using MemoryStream stream = new();
            Encode(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Encodes the request using the published Avro field order.
        /// </summary>
        /// <param name = "stream">The destination stream.</param>
        public void Encode(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            AvroBinaryWriter writer = new(stream);
            try
            {
                writer.WriteLong(RequesterId is null ? 0 : 1);
                if (RequesterId is not null)
                {
                    writer.WriteString(RequesterId);
                }

                writer.WriteLong(SchemaIds.Count);
                for (int i = 0; i < SchemaIds.Count; i++)
                {
                    if (SchemaIds[i].IsNull)
                    {
                        throw new InvalidOperationException("SchemaIds cannot contain null values.");
                    }

                    writer.WriteBytes(SchemaIds[i].Span);
                }

                writer.WriteLong(0);
                writer.Flush();
            }
            finally
            {
                writer.Release();
            }
        }

        /// <summary>
        /// Decodes a request from its Avro binary payload.
        /// </summary>
        /// <param name = "payload">The encoded Avro binary payload.</param>
        /// <returns>The decoded request.</returns>
        public static AvroSchemaRequest Decode(ReadOnlyMemory<byte> payload)
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);
            return Decode(stream);
        }

        /// <summary>
        /// Decodes a request from its Avro binary payload.
        /// </summary>
        /// <param name = "stream">The source stream.</param>
        /// <returns>The decoded request.</returns>
        public static AvroSchemaRequest Decode(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            AvroBinaryReader reader = new(stream);
            try
            {
                long requesterBranch = reader.ReadLong();
                string? requesterId = requesterBranch switch
                {
                    0 => null,
                    1 => reader.ReadString(),
                    _ => throw new FormatException("Invalid Avro RequesterId union branch."),
                };
                var schemaIds = new List<ByteString>();
                while (true)
                {
                    long count = reader.ReadLong();
                    if (count == 0)
                    {
                        break;
                    }

                    if (count < 0)
                    {
                        _ = reader.ReadLong();
                        count = -count;
                    }

                    for (long i = 0; i < count; i++)
                    {
                        schemaIds.Add(ByteString.From(reader.ReadBytes()));
                    }
                }

                return new AvroSchemaRequest(requesterId, schemaIds);
            }
            finally
            {
                reader.Release();
            }
        }
    }
}
