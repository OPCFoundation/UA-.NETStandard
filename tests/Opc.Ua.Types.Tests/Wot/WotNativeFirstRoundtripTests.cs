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
    public class WotNativeFirstRoundtripTests
    {
        [Test]
        public void DefaultConversionUsesCompleteNativeProjectionWithoutEnvelope()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.TryGetNativeProjection(out JsonElement projection), Is.True);
            Assert.That(
                projection.GetProperty("profileVersion").GetString(),
                Is.EqualTo("1.0"));
            Assert.That(document.TryGetEnvelope(out _), Is.False);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            NodeSetComparisonResult comparison = NodeSetComparer.Compare(source, restored);
            Assert.That(
                comparison.AreEquivalent,
                Is.True,
                string.Join("; ", comparison.Differences));
        }

        [Test]
        public void NeverModeProvesCompleteSchemaRoundtripWithoutFallback()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(source, options: options);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.TryGetEnvelope(out _), Is.False);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document, options);
            Assert.That(
                WotTestData.Serialize(restored),
                Is.EqualTo(WotTestData.Serialize(source)));
        }

        [Test]
        public void WhenRequiredFallsBackOnlyWhenNativeProjectionIsBounded()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var fallback = new WotNodeSetConverterOptions
            {
                MaxNodeCount = 1,
                PreservationMode = WotNodeSetPreservationMode.WhenRequired
            };

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(source, options: fallback);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.TryGetEnvelope(out _), Is.True);
            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NativeProjectionIncomplete),
                Is.True);

            var nativeOnly = new WotNodeSetConverterOptions
            {
                MaxNodeCount = 1,
                PreservationMode = WotNodeSetPreservationMode.Never
            };
            WotConversionResult<WotDocument> rejected =
                WotNodeSetConverter.FromNodeSetResult(source, options: nativeOnly);
            Assert.That(rejected.Value, Is.Null);
            Assert.That(rejected.HasErrors, Is.True);
        }

        [Test]
        public void UnknownJsonLdResidueSurvivesTwoEnvelopeFreeRoundtrips()
        {
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"vendor\":\"urn:vendor:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
                "\"vendor:root\":{\"b\":2,\"a\":1}," +
                "\"properties\":{\"speed\":{" +
                "\"@type\":\"uav:variableType\",\"uav:browseName\":\"1:Speed\"," +
                "\"type\":\"number\",\"readOnly\":true,\"observable\":true," +
                "\"forms\":[{\"href\":\"opc.tcp://example.test:4840\"," +
                "\"op\":[\"readproperty\"]}]," +
                "\"vendor:quality\":{\"mode\":\"good\"}}}}";

            UANodeSet firstNodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            Assert.That(
                firstNodeSet.Extensions!.Any(e =>
                    e.LocalName == "WoTJsonResidue" &&
                    e.NamespaceURI == WotNodeSetConverter.VocabularyNamespace),
                Is.True);

            using WotDocument first = WotNodeSetConverter.FromNodeSet(firstNodeSet);
            Assert.That(first.TryGetEnvelope(out _), Is.False);
            AssertResidue(first);

            UANodeSet secondNodeSet = WotNodeSetConverter.ToNodeSet(first);
            using WotDocument second = WotNodeSetConverter.FromNodeSet(secondNodeSet);
            Assert.That(second.TryGetEnvelope(out _), Is.False);
            AssertResidue(second);
        }

        [Test]
        public void ContextAndMappedLinkResidueUseStableSelectors()
        {
            const string json =
                "{" +
                "\"@context\":[{\"vendor\":\"urn:vendor:\"}," +
                "\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"extra\":\"urn:extra:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
                "\"links\":[{\"rel\":\"tm:extends\"," +
                "\"href\":\"nsu=urn:base;i=1001\",\"hreflang\":\"en\"}]}";

            UANodeSet nodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement context = restored.RootElement.GetProperty("@context");
            Assert.That(
                context[1].GetProperty("extra").GetString(),
                Is.EqualTo("urn:extra:"));
            Assert.That(
                context.EnumerateArray().Any(item =>
                    item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("vendor", out JsonElement vendor) &&
                    vendor.GetString() == "urn:vendor:"),
                Is.True);

            JsonElement links = restored.RootElement.GetProperty("links");
            Assert.That(links.GetArrayLength(), Is.EqualTo(1));
            Assert.That(links[0].GetProperty("rel").GetString(), Is.EqualTo("tm:extends"));
            Assert.That(
                links[0].GetProperty("href").GetString(),
                Is.EqualTo("nsu=urn:base;i=1001"));
            Assert.That(links[0].GetProperty("hreflang").GetString(), Is.EqualTo("en"));

            UANodeSet secondNodeSet = WotNodeSetConverter.ToNodeSet(restored);
            using WotDocument second = WotNodeSetConverter.FromNodeSet(secondNodeSet);
            JsonElement secondLink = second.RootElement.GetProperty("links")[0];
            Assert.That(secondLink.GetProperty("rel").GetString(), Is.EqualTo("tm:extends"));
            Assert.That(secondLink.GetProperty("hreflang").GetString(), Is.EqualTo("en"));
        }

        [Test]
        public void ResidueHonorsConfiguredDepthAboveFrameworkDefault()
        {
            const int depth = 70;
            var nested = new StringBuilder();
            for (int ii = 0; ii < depth; ii++)
            {
                nested.Append("{\"next\":");
            }
            nested.Append("\"leaf\"");
            for (int ii = 0; ii < depth; ii++)
            {
                nested.Append('}');
            }

            string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"DeepType\",\"uav:browseName\":\"1:DeepType\"," +
                "\"vendor:deep\":" + nested + "}";
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 96 };

            using WotDocument source = WotDocument.Parse(
                Encoding.UTF8.GetBytes(json),
                options);
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(source, options);
            using WotDocument restored =
                WotNodeSetConverter.FromNodeSet(nodeSet, options: options);

            JsonElement current = restored.RootElement.GetProperty("vendor:deep");
            for (int ii = 0; ii < depth; ii++)
            {
                current = current.GetProperty("next");
            }
            Assert.That(current.GetString(), Is.EqualTo("leaf"));
        }

        [Test]
        public void ResiduePointerBeyondConfiguredDepthProducesDiagnostic()
        {
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"BoundedType\",\"uav:browseName\":\"1:BoundedType\"," +
                "\"vendor:value\":1}";

            UANodeSet nodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            System.Xml.XmlElement residue = nodeSet.Extensions!.Single(e =>
                e.LocalName == "WoTJsonResidue");
            System.Xml.XmlElement member = residue.ChildNodes
                .OfType<System.Xml.XmlElement>()
                .Single();
            member.SetAttribute("Pointer", string.Concat(Enumerable.Repeat("/x", 12)));

            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 8 };
            WotConversionResult<WotDocument> result = null;
            Assert.That(
                () => result = WotNodeSetConverter.FromNodeSetResult(
                    nodeSet,
                    options: options),
                Throws.Nothing);
            using WotDocument document = result!.Value!;
            Assert.That(result.HasErrors, Is.True);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ResidueUsesSameAffordanceCollisionKeysAsReadableMapping()
        {
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"CollisionType\",\"uav:browseName\":\"1:CollisionType\"," +
                "\"properties\":{" +
                "\"first\":{\"uav:browseName\":\"1:Temp\",\"type\":\"number\"," +
                "\"vendor:value\":\"first\"}," +
                "\"second\":{\"uav:browseName\":\"2:Temp\",\"type\":\"number\"," +
                "\"vendor:value\":\"second\"}}}";

            UANodeSet nodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement properties = restored.RootElement.GetProperty("properties");
            Assert.That(
                properties.GetProperty("Temp").GetProperty("vendor:value").GetString(),
                Is.EqualTo("first"));
            Assert.That(
                properties.GetProperty("Temp_2").GetProperty("vendor:value").GetString(),
                Is.EqualTo("second"));
        }

        private static void AssertResidue(WotDocument document)
        {
            JsonElement root = document.RootElement;
            Assert.That(
                root.GetProperty("vendor:root").GetProperty("b").GetInt32(),
                Is.EqualTo(2));

            JsonElement speed = root
                .GetProperty("properties")
                .GetProperty("Speed");
            Assert.That(
                speed.GetProperty("vendor:quality").GetProperty("mode").GetString(),
                Is.EqualTo("good"));
            Assert.That(
                speed.GetProperty("forms")[0].GetProperty("op")[0].GetString(),
                Is.EqualTo("readproperty"));

            JsonElement context = root.GetProperty("@context");
            Assert.That(
                context[1].GetProperty("vendor").GetString(),
                Is.EqualTo("urn:vendor:"));
        }
    }
}
