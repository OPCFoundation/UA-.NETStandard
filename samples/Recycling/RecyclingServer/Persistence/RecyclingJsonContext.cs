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

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Persistence/scenario serialization is source generated and is not used as the OPC UA wire protocol.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        GenerationMode = JsonSourceGenerationMode.Metadata,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        [
            typeof(RecyclingArrayConverter<long>),
            typeof(RecyclingArrayConverter<string>),
            typeof(RecyclingArrayConverter<StageRecipe>),
            typeof(RecyclingArrayConverter<ScenarioWindow>),
            typeof(RecyclingArrayConverter<EquipmentFaultWindow>),
            typeof(RecyclingArrayConverter<SourceContribution>),
            typeof(RecyclingArrayConverter<StageSnapshot>),
            typeof(RecyclingArrayConverter<MaterialParcel>),
            typeof(RecyclingArrayConverter<MaterialPackage>),
            typeof(RecyclingArrayConverter<MaterialResidue>),
            typeof(RecyclingArrayConverter<QualityHold>),
            typeof(RecyclingArrayConverter<MaterialExecution>),
            typeof(RecyclingArrayConverter<ProcessParticipant>),
            typeof(RecyclingArrayConverter<RecyclingCommandReceipt>),
            typeof(RecyclingArrayConverter<RecyclingIdentityBinding>),
            typeof(JsonStringEnumConverter<RecyclingAccess>),
            typeof(JsonStringEnumConverter<RecyclingProcess>),
            typeof(JsonStringEnumConverter<RecyclingEquipmentFault>)
        ])]
    [JsonSerializable(typeof(RecyclingCheckpoint))]
    [JsonSerializable(typeof(RecyclingConfiguration))]
    [JsonSerializable(typeof(RecyclingCommand))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(StageRecipe))]
    [JsonSerializable(typeof(ScenarioWindow))]
    [JsonSerializable(typeof(EquipmentFaultWindow))]
    [JsonSerializable(typeof(SyntheticRgb))]
    [JsonSerializable(typeof(SourceContribution))]
    [JsonSerializable(typeof(StageSnapshot))]
    [JsonSerializable(typeof(MaterialParcel))]
    [JsonSerializable(typeof(MaterialPackage))]
    [JsonSerializable(typeof(MaterialResidue))]
    [JsonSerializable(typeof(QualityHold))]
    [JsonSerializable(typeof(MaterialExecution))]
    [JsonSerializable(typeof(ProcessParticipant))]
    [JsonSerializable(typeof(RecyclingCommandReceipt))]
    [JsonSerializable(typeof(RecyclingIdentityBinding))]
    [JsonSerializable(typeof(RecyclingIdentityConfiguration))]
    internal sealed partial class RecyclingJsonContext : JsonSerializerContext;

    /// <summary>
    /// Closed generic converters use only the explicitly source-generated element metadata.
    /// </summary>
    internal sealed class RecyclingArrayConverter<T> : JsonConverter<ArrayOf<T>>
    {
        /// <inheritdoc/>
        public override ArrayOf<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("A non-null typed array is required.");
            }
            var values = new List<T>();
            int maximum = typeof(T) == typeof(RecyclingCommandReceipt)
                ? 100_000 + Enum.GetValues<RecyclingProcess>().Length
                : 100_000;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (values.Count >= maximum)
                {
                    throw new JsonException("The configured maximum serialized collection size was exceeded.");
                }
                T value = JsonSerializer.Deserialize(ref reader, ElementType)
                    ?? throw new JsonException("Array elements must not be null.");
                values.Add(value);
            }
            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("Truncated typed array.");
            }
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ArrayOf<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T item in value)
            {
                JsonSerializer.Serialize(writer, item, ElementType);
            }
            writer.WriteEndArray();
        }

        private static JsonTypeInfo<T> ElementType =>
            (JsonTypeInfo<T>)(RecyclingJsonContext.Default.GetTypeInfo(typeof(T)) ??
                throw new InvalidOperationException("Unregistered persistence element type."));
    }
}
