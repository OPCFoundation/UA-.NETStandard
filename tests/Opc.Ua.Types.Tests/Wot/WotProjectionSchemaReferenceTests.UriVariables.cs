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
        [TestCase("value/{device}", "device")]
        [TestCase("value/{+device}", "device")]
        [TestCase("value/{#device}", "device")]
        [TestCase("value/{.device}", "device")]
        [TestCase("value/{/device}", "device")]
        [TestCase("value/{;device}", "device")]
        [TestCase("value/{?device}", "device")]
        [TestCase("value/{&device}", "device")]
        [TestCase("value/{device:3}", "device")]
        [TestCase("value/{device*}", "device")]
        [TestCase("value/{device.name}", "device.name")]
        [TestCase("value/{device%2Ename}", "device%2Ename")]
        public async Task RequiredRootUriVariablesFollowTheCarriedTemplate(string href, string name)
        {
            JsonObject source = Source();
            source["uriVariables"] = new JsonObject
            {
                [name] = UriVariable("source"),
                ["unused"] = UriVariable("unused")
            };
            source["properties"]!["Value"]!["forms"] = Forms(href);
            string original = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement variables = view.Properties["reading"].GetProperty("uriVariables");
            Assert.That(variables.EnumerateObject().Select(variable => variable.Name), Is.EqualTo([name]));
            Assert.That(variables.GetProperty(name).GetProperty("default").GetString(), Is.EqualTo("source"));
            Assert.That(view.Properties["reading"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://device.test/runtime/" + href));
            Assert.That(source.ToJsonString(), Is.EqualTo(original));
        }

        [Test]
        public async Task RequiredUriVariablesUseTheirActualFormOwner(
            [Values] bool hostForms, [Values] bool localDeclaration)
        {
            JsonObject source = Source();
            JsonObject plan = Plan();
            source["uriVariables"] = new JsonObject { ["device"] = UriVariable("source-root") };
            plan["uriVariables"] = new JsonObject { ["device"] = UriVariable("host-root") };
            source["properties"]!["Value"]!["forms"] = Forms("value/{device}");
            if (localDeclaration)
            {
                source["properties"]!["Value"]!["uriVariables"] =
                    new JsonObject { ["device"] = UriVariable("source-local") };
            }
            if (hostForms)
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = Forms("https://host.test/{device}");
            }
            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string expected = hostForms ? "host-root" : localDeclaration ? "source-local" : "source-root";
            Assert.That(view.Properties["reading"].GetProperty("uriVariables").GetProperty("device")
                .GetProperty("default").GetString(), Is.EqualTo(expected));
            Assert.That(view.RootElement.GetProperty("uriVariables").GetProperty("device")
                .GetProperty("default").GetString(), Is.EqualTo("host-root"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RequiredUriVariableSchemaReferencesRetainTheirOriginalOwner(bool hostForms)
        {
            JsonObject source = Source();
            JsonObject plan = Plan();
            source["schemaDefinitions"] = new JsonObject { ["Device"] = UriVariable("source") };
            plan["schemaDefinitions"] = new JsonObject { ["Device"] = UriVariable("host") };
            source["uriVariables"] = new JsonObject
            {
                ["device"] = new JsonObject { ["$ref"] = "#/schemaDefinitions/Device" }
            };
            plan["uriVariables"] = new JsonObject
            {
                ["device"] = new JsonObject { ["$ref"] = "#/schemaDefinitions/Device" }
            };
            source["properties"]!["Value"]!["forms"] = Forms("value/{device}");
            if (hostForms)
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = Forms("https://host.test/{device}");
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string reference = view.Properties["reading"].GetProperty("uriVariables").GetProperty("device")
                .GetProperty("$ref").GetString();
            Assert.That(reference, Does.StartWith("#/schemaDefinitions/"));
            Assert.That(WotDocument.TryEvaluatePointer(view.RootElement, reference[1..], out JsonElement definition),
                Is.True);
            Assert.That(definition.GetProperty("default").GetString(), Is.EqualTo(hostForms ? "host" : "source"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ARequiredUriVariableCannotBorrowTheOtherOwnersDeclaration(bool hostForms)
        {
            JsonObject source = Source();
            JsonObject plan = Plan();
            source["properties"]!["Value"]!["forms"] = Forms("value/{device}");
            if (hostForms)
            {
                source["uriVariables"] = new JsonObject { ["device"] = UriVariable("source-only") };
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = Forms("https://host.test/{device}");
            }
            else
            {
                plan["uriVariables"] = new JsonObject { ["device"] = UriVariable("host-only") };
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Message.Contains("device", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("value/{")]
        [TestCase("value/}")]
        [TestCase("value/{}")]
        [TestCase("value/{?}")]
        [TestCase("value/{device,,other}")]
        [TestCase("value/{device:0}")]
        [TestCase("value/{device:12345}")]
        [TestCase("value/{device:2*}")]
        [TestCase("value/{device..name}")]
        [TestCase("value/{device%Q0}")]
        [TestCase("value/{=device}")]
        public async Task InvalidUriTemplatesFailInsteadOfHidingTheirDependencies(string href)
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["forms"] = Forms(href);
            source["uriVariables"] = new JsonObject { ["device"] = UriVariable("source") };

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
        }

        [Test]
        public async Task EscapedTemplateDelimitersAndLiteralMetadataDoNotInventDependencies()
        {
            JsonObject source = Source();
            source["properties"]!["Value"]!["forms"] = Forms("value/%7Bdevice%7D");
            source["properties"]!["Value"]!["default"] = "{missing}";
            source["properties"]!["Value"]!["x-example"] = new JsonObject { ["href"] = "{missing}" };

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].TryGetProperty("uriVariables", out _), Is.False);
            Assert.That(view.Properties["reading"].GetProperty("default").GetString(), Is.EqualTo("{missing}"));
            Assert.That(view.Properties["reading"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://device.test/runtime/value/%7Bdevice%7D"));
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public async Task SupportingUriVariablesParticipateInTheProjectionBudget(int limit, bool success)
        {
            JsonObject source = Source();
            source["uriVariables"] = new JsonObject { ["device"] = UriVariable("source") };
            source["properties"]!["Value"]!["forms"] = Forms("value/{device}");

            WotConversionResult<WotDocument> result = await ResolveAsync(
                Plan(), source, new WotNodeSetConverterOptions { MaxNodeCount = limit }).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.Properties["reading"].GetProperty("uriVariables").GetProperty("device")
                    .GetProperty("default").GetString(), Is.EqualTo("source"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.TraversalBudgetExhausted), Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ProjectionAnnotationsCannotRedefineUriVariables(bool hostForms)
        {
            JsonObject plan = Plan();
            plan["properties"]!["reading"]!["uriVariables"] =
                new JsonObject { ["device"] = UriVariable("override") };
            if (hostForms)
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = Forms("https://host.test/{device}");
            }
            var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan.ToJsonString()));
            var resolver = new WotProjectionResolver(documents.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionAnnotationNotPermitted), Is.True);
            documents.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CarriedRootUriVariablesDoNotInheritTheAffordancesUnrelatedContext(bool termScoped)
        {
            JsonObject source = Source();
            var context = new JsonObject { ["native"] = termScoped ? "urn:wrong-root" : Namespaces.OpcUa };
            if (termScoped)
            {
                context["uriVariables"] = new JsonObject
                {
                    ["@id"] = "https://www.w3.org/2019/wot/td#hasUriTemplateSchema",
                    ["@container"] = "@index",
                    ["@context"] = new JsonObject { ["native"] = Namespaces.OpcUa }
                };
            }
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1", context);
            source["uriVariables"] = new JsonObject
            {
                ["device"] = new JsonObject { ["type"] = "integer", ["uav:dataTypeName"] = "native:UInt16" }
            };
            source["properties"]!["Value"]!["@context"] = new JsonObject { ["native"] = "urn:wrong-affordance" };
            source["properties"]!["Value"]!["forms"] = Forms("value/{device}");

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement schema = view.Properties["reading"].GetProperty("uriVariables").GetProperty("device");
            Assert.That(view.TryGetContextPrefix("native", out string uri, schema), Is.True);
            Assert.That(uri, Is.EqualTo(Namespaces.OpcUa));
            Assert.That(schema.GetProperty("uav:dataTypeName").GetString(), Is.EqualTo("native:UInt16"));
            Assert.That(view.TryGetContextPrefix("native", out string affordanceUri, view.Properties["reading"]),
                Is.True);
            Assert.That(affordanceUri, Is.EqualTo("urn:wrong-affordance"));
        }

        [Test]
        public async Task DuplicateUriVariableDeclarationsCannotReplaceTheirOwnersFacts(
            [Values] bool hostForms, [Values] bool containers, [Values] bool equivalent)
        {
            JsonObject source = Source();
            JsonObject plan = Plan();
            source["properties"]!["Value"]!["forms"] = Forms("value/{device}");
            if (hostForms)
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = Forms("https://host.test/{device}");
            }
            const string first = "\"device\":{\"type\":\"string\",\"default\":\"original\"}";
            string second = "\"device\":{\"type\":\"string\",\"default\":\"" +
                (equivalent ? "original" : "replaced") +
                "\"}";
            string addition = containers
                ? ",\"uriVariables\":{" + first + "},\"uriVariables\":{" + second + "}}"
                : ",\"uriVariables\":{" + first + "," + second + "}}";
            string owner = (hostForms ? plan : source).ToJsonString();
            owner = owner[..^1] + addition;

            WotConversionResult<WotDocument> result = await ResolveAsync(
                hostForms ? owner : plan.ToJsonString(),
                hostForms ? source.ToJsonString() : owner).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(equivalent), string.Join("; ", result.Diagnostics));
            if (equivalent)
            {
                JsonElement variables = view.Properties["reading"].GetProperty("uriVariables");
                Assert.That(variables.EnumerateObject().Count(), Is.EqualTo(1));
                Assert.That(variables.GetProperty("device").GetProperty("default").GetString(), Is.EqualTo("original"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
            }
        }

        [Test]
        public async Task RequiredUriVariableCarriageUsesCodePointOrderWithoutChangingTheTemplate()
        {
            JsonObject source = Source();
            source["uriVariables"] = new JsonObject { ["a"] = UriVariable("first") };
            source["properties"]!["Value"]!["uriVariables"] = new JsonObject { ["z"] = UriVariable("last") };
            source["properties"]!["Value"]!["forms"] = Forms("value{?z,a,z}");

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("uriVariables").EnumerateObject()
                .Select(variable => variable.Name), Is.EqualTo(s_uriVariableOrder));
            Assert.That(view.Properties["reading"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://device.test/runtime/value{?z,a,z}"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SameNamedUriVariablesFromDifferentSourcesRemainIndependent(bool reverse)
        {
            JsonObject source = Source();
            JsonObject other = Source();
            const string otherHref = "https://other.test/source.json";
            other["id"] = otherHref;
            source["uriVariables"] = new JsonObject { ["device"] = UriVariable("first-source") };
            other["uriVariables"] = new JsonObject { ["device"] = UriVariable("second-source") };
            source["properties"]!["Value"]!["forms"] = Forms("value/{device}");
            other["properties"]!["Value"]!["forms"] = Forms("other/{device}");
            JsonObject plan = Plan();
            var second = new JsonObject
            {
                ["uav:sourceName"] = "other",
                ["href"] = otherHref,
                ["type"] = "application/td+json"
            };
            if (reverse)
            {
                plan["uav:projects"]!.AsArray().Insert(0, second);
            }
            else
            {
                plan["uav:projects"]!.AsArray().Add(second);
            }
            plan["properties"]!["other"] = new JsonObject { ["tm:ref"] = otherHref + "#/properties/Value" };
            var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
            documents.Setup(value => value.ResolveThingAsync(
                    SourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(source.ToJsonString())));
            documents.Setup(value => value.ResolveThingAsync(
                    otherHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(other.ToJsonString())));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan.ToJsonString()));
            var resolver = new WotProjectionResolver(documents.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("uriVariables").GetProperty("device")
                .GetProperty("default").GetString(), Is.EqualTo("first-source"));
            Assert.That(view.Properties["other"].GetProperty("uriVariables").GetProperty("device")
                .GetProperty("default").GetString(), Is.EqualTo("second-source"));
            documents.Verify(value => value.ResolveThingAsync(
                SourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            documents.Verify(value => value.ResolveThingAsync(
                otherHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            documents.VerifyNoOtherCalls();
        }

        private static JsonObject UriVariable(string value)
        {
            return new JsonObject { ["type"] = "string", ["default"] = value };
        }

        private static readonly string[] s_uriVariableOrder = ["a", "z"];
    }
}
