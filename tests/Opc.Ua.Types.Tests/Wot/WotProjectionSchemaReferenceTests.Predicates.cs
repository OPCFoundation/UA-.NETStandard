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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [Test]
        public async Task PredicateSelectionComparesOwnerSemanticIdentities(
            [Values("@type", "uav:semanticId")] string predicate,
            [Values] bool sameSpelling)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["ontology"] = "https://vocabulary.test/shared/" });
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(
                new JsonObject { [predicate] = "ontology:Sensor" });
            JsonObject source = ContextReviewSource();
            string sourcePrefix = sameSpelling ? "ontology" : "sensor";
            source["@context"]!.AsArray().Add(new JsonObject
            {
                [sourcePrefix] = sameSpelling ? "https://vocabulary.test/other/" : "https://vocabulary.test/shared/"
            });
            source["properties"]!["Value"]![predicate] = predicate == "@type"
                ? new JsonArray("uav:variable", sourcePrefix + ":Sensor")
                : JsonValue.Create(sourcePrefix + ":Sensor");

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            if (sameSpelling)
            {
                Assert.That(view.Properties, Is.Empty,
                    "Equal prefix spellings under different owner bindings must not select the source.");
            }
            else
            {
                Assert.That(view.Properties, Has.Count.EqualTo(1),
                    "Different prefix spellings for the same semantic identity must select the source.");
                Assert.That(view.Properties["Value"].GetProperty("uav:resolvedFrom").GetString(),
                    Is.EqualTo(SourceHref + "#/properties/Value"));
                Assert.That(view.Properties["Value"].GetProperty("uav:dataTypeId").GetString(), Is.EqualTo("i=11"));
                WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
                Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
                UAVariable variable = native.Value.Items.OfType<UAVariable>().Single();
                Assert.That(variable.DataType, Is.EqualTo("i=11"));
                Assert.That(variable.ValueRank, Is.EqualTo(-1));
                QualifiedName name = QualifiedName.Parse(variable.BrowseName);
                Assert.That(name.Name, Is.EqualTo("Reading"));
                Assert.That(native.Value.NamespaceUris[name.NamespaceIndex - 1], Is.EqualTo("urn:source"));
            }
        }

        [Test]
        public async Task PredicateSelectionRetainsFilterAndAffordanceScopes(
            [Values("@type", "uav:semanticId")] string predicate, [Values] bool matching)
        {
            JsonObject plan = Plan();
            plan["@context"] = DefinitionTimeContexts(new JsonObject
            {
                ["ontology"] = "https://host.test/unrelated/",
                ["uav:select"] = new JsonObject
                {
                    ["@id"] = "http://opcfoundation.org/UA/WoT-Binding/select",
                    ["@context"] = new JsonObject { ["ontology"] = "https://vocabulary.test/shared/" }
                }
            });
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(
                new JsonObject { [predicate] = "ontology:Sensor" });
            JsonObject source = ContextReviewSource();
            source["@context"]!.AsArray().Add(new JsonObject { ["ontology"] = "https://source.test/unrelated/" });
            source["properties"]!["Value"]!["@context"] = new JsonObject
            {
                ["ontology"] = matching ? "https://vocabulary.test/shared/" : "https://vocabulary.test/other/"
            };
            source["properties"]!["Value"]![predicate] = predicate == "@type"
                ? new JsonArray("uav:variable", "ontology:Sensor")
                : JsonValue.Create("ontology:Sensor");

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties.ContainsKey("Value"), Is.EqualTo(matching));
            Assert.That(view.Properties, Has.Count.EqualTo(matching ? 1 : 0));
        }

        [Test]
        public async Task PredicateFiltersComposeKindSemanticIdAndAllTypes([Values] bool readable)
        {
            JsonObject plan = Plan();
            plan["@context"] = DefinitionTimeContexts(
                new JsonObject { ["filter"] = "https://vocabulary.test/" });
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(
                new JsonObject
                {
                    ["uav:affordanceKind"] = "property",
                    ["uav:semanticId"] = "filter:Process",
                    ["@type"] = new JsonArray("filter:Sensor", "filter:Readable")
                },
                new JsonObject
                {
                    ["uav:affordanceKind"] = "action",
                    ["uav:semanticId"] = "filter:Reset",
                    ["@type"] = "filter:ResetType"
                });
            JsonObject source = ContextReviewSource();
            source["@context"]!.AsArray().Add(new JsonObject { ["model"] = "https://vocabulary.test/" });
            source["properties"]!["Value"]!["@type"] = readable
                ? new JsonArray("uav:variable", "model:Sensor", "model:Readable")
                : new JsonArray("uav:variable", "model:Sensor");
            source["properties"]!["Value"]!["uav:semanticId"] = "model:Process";
            source["properties"]!["WrongMeaning"] = source["properties"]!["Value"]!.DeepClone();
            source["properties"]!["WrongMeaning"]!["uav:semanticId"] = "model:OtherProcess";
            source["actions"] = new JsonObject
            {
                ["Reset"] = new JsonObject
                {
                    ["@type"] = new JsonArray("uav:method", "model:ResetType"),
                    ["uav:semanticId"] = "model:Reset",
                    ["forms"] = Forms("reset")
                },
                ["WrongKind"] = new JsonObject
                {
                    ["@type"] = new JsonArray("uav:method", "model:Sensor", "model:Readable"),
                    ["uav:semanticId"] = "model:Process",
                    ["forms"] = Forms("wrong-kind")
                }
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties.ContainsKey("Value"), Is.EqualTo(readable));
            Assert.That(view.Properties, Has.Count.EqualTo(readable ? 1 : 0));
            Assert.That(view.Actions, Has.Count.EqualTo(1));
            Assert.That(view.Actions["Reset"].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/actions/Reset"));
        }

        [Test]
        public async Task UnresolvedPredicateFailsBeforeSourceAcquisition(
            [Values("@type", "uav:semanticId")] string predicate, [Values] bool emptySource)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1", "https://unacquired.test/context.jsonld");
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(new JsonObject
            {
                ["uav:affordanceKind"] = "event",
                [predicate] = predicate == "@type" ? "Signal" : "unknown:Signal"
            });
            JsonObject source = ContextReviewSource();
            if (emptySource)
            {
                source["properties"] = new JsonObject();
            }
            var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
            documents.Setup(resolver => resolver.ResolveThingAsync(
                    SourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(source.ToJsonString())));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan.ToJsonString()));

            WotConversionResult<WotDocument> result = await new WotProjectionResolver(documents.Object)
                .ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionSelectorInvalid),
                Is.True, string.Join("; ", result.Diagnostics));
            documents.VerifyNoOtherCalls();
        }

        [Test]
        public async Task DecisivePredicateMatchDoesNotDependOnUnknownAlternativeType(
            [Values("type-match", "filter-match", "unresolved")] string outcome, [Values] bool reverse)
        {
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject();
            var filters = new JsonArray(new JsonObject
            {
                ["@type"] = outcome == "type-match" ? "https://vocabulary.test/Sensor" : "https://vocabulary.test/Other"
            });
            if (outcome == "filter-match")
            {
                filters.Add(new JsonObject { ["uav:semanticId"] = "https://vocabulary.test/Process" });
            }
            plan["uav:projects"]![0]!["uav:select"] = filters;
            JsonObject source = ContextReviewSource();
            source["@context"]!.AsArray().Add("https://unacquired.test/context.jsonld");
            source["properties"]!["Value"]!["uav:semanticId"] = "https://vocabulary.test/Process";
            source["properties"]!["Value"]!["@type"] = reverse
                ? new JsonArray("https://vocabulary.test/Sensor", "Mystery")
                : new JsonArray("Mystery", "https://vocabulary.test/Sensor");

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            if (outcome == "unresolved")
            {
                Assert.That(result.Success, Is.False);
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionSelectorInvalid),
                    Is.True, string.Join("; ", result.Diagnostics));
                return;
            }
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties, Has.Count.EqualTo(1));
            JsonElement property = view.Properties["Value"];
            Assert.That(property.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Value"));
            Assert.That(property.GetProperty("@type").EnumerateArray().Select(item => item.GetString()),
                Does.Contain("Mystery"));
        }

        [Test]
        public async Task PredicateIdentityRetainsItsDefinitionTimeNamespace(
            [Values("@type", "uav:semanticId")] string predicate, [Values] bool before)
        {
            JsonObject plan = Plan();
            var contexts = DefinitionTimeContexts(new JsonObject { ["original"] = "https://vocabulary.test/shared/" });
            if (before)
            {
                contexts.Add(new JsonObject { ["selected"] = "original:" });
            }
            contexts.Add(new JsonObject { ["original"] = "https://vocabulary.test/later/" });
            if (!before)
            {
                contexts.Add(new JsonObject { ["selected"] = "original:" });
            }
            plan["@context"] = contexts;
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(
                new JsonObject { [predicate] = "selected:Sensor" });
            JsonObject source = ContextReviewSource();
            source["properties"]!["Value"]![predicate] = predicate == "@type"
                ? new JsonArray("uav:variable", "https://vocabulary.test/shared/Sensor")
                : JsonValue.Create("https://vocabulary.test/shared/Sensor");

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties.ContainsKey("Value"), Is.EqualTo(before));
            Assert.That(view.Properties, Has.Count.EqualTo(before ? 1 : 0));
        }

        [Test]
        public async Task SemanticPredicateUsesSourceLocationNotEndpointBase([Values] bool endpoint)
        {
            JsonObject plan = Plan();
            plan["id"] = "https://host.test/view.td.json";
            plan["base"] = "https://host.test/runtime/";
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(new JsonObject
            {
                ["uav:semanticId"] = endpoint
                    ? "https://device.test/runtime/Role"
                    : "https://origin.test/models/Role"
            });
            JsonObject source = ContextReviewSource();
            source["properties"]!["Value"]!["uav:semanticId"] = "Role";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties.ContainsKey("Value"), Is.EqualTo(!endpoint));
            Assert.That(view.Properties, Has.Count.EqualTo(endpoint ? 0 : 1));
        }

        [Test]
        public async Task TypePredicateUsesItsOwnVocabularyNotTheSourceVocabulary([Values] bool explicitIdentity)
        {
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(new JsonObject
            {
                ["@type"] = explicitIdentity ? "https://www.w3.org/2019/wot/json-schema#Sensor" : "Sensor"
            });
            JsonObject source = ContextReviewSource();
            source["properties"]!["Value"]!["@type"] = new JsonArray("uav:variable", "Sensor");

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties.ContainsKey("Value"), Is.EqualTo(explicitIdentity));
            Assert.That(view.Properties, Has.Count.EqualTo(explicitIdentity ? 1 : 0));
        }
    }
}
