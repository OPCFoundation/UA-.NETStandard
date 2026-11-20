/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Resolves a projection document (OPC 10101-2) into a resolved
    /// view: an ordinary Thing Description or Thing Model that carries no
    /// <c>uav:projection</c> marker, so a consumer needs no projection support
    /// to use it.
    /// </summary>
    public sealed partial class WotProjectionResolver
    {
        private static bool IsLiteralMember(string name)
        {
            return (name != "@context" && WotDocument.IsSemanticBoundary(name)) ||
                WotNodeSetConverter.IsLiteralSchemaMember(name);
        }

        private static JsonValue? CloneLiteral(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Null ? null :
                JsonValue.Create(new PreservedLiteral(value.Clone()), s_literalTypeInfo);
        }

        private static void PreserveLiteralValues(
            JsonNode? target, WotDocument document, JsonElement original, bool indexMap = false)
        {
            if (target is JsonObject value && original.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty member in original.EnumerateObject())
                {
                    if (!value.TryGetPropertyValue(member.Name, out JsonNode? child))
                    {
                        continue;
                    }
                    if (!indexMap && IsLiteralMember(member.Name))
                    {
                        value[member.Name] = CloneLiteral(member.Value);
                    }
                    else if (indexMap || member.Name != "@context")
                    {
                        PreserveLiteralValues(child, document, member.Value, !indexMap &&
                            (WotNodeSetConverter.IsSchemaDeclarationMap(member.Name) ||
                                document.IsContextIndexMap(member.Name, original)));
                    }
                }
            }
            else if (target is JsonArray array && original.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement item in original.EnumerateArray())
                {
                    PreserveLiteralValues(array[index++], document, item);
                }
            }
        }

        private readonly record struct PreservedLiteral(JsonElement Value);

        private sealed class PreservedLiteralConverter : JsonConverter<PreservedLiteral>
        {
            /// <summary>
            /// Reads a preserved literal from JSON.
            /// </summary>
            /// <param name="reader">The utf 8 JSON reader.</param>
            /// <param name="typeToConvert">The type to convert, always the preserved literal.</param>
            /// <param name="options">The serializer options in effect.</param>
            public override PreservedLiteral Read(
                ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                return new PreservedLiteral(document.RootElement.Clone());
            }

            /// <summary>
            /// Writes a preserved literal as JSON.
            /// </summary>
            /// <param name="writer">The utf 8 JSON writer.</param>
            /// <param name="value">The preserved literal.</param>
            /// <param name="options">The serializer options in effect.</param>
            public override void Write(
                Utf8JsonWriter writer, PreservedLiteral value, JsonSerializerOptions options)
            {
                writer.WriteRawValue(value.Value.GetRawText(), skipInputValidation: true);
            }
        }

        private static readonly JsonTypeInfo<PreservedLiteral> s_literalTypeInfo =
            JsonMetadataServices.CreateValueInfo<PreservedLiteral>(
                new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine() },
                new PreservedLiteralConverter());
    }
}
