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

using System.IO;
using System.Linq;
using System.Text.Json;
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
            var options = new WotNodeSetConverterOptions();

            WotNativeModel model = WotNativeProjection.Build(source, options);

            WotNativeNode variable = model.Nodes.Single(n => n.NodeClass == "Variable");
            Assert.That(variable.TypeDefinition, Is.EqualTo("i=63"));
            Assert.That(variable.ModellingRule, Is.EqualTo("Mandatory"));

            WotNativeNode objectType = model.Nodes.Single(n => n.NodeClass == "ObjectType");
            Assert.That(objectType.SuperType, Is.EqualTo("i=58"));
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
                new Opc.Ua.Export.LocalizedText { Locale = "en", Value = "IsControlledBy" },
                new Opc.Ua.Export.LocalizedText { Locale = "de", Value = "WirdGesteuertVon" }
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
            var options = new WotNodeSetConverterOptions();
            WotNativeModel model = WotNativeProjection.Build(source, options);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("@type", "tm:ThingModel");
                writer.WriteString("title", "Reconstructed");
                writer.WritePropertyName("uav:nodes");
                WotNativeProjection.Write(writer, model);
                writer.WriteEndObject();
            }
            return stream.ToArray();
        }
    }
}
