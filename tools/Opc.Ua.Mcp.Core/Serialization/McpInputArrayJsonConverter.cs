// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Opc.Ua.Mcp.Serialization
{
    /// <summary>
    /// Reads bounded arrays of concrete request objects using source-generated element metadata.
    /// </summary>
    /// <typeparam name="T">A concrete, non-null request object.</typeparam>
    public abstract class McpInputArrayJsonConverter<T> : JsonConverter<ArrayOf<T>>
        where T : class
    {
        /// <summary>
        /// Gets the concrete element contract without reflection-based serialization.
        /// </summary>
        protected abstract JsonTypeInfo<T> ElementType { get; }

        /// <inheritdoc/>
        public override ArrayOf<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return ArrayOf<T>.Null;
            }
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected an array of input objects.");
            }
            var values = new List<T>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return new ArrayOf<T>(values.ToArray());
                }
                if (values.Count == 500 || reader.CurrentDepth > 32)
                {
                    throw new JsonException("Inputs are limited to 500 items per array and 32 JSON levels.");
                }
                values.Add(JsonSerializer.Deserialize(ref reader, ElementType)
                    ?? throw new JsonException("Null input-array entries are not permitted."));
            }
            throw new JsonException("The input array is incomplete.");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ArrayOf<T> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value.IsNull)
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStartArray();
            foreach (T item in value)
            {
                JsonSerializer.Serialize(writer, item, ElementType);
            }
            writer.WriteEndArray();
        }
    }
}
