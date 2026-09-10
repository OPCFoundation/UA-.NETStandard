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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotNativeProjectionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnsupportedNativeGrammarLeavesReadableContentUsableAsync(bool asynchronous)
        {
            const string json =
                """
                {
                  "@context": {
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                    "ua": "http://opcfoundation.org/UA/"
                  },
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "PumpType",
                  "uav:id": "nsu=urn:test:future;s=PumpType",
                  "uav:browseName": "nsu=urn:test:future;PumpType",
                  "uav:nodes": {
                    "@type": "uav:NodeModel",
                    "profileVersion": "99.0",
                    "nodes": {"futureRecord": "not the current grammar"}
                  }
                }
                """;
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));

            WotConversionResult<UANodeSet> result = asynchronous
                ? await WotNodeSetConverter.ToNodeSetResultAsync(document).ConfigureAwait(false)
                : WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.Multiple(() =>
            {
                Assert.That(result.Value!.Items, Has.Length.EqualTo(1));
                Assert.That(result.Value.Items![0].NodeId, Is.EqualTo("ns=1;s=PumpType"));
                Assert.That(result.Value.Items[0], Is.TypeOf<UAObjectType>());
                Assert.That(
                    result.Diagnostics.Single(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid).Severity,
                    Is.EqualTo(WotDiagnosticSeverity.Warning));
            });

            using WotDocument roundTrip = WotNodeSetConverter.FromNodeSet(result.Value!);
            Assert.That(
                roundTrip.RootElement.GetProperty("uav:nodes").GetProperty("nodes")
                    .GetProperty("futureRecord").GetString(),
                Is.EqualTo("not the current grammar"));
            Assert.That(roundTrip.RootElement.GetProperty("uav:nodes")
                .GetProperty("profileVersion").GetString(), Is.EqualTo("99.0"));
        }

        [Test]
        public void UnsupportedNativeOnlyDocumentDoesNotInventReadableContent()
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                """
                {"uav:nodes":{"@type":"uav:NodeModel","profileVersion":"99.0","nodes":{}}}
                """));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NoConvertibleContent), Is.True);
        }

        [Test]
        public void MalformedSupportedProjectionDoesNotFallBackToReadableContent()
        {
            var root = new JsonObject
            {
                ["@type"] = "tm:ThingModel",
                ["title"] = "PumpType",
                ["uav:nodes"] = new JsonObject
                {
                    ["@type"] = "uav:NodeModel",
                    ["profileVersion"] = "1.0",
                    ["nodes"] = new JsonObject()
                }
            };
            using WotDocument document = WotDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(root));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
        }

        [Test]
        public async Task UnsupportedProjectionDoesNotBypassReadableTypeResolutionAsync()
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                """
                {
                  "@context":{"uav":"http://opcfoundation.org/UA/WoT-Binding/","ua":"http://opcfoundation.org/UA/"},
                  "@type":["Thing","uav:object"],
                  "uav:id":"nsu=urn:test:future;s=Pump",
                  "links":[{"rel":"ua:HasTypeDefinition","href":"nsu=urn:missing;i=1000"}],
                  "uav:nodes":{"@type":"uav:NodeModel","profileVersion":"99.0","nodes":{}}
                }
                """));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnresolvedTypeBinding),
                Is.True,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void NativeProjectionReconstructsNodeSetWithoutEnvelope()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();
            byte[] json = BuildNativeOnlyDocument(source);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(json);

            NodeSetComparisonResult comparison = NodeSetComparer.Compare(source, restored);
            Assert.That(
                comparison.AreEquivalent,
                Is.True,
                string.Join("; ", comparison.Differences));
        }

        [Test]
        public void NativeReconstructionPreservesNodeClassesAndReferences()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();
            byte[] json = BuildNativeOnlyDocument(source);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(json);

            Assert.That(restored.Items, Has.Length.EqualTo(3));
            UAVariable variable = restored.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.BrowseName, Is.EqualTo("1:PumpSpeed"));
            Assert.That(variable.DataType, Is.EqualTo("Double"));
            Assert.That(variable.AccessLevel, Is.EqualTo(3));
            Assert.That(
                variable.References!.Any(r =>
                    r.ReferenceType == "HasModellingRule" && r.Value == "i=78"),
                Is.True);

            UAMethod method = restored.Items!.OfType<UAMethod>().Single();
            Assert.That(method.BrowseName, Is.EqualTo("1:Reset"));
        }

        [Test]
        public void NativeReconstructionIsDeterministic()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();
            byte[] json = BuildNativeOnlyDocument(source);

            UANodeSet first = WotNodeSetConverter.ToNodeSet(json);
            UANodeSet second = WotNodeSetConverter.ToNodeSet(json);

            Assert.That(WotTestData.Serialize(first), Is.EqualTo(WotTestData.Serialize(second)));
        }

        [Test]
        public void NativeProjectionExposesDerivedTypeInformation()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: NativeOnly());
            JsonElement nodes = document.RootElement
                .GetProperty("uav:nodes")
                .GetProperty("nodes");

            JsonElement variable = nodes.EnumerateArray()
                .Single(n => n.GetProperty("nodeClass").GetString() == "Variable");
            Assert.That(variable.GetProperty("typeDefinition").GetString(), Is.EqualTo("i=63"));
            Assert.That(variable.GetProperty("modellingRule").GetString(), Is.EqualTo("Mandatory"));

            JsonElement objectType = nodes.EnumerateArray()
                .Single(n => n.GetProperty("nodeClass").GetString() == "ObjectType");
            Assert.That(objectType.GetProperty("superType").GetString(), Is.EqualTo("i=58"));
        }

        [Test]
        public void NativeReconstructionPreservesReferenceTypeInverseName()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            byte[] json = BuildNativeOnlyDocument(source);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(json);

            UAReferenceType referenceType = restored.Items!.OfType<UAReferenceType>().Single();
            Assert.That(referenceType.BrowseName, Is.EqualTo("1:Controls"));
            Assert.That(referenceType.Symmetric, Is.False);
            // The InverseName must be restored exactly, not silently dropped.
            Assert.That(referenceType.InverseName, Is.Not.Null,
                "A non-symmetric ReferenceType must retain its InverseName across the native projection.");
            Assert.That(referenceType.InverseName!, Has.Length.EqualTo(1));
            Assert.That(referenceType.InverseName[0].Value, Is.EqualTo("IsControlledBy"));
        }

        [Test]
        public void NativeReconstructionPreservesLocalizedInverseName()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            UAReferenceType sourceReference = source.Items!.OfType<UAReferenceType>().Single();
            // Exercise the localized-entry path: a locale plus a second entry.
            sourceReference.InverseName =
            [
                new Export.LocalizedText { Locale = "en", Value = "IsControlledBy" },
                new Export.LocalizedText { Locale = "de", Value = "WirdGesteuertVon" }
            ];

            byte[] json = BuildNativeOnlyDocument(source);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(json);

            UAReferenceType referenceType = restored.Items!.OfType<UAReferenceType>().Single();
            Assert.That(referenceType.InverseName!, Has.Length.EqualTo(2));
            Assert.That(referenceType.InverseName![0].Locale, Is.EqualTo("en"));
            Assert.That(referenceType.InverseName[0].Value, Is.EqualTo("IsControlledBy"));
            Assert.That(referenceType.InverseName[1].Locale, Is.EqualTo("de"));
            Assert.That(referenceType.InverseName[1].Value, Is.EqualTo("WirdGesteuertVon"));
        }

        private static byte[] BuildNativeOnlyDocument(UANodeSet source)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: NativeOnly());
            Assert.That(document.TryGetEnvelope(out _), Is.False);
            return document.Utf8Json.ToArray();
        }

        private static WotNodeSetConverterOptions NativeOnly()
        {
            return new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };
        }
    }
}
