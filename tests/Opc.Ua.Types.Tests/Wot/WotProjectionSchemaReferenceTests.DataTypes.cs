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
        [TestCase(false)]
        [TestCase(true)]
        public async Task SelectedPayloadCarriesExactlyOneCompleteDataTypeDefinition(bool inline)
        {
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            source["uav:dataTypeDefinitions"] = inline
                ? new JsonArray(ReadingDefinition("Unused", 3001))
                : new JsonArray(definition, ReadingDefinition("Unused", 3001));
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] = inline
                ? definition
                : new JsonObject { ["@id"] = "urn:dtd:Reading" };
            string original = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.GetArrayLength(), Is.EqualTo(1));
            Assert.That(definitions[0].GetProperty("@id").GetString(), Is.EqualTo("urn:dtd:Reading"));
            Assert.That(definitions[0].GetProperty("uav:fields")[0].GetProperty("uav:fieldName").GetString(),
                Is.EqualTo("Value"));
            Assert.That(view.Properties["reading"].GetProperty("uav:dataTypeDefinition")
                .EnumerateObject().Select(member => member.Name), Is.EqualTo(s_dataTypeReferenceKeys));
            Assert.That(source.ToJsonString(), Is.EqualTo(original));

            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            UADataType type = native.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(type.BrowseName, Does.EndWith(":Reading"));
            Assert.That(type.Definition!.Field!.Single().DataType, Is.EqualTo("i=11"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DataTypeFieldReferencesCloseOverOnlyTheirOriginalDefinitions(bool recursive)
        {
            JsonObject source = DataTypeSource();
            JsonObject reading = ReadingDefinition();
            JsonObject nested = ReadingDefinition("Nested", 3001);
            JsonNode field = reading["uav:fields"]![0]!;
            field.AsObject().Remove("uav:fieldDataTypeId");
            field["uav:fieldDataTypeDefinition"] = new JsonObject { ["@id"] = "urn:dtd:Nested" };
            if (recursive)
            {
                JsonNode back = nested["uav:fields"]![0]!;
                back.AsObject().Remove("uav:fieldDataTypeId");
                back["uav:fieldDataTypeDefinition"] = new JsonObject { ["@id"] = "urn:dtd:Reading" };
            }
            source["uav:dataTypeDefinitions"] = new JsonArray(reading, nested, ReadingDefinition("Unused", 3002));

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.EnumerateArray().Select(item => item.GetProperty("@id").GetString()),
                Is.EquivalentTo(s_readingAndNestedIds));
            JsonElement carried = definitions.EnumerateArray()
                .Single(item => item.GetProperty("@id").GetString() == "urn:dtd:Reading");
            Assert.That(carried.GetProperty("uav:fields")[0].GetProperty("uav:fieldDataTypeDefinition")
                .GetProperty("@id").GetString(), Is.EqualTo("urn:dtd:Nested"));
        }

        [TestCase("uav:dataTypeName", "t:Reading")]
        [TestCase("uav:dataTypeId", "nsu=urn:test:projection-types;i=3000")]
        public async Task NativeTypeIdentityReferencesCarryTheirKnownDefinition(string term, string reference)
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            source["properties"]!["Value"]!.AsObject().Remove("uav:dataTypeDefinition");
            source["properties"]!["Value"]![term] = reference;

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
            Assert.That(view.Properties["reading"].GetProperty(term).GetString(), Is.EqualTo(reference));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DuplicateCompleteDataTypeDefinitionsCannotBeMergedWithinOneSource(bool sameFacts)
        {
            JsonObject source = DataTypeSource();
            JsonObject second = ReadingDefinition();
            if (!sameFacts)
            {
                second["uav:fields"]![0]!["uav:fieldDataTypeId"] = "i=6";
            }
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition(), second);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True);
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public async Task CarriedDataTypeDefinitionsShareTheProjectionNodeBudget(int limit, bool success)
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());

            WotConversionResult<WotDocument> result = await ResolveAsync(
                DataTypePlan(), source, new WotNodeSetConverterOptions { MaxNodeCount = limit }).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.TraversalBudgetExhausted),
                    Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DataTypeBaseReferencesCarryTheLocalBaseDefinition(bool objectReference)
        {
            JsonObject source = DataTypeSource();
            JsonObject derived = ReadingDefinition();
            derived["uav:dataTypeSubtypeOf"] = objectReference
                ? new JsonObject { ["@id"] = "urn:dtd:Base" }
                : JsonValue.Create("urn:dtd:Base");
            source["uav:dataTypeDefinitions"] = new JsonArray(derived, ReadingDefinition("Base", 3001));

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.GetArrayLength(), Is.EqualTo(2));
            Assert.That(definitions.EnumerateArray().Any(item => item.GetProperty("@id").GetString() == "urn:dtd:Base"),
                Is.True);
        }

        [Test]
        public async Task BaseTypeCarriagePreservesEverySuppliedIdentityForm(
            [Values("name", "id", "both", "graphAndName", "graphAndId", "all")] string form)
        {
            JsonObject source = DataTypeSource();
            JsonObject derived = ReadingDefinition();
            var reference = new JsonObject();
            if (form is "graphAndName" or "graphAndId" or "all")
            {
                reference["@id"] = "urn:dtd:Base";
            }
            if (form is "id" or "both" or "graphAndId" or "all")
            {
                reference["uav:dataTypeId"] = "nsu=urn:test:projection-types;i=3001";
            }
            if (form is "name" or "both" or "graphAndName" or "all")
            {
                reference["uav:dataTypeName"] = "t:Base";
            }
            string original = reference.ToJsonString();
            derived["uav:dataTypeSubtypeOf"] = reference;
            source["uav:dataTypeDefinitions"] = new JsonArray(derived, ReadingDefinition("Base", 3001));

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.GetArrayLength(), Is.EqualTo(2));
            JsonElement reading = definitions.EnumerateArray()
                .Single(definition => definition.GetProperty("@id").GetString() == "urn:dtd:Reading");
            Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(reading.GetProperty("uav:dataTypeSubtypeOf").GetRawText()),
                JsonNode.Parse(original)), Is.True);
        }

        [TestCase("uav:dataTypeId", "nsu=urn:test:projection-types;i=3002")]
        [TestCase("uav:dataTypeName", "t:Other")]
        public async Task ConflictingBaseIdentityFormsCannotBeErasedDuringCarriage(string term, string value)
        {
            JsonObject source = DataTypeSource();
            JsonObject derived = ReadingDefinition();
            derived["uav:dataTypeSubtypeOf"] = new JsonObject
            {
                ["@id"] = "urn:dtd:Base",
                ["uav:dataTypeId"] = "nsu=urn:test:projection-types;i=3001",
                ["uav:dataTypeName"] = "t:Base"
            };
            derived["uav:dataTypeSubtypeOf"]![term] = value;
            source["uav:dataTypeDefinitions"] = new JsonArray(
                derived, ReadingDefinition("Base", 3001), ReadingDefinition("Other", 3002));

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True);
        }

        [Test]
        public async Task LiteralAndOpaqueDefinitionLookalikesDoNotBecomeDependencies()
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            var literal = new JsonObject { ["uav:dataTypeDefinition"] = ReadingDefinition("Literal", 3100) };
            source["properties"]!["Value"]!["default"] = literal;
            source["properties"]!["Value"]!["uav:metadata"] =
                new JsonObject { ["uav:dataTypeDefinition"] = ReadingDefinition("Metadata", 3200) };
            string original = literal.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
            Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(view.Properties["reading"].GetProperty("default").GetRawText()),
                JsonNode.Parse(original)), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MatchingHostAndSourceGraphIdentitiesRequireEqualResolvedFacts(bool conflict)
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            JsonObject plan = DataTypePlan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["host"] = "urn:test:projection-types" });
            JsonObject hosted = ReadingDefinition();
            hosted["uav:dataTypeName"] = "host:Reading";
            if (conflict)
            {
                hosted["uav:fields"]![0]!["uav:fieldDataTypeId"] = "i=6";
            }
            plan["uav:dataTypeDefinitions"] = new JsonArray(hosted);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(!conflict), string.Join("; ", result.Diagnostics));
            if (!conflict)
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
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
        public async Task ReusedDefinitionsStillValidateTheSecondOwnersTransitiveFacts(bool conflict)
        {
            JsonObject source = DataTypeSource();
            JsonObject reading = ReadingDefinition();
            JsonObject nested = ReadingDefinition("Nested", 3001);
            reading["uav:fields"]![0]!.AsObject().Remove("uav:fieldDataTypeId");
            reading["uav:fields"]![0]!["uav:fieldDataTypeDefinition"] =
                new JsonObject { ["@id"] = "urn:dtd:Nested" };
            source["uav:dataTypeDefinitions"] = new JsonArray(reading, nested);
            JsonObject plan = DataTypePlan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["t"] = "urn:test:projection-types" });
            plan["uav:dataTypeDefinitions"] = new JsonArray(reading.DeepClone(), nested.DeepClone());
            if (conflict)
            {
                nested["uav:fields"]![0]!["uav:fieldDataTypeId"] = "i=6";
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(!conflict), string.Join("; ", result.Diagnostics));
            if (conflict)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                    Is.True);
            }
            else
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(2));
            }
        }

        [TestCase("#/uav:dataTypeDefinitions/0", true)]
        [TestCase("#/uav:dataTypeDefinitions/9", false)]
        [TestCase("#/properties/Value/default", false)]
        public async Task LocalDataTypePointersMustIdentifyAnOwnedDefinition(string reference, bool success)
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] = new JsonObject { ["@id"] = reference };
            source["properties"]!["Value"]!["default"] = ReadingDefinition("Literal", 3009);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.RootElement.GetProperty("uav:dataTypeDefinitions").GetArrayLength(), Is.EqualTo(1));
                Assert.That(view.Properties["reading"].GetProperty("uav:dataTypeDefinition")
                    .GetProperty("@id").GetString(), Is.EqualTo("urn:dtd:Reading"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                    Is.True);
            }
        }

        [Test]
        public async Task DuplicateDataTypeContainersCannotAssignDifferentDefinitionsToOnePointer()
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            string json = source.ToJsonString();
            json = json[..^1] +
                ",\"uav:dataTypeDefinitions\":[" +
                ReadingDefinition("Other", 3001).ToJsonString() +
                "]}";

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan().ToJsonString(), json)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True);
        }

        [Test]
        public async Task DifferentGraphIdentitiesCannotClaimOneNativeDataType()
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            JsonObject plan = DataTypePlan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["t"] = "urn:test:projection-types" });
            JsonObject hosted = ReadingDefinition();
            hosted["@id"] = "urn:dtd:Other";
            plan["uav:dataTypeDefinitions"] = new JsonArray(hosted);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True);
        }

        [Test]
        public async Task InvalidDataTypeCollectionsCannotDisappearDuringCarriage(
            [Values("null", "{}", "[null]", "[false]", "[17]")] string collection, [Values] bool host)
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            JsonObject plan = DataTypePlan();
            (host ? plan : source)["uav:dataTypeDefinitions"] = JsonNode.Parse(collection);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True);
        }

        [TestCase("null")]
        [TestCase("17")]
        [TestCase("[]")]
        [TestCase("\"urn:dtd:Reading\"")]
        public async Task ADataTypeReferenceMustRetainItsObjectShape(string reference)
        {
            JsonObject source = DataTypeSource();
            source["uav:dataTypeDefinitions"] = new JsonArray(ReadingDefinition());
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] = JsonNode.Parse(reference);

            WotConversionResult<WotDocument> result = await ResolveAsync(DataTypePlan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True);
        }

        [Test]
        public async Task DefinitionReuseDoesNotEraseContextOrLocationDependentFacts(
            [Values("custom", "language", "schema")] string fact, [Values] bool equal)
        {
            JsonObject source = DataTypeSource();
            JsonObject sourceType = ReadingDefinition();
            JsonObject hostType = ReadingDefinition();
            JsonObject plan = DataTypePlan();
            plan["id"] = "https://host.test/view.json";
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["t"] = "urn:test:projection-types" });
            if (fact == "custom")
            {
                sourceType["@context"] = ClassificationContext("urn:source:");
                hostType["@context"] = ClassificationContext(equal ? "urn:source:" : "urn:host:");
                sourceType["classification"] = "tag:Reading";
                hostType["classification"] = "tag:Reading";
            }
            else if (fact == "language")
            {
                sourceType["uav:fields"]![0]!["@context"] = new JsonObject { ["@language"] = "en" };
                hostType["uav:fields"]![0]!["@context"] = new JsonObject { ["@language"] = equal ? "en" : "de" };
                sourceType["uav:fields"]![0]!["uav:fieldDescription"] = "Value";
                hostType["uav:fields"]![0]!["uav:fieldDescription"] = "Value";
            }
            else
            {
                sourceType["uav:externalSchema"] = "./reading.schema.json";
                hostType["uav:externalSchema"] = equal
                    ? "https://origin.test/models/reading.schema.json" : "./reading.schema.json";
            }
            source["uav:dataTypeDefinitions"] = new JsonArray(sourceType);
            plan["uav:dataTypeDefinitions"] = new JsonArray(hostType);

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
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.DataTypeDefinitionInvalid),
                    Is.True);
            }
        }

        [Test]
        public async Task OpaqueDefinitionMetadataRemainsLiteralDuringReuse()
        {
            JsonObject source = DataTypeSource();
            JsonObject sourceType = ReadingDefinition();
            JsonObject hostType = ReadingDefinition();
            sourceType["@context"] = ClassificationContext("urn:source:");
            hostType["@context"] = ClassificationContext("urn:host:");
            sourceType["uav:metadata"] = new JsonObject { ["classification"] = "tag:Reading" };
            hostType["uav:metadata"] = new JsonObject { ["classification"] = "tag:Reading" };
            source["uav:dataTypeDefinitions"] = new JsonArray(sourceType);
            JsonObject plan = DataTypePlan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["t"] = "urn:test:projection-types" });
            plan["uav:dataTypeDefinitions"] = new JsonArray(hostType);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("uav:dataTypeDefinitions");
            Assert.That(definitions.GetArrayLength(), Is.EqualTo(1));
            Assert.That(definitions[0].GetProperty("uav:metadata").GetProperty("classification").GetString(),
                Is.EqualTo("tag:Reading"));
        }

        private static JsonObject ClassificationContext(string prefix)
        {
            return new JsonObject
            {
                ["tag"] = prefix,
                ["classification"] = new JsonObject { ["@id"] = "urn:classification", ["@type"] = "@id" }
            };
        }

        private static JsonObject DataTypeSource()
        {
            JsonObject source = Source();
            source["@context"] = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["t"] = "urn:test:projection-types" });
            source["properties"]!["Value"]!["type"] = "object";
            source["properties"]!["Value"]!["uav:dataTypeDefinition"] =
                new JsonObject { ["@id"] = "urn:dtd:Reading" };
            source["properties"]!["Value"]!["properties"] = new JsonObject
            {
                ["Value"] = new JsonObject { ["type"] = "number", ["uav:dataTypeId"] = "i=11" }
            };
            source["properties"]!["Value"]!["required"] = new JsonArray("Value");
            return source;
        }

        private static JsonObject DataTypePlan()
        {
            JsonObject plan = Plan();
            plan["uav:id"] = "nsu=urn:test:projection;i=1";
            return plan;
        }

        private static JsonObject ReadingDefinition(string name = "Reading", uint id = 3000)
        {
            return new JsonObject
            {
                ["@id"] = "urn:dtd:" + name,
                ["@type"] = "uav:StructureDefinition",
                ["uav:dataTypeName"] = "t:" + name,
                ["uav:dataTypeId"] = "nsu=urn:test:projection-types;i=" +
                    id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["uav:structureType"] = "Structure",
                ["uav:fields"] = new JsonArray(new JsonObject
                {
                    ["@type"] = "uav:StructureField",
                    ["uav:fieldName"] = "Value",
                    ["uav:fieldDataTypeId"] = "i=11"
                })
            };
        }

        private static readonly string[] s_dataTypeReferenceKeys = ["@id"];
        private static readonly string[] s_readingAndNestedIds = ["urn:dtd:Reading", "urn:dtd:Nested"];
    }
}
