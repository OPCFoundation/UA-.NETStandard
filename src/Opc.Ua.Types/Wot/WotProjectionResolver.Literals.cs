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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Opc.Ua.Wot
{
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
            public override PreservedLiteral Read(
                ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                return new PreservedLiteral(document.RootElement.Clone());
            }

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
