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
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// A preservation envelope that cannot be trusted yields no NodeSet at
    /// all, rather than a plausible one rebuilt from the plaintext beside it.
    /// </summary>
    /// <remarks>
    /// The envelope is the authoritative form: when it is present, the
    /// plaintext affordances are a rendering of it and not an independent
    /// source. Falling back to synthesis after an integrity, encoding or XML
    /// failure would hand a caller a NodeSet that silently differs from the
    /// one the author signed, with only a diagnostic beside it that a caller
    /// reading <c>Value</c> never has to look at. Every failure here is
    /// therefore asserted twice: the diagnostic, and the absence of a value.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotEnvelopeFailClosedTests
    {
        /// <summary>
        /// A content type the reader does not know describes bytes it cannot
        /// interpret, so it stops rather than guessing they are NodeSet2 XML.
        /// </summary>
        [Test]
        public void AnUnsupportedContentTypeFailsClosed()
        {
            AssertFailsClosed(
                Envelope(contentType: "application/opcua-nodeset+json"),
                WotDiagnosticCode.UnsupportedContentType);
        }

        [Test]
        public void AMissingContentTypeFailsClosed()
        {
            AssertFailsClosed(
                "{\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"uav:browseName\":\"1:Pump\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\",\"encoding\":\"base64\"," +
                "\"sha256\":\"" + Digest(Payload()) + "\"," +
                "\"data\":\"" + Convert.ToBase64String(Payload()) + "\"}}",
                WotDiagnosticCode.UnsupportedContentType);
        }

        [Test]
        public void AnUnsupportedEncodingFailsClosed()
        {
            AssertFailsClosed(
                Envelope(encoding: "hex"),
                WotDiagnosticCode.UnsupportedEncoding);
        }

        [Test]
        public void AMissingDataMemberFailsClosed()
        {
            AssertFailsClosed(
                "{\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"uav:browseName\":\"1:Pump\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\"," +
                "\"contentType\":\"application/opcua-nodeset+xml\"," +
                "\"encoding\":\"base64\"," +
                "\"sha256\":\"" + Digest(Payload()) + "\"}}",
                WotDiagnosticCode.EnvelopeInvalid);
        }

        [Test]
        public void DataThatIsNotBase64FailsClosed()
        {
            AssertFailsClosed(
                Envelope(data: "not base64 at all!!"),
                WotDiagnosticCode.InvalidBase64);
        }

        [Test]
        public void AMissingDigestFailsClosed()
        {
            AssertFailsClosed(
                "{\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"uav:browseName\":\"1:Pump\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\"," +
                "\"contentType\":\"application/opcua-nodeset+xml\"," +
                "\"encoding\":\"base64\"," +
                "\"data\":\"" + Convert.ToBase64String(Payload()) + "\"}}",
                WotDiagnosticCode.InvalidDigest);
        }

        /// <summary>
        /// A digest that is present but not a string is as untrustworthy as an
        /// absent one, because nothing can be compared against it.
        /// </summary>
        [Test]
        public void ANonStringDigestFailsClosed()
        {
            AssertFailsClosed(
                "{\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"uav:browseName\":\"1:Pump\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\"," +
                "\"contentType\":\"application/opcua-nodeset+xml\"," +
                "\"encoding\":\"base64\",\"sha256\":12345," +
                "\"data\":\"" + Convert.ToBase64String(Payload()) + "\"}}",
                WotDiagnosticCode.InvalidDigest);
        }

        [Test]
        public void ADigestOfTheWrongLengthFailsClosed()
        {
            AssertFailsClosed(
                Envelope(digest: "abcdef"),
                WotDiagnosticCode.InvalidDigest);
        }

        [Test]
        public void ADigestThatDoesNotMatchThePayloadFailsClosed()
        {
            AssertFailsClosed(
                Envelope(digest: new string('a', 64)),
                WotDiagnosticCode.DigestMismatch);
        }

        /// <summary>
        /// A payload larger than the configured bound is refused before it is
        /// parsed, so a document cannot spend a reader's memory by asserting
        /// that it should.
        /// </summary>
        [Test]
        public void APayloadOverTheConfiguredBoundFailsClosed()
        {
            using WotDocument document = WotDocument.Parse(
                WotTestData.Utf8(Envelope()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document,
                new WotNodeSetConverterOptions { MaxNodeSetSize = 8 });

            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error &&
                        d.Code == WotDiagnosticCode.NodeSetTooLarge),
                    Is.True,
                    Messages(result));
            });
        }

        [Test]
        public void PayloadThatIsNotNodeSetXmlFailsClosed()
        {
            AssertFailsClosed(
                Envelope(payload: WotTestData.Utf8("<not-a-nodeset/>")),
                WotDiagnosticCode.MalformedNodeSet);
        }

        [Test]
        public void PayloadThatIsNotXmlAtAllFailsClosed()
        {
            AssertFailsClosed(
                Envelope(payload: WotTestData.Utf8("nothing resembling a document")),
                WotDiagnosticCode.MalformedNodeSet);
        }

        /// <summary>
        /// The affordances beside a broken envelope are never promoted to a
        /// source: a document that carries both and fails integrity yields
        /// nothing, even though the plaintext alone would have synthesized.
        /// </summary>
        [Test]
        public void PlaintextAffordancesDoNotRescueABrokenEnvelope()
        {
            string plaintext =
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"1:Speed\"}},";

            string withoutEnvelope =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                plaintext.TrimEnd(',') + "}";
            using WotDocument synthesizing = WotDocument.Parse(
                WotTestData.Utf8(withoutEnvelope));
            Assert.That(
                WotNodeSetConverter.ToNodeSetResult(synthesizing).Value,
                Is.Not.Null,
                "The plaintext alone has to synthesize, or the contrast below " +
                "proves nothing.");

            string withBrokenEnvelope =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                plaintext +
                "\"uav:nodeSet\":{\"@type\":\"uav:nodeSet\"," +
                "\"contentType\":\"application/opcua-nodeset+xml\"," +
                "\"encoding\":\"base64\",\"sha256\":\"" + new string('a', 64) + "\"," +
                "\"data\":\"" + Convert.ToBase64String(Payload()) + "\"}}";

            AssertFailsClosed(withBrokenEnvelope, WotDiagnosticCode.DigestMismatch);
        }

        private static void AssertFailsClosed(string json, WotDiagnosticCode code)
        {
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(json));

            WotConversionResult<UANodeSet>? result = null;
            Assert.That(
                () => result = WotNodeSetConverter.ToNodeSetResult(document),
                Throws.Nothing,
                "A malformed envelope is a diagnostic, never an exception.");

            Assert.Multiple(() =>
            {
                Assert.That(
                    result!.Value,
                    Is.Null,
                    "An untrustworthy envelope yields no NodeSet, so no caller " +
                    "reading Value can act on a rebuilt approximation.");
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error && d.Code == code),
                    Is.True,
                    Messages(result));
            });
        }

        private static string Messages(WotConversionResult<UANodeSet> result)
        {
            return string.Join(
                "; ", result.Diagnostics.Select(d => d.Code + ": " + d.Message));
        }

        private static byte[] Payload()
        {
            return WotTestData.Utf8(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>urn:test:pump</Uri></NamespaceUris>" +
                "</UANodeSet>");
        }

        private static string Envelope(
            string contentType = "application/opcua-nodeset+xml",
            string encoding = "base64",
            string? digest = null,
            string? data = null,
            byte[]? payload = null)
        {
            byte[] bytes = payload ?? Payload();
            return "{\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"uav:browseName\":\"1:Pump\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\"," +
                "\"contentType\":\"" + contentType + "\"," +
                "\"encoding\":\"" + encoding + "\"," +
                "\"sha256\":\"" + (digest ?? Digest(bytes)) + "\"," +
                "\"data\":\"" + (data ?? Convert.ToBase64String(bytes)) + "\"}}";
        }

        private static string Digest(byte[] data)
        {
#if NET6_0_OR_GREATER
            byte[] hash = SHA256.HashData(data);
#else
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(data);
            }
#endif
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
