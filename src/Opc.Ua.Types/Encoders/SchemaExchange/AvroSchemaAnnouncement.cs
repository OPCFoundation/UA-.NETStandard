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
using System.IO;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Carries an Avro SchemaId and its self-contained Avro schema JSON.
    /// </summary>
    /// <param name = "SchemaId">The raw 8-byte CRC-64-AVRO schema identifier.</param>
    /// <param name = "SchemaJson">The self-contained Avro schema JSON document.</param>
    /// <param name = "SchemaEpoch">The optional operational schema epoch.</param>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Encoders")]
    public sealed record AvroSchemaAnnouncement(ByteString SchemaId, string SchemaJson, long? SchemaEpoch)
    {
        /// <summary>
        /// Encodes the announcement using the published Avro field order.
        /// </summary>
        /// <returns>The encoded Avro binary payload.</returns>
        public byte[] Encode()
        {
            using MemoryStream stream = new();
            Encode(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Encodes the announcement using the published Avro field order.
        /// </summary>
        /// <param name = "stream">The destination stream.</param>
        public void Encode(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (SchemaId.IsNull)
            {
                throw new InvalidOperationException("SchemaId is required.");
            }

            if (SchemaJson is null)
            {
                throw new InvalidOperationException("SchemaJson is required.");
            }

            AvroBinaryWriter writer = new(stream);
            try
            {
                writer.WriteBytes(SchemaId.Span);
                writer.WriteString(SchemaJson);
                writer.WriteLong(SchemaEpoch.HasValue ? 1 : 0);
                if (SchemaEpoch.HasValue)
                {
                    writer.WriteLong(SchemaEpoch.Value);
                }

                writer.Flush();
            }
            finally
            {
                writer.Release();
            }
        }

        /// <summary>
        /// Decodes an announcement from its Avro binary payload.
        /// </summary>
        /// <param name = "payload">The encoded Avro binary payload.</param>
        /// <returns>The decoded announcement.</returns>
        public static AvroSchemaAnnouncement Decode(ReadOnlyMemory<byte> payload)
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);
            return Decode(stream);
        }

        /// <summary>
        /// Decodes an announcement from its Avro binary payload.
        /// </summary>
        /// <param name = "stream">The source stream.</param>
        /// <returns>The decoded announcement.</returns>
        public static AvroSchemaAnnouncement Decode(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            AvroBinaryReader reader = new(stream);
            try
            {
                ByteString schemaId = ByteString.From(reader.ReadBytes());
                string schemaJson = reader.ReadString();
                long branch = reader.ReadLong();
                long? schemaEpoch = branch switch
                {
                    0 => null,
                    1 => reader.ReadLong(),
                    _ => throw new FormatException("Invalid Avro SchemaEpoch union branch."),
                };
                return new AvroSchemaAnnouncement(schemaId, schemaJson, schemaEpoch);
            }
            finally
            {
                reader.Release();
            }
        }

        /// <summary>
        /// Computes the SchemaId bytes for an Avro schema JSON document.
        /// </summary>
        /// <param name = "schemaJson">The canonical or self-contained schema JSON.</param>
        /// <returns>The raw 8-byte CRC-64-AVRO SchemaId.</returns>
        public static ByteString ComputeSchemaId(string schemaJson)
        {
            if (schemaJson is null)
            {
                throw new ArgumentNullException(nameof(schemaJson));
            }

            ulong fingerprint = global::Opc.Ua.SchemaId.RabinCrc64Avro(Encoding.UTF8.GetBytes(schemaJson));
            return ByteString.From(global::Opc.Ua.SchemaId.AvroSingleObjectPrefix(fingerprint).AsSpan(2, 8));
        }
    }
}
