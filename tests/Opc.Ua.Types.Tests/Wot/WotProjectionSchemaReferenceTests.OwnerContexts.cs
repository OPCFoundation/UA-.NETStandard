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
        public async Task SourceAffordanceRetainsItsOwnPrefixDespiteHostBinding(
            [Values] bool objectPrefix, [Values] bool localContext)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:host" });
            plan["id"] = "urn:projection:owner-context";
            plan["uav:id"] = "nsu=urn:host;i=1";
            JsonObject source = Source();
            JsonNode prefix = objectPrefix
                ? new JsonObject { ["@id"] = "urn:source", ["@prefix"] = true }
                : JsonValue.Create("urn:source");
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = prefix });
            source["uav:id"] = "nsu=urn:source;i=1";
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:dataTypeId"] = "i=11";
            source["properties"]!["Value"]!["uav:id"] = "nsu=urn:source;i=2";
            source["properties"]!["Value"]!["uav:browseName"] = "ex:Reading";
            if (localContext)
            {
                source["properties"]!["Value"]!["@context"] = new JsonObject { ["ex"] = "urn:local" };
            }
            string originalPlan = plan.ToJsonString();
            string originalSource = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(plan.ToJsonString(), Is.EqualTo(originalPlan));
            Assert.That(source.ToJsonString(), Is.EqualTo(originalSource));
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            UAVariable reading = native.Value!.Items!.OfType<UAVariable>()
                .Single(node => node.NodeId.EndsWith("i=2", StringComparison.Ordinal));
            var name = QualifiedName.Parse(reading.BrowseName);
            Assert.That(name.Name, Is.EqualTo("Reading"));
            Assert.That(native.Value.NamespaceUris[name.NamespaceIndex - 1],
                Is.EqualTo(localContext ? "urn:local" : "urn:source"));
        }

        [Test]
        public async Task ProjectionContextRetainsItsOrderedEntries([Values] bool repeatedContextUrl)
        {
            JsonObject plan = Plan();
            var context = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:early" });
            if (repeatedContextUrl)
            {
                context.Add("https://www.w3.org/2022/wot/td/v1.1");
            }
            context.Add(new JsonObject { ["ex"] = "urn:late" });
            plan["@context"] = context;
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";
            string original = context.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(view.RootElement.GetProperty("@context").GetRawText()),
                JsonNode.Parse(original)), Is.True);
        }

        [Test]
        public async Task SourceContextDoesNotInheritHostOnlyPrefixes([Values] bool reset)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["hostOnly"] = "urn:host" });
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";
            if (reset)
            {
                source["properties"]!["Value"]!["@context"] = null;
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement carried = view.Properties["reading"];
            Assert.That(view.TryGetContextPrefix("hostOnly", out _, carried), Is.False);
            Assert.That(view.TryGetContextPrefix("ua", out string ua, carried), Is.EqualTo(!reset));
            if (!reset)
            {
                Assert.That(ua, Is.EqualTo(Ua.Namespaces.OpcUa));
            }
        }

        [Test]
        public async Task CarriedFormsKeepTheirActualOwnerContext([Values] bool hostRouting, [Values] bool local)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:host" });
            JsonObject source = Source();
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:source" });
            source["properties"]!["Value"]!["type"] = "number";
            JsonArray forms = hostRouting ? Forms("https://host.test/read") :
                source["properties"]!["Value"]!["forms"]!.AsArray();
            if (local)
            {
                forms[0]!["@context"] = new JsonObject { ["ex"] = hostRouting ? "urn:host-form" : "urn:source-form" };
            }
            if (hostRouting)
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = forms;
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement form = view.Properties["reading"].GetProperty("forms")[0];
            Assert.That(view.TryGetContextPrefix("ex", out string actual, form), Is.True);
            Assert.That(actual, Is.EqualTo(hostRouting
                ? local ? "urn:host-form" : "urn:host"
                : local ? "urn:source-form" : "urn:source"));
            Assert.That(view.TryGetContextPrefix("ex", out string dataScope, view.Properties["reading"]), Is.True);
            Assert.That(dataScope, Is.EqualTo("urn:source"));
        }

        [TestCase("schema")]
        [TestCase("variable")]
        [TestCase("datatype")]
        [TestCase("security")]
        public async Task CarriedDependenciesRetainTheirOriginalContext(string kind)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:host" });
            JsonObject source = kind == "datatype" ? DataTypeSource() : Source();
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:source", ["t"] = "urn:test:projection-types" });
            JsonObject owned;
            string pointer;
            switch (kind)
            {
                case "schema":
                    owned = new JsonObject { ["type"] = "number" };
                    source["schemaDefinitions"] = new JsonObject { ["reading"] = owned };
                    source["properties"]!["Value"]!["$ref"] = "#/schemaDefinitions/reading";
                    pointer = "/schemaDefinitions/reading";
                    break;
                case "variable":
                    owned = new JsonObject { ["type"] = "string" };
                    source["uriVariables"] = new JsonObject { ["slot"] = owned };
                    source["properties"]!["Value"]!["type"] = "number";
                    source["properties"]!["Value"]!["forms"] = Forms("read{?slot}");
                    pointer = "/properties/reading/uriVariables/slot";
                    break;
                case "datatype":
                    owned = ReadingDefinition();
                    source["uav:dataTypeDefinitions"] = new JsonArray(owned);
                    pointer = "/uav:dataTypeDefinitions/0";
                    break;
                default:
                    owned = source["securityDefinitions"]!["none"]!.AsObject();
                    source["properties"]!["Value"]!["type"] = "number";
                    pointer = "/securityDefinitions/q:s:cA:bm9uZQ";
                    break;
            }
            owned["@context"] = new JsonObject { ["ex"] = "urn:owned:" + kind };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.TryEvaluatePointer(pointer, out JsonElement dependency), Is.True);
            Assert.That(view.TryGetContextPrefix("ex", out string actual, dependency), Is.True);
            Assert.That(actual, Is.EqualTo("urn:owned:" + kind));
            Assert.That(view.TryGetContextPrefix("ex", out string rootScope), Is.True);
            Assert.That(rootScope, Is.EqualTo("urn:host"));
        }

        [Test]
        public async Task HostAnnotationsRetainTheirOwnSemanticIdentities()
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:host:" });
            plan["properties"]!["reading"]!["@type"] = "ex:Signal";
            plan["properties"]!["reading"]!["uav:semanticId"] = "ex:Role";
            JsonObject source = Source();
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ex"] = "urn:source:" });
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["@type"] = "ex:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement property = view.Properties["reading"];
            Assert.That(property.GetProperty("@type").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(["ex:Signal", "urn:host:Signal"]));
            Assert.That(property.GetProperty("uav:semanticId").GetString(), Is.EqualTo("urn:host:Role"));
            Assert.That(view.TryGetContextPrefix("ex", out string sourcePrefix, property), Is.True);
            Assert.That(sourcePrefix, Is.EqualTo("urn:source:"));
        }

        [Test]
        public async Task HostTitleOverrideDoesNotRetagSourceDescription([Values] bool nullLanguage)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["@container"] = "@index",
                        ["@context"] = new JsonObject
                        {
                            ["title"] = LanguageTerm("title", nullLanguage ? null : "fr")
                        }
                    }
                });
            plan["id"] = "urn:projection:localized";
            plan["uav:id"] = "nsu=urn:host;i=1";
            plan["properties"]!["reading"]!["title"] = "Titre";
            JsonObject source = Source();
            source["uav:id"] = "nsu=urn:source;i=1";
            source["properties"]!["Value"]!["uav:id"] = "nsu=urn:source;i=2";
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["title"] = "Quelle";
            source["properties"]!["Value"]!["description"] = "Beschreibung";
            source["properties"]!["Value"]!["@context"] = new JsonObject
            {
                ["title"] = LanguageTerm("title", "de"),
                ["description"] = LanguageTerm("description", "de")
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            UAVariable reading = native.Value!.Items!.OfType<UAVariable>()
                .Single(node => node.NodeId.EndsWith("i=2", StringComparison.Ordinal));
            Assert.That(reading.DisplayName![0].Value, Is.EqualTo("Titre"));
            if (nullLanguage)
            {
                Assert.That(reading.DisplayName[0].Locale, Is.Null.Or.Empty);
            }
            else
            {
                Assert.That(reading.DisplayName[0].Locale, Is.EqualTo("fr"));
            }
            Assert.That(reading.Description![0].Value, Is.EqualTo("Beschreibung"));
            Assert.That(reading.Description[0].Locale, Is.EqualTo("de"));

            static JsonObject LanguageTerm(string term, string language)
            {
                return new JsonObject
                {
                    ["@id"] = "https://www.w3.org/2019/wot/td#" + term,
                    ["@language"] = language
                };
            }
        }

        [Test]
        public async Task MovedContextLocationsUseTheSourceDocumentNotItsEndpointBase()
        {
            JsonObject plan = Plan();
            JsonObject source = Source();
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1", "./contexts/model.jsonld",
                new JsonObject
                {
                    ["@base"] = "../identity/",
                    ["term"] = new JsonObject { ["@id"] = "urn:term", ["@context"] = "./contexts/term.jsonld" }
                });
            source["properties"]!["Value"]!["type"] = "number";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement context = view.Properties["reading"].GetProperty("@context");
            Assert.That(context.EnumerateArray().Any(entry => entry.ValueKind == JsonValueKind.String &&
                entry.GetString() == "https://origin.test/models/contexts/model.jsonld"), Is.True);
            JsonElement authored = context.EnumerateArray().Single(entry => entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("@base", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString() == "https://origin.test/identity/");
            Assert.That(authored.GetProperty("term").GetProperty("@context").GetString(),
                Is.EqualTo("https://origin.test/models/contexts/term.jsonld"));
        }

        [TestCase("items")]
        [TestCase("additionalProperties")]
        public async Task NestedSchemaContextLocationsRetainTheSourceOrigin(string schemaMember)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = schemaMember == "items" ? "array" : "object";
            source["properties"]!["Value"]![schemaMember] = new JsonObject
            {
                ["type"] = "number",
                ["@context"] = "./contexts/nested.jsonld"
            };
            source["properties"]!["Value"]!["default"] = new JsonObject
            {
                ["@context"] = "./literal.jsonld",
                ["id"] = "not-a-reference"
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement property = view.Properties["reading"];
            Assert.That(property.GetProperty(schemaMember).GetProperty("@context").GetString(),
                Is.EqualTo("https://origin.test/models/contexts/nested.jsonld"));
            Assert.That(property.GetProperty("default").GetProperty("@context").GetString(),
                Is.EqualTo("./literal.jsonld"));
            Assert.That(property.GetProperty("default").GetProperty("id").GetString(), Is.EqualTo("not-a-reference"));
        }

        [Test]
        public async Task OrderedSourceBaseContextsRetainTheirEffectiveBase([Values] bool reset)
        {
            JsonObject source = Source();
            var contexts = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["@base"] = "./identity/" });
            if (reset)
            {
                contexts.Add(null);
            }
            contexts.Add(new JsonObject { ["@base"] = "child/" });
            source["@context"] = contexts;
            source["properties"]!["Value"]!["type"] = "number";

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.TryGetContextTerm("@base", out JsonElement baseUri, view.Properties["reading"]), Is.True);
            Assert.That(baseUri.GetString(), Is.EqualTo(reset
                ? "https://origin.test/models/child/"
                : "https://origin.test/models/identity/child/"));
        }

        [Test]
        public async Task HostSemanticIdentityUsesItsOrderedBaseContexts([Values] bool reset)
        {
            JsonObject plan = Plan();
            plan["id"] = "https://host.test/models/view.json";
            var contexts = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["@base"] = "./identity/" });
            if (reset)
            {
                contexts.Add(null);
            }
            contexts.Add(new JsonObject { ["@base"] = "child/" });
            plan["@context"] = contexts;
            plan["properties"]!["reading"]!["uav:semanticId"] = "Reading";
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("uav:semanticId").GetString(), Is.EqualTo(reset
                ? "https://host.test/models/child/Reading"
                : "https://host.test/models/identity/child/Reading"));
        }

        [TestCase("urn:host:unlocated")]
        [TestCase("tag:host,2026:unlocated")]
        public async Task RelativeContextLocationDoesNotUseAnOpaqueLogicalIdentifier(string id)
        {
            JsonObject plan = Plan();
            plan["id"] = id;
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1", "../context.jsonld");
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.RootElement.GetProperty("@context")[1].GetString(), Is.EqualTo("../context.jsonld"));
        }

        [Test]
        public async Task HostTitleStringTermKeepsItsSemanticIdentity()
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["@container"] = "@index",
                        ["@context"] = new JsonObject { ["title"] = "urn:host:title", ["@language"] = "fr" }
                    }
                });
            plan["properties"]!["reading"]!["title"] = "Titre";
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.TryGetContextTerm("title", out JsonElement title, view.Properties["reading"]), Is.True);
            Assert.That(title.GetProperty("@id").GetString(), Is.EqualTo("urn:host:title"));
            Assert.That(title.GetProperty("@language").GetString(), Is.EqualTo("fr"));
        }

        [Test]
        public async Task ContextMapKeysAreNotContextDeclarations(
            [Values] bool arrayContainer, [Values] bool objectEntry)
        {
            JsonObject source = Source();
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["labels"] = new JsonObject
                    {
                        ["@id"] = "urn:labels",
                        ["@container"] = arrayContainer ? new JsonArray("@index", "@set") : JsonValue.Create("@index")
                    }
                });
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["labels"] = new JsonObject
            {
                ["@context"] = objectEntry
                    ? new JsonObject { ["@context"] = "./contexts/item.jsonld", ["marker"] = "preserved" }
                    : JsonValue.Create("literal")
            };
            string original = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(source.ToJsonString(), Is.EqualTo(original));
            JsonElement entry = view.Properties["reading"].GetProperty("labels").GetProperty("@context");
            if (objectEntry)
            {
                Assert.That(entry.GetProperty("@context").GetString(),
                    Is.EqualTo("https://origin.test/models/contexts/item.jsonld"));
                Assert.That(entry.GetProperty("marker").GetString(), Is.EqualTo("preserved"));
            }
            else
            {
                Assert.That(entry.GetString(), Is.EqualTo("literal"));
            }
        }
    }
}
