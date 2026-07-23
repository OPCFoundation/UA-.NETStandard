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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: AlwaysPreserve());
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(WotTestData.Serialize(restored), Is.EqualTo(WotTestData.Serialize(source)));
        }

        [Test]
        public void EnvelopeDigestIsLowercaseHex()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateRichNodeSet(),
                options: AlwaysPreserve());

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
            using WotDocument original = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateRichNodeSet(),
                options: AlwaysPreserve());
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
            using WotDocument original = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateRichNodeSet(),
                options: AlwaysPreserve());
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

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: AlwaysPreserve());
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document.Utf8Json);

            Assert.That(WotTestData.Serialize(restored), Is.EqualTo(WotTestData.Serialize(source)));
        }

        [Test]
        public void MissingDigestIsRejectedAsMandatory()
        {
            using WotDocument original = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateReconstructableNodeSet(),
                options: AlwaysPreserve());
            string json = Encoding.UTF8.GetString(original.Utf8Json.ToArray());
            string withoutDigest = RemoveJsonStringProperty(json, "sha256");

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(withoutDigest));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidDigest),
                Is.True);
        }

        [Test]
        public void MalformedDigestIsRejected()
        {
            using WotDocument original = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateReconstructableNodeSet(),
                options: AlwaysPreserve());
            string json = Encoding.UTF8.GetString(original.Utf8Json.ToArray());
            string malformed = ReplaceJsonStringProperty(json, "sha256", "not-a-valid-digest");

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(malformed));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidDigest),
                Is.True);
        }

        [Test]
        public void MalformedNodeSetXmlProducesDiagnosticInsteadOfThrowing()
        {
            byte[] payload = Encoding.UTF8.GetBytes("this is not a valid NodeSet2 XML document at all");
            string json = BuildEnvelopeJson(payload);

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));

            WotConversionResult<UANodeSet> result = null;
            Assert.That(
                () => result = WotNodeSetConverter.ToNodeSetResult(document),
                Throws.Nothing);
            Assert.That(result!.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.MalformedNodeSet),
                Is.True);
        }

        [Test]
        public void UnsupportedEncodingIsRejected()
        {
            using WotDocument original = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateReconstructableNodeSet(),
                options: AlwaysPreserve());
            string json = Encoding.UTF8.GetString(original.Utf8Json.ToArray());
            string tampered = ReplaceJsonStringProperty(json, "encoding", "base64url");

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(tampered));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnsupportedEncoding),
                Is.True);
        }

        [Test]
        public void Base64IsTheOnlyAcceptedEncodingPerSpecAndRoundTrips()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: AlwaysPreserve());

            string encoding = document.RootElement
                .GetProperty("uav:nodeSet")
                .GetProperty("encoding")
                .GetString()!;
            Assert.That(encoding, Is.EqualTo("base64"));

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(WotTestData.Serialize(restored), Is.EqualTo(WotTestData.Serialize(source)));
        }

        private static string RemoveJsonStringProperty(string json, string propertyName)
        {
            return Regex.Replace(
                json,
                "\"" + Regex.Escape(propertyName) + "\":\\s*\"[^\"]*\",?\\s*",
                string.Empty);
        }

        private static WotNodeSetConverterOptions AlwaysPreserve()
        {
            return new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Always
            };
        }

        private static string ReplaceJsonStringProperty(string json, string propertyName, string newValue)
        {
            return Regex.Replace(
                json,
                "\"" + Regex.Escape(propertyName) + "\":\\s*\"[^\"]*\"",
                "\"" + propertyName + "\": \"" + newValue + "\"");
        }

        private static string BuildEnvelopeJson(byte[] nodeSetBytes)
        {
            byte[] digest = ComputeSha256(nodeSetBytes);
            return "{\"@type\":\"tm:ThingModel\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\",\"contentType\":\"application/opcua-nodeset+xml\"," +
                "\"encoding\":\"base64\"," +
                "\"sha256\":\"" + ToLowerHexString(digest) + "\"," +
                "\"data\":\"" + Convert.ToBase64String(nodeSetBytes) + "\"}}";
        }

        private static byte[] ComputeSha256(byte[] data)
        {
#if NET6_0_OR_GREATER
            return SHA256.HashData(data);
#else
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
#endif
        }

        private static string ToLowerHexString(byte[] data)
        {
            var builder = new StringBuilder(data.Length * 2);
            foreach (byte value in data)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
