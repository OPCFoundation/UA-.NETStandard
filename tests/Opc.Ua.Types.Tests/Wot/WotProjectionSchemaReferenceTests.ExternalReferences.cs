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
 *
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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [Test]
        public async Task ExternalBindingEventClosureUsesOriginalLocationsAcrossPublicServices()
        {
            JsonObject source = ExternalEventSource("./events/derived.json");
            using WotDocument view = await ProjectExternalEventAsync(source).ConfigureAwait(false);
            var requests = new List<string>();
            var provider = new ExternalThingProvider((reference, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                requests.Add(reference);
                return ExternalAnswer(reference switch
                {
                    "https://origin.test/models/events/derived.json" =>
                        """
                        {
                          "id":"https://host.test/not-the-retrieval-location.json",
                          "base":"https://runtime.test/not-the-document-base/",
                          "tm:ref":"../base.json"
                        }
                        """,
                    "https://origin.test/models/base.json" => ExternalEventDefinition,
                    _ => throw new AssertionException("Unexpected document acquisition: " + reference)
                });
            });

            WotConversionResult<WotEventSelectionCatalog> result = await new WotEventSelectionResolver(provider)
                .ResolveAsync(view).ConfigureAwait(false);

            Assert.That(view.Events["projectedAlarm"].GetProperty("tm:ref").GetString(),
                Is.EqualTo("https://origin.test/models/events/derived.json"));
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertExternalSelection(result.Value);
            Assert.That(requests, Is.EqualTo(s_externalLocationChain));
            Assert.That(view.Events["projectedAlarm"].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/events/alarm"));
        }

        [Test]
        public async Task ExternalBindingLogicalEventIdentityIsExpandedInItsOriginalOwner()
        {
            JsonObject source = ExternalEventSource("evt:Alarm");
            source["events"]!["alarm"]!["@context"] = new JsonObject { ["evt"] = "urn:source:event:" };
            JsonObject plan = ExternalEventPlan();
            plan["@context"] = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["evt"] = "urn:host:event:" });
            WotConversionResult<WotDocument> projected = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            var requests = new List<string>();
            var provider = new ExternalThingProvider((reference, _, _) =>
            {
                requests.Add(reference);
                Assert.That(reference, Is.EqualTo("urn:source:event:Alarm"));
                return ExternalAnswer(ExternalEventDefinition);
            });

            Assert.That(view.Events["projectedAlarm"].GetProperty("tm:ref").GetString(),
                Is.EqualTo("urn:source:event:Alarm"));
            WotConversionResult<WotEventSelectionCatalog> result = await new WotEventSelectionResolver(provider)
                .ResolveAsync(view).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertExternalSelection(result.Value);
            Assert.That(requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExternalBindingExplicitClauseLocationIsRelocatedBeforeMovement()
        {
            JsonObject source = ExternalEventSource(null);
            source["events"]!["alarm"]!["uav:eventSelectClauses"] = new JsonArray(new JsonObject
            {
                ["tm:ref"] = "./event.json",
                ["uav:browsePath"] = "Temperature"
            });
            using WotDocument view = await ProjectExternalEventAsync(source).ConfigureAwait(false);
            int calls = 0;
            var provider = new ExternalThingProvider((reference, _, _) =>
            {
                calls++;
                Assert.That(reference, Is.EqualTo("https://origin.test/models/event.json"));
                return ExternalAnswer(ExternalEventDefinition);
            });

            Assert.That(view.Events["projectedAlarm"].GetProperty("uav:eventSelectClauses")[0]
                .GetProperty("tm:ref").GetString(), Is.EqualTo("https://origin.test/models/event.json"));
            WotConversionResult<WotEventSelectionCatalog> result = await new WotEventSelectionResolver(provider)
                .ResolveAsync(view).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertExternalSelection(result.Value);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ExternalBindingMappedLocalEventTypeIsResolvedWithoutRefetchingTheSource()
        {
            JsonObject source = ExternalEventSource(null);
            source["events"]!["alarm"]!["uav:eventSelectClauses"] = new JsonArray(new JsonObject
            {
                ["tm:ref"] = SourceHref + "#/schemaDefinitions/EventType",
                ["uav:browsePath"] = "Temperature"
            });
            source["schemaDefinitions"] = new JsonObject
            {
                ["EventType"] = JsonNode.Parse(ExternalEventDefinition)
            };
            using WotDocument view = await ProjectExternalEventAsync(source).ConfigureAwait(false);
            int calls = 0;
            var provider = new ExternalThingProvider((_, _, _) =>
            {
                calls++;
                throw new AssertionException("A carried local EventType must not be fetched again.");
            });

            Assert.That(view.Events["projectedAlarm"].GetProperty("uav:eventSelectClauses")[0]
                .GetProperty("tm:ref").GetString(), Is.EqualTo("#/schemaDefinitions/EventType"));
            WotConversionResult<WotEventSelectionCatalog> result = await new WotEventSelectionResolver(provider)
                .ResolveAsync(view).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertExternalSelection(result.Value);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public async Task ExternalBindingUnavailableRequiredEventDoesNotProduceASelection()
        {
            using WotDocument view = await ProjectExternalEventAsync(ExternalEventSource("./missing.json"))
                .ConfigureAwait(false);
            int calls = 0;
            var provider = new ExternalThingProvider((reference, _, _) =>
            {
                calls++;
                Assert.That(reference, Is.EqualTo("https://origin.test/models/missing.json"));
                return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
            });

            WotConversionResult<WotEventSelectionCatalog> result = await new WotEventSelectionResolver(provider)
                .ResolveAsync(view).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.EventSelectClauseInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ExternalBindingConfiguredPolicyDenialCannotBecomeSuccessfulClosure()
        {
            using WotDocument view = await ProjectExternalEventAsync(ExternalEventSource("./denied.json"))
                .ConfigureAwait(false);
            int calls = 0;
            var provider = new ExternalThingProvider((reference, _, _) =>
            {
                calls++;
                Assert.That(reference, Is.EqualTo("https://origin.test/models/denied.json"));
                throw new UnauthorizedAccessException("Denied by the caller's configured provider.");
            });
            var resolver = new WotEventSelectionResolver(provider);

            await Assert.ThatAsync(async () =>
                await resolver.ResolveAsync(view).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ExternalBindingRequiredEventCycleIsReportedWithoutAPartialSelection()
        {
            using WotDocument view = await ProjectExternalEventAsync(ExternalEventSource("./a.json"))
                .ConfigureAwait(false);
            var requests = new List<string>();
            var provider = new ExternalThingProvider((reference, _, _) =>
            {
                requests.Add(reference);
                return ExternalAnswer(reference switch
                {
                    "https://origin.test/models/a.json" => """{"tm:ref":"./b.json"}""",
                    "https://origin.test/models/b.json" => """{"tm:ref":"./a.json"}""",
                    _ => throw new AssertionException("Unexpected cycle acquisition: " + reference)
                });
            });

            WotConversionResult<WotEventSelectionCatalog> result = await new WotEventSelectionResolver(provider)
                .ResolveAsync(view).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("acyclic", StringComparison.Ordinal)), Is.True);
            Assert.That(requests, Is.EqualTo(s_externalCycleLocations));
        }

        [Test]
        public async Task ExternalBindingEventCancellationRetainsTheCallerToken()
        {
            using WotDocument view = await ProjectExternalEventAsync(ExternalEventSource("./event.json"))
                .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            var provider = new ExternalThingProvider((_, _, token) =>
            {
                calls++;
                Assert.That(token, Is.EqualTo(cancellation.Token));
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return ExternalAnswer(ExternalEventDefinition);
            });
            var resolver = new WotEventSelectionResolver(provider);

            await Assert.ThatAsync(async () =>
                await resolver.ResolveAsync(view, cancellationToken: cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalBindingSupportingSchemaRetainsOriginAndCanonicalDataType(bool compactIdentity)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:mapToType"] = "i=11";
            source["properties"]!["Value"]!["uav:externalSchema"] = compactIdentity
                ? "schema:reading.json" : "./schemas/reading.json";
            source["properties"]!["Value"]!["@context"] = new JsonObject
            {
                ["schema"] = "https://origin.test/models/schemas/"
            };
            WotConversionResult<WotDocument> projected = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            int calls = 0;
            var provider = new ExternalSchemaProvider((reference, _, _) =>
            {
                calls++;
                Assert.That(reference, Is.EqualTo("https://origin.test/models/schemas/reading.json"));
                return ExternalAnswer("""{"type":"number","uav:mapToType":"i=11"}""");
            });

            Assert.That(view.Properties["reading"].GetProperty("uav:externalSchema").GetString(),
                Is.EqualTo("https://origin.test/models/schemas/reading.json"));
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                view, null, null, null, null, new WotExternalSchemaResolver(provider)).ConfigureAwait(false);

            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ExternalSchemaIncompatible ||
                diagnostic.Code == WotDiagnosticCode.ExternalSchemaUnresolved), Is.False);
            Assert.That(result.Value.Items.OfType<UAVariable>().Any(variable => variable.DataType == "i=11"), Is.True);
            Assert.That(view.Properties["reading"].GetProperty("type").GetString(), Is.EqualTo("number"));
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalBindingSupportingSchemaDistinguishesUnevaluatedAndUnavailable(bool configured)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:externalSchema"] = "./schema.json";
            WotConversionResult<WotDocument> projected = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            int calls = 0;
            var provider = new ExternalSchemaProvider((_, _, _) =>
            {
                calls++;
                return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
            });
            var resolver = configured ? new WotExternalSchemaResolver(provider) : new WotExternalSchemaResolver();

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                view.Properties["reading"].GetProperty("uav:externalSchema").GetString(),
                view.Properties["reading"], "i=11", new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(configured
                ? WotExternalSchemaOutcome.Unresolved : WotExternalSchemaOutcome.NotEvaluated));
            Assert.That(result.Reference, Is.EqualTo("https://origin.test/models/schema.json"));
            Assert.That(calls, Is.EqualTo(configured ? 1 : 0));
            Assert.That(view.Properties["reading"].GetProperty("type").GetString(), Is.EqualTo("number"));
        }

        [Test]
        public async Task ExternalBindingIncompatibleSupportingSchemaCannotRedefineCanonicalData()
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:mapToType"] = "i=11";
            source["properties"]!["Value"]!["uav:externalSchema"] = "./schema.json";
            WotConversionResult<WotDocument> projected = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            var provider = new ExternalSchemaProvider((_, _, _) =>
                ExternalAnswer("""{"type":"string","uav:mapToType":"i=12"}"""));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                view, null, null, null, null, new WotExternalSchemaResolver(provider)).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ExternalSchemaIncompatible &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            Assert.That(result.Value.Items.OfType<UAVariable>().Any(variable => variable.DataType == "i=11"), Is.True);
            Assert.That(view.Properties["reading"].GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=11"));
        }

        [Test]
        public async Task ExternalBindingUnselectedOpaqueAndLiteralReferencesNeverAcquireDocuments()
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "object";
            const string lookalikes = """
                {"tm:ref":"https://do-not-fetch.test/event.json","uav:externalSchema":"https://do-not-fetch.test/schema.json"}
                """;
            source["properties"]!["Value"]!["const"] = JsonNode.Parse(lookalikes);
            source["properties"]!["Value"]!["default"] = JsonNode.Parse(lookalikes);
            source["properties"]!["Value"]!["enum"] = new JsonArray(JsonNode.Parse(lookalikes));
            source["properties"]!["Value"]!["examples"] = new JsonArray(JsonNode.Parse(lookalikes));
            source["properties"]!["Value"]!["uav:metadata"] = JsonNode.Parse(lookalikes);
            source["properties"]!["Ignored"] = new JsonObject
            {
                ["type"] = "number",
                ["uav:externalSchema"] = "https://do-not-fetch.test/ignored.json",
                ["forms"] = Forms("ignored")
            };
            source["events"] = new JsonObject
            {
                ["ignored"] = new JsonObject { ["tm:ref"] = "https://do-not-fetch.test/ignored-event.json" }
            };
            WotConversionResult<WotDocument> projected = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            int calls = 0;
            var things = new ExternalThingProvider((_, _, _) =>
            {
                calls++;
                throw new AssertionException("An out-of-scope document was requested.");
            });
            var schemas = new ExternalSchemaProvider((_, _, _) =>
            {
                calls++;
                throw new AssertionException("An out-of-scope schema was requested.");
            });

            WotConversionResult<WotEventSelectionCatalog> selections = await new WotEventSelectionResolver(things)
                .ResolveAsync(view).ConfigureAwait(false);
            WotConversionResult<UANodeSet> converted = await WotNodeSetConverter.ToNodeSetResultAsync(
                view, null, null, null, null, new WotExternalSchemaResolver(schemas)).ConfigureAwait(false);

            Assert.That(selections.Success, Is.True, string.Join("; ", selections.Diagnostics));
            Assert.That(selections.Value.IsEmpty, Is.True);
            Assert.That(converted.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ExternalSchemaUnresolved), Is.False);
            Assert.That(view.Properties.ContainsKey("Ignored"), Is.False);
            Assert.That(view.Events, Is.Empty);
            Assert.That(view.Properties["reading"].GetProperty("const").GetProperty("tm:ref").GetString(),
                Is.EqualTo("https://do-not-fetch.test/event.json"));
            Assert.That(view.Properties["reading"].GetProperty("uav:metadata").GetProperty("uav:externalSchema")
                .GetString(), Is.EqualTo("https://do-not-fetch.test/schema.json"));
            Assert.That(calls, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalBindingSourceDigestPinsExactBytesBeforeAnyReferenceConsumer(bool sameBytes)
        {
            JsonObject source = ExternalEventSource("./event.json");
            string original = source.ToJsonString();
            string supplied = sameBytes ? original : "\n" + original;
            JsonObject plan = ExternalEventPlan();
            plan["uav:projects"]![0]!["uav:sourceDigest"] = "sha-256:" + ExternalSha256(original);

            WotConversionResult<WotDocument> result = await ResolveAsync(plan.ToJsonString(), supplied)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(sameBytes), string.Join("; ", result.Diagnostics));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionDigestMismatch), Is.EqualTo(!sameBytes));
            if (!sameBytes)
            {
                Assert.That(view, Is.Null);
            }
            else
            {
                Assert.That(view.Events["projectedAlarm"].GetProperty("tm:ref").GetString(),
                    Is.EqualTo("https://origin.test/models/event.json"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalBindingExecutableSourceTdCannotHaveMissingOrEmptyForms(bool emptyForms)
        {
            JsonObject source = Source();
            if (emptyForms)
            {
                source["properties"]!["Value"]!["forms"] = new JsonArray();
            }
            else
            {
                source["properties"]!["Value"]!.AsObject().Remove("forms");
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.Reference == "/properties/reading/forms"), Is.True);
        }

        [Test]
        public async Task ExternalBindingStaticSourcePropertyDoesNotRequireAnExecutableForm()
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "string";
            source["properties"]!["Value"]!["const"] = "rpm";
            source["properties"]!["Value"]!.AsObject().Remove("forms");

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("const").GetString(), Is.EqualTo("rpm"));
            Assert.That(view.Properties["reading"].TryGetProperty("forms", out _), Is.False);
            Assert.That(view.Properties["reading"].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Value"));
        }

        [Test]
        public async Task ExternalBindingSupportingUnitFactsDoNotBecomeExecutableSelections()
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:unitProperty"] = "/properties/Unit";
            source["properties"]!["Unit"] = new JsonObject
            {
                ["type"] = "string",
                ["readOnly"] = true,
                ["uav:engineeringUnits"] = new JsonObject
                {
                    ["displayName"] = "Pa",
                    ["namespaceUri"] = "http://www.opcfoundation.org/UA/units/un/cefact",
                    ["unitId"] = 5259596
                }
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/Unit"));
            Assert.That(view.Properties["reading"].GetProperty("forms").GetArrayLength(), Is.EqualTo(1));
            Assert.That(view.Properties["Unit"].TryGetProperty("forms", out _), Is.False);
            Assert.That(view.Properties["Unit"].TryGetProperty("const", out _), Is.False);
            Assert.That(view.Properties["Unit"].GetProperty("uav:engineeringUnits").GetProperty("unitId").GetInt32(),
                Is.EqualTo(5259596));
        }

        [Test]
        public async Task ExternalBindingAbstractSourceTmMayRetainAnAffordanceWithoutForms()
        {
            JsonObject source = Source();
            source["@type"] = "tm:ThingModel";
            source["properties"]!["Value"]!.AsObject().Remove("forms");
            JsonObject plan = Plan();
            plan["uav:projectionKind"] = "ThingModel";
            plan["uav:projects"]![0]!["type"] = "application/tm+json";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Kind, Is.EqualTo(WotDocumentKind.ThingModel));
            Assert.That(view.Properties["reading"].TryGetProperty("forms", out _), Is.False);
        }

        private static JsonObject ExternalEventPlan()
        {
            JsonObject plan = Plan();
            plan.Remove("properties");
            plan["id"] = "https://host.test/views/plan.json";
            plan["base"] = "https://host.test/runtime/";
            plan["events"] = new JsonObject
            {
                ["projectedAlarm"] = new JsonObject { ["tm:ref"] = SourceHref + "#/events/alarm" }
            };
            return plan;
        }

        private static JsonObject ExternalEventSource(string reference)
        {
            JsonObject source = Source();
            source.Remove("properties");
            var alarm = new JsonObject { ["forms"] = Forms("events") };
            if (reference is not null)
            {
                alarm["tm:ref"] = reference;
            }
            source["events"] = new JsonObject { ["alarm"] = alarm };
            return source;
        }

        private static async Task<WotDocument> ProjectExternalEventAsync(JsonObject source)
        {
            WotConversionResult<WotDocument> result = await ResolveAsync(ExternalEventPlan(), source)
                .ConfigureAwait(false);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            return result.Value;
        }

        private static void AssertExternalSelection(WotEventSelectionCatalog catalog)
        {
            Assert.That(catalog.TryGetSelection("projectedAlarm", out ArrayOf<WotResolvedEventSelectClause> clauses),
                Is.True);
            Assert.That(clauses, Has.Count.EqualTo(1));
            Assert.That(clauses[0].BrowsePath, Is.EqualTo("Temperature"));
            Assert.That(clauses[0].TypeDefinitionId, Is.EqualTo("nsu=urn:external:types;i=7001"));
        }

        private static ValueTask<WotResolverResult> ExternalAnswer(string json)
        {
            return new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json)));
        }

        private static string ExternalSha256(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
#if NET6_0_OR_GREATER
            byte[] hash = SHA256.HashData(bytes);
#else
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(bytes);
            }
