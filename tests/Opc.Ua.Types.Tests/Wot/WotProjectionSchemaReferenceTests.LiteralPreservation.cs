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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [TestCase("uav:metadata")]
        [TestCase("const")]
        [TestCase("default")]
        [TestCase("enum")]
        [TestCase("examples")]
        public async Task DataTypeLiteralDuplicateMembersSurviveProjection(string member)
        {
            const string literal = "{\"value\":1,\"value\":2}";
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            definition[member] = member is "enum" or "examples"
                ? new JsonArray("literal-placeholder")
                : JsonValue.Create("literal-placeholder");
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            string sourceJson = source.ToJsonString().Replace(
                "\"literal-placeholder\"", literal, StringComparison.Ordinal);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan().ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.GetArrayLength(), Is.EqualTo(1));
            Assert.That(definitions[0].GetProperty("@id").GetString(), Is.EqualTo("urn:dtd:Reading"));
            JsonElement value = definitions[0].GetProperty(member);
            if (member is "enum" or "examples")
            {
                Assert.That(value.GetArrayLength(), Is.EqualTo(1));
                value = value[0];
            }
            Assert.That(value.GetRawText(), Is.EqualTo(literal));
            Assert.That(value.EnumerateObject().Select(item => item.Value.GetInt32()),
                Is.EqualTo(s_duplicateLiteralValues));
            Assert.That(view.Properties["reading"].GetProperty("uav:dataTypeDefinition")
                .GetProperty("@id").GetString(), Is.EqualTo("urn:dtd:Reading"));

            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            UADataType type = native.Value.Items.OfType<UADataType>().Single();
            Assert.That(type.BrowseName, Does.EndWith(":Reading"));
            Assert.That(type.Definition.Field.Single().DataType, Is.EqualTo("i=11"));
        }

        [Test]
        public async Task UndeclaredConfigurationMemberRetainsSemanticUniqueness()
        {
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            definition["uav:configuration"] = "configuration-placeholder";
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            string sourceJson = source.ToJsonString().Replace(
                "\"configuration-placeholder\"", "{\"value\":1,\"value\":2}", StringComparison.Ordinal);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan().ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True, string.Join("; ", result.Diagnostics));
        }

        [Test]
        public async Task OpaqueProjectionValuesRetainReceivedRepresentation([Values] bool onDefinition)
        {
            const string literal =
                """
                {"https://vendor.test/settings": { "caption":"\u0041", "number":1.2300e+02, "reference":"../same" }}
                """;
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            JsonObject owner = onDefinition ? definition : source["properties"]!["Value"]!.AsObject();
            owner["uav:metadata"] = "literal-placeholder";
            string sourceJson = source.ToJsonString().Replace(
                "\"literal-placeholder\"", literal, StringComparison.Ordinal);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan().ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement actualOwner = onDefinition
                ? view.RootElement.GetProperty("uav:dataTypeDefinitions")[0]
                : view.Properties["reading"];
            Assert.That(actualOwner.GetProperty("uav:metadata").GetRawText(), Is.EqualTo(literal));
        }

        [Test]
        public async Task DeclarationMapNamesDoNotBecomeLiteralBoundaries([Values] bool repeated)
        {
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            definition["properties"] = new JsonObject { ["const"] = "declaration-placeholder" };
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            string sourceJson = source.ToJsonString().Replace("\"declaration-placeholder\"",
                repeated ? "{\"type\":\"number\",\"type\":\"string\"}" : "{\"type\":\"number\"}",
                StringComparison.Ordinal);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan().ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(!repeated), string.Join("; ", result.Diagnostics));
            if (repeated)
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                    Is.True);
            }
            else
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions")[0]
                    .GetProperty("properties").GetProperty("const").GetProperty("type").GetString(),
                    Is.EqualTo("number"));
            }
        }

        [Test]
        public async Task OpaqueDuplicateMembersParticipateInDataTypeReuseWithoutCollapsing([Values] bool same)
        {
            JsonObject source = DataTypeSource();
            JsonObject sourceType = ReadingDefinition();
            sourceType["uav:metadata"] = "source-literal";
            source["uav:dataTypeDefinitions"] = new JsonArray(sourceType);
            JsonObject plan = DataTypePlan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["t"] = "urn:test:projection-types" });
            JsonObject hostType = ReadingDefinition();
            hostType["uav:metadata"] = "host-literal";
            plan["uav:dataTypeDefinitions"] = new JsonArray(hostType);
            string sourceJson = source.ToJsonString().Replace(
                "\"source-literal\"", "{\"value\":1,\"value\":2}", StringComparison.Ordinal);
            string planJson = plan.ToJsonString().Replace("\"host-literal\"",
                same ? "{\"value\":1,\"value\":2}" : "{\"value\":1,\"value\":3}", StringComparison.Ordinal);

            WotConversionResult<WotDocument> result = await ResolveAsync(planJson, sourceJson).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(same), string.Join("; ", result.Diagnostics));
            if (!same)
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                    Is.True);
                return;
            }
            JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.GetArrayLength(), Is.EqualTo(1));
            Assert.That(definitions[0].GetProperty("uav:metadata").GetRawText(),
                Is.EqualTo("{\"value\":1,\"value\":2}"));
        }

        [Test]
        public async Task ProjectedOpaqueDataTypeValueSurvivesNativeRoundTrip([Values] bool repeated)
        {
            string literal = repeated
                ? "{\"value\":1,\"value\":2}"
                : """{"https://vendor.test/value": { "text":"\u0041", "number":1.2300e+02 }}""";
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            definition["uav:metadata"] = "literal-placeholder";
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            string sourceJson = source.ToJsonString().Replace(
                "\"literal-placeholder\"", literal, StringComparison.Ordinal);

            WotConversionResult<WotDocument> projected = await ResolveAsync(DataTypePlan().ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            WotConversionResult<WotDocument> restored = WotNodeSetConverter.FromNodeSetResult(native.Value);
            using WotDocument document = restored.Value;

            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
            JsonElement definitions = document.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.GetArrayLength(), Is.EqualTo(1));
            Assert.That(definitions[0].GetProperty("uav:metadata").GetRawText(), Is.EqualTo(literal));
        }

        [TestCase("properties", "uav:propertyConfiguration")]
        [TestCase("actions", "uav:actionConfiguration")]
        [TestCase("events", "uav:eventConfiguration")]
        public async Task DeclaredAffordanceConfigurationsRemainVerbatim(string collection, string member)
        {
            const string literal = """{"https://vendor.test/settings": { "text":"\u0041", "number":1.00 }}""";
            JsonObject source = Source();
            JsonObject definition = source["properties"]!["Value"]!.DeepClone().AsObject();
            definition[member] = "literal-placeholder";
            source.Remove("properties");
            source[collection] = new JsonObject { ["Value"] = definition };
            JsonObject plan = Plan();
            plan.Remove("properties");
            plan[collection] = new JsonObject
            {
                ["reading"] = new JsonObject { ["tm:ref"] = SourceHref + "#/" + collection + "/Value" }
            };
            string sourceJson = source.ToJsonString().Replace(
                "\"literal-placeholder\"", literal, StringComparison.Ordinal);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan.ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement actual = view.RootElement.GetProperty(collection).GetProperty("reading");
            Assert.That(actual.GetProperty(member).GetRawText(), Is.EqualTo(literal));
            Assert.That(actual.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/" + collection + "/Value"));
        }

        [Test]
        public async Task DataTypeLiteralResidueTargetsGeneratedNativeDefinition([Values] bool reverseDefinitions)
        {
            JsonObject source = DataTypeSource();
            JsonObject first = ReadingDefinition();
            JsonObject second = ReadingDefinition("Other", 3001);
            first["uav:metadata"] = "first-literal";
            second["uav:metadata"] = "second-literal";
            source["uav:dataTypeDefinitions"] = reverseDefinitions
                ? new JsonArray(second, first)
                : new JsonArray(first, second);
            JsonNode other = source["properties"]!["Value"]!.DeepClone();
            other["uav:dataTypeDefinition"] = new JsonObject { ["@id"] = "urn:dtd:Other" };
            source["properties"]!["Other"] = other;
            JsonObject plan = DataTypePlan();
            plan["properties"]!["another"] = new JsonObject { ["tm:ref"] = SourceHref + "#/properties/Other" };
            const string firstLiteral = """{ "https://vendor.test/first": "\u0041" }""";
            const string secondLiteral = """{"https://vendor.test/second": [1.00,2e0]}""";
            string sourceJson = source.ToJsonString()
                .Replace("\"first-literal\"", firstLiteral, StringComparison.Ordinal)
                .Replace("\"second-literal\"", secondLiteral, StringComparison.Ordinal);

            WotConversionResult<WotDocument> projected = await ResolveAsync(plan.ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            WotConversionResult<WotDocument> restored = WotNodeSetConverter.FromNodeSetResult(native.Value);
            using WotDocument document = restored.Value;

            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
            JsonElement[] definitions =
                [.. document.RootElement.GetProperty("uav:dataTypeDefinitions").EnumerateArray()];
            Assert.That(definitions, Has.Length.EqualTo(2));
            Assert.That(definitions.Single(value =>
                    value.GetProperty("uav:dataTypeId").GetString() == "nsu=urn:test:projection-types;i=3000")
                .GetProperty("uav:metadata").GetRawText(), Is.EqualTo(firstLiteral));
            Assert.That(definitions.Single(value =>
                    value.GetProperty("uav:dataTypeId").GetString() == "nsu=urn:test:projection-types;i=3001")
                .GetProperty("uav:metadata").GetRawText(), Is.EqualTo(secondLiteral));
        }

        private static readonly int[] s_duplicateLiteralValues = [1, 2];
    }
}
