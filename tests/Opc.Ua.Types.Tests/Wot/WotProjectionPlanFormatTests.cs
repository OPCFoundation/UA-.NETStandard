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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT projection plan format.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionPlanFormatTests
    {
        /// <summary>
        /// Verifies that non object JSON is not a plan and never resolves sources.
        /// </summary>
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("42")]
        [TestCase("true")]
        [TestCase("\"uav:projection\"")]
        public async Task NonObjectJsonIsNotAPlanAndNeverResolvesSources(string json)
        {
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            var diagnostics = new List<WotDiagnostic>();
            var sources = new Mock<IWotThingResolver>(MockBehavior.Strict);
            var resolver = new WotProjectionResolver(sources.Object);

            Assert.That(WotProjection.IsProjection(document), Is.False);
            Assert.That(WotProjection.Parse(document, diagnostics), Is.Null);
            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ValidationError &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            sources.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Verifies that resolved projection has the declared output kind and no plan control.
        /// </summary>
        /// <param name="expectedType">The expected type asserted by the test.</param>
        [TestCase("ThingDescription", "Thing")]
        [TestCase("ThingModel", "tm:ThingModel")]
        public async Task ResolvedProjectionHasTheDeclaredOutputKindAndNoPlanControl(string kind, string expectedType)
        {
            JsonObject root = Plan(kind);
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(SourceJson)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument? resolved = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(resolved.Required().RootElement.GetProperty("@type").EnumerateArray()
                .Select(value => value.GetString()), Does.Contain(expectedType));
            Assert.That(resolved.Kind, Is.EqualTo(kind == "ThingModel"
                ? WotDocumentKind.ThingModel : WotDocumentKind.ThingDescription));
            Assert.That(resolved.RootElement.TryGetProperty("uav:projectionKind", out _), Is.False);
            Assert.That(WotProjection.IsProjection(resolved), Is.False);
            Assert.That(resolved.Properties["value"].GetProperty("forms").GetArrayLength(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("@type").GetArrayLength(), Is.EqualTo(2));
            Assert.That(WotProjection.IsProjection(document), Is.True);
        }

        /// <summary>
        /// Verifies that a plan requires exactly one ordinary TD/TM root role.
        /// </summary>
        [TestCase("[\"uav:projection\"]")]
        [TestCase("[\"uav:projection\",\"Thing\",\"tm:ThingModel\"]")]
        [TestCase("[\"uav:projection\",null]")]
        [TestCase("[\"uav:projection\",\"Thing\",42]")]
        public void APlanRequiresExactlyOneOrdinaryRootRole(string json)
        {
            JsonObject root = Plan("ThingDescription");
            root["@type"] = JsonNode.Parse(json);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(value => value.Severity == WotDiagnosticSeverity.Error &&
                value.Message.Contains("@type", StringComparison.Ordinal)), Is.True);
        }

        /// <summary>
        /// Verifies that the old result-kind term is rejected unless compatibility is explicit.
        /// </summary>
        [Test]
        public void ACurrentPlanRejectsTheLegacyProjectionKindTerm()
        {
            JsonObject root = Plan("ThingDescription");
            root["uav:projectionKind"] = "ThingDescription";
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(value => value.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        /// <summary>
        /// Verifies that the ordinary root role determines the result kind.
        /// </summary>
        [TestCase("Thing", WotDocumentKind.ThingDescription)]
        [TestCase("tm:ThingModel", WotDocumentKind.ThingModel)]
        public void OrdinaryRootRoleDeterminesTheResultKind(string role, WotDocumentKind expected)
        {
            JsonObject root = Plan(expected == WotDocumentKind.ThingModel ? "ThingModel" : "ThingDescription");
            root["@type"] = new JsonArray(role, "uav:projection");
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection? parsed = WotProjection.Parse(document, diagnostics);

            Assert.That(parsed.Required().ResultKind, Is.EqualTo(expected));
            Assert.That(diagnostics.Any(value => value.Severity == WotDiagnosticSeverity.Error), Is.False);
            Assert.That(document.RootElement.TryGetProperty("uav:projectionKind", out _), Is.False);
        }

        /// <summary>
        /// Verifies that a retired draft projection is rejected before source resolution.
        /// </summary>
        [Test]
        public async Task ResolverRejectsRetiredDraftPlansBeforeResolvingSources()
        {
            JsonObject root = LegacyPlan("ThingDescription");
            var source = new Mock<IWotThingResolver>(MockBehavior.Strict);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True,
                    string.Join("; ", result.Diagnostics));
            });
            source.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Verifies that nested plan sources use their ordinary TD media type.
        /// </summary>
        [Test]
        public async Task NestedPlanSourcesUseTheProjectionMediaType()
        {
            JsonObject root = Plan("ThingDescription");
            JsonObject nested = Plan("ThingDescription");
            nested["id"] = "urn:plan:source";
            nested["uav:projects"]![0]!["href"] = "urn:plan:leaf";
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(nested.ToJsonString())));
            source.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:plan:leaf", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(SourceJson)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument? resolved = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(WotProjection.IsProjection(resolved.Required()), Is.False);
            Assert.That(resolved.Properties["value"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://source.test/value"));
            source.Verify(resolver => resolver.ResolveThingAsync(
                "urn:plan:leaf", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Verifies that a projection source cannot declare the retired draft media type.
        /// </summary>
        [Test]
        public async Task ResolverRejectsRetiredProjectionSourceMediaTypeBeforeResolvingAsync()
        {
            JsonObject root = Plan("ThingDescription");
            root["uav:projects"]![0]!["type"] =
                "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.1/projection\"";
            var source = new Mock<IWotThingResolver>(MockBehavior.Strict);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True,
                    string.Join("; ", result.Diagnostics));
            });
            source.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Verifies that source media type must describe the fetched document role.
        /// </summary>
        /// <param name="mediaType">The media type used by this test case.</param>
        [TestCase("application/tm+json", "Thing")]
        [TestCase("application/td+json", "tm:ThingModel")]
        public async Task SourceMediaTypeMustDescribeTheFetchedDocumentRole(string mediaType, string role)
        {
            JsonObject root = Plan("ThingModel");
            root["uav:projects"]![0]!["type"] = mediaType;
            JsonObject fetched = (JsonObject)JsonNode.Parse(SourceJson)!;
            fetched["@type"] = new JsonArray(role);
            fetched["id"] = "urn:plan:source";
            var source = new Mock<IWotThingResolver>(MockBehavior.Strict);
            source.Setup(value => value.ResolveThingAsync(
                    "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(fetched.ToJsonString())));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument? resolved = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True, string.Join("; ", result.Diagnostics));
            source.Verify(value => value.ResolveThingAsync(
                "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            source.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Verifies that malformed root roles cannot be hidden by a projection marker.
        /// </summary>
        [TestCase("[\"uav:projection\",null]")]
        [TestCase("[\"uav:projection\",42]")]
        [TestCase("[\"uav:projection\",[]]")]
        [TestCase("[\"uav:projection\",\"\"]")]
        [TestCase("[\"uav:projection\",\"Thing\",null]")]
        [TestCase("[\"uav:projection\",\"Thing\",\"uav:object\"]")]
        [TestCase("[\"uav:projection\",\"tm:ThingModel\",\"uav:objectType\"]")]
        public void MalformedRootRolesCannotBeHiddenByAProjectionMarker(string types)
        {
            JsonObject root = Plan("ThingDescription");
            root["@type"] = JsonNode.Parse(types);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        private static JsonObject Plan(string? kind)
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = new JsonArray(
                    kind == "ThingModel" ? "tm:ThingModel" : "Thing", "uav:projection"),
                ["id"] = "urn:plan:root",
                ["title"] = "Projection plan",
                ["uav:scenario"] = "urn:scenario:plan",
                ["security"] = "none",
                ["securityDefinitions"] = new JsonObject
                {
                    ["none"] = new JsonObject { ["scheme"] = "nosec" }
                },
                ["uav:projects"] = new JsonArray(new JsonObject
                {
                    ["uav:sourceName"] = "source",
                    ["href"] = "urn:plan:source",
                    ["type"] = "application/td+json",
                    ["uav:selectAll"] = true
                })
            };
        }

        private static JsonObject LegacyPlan(string kind)
        {
            JsonObject root = Plan(kind);
            root["@type"] = new JsonArray("uav:projection");
            root["uav:projectionKind"] = kind;
            return root;
        }

        private const string SourceJson = """
            {
              "@context":"https://www.w3.org/2022/wot/td/v1.1",
              "@type":["Thing","uav:object"],
              "id":"urn:plan:source",
              "title":"Source",
              "securityDefinitions":{"none":{"scheme":"nosec"}},
              "security":"none",
              "properties":{
                "value":{"type":"integer","forms":[{"href":"https://source.test/value","op":"readproperty"}]}
              }
            }
            """;

        [TestCase(true)]
        [TestCase(false)]
        public async Task StrictResolverRejectsRetiredDraftMarkersBeforeResolvingSourcesAsync(
            bool includesProjectionKind)
        {
            JsonObject root = Plan("ThingDescription");
            root["@type"] = new JsonArray("uav:projection");
            if (includesProjectionKind)
            {
                root["uav:projectionKind"] = "ThingDescription";
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var source = new Mock<IWotThingResolver>(MockBehavior.Strict);
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result =
                await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True,
                    string.Join("; ", result.Diagnostics));
            });
            source.VerifyNoOtherCalls();
        }

        [TestCase("projection-kind")]
        [TestCase("node-class-role")]
        [TestCase("marker-only-root-role")]
        public async Task OrdinaryNodeSetConversionRejectsRetiredProjectionMarkersInStrictModeAsync(
            string markerCase)
        {
            JsonObject root = Plan("ThingDescription");
            root["uav:id"] = "nsu=urn:projection:namespace;i=11";
            root["uav:namespaceUri"] = "urn:projection:namespace";
            root["uav:name"] = "Projection";
            if (markerCase == "projection-kind")
            {
                root["uav:projectionKind"] = "ThingDescription";
            }
            else if (markerCase == "marker-only-root-role")
            {
                root["@type"] = new JsonArray("uav:projection");
            }
            else
            {
                ((JsonArray)root["@type"]!).Add("uav:object");
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var source = new Mock<IWotThingResolver>(MockBehavior.Strict);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetResultAsync(
                    document,
                    new WotNodeSetConverterOptions(),
                    source.Object).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True,
                    string.Join("; ", result.Diagnostics));
            });
            source.VerifyNoOtherCalls();
        }
    }
}
