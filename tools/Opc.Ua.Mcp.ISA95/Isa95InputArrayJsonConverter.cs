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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Converts bounded DTO arrays using only the module's source-generated JSON metadata.
    /// </summary>
    /// <typeparam name="T">A concrete ISA-95 input type registered in the generated context.</typeparam>
    public sealed class Isa95InputArrayJsonConverter<T> : JsonConverter<ArrayOf<T>>
        where T : class
    {
        /// <inheritdoc/>
        public override ArrayOf<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return ArrayOf<T>.Null;
            }
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected an array of ISA-95 input objects.");
            }
            JsonTypeInfo<T> type = ElementType();
            var values = new List<T>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return new ArrayOf<T>(values.ToArray());
                }
                if (values.Count == 500 || reader.CurrentDepth > 32)
                {
                    throw new JsonException("ISA-95 inputs are limited to 500 items per array and 32 JSON levels.");
                }
                values.Add(JsonSerializer.Deserialize(ref reader, type)
                    ?? throw new JsonException("Null ISA-95 array entries are not permitted."));
            }
            throw new JsonException("The ISA-95 input array is incomplete.");
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
            JsonTypeInfo<T> type = ElementType();
            writer.WriteStartArray();
            foreach (T item in value)
            {
                JsonSerializer.Serialize(writer, item, type);
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Resolves a closed, source-generated input contract without reflection.
        /// </summary>
        private static JsonTypeInfo<T> ElementType()
        {
            return Isa95InputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
                ?? throw new NotSupportedException("This type is not an ISA-95 input contract.");
        }
    }

    /// <summary>
    /// Source-generated contracts used by nested input-array converters.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(Isa95TextInput))]
    [JsonSerializable(typeof(Isa95TypedValueInput))]
    [JsonSerializable(typeof(Isa95UnitInput))]
    [JsonSerializable(typeof(Isa95ParameterInput))]
    [JsonSerializable(typeof(Isa95ResourceInput))]
    [JsonSerializable(typeof(Isa95MaterialInput))]
    [JsonSerializable(typeof(Isa95WorkMasterInput))]
    [JsonSerializable(typeof(Isa95StateInput))]
    [JsonSerializable(typeof(Isa95CommentInput))]
    [JsonSerializable(typeof(Isa95JobOrderInput))]
    [JsonSerializable(typeof(Isa95JobResponseInput))]
    [JsonSerializable(typeof(Isa95ResponseQueryInput))]
    internal sealed partial class Isa95InputJsonContext : JsonSerializerContext
    {
    }
}
