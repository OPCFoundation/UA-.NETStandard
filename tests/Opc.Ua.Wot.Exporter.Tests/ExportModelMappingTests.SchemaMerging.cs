// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter.Tests
{
    public sealed partial class ExportModelMappingTests
    {
        /// <summary>
        /// Intersects reordered alternatives without weakening authored constraints or native integer storage bounds.
        /// </summary>
        [TestCase("1", false)]
        [TestCase("100", true)]
        [TestCase("65535", true)]
        [TestCase("65536", false)]
        [TestCase("[]", false)]
        [TestCase("[1,2]", true)]
        [TestCase("[1,2,3]", false)]
        [TestCase("[65536]", false)]
        public void ReorderedAlternativesRetainAuthoredAndNativeConstraints(string payload, bool accepted)
        {
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"properties":{"value":{"uav:mapToType":"i=5","uav:valueRank":-3,
                  "allOf":[{"minItems":1}],"oneOf":[
                    {"type":"array","maxItems":2},{"type":"integer","minimum":100}]}}}
                """);
            using JsonDocument value = JsonDocument.Parse(payload);
            Assert.That(MatchesSchema(document.RootElement.GetProperty("properties").GetProperty("value"),
                value.RootElement), Is.EqualTo(accepted), payload);
        }

        /// <summary>
        /// Allows an authored alternative set to select only part of the native scalar-or-array domain.
        /// </summary>
        [TestCase("[1]", true)]
        [TestCase("100", false)]
        [TestCase("[65536]", false)]
        public void AuthoredAlternativesNeedNotHaveTheNativeBranchCount(string payload, bool accepted)
        {
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"properties":{"value":{"uav:mapToType":"i=5","uav:valueRank":-3,
                  "oneOf":[{"type":"array","maxItems":2}]}}}
                """);
            using JsonDocument value = JsonDocument.Parse(payload);
            Assert.That(MatchesSchema(document.RootElement.GetProperty("properties").GetProperty("value"),
                value.RootElement), Is.EqualTo(accepted), payload);
        }

        /// <summary>
        /// Retains Boolean item schemas that forbid every element while still allowing empty arrays.
        /// </summary>
        [TestCase(1, "[]", true)]
        [TestCase(1, "[1]", false)]
        [TestCase(2, "[[]]", true)]
        [TestCase(2, "[[1]]", false)]
        public void FalseItemSchemasContinueToForbidArrayElements(int rank, string payload, bool accepted)
        {
            string items = rank == 1 ? "false" : """{"items":false}""";
            string rankText = rank.ToString(CultureInfo.InvariantCulture);
            string json = $$$$"""
                {"properties":{"value":{"uav:mapToType":"i=5","uav:valueRank":{{{{rankText}}}},
                  "items":{{{{items}}}}}}}
                """;
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, json);
            using JsonDocument value = JsonDocument.Parse(payload);
            Assert.That(MatchesSchema(document.RootElement.GetProperty("properties").GetProperty("value"),
                value.RootElement), Is.EqualTo(accepted), payload);
        }

        /// <summary>
        /// Treats native field names as map keys rather than schema keywords and preserves forbidden optional fields.
        /// </summary>
        [TestCase(/*lang=json,strict*/ """{"maxItems":100,"minimum":100}""", true)]
        [TestCase(/*lang=json,strict*/ """{"maxItems":99,"minimum":100}""", false)]
        [TestCase(/*lang=json,strict*/ """{"maxItems":100,"minimum":"text"}""", false)]
        [TestCase(/*lang=json,strict*/ """{"maxItems":100,"minimum":100,"blocked":true}""", false)]
        [TestCase(/*lang=json,strict*/ """{"maxItems":100}""", false)]
        public async Task PropertyMapsKeepKeywordNamedAndForbiddenFieldsAsync(string payload, bool accepted)
        {
            UANodeSet source = Source("""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:Settings">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=22</Reference></References>
                  <Definition Name="1:Settings">
                    <Field Name="maxItems" DataType="i=5" />
                    <Field Name="minimum" DataType="i=5" />
                    <Field Name="blocked" DataType="i=1" IsOptional="true" />
                  </Definition>
                </UADataType>
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            using WotDocumentSet documents = result.Value!;
            mapping.AddDefinitions(documents);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"properties":{"value":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001",
                  "properties":{"maxItems":{"minimum":100},"minimum":{"minimum":100},"blocked":false}}}}
                """);
            using JsonDocument value = JsonDocument.Parse(payload);
            Assert.That(MatchesSchema(document.RootElement.GetProperty("properties").GetProperty("value"),
                value.RootElement), Is.EqualTo(accepted), payload);
        }

        /// <summary>
        /// Evaluates the JSON Schema assertion subset exercised by the merge regression payloads.
        /// </summary>
        private static bool MatchesSchema(JsonElement schema, JsonElement value)
        {
            if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return schema.GetBoolean();
            }
            if (schema.TryGetProperty("allOf", out JsonElement allOf) &&
                !allOf.EnumerateArray().All(branch => MatchesSchema(branch, value)))
            {
                return false;
            }
            if (schema.TryGetProperty("oneOf", out JsonElement oneOf) &&
                oneOf.EnumerateArray().Count(branch => MatchesSchema(branch, value)) != 1)
            {
                return false;
            }
            if (schema.TryGetProperty("type", out JsonElement type) && !(type.GetString() switch
            {
                "integer" => value.ValueKind == JsonValueKind.Number &&
                    value.TryGetDecimal(out decimal number) && decimal.Truncate(number) == number,
                "array" => value.ValueKind == JsonValueKind.Array,
                "object" => value.ValueKind == JsonValueKind.Object,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => throw new InvalidOperationException("The regression payload uses an unsupported schema type.")
            }))
            {
                return false;
            }
            if (value.ValueKind == JsonValueKind.Number &&
                ((schema.TryGetProperty("minimum", out JsonElement minimum) &&
                    value.GetDecimal() < minimum.GetDecimal()) ||
                (schema.TryGetProperty("maximum", out JsonElement maximum) &&
                    value.GetDecimal() > maximum.GetDecimal())))
            {
                return false;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                if ((schema.TryGetProperty("minItems", out JsonElement minimumItems) &&
                    value.GetArrayLength() < minimumItems.GetInt32()) ||
                    (schema.TryGetProperty("maxItems", out JsonElement maximumItems) &&
                    value.GetArrayLength() > maximumItems.GetInt32()))
                {
                    return false;
                }
                if (schema.TryGetProperty("items", out JsonElement items) &&
                    !value.EnumerateArray().All(item => MatchesSchema(items, item)))
                {
                    return false;
                }
            }
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (schema.TryGetProperty("required", out JsonElement required) &&
                    !required.EnumerateArray().All(name => value.TryGetProperty(name.GetString()!, out _)))
                {
                    return false;
                }
                bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties);
                bool closed = schema.TryGetProperty("additionalProperties", out JsonElement additional) &&
                    additional.ValueKind == JsonValueKind.False;
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (hasProperties && properties.TryGetProperty(property.Name, out JsonElement propertySchema))
                    {
                        if (!MatchesSchema(propertySchema, property.Value))
                        {
                            return false;
                        }
                    }
                    else if (closed)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
