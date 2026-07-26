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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Tests for WotNodeSetConverter preservation modes, byte-archival fallback,
    /// and reconstruction paths.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotConverterPreservationTests
    {
        [Test]
        public void PreservationModeAlwaysEmitsEnvelopeEvenForCompleteNodeSet()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Always
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet, null, options);

            Assert.That(document.TryGetEnvelope(out _), Is.True);
        }

        [Test]
        public void PreservationModeNeverSucceedsWhenNativeProjectionIsComplete()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet, null, options);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.HasErrors, Is.False);
        }

        [Test]
        public void PreservationModeNeverFailsWhenNativeProjectionIncomplete()
        {
            // MaxNodeCount=2 forces a NodeCountExceeded error during Write,
            // making nativeComplete=false and triggering NativeProjectionIncomplete.
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                MaxNodeCount = 2,
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet, null, options);

            Assert.That(result.Value, Is.Null);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionIncomplete),
                Is.True);
        }

        [Test]
        public void PreservationModeWhenRequiredOmitsEnvelopeForCompleteNodeSet()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.WhenRequired
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet, null, options);

            Assert.That(document.TryGetEnvelope(out _), Is.False);
        }

        [Test]
        public void PreservationModeWhenRequiredEmitsEnvelopeWithWarningWhenIncomplete()
        {
            // MaxNodeCount=2 forces incomplete native projection.
            // WhenRequired mode should emit envelope + warning (not error).
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                MaxNodeCount = 2,
                PreservationMode = WotNodeSetPreservationMode.WhenRequired
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet, null, options);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.NativeProjectionIncomplete &&
                    d.Severity == WotDiagnosticSeverity.Warning),
                Is.True);
            using WotDocument document = result.Value!;
            Assert.That(document.TryGetEnvelope(out _), Is.True);
        }

        [Test]
        public void FromNodeSetResultRejectsNodeSetExceedingMaxNodeSetSize()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions { MaxNodeSetSize = 1 };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet, null, options);

            Assert.That(result.Value, Is.Null);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NodeSetTooLarge),
                Is.True);
        }

        [Test]
        public void ToNodeSetFromBytesReconstructsNodeSet()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                null,
                new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document.Utf8Json);

            Assert.That(WotTestData.Serialize(restored), Is.EqualTo(WotTestData.Serialize(source)));
        }

        [Test]
        public void ToNodeSetFromDocumentWithNativeProjectionReconstructsNodes()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.TryGetNativeProjection(out _), Is.True);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(
                NodeSetComparer.Compare(source, restored).AreEquivalent,
                Is.True);
        }

        [Test]
        public void ConversionResultSuccessIsTrueWhenNoErrors()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(nodeSet);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.Not.Null);
            result.Value?.Dispose();
        }

        [Test]
        public void ConversionResultSuccessIsFalseWhenErrors()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions { MaxNodeSetSize = 1 };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet, null, options);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
        }

        [Test]
        public void UnknownDocumentKindProducesNoConvertibleContentDiagnostic()
        {
            // A document with no @type, no uav:nodeSet, and no uav:nodes is Unknown.
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"no-type-document\"}");
            using var document = WotDocument.Parse(json);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NoConvertibleContent),
                Is.True);
        }

        [Test]
        public void UnsupportedContentTypeInEnvelopeProducesDiagnostic()
        {
            const string json =
                "{\"@type\":\"tm:ThingModel\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\"," +
                "\"contentType\":\"application/xml\"," +
                "\"encoding\":\"base64\"," +
                "\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"," +
                "\"data\":\"AA==\"}}";

            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnsupportedContentType),
                Is.True);
        }

        [Test]
        public void TrySelectProjectionRootReturnsNullForEmptyNodeSet()
        {
            var emptyNodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Items = []
            };

            ExpandedNodeId? root = WotNodeSetConverter.TrySelectProjectionRoot(emptyNodeSet);

            Assert.That(root.HasValue, Is.False);
        }

        [Test]
        public void TrySelectProjectionRootReturnsExpandedNodeIdForRootedNodeSet()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();

            ExpandedNodeId? root = WotNodeSetConverter.TrySelectProjectionRoot(nodeSet);

            Assert.That(root.HasValue, Is.True);
            Assert.That(root!.Value.IsNull, Is.False);
        }

        [Test]
        public void TrySelectProjectionRootNullArgThrows()
        {
            Assert.That(
                () => WotNodeSetConverter.TrySelectProjectionRoot(null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void ToNodeSetResultSuccessPathFromThingModelJson()
        {
            // A minimal ThingModel with uav:nodes can be reconstructed.
            UANodeSet source = WotTestData.CreateRichNodeSet();
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void RestoreFromEnvelopeRejectsUnsupportedEncoding()
        {
            byte[] bytes = BuildMinimalEnvelopeDocumentBytes(encoding: "hex");
            using var document = WotDocument.Parse(bytes);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnsupportedEncoding),
                Is.True);
        }

        [Test]
        public void RestoreFromEnvelopeRejectsMissingDataField()
        {
            byte[] bytes = BuildMinimalEnvelopeDocumentBytes(omitData: true);
            using var document = WotDocument.Parse(bytes);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.EnvelopeInvalid),
                Is.True);
        }

        [Test]
        public void RestoreFromEnvelopeRejectsInvalidBase64Data()
        {
            byte[] bytes = BuildMinimalEnvelopeDocumentBytes(dataOverride: "not!valid!base64===");
            using var document = WotDocument.Parse(bytes);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidBase64),
                Is.True);
        }

        [Test]
        public void RestoreFromEnvelopeRejectsDecodedNodeSetExceedingMaxSize()
        {
            byte[] bytes = BuildMinimalEnvelopeDocumentBytes();
            using var document = WotDocument.Parse(bytes);

            var options = new WotNodeSetConverterOptions { MaxNodeSetSize = 1 };
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NodeSetTooLarge),
                Is.True);
        }

        [Test]
        public void RestoreFromEnvelopeRejectsMissingSha256Field()
        {
            byte[] bytes = BuildMinimalEnvelopeDocumentBytes(omitSha256: true);
            using var document = WotDocument.Parse(bytes);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidDigest),
                Is.True);
        }

        [Test]
        public void RestoreFromEnvelopeRejectsInvalidDigestFormat()
        {
            byte[] bytes = BuildMinimalEnvelopeDocumentBytes(sha256Override: "not-a-hex-digest");
            using var document = WotDocument.Parse(bytes);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidDigest),
                Is.True);
        }

        [Test]
        public void RestoreFromEnvelopeRejectsDigestMismatch()
        {
            byte[] bytes = BuildMinimalEnvelopeDocumentBytes(
                sha256Override: new string('0', 64));
            using var document = WotDocument.Parse(bytes);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DigestMismatch),
                Is.True);
        }

        private static byte[] BuildMinimalEnvelopeDocumentBytes(
            string contentType = "application/opcua-nodeset+xml",
            string encoding = "base64",
            bool omitData = false,
            bool omitSha256 = false,
            string sha256Override = null,
            string dataOverride = null)
        {
            var nodeSet = new Opc.Ua.Export.UANodeSet
            {
                Models = []
            };
            using var ms = new MemoryStream();
            nodeSet.Write(ms);
            byte[] nodeSetBytes = ms.ToArray();
            byte[] hash = SHA256.HashData(nodeSetBytes);
            string hashHex = Convert.ToHexString(hash).ToLowerInvariant();
            string base64 = Convert.ToBase64String(nodeSetBytes);

            string json =
                "{\"@type\":\"tm:ThingModel\",\"title\":\"T\",\"uav:nodeSet\":{" +
                "\"@type\":\"uav:nodeSet\"" +
                ",\"contentType\":\"" + contentType + "\"" +
                ",\"encoding\":\"" + encoding + "\"" +
                (!omitData ? ",\"data\":\"" + (dataOverride ?? base64) + "\"" : string.Empty) +
                (!omitSha256 ? ",\"sha256\":\"" + (sha256Override ?? hashHex) + "\"" : string.Empty) +
                "}}";
            return Encoding.UTF8.GetBytes(json);
        }
    }
}
