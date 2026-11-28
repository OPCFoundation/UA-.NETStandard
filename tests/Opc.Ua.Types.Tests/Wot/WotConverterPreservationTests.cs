// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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
        public async Task IncompleteNativeProjectionRoundTripsThroughEnvelope(
            [Values(WotNodeSetPreservationMode.WhenRequired, WotNodeSetPreservationMode.Always)]
            WotNodeSetPreservationMode forwardMode,
            [Values] WotNodeSetPreservationMode reverseMode,
            [Values] WotConformanceMode conformanceMode,
            [Values(false, true)] bool legacyProjection)
        {
            const string xml = """
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>http://example.org/Repro/</Uri></NamespaceUris>
                  <Models>
                    <Model ModelUri="http://example.org/Repro/" Version="1.0.0" PublicationDate="2025-01-01T00:00:00Z">
                      <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.03"
                        PublicationDate="2023-12-15T00:00:00Z" />
                    </Model>
                  </Models>
                  <Aliases><Alias Alias="HasSubtype">i=45</Alias></Aliases>
                  <UADataType NodeId="ns=1;i=3001" BrowseName="1:MultiplierKind">
                    <DisplayName>MultiplierKind</DisplayName>
                    <References><Reference ReferenceType="HasSubtype" IsForward="false">i=29</Reference></References>
                    <Definition Name="1:MultiplierKind">
                      <Field Name="m" Value="-3" /><Field Name="" Value="0" /><Field Name="k" Value="3" />
                    </Definition>
                  </UADataType>
                </UANodeSet>
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            UANodeSet source = UANodeSet.Read(stream)!;
            WotConversionResult<WotDocument> forward = WotNodeSetConverter.FromNodeSetResult(
                source, options: new WotNodeSetConverterOptions { PreservationMode = forwardMode });
            Assert.That(forward.HasErrors, Is.False);
            Assert.That(forward.Diagnostics.Any(d =>
                d.Code == WotDiagnosticCode.NativeProjectionIncomplete &&
                d.Severity == WotDiagnosticSeverity.Warning), Is.True);
            using WotDocument generated = forward.Value!;
            Assert.That(generated.TryGetEnvelope(out _), Is.True);
            Assert.That(generated.TryGetNativeProjection(out _), Is.False);
            var depthOptions = new WotNodeSetConverterOptions { MaxXmlDepth = 1 };
            WotConversionResult<WotDocument> depthLimitedForward =
                WotNodeSetConverter.FromNodeSetResult(source, options: depthOptions);
            Assert.That(depthLimitedForward.Value, Is.Null);
            Assert.That(depthLimitedForward.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DepthExceeded), Is.True);
            WotConversionResult<UANodeSet> depthLimitedReverse =
                WotNodeSetConverter.ToNodeSetResult(generated, depthOptions);
            Assert.That(depthLimitedReverse.Value, Is.Null);
            Assert.That(depthLimitedReverse.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DepthExceeded), Is.True);

            JsonNode json = JsonNode.Parse(generated.Utf8Json.Span)!;
            if (legacyProjection)
            {
                var diagnostics = new List<WotDiagnostic>();
                json["uav:nodes"] = JsonNode.Parse(WotNativeProjection.Write(
                    source, new WotNodeSetConverterOptions(), diagnostics));
                Assert.That(diagnostics, Is.Empty);
            }
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = reverseMode,
                ConformanceMode = conformanceMode
            };
            WotConversionResult<UANodeSet> sync = WotNodeSetConverter.ToNodeSetResult(document, options);
            WotConversionResult<UANodeSet> asyncResult =
                await WotNodeSetConverter.ToNodeSetResultAsync(document, options);
            Assert.Multiple(() =>
            {
                Assert.That(sync.HasErrors, Is.False, string.Join("; ", sync.Diagnostics));
                Assert.That(sync.Value, Is.Not.Null);
                Assert.That(NodeSetComparer.Compare(source, sync.Value!).AreEquivalent, Is.True);
                Assert.That(asyncResult.HasErrors, Is.False, string.Join("; ", asyncResult.Diagnostics));
                Assert.That(asyncResult.Value, Is.Not.Null);
                Assert.That(NodeSetComparer.Compare(source, asyncResult.Value!).AreEquivalent, Is.True);
            });

            string originalId = json["uav:id"]!.GetValue<string>();
            json["uav:id"] = "nsu=http://example.org/Repro/;i=3002";
            using (WotDocument tampered = WotDocument.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())))
            {
                WotConversionResult<UANodeSet> conflict = WotNodeSetConverter.ToNodeSetResult(tampered, options);
                Assert.That(conflict.HasErrors, Is.True);
                Assert.That(conflict.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict),
                    Is.True);
            }
            json["uav:id"] = originalId;

            if (legacyProjection)
            {
                WotConversionResult<UANodeSet> depthLimited = WotNodeSetConverter.ToNodeSetResult(
                    document, new WotNodeSetConverterOptions { MaxXmlDepth = 1 });
                Assert.That(depthLimited.HasErrors, Is.True);
                Assert.That(depthLimited.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DepthExceeded),
                    Is.True);

                json["uav:nodes"]!["nodes"]![0]!["browseName"] = "1:Tampered";
                using WotDocument tampered = WotDocument.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
                WotConversionResult<UANodeSet> conflict = WotNodeSetConverter.ToNodeSetResult(tampered, options);
                Assert.That(conflict.HasErrors, Is.True);
                Assert.That(conflict.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict),
                    Is.True);
            }

            ((UADataType)source.Items![0]).Definition!.Field![1].Name = "none";
            WotConversionResult<WotDocument> complete = WotNodeSetConverter.FromNodeSetResult(source);
            Assert.That(complete.HasErrors, Is.False);
            using WotDocument completeDocument = complete.Value!;
            Assert.That(completeDocument.TryGetEnvelope(out _), Is.False);
            WotConversionResult<UANodeSet> completeReverse = WotNodeSetConverter.ToNodeSetResult(completeDocument);
            Assert.That(completeReverse.HasErrors, Is.False, string.Join("; ", completeReverse.Diagnostics));
            Assert.That(NodeSetComparer.Compare(source, completeReverse.Value!).AreEquivalent, Is.True);
        }

        [Test]
        public void TruncatedProjectionIsRejectedWhileNewFallbackRestoresEnvelope()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var limited = new WotNodeSetConverterOptions { MaxNodeCount = 2 };
            WotConversionResult<WotDocument> forward = WotNodeSetConverter.FromNodeSetResult(
                source, options: limited);
            Assert.That(forward.HasErrors, Is.False);
            using WotDocument generated = forward.Value!;
            Assert.That(generated.TryGetNativeProjection(out _), Is.False);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(generated);
            Assert.That(result.HasErrors, Is.False, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(NodeSetComparer.Compare(source, result.Value!).AreEquivalent, Is.True);

            var diagnostics = new List<WotDiagnostic>();
            byte[] projection = WotNativeProjection.Write(source, limited, diagnostics);
            Assert.That(diagnostics.Any(d => d.Code == WotDiagnosticCode.NodeCountExceeded), Is.True);
            JsonNode json = JsonNode.Parse(generated.Utf8Json.Span)!;
            json["uav:nodes"] = JsonNode.Parse(projection);
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));

            WotConversionResult<UANodeSet> truncated = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(truncated.HasErrors, Is.True);
            Assert.That(truncated.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict), Is.True);

            WotConversionResult<UANodeSet> overLimit = WotNodeSetConverter.ToNodeSetResult(
                document, new WotNodeSetConverterOptions { MaxNodeCount = 1 });
            Assert.That(overLimit.HasErrors, Is.True);
            Assert.That(overLimit.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict), Is.True);

            json["uav:nodes"]!["nodes"]![0]!["browseName"] = "1:Tampered";
            using WotDocument tampered = WotDocument.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
            WotConversionResult<UANodeSet> conflict = WotNodeSetConverter.ToNodeSetResult(tampered);
            Assert.That(conflict.HasErrors, Is.True);
            Assert.That(conflict.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict), Is.True);
        }

        [Test]
        public void DeepEnvelopeWithConflictingProjectionReturnsDiagnostic()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            using WotDocument generated = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });
            var model = new ModelTableEntry { ModelUri = "urn:test:deep" };
            for (int i = 0; i < 40; i++)
            {
                model = new ModelTableEntry { ModelUri = "urn:test:deep", RequiredModel = [model] };
            }
            source.Models = [model];
            byte[] payload = WotTestData.Serialize(source);
#if NETFRAMEWORK
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(payload);
#else
            byte[] hash = SHA256.HashData(payload);
#endif
            string digest = string.Concat(Array.ConvertAll(
                hash, b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            JsonNode json = JsonNode.Parse(generated.Utf8Json.Span)!;
            json["uav:nodeSet"]!["data"] = Convert.ToBase64String(payload);
            json["uav:nodeSet"]!["sha256"] = digest;
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document, new WotNodeSetConverterOptions { MaxJsonDepth = 64 });

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict), Is.True);
        }

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

            ExpandedNodeId root = WotNodeSetConverter.TrySelectProjectionRoot(emptyNodeSet);

            Assert.That(root.IsNull, Is.True);
        }

        [Test]
        public void TrySelectProjectionRootReturnsExpandedNodeIdForRootedNodeSet()
        {
            UANodeSet nodeSet = WotTestData.CreateRichNodeSet();

            ExpandedNodeId root = WotNodeSetConverter.TrySelectProjectionRoot(nodeSet);

            Assert.That(root.IsNull, Is.False);
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
            string? sha256Override = null,
            string? dataOverride = null)
        {
            var nodeSet = new Opc.Ua.Export.UANodeSet
            {
                Models = []
            };
            using var ms = new MemoryStream();
            nodeSet.Write(ms);
            byte[] nodeSetBytes = ms.ToArray();
#if NETFRAMEWORK
            // SHA256.HashData is not available on .NET Framework.
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(nodeSetBytes);
#else
            byte[] hash = SHA256.HashData(nodeSetBytes);
#endif
            string hashHex = string.Concat(
                Array.ConvertAll(
                    hash,
                    b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
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
