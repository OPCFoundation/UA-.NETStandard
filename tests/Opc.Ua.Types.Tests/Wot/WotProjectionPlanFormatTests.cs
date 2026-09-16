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
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionPlanFormatTests
    {
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
            using WotDocument resolved = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(resolved.RootElement.GetProperty("@type").EnumerateArray()
                .Select(value => value.GetString()), Does.Contain(expectedType));
            Assert.That(resolved.Kind, Is.EqualTo(kind == "ThingModel"
                ? WotDocumentKind.ThingModel : WotDocumentKind.ThingDescription));
            Assert.That(resolved.RootElement.TryGetProperty("uav:projectionKind", out _), Is.False);
            Assert.That(WotProjection.IsProjection(resolved), Is.False);
            Assert.That(resolved.Properties["value"].GetProperty("forms").GetArrayLength(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("@type").GetArrayLength(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("uav:projectionKind").GetString(), Is.EqualTo(kind));
        }

        [TestCase("null")]
        [TestCase("42")]
        [TestCase("\"\"")]
        [TestCase("\"Thing\"")]
        [TestCase("\"thingdescription\"")]
        [TestCase("[]")]
        public void PresentInvalidResultKindsCannotBecomeAnUnspecifiedPlan(string json)
        {
            JsonObject root = Plan("ThingDescription");
            root["uav:projectionKind"] = JsonNode.Parse(json);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(value => value.Severity == WotDiagnosticSeverity.Error &&
                value.Message.Contains("uav:projectionKind", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("Thing")]
        [TestCase("tm:ThingModel")]
        [TestCase("uav:object")]
        [TestCase("uav:variableType")]
        public void AModernPlanCannotClaimAnAlreadyResolvedRole(string type)
        {
            JsonObject root = Plan("ThingDescription");
            ((JsonArray)root["@type"]!).Add(type);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(value => value.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [TestCase("Thing")]
        [TestCase("tm:ThingModel")]
        [TestCase(null)]
        public void MissingResultKindDoesNotImplicitlyEnableDraftCompatibility(string legacyType)
        {
            JsonObject root = Plan("ThingDescription");
            root.Remove("uav:projectionKind");
            if (legacyType is not null)
            {
                ((JsonArray)root["@type"]!).Add(legacyType);
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(value => value.Severity == WotDiagnosticSeverity.Error &&
                value.Message.Contains("uav:projectionKind", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("Thing", false, WotDocumentKind.ThingDescription)]
        [TestCase("tm:ThingModel", false, WotDocumentKind.ThingModel)]
        [TestCase("Thing", true, WotDocumentKind.Unknown)]
        [TestCase(null, false, WotDocumentKind.Unknown)]
        public void ExplicitDraftCompatibilityRequiresExactlyOneLegacyKind(
            string marker, bool contradictory, WotDocumentKind expected)
        {
            JsonObject root = Plan("ThingDescription");
            root.Remove("uav:projectionKind");
            if (marker is not null)
            {
                ((JsonArray)root["@type"]!).Add(marker);
            }
            if (contradictory)
            {
                ((JsonArray)root["@type"]!).Add("tm:ThingModel");
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            var parsed = WotProjection.Parse(
                document, diagnostics, WotProjectionCompatibilityMode.DraftProjection11);

            Assert.That(parsed.ResultKind, Is.EqualTo(expected));
            Assert.That(diagnostics.Any(value => value.Severity == WotDiagnosticSeverity.Error),
                Is.EqualTo(expected == WotDocumentKind.Unknown));
            Assert.That(document.RootElement.TryGetProperty("uav:projectionKind", out _), Is.False);
        }

        [Test]
        public async Task ResolverRequiresTheExplicitCompatibilityOptionForDraftPlans()
        {
            JsonObject root = Plan("ThingDescription");
            root.Remove("uav:projectionKind");
            ((JsonArray)root["@type"]!).Add("Thing");
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(SourceJson)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var current = new WotProjectionResolver(source.Object);
            var compatible = new WotProjectionResolver(source.Object, new WotNodeSetConverterOptions
            {
                ProjectionCompatibilityMode = WotProjectionCompatibilityMode.DraftProjection11
            });

            WotConversionResult<WotDocument> rejected = await current.ResolveAsync(document).ConfigureAwait(false);
            WotConversionResult<WotDocument> accepted = await compatible.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument resolved = accepted.Value;

            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Value, Is.Null);
            Assert.That(accepted.Success, Is.True, string.Join("; ", accepted.Diagnostics));
            Assert.That(WotProjection.IsProjection(resolved), Is.False);
            source.Verify(resolver => resolver.ResolveThingAsync(
                "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task NestedPlanSourcesUseTheProjectionMediaType()
        {
            JsonObject root = Plan("ThingDescription");
            root["uav:projects"]![0]!["type"] =
                "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"";
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
            using WotDocument resolved = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(WotProjection.IsProjection(resolved), Is.False);
            Assert.That(resolved.Properties["value"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://source.test/value"));
            source.Verify(resolver => resolver.ResolveThingAsync(
                "urn:plan:leaf", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase("application/tm+json", "Thing")]
        [TestCase("application/td+json", "tm:ThingModel")]
        [TestCase(WotProjection.ContentType, "Thing")]
        [TestCase(WotProjection.ContentType, "tm:ThingModel")]
        [TestCase("application/td+json", "uav:projection")]
        [TestCase("application/tm+json", "uav:projection")]
        public async Task SourceMediaTypeMustDescribeTheFetchedDocumentRole(string mediaType, string role)
        {
            JsonObject root = Plan("ThingModel");
            root["uav:projects"]![0]!["type"] = mediaType;
            JsonObject fetched = role == "uav:projection"
                ? Plan("ThingDescription")
                : (JsonObject)JsonNode.Parse(SourceJson)!;
            fetched["@type"] = new JsonArray(role);
            fetched["id"] = "urn:plan:source";
            if (role == "uav:projection")
            {
                fetched["uav:projects"]![0]!["href"] = "urn:unfetched";
            }
            var source = new Mock<IWotThingResolver>(MockBehavior.Strict);
            source.Setup(value => value.ResolveThingAsync(
                    "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(fetched.ToJsonString())));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object, new WotNodeSetConverterOptions
            {
                ProjectionCompatibilityMode = WotProjectionCompatibilityMode.DraftProjection11
            });

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument resolved = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True, string.Join("; ", result.Diagnostics));
            source.Verify(value => value.ResolveThingAsync(
                "urn:plan:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            source.VerifyNoOtherCalls();
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void UndefinedCompatibilityModesAreRejectedAtBothPublicBoundaries(int value)
        {
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(Plan("ThingModel").ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();
            var mode = (WotProjectionCompatibilityMode)value;
            var source = new Mock<IWotThingResolver>();

            Assert.Throws<ArgumentOutOfRangeException>(() => WotProjection.Parse(document, diagnostics, mode));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WotProjectionResolver(
                source.Object, new WotNodeSetConverterOptions { ProjectionCompatibilityMode = mode }));
            Assert.That(diagnostics, Is.Empty);
            source.VerifyNoOtherCalls();
        }

        [TestCase("ThingDescription", "Thing")]
        [TestCase("ThingModel", "tm:ThingModel")]
        [TestCase("Thing", null)]
        [TestCase(null, "Thing")]
        public void ExplicitCompatibilityDoesNotRelaxAnAuthoredModernKind(string kind, string marker)
        {
            JsonObject root = Plan(kind);
            if (marker is not null)
            {
                ((JsonArray)root["@type"]!).Add(marker);
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics, WotProjectionCompatibilityMode.DraftProjection11);

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [TestCase("[\"uav:projection\",null]", false)]
        [TestCase("[\"uav:projection\",42]", false)]
        [TestCase("[\"uav:projection\",[]]", false)]
        [TestCase("[\"uav:projection\",\"\"]", false)]
        [TestCase("[\"uav:projection\",\"Thing\",null]", true)]
        [TestCase("[\"uav:projection\",\"Thing\",\"uav:object\"]", true)]
        [TestCase("[\"uav:projection\",\"tm:ThingModel\",\"uav:objectType\"]", true)]
        public void MalformedRootRolesCannotBeHiddenByAProjectionMarker(string types, bool legacy)
        {
            JsonObject root = Plan("ThingDescription");
            root["@type"] = JsonNode.Parse(types);
            if (legacy)
            {
                root.Remove("uav:projectionKind");
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics, WotProjectionCompatibilityMode.DraftProjection11);

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void CurrentPlanMetadataAndResultKindTermAreDiscoverable()
        {
            Assert.That(WotProjection.Format, Is.EqualTo("WoT-Projection/1.2"));
            Assert.That(WotProjection.ContentType, Is.EqualTo(
                "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\""));
            Assert.That(WotBindingConformance.IsKnownTerm("uav:projectionKind"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OrdinaryNodeSetConversionRejectsAProjectionMarkerEvenWithAnObjectRole(bool legacy)
        {
            JsonObject root = Plan("ThingDescription");
            root["uav:id"] = "nsu=urn:projection:namespace;i=11";
            root["uav:namespaceUri"] = "urn:projection:namespace";
            root["uav:name"] = "Projection";
            ((JsonArray)root["@type"]!).Add("uav:object");
            if (legacy)
            {
                root.Remove("uav:projectionKind");
                ((JsonArray)root["@type"]!).Add("Thing");
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var options = new WotNodeSetConverterOptions
            {
                ProjectionCompatibilityMode = WotProjectionCompatibilityMode.DraftProjection11
            };
            var source = new Mock<IWotThingResolver>();

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, options, source.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.False, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid),
                Is.True, string.Join("; ", result.Diagnostics));
            source.VerifyNoOtherCalls();
        }

        private static JsonObject Plan(string kind)
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = new JsonArray("uav:projection"),
                ["id"] = "urn:plan:root",
                ["title"] = "Projection plan",
                ["uav:projectionKind"] = kind,
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
    }
}
