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
using System.IO;
using System.Text;
using System.Text.Json;

namespace Opc.Ua
{
    /// <summary>
    /// Carries a JSON SchemaId and its self-contained JSON Schema document.
    /// </summary>
    /// <param name = "SchemaId">The raw 8-byte SHA-256-prefix schema identifier.</param>
    /// <param name = "SchemaJson">The self-contained JSON Schema document.</param>
    /// <param name = "SchemaEpoch">The optional operational schema epoch.</param>
    public sealed record JsonSchemaAnnouncement(ByteString SchemaId, string SchemaJson, long? SchemaEpoch)
    {
        /// <summary>
        /// Encodes the announcement as a JSON envelope.
        /// </summary>
        /// <returns>The encoded JSON payload.</returns>
        public byte[] Encode()
        {
            using MemoryStream stream = new();
            Encode(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Encodes the announcement as a JSON envelope.
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

            using Utf8JsonWriter writer = new(stream);
            writer.WriteStartObject();
            writer.WriteString("schemaId", Convert.ToBase64String(SchemaId.Span.ToArray()));
            writer.WriteString("schemaJson", SchemaJson);
            if (SchemaEpoch.HasValue)
            {
                writer.WriteNumber("schemaEpoch", SchemaEpoch.Value);
            }
            else
            {
                writer.WriteNull("schemaEpoch");
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Decodes an announcement from its JSON envelope.
        /// </summary>
        /// <param name = "payload">The encoded JSON payload.</param>
        /// <returns>The decoded announcement.</returns>
        public static JsonSchemaAnnouncement Decode(ReadOnlyMemory<byte> payload)
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);
            return Decode(stream);
        }

        /// <summary>
        /// Decodes an announcement from its JSON envelope.
        /// </summary>
        /// <param name = "stream">The source stream.</param>
        /// <returns>The decoded announcement.</returns>
        public static JsonSchemaAnnouncement Decode(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;
                ByteString schemaId = ByteString.From(
                    Convert.FromBase64String(root.GetProperty("schemaId").GetString() ?? string.Empty));
                string schemaJson = root.GetProperty("schemaJson").GetString()
                    ?? throw new FormatException("The JSON schema envelope is missing SchemaJson.");
                JsonElement epoch = root.GetProperty("schemaEpoch");
                long? schemaEpoch = epoch.ValueKind == JsonValueKind.Null
                    ? null
                    : epoch.GetInt64();
                return new JsonSchemaAnnouncement(schemaId, schemaJson, schemaEpoch);
            }
            catch (Exception ex) when (SchemaExchangePayload.IsMalformedPayload(ex))
            {
                throw new FormatException("The JsonSchemaAnnouncement payload is malformed.", ex);
            }
        }

        /// <summary>
        /// Computes the SchemaId bytes for a JSON Schema document.
        /// </summary>
        /// <param name = "schemaJson">The JSON Schema document.</param>
        /// <returns>The raw 8-byte SHA-256-prefix SchemaId.</returns>
        public static ByteString ComputeSchemaId(string schemaJson)
        {
            if (schemaJson is null)
            {
                throw new ArgumentNullException(nameof(schemaJson));
            }

            return ByteString.From(global::Opc.Ua.SchemaId.JsonSchemaId(Encoding.UTF8.GetBytes(schemaJson)));
        }
    }
}
