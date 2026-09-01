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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The closed set of members a projection may annotate a declared
    /// affordance with (WoT Binding Section 12.5).
    /// </summary>
    /// <remarks>
    /// A projection declares affordances; it does not define them. Restating a
    /// schema member would let a view publish a description of a Node that
    /// disagrees with the Node, so the set of members an annotation may carry
    /// is closed in the same way the <c>uav:select</c> predicate set is.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotProjectionAnnotationTests
    {
        // Each of these restates part of the source's schema, which is exactly
        // what an annotation may not do.
        [TestCase("\"type\":\"string\"")]
        [TestCase("\"unit\":\"rpm\"")]
        [TestCase("\"minimum\":0")]
        [TestCase("\"maximum\":100")]
        [TestCase("\"enum\":[1,2,3]")]
        [TestCase("\"readOnly\":true")]
        [TestCase("\"observable\":false")]
        [TestCase("\"properties\":{\"nested\":{\"type\":\"number\"}}")]
        [TestCase("\"uav:browseName\":\"pump:Something\"")]
        [TestCase("\"uav:modellingRule\":\"Mandatory\"")]
        public void AForbiddenAnnotationIsReported(string annotation)
        {
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection(annotation)));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.ProjectionAnnotationNotPermitted),
                Is.True,
                $"'{annotation}' should not be admitted as a projection annotation.");
        }

        [TestCase("\"title\":\"Speed\"")]
        [TestCase("\"titles\":{\"en\":\"Speed\",\"de\":\"Drehzahl\"}")]
        [TestCase("\"description\":\"A condition signal.\"")]
        [TestCase("\"descriptions\":{\"en\":\"A condition signal.\"}")]
        [TestCase("\"@type\":\"http://example.com/ontology/ConditionSignal\"")]
        [TestCase("\"uav:semanticId\":\"http://example.com/ontology/Signal\"")]
        [TestCase("\"uav:metadata\":{\"http://example.com/x\":1}")]
        [TestCase("\"forms\":[{\"href\":\"https://example.com/x\"}]")]
        [TestCase("\"security\":\"nosec_sc\"")]
        public void APermittedAnnotationIsAccepted(string annotation)
        {
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection(annotation)));
            var diagnostics = new List<WotDiagnostic>();

            WotProjection.Parse(document, diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False,
                $"'{annotation}' should be admitted as a projection annotation.");
        }

        [Test]
        public async Task AForbiddenAnnotationNeverOverridesTheSourceSchemaAsync()
        {
            var resolver = new WotProjectionResolver(new MapResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["./source.jsonld"] = SourceJson
                }));
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection("\"type\":\"string\",\"unit\":\"rpm\"")));

            WotConversionResult<WotDocument> result =
                await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionAnnotationNotPermitted),
                Is.True);
            if (result.Value is null)
            {
                // Resolution refused the projection outright, which is a
                // stronger outcome than dropping the members.
                return;
            }
            using WotDocument view = result.Value;
            JsonElement speed = view.RootElement
                .GetProperty("properties")
                .GetProperty("pumpSpeed");
            Assert.Multiple(() =>
            {
                Assert.That(
                    speed.GetProperty("type").GetString(),
                    Is.EqualTo("number"),
                    "The source's own schema shall survive the merge.");
                Assert.That(speed.TryGetProperty("unit", out _), Is.False);
            });
        }

        [Test]
        public async Task PermittedAnnotationsAreMergedAsync()
        {
            var resolver = new WotProjectionResolver(new MapResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["./source.jsonld"] = SourceJson
                }));
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection(
                    "\"title\":\"Condition signal\"," +
                    "\"uav:semanticId\":\"http://example.com/ontology/Signal\"," +
                    "\"@type\":\"http://example.com/ontology/ConditionSignal\"")));

            WotConversionResult<WotDocument> result =
                await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            using WotDocument view = result.Value!;
            JsonElement speed = view.RootElement
                .GetProperty("properties")
                .GetProperty("pumpSpeed");
            Assert.Multiple(() =>
            {
                Assert.That(
                    speed.GetProperty("title").GetString(),
                    Is.EqualTo("Condition signal"));
                Assert.That(
                    speed.GetProperty("uav:semanticId").GetString(),
                    Is.EqualTo("http://example.com/ontology/Signal"));
                Assert.That(
                    speed.GetProperty("type").GetString(),
                    Is.EqualTo("number"),
                    "A permitted annotation shall not disturb the source schema.");
            });
        }

        [Test]
        public async Task UnderSourceRoutingAnAnnotatedFormMakesTheDocumentInvalidAsync()
        {
            // Section 12.5: a member routed to its source carries the source's
            // own form, so a member that restates one makes the document
            // invalid. Dropping it would leave a form the author wrote and the
            // consumer silently did not use.
            var resolver = new WotProjectionResolver(new MapResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["./source.jsonld"] = SourceJson
                }));
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection(
                    "\"forms\":[{\"href\":\"https://example.com/injected\"}]",
                    routing: "source")));

            WotConversionResult<WotDocument> result =
                await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error &&
                        d.Code == WotDiagnosticCode.ProjectionAnnotationNotPermitted),
                    Is.True,
                    string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.That(result.Value, Is.Null, "An invalid document resolves to no view.");
            });
        }

        [Test]
        public async Task UnderSourceRoutingAnAnnotatedSecurityMakesTheDocumentInvalidAsync()
        {
            var resolver = new WotProjectionResolver(new MapResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["./source.jsonld"] = SourceJson
                }));
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection(
                    "\"security\":[\"nosec_sc\"]",
                    routing: "source")));

            WotConversionResult<WotDocument> result =
                await resolver.ResolveAsync(document).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.ProjectionAnnotationNotPermitted),
                Is.True,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// A <c>uav:namePrefix</c> upper-cases the first character of the
        /// source name (Section 12.3), so <c>serialNumber</c> and
        /// <c>SerialNumber</c> in one source both become
        /// <c>deviceSerialNumber</c> in the view. Two bulk selections of the
        /// same kind from the same source can therefore take the same view name
        /// from different definitions, and nothing before the final tie-break
        /// separates them. With it, whichever of the two sorts first by the
        /// affordance's own name in the source is the one the view keeps - in
        /// every implementation.
        /// </summary>
        [Test]
        public async Task ATiedViewNameIsBrokenByTheSourceAffordanceNameAsync()
        {
            var resolver = new WotProjectionResolver(new MapResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["./collide.jsonld"] = CollidingSourceJson
                }));
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(PrefixedProjection));

            WotConversionResult<WotDocument> result =
                await resolver.ResolveAsync(document).ConfigureAwait(false);

            using WotDocument view = result.Value!;
            JsonElement selected = view.RootElement
                .GetProperty("properties")
                .GetProperty("deviceSerialNumber");
            Assert.Multiple(() =>
            {
                Assert.That(
                    selected.GetProperty("uav:resolvedFrom").GetString(),
                    Does.Contain("/properties/SerialNumber"),
                    "'SerialNumber' sorts before 'serialNumber' by Unicode code point, so " +
                    "the order is total and both implementations keep the same definition.");
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.ProjectionSelectionDropped),
                    Is.True,
                    "The later selection of the same name is dropped and reported.");
            });
        }

        private static string Projection(string annotation, string routing = "projection")
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"tm\":\"https://www.w3.org/2019/wot/tm#\"}]," +
                "\"@type\":[\"Thing\",\"uav:projection\"]," +
                "\"id\":\"urn:dev:opcua:view:annotations\"," +
                "\"title\":\"Annotation view\"," +
                "\"uav:scenario\":\"http://example.com/scenario/Annotations\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"security\":\"nosec_sc\"," +
                "\"uav:projects\":[{\"uav:sourceName\":\"pump\"," +
                "\"href\":\"./source.jsonld\",\"type\":\"application/td+json\"," +
                "\"uav:routing\":\"" + routing + "\"}]," +
                "\"properties\":{\"pumpSpeed\":{" +
                "\"tm:ref\":\"./source.jsonld#/properties/pumpSpeed\"," +
                annotation + "}}}";
        }

        /// <summary>
        /// A source whose two property affordances differ only in the case of
        /// their first character, so a <c>uav:namePrefix</c> gives both the
        /// same name in the view.
        /// </summary>
        private const string CollidingSourceJson =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":\"uav:object\",\"title\":\"Device\"," +
            "\"id\":\"urn:dev:opcua:device\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Device\"," +
            "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
            "\"security\":\"nosec_sc\"," +
            "\"properties\":{" +
            "\"serialNumber\":{\"@type\":\"uav:variable\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;serialNumber\"," +
            "\"type\":\"string\"," +
            "\"forms\":[{\"href\":\"opc.tcp://example.test:4840\"," +
            "\"op\":[\"readproperty\"]}]}," +
            "\"SerialNumber\":{\"@type\":\"uav:variable\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;SerialNumber\"," +
            "\"type\":\"string\"," +
            "\"forms\":[{\"href\":\"opc.tcp://example.test:4840\"," +
            "\"op\":[\"readproperty\"]}]}}}";

        private const string PrefixedProjection =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
            "\"tm\":\"https://www.w3.org/2019/wot/tm#\"}]," +
            "\"@type\":[\"Thing\",\"uav:projection\"]," +
            "\"id\":\"urn:dev:opcua:view:prefixed\"," +
            "\"title\":\"Prefixed view\"," +
            "\"uav:scenario\":\"http://example.com/scenario/Prefixed\"," +
            "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
            "\"security\":\"nosec_sc\"," +
            "\"uav:projects\":[{\"uav:sourceName\":\"device\"," +
            "\"href\":\"./collide.jsonld\",\"type\":\"application/td+json\"," +
            "\"uav:routing\":\"source\",\"uav:namePrefix\":\"device\"," +
            "\"uav:selectAll\":true}]}";

        private const string SourceJson =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":\"uav:object\",\"title\":\"Pump\"," +
            "\"id\":\"urn:dev:opcua:pump\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Pump\"," +
            "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
            "\"security\":\"nosec_sc\"," +
            "\"properties\":{\"pumpSpeed\":{\"@type\":\"uav:variable\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;PumpSpeed\"," +
            "\"type\":\"number\"," +
            "\"forms\":[{\"href\":\"opc.tcp://example.test:4840\"," +
            "\"op\":[\"readproperty\"]}]}}}";

        private sealed class MapResolver : IWotThingResolver
        {
            public MapResolver(Dictionary<string, string> map)
            {
                m_map = map;
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                WotResolverResult result = m_map.TryGetValue(reference, out string? json)
                    ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json!))
                    : WotResolverResult.NotFound;
                return new ValueTask<WotResolverResult>(result);
            }

            private readonly Dictionary<string, string> m_map;
        }
    }
}
