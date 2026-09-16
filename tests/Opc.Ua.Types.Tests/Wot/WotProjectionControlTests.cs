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
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionControlTests
    {
        [Test]
        public async Task ReviewedMalformedSemanticIrisAreRejectedBeforeDownstreamAcquisition(
            [Values("space", "scheme", "percent", "control")] string failure,
            [Values] bool nested)
        {
            string iri = failure switch
            {
                "space" => "urn:bad meaning",
                "scheme" => "\u00e9:meaning",
                "percent" => "urn:bad%GG",
                "control" => "urn:\u0001",
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
            JsonObject invalid = Projection();
            invalid["uav:projects"]![0]!["href"] = "urn:review:source";
            invalid["uav:projects"]![0]!["uav:select"] =
                new JsonArray(new JsonObject { ["uav:semanticId"] = iri });
            var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
            documents.Setup(value => value.ResolveThingAsync(
                    "urn:review:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(
                    /*lang=json,strict*/
                                         "{\"@type\":\"Thing\",\"title\":\"Source\",\"properties\":{}}")));
            JsonObject root = invalid;
            if (nested)
            {
                root = Projection();
                root["uav:projects"]![0]!["href"] = "urn:review:nested";
                root["uav:projects"]![0]!["type"] = WotProjection.ContentType;
                documents.Setup(value => value.ResolveThingAsync(
                        "urn:review:nested", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(invalid.ToJsonString())));
            }
            using WotDocument document = Parse(root);
            var resolver = new WotProjectionResolver(documents.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSelectorInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            documents.Verify(value => value.ResolveThingAsync(
                "urn:review:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void NumericSourceDigestIsReportedInsteadOfDiscardingThePin()
        {
            JsonObject projection = Projection();
            projection["uav:projects"]![0]!["uav:sourceDigest"] = 42;
            using WotDocument document = Parse(projection);
            var diagnostics = new List<WotDiagnostic>();

            var parsed = WotProjection.Parse(document, diagnostics);

            Assert.That(parsed, Is.Not.Null);
            Assert.That(diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("uav:sourceDigest", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("uav:routing", "42")]
        [TestCase("uav:routing", "null")]
        [TestCase("uav:routing", "{}")]
        [TestCase("uav:routing", "[]")]
        [TestCase("uav:routing", "false")]
        [TestCase("uav:namePrefix", "42")]
        [TestCase("uav:namePrefix", "null")]
        [TestCase("uav:namePrefix", "{}")]
        [TestCase("uav:namePrefix", "[]")]
        [TestCase("uav:namePrefix", "false")]
        [TestCase("uav:namePrefix", "\"\"")]
        [TestCase("uav:sourceDigest", "null")]
        [TestCase("uav:sourceDigest", "{}")]
        [TestCase("uav:sourceDigest", "[]")]
        [TestCase("uav:sourceDigest", "false")]
        public void PresentInvalidTextControlsAreNotTreatedAsOmitted(string term, string value)
        {
            JsonObject projection = Projection();
            projection["uav:projects"]![0]![term] = JsonNode.Parse(value);
            using WotDocument document = Parse(projection);
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Message.Contains(term, StringComparison.Ordinal)), Is.True);
        }

        [TestCase("42")]
        [TestCase("null")]
        [TestCase("true")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("\"\"")]
        [TestCase("[\"urn:Sensor\",42]")]
        [TestCase("[\"urn:Sensor\",null]")]
        [TestCase("[\"urn:Sensor\",\"\"]")]
        [TestCase("[[\"urn:Sensor\"]]")]
        public void InvalidTypePredicatesCannotBecomeUnconstrainedFilters(string value)
        {
            JsonObject projection = Projection();
            projection["uav:projects"]![0]!["uav:select"] =
                new JsonArray(new JsonObject { ["@type"] = JsonNode.Parse(value) });
            using WotDocument document = Parse(projection);
            var diagnostics = new List<WotDiagnostic>();

            var parsed = WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSelectorInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            Assert.That(parsed.Sources[0].Filters.Count, Is.Zero);
        }

        [TestCase("[]")]
        [TestCase("[{}]")]
        public void ExplicitlyEmptySelectorsCannotImplySelectAll(string value)
        {
            JsonObject projection = Projection();
            projection["uav:projects"]![0]!["uav:select"] = JsonNode.Parse(value);
            using WotDocument document = Parse(projection);
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSelectorInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [TestCase("")]
        [TestCase("relative/meaning")]
        public void SemanticPredicatesRequireAnAbsoluteIdentity(string value)
        {
            JsonObject projection = Projection();
            projection["uav:projects"]![0]!["uav:select"] =
                new JsonArray(new JsonObject { ["uav:semanticId"] = value });
            using WotDocument document = Parse(projection);
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSelectorInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void AbsentControlsKeepDefaultsAndValidControlsRetainEveryPredicate()
        {
            JsonObject projection = Projection();
            using WotDocument unconfigured = Parse(projection);
            var diagnostics = new List<WotDiagnostic>();
            var defaults = WotProjection.Parse(unconfigured, diagnostics);
            Assert.That(diagnostics, Is.Empty);
            Assert.That(defaults.Sources[0].Routing, Is.EqualTo(WotProjectionRouting.Source));
            Assert.That(defaults.Sources[0].SourceDigest, Is.Null);
            Assert.That(defaults.Sources[0].NamePrefix, Is.Null);
            Assert.That(defaults.Sources[0].Filters.Count, Is.Zero);

            JsonNode source = projection["uav:projects"]![0]!;
            const string digest = "sha-256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            source["uav:sourceDigest"] = digest;
            source["uav:routing"] = "projection";
            source["uav:namePrefix"] = "selected";
            source["uav:select"] = new JsonArray(new JsonObject
            {
                ["uav:affordanceKind"] = "property",
                ["uav:semanticId"] = "urn:meaning:temperature",
                ["@type"] = new JsonArray("urn:Sensor", "urn:Calibrated")
            });
            using WotDocument configured = Parse(projection);

            var parsed = WotProjection.Parse(configured, diagnostics);

            Assert.That(diagnostics, Is.Empty);
            WotProjectionManifestSource actual = parsed.Sources[0];
            Assert.That(actual.SourceDigest, Is.EqualTo(digest));
            Assert.That(actual.Routing, Is.EqualTo(WotProjectionRouting.Projection));
            Assert.That(actual.NamePrefix, Is.EqualTo("selected"));
            Assert.That(actual.Filters.Count, Is.EqualTo(1));
            Assert.That(actual.Filters[0].AffordanceKind, Is.EqualTo(WotAffordanceKind.Property));
            Assert.That(actual.Filters[0].SemanticId, Is.EqualTo("urn:meaning:temperature"));
            Assert.That(actual.Filters[0].TypeTokens.ToArray(),
                Is.EqualTo(s_expectedTypes));
        }

        [TestCase("uav:sourceDigest", "42")]
        [TestCase("uav:select", /*lang=json,strict*/ "[{\"@type\":42}]")]
        [TestCase("uav:namePrefix", "{}")]
        public async Task InvalidControlsFailBeforeSourceAcquisition(string term, string value)
        {
            JsonObject projection = Projection();
            projection["uav:projects"]![0]![term] = JsonNode.Parse(value);
            using WotDocument document = Parse(projection);
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.NotFound);
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            source.Verify(sourceResolver => sourceResolver.ResolveThingAsync(
                It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task InvalidNestedControlsCannotAcquireTheirUnpinnedSource()
        {
            JsonObject nested = Projection();
            nested["uav:projects"]![0]!["uav:sourceDigest"] = 42;
            JsonObject root = Projection();
            root["uav:projects"]![0]!["href"] = "urn:nested:projection";
            using WotDocument document = Parse(root);
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.NotFound);
            source.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:nested:projection", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(nested.ToJsonString())));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionManifestInvalid), Is.True);
            source.Verify(sourceResolver => sourceResolver.ResolveThingAsync(
                "urn:nested:projection", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            source.Verify(sourceResolver => sourceResolver.ResolveThingAsync(
                "urn:source:controls", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task PrefixCapitalizesTheFirstUnicodeScalarWithoutChangingTheRemainder()
        {
            JsonObject projection = Projection();
            projection["uav:projects"]![0]!["uav:selectAll"] = true;
            projection["uav:projects"]![0]!["uav:namePrefix"] = "selected";
            var sourceDocument = new JsonObject
            {
                ["@type"] = "Thing",
                ["properties"] = new JsonObject
                {
                    ["\U00010428MixedCase"] = new JsonObject { ["type"] = "integer" }
                }
            };
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:source:controls", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(sourceDocument.ToJsonString())));
            using WotDocument document = Parse(projection);
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument resolved = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(resolved.Properties.Keys, Is.EqualTo(s_expectedPrefixedNames));
            Assert.That(sourceDocument["properties"]!["\U00010428MixedCase"], Is.Not.Null);
        }

        private static JsonObject Projection()
        {
            return new JsonObject
            {
                ["@type"] = new JsonArray("uav:projection"),
                ["uav:projectionKind"] = "ThingDescription",
                ["uav:scenario"] = "urn:scenario:controls",
                ["uav:projects"] = new JsonArray(new JsonObject
                {
                    ["uav:sourceName"] = "source",
                    ["href"] = "urn:source:controls",
                    ["type"] = "application/td+json"
                })
            };
        }

        private static WotDocument Parse(JsonObject document)
        {
            return WotDocument.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));
        }

        private static readonly string[] s_expectedTypes = ["urn:Sensor", "urn:Calibrated"];
        private static readonly string[] s_expectedPrefixedNames = ["selected\U00010400MixedCase"];
    }
}
