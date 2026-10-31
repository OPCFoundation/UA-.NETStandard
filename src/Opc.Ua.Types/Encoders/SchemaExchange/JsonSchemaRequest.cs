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
using System.IO;
using System.Text.Json;

namespace Opc.Ua
{
    /// <summary>
    /// Requests one or more JSON schemas by their raw SchemaId values.
    /// </summary>
    /// <param name = "RequesterId">The optional receiver or session identifier.</param>
    /// <param name = "SchemaIds">The raw 8-byte SchemaId values requested by the receiver.</param>
    public sealed record JsonSchemaRequest(string? RequesterId, IReadOnlyList<ByteString> SchemaIds)
    {
        /// <summary>
        /// Encodes the request as a JSON envelope.
        /// </summary>
        /// <returns>The encoded JSON payload.</returns>
        public byte[] Encode()
        {
            using MemoryStream stream = new();
            Encode(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Encodes the request as a JSON envelope.
        /// </summary>
        /// <param name = "stream">The destination stream.</param>
        public void Encode(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (SchemaIds is null)
            {
                throw new InvalidOperationException("SchemaIds is required.");
            }

            using Utf8JsonWriter writer = new(stream);
            writer.WriteStartObject();
            if (RequesterId is null)
            {
                writer.WriteNull("requesterId");
            }
            else
            {
                writer.WriteString("requesterId", RequesterId);
            }
            writer.WriteStartArray("schemaIds");
            for (int i = 0; i < SchemaIds.Count; i++)
            {
                if (SchemaIds[i].IsNull)
                {
                    throw new InvalidOperationException("SchemaIds cannot contain null values.");
                }

                writer.WriteStringValue(Convert.ToBase64String(SchemaIds[i].Span.ToArray()));
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>
        /// Decodes a request from its JSON envelope.
        /// </summary>
        /// <param name = "payload">The encoded JSON payload.</param>
        /// <returns>The decoded request.</returns>
        public static JsonSchemaRequest Decode(ReadOnlyMemory<byte> payload)
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);
            return Decode(stream);
        }

        /// <summary>
        /// Decodes a request from its JSON envelope.
        /// </summary>
        /// <param name = "stream">The source stream.</param>
        /// <returns>The decoded request.</returns>
        public static JsonSchemaRequest Decode(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;
                JsonElement requester = root.GetProperty("requesterId");
                string? requesterId = requester.ValueKind == JsonValueKind.Null
                    ? null
                    : requester.GetString();
                var schemaIds = new List<ByteString>();
                foreach (JsonElement item in root.GetProperty("schemaIds").EnumerateArray())
                {
                    if (schemaIds.Count >= SchemaExchangePayload.MaxSchemaIds)
                    {
                        throw new FormatException("The JsonSchemaRequest exceeds the SchemaId count limit.");
                    }
                    schemaIds.Add(ByteString.From(Convert.FromBase64String(item.GetString() ?? string.Empty)));
                }

                return new JsonSchemaRequest(requesterId, schemaIds);
            }
            catch (Exception ex) when (SchemaExchangePayload.IsMalformedPayload(ex))
            {
                throw new FormatException("The JsonSchemaRequest payload is malformed.", ex);
            }
        }
    }
}
