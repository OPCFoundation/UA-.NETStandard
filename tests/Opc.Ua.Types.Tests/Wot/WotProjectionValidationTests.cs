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
using System.Text;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Validation of a projection document, which is client-supplied input:
    /// every rule the document breaks is reported rather than thrown, and a
    /// malformed member is dropped rather than silently accepted.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public class WotProjectionValidationTests
    {
        [Test]
        public void ParseRejectsNullArguments()
        {
            using WotDocument document = Parse(Projection());
            var diagnostics = new List<WotDiagnostic>();

            Assert.Throws<ArgumentNullException>(
                () => WotProjection.Parse(null, diagnostics));
            Assert.Throws<ArgumentNullException>(
                () => WotProjection.Parse(document, null));
            Assert.Throws<ArgumentNullException>(
                () => WotProjection.IsProjection(null));
        }

        [Test]
        public void ADocumentWithoutTheProjectionAnnotationIsNotAProjection()
        {
            const string json = """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "@type": "Thing",
              "id": "urn:plain",
              "title": "Plain"
            }
            """;
            using WotDocument document = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            Assert.That(WotProjection.IsProjection(document), Is.False);
            Assert.That(WotProjection.Parse(document, diagnostics), Is.Null);
            Assert.That(diagnostics, Is.Empty,
                "A document that is not a projection is not a malformed projection.");
        }

        [Test]
        public void AMissingScenarioIsReported()
        {
            WotProjection projection = ParseProjection(
                Projection(scenario: null), out List<WotDiagnostic> diagnostics);

            Assert.That(projection, Is.Not.Null);
            Assert.That(projection.Scenario, Is.Empty);
            AssertHas(diagnostics, WotDiagnosticCode.ProjectionScenarioMissing);
        }

        [Test]
        public void ARelativeScenarioIsReported()
        {
            ParseProjection(Projection(scenario: "not-an-iri"), out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionScenarioMissing);
        }

        [Test]
        public void AMissingManifestIsReported()
        {
            const string json = """
            {
              "@context": [
                "https://www.w3.org/2022/wot/td/v1.1",
                { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
              ],
              "@type": ["Thing", "uav:projection"],
              "id": "urn:view:empty",
              "title": "Empty",
              "uav:scenario": "http://example.com/scenario/Empty"
            }
            """;
            ParseProjection(json, out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
        }

        [Test]
        public void AnEmptyManifestIsReported()
        {
            ParseProjection(Projection(sources: "[]"), out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
        }

        [Test]
        public void ANonObjectManifestEntryIsReported()
        {
            ParseProjection(Projection(sources: "[ 42 ]"), out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
        }

        [Test]
        public void AManifestEntryMissingItsRequiredKeysIsReported()
        {
            WotProjection projection = ParseProjection(
                Projection(sources: """[ { "uav:sourceName": "a" } ]"""),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
            Assert.That(projection.Sources.Count, Is.Zero,
                "A source missing href or type is dropped rather than half-built.");
        }

        [Test]
        public void AnUnknownSourceMediaTypeIsReported()
        {
            ParseProjection(
                Projection(sources: Source(mediaType: "application/json")),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
        }

        [Test]
        public void ADuplicateSourceNameIsReported()
        {
            string two = "[" + Inner() + "," + Inner() + "]";
            WotProjection projection = ParseProjection(
                Projection(sources: two), out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
            Assert.That(projection.Sources.Count, Is.EqualTo(1),
                "The second declaration of a source name is dropped.");
        }

        [Test]
        public void AnUnknownRoutingIsReported()
        {
            ParseProjection(
                Projection(sources: Source(extra: """, "uav:routing": "elsewhere" """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
        }

        [Test]
        public void ProjectionRoutingIsAccepted()
        {
            WotProjection projection = ParseProjection(
                Projection(sources: Source(extra: """, "uav:routing": "projection" """)),
                out List<WotDiagnostic> diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(projection.Sources[0].Routing, Is.EqualTo(WotProjectionRouting.Projection));
        }

        [Test]
        public void AMalformedSourceDigestIsReported()
        {
            ParseProjection(
                Projection(sources: Source(extra: """, "uav:sourceDigest": "md5:abcd" """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionManifestInvalid);
        }

        [Test]
        public void ANonBooleanSelectAllIsReported()
        {
            ParseProjection(
                Projection(sources: Source(extra: """, "uav:selectAll": "yes" """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid);
        }

        [Test]
        public void ANonArraySelectIsReported()
        {
            ParseProjection(
                Projection(sources: Source(extra: """, "uav:select": 7 """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid);
        }

        [Test]
        public void ANonObjectSelectEntryIsReported()
        {
            ParseProjection(
                Projection(sources: Source(extra: """, "uav:select": [ 7 ] """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid);
        }

        [Test]
        public void AnUnknownAffordanceKindIsReported()
        {
            ParseProjection(
                Projection(sources: Source(
                    extra: """, "uav:select": [ { "uav:affordanceKind": "gadget" } ] """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid);
        }

        [Test]
        public void ANonStringSemanticIdInAFilterIsReported()
        {
            ParseProjection(
                Projection(sources: Source(
                    extra: """, "uav:select": [ { "uav:semanticId": 5 } ] """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid);
        }

        [Test]
        public void AFilterCarryingAnUnadmittedKeyIsReported()
        {
            // The predicate set is deliberately closed so a filter stays
            // decidable by inspection.
            ParseProjection(
                Projection(sources: Source(
                    extra: """, "uav:select": [ { "uav:whatever": "x" } ] """)),
                out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid);
        }

        [Test]
        public void EveryAffordanceKindIsRecognised()
        {
            foreach ((string token, WotAffordanceKind expected) in new[]
            {
                ("property", WotAffordanceKind.Property),
                ("action", WotAffordanceKind.Action),
                ("event", WotAffordanceKind.Event)
            })
            {
                WotProjection projection = ParseProjection(
                    Projection(sources: Source(
                        extra: $$""", "uav:select": [ { "uav:affordanceKind": "{{token}}" } ] """)),
                    out List<WotDiagnostic> diagnostics);

                Assert.That(diagnostics, Is.Empty, $"'{token}' is a valid affordance kind.");
                Assert.That(projection.Sources[0].Filters[0].AffordanceKind, Is.EqualTo(expected));
            }
        }

        [Test]
        public void AProjectedAffordanceWithoutTmRefIsReported()
        {
            string json = Projection(affordances: """
              "properties": { "alpha": { "type": "number" } },
            """);
            WotProjection projection = ParseProjection(json, out List<WotDiagnostic> diagnostics);

            AssertHas(diagnostics, WotDiagnosticCode.ProjectionDefinesAffordance);
            Assert.That(projection.References.Count, Is.Zero,
                "A projection declares affordances; one that defines a body is not a reference.");
        }

        [Test]
        public void AProjectedAffordanceWithTmRefBecomesAReference()
        {
            string json = Projection(affordances: """
              "properties": { "alpha": { "tm:ref": "a#alpha" } },
              "actions": { "run": { "tm:ref": "a#run" } },
              "events": { "tick": { "tm:ref": "a#tick" } },
            """);
            WotProjection projection = ParseProjection(json, out List<WotDiagnostic> diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(projection.References.Count, Is.EqualTo(3));
        }

        [Test]
        public void OrganizingLinksAreReadAndUnrelatedLinksIgnored()
        {
            string json = Projection(links: """
              "links": [
                { "rel": "unrelated", "href": "urn:other" },
                { "rel": "ua:Organizes", "href": "urn:group:one",
                  "uav:refName": "One", "type": "application/td+json" },
                { "rel": "ua:Organizes" },
                "not-an-object"
              ],
            """);
            WotProjection projection = ParseProjection(json, out List<WotDiagnostic> diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(projection.OrganizingLinks.Count, Is.EqualTo(1),
                "Only a well-formed ua:Organizes link with an href is an organizing link.");
            Assert.That(projection.OrganizingLinks[0].RefName, Is.EqualTo("One"));
            Assert.That(projection.OrganizingLinks[0].Href, Is.EqualTo("urn:group:one"));
        }

        private static WotProjection ParseProjection(
            string json,
            out List<WotDiagnostic> diagnostics)
        {
            using WotDocument document = Parse(json);
            diagnostics = [];
            WotProjection projection = WotProjection.Parse(document, diagnostics);
            Assert.That(projection, Is.Not.Null, "The document carries uav:projection.");
            return projection;
        }

        private static WotDocument Parse(string json)
        {
            return WotDocument.Parse(Encoding.UTF8.GetBytes(json));
        }

        private static void AssertHas(List<WotDiagnostic> diagnostics, WotDiagnosticCode code)
        {
            foreach (WotDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Code == code && diagnostic.Severity == WotDiagnosticSeverity.Error)
                {
                    return;
                }
            }
            Assert.Fail($"Expected an error diagnostic {code}; got: " +
                string.Join(", ", diagnostics.ConvertAll(static d => $"{d.Severity}/{d.Code}")));
        }

        private static string Inner(string mediaType = "application/td+json", string extra = "")
        {
            return $$"""
            {
              "uav:sourceName": "a",
              "href": "urn:sourceA",
              "type": "{{mediaType}}"
              {{extra}}
            }
            """;
        }

        private static string Source(string mediaType = "application/td+json", string extra = "")
        {
            return "[" + Inner(mediaType, extra) + "]";
        }

        private static string Projection(
            string scenario = "http://example.com/scenario/Simple",
            string sources = null,
            string affordances = "",
            string links = "")
        {
            string scenarioLine = scenario is null
                ? string.Empty
                : "\"uav:scenario\": \"" + scenario + "\",";
            return $$"""
            {
              "@context": [
                "https://www.w3.org/2022/wot/td/v1.1",
                { "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                  "ua": "http://opcfoundation.org/UA/",
                  "tm": "https://www.w3.org/2019/wot/tm#" }
              ],
              "@type": ["Thing", "uav:projection"],
              "id": "urn:view:simple",
              "title": "Simple view",
              {{scenarioLine}}
              {{links}}
              {{affordances}}
              "uav:projects": {{sources ?? Source()}}
            }
            """;
        }
    }
}
