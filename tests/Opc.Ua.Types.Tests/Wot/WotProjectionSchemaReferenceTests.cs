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
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecursiveSourceSchemasAreCarriedWithoutReplacingHostDefinitions(bool hostCollision)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = "#/schemaDefinitions/Reading";
            source["schemaDefinitions"] = new JsonObject
            {
                ["Reading"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["next"] = new JsonObject { ["$ref"] = "#/schemaDefinitions/Reading" }
                    }
                }
            };
            JsonObject plan = Plan();
            const string stem = "q:d:cA:L3NjaGVtYURlZmluaXRpb25zL1JlYWRpbmc";
            if (hostCollision)
            {
                plan["schemaDefinitions"] = new JsonObject
                {
                    ["Reading"] = new JsonObject { ["type"] = "string" },
                    [stem] = new JsonObject { ["type"] = "boolean" }
                };
            }
            string original = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string name = hostCollision ? stem + ":1" : "Reading";
            JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
            Assert.That(definitions.EnumerateObject().Count(), Is.EqualTo(hostCollision ? 3 : 1));
            Assert.That(view.Properties["reading"].GetProperty("$ref").GetString(),
                Is.EqualTo("#/schemaDefinitions/" + name));
            Assert.That(definitions.GetProperty(name).GetProperty("properties").GetProperty("next")
                .GetProperty("$ref").GetString(), Is.EqualTo("#/schemaDefinitions/" + name));
            if (hostCollision)
            {
                Assert.That(definitions.GetProperty("Reading").GetProperty("type").GetString(), Is.EqualTo("string"));
                Assert.That(definitions.GetProperty(stem).GetProperty("type").GetString(), Is.EqualTo("boolean"));
            }
            Assert.That(source.ToJsonString(), Is.EqualTo(original));
        }

        [TestCase("./schemas.json#/definitions/Reading", "https://origin.test/models/schemas.json#/definitions/Reading")]
        [TestCase("../schemas.json#/definitions/Reading", "https://origin.test/schemas.json#/definitions/Reading")]
        [TestCase("https://schema.test/reading", "https://schema.test/reading")]
        public async Task ExternalSchemaReferencesUseTheOriginalDocumentLocationWithoutFetching(
            string reference, string expected)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = reference;
            source["properties"]!["Value"]!["uav:metadata"] = new JsonObject { ["$ref"] = reference };
            source["properties"]!["Value"]!["const"] = new JsonObject { ["$ref"] = reference };
            JsonObject plan = Plan();
            plan["base"] = "https://host.test/runtime/";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("$ref").GetString(), Is.EqualTo(expected));
            Assert.That(view.Properties["reading"].GetProperty("uav:metadata").GetProperty("$ref").GetString(),
                Is.EqualTo(reference));
            Assert.That(view.Properties["reading"].GetProperty("const").GetProperty("$ref").GetString(),
                Is.EqualTo(reference));
        }

        [TestCase("null")]
        [TestCase("42")]
        [TestCase("[]")]
        [TestCase("\"\"")]
        [TestCase("\"#/schemaDefinitions/Missing\"")]
        [TestCase("\"#/schemaDefinitions/invalid~2\"")]
        public async Task InvalidKnownSchemaReferencesNeverLeaveADanglingSuccessfulResult(string reference)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = JsonNode.Parse(reference);
            source["schemaDefinitions"] = new JsonObject
            {
                ["invalid~2"] = new JsonObject { ["type"] = "number" }
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public async Task ReferencesToSelectedDataSchemasUseTheirSelectedOutputLocation()
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = "#/properties/Other";
            source["properties"]!["Other"] = new JsonObject
            {
                ["type"] = "string",
                ["forms"] = Forms("other")
            };
            JsonObject plan = Plan();
            plan["properties"]!["renamed"] = new JsonObject { ["tm:ref"] = SourceHref + "#/properties/Other" };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("$ref").GetString(), Is.EqualTo("#/properties/renamed"));
            Assert.That(view.Properties["renamed"].GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(view.RootElement.TryGetProperty("schemaDefinitions", out _), Is.False);
        }

        [Test]
        public async Task NestedActionEventAndVariableSchemasShareTheCorrectLocalDefinition()
        {
            JsonObject source = Source();
            source["schemaDefinitions"] = new JsonObject
            {
                ["Reading"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["temperature"] = new JsonObject { ["type"] = "number", ["maximum"] = 42 }
                    }
                }
            };
            source["properties"]!["Value"]!["uriVariables"] = new JsonObject
            {
                ["limit"] = new JsonObject { ["$ref"] = "#/schemaDefinitions/Reading/properties/temperature" }
            };
            source["actions"] = new JsonObject
            {
                ["calculate"] = new JsonObject
                {
                    ["input"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["reading"] = new JsonObject { ["$ref"] = "#/schemaDefinitions/Reading" }
                        }
                    },
                    ["output"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["$ref"] = "#/schemaDefinitions/Reading" }
                    },
                    ["forms"] = Forms("calculate")
                }
            };
            source["events"] = new JsonObject
            {
                ["changed"] = new JsonObject
                {
                    ["data"] = new JsonObject
                    {
                        ["oneOf"] = new JsonArray(
                            new JsonObject { ["$ref"] = "#/schemaDefinitions/Reading/properties/temperature" },
                            new JsonObject { ["type"] = "null" })
                    },
                    ["tm:ref"] = "./events.tm.json#/events/Changed",
                    ["forms"] = Forms("changed")
                }
            };
            JsonObject plan = Plan();
            plan["actions"] = new JsonObject
            {
                ["calculateReading"] = new JsonObject { ["tm:ref"] = SourceHref + "#/actions/calculate" }
            };
            plan["events"] = new JsonObject
            {
                ["readingChanged"] = new JsonObject { ["tm:ref"] = SourceHref + "#/events/changed" }
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
            Assert.That(definitions.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(definitions.GetProperty("Reading").GetProperty("properties").GetProperty("temperature")
                .GetProperty("maximum").GetInt32(), Is.EqualTo(42));
            Assert.That(view.Actions["calculateReading"].GetProperty("input").GetProperty("properties")
                .GetProperty("reading").GetProperty("$ref").GetString(), Is.EqualTo("#/schemaDefinitions/Reading"));
            Assert.That(view.Actions["calculateReading"].GetProperty("output").GetProperty("items")
                .GetProperty("$ref").GetString(), Is.EqualTo("#/schemaDefinitions/Reading"));
            Assert.That(view.Events["readingChanged"].GetProperty("data").GetProperty("oneOf")[0]
                .GetProperty("$ref").GetString(), Is.EqualTo("#/schemaDefinitions/Reading/properties/temperature"));
            Assert.That(view.Events["readingChanged"].GetProperty("tm:ref").GetString(),
                Is.EqualTo("https://origin.test/models/events.tm.json#/events/Changed"));
            Assert.That(view.Properties["reading"].GetProperty("uriVariables").GetProperty("limit")
                .GetProperty("$ref").GetString(), Is.EqualTo("#/schemaDefinitions/Reading/properties/temperature"));
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase(/*lang=json,strict*/ "{\"Reading\":null}")]
        [TestCase(/*lang=json,strict*/ "{\"Reading\":false}")]
        public async Task InvalidReferencedSchemaContainersCannotBeCompletedSilently(string definitions)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = "#/schemaDefinitions/Reading";
            source["schemaDefinitions"] = JsonNode.Parse(definitions);

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase(/*lang=json,strict*/ "{\"Reading\":null}")]
        public async Task InvalidHostSchemaContainersAreNotReplacedDuringSourceCarriage(string definitions)
        {
            JsonObject plan = Plan();
            plan["schemaDefinitions"] = JsonNode.Parse(definitions);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, Source()).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
        }

        [TestCase("null")]
        [TestCase("42")]
        [TestCase("\"\"")]
        [TestCase("\"missing\"")]
        public async Task InvalidNamedResponseSchemasAreReported(string name)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["forms"]![0]!["additionalResponses"] = new JsonArray(new JsonObject
            {
                ["schema"] = JsonNode.Parse(name)
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConflictingDuplicateReusableDefinitionsCannotChooseAnArbitraryWinner(bool reversed)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = "#/schemaDefinitions/Reading";
            string first = reversed ? "string" : "number";
            string second = reversed ? "number" : "string";
            string json = source.ToJsonString();
            json = json[..^1] +
                ",\"schemaDefinitions\":{\"Reading\":{\"type\":\"" +
                first +
                "\"},\"Reading\":{\"type\":\"" +
                second +
                "\"}}}";

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan().ToJsonString(), json).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
        }

        [Test]
        public async Task ConsistentDuplicateReusableDefinitionsCanShareOneCarriedSchema()
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = "#/schemaDefinitions/Reading";
            string json = source.ToJsonString();
            json = json[..^1] +
                ",\"schemaDefinitions\":{\"Reading\":{\"type\":\"number\",\"minimum\":1.0}," +
                "\"Reading\":{\"minimum\":1e0,\"type\":\"number\"}}}";

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan().ToJsonString(), json).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
            Assert.That(definitions.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(definitions.GetProperty("Reading").GetProperty("minimum").GetDouble(), Is.EqualTo(1d));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostRepeatedDefinitionsRequireEquivalentFacts(bool equivalent)
        {
            string plan = Plan().ToJsonString();
            string second = equivalent
                ? /*lang=json,strict*/ "{\"minimum\":1e0,\"type\":\"number\"}"
                : /*lang=json,strict*/ "{\"type\":\"string\"}";
            plan = plan[..^1] +
                ",\"schemaDefinitions\":{\"Reading\":{\"type\":\"number\",\"minimum\":1.0},\"Reading\":" +
                second +
                "}}";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, Source().ToJsonString())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(equivalent), string.Join("; ", result.Diagnostics));
            if (equivalent)
            {
                JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
                Assert.That(definitions.EnumerateObject().Count(), Is.EqualTo(1));
                Assert.That(definitions.GetProperty("Reading").GetProperty("minimum").GetDouble(), Is.EqualTo(1d));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task ResponseSchemaDependenciesBelongToTheActualFormOwner(bool hostForms, bool equalLogicalId)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = "#/schemaDefinitions/Response";
            source["schemaDefinitions"] = new JsonObject
            {
                ["Response"] = new JsonObject { ["type"] = "number" }
            };
            JsonObject plan = Plan();
            if (equalLogicalId)
            {
                plan["id"] = SourceHref;
            }
            plan["schemaDefinitions"] = new JsonObject
            {
                ["Response"] = new JsonObject { ["type"] = "string" }
            };
            JsonArray forms = Forms("https://actual-form.test/reading");
            forms[0]!["additionalResponses"] = new JsonArray(new JsonObject
            {
                ["success"] = true,
                ["schema"] = "Response"
            });
            if (hostForms)
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = forms;
            }
            else
            {
                source["properties"]!["Value"]!["forms"] = forms;
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            const string sourceName = "q:d:cA:L3NjaGVtYURlZmluaXRpb25zL1Jlc3BvbnNl";
            Assert.That(view.Properties["reading"].GetProperty("$ref").GetString(),
                Is.EqualTo("#/schemaDefinitions/" + sourceName));
            Assert.That(view.Properties["reading"].GetProperty("forms")[0].GetProperty("additionalResponses")[0]
                .GetProperty("schema").GetString(), Is.EqualTo(hostForms ? "Response" : sourceName));
            JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
            Assert.That(definitions.GetProperty(sourceName).GetProperty("type").GetString(), Is.EqualTo("number"));
            Assert.That(definitions.GetProperty("Response").GetProperty("type").GetString(), Is.EqualTo("string"));
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public async Task SupportingSchemasParticipateInTheProjectionBudget(int limit, bool success)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["$ref"] = "#/schemaDefinitions/Reading";
            source["schemaDefinitions"] = new JsonObject
            {
                ["Reading"] = new JsonObject { ["type"] = "number" }
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(
                Plan(), source, new WotNodeSetConverterOptions { MaxNodeCount = limit }).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.RootElement.GetProperty("schemaDefinitions").GetProperty("Reading")
                    .GetProperty("type").GetString(), Is.EqualTo("number"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.TraversalBudgetExhausted), Is.True);
            }
        }

        private static Task<WotConversionResult<WotDocument>> ResolveAsync(
            JsonObject plan, JsonObject source, WotNodeSetConverterOptions options = null)
        {
            return ResolveAsync(plan.ToJsonString(), source.ToJsonString(), options);
        }

        private static async Task<WotConversionResult<WotDocument>> ResolveAsync(
            string plan, string source, WotNodeSetConverterOptions options = null)
        {
            var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
            documents.Setup(resolver => resolver.ResolveThingAsync(
                    SourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(source)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan));
            var resolver = new WotProjectionResolver(documents.Object, options);
            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            documents.Verify(value => value.ResolveThingAsync(
                SourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            documents.VerifyNoOtherCalls();
            return result;
        }

        private static JsonObject Plan()
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = "uav:projection",
                ["uav:projectionKind"] = "ThingDescription",
                ["title"] = "Projection",
                ["uav:scenario"] = "urn:scenario:schema",
                ["security"] = "none",
                ["securityDefinitions"] = SecurityDefinitions(),
                ["uav:projects"] = new JsonArray(new JsonObject
                {
                    ["uav:sourceName"] = "p",
                    ["href"] = SourceHref,
                    ["type"] = "application/td+json"
                }),
                ["properties"] = new JsonObject
                {
                    ["reading"] = new JsonObject { ["tm:ref"] = SourceHref + "#/properties/Value" }
                }
            };
        }

        private static JsonObject Source()
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = "Thing",
                ["id"] = SourceHref,
                ["title"] = "Source",
                ["base"] = "https://device.test/runtime/",
                ["security"] = "none",
                ["securityDefinitions"] = SecurityDefinitions(),
                ["properties"] = new JsonObject
                {
                    ["Value"] = new JsonObject { ["forms"] = Forms("value") }
                }
            };
        }

        private static JsonArray Forms(string href)
        {
            return new JsonArray(new JsonObject { ["href"] = href });
        }

        private static JsonObject SecurityDefinitions()
        {
            return new JsonObject { ["none"] = new JsonObject { ["scheme"] = "nosec" } };
        }

        private const string SourceHref = "https://origin.test/models/source.td.json";
    }
}
