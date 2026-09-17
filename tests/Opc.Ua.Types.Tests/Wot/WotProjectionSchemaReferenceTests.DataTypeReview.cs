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
        [Test]
        public async Task LiteralDefinitionsCannotBecomeAuthoritativeOrDuplicateSemanticDefinitions(
            [Values("const", "default", "enum", "examples", "uav:metadata")] string member,
            [Values] bool pointerTarget)
        {
            JsonObject source = DataTypeSource();
            JsonObject literal = new()
            {
                ["Value"] = 1,
                ["uav:dataTypeDefinition"] = ReadingDefinition()
            };
            bool array = member is "enum" or "examples";
            source["properties"]!["Value"]![member] = array ? new JsonArray(literal) : literal;
            if (pointerTarget)
            {
                source["properties"]!["Value"]!["uav:dataTypeDefinition"] = new JsonObject
                {
                    ["@id"] = "#/properties/Value/" + member + (array ? "/0" : string.Empty) + "/uav:dataTypeDefinition"
                };
            }
            else
            {
                source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            }
            string original = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(!pointerTarget), string.Join("; ", result.Diagnostics));
            Assert.That(source.ToJsonString(), Is.EqualTo(original));
            if (pointerTarget)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                    Is.True);
            }
            else
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
                Assert.That(JsonNode.DeepEquals(
                    JsonNode.Parse(view.Properties["reading"].GetProperty(member).GetRawText()),
                    source["properties"]!["Value"]![member]), Is.True);
            }
        }

        [TestCase("uav:dataTypeDefinition")]
        [TestCase("uav:fieldDataTypeDefinition")]
        [TestCase("default")]
        [TestCase("const")]
        [TestCase("enum")]
        [TestCase("examples")]
        public async Task DataSchemaPropertyNamesAreNotSemanticPredicates(string name)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "object";
            source["properties"]!["Value"]!["properties"] = new JsonObject
            {
                [name] = new JsonObject { ["type"] = "string" }
            };
            source["properties"]!["Value"]!["required"] = new JsonArray(name);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.RootElement.TryGetProperty("uav:dataTypeDefinitions", out _), Is.False);
            Assert.That(view.Properties["reading"].GetProperty("properties").GetProperty(name)
                .GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(view.Properties["reading"].GetProperty("required")[0].GetString(), Is.EqualTo(name));
        }

        [Test]
        public async Task UnknownPrefixedDataTypeFactsRetainTheirOriginalContext(
            [Values("uav:futureSemantic", "classification", "urn:semantic:classification")] string term,
            [Values] bool equal)
        {
            JsonObject source = DataTypeSource();
            JsonObject first = ReadingDefinition();
            JsonObject second = ReadingDefinition();
            first["@context"] = SemanticContext("urn:tag:source:");
            second["@context"] = SemanticContext(equal ? "urn:tag:source:" : "urn:tag:host:");
            first[term] = "tag:Reading";
            second[term] = "tag:Reading";
            source["uav:dataTypeDefinitions"] = new JsonArray(first);
            JsonObject plan = DataTypePlan();
            plan["@context"] = source["@context"]!.DeepClone();
            plan["uav:dataTypeDefinitions"] = new JsonArray(second);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(equal), string.Join("; ", result.Diagnostics));
            if (equal)
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
            }

            JsonObject SemanticContext(string tag)
            {
                return new JsonObject
                {
                    ["uav"] = "http://opcfoundation.org/UA/WoT-Binding/",
                    ["tag"] = tag,
                    [term] = new JsonObject { ["@id"] = "urn:semantic:classification", ["@type"] = "@id" }
                };
            }
        }

        [TestCase("@id")]
        [TestCase("uav:dataTypeName")]
        [TestCase("uav:fields")]
        public async Task DuplicateDefinitionMembersReturnDiagnosticsInsteadOfDictionaryExceptions(string name)
        {
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            string original = definition.ToJsonString();
            string duplicate = original[..^1] + ",\"" + name + "\":" + definition[name]!.ToJsonString() + "}";
            string document = source.ToJsonString().Replace(original, duplicate, StringComparison.Ordinal);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan().ToJsonString(), document)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True);
        }

        [TestCase("graph", true)]
        [TestCase("native", true)]
        [TestCase("name", false)]
        public async Task AmbiguousNamesDoNotOverrideDefinitiveDataTypeReferences(string selection, bool success)
        {
            JsonObject source = Source();
            source["@context"] = DataTypeSource()["@context"]!.DeepClone();
            source["uav:dataTypeDefinitions"] = new JsonArray(
                SimpleDefinition("urn:dtd:Reading", 3000, "t:Reading"),
                SimpleDefinition("urn:dtd:Other", 3001, "t:Reading"));
            source["properties"]!["Value"]!["type"] = "number";
            if (selection == "graph")
            {
                source["properties"]!["Value"]!["uav:dataTypeDefinition"] =
                    new JsonObject { ["@id"] = "urn:dtd:Reading" };
            }
            else
            {
                source["properties"]!["Value"]![selection == "native" ? "uav:dataTypeId" : "uav:dataTypeName"] =
                    selection == "native" ? "nsu=urn:test:projection-types;i=3000" : "t:Reading";
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
                Assert.That(definitions.GetArrayLength(), Is.EqualTo(1));
                Assert.That(definitions[0].GetProperty("@id").GetString(), Is.EqualTo("urn:dtd:Reading"));
                WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
                Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
                Assert.That(native.Value!.Items!.OfType<UADataType>().Single().NodeId, Does.EndWith("i=3000"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
            }
        }

        [TestCase("name", false)]
        [TestCase("object", false)]
        [TestCase("native", true)]
        [TestCase("graph", true)]
        public async Task AmbiguousBaseNamesRequireTheirOwnDefinitiveReference(string selection, bool success)
        {
            JsonObject source = Source();
            source["@context"] = DataTypeSource()["@context"]!.DeepClone();
            JsonObject reading = SimpleDefinition("urn:dtd:Reading", 3000, "t:Reading");
            reading["uav:dataTypeSubtypeOf"] = selection switch
            {
                "name" => JsonValue.Create("t:Reading"),
                "object" => new JsonObject { ["uav:dataTypeName"] = "t:Reading" },
                "native" => new JsonObject
                {
                    ["uav:dataTypeName"] = "t:Reading",
                    ["uav:dataTypeId"] = "nsu=urn:test:projection-types;i=3001"
                },
                _ => new JsonObject { ["@id"] = "urn:dtd:Other", ["uav:dataTypeName"] = "t:Reading" }
            };
            source["uav:dataTypeDefinitions"] = new JsonArray(
                reading, SimpleDefinition("urn:dtd:Other", 3001, "t:Reading"));
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] =
                new JsonObject { ["@id"] = "urn:dtd:Reading" };

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").EnumerateArray()
                    .Select(definition => definition.GetProperty("@id").GetString()),
                    Is.EquivalentTo(["urn:dtd:Reading", "urn:dtd:Other"]));
                WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
                Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
                UADataType outer = native.Value!.Items!.OfType<UADataType>()
                    .Single(node => node.NodeId.EndsWith("i=3000", StringComparison.Ordinal));
                Assert.That(outer.References!.Single(reference => reference.ReferenceType == "HasSubtype").Value,
                    Does.EndWith("i=3001"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                    Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EquivalentLocalDataTypePointersUseTheIndexedGraphIdentity(bool pointers)
        {
            JsonObject source = DataTypeSource();
            JsonObject reading = ReadingDefinition();
            reading["uav:fields"]![0]!.AsObject().Remove("uav:fieldDataTypeId");
            reading["uav:fields"]![0]!["uav:fieldDataTypeDefinition"] =
                new JsonObject { ["@id"] = pointers ? "#/uav:dataTypeDefinitions/1" : "urn:dtd:Nested" };
            source["uav:dataTypeDefinitions"] = new JsonArray(reading, ReadingDefinition("Nested", 3001));
            source["properties"]!["Value"]!["properties"]!["Value"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["Value"] = new JsonObject { ["type"] = "number", ["uav:dataTypeId"] = "i=11" }
                },
                ["required"] = new JsonArray("Value")
            };
            JsonObject plan = DataTypePlan();
            plan["id"] = "https://host.test/view.json";
            plan["@context"] = source["@context"]!.DeepClone();
            plan["uav:dataTypeDefinitions"] = source["uav:dataTypeDefinitions"]!.DeepClone();

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(2));
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            UADataType outer = native.Value!.Items!.OfType<UADataType>()
                .Single(node => node.NodeId.EndsWith("i=3000", StringComparison.Ordinal));
            Assert.That(outer.Definition!.Field!.Single().DataType, Does.EndWith("i=3001"));
        }

        [Test]
        public async Task KnownSimpleDataTypeFacetsDoNotMakePrefixAliasesIncomparable(
            [Values] bool alias, [Values] bool sameMinimum)
        {
            JsonObject source = Source();
            source["@context"] = DataTypeSource()["@context"]!.DeepClone();
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["minimum"] = 0;
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] =
                new JsonObject { ["@id"] = "urn:dtd:Reading" };
            source["uav:dataTypeDefinitions"] = new JsonArray(
                SimpleDefinition("urn:dtd:Reading", 3000, "t:Reading"));
            JsonObject host = SimpleDefinition("urn:dtd:Reading", 3000, alias ? "host:Reading" : "t:Reading");
            host["minimum"] = sameMinimum ? 0 : 1;
            JsonObject plan = DataTypePlan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { [alias ? "host" : "t"] = "urn:test:projection-types" });
            plan["uav:dataTypeDefinitions"] = new JsonArray(host);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(sameMinimum), string.Join("; ", result.Diagnostics));
            if (sameMinimum)
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
            }
        }

        private static JsonObject SimpleDefinition(string id, uint nativeId, string name)
        {
            return new JsonObject
            {
                ["@id"] = id,
                ["@type"] = "uav:SimpleDataType",
                ["uav:dataTypeName"] = name,
                ["uav:dataTypeId"] = "nsu=urn:test:projection-types;i=" +
                    nativeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["uav:dataTypeSubtypeOf"] = "i=11",
                ["type"] = "number",
                ["minimum"] = 0
            };
        }
    }
}
