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
