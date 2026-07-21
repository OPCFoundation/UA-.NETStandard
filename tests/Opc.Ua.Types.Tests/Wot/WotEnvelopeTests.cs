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
    public class WotEnvelopeTests
    {
        [Test]
        public void EnvelopeRoundTripsAllNodeClassesAndExtensions()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(WotTestData.Serialize(restored), Is.EqualTo(WotTestData.Serialize(source)));
        }

        [Test]
        public void EnvelopeDigestIsLowercaseHex()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());

            string digest = document.RootElement
                .GetProperty("uav:nodeSet")
                .GetProperty("sha256")
                .GetString()!;

            Assert.That(digest, Has.Length.EqualTo(64));
            Assert.That(digest, Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public void FromNodeSetEmitsNativeAffordances()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingModel));

            Assert.That(document.Properties.ContainsKey("Speed"), Is.True);
            JsonElement speed = document.Properties["Speed"];
            Assert.That(speed.GetProperty("@type").GetString(), Is.EqualTo("uav:variableType"));
            Assert.That(speed.GetProperty("type").GetString(), Is.EqualTo("number"));
            Assert.That(speed.GetProperty("observable").GetBoolean(), Is.True);
            Assert.That(speed.GetProperty("uav:modellingRule").GetString(), Is.EqualTo("Mandatory"));

            Assert.That(document.Actions.ContainsKey("Reset"), Is.True);
            Assert.That(
                document.Actions["Reset"].GetProperty("uav:modellingRule").GetString(),
                Is.EqualTo("Optional"));

            Assert.That(document.Events.ContainsKey("OverTemperatureEventType"), Is.True);
            Assert.That(
                document.Events["OverTemperatureEventType"].GetProperty("uav:isEvent").GetBoolean(),
                Is.True);
        }

        [Test]
        public void FromNodeSetEmitsNativeProjectionForEveryNode()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());

            Assert.That(document.TryGetNativeProjection(out JsonElement projection), Is.True);
            JsonElement nodes = projection.GetProperty("nodes");
            Assert.That(nodes.GetArrayLength(), Is.EqualTo(WotTestData.CreateRichNodeSet().Items!.Length));

            JsonElement objectType = nodes.EnumerateArray()
                .First(n => n.GetProperty("nodeId").GetString() == "ns=1;i=1001");
            Assert.That(objectType.GetProperty("nodeClass").GetString(), Is.EqualTo("ObjectType"));
            Assert.That(objectType.GetProperty("browseName").GetString(), Is.EqualTo("1:MachineType"));
        }

        [Test]
        public void DigestMismatchProducesDiagnostic()
        {
            using WotDocument original = WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());
            string json = Encoding.UTF8.GetString(original.Utf8Json.ToArray());
            const string marker = "\"data\": \"";
            int index = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            char[] characters = json.ToCharArray();
            characters[index] = characters[index] == 'A' ? 'B' : 'A';

            using WotDocument tampered = WotDocument.Parse(
                Encoding.UTF8.GetBytes(new string(characters)));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(tampered);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DigestMismatch),
                Is.True);
        }

        [Test]
        public void NativeProjectionConflictIsReportedNotSilentlyResolved()
        {
            using WotDocument original = WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());
            string json = Encoding.UTF8.GetString(original.Utf8Json.ToArray());

            // Rewrite the plaintext BrowseName; the base64 envelope keeps the
            // authoritative value so the native projection now conflicts.
            string conflicted = json.Replace("1:MachineType", "1:Tampered", StringComparison.Ordinal);

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(conflicted));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True);
            Assert.That(result.HasErrors, Is.True);
        }

        [Test]
        public void EnvelopeRebuildsExactNodeSetForSourceGeneratorConsumption()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document.Utf8Json);

            Assert.That(WotTestData.Serialize(restored), Is.EqualTo(WotTestData.Serialize(source)));
        }
    }
}
