/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opc.Ua.Mcp.Serialization
{
    /// <summary>
    /// Accepts bounded JSON string arrays without exposing ArrayOf's memory representation.
    /// Tool schemas using this converter must declare an array of strings explicitly.
    /// </summary>
    public sealed class McpStringArrayJsonConverter : JsonConverter<ArrayOf<string>>
    {
        /// <inheritdoc/>
        public override ArrayOf<string> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return ArrayOf<string>.Null;
            }
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected a JSON array of strings.");
            }
            var items = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return items.ToArrayOf();
                }
                if (reader.TokenType != JsonTokenType.String || items.Count == 500)
                {
                    throw new JsonException("Expected at most 500 non-null string items.");
                }
                items.Add(reader.GetString()!);
            }
            throw new JsonException("The string array is incomplete.");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ArrayOf<string> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value.IsNull)
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStartArray();
            foreach (string item in value)
            {
                writer.WriteStringValue(item);
            }
            writer.WriteEndArray();
        }
    }
}
