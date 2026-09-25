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

        [Test]
        public async Task ADeepAffordanceWithinTheConfiguredDepthIsProjectedAsync()
        {
            // 70 nested arrays: inside the configured 128 levels, but deeper
            // than the 64 the clone used to re-parse with.
            string deep = new string('[', 70) + new string(']', 70);
            string source = Source("urn:plant", "sc", "{\"scheme\":\"nosec\"}", "p1")
                .Replace("{\"type\":\"number\",", "{\"type\":\"array\",\"const\":" + deep + ",");
            string projection = Projection(
                "\"nosec_sc\":{\"scheme\":\"nosec\"}",
                "nosec_sc",
                ("plant", "urn:plant"));
            WotProjectionResolver resolver = ThingResolver(("urn:plant", source));

            WotConversionResult<WotDocument> result =
                await ResolveAsync(resolver, projection).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(
                result.Value!.RootElement.GetProperty("properties").GetProperty("p1")
                    .GetProperty("const").GetArrayLength(),
                Is.EqualTo(1));
        }

        [Test]
        public void AnOversizedReadableDocumentIsReportedNotThrown()
        {
            // The readable document was parsed back (and threw FormatException)
            // before its size was checked.
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet(withInstrumentRange: false);
            var options = new WotNodeSetConverterOptions { MaxJsonDocumentSize = 256 };

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(source, null, options);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.JsonDocumentTooLarge),
                    Is.True);
            });
        }

        [TestCase("{\"uav:securityMode\":\"SignAndEncrypt\",\"uav:securityMode\":\"None\"}")]
        [TestCase("{\"uav:securityPolicy\":\"Aes256_Sha256_RsaPss\",\"uav:securityPolicy\":\"None\"}")]
        public void ASecurityFloorStatingAMemberTwiceIsRejected(string json)
        {
            // The last value used to win, so the floor could be restated weaker
            // than a first-wins reader (or a JCS canonicalizer) sees it.
            using var document = JsonDocument.Parse(json);

            bool parsed = WotSecurityFloor.TryParse(
                document.RootElement, out WotSecurityFloor? floor, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.False);
                Assert.That(floor, Is.Null);
                Assert.That(error, Does.Contain("more than once"));
            });
        }

        [Test]
        public void ASecurityFloorStatingEachMemberOnceIsAccepted()
        {
            using var document = JsonDocument.Parse(
                "{\"uav:securityMode\":\"SignAndEncrypt\",\"uav:securityPolicy\":\"Aes256_Sha256_RsaPss\"}");

            Assert.That(
                WotSecurityFloor.TryParse(document.RootElement, out WotSecurityFloor? floor, out _),
                Is.True);
            Assert.That(floor!.SecurityMode, Is.EqualTo("SignAndEncrypt"));
        }

        [Test]
        public async Task ProjectionNodeIdsAreIndexedWithAnEscapedNamespaceAsync()
        {
            // Part 6 5.1.12: the nsu= URI is percent-encoded with ';' reserved.
            // The projection index concatenated it raw, so the escaped id every
            // other producer writes missed.
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pumps\"," +
                "\"uav:nodes\":{\"namespaceUris\":[\"urn:acme;pumps\"]," +
                "\"nodes\":[{\"nodeId\":\"ns=1;i=1001\",\"nodeClass\":\"ObjectType\"}]}" +
                "}";

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            var resolver = new WotDocumentNodeResolver([document]);

            WotResolvedNode? resolved = await resolver
                .ResolveByNodeIdAsync(
                    "nsu=urn:acme%3Bpumps;i=1001",
                    WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(resolved, Is.Not.Null);
        }

        [Test]
        [CancelAfter(60000)]
        public void ALongSubtypeChainIsValidatedInLinearTime()
        {
            // Every entry used to walk its whole ancestor chain: N^2/2 steps.
            const int count = 20000;
            var definitions = new StringBuilder();
            for (int ii = 0; ii < count; ii++)
            {
                if (ii > 0)
                {
                    definitions.Append(',');
                }
                definitions.Append("{\"@id\":\"urn:t#T").Append(ii)
                    .Append("\",\"@type\":\"uav:StructureDefinition\",\"uav:dataTypeName\":\"demo:T")
                    .Append(ii).Append('"');
                if (ii > 0)
                {
                    definitions.Append(",\"uav:dataTypeSubtypeOf\":{\"@id\":\"urn:t#T")
                        .Append(ii - 1).Append("\"}");
                }
                definitions.Append('}');
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            WotConversionResult<UANodeSet> result = ConvertDataTypes(definitions.ToString());
            watch.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Where(d => d.Message.Contains("its own ancestor", StringComparison.Ordinal)),
                    Is.Empty);
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)));
            });
        }

        [Test]
        public void OnlyTheMembersOfASubtypeCycleAreTheirOwnAncestors()
        {
            // A -> B -> C -> B: A leads into the cycle but is not on it.
            WotConversionResult<UANodeSet> result = ConvertDataTypes(
                Definition("A", "B") + "," + Definition("B", "C") + "," + Definition("C", "B"));

            string[] cyclic = result.Diagnostics
                .Where(d => d.Message.Contains("its own ancestor", StringComparison.Ordinal))
                .Select(d => d.Location!.Reference!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(cyclic, Is.EqualTo(new[] { "demo:B", "demo:C" }));

            static string Definition(string name, string baseName)
            {
                return "{\"@id\":\"urn:t#" + name + "\",\"@type\":\"uav:StructureDefinition\"," +
                    "\"uav:dataTypeName\":\"demo:" + name + "\"," +
                    "\"uav:dataTypeSubtypeOf\":{\"@id\":\"urn:t#" + baseName + "\"}}";
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ManyComponentsOfOneOwnerAreLinkedInLinearTime(bool ownerFirst)
        {
            // Each child scanned every Node for its owner and copied the
            // owner's growing References array (twice): O(N^2) in both the
            // owner-first path and the reconcile path for an owner stated last.
            const int count = 60000;
            const string owner = "\"P\":{\"type\":\"number\",\"uav:id\":\"nsu=urn:x;s=P\"}";
            var properties = new StringBuilder();
            if (ownerFirst)
            {
                properties.Append(owner).Append(',');
            }
            for (int ii = 0; ii < count; ii++)
            {
                properties.Append("\"c").Append(ii)
                    .Append("\":{\"type\":\"number\",\"uav:componentOf\":[\"nsu=urn:x;s=P\"]},");
            }
            if (!ownerFirst)
            {
                properties.Append(owner).Append(',');
            }
            properties.Length--;

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                "\"properties\":{" + properties + "}}"));
            var options = new WotNodeSetConverterOptions
            {
                MaxAffordanceCount = count + 10,
                MaxNodeCount = 10 * count
            };

            var watch = System.Diagnostics.Stopwatch.StartNew();
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);
            watch.Stop();

            UANode ownerNode = result.Value!.Items!.First(n => n.BrowseName == "1:P");
            Assert.Multiple(() =>
            {
                Assert.That(
                    ownerNode.References!.Count(r => r.IsForward && r.ReferenceType == "HasComponent"),
                    Is.EqualTo(count));
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)));
            });
            TestContext.Out.WriteLine($"{count} components linked in {watch.Elapsed}.");
        }

        [Test]
        public void ManyNamespacesAreLookedUpWithoutScanningTheTable()
        {
            // Every nsu= identifier scanned the whole namespace table and every
            // new URI copied it: O(U^2) compares for U namespaces.
            const int count = 60000;
            WotConversionResult<UANodeSet> result = ConvertNamespaces(count, out TimeSpan elapsed);

            Assert.Multiple(() =>
            {
                Assert.That(result.Value!.NamespaceUris, Does.Contain("urn:ns" + (count - 1)));
                Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(20)));
            });
            TestContext.Out.WriteLine($"{count} namespaces converted in {elapsed}.");
        }

        [Test]
        public void MoreNamespacesThanANamespaceIndexAddressesAreReported()
        {
            // A NamespaceIndex is a UInt16: a table beyond 65535 URIs cannot be
            // addressed, and nothing bounded it before.
            WotConversionResult<UANodeSet> result = ConvertNamespaces(ushort.MaxValue + 5, out TimeSpan elapsed);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error &&
                        d.Message.Contains("UInt16 NamespaceIndex", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(
                    result.Value?.NamespaceUris?.Length ?? 0,
                    Is.LessThanOrEqualTo(ushort.MaxValue));
            });
            TestContext.Out.WriteLine($"{ushort.MaxValue + 5} namespaces converted in {elapsed}.");
        }

        private static WotConversionResult<UANodeSet> ConvertNamespaces(int count, out TimeSpan elapsed)
        {
            var properties = new StringBuilder();
            for (int ii = 0; ii < count; ii++)
            {
                if (ii > 0)
                {
                    properties.Append(',');
                }
                properties.Append("\"p").Append(ii).Append("\":{\"type\":\"number\",\"uav:id\":\"nsu=urn:ns")
                    .Append(ii).Append(";s=x\"}");
            }
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                "\"properties\":{" + properties + "}}"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);
            elapsed = watch.Elapsed;
            return result;
        }

        private static WotConversionResult<UANodeSet> ConvertDataTypes(string definitions)
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"demo\":\"http://example.com/demo/pump\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Thing\"," +
                "\"uav:dataTypeDefinitions\":[" + definitions + "]}"));
            return WotNodeSetConverter.ToNodeSetResult(document);
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
