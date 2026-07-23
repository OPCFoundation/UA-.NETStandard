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
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotResolverAndBoundsTests
    {
        [Test]
        public void ResolutionContextDetectsCycles()
        {
            var context = new WotResolutionContext();

            Assert.That(context.TryEnter(WotResolutionKind.Thing, "urn:a", out _), Is.True);
            Assert.That(context.TryEnter(WotResolutionKind.Thing, "urn:a", out var diagnostic), Is.False);
            Assert.That(diagnostic!.Code, Is.EqualTo(WotDiagnosticCode.ResolverCycle));
        }

        [Test]
        public void ResolutionContextEnforcesDepthLimit()
        {
            var context = new WotResolutionContext(new WotResolverOptions { MaxDepth = 1 });

            Assert.That(context.TryEnter(WotResolutionKind.Thing, "urn:a", out _), Is.True);
            Assert.That(context.TryEnter(WotResolutionKind.Thing, "urn:b", out var diagnostic), Is.False);
            Assert.That(diagnostic!.Code, Is.EqualTo(WotDiagnosticCode.ResolverDepthExceeded));
        }

        [Test]
        public void ResolutionContextEnforcesDocumentAndByteLimits()
        {
            var context = new WotResolutionContext(
                new WotResolverOptions { MaxDocuments = 1, MaxDepth = 10, MaxDocumentBytes = 5 });

            Assert.That(context.TryEnter(WotResolutionKind.Thing, "urn:a", out _), Is.True);
            Assert.That(context.TryAddBytes("urn:a", 10, out var byteLimit), Is.False);
            Assert.That(byteLimit!.Code, Is.EqualTo(WotDiagnosticCode.ResolverLimitExceeded));

            context.Leave("urn:a");
            Assert.That(context.TryEnter(WotResolutionKind.Thing, "urn:b", out var documentLimit), Is.False);
            Assert.That(documentLimit!.Code, Is.EqualTo(WotDiagnosticCode.ResolverLimitExceeded));
        }

        [Test]
        public void NullResolverNeverResolves()
        {
            var context = new WotResolutionContext();
            WotResolverResult result = NullWotResolver.Instance.ResolveThing("urn:a", context);
            Assert.That(result.Found, Is.False);
        }

        [Test]
        public void ResolverDrivenLinkResolutionFollowsRedirect()
        {
            var resolver = new MapResolver(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["urn:a"] = "{\"uav:congruentType\":\"urn:b\"}",
                ["urn:b"] = "{\"uav:id\":\"ns=2;i=99\"}"
            });

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(LinkModel("urn:a")));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document, null, resolver);

            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().Single();
            Assert.That(root.References!.Any(r => r.Value == "ns=2;i=99"), Is.True);
        }

        [Test]
        public void ResolverDrivenLinkResolutionDetectsCycle()
        {
            var resolver = new MapResolver(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["urn:a"] = "{\"uav:congruentType\":\"urn:b\"}",
                ["urn:b"] = "{\"uav:congruentType\":\"urn:a\"}"
            });

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(LinkModel("urn:a")));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document, null, resolver, new WotResolutionContext());

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ResolverCycle),
                Is.True);
        }

        [Test]
        public void OneResolutionContextIsCreatedPerTopLevelConversionNotPerLink()
        {
            // Regression test: TryResolveTargetNodeId used to fall back to
            // `new WotResolutionContext()` whenever it was handed a null
            // context, and that fallback ran once per resolved link. With
            // three sibling links sharing the same conversion, a document
            // budget of two must therefore be exhausted by the third link,
            // proving all links share one context seeded up front rather than
            // each silently getting a fresh, unbounded context of its own.
            var resolver = new MapResolver(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["urn:a"] = "{\"uav:id\":\"ns=2;i=101\"}",
                ["urn:b"] = "{\"uav:id\":\"ns=2;i=102\"}",
                ["urn:c"] = "{\"uav:id\":\"ns=2;i=103\"}"
            });
            var options = new WotNodeSetConverterOptions { MaxResolverDocuments = 2 };

            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(MultiLinkModel("urn:a", "urn:b", "urn:c")));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options, resolver);

            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().Single();
            Assert.That(
                root.References!.Count(r => r.Value is "ns=2;i=101" or "ns=2;i=102"),
                Is.EqualTo(2));
            Assert.That(root.References!.Any(r => r.Value == "ns=2;i=103"), Is.False);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ResolverLimitExceeded),
                Is.True);
        }

        [Test]
        public void MultipleLinksAccumulateAggregateByteLimitAcrossTheSameConversion()
        {
            // Both resolved documents are 23 bytes; a 30 byte total budget
            // allows the first but must reject the second. If a fresh
            // context were created per link (the bug this guards against),
            // both would fit under the budget individually and no diagnostic
            // would ever be produced.
            var resolver = new MapResolver(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["urn:a"] = "{\"uav:id\":\"ns=2;i=101\"}",
                ["urn:b"] = "{\"uav:id\":\"ns=2;i=102\"}"
            });
            var options = new WotNodeSetConverterOptions { MaxResolverTotalBytes = 30 };

            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(MultiLinkModel("urn:a", "urn:b")));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options, resolver);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ResolverLimitExceeded),
                Is.True);
        }

        [Test]
        public void SiblingLinkCycleDoesNotBlockAnUnrelatedSiblingLinkButIsStillReported()
        {
            var resolver = new MapResolver(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["urn:ok"] = "{\"uav:id\":\"ns=2;i=201\"}",
                ["urn:cyclic-a"] = "{\"uav:congruentType\":\"urn:cyclic-b\"}",
                ["urn:cyclic-b"] = "{\"uav:congruentType\":\"urn:cyclic-a\"}"
            });

            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(MultiLinkModel("urn:ok", "urn:cyclic-a")));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, null, resolver);

            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().Single();
            Assert.That(root.References!.Any(r => r.Value == "ns=2;i=201"), Is.True);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ResolverCycle),
                Is.True);
        }

        [Test]
        public void ConverterOptionsProjectToMatchingResolverOptions()
        {
            var options = new WotNodeSetConverterOptions
            {
                MaxResolverDepth = 3,
                MaxResolverDocuments = 4,
                MaxResolverDocumentBytes = 5,
                MaxResolverTotalBytes = 6
            };

            WotResolverOptions resolverOptions = options.ToResolverOptions();

            Assert.That(resolverOptions.MaxDepth, Is.EqualTo(3));
            Assert.That(resolverOptions.MaxDocuments, Is.EqualTo(4));
            Assert.That(resolverOptions.MaxDocumentBytes, Is.EqualTo(5));
            Assert.That(resolverOptions.MaxTotalBytes, Is.EqualTo(6));
        }

        [Test]
        public void OptionsValidateRejectsNonPositiveResolverLimits()
        {
            Assert.That(
                () => new WotNodeSetConverterOptions { MaxResolverDocuments = 0 }.Validate(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new WotNodeSetConverterOptions { MaxResolverDocumentBytes = 0 }.Validate(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new WotNodeSetConverterOptions { MaxResolverTotalBytes = 0 }.Validate(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void OptionsValidateRejectsNonPositiveLimits()
        {
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 0 };
            Assert.That(() => options.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ParseRejectsOversizedDocuments()
        {
            var options = new WotNodeSetConverterOptions { MaxJsonDocumentSize = 8 };
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"a rather long value\"}");

            Assert.That(
                () => WotDocument.Parse(json, options),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParseEnforcesDepthLimit()
        {
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 2 };
            byte[] json = Encoding.UTF8.GetBytes("{\"a\":{\"b\":{\"c\":1}}}");

            Assert.That(
                () => WotDocument.Parse(json, options),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void MalformedJsonThrows()
        {
            Assert.That(
                () => WotDocument.Parse(Encoding.UTF8.GetBytes("{ not json")),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void InvalidBase64EnvelopeIsReported()
        {
            const string json =
                "{\"@type\":\"tm:ThingModel\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\",\"contentType\":\"application/opcua-nodeset+xml\"," +
                "\"encoding\":\"base64\",\"data\":\"not*valid*base64\"}}";

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidBase64),
                Is.True);
        }

        [Test]
        public void DecodedNodeSetExceedingLimitIsReported()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateReconstructableNodeSet(),
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            var options = new WotNodeSetConverterOptions { MaxNodeSetSize = 16 };

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NodeSetTooLarge),
                Is.True);
        }

        private static string LinkModel(string href)
        {
            return MultiLinkModel(href);
        }

        private static string MultiLinkModel(params string[] hrefs)
        {
            string links = string.Join(
                ",",
                hrefs.Select(href =>
                    "{\"rel\":\"uav:typedReference\",\"href\":\"" + href + "\",\"uav:refType\":\"i=47\"}"));
            return
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"links\":[" + links + "]}";
        }

        private sealed class MapResolver : IWotThingResolver
        {
            private readonly Dictionary<string, string> m_map;

            public MapResolver(Dictionary<string, string> map)
            {
                m_map = map;
            }

            public WotResolverResult ResolveThing(string reference, WotResolutionContext context)
            {
                return m_map.TryGetValue(reference, out var json)
                    ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json))
                    : WotResolverResult.NotFound;
            }
        }
    }
}
