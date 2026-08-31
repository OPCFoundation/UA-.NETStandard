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
using System.Globalization;
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
        public async Task UnderSourceRoutingAnAnnotatedFormIsNotCarriedAsync()
        {
            // Section 12.5: a member routed to its source carries the source's
            // own form, so an annotated form is dropped rather than merged.
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

            using WotDocument view = result.Value!;
            JsonElement speed = view.RootElement
                .GetProperty("properties")
                .GetProperty("pumpSpeed");
            Assert.That(
                speed.GetProperty("forms")[0].GetProperty("href").GetString(),
                Does.Not.Contain("injected"));
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
