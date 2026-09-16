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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [Test]
        public async Task BulkHostRoutingWithoutProviderCannotReturnSourceFormsOrEmptySuccess()
        {
            JsonObject plan = Plan();
            plan.Remove("properties");
            plan["uav:projects"]![0]!["uav:selectAll"] = true;
            plan["uav:projects"]![0]!["uav:routing"] = "projection";
            JsonObject source = Source();
            string originalPlan = plan.ToJsonString();
            string originalSource = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.Reference == "/properties/Value/forms"), Is.True);
            Assert.That(plan.ToJsonString(), Is.EqualTo(originalPlan));
            Assert.That(source.ToJsonString(), Is.EqualTo(originalSource));
        }

        [Test]
        public async Task BulkHostProviderSuppliesActualFormsWithOwnedSecurityVariablesAndResponseSchemas()
        {
            JsonObject plan = Plan();
            plan.Remove("properties");
            plan["id"] = "https://host.test/views/project.json";
            plan["base"] = "https://host.test/runtime/";
            plan["uav:projects"]![0]!["uav:selectAll"] = true;
            plan["uav:projects"]![0]!["uav:routing"] = "projection";
            plan["security"] = "host";
            plan["securityDefinitions"] = new JsonObject
            {
                ["host"] = new JsonObject { ["scheme"] = "apikey", ["in"] = "uri", ["name"] = "token" }
            };
            plan["uriVariables"] = new JsonObject
            {
                ["device"] = new JsonObject { ["type"] = "integer", ["minimum"] = 7, ["maximum"] = 11 }
            };
            plan["schemaDefinitions"] = new JsonObject
            {
                ["Response"] = new JsonObject { ["type"] = "boolean", ["title"] = "host" }
            };
            JsonObject source = Source();
            source["uriVariables"] = new JsonObject
            {
                ["device"] = new JsonObject { ["type"] = "integer", ["minimum"] = 101, ["maximum"] = 103 }
            };
            source["schemaDefinitions"] = new JsonObject
            {
                ["Response"] = new JsonObject { ["type"] = "string", ["title"] = "source" }
            };
            const string supplied = /*lang=json,strict*/ """
                {
                  "@context":{"formOwner":"urn:host-form:"},
                  "href":"values/{token}{?device}",
                  "additionalResponses":[{"success":true,"schema":"Response"}]
                }
                """;
            string originalPlan = plan.ToJsonString();
            string originalSource = source.ToJsonString();
            int calls = 0;
            var provider = new ProjectionFormProvider((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                calls++;
                Assert.That(request.ProjectionDocument.Id, Is.EqualTo("https://host.test/views/project.json"));
                Assert.That(request.SourceDocument.Id, Is.EqualTo(SourceHref));
                Assert.That(request.SourceLocation, Is.EqualTo(SourceHref));
                Assert.That(request.SourcePointer, Is.EqualTo("/properties/Value"));
                Assert.That(request.Name, Is.EqualTo("Value"));
                Assert.That(request.Kind, Is.EqualTo(WotAffordanceKind.Property));
                Assert.That(request.ResultKind, Is.EqualTo(WotDocumentKind.ThingDescription));
                return new ValueTask<ArrayOf<JsonElement>>([ParseHostForm(supplied)]);
            });
            var options = new WotNodeSetConverterOptions { ProjectionFormProvider = provider };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source, options).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement value = view.Properties["Value"];
            JsonElement actual = value.GetProperty("forms")[0];
            Assert.That(actual.GetProperty("href").GetString(),
                Is.EqualTo("https://host.test/runtime/values/{token}{?device}"));
            Assert.That(actual.GetProperty("security").EnumerateArray().Select(item => item.GetString()),
                Is.EqualTo(s_suppliedHostSecurity));
            Assert.That(actual.GetProperty("additionalResponses")[0].GetProperty("schema").GetString(),
                Is.EqualTo("Response"));
            Assert.That(actual.GetProperty("@context").EnumerateArray().Any(entry =>
                entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("formOwner", out JsonElement owner) &&
                owner.GetString() == "urn:host-form:"), Is.True);
            JsonElement device = value.GetProperty("uriVariables").GetProperty("device");
            Assert.That(device.GetProperty("minimum").GetInt32(), Is.EqualTo(7));
            Assert.That(device.GetProperty("maximum").GetInt32(), Is.EqualTo(11));
            Assert.That(value.GetProperty("uriVariables").TryGetProperty("token", out _), Is.False);
            Assert.That(view.RootElement.GetProperty("schemaDefinitions").GetProperty("Response")
                .GetProperty("title").GetString(), Is.EqualTo("host"));
            Assert.That(value.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Value"));
            Assert.That(plan.ToJsonString(), Is.EqualTo(originalPlan));
            Assert.That(source.ToJsonString(), Is.EqualTo(originalSource));
        }

        [TestCase(WotDocumentKind.ThingDescription, "Thing")]
        [TestCase(WotDocumentKind.ThingModel, "tm:ThingModel")]
        public async Task HostFormProviderPreservesExplicitResultKindDomainAndSourceProvenance(
            WotDocumentKind kind, string type)
        {
            JsonObject plan = HostPlan();
            plan["uav:projectionKind"] = kind.ToString();
            plan["uav:projects"]![0]!["type"] = kind == WotDocumentKind.ThingModel
                ? "application/tm+json" : "application/td+json";
            JsonObject source = Source();
            source["@type"] = type;
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["minimum"] = -2;
            source["properties"]!["Value"]!["maximum"] = 8;
            source["properties"]!["Value"]!["readOnly"] = true;
            int calls = 0;
            var provider = new ProjectionFormProvider((request, token) =>
            {
                token.ThrowIfCancellationRequested();
                calls++;
                Assert.That(request.ResultKind, Is.EqualTo(kind));
                return HostForms("https://host.test/runtime/actual-value");
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, source, new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Kind, Is.EqualTo(kind));
            Assert.That(view.RootElement.GetProperty("@type").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(new[] { type }));
            Assert.That(view.RootElement.TryGetProperty("uav:projectionKind", out _), Is.False);
            Assert.That(view.RootElement.TryGetProperty("uav:projects", out _), Is.False);
            JsonElement value = view.Properties["Value"];
            Assert.That(value.GetProperty("minimum").GetInt32(), Is.EqualTo(-2));
            Assert.That(value.GetProperty("maximum").GetInt32(), Is.EqualTo(8));
            Assert.That(value.GetProperty("readOnly").GetBoolean(), Is.True);
            Assert.That(value.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://host.test/runtime/actual-value"));
            Assert.That(value.GetProperty("forms")[0].GetProperty("security")[0].GetString(),
                Is.EqualTo("q:p:bm9uZQ"));
            Assert.That(value.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Value"));
            Assert.That(view.SecurityDefinitions.Keys, Is.EqualTo(s_defaultHostSecurity));
            TestContext.Out.WriteLine(
                $"Host-form runtime: {RuntimeInformation.FrameworkDescription}; " +
                $"version={Environment.Version}; base={AppContext.BaseDirectory}");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostFormProviderUnavailableReturnsAnExplicitErrorWithoutAValue(bool nullResult)
        {
            int calls = 0;
            var provider = new ProjectionFormProvider((_, _) =>
            {
                calls++;
                ArrayOf<JsonElement> forms = nullResult ? default : [];
                return new ValueTask<ArrayOf<JsonElement>>(forms);
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                HostPlan(), Source(), new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(1));
            AssertHostFormFailure(result);
        }

        [TestCase("io")]
        [TestCase("operation")]
        [TestCase("timeout")]
        public async Task HostFormProviderOperationalFailureReturnsAnExplicitErrorWithoutAValue(string failure)
        {
            int calls = 0;
            var provider = new ProjectionFormProvider((_, _) =>
            {
                calls++;
                throw failure switch
                {
                    "io" => new IOException("Host form acquisition failed."),
                    "operation" => new InvalidOperationException("Host form service is unavailable."),
                    "timeout" => new TimeoutException("Host form acquisition timed out."),
                    _ => new ArgumentOutOfRangeException(nameof(failure))
                };
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                HostPlan(), Source(), new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(1));
            AssertHostFormFailure(result);
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase(/*lang=json,strict*/ "{\"href\":42}")]
        [TestCase(/*lang=json,strict*/ "{\"href\":\"https://host.test/runtime/value\",\"additionalResponses\":42}")]
        [TestCase(/*lang=json,strict*/
            "{\"href\":\"https://host.test/runtime/value\",\"additionalResponses\":[{\"schema\":\"Missing\"}]}")]
        public async Task HostFormProviderMalformedFormsCannotProduceAPartialSuccessfulView(string json)
        {
            int calls = 0;
            var provider = new ProjectionFormProvider((_, _) =>
            {
                calls++;
                return new ValueTask<ArrayOf<JsonElement>>([ParseHostForm(json)]);
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                HostPlan(), Source(), new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [TestCase("https://device.test/runtime/value")]
        [TestCase("https://unrelated.test/runtime/value")]
        public async Task HostFormProviderCannotSendHostCredentialsToAnotherOrigin(string href)
        {
            JsonObject plan = HostPlan();
            plan["security"] = "host";
            plan["securityDefinitions"] = new JsonObject
            {
                ["host"] = new JsonObject { ["scheme"] = "basic" }
            };
            int calls = 0;
            var provider = new ProjectionFormProvider((_, _) =>
            {
                calls++;
                return HostForms(href);
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, Source(), new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(1));
            AssertHostFormFailure(result);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostFormProviderCancellationNeverReturnsAPartialView(bool providerThrows)
        {
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            var provider = new ProjectionFormProvider((_, token) =>
            {
                calls++;
                Assert.That(token, Is.EqualTo(cancellation.Token));
                cancellation.Cancel();
                if (providerThrows)
                {
                    token.ThrowIfCancellationRequested();
                }
                return HostForms("https://host.test/runtime/value");
            });
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(HostPlan().ToJsonString()));
            var resolver = new WotProjectionResolver(
                new HostFormSourceResolver(Source().ToJsonString()),
                new WotNodeSetConverterOptions { ProjectionFormProvider = provider });

            await Assert.ThatAsync(async () =>
            {
                WotConversionResult<WotDocument> result = await resolver.ResolveAsync(
                    document, cancellationToken: cancellation.Token).ConfigureAwait(false);
                using WotDocument view = result.Value;
            }, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task HostFormProviderRejectsOwnerDataAndCredentialVariableConflicts()
        {
            JsonObject plan = HostPlan();
            plan["security"] = "host";
            plan["securityDefinitions"] = new JsonObject
            {
                ["host"] = new JsonObject { ["scheme"] = "apikey", ["in"] = "uri", ["name"] = "token" }
            };
            plan["uriVariables"] = new JsonObject
            {
                ["token"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 }
            };
            int calls = 0;
            var provider = new ProjectionFormProvider((_, _) =>
            {
                calls++;
                return HostForms("https://host.test/runtime/value/{token}");
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, Source(), new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Location?.Reference == "/properties/Value/forms/0/href"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostFormProviderNeverReplacesAuthoredFormsEvenWhenTheyAreUnavailable(bool emptyForms)
        {
            JsonObject plan = HostPlan(bulk: false);
            plan["properties"]!["reading"]!["forms"] = emptyForms
                ? new JsonArray() : Forms("https://host.test/runtime/authored");
            int calls = 0;
            var provider = new ProjectionFormProvider((_, _) =>
            {
                calls++;
                return HostForms("https://host.test/runtime/not-authored");
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, Source(), new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.Zero);
            Assert.That(result.Success, Is.EqualTo(!emptyForms), string.Join("; ", result.Diagnostics));
            if (emptyForms)
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            }
            else
            {
                Assert.That(view.Properties["reading"].GetProperty("forms")[0].GetProperty("href").GetString(),
                    Is.EqualTo("https://host.test/runtime/authored"));
            }
        }

        [Test]
        public async Task HostFormProviderPreservesBulkSelectionAndSuppliedFormOrder()
        {
            JsonObject source = Source();
            source["properties"]!["Alpha"] = new JsonObject { ["forms"] = Forms("alpha") };
            source["actions"] = new JsonObject
            {
                ["Operate"] = new JsonObject { ["forms"] = Forms("operate") }
            };
            source["events"] = new JsonObject
            {
                ["State"] = new JsonObject { ["forms"] = Forms("state") }
            };
            var requested = new List<string>();
            var provider = new ProjectionFormProvider((request, _) =>
            {
                requested.Add(request.Name);
                return HostForms(
                    "https://host.test/runtime/primary/" + request.Name,
                    "https://host.test/runtime/secondary/" + request.Name);
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                HostPlan(), source, new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(requested, Is.EqualTo(s_hostRequestOrder));
            Assert.That(view.Properties.Keys, Is.EqualTo(s_hostPropertyOrder));
            Assert.That(view.Actions.Keys, Is.EqualTo(s_hostActionOrder));
            Assert.That(view.Events.Keys, Is.EqualTo(s_hostEventOrder));
            Assert.That(view.Properties["Value"].GetProperty("forms").EnumerateArray()
                .Select(form => form.GetProperty("href").GetString()), Is.EqualTo(s_hostFormOrder));
        }

        [Test]
        public async Task HostFormProviderSuppliesSupportingFormsWithoutReplacingAuthoredSelections()
        {
            JsonObject plan = HostPlan(bulk: false);
            plan["properties"]!["reading"]!["tm:ref"] = SourceHref + "#/properties/Reading";
            plan["properties"]!["reading"]!["forms"] = Forms("https://host.test/runtime/reading");
            JsonObject source = Source();
            source["properties"]!["Value"]!["type"] = "string";
            source["properties"]!["Value"]!["const"] = "rpm";
            source["properties"]!["Reading"] = new JsonObject
            {
                ["type"] = "number",
                ["uav:unitProperty"] = "/properties/Value",
                ["forms"] = Forms("reading")
            };
            var requested = new List<string>();
            var provider = new ProjectionFormProvider((request, _) =>
            {
                requested.Add(request.SourcePointer);
                return HostForms("https://host.test/runtime/units");
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, source, new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(requested, Is.EqualTo(s_hostSupportingPointers));
            Assert.That(view.Properties.Keys, Is.EqualTo(s_hostSupportingOrder));
            Assert.That(view.Properties["reading"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/Value"));
            Assert.That(view.Properties["reading"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://host.test/runtime/reading"));
            Assert.That(view.Properties["Value"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://host.test/runtime/units"));
            Assert.That(view.Properties["Value"].GetProperty("const").GetString(), Is.EqualTo("rpm"));
            Assert.That(view.Properties["Value"].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Value"));
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public async Task HostFormProviderSharesDocumentAndByteBudgets(bool documentLimit, int expectedCalls)
        {
            int calls = 0;
            var provider = new ProjectionFormProvider((_, _) =>
            {
                calls++;
                JsonElement form = ParseHostForm(new JsonObject
                {
                    ["href"] = "https://host.test/runtime/value",
                    ["description"] = new string('x', 2048)
                }.ToJsonString());
                return new ValueTask<ArrayOf<JsonElement>>([form]);
            });
            var options = new WotNodeSetConverterOptions { ProjectionFormProvider = provider };
            if (documentLimit)
            {
                options.MaxResolverDocuments = 1;
            }
            else
            {
                options.MaxResolverDocumentBytes = 1024;
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(HostPlan(), Source(), options)
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(calls, Is.EqualTo(expectedCalls));
            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        private static JsonObject HostPlan(bool bulk = true)
        {
            JsonObject plan = Plan();
            plan["id"] = "https://host.test/views/provider.json";
            plan["base"] = "https://host.test/runtime/";
            plan["uav:projects"]![0]!["uav:routing"] = "projection";
            if (bulk)
            {
                plan.Remove("properties");
                plan["uav:projects"]![0]!["uav:selectAll"] = true;
            }
            return plan;
        }

        private static ValueTask<ArrayOf<JsonElement>> HostForms(params string[] hrefs)
        {
            var forms = new JsonElement[hrefs.Length];
            for (int index = 0; index < hrefs.Length; index++)
            {
                using JsonDocument document = JsonDocument.Parse(
                    new JsonObject { ["href"] = hrefs[index] }.ToJsonString());
                forms[index] = document.RootElement.Clone();
            }
            return new ValueTask<ArrayOf<JsonElement>>(forms);
        }

        private static JsonElement ParseHostForm(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static void AssertHostFormFailure(WotConversionResult<WotDocument> result)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.Reference == "/properties/Value/forms"), Is.True);
        }

        private sealed class HostFormSourceResolver(string source) : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string href, WotResolutionContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(href, Is.EqualTo(SourceHref));
                return new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(source)));
            }
        }

        private sealed class ProjectionFormProvider(
            Func<WotProjectionFormContext, CancellationToken, ValueTask<ArrayOf<JsonElement>>> handler)
            : IWotProjectionFormProvider
        {
            public ValueTask<ArrayOf<JsonElement>> GetFormsAsync(
                WotProjectionFormContext context, CancellationToken cancellationToken = default)
            {
                return handler(context, cancellationToken);
            }
        }

        private static readonly string[] s_suppliedHostSecurity = ["q:p:aG9zdA"];
        private static readonly string[] s_defaultHostSecurity = ["q:p:bm9uZQ"];
        private static readonly string[] s_hostRequestOrder = ["Alpha", "Value", "Operate", "State"];
        private static readonly string[] s_hostPropertyOrder = ["Alpha", "Value"];
        private static readonly string[] s_hostActionOrder = ["Operate"];
        private static readonly string[] s_hostEventOrder = ["State"];
        private static readonly string[] s_hostFormOrder =
        [
            "https://host.test/runtime/primary/Value", "https://host.test/runtime/secondary/Value"
        ];
        private static readonly string[] s_hostSupportingPointers = ["/properties/Value"];
        private static readonly string[] s_hostSupportingOrder = ["reading", "Value"];
    }
}