#endif
            return string.Concat(hash.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private sealed class ExternalThingProvider(
            Func<string, WotResolutionContext, CancellationToken, ValueTask<WotResolverResult>> handler)
            : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference, WotResolutionContext context, CancellationToken cancellationToken = default)
            {
                return handler(reference, context, cancellationToken);
            }
        }

        private sealed class ExternalSchemaProvider(
            Func<string, WotResolutionContext, CancellationToken, ValueTask<WotResolverResult>> handler)
            : IWotSchemaResolver
        {
            public ValueTask<WotResolverResult> ResolveSchemaAsync(
                string reference, WotResolutionContext context, CancellationToken cancellationToken = default)
            {
                return handler(reference, context, cancellationToken);
            }
        }

        private const string ExternalEventDefinition = """
            {
              "@context":"https://www.w3.org/2022/wot/td/v1.1",
              "@type":["tm:ThingModel","uav:eventType"],
              "@id":"urn:source:event:Alarm","uav:id":"nsu=urn:external:types;i=7001",
              "title":"Alarm type",
              "data":{
                "type":"object","uav:fieldOrder":["Temperature"],
                "properties":{"Temperature":{"type":"number"}}
              }
            }
            """;
        private static readonly string[] s_externalLocationChain =
        [
            "https://origin.test/models/events/derived.json", "https://origin.test/models/base.json"
        ];
        private static readonly string[] s_externalCycleLocations =
        [
            "https://origin.test/models/a.json", "https://origin.test/models/b.json"
        ];
    }
}
