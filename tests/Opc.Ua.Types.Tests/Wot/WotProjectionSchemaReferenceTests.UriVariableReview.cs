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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task HostRoutedFormsDoNotRetargetSourceDataReferences(bool nested)
        {
            JsonObject plan = UriVariableReviewPlan(host: true);
            JsonObject source = UriVariableReviewSource();
            JsonObject selected = source["properties"]!["Value"]!.AsObject();
            selected["uriVariables"] = new JsonObject { ["device"] = UriVariable("source-schema") };
            const string reference = "#/properties/Value/uriVariables/device";
            if (nested)
            {
                selected["type"] = "object";
                selected["properties"] = new JsonObject
                {
                    ["payload"] = new JsonObject { ["$ref"] = reference }
                };
            }
            else
            {
                selected.Remove("type");
                selected["$ref"] = reference;
            }

            WotConversionResult<WotDocument> result = await ResolveUriVariableReviewAsync(
                plan, source.ToJsonString()).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error),
                Is.False);
            JsonElement picked = view.Properties["picked"];
            string relocated = (nested ? picked.GetProperty("properties").GetProperty("payload") : picked)
                .GetProperty("$ref").GetString();
            Assert.That(relocated, Does.StartWith("#/"));
            Assert.That(WotDocument.TryEvaluatePointer(view.RootElement, relocated[1..], out JsonElement target),
                Is.True);
            Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(target.GetProperty("default").GetString(), Is.EqualTo("source-schema"));
            Assert.That(picked.GetProperty("uriVariables").GetProperty("device").GetProperty("default").GetString(),
                Is.EqualTo("host-schema"));
            Assert.That(view.RootElement.GetProperty("uriVariables").GetProperty("device")
                .GetProperty("default").GetString(), Is.EqualTo("host-schema"));
            Assert.That(picked.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://host.test/{device}"));
            Assert.That(picked.GetProperty("forms")[0].GetProperty("op").GetString(), Is.EqualTo("readproperty"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SourceLocalDuplicateUriVariableContainersAreValidatedBeforeSelection(bool equivalent)
        {
            JsonObject source = UriVariableReviewSource();
            source["properties"]!["Value"]!["forms"]![0]!["href"] = "https://source.test/{device}";
            source["properties"]!["Value"]!["uriVariables"] =
                new JsonObject { ["device"] = UriVariable("original") };
            string raw = DuplicateLocalUriVariableContainers(source, equivalent);

            WotConversionResult<WotDocument> result = await ResolveUriVariableReviewAsync(
                UriVariableReviewPlan(host: false), raw).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(equivalent), string.Join("; ", result.Diagnostics));
            if (equivalent)
            {
                Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error),
                    Is.False);
                JsonElement picked = view.Properties["picked"];
                Assert.That(picked.EnumerateObject().Count(property => property.NameEquals("uriVariables")),
                    Is.EqualTo(1));
                JsonElement variables = picked.GetProperty("uriVariables");
                Assert.That(variables.EnumerateObject().Count(), Is.EqualTo(1));
                Assert.That(variables.GetProperty("device").GetProperty("type").GetString(), Is.EqualTo("string"));
                Assert.That(variables.GetProperty("device").GetProperty("default").GetString(), Is.EqualTo("original"));
                Assert.That(picked.GetProperty("forms")[0].GetProperty("href").GetString(),
                    Is.EqualTo("https://source.test/{device}"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            }
        }

        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        public async Task SourceVariableSupportKeepsItsOriginalContextReferencesAndBudget(int limit, bool success)
        {
            JsonObject source = UriVariableReviewSource();
            source["@context"] = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["native"] = "urn:root:",
                    ["uriVariables"] = new JsonObject
                    {
                        ["@id"] = "https://www.w3.org/2019/wot/td#hasUriTemplateSchema",
                        ["@container"] = "@index",
                        ["@context"] = new JsonObject { ["native"] = "urn:term:" }
                    }
                });
            JsonObject variable = UriVariable("source-schema");
            variable["@context"] = new JsonArray(
                new JsonObject { ["native"] = "urn:own-first:" },
                new JsonObject { ["native"] = "urn:own-last:" });
            variable["uav:semanticId"] = "native:Variable";
            variable["$ref"] = "#/schemaDefinitions/S";
            JsonObject selected = source["properties"]!["Value"]!.AsObject();
            selected["@context"] = new JsonObject { ["native"] = "urn:affordance:" };
            selected["type"] = "object";
            selected["uriVariables"] = new JsonObject { ["device"] = variable };
            selected["properties"] = new JsonObject
            {
                ["first"] = new JsonObject { ["$ref"] = "#/properties/Value/uriVariables/device" },
                ["second"] = new JsonObject { ["$ref"] = "#/properties/Value/uriVariables/device" }
            };
            source["schemaDefinitions"] = new JsonObject { ["S"] = UriVariable("source-shared") };

            WotConversionResult<WotDocument> result = await ResolveUriVariableReviewAsync(
                UriVariableReviewPlan(host: true), source.ToJsonString(),
                new WotNodeSetConverterOptions { MaxNodeCount = limit }).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (!success)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.TraversalBudgetExhausted), Is.True);
                return;
            }
            JsonElement picked = view.Properties["picked"];
            string reference = picked.GetProperty("properties").GetProperty("first").GetProperty("$ref").GetString();
            Assert.That(picked.GetProperty("properties").GetProperty("second").GetProperty("$ref").GetString(),
                Is.EqualTo(reference));
            Assert.That(WotDocument.TryEvaluatePointer(view.RootElement, reference[1..], out JsonElement carried),
                Is.True);
            Assert.That(carried.GetProperty("default").GetString(), Is.EqualTo("source-schema"));
            Assert.That(carried.GetProperty("uav:semanticId").GetString(), Is.EqualTo("native:Variable"));
            Assert.That(string.Join("|", carried.GetProperty("@context").EnumerateArray()
                    .Where(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("native", out _))
                    .Select(entry => entry.GetProperty("native").GetString())),
                Is.EqualTo("urn:root:|urn:affordance:|urn:term:|urn:own-first:|urn:own-last:"));
            string dependency = carried.GetProperty("$ref").GetString();
            Assert.That(WotDocument.TryEvaluatePointer(view.RootElement, dependency[1..], out JsonElement shared),
                Is.True);
            Assert.That(shared.GetProperty("default").GetString(), Is.EqualTo("source-shared"));
            Assert.That(view.RootElement.GetProperty("schemaDefinitions").EnumerateObject().Count(), Is.EqualTo(2));
            Assert.That(picked.GetProperty("uriVariables").GetProperty("device").GetProperty("default").GetString(),
                Is.EqualTo("host-schema"));
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public async Task SourceVariableSupportRemainsIndependentOfHostSupply(bool hostTemplate, bool collision)
        {
            JsonObject plan = UriVariableReviewPlan(host: true);
            if (!hostTemplate)
            {
                plan.Remove("uriVariables");
                plan["properties"]!["picked"]!["forms"]![0]!["href"] = "https://host.test/plain";
            }
            if (collision)
            {
                plan["schemaDefinitions"] = new JsonObject { ["device"] = UriVariable("host-definition") };
            }
            JsonObject source = UriVariableReviewSource();
            JsonObject selected = source["properties"]!["Value"]!.AsObject();
            selected.Remove("type");
            selected["uriVariables"] = new JsonObject { ["device"] = UriVariable("source-schema") };
            selected["$ref"] = "#/properties/Value/uriVariables/device";

            WotConversionResult<WotDocument> result = await ResolveUriVariableReviewAsync(
                plan, source.ToJsonString()).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement picked = view.Properties["picked"];
            string reference = picked.GetProperty("$ref").GetString();
            Assert.That(reference, Does.StartWith("#/schemaDefinitions/"));
            Assert.That(WotDocument.TryEvaluatePointer(view.RootElement, reference[1..], out JsonElement carried),
                Is.True);
            Assert.That(carried.GetProperty("default").GetString(), Is.EqualTo("source-schema"));
            Assert.That(picked.TryGetProperty("uriVariables", out JsonElement variables), Is.EqualTo(hostTemplate));
            if (hostTemplate)
            {
                Assert.That(variables.GetProperty("device").GetProperty("default").GetString(),
                    Is.EqualTo("host-schema"));
            }
            Assert.That(picked.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo(hostTemplate ? "https://host.test/{device}" : "https://host.test/plain"));
            JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
            Assert.That(definitions.EnumerateObject().Count(), Is.EqualTo(collision ? 2 : 1));
            if (collision)
            {
                Assert.That(definitions.GetProperty("device").GetProperty("default").GetString(),
                    Is.EqualTo("host-definition"));
                Assert.That(reference, Is.Not.EqualTo("#/schemaDefinitions/device"));
            }
        }

        [Test]
        public async Task AReplacedVariableContainerCannotUseItsSourceAffordanceAncestorMapping()
        {
            JsonObject source = UriVariableReviewSource();
            JsonObject selected = source["properties"]!["Value"]!.AsObject();
            selected.Remove("type");
            selected["uriVariables"] = new JsonObject { ["device"] = UriVariable("source-schema") };
            selected["$ref"] = "#/properties/Value/uriVariables";

            WotConversionResult<WotDocument> result = await ResolveUriVariableReviewAsync(
                UriVariableReviewPlan(host: true), source.ToJsonString()).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ImplicitSelectionsValidateSourceLocalDuplicateUriVariableContainers(
            bool supporting, bool equivalent)
        {
            JsonObject plan = UriVariableReviewPlan(host: false);
            JsonObject source = UriVariableReviewSource();
            source["properties"]!["Value"]!["uriVariables"] =
                new JsonObject { ["device"] = UriVariable("original") };
            if (supporting)
            {
                source["properties"]!["Value"]!["type"] = "string";
                source["properties"]!["Value"]!["const"] = "rpm";
                source["properties"]!["Reading"] = new JsonObject
                {
                    ["type"] = "number",
                    ["uav:unitProperty"] = "/properties/Value",
                    ["forms"] = Forms("https://source.test/plain")
                };
                plan["properties"]!["picked"]!["tm:ref"] = UriVariableReviewSourceHref + "#/properties/Reading";
            }
            else
            {
                plan.Remove("properties");
                plan["uav:projects"]![0]!["uav:selectAll"] = true;
            }

            WotConversionResult<WotDocument> result = await ResolveUriVariableReviewAsync(
                plan, DuplicateLocalUriVariableContainers(source, equivalent)).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(equivalent), string.Join("; ", result.Diagnostics));
            if (equivalent)
            {
                JsonElement variables = view.Properties["Value"].GetProperty("uriVariables");
                Assert.That(variables.EnumerateObject().Count(), Is.EqualTo(1));
                Assert.That(variables.GetProperty("device").GetProperty("default").GetString(), Is.EqualTo("original"));
                Assert.That(view.Properties, Has.Count.EqualTo(supporting ? 2 : 1));
                if (supporting)
                {
                    Assert.That(view.Properties["picked"].GetProperty("uav:unitProperty").GetString(),
                        Is.EqualTo("/properties/Value"));
                    Assert.That(view.Properties["Value"].GetProperty("const").GetString(), Is.EqualTo("rpm"));
                }
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostRoutingKeepsDuplicateSourceVariableFactsIndependent(bool equivalent)
        {
            JsonObject source = UriVariableReviewSource();
            JsonObject selected = source["properties"]!["Value"]!.AsObject();
            selected.Remove("type");
            selected["uriVariables"] = new JsonObject { ["device"] = UriVariable("original") };
            selected["$ref"] = "#/properties/Value/uriVariables/device";

            WotConversionResult<WotDocument> result = await ResolveUriVariableReviewAsync(
                UriVariableReviewPlan(host: true),
                DuplicateLocalUriVariableContainers(source, equivalent)).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(equivalent), string.Join("; ", result.Diagnostics));
            if (equivalent)
            {
                JsonElement picked = view.Properties["picked"];
                string reference = picked.GetProperty("$ref").GetString();
                Assert.That(WotDocument.TryEvaluatePointer(view.RootElement, reference[1..], out JsonElement carried),
                    Is.True);
                Assert.That(carried.GetProperty("default").GetString(), Is.EqualTo("original"));
                Assert.That(picked.GetProperty("uriVariables").GetProperty("device").GetProperty("default").GetString(),
                    Is.EqualTo("host-schema"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
            }
        }

        private static string DuplicateLocalUriVariableContainers(JsonObject source, bool equivalent)
        {
            const string first = "\"device\":{\"type\":\"string\",\"default\":\"original\"}";
            string second = equivalent
                ? "\"device\":{\"default\":\"original\",\"type\":\"string\"}"
                : "\"device\":{\"type\":\"string\",\"default\":\"contradiction\"}";
            string raw = source.ToJsonString();
            Assert.That(raw, Does.Contain("\"uriVariables\":{" + first + "}"));
            return raw.Replace(
                "\"uriVariables\":{" + first + "}",
                "\"uriVariables\":{" + first + "},\"uriVariables\":{" + second + "}",
                StringComparison.Ordinal);
        }

        private static async Task<WotConversionResult<WotDocument>> ResolveUriVariableReviewAsync(
            JsonObject plan, string source, WotNodeSetConverterOptions options = null)
        {
            var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
            documents.Setup(resolver => resolver.ResolveThingAsync(
                    UriVariableReviewSourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(source)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan.ToJsonString()));
            string original = document.RootElement.GetRawText();
            var resolver = new WotProjectionResolver(documents.Object, options);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.That(document.RootElement.GetRawText(), Is.EqualTo(original));
            documents.Verify(value => value.ResolveThingAsync(
                UriVariableReviewSourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            documents.VerifyNoOtherCalls();
            return result;
        }

        private static JsonObject UriVariableReviewPlan(bool host)
        {
            JsonObject plan = JsonNode.Parse("""
                {
                  "@type": ["uav:projection"],
                  "title": "Independent bounded review",
                  "uav:scenario": "urn:review:bounded",
                  "uav:projects": [{
                    "uav:sourceName": "s",
                    "href": "https://origin.test/source.td.json",
                    "type": "application/td+json"
                  }],
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "uav:projectionKind": "ThingDescription",
                  "id": "https://host.test/view.json",
                  "security": "none",
                  "securityDefinitions": {"none": {"scheme": "nosec"}},
                  "properties": {
                    "picked": {"tm:ref": "https://origin.test/source.td.json#/properties/Value"}
                  }
                }
                """)!.AsObject();
            if (host)
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["picked"]!["forms"] = new JsonArray(new JsonObject
                {
                    ["href"] = "https://host.test/{device}",
                    ["op"] = "readproperty"
                });
                plan["uriVariables"] = new JsonObject { ["device"] = UriVariable("host-schema") };
            }
            return plan;
        }

        private static JsonObject UriVariableReviewSource()
        {
            return JsonNode.Parse("""
                {
                  "@type": ["Thing"],
                  "title": "Independent source",
                  "securityDefinitions": {"auth": {"scheme": "basic"}},
                  "security": "auth",
                  "properties": {
                    "Value": {
                      "title": "properties-source",
                      "forms": [{"href": "https://source.test/read{?device}", "op": "readproperty"}],
                      "type": "integer"
                    }
                  },
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "id": "https://origin.test/source.td.json",
                  "base": "https://source.test/runtime/"
                }
                """)!.AsObject();
        }

        private const string UriVariableReviewSourceHref = "https://origin.test/source.td.json";
    }
}
