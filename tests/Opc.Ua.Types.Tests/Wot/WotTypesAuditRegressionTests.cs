/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Regression tests for the T6 WoT findings of the Opc.Ua.Types audit.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotTypesAuditRegressionTests
    {
        [TestCase("{\"@type\":\"uav:object\",\"title\":\"\\ud800\",\"properties\":{}}")]
        [TestCase("{\"@type\":\"uav:object\",\"title\":\"x\",\"properties\":{\"\\udc00\":1}}")]
        public void LoneSurrogateEscapeIsRejectedAsJson(string json)
        {
            // The reader accepts the escape; GetString / Name then threw
            // InvalidOperationException out of every consumer.
            Assert.Throws<JsonException>(
                () => WotDocument.Parse(Encoding.UTF8.GetBytes(json)));
        }

        [Test]
        public void InvalidUtf8InAStringIsRejectedAsJson()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"ab\"}");
            json[10] = 0xFF;

            Assert.Throws<JsonException>(() => WotDocument.Parse(json));
        }

        [Test]
        public void EscapedSurrogatePairStillParses()
        {
            byte[] json = Encoding.UTF8.GetBytes(
                "{\"@type\":\"uav:object\",\"title\":\"\\ud83d\\ude00 \\\"q\\\"\",\"properties\":{}}");

            using WotDocument document = WotDocument.Parse(json);

            Assert.That(
                document.RootElement.GetProperty("title").GetString(),
                Is.EqualTo("\U0001F600 \"q\""));
        }

        [Test]
        public async Task ExternalSchemaOutcomesAreKeptPerAffordanceAsync()
        {
            // Both affordances have the local BrowseName "Temp"; the catalog
            // keyed by it let the second (compatible) outcome mask the first
            // (incompatible) one.
            byte[] json = Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"TankType\",\"uav:browseName\":\"pump:TankType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1042\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{" +
                "\"Temp\":{\"type\":\"number\",\"uav:externalSchema\":\"bad.json\"}," +
                "\"Other\":{\"type\":\"number\",\"uav:browseName\":\"pump:Temp\"," +
                "\"uav:id\":\"nsu=urn:test:pump;s=x\",\"uav:externalSchema\":\"good.json\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document,
                null,
                null,
                null,
                null,
                new WotExternalSchemaResolver(new SchemaByNameProvider())).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Count(d => d.Code == WotDiagnosticCode.ExternalSchemaIncompatible),
                Is.EqualTo(1));
        }

        [Test]
        public async Task SourceSchemeCannotReplaceTheProjectionsOwnSchemeAsync()
        {
            // "plant" + "_" + "sc" is the name of the projection's own floor;
            // the source's nosec scheme used to overwrite it silently.
            string projection = Projection(
                "\"plant_sc\":{\"scheme\":\"uav:channelsec\"," +
                "\"uav:securityMode\":\"SignAndEncrypt\"," +
                "\"uav:securityPolicy\":\"Aes256_Sha256_RsaPss\"}",
                "plant_sc",
                ("plant", "urn:plant"));
            WotProjectionResolver resolver = ThingResolver(
                ("urn:plant", Source("urn:plant", "sc", "{\"scheme\":\"nosec\"}", "p1")));

            WotConversionResult<WotDocument> result =
                await ResolveAsync(resolver, projection).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ProjectionSecurityConflict),
                    Is.True);
            });
        }

        [Test]
        public async Task TwoSourceSchemesWithOneQualifiedNameAreRefusedAsync()
        {
            // "line" + "2_sc" and "line_2" + "sc" both qualify to "line_2_sc".
            string projection = Projection(
                "\"nosec_sc\":{\"scheme\":\"nosec\"}",
                "nosec_sc",
                ("line", "urn:line"),
                ("line_2", "urn:line2"));
            WotProjectionResolver resolver = ThingResolver(
                ("urn:line", Source("urn:line", "2_sc", "{\"scheme\":\"nosec\"}", "p1")),
                ("urn:line2", Source(
                    "urn:line2",
                    "sc",
                    "{\"scheme\":\"uav:channelsec\",\"uav:securityMode\":\"SignAndEncrypt\"," +
                    "\"uav:securityPolicy\":\"Aes256_Sha256_RsaPss\"}",
                    "p2")));

            WotConversionResult<WotDocument> result =
                await ResolveAsync(resolver, projection).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ProjectionSecurityConflict),
                    Is.True);
            });
        }

        [Test]
        public async Task AnIdenticalProjectionSchemeIsSharedAsync()
        {
            const string definition = "{\"scheme\":\"nosec\"}";
            string projection = Projection(
                "\"plant_sc\":" + definition,
                "plant_sc",
                ("plant", "urn:plant"));
            WotProjectionResolver resolver = ThingResolver(
                ("urn:plant", Source("urn:plant", "sc", definition, "p1")));

            WotConversionResult<WotDocument> result =
                await ResolveAsync(resolver, projection).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(
                result.Value!.RootElement.GetProperty("properties").GetProperty("p1")
                    .GetProperty("forms")[0].GetProperty("security")[0].GetString(),
                Is.EqualTo("plant_sc"));
        }

        private static string Projection(
            string securityDefinitions,
            string security,
            params (string Name, string Href)[] sources)
        {
            var projects = new StringBuilder();
            foreach ((string name, string href) in sources)
            {
                if (projects.Length > 0)
                {
                    projects.Append(',');
                }
                projects.Append("{\"uav:sourceName\":\"").Append(name)
                    .Append("\",\"href\":\"").Append(href)
                    .Append("\",\"type\":\"application/td+json\",\"uav:routing\":\"source\"," +
                        "\"uav:selectAll\":true}");
            }
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"tm\":\"https://www.w3.org/2019/wot/tm#\"}]," +
                "\"@type\":[\"Thing\",\"uav:projection\"],\"id\":\"urn:view\",\"title\":\"View\"," +
                "\"uav:scenario\":\"http://example.com/scenario/View\"," +
                "\"securityDefinitions\":{" + securityDefinitions + "}," +
                "\"security\":\"" + security + "\"," +
                "\"uav:projects\":[" + projects + "]}";
        }

        private static string Source(string id, string scheme, string definition, string property)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"id\":\"" + id + "\",\"title\":\"Source\"," +
                "\"securityDefinitions\":{\"" + scheme + "\":" + definition + "}," +
                "\"security\":\"" + scheme + "\",\"base\":\"opc.tcp://host:4840\"," +
                "\"properties\":{\"" + property + "\":{\"type\":\"number\"," +
                "\"forms\":[{\"href\":\"/?id=nsu=urn:x;s=" + property + "\",\"op\":[\"readproperty\"]}]}}}";
        }

        private static async Task<WotConversionResult<WotDocument>> ResolveAsync(
            WotProjectionResolver resolver,
            string projection)
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection));
            return await resolver.ResolveAsync(document).ConfigureAwait(false);
        }

        private static WotProjectionResolver ThingResolver(params (string Href, string Json)[] map)
        {
            return new WotProjectionResolver(
                new MapThingResolver(map.ToDictionary(e => e.Href, e => e.Json, StringComparer.Ordinal)));
        }

        private sealed class MapThingResolver(Dictionary<string, string> map) : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                System.Threading.CancellationToken cancellationToken)
            {
                return new ValueTask<WotResolverResult>(
                    map.TryGetValue(reference, out string? json)
                        ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json))
                        : WotResolverResult.NotFound);
            }
        }

        /// <summary>
        /// Answers "bad.json" with a string schema and anything else with a
        /// number schema.
        /// </summary>
        private sealed class SchemaByNameProvider : IWotSchemaResolver
        {
            public ValueTask<WotResolverResult> ResolveSchemaAsync(
                string reference,
                WotResolutionContext context,
                System.Threading.CancellationToken cancellationToken)
            {
                string body = reference.EndsWith("bad.json", StringComparison.Ordinal)
                    ? "{\"type\":\"string\"}"
                    : "{\"type\":\"number\"}";
                return new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(body), null));
            }
        }
    }
}
