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

#if NET10_0
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Wot;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Proves session-independent inspection, honest validation, planning and complete artifact round trips.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WotDocumentToolsTests
    {
        /// <summary>
        /// Ordinary W3C documents are not silently relabelled as Binding projections.
        /// </summary>
        [Test]
        public async Task InspectUnannotatedDocumentReportsUnknownAndDigestWithoutBodyAsync()
        {
            using var context = new WotDocumentTestContext();
            const string text =
                /*lang=json,strict*/
                """{"title":"Lamp","properties":{"power":{"type":"boolean","secret":"not-returned"}}}""";
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(WotDocumentTestContext.Input(text)).ConfigureAwait(false));

            Assert.That(result["documents"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("Unknown"));
            Assert.That(result["documents"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("Lamp"));
            Assert.That(result["documents"]![0]!["properties"]!["names"]![0]!.GetValue<string>(), Is.EqualTo("power"));
            Assert.That(result["provenance"]!["primarySource"]!["sha256"]!.GetValue<string>(),
                Is.EqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))));
            Assert.That(result.ToJsonString(), Does.Not.Contain("not-returned"));
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Parsing a generic JSON object does not certify W3C syntax beyond JSON or apply authoring rules.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ValidateUnannotatedDocumentExplicitlyClaimsSyntaxOnlyAsync(bool strict)
        {
            using var context = new WotDocumentTestContext();
            WotDocumentInput input = WotDocumentTestContext.Input(/*lang=json,strict*/ """{"title":"Lamp"}""");
            input.StrictAuthoring = strict;
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.ValidateDocumentAsync(input).ConfigureAwait(false));

            Assert.That(result["valid"]!.GetValue<bool>(), Is.True);
            Assert.That(result["projectionValidated"]!.GetValue<bool>(), Is.False);
            Assert.That(result["authoringChecksApplied"]!.GetValue<bool>(), Is.False);
            Assert.That(result["validationScope"]!.GetValue<string>(), Is.EqualTo("JSON syntax only"));
        }

        /// <summary>
        /// Read-only inline workflows require neither a transfer directory nor any UA session infrastructure.
        /// </summary>
        [Test]
        public async Task InlineInspectionValidationAndPlanningWorkWithoutTransferRootAsync()
        {
            using var context = new WotDocumentTestContext();
            context.Shared.TransferRoot = null;
            JsonObject inspected = WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                WotDocumentTestContext.Input()).ConfigureAwait(false));
            JsonObject validated = WotDocumentTestContext.Payload(await context.Tools.ValidateDocumentAsync(
                WotDocumentTestContext.Input()).ConfigureAwait(false));
            JsonObject planned = WotDocumentTestContext.Payload(await context.Tools.PlanBindingsAsync(
                WotDocumentTestContext.Input()).ConfigureAwait(false));
            Assert.That(inspected["documents"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("ThingModel"));
            Assert.That(validated["valid"]!.GetValue<bool>(), Is.True);
            Assert.That(planned["compiledFormCount"]!.GetValue<int>(), Is.Zero);
            Assert.That(planned["hasExecutableForms"]!.GetValue<bool>(), Is.False);
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Consumer acceptance and strict authoring are observably distinct.
        /// </summary>
        [Test]
        public async Task ConsumerPreservesUnknownVocabularyWhileStrictAuthoringReportsItAsync()
        {
            using var context = new WotDocumentTestContext();
            JsonObject document = JsonNode.Parse(WotDocumentTestContext.Model)!.AsObject();
            document["uav:futureTerm"] = "retained";
            WotDocumentInput input = WotDocumentTestContext.Input(document.ToJsonString());
            JsonObject consumer = WotDocumentTestContext.Payload(
                await context.Tools.ValidateDocumentAsync(input).ConfigureAwait(false));
            input.StrictAuthoring = true;
            JsonObject author = WotDocumentTestContext.Payload(
                await context.Tools.ValidateDocumentAsync(input).ConfigureAwait(false), error: true);

            Assert.That(consumer["valid"]!.GetValue<bool>(), Is.True);
            Assert.That(consumer["mode"]!.GetValue<string>(), Is.EqualTo("consumer"));
            Assert.That(author["valid"]!.GetValue<bool>(), Is.False);
            Assert.That(author["mode"]!.GetValue<string>(), Is.EqualTo("strictAuthoring"));
            Assert.That(author["diagnostics"]!["items"]!.AsArray().Any(
                item => item!["code"]!.GetValue<string>() == "UnknownVocabularyTerm"), Is.True);
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
        }

        /// <summary>
        /// Explicit preservation content still receives Binding validation even without a lexical document kind.
        /// </summary>
        [Test]
        public async Task ValidateDoesNotTreatAnInvalidPreservationEnvelopeAsSyntaxOnlyAsync()
        {
            using var context = new WotDocumentTestContext();
            var response = await context.Tools.ValidateDocumentAsync(
                WotDocumentTestContext.Input(/*lang=json,strict*/ """{"uav:nodeSet":{}}"""))
                .ConfigureAwait(false);
            // TEMP-DIAGNOSTIC: remove after the payload is captured.
            TestContext.Out.WriteLine("TEMP-PAYLOAD isError=" + response.IsError + " " +
                response.StructuredContent?.GetRawText());
            JsonObject result = WotDocumentTestContext.Payload(response, true);
            Assert.That(result["projectionValidated"]!.GetValue<bool>(), Is.True);
            Assert.That(result["valid"]!.GetValue<bool>(), Is.False);
            Assert.That(result["diagnostics"]!["errors"]!.GetValue<int>(), Is.GreaterThan(0));
        }

        /// <summary>
        /// Invalid syntax and non-object roots cannot produce successful inspection.
        /// </summary>
        [TestCase("")]
        [TestCase("{")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("\"text\"")]
        public async Task InspectRejectsEmptyMalformedAndNonObjectDocumentsAsync(string text)
        {
            using var context = new WotDocumentTestContext();
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(WotDocumentTestContext.Input(text)).ConfigureAwait(false),
                true);
            Assert.That(result["errorType"]!.GetValue<string>(), Is.Not.Empty);
            Assert.That(result["documents"], Is.Null);
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Explicit kind is required for planning an unannotated TD, and plans remain non-executable.
        /// </summary>
        [Test]
        public async Task PlanRequiresKindAndNeverActivatesAnExecutorAsync()
        {
            using var context = new WotDocumentTestContext();
            WotDocumentInput input = WotDocumentTestContext.Input(/*lang=json,strict*/ """
                {"title":"Lamp","properties":{"power":{"type":"boolean","forms":[
                  {"href":"https://device.invalid/power","op":"readproperty","contentType":"application/json"}]}}}
                """);
            JsonObject unknown = WotDocumentTestContext.Payload(
                await context.Tools.PlanBindingsAsync(input).ConfigureAwait(false), true);
            JsonObject planned = WotDocumentTestContext.Payload(
                await context.Tools.PlanBindingsAsync(input, WotDocumentKind.ThingDescription).ConfigureAwait(false));

            Assert.That(unknown["message"]!.GetValue<string>(), Does.Contain("kind"));
            Assert.That(planned["planningOnly"]!.GetValue<bool>(), Is.True);
            Assert.That(planned["hasExecutableForms"]!.GetValue<bool>(), Is.False);
            Assert.That(planned["compiledFormCount"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(planned["compiledForms"]![0]!["operation"]!.GetValue<string>(), Is.EqualTo("readproperty"));
            Assert.That(planned["compiledForms"]![0]!["isExecutable"]!.GetValue<bool>(), Is.False);
            Assert.That(planned["planners"]!.AsArray(), Has.Count.GreaterThanOrEqualTo(8));
            Assert.That(planned["planners"]!.AsArray().All(
                item => !item!["isExecutable"]!.GetValue<bool>() &&
                    item["version"]!.GetValue<string>().Length > 0 &&
                    item["maturity"]!.GetValue<string>().Length > 0),
                Is.True);
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Unsupported form mappings preserve planner diagnostics rather than returning an executable fallback.
        /// </summary>
        [Test]
        public async Task PlanReportsUnsupportedTargetMappingAtTheAuthoredPointerAsync()
        {
            using var context = new WotDocumentTestContext();
            WotDocumentInput input = WotDocumentTestContext.Input(/*lang=json,strict*/ """
                {"title":"Lamp","actions":{"reset":{"uav:mapToNodeId":"nsu=urn:test;i=1","forms":[
                  {"href":"https://device.invalid/reset"}]}}}
                """);
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.PlanBindingsAsync(input, WotDocumentKind.ThingDescription).ConfigureAwait(false),
                error: true);

            Assert.That(result["fullySupported"]!.GetValue<bool>(), Is.False);
            Assert.That(result["unsupportedFormCount"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(result["compiledFormCount"]!.GetValue<int>(), Is.Zero);
            Assert.That(result["bindingDiagnostics"]!["items"]!.AsArray().Any(
                item => item!["code"]!.GetValue<string>() == "TargetMappingNotOnProperty" &&
                    item["jsonPointer"]!.GetValue<string>().StartsWith("/actions/reset", StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// Consumer conversion keeps unknown vocabulary through XML and the complete ZIP representation.
        /// </summary>
        [Test]
        public async Task JsonNodeSetArchiveRoundTripRetainsUnknownVocabularyAsync()
        {
            using var context = new WotDocumentTestContext();
            JsonObject document = JsonNode.Parse(WotDocumentTestContext.Model)!.AsObject();
            document["vendor:note"] = "retained-verbatim";
            WotDocumentInput input = WotDocumentTestContext.Input(document.ToJsonString());
            JsonObject xmlResult = WotDocumentTestContext.Payload(
                await context.Tools.ToNodeSetAsync(input, "model.xml").ConfigureAwait(false));
            byte[] xml = await File.ReadAllBytesAsync(Path.Combine(context.Root, "model.xml")).ConfigureAwait(false);
            JsonObject archiveResult = WotDocumentTestContext.Payload(
                await context.Tools.FromNodeSetAsync(new WotDocumentInput
                {
                    Source = new WotDocumentSource { FilePath = "model.xml" }
                }, "model.zip").ConfigureAwait(false));

            Assert.That(xmlResult["artifact"]!["sha256"]!.GetValue<string>(),
                Is.EqualTo(Convert.ToHexString(SHA256.HashData(xml))));
            Assert.That(archiveResult["provenance"]!["primarySource"]!["sha256"]!.GetValue<string>(),
                Is.EqualTo(Convert.ToHexString(SHA256.HashData(xml))));
            using ZipArchive archive = await ZipFile.OpenReadAsync(Path.Combine(context.Root, "model.zip"))
                .ConfigureAwait(false);
            Assert.That(archive.Entries, Has.Count.EqualTo(archiveResult["documentCount"]!.GetValue<int>() + 1));
            using StreamReader reader = new(
                await archive.GetEntry("document-0000.json")!.OpenAsync().ConfigureAwait(false), Encoding.UTF8);
            JsonObject reconstructed = JsonNode.Parse(await reader.ReadToEndAsync().ConfigureAwait(false))!.AsObject();
            Assert.That(reconstructed["vendor:note"]!.GetValue<string>(), Is.EqualTo("retained-verbatim"));
        }

        /// <summary>
        /// A linked NodeSet converts to multiple exact documents and reconstructs every child node and parent link.
        /// </summary>
        [Test]
        public async Task LinkedNodeSetRoundTripPreservesEveryDocumentAndChildAsync()
        {
            using var context = new WotDocumentTestContext();
            JsonObject produced = WotDocumentTestContext.Payload(await context.Tools.FromNodeSetAsync(
                WotDocumentTestContext.Input(WotDocumentTestContext.LinkedNodeSet), "linked.zip", "pump")
                .ConfigureAwait(false));
            Assert.That(produced["documentCount"]!.GetValue<int>(), Is.EqualTo(2));
            WotDocumentInput archiveInput = new()
            {
                Source = new WotDocumentSource { FilePath = "linked.zip" },
                DocumentSetArchive = true
            };
            JsonObject inspected = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(archiveInput).ConfigureAwait(false));
            Assert.That(inspected["totalDocuments"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(inspected["rootReference"]!.GetValue<string>(), Is.EqualTo("pump"));
            WotDocumentTestContext.Payload(await context.Tools.ToNodeSetAsync(
                archiveInput, "linked.xml").ConfigureAwait(false));
            using FileStream stream = File.OpenRead(Path.Combine(context.Root, "linked.xml"));
            UANodeSet restored = UANodeSet.Read(stream)!;
            Assert.That(restored.Items, Has.Length.EqualTo(3));
            Assert.That(restored.Items!.Select(node => node.NodeId),
                Is.EquivalentTo(s_linkedNodeIds));
            UANode child = restored.Items.Single(node => node.NodeId == "ns=1;s=Child");
            Assert.That(child.References!.Any(reference => !reference.IsForward &&
                reference.Value == "ns=1;s=Pump"), Is.True);
            Assert.That(Directory.GetFiles(context.Root).Select(Path.GetFileName),
                Is.EquivalentTo(s_linkedArtifacts));
        }

        /// <summary>
        /// A failed projection cannot publish a success-shaped or partial artifact.
        /// </summary>
        [Test]
        public async Task FailedConversionPublishesNothingAsync()
        {
            using var context = new WotDocumentTestContext();
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.ToNodeSetAsync(
                WotDocumentTestContext.Input(/*lang=json,strict*/ """{"title":"unannotated"}"""), "failed.xml")
                .ConfigureAwait(false), true);

            Assert.That(result["valid"]!.GetValue<bool>(), Is.False);
            Assert.That(result["artifact"], Is.Null);
            Assert.That(result["diagnostics"]!["errors"]!.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
        }

        /// <summary>
        /// Bounded generated XML cannot leave a partially written transfer file.
        /// </summary>
        [Test]
        public async Task OversizedGeneratedArtifactLeavesNoDestinationOrStagingFileAsync()
        {
            int sourceBytes = Encoding.UTF8.GetByteCount(WotDocumentTestContext.Model);
            using var context = new WotDocumentTestContext(new WotMcpOptions { MaxTotalBytes = sourceBytes + 1 });
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.ToNodeSetAsync(
                WotDocumentTestContext.Input(), "too-large.xml").ConfigureAwait(false), true);
            Assert.That(result["artifact"], Is.Null);
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
        }

        /// <summary>
        /// A failure inside the shared transfer callback removes staging even after conversion succeeded.
        /// </summary>
        [Test]
        public async Task TransferFailureAfterConversionRemovesItsStagingFileAsync()
        {
            using var context = new WotDocumentTestContext();
            var transfers = new McpFileTransfers(new OpcUaMcpOptions
            {
                TransferRoot = context.Root,
                MaxTransferBytes = 1
            });
            using var tools = new WotDocumentTools(transfers, context.Shared, context.Options, context.Clients.Object);
            JsonObject result = WotDocumentTestContext.Payload(await tools.ToNodeSetAsync(
                WotDocumentTestContext.Input(), "interrupted.xml").ConfigureAwait(false), true);

            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo("IOException"));
            Assert.That(result["message"]!.GetValue<string>(), Does.Contain("limit"));
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
        }

        /// <summary>
        /// Existing destinations and path escapes fail before retrieving dependencies.
        /// </summary>
        [TestCase("exists.xml")]
        [TestCase("..\\escape.xml")]
        [TestCase("missing\\out.xml")]
        [TestCase("out.xml:stream")]
        public async Task DestinationPreflightPreventsDependencyRequestsAsync(string path)
        {
            if (!OperatingSystem.IsWindows() && path.Contains(':', StringComparison.Ordinal))
            {
                path = "../escape.xml";
            }
            path = path.Replace('\\', Path.DirectorySeparatorChar);
            using var context = new WotDocumentTestContext(
                new WotMcpOptions { AllowedDependencyOrigins = ["https://example.test"] });
            await File.WriteAllTextAsync(Path.Combine(context.Root, "exists.xml"), "original").ConfigureAwait(false);
            JsonObject document = JsonNode.Parse(WotDocumentTestContext.Model)!.AsObject();
            document["properties"]!["speed"]!["uav:externalSchema"] = "https://example.test/schema";
            WotDocumentInput input = WotDocumentTestContext.Input(document.ToJsonString());
            input.ResolveRemoteDependencies = true;
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.ToNodeSetAsync(input, path).ConfigureAwait(false), true);

            Assert.That(result["artifact"], Is.Null);
            Assert.That(await File.ReadAllTextAsync(Path.Combine(context.Root, "exists.xml")).ConfigureAwait(false),
                Is.EqualTo("original"));
            Assert.That(Directory.GetFiles(context.Root), Has.Length.EqualTo(1));
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Root confinement is applied to reads as well as writes.
        /// </summary>
        [Test]
        public async Task FileInputRequiresConfiguredTransferRootAndConfinedPathAsync()
        {
            using var context = new WotDocumentTestContext();
            WotDocumentInput input = new()
            {
                Source = new WotDocumentSource { FilePath = Path.Combine("..", "outside.json") }
            };
            JsonObject outside = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(input).ConfigureAwait(false), true);
            Assert.That(outside["message"]!.GetValue<string>(), Does.Contain("TransferRoot"));
            context.Shared.TransferRoot = null;
            input.Source.FilePath = "source.json";
            JsonObject missingRoot = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(input).ConfigureAwait(false), true);
            Assert.That(missingRoot["message"]!.GetValue<string>(), Does.Contain("disabled"));
        }

        /// <summary>
        /// File inputs may exceed the inline ceiling but never the host JSON document limit.
        /// </summary>
        [Test]
        public async Task FileInputUsesHostDocumentLimitBeforeParsingAsync()
        {
            using var context = new WotDocumentTestContext(new WotMcpOptions { MaxDocumentBytes = 8 });
            await File.WriteAllTextAsync(
                Path.Combine(context.Root, "source.json"), /*lang=json,strict*/ """{"tooLarge":true}""")
                .ConfigureAwait(false);
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                new WotDocumentInput { Source = new WotDocumentSource { FilePath = "source.json" } })
                .ConfigureAwait(false), true);
            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo("IOException"));
            Assert.That(result["message"]!.GetValue<string>(), Does.Contain("budget"));
            Assert.That(Directory.GetFiles(context.Root), Has.Length.EqualTo(1));
        }

        /// <summary>
        /// Inline UTF-8 boundaries use byte count, not the number of UTF-16 characters.
        /// </summary>
        [TestCase(65_535, false)]
        [TestCase(65_536, false)]
        [TestCase(65_537, true)]
        public async Task InlineSourceEnforcesExactByteBoundaryAsync(int bytes, bool error)
        {
            using var context = new WotDocumentTestContext();
            string text = "{\"x\":\"" + new string('a', bytes - 8) + "\"}";
            Assert.That(Encoding.UTF8.GetByteCount(text), Is.EqualTo(bytes));
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(WotDocumentTestContext.Input(text)).ConfigureAwait(false),
                error);
            if (!error)
            {
                Assert.That(result["provenance"]!["totalInputBytes"]!.GetValue<long>(), Is.EqualTo(bytes));
            }
        }

        /// <summary>
        /// Multi-byte UTF-8 must not bypass the byte ceiling by having fewer than 64 KiB of UTF-16 characters.
        /// </summary>
        [TestCase(32_764, false)]
        [TestCase(32_765, true)]
        public async Task InlineUnicodeInputUsesUtf8BytesRatherThanCharacterCountAsync(int characters, bool error)
        {
            using var context = new WotDocumentTestContext();
            string text = "{\"x\":\"" + new string('\u00E9', characters) + "\"}";
            Assert.That(text, Has.Length.LessThan(65_536));
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(WotDocumentTestContext.Input(text)).ConfigureAwait(false),
                error);
            if (!error)
            {
                Assert.That(result["provenance"]!["totalInputBytes"]!.GetValue<long>(), Is.EqualTo(65_536));
            }
        }

        /// <summary>
        /// Generated responses do not grow with all affordance names or an arbitrarily long title.
        /// </summary>
        [Test]
        public async Task InspectionMetadataIsBoundedAndReportsTruncationAsync()
        {
            using var context = new WotDocumentTestContext();
            var properties = new JsonObject();
            for (int index = 0; index < 101; index++)
            {
                properties["p" + index] = new JsonObject { ["type"] = "number" };
            }
            var document = new JsonObject { ["title"] = new string('x', 4000), ["properties"] = properties };
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                WotDocumentTestContext.Input(document.ToJsonString())).ConfigureAwait(false));
            JsonNode metadata = result["documents"]![0]!;
            Assert.That(metadata["title"]!.GetValue<string>(), Has.Length.EqualTo(1025));
            Assert.That(metadata["properties"]!["count"]!.GetValue<int>(), Is.EqualTo(101));
            Assert.That(metadata["properties"]!["names"]!.AsArray(), Has.Count.EqualTo(100));
            Assert.That(metadata["properties"]!["truncated"]!.GetValue<bool>(), Is.True);
        }

        /// <summary>
        /// DTDs and excessive XML nesting fail before conversion and artifact publication.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task NodeSetRejectsDtdAndExcessiveDepthWithoutArtifactAsync(bool dtd)
        {
            using var context = new WotDocumentTestContext();
            string xml = dtd
                ? "<!DOCTYPE UANodeSet [<!ENTITY ext SYSTEM \"https://never.invalid/entity\">]>" +
                    "<UANodeSet>&ext;</UANodeSet>"
                : "<UANodeSet>" +
                    string.Concat(Enumerable.Repeat("<x>", 257)) +
                    string.Concat(Enumerable.Repeat("</x>", 257)) +
                    "</UANodeSet>";
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.FromNodeSetAsync(
                WotDocumentTestContext.Input(xml), "blocked.zip").ConfigureAwait(false), true);

            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo(dtd ? "InvalidXml" : "FormatException"));
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Every public operation propagates pre-cancellation and creates no output.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void AllFiveToolsHonorPreCancellation(int operation)
        {
            using var context = new WotDocumentTestContext();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            WotDocumentInput input = WotDocumentTestContext.Input();
            Assert.That(async () =>
            {
                Task<CallToolResult> result = operation switch
                {
                    0 => context.Tools.InspectDocumentAsync(input, cancellation.Token),
                    1 => context.Tools.ValidateDocumentAsync(input, cancellation.Token),
                    2 => context.Tools.PlanBindingsAsync(input, ct: cancellation.Token),
                    3 => context.Tools.ToNodeSetAsync(input, "x.xml", cancellation.Token),
                    _ => context.Tools.FromNodeSetAsync(input, "x.zip", ct: cancellation.Token)
                };
                await result.ConfigureAwait(false);
            }, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
        }

        /// <summary>
        /// Generated ArrayOf metadata preserves arrays, explicit settings and empty dependencies without reflection.
        /// </summary>
        [Test]
        public void DocumentContractsRoundTripWithGeneratedMetadataOnly()
        {
            const string json = """
                {"source":{"text":"{}"},"dependencies":[{"reference":"schema","text":"true"}],
                 "strictAuthoring":true,"requiredConformance":["Core"],"preservationMode":"Never"}
                """;
            WotDocumentInput input = JsonSerializer.Deserialize(json, WotInputJsonContext.Default.WotDocumentInput)!;
            string encoded = JsonSerializer.Serialize(input, WotInputJsonContext.Default.WotDocumentInput);
            WotDocumentInput decoded = JsonSerializer.Deserialize(
                encoded, WotInputJsonContext.Default.WotDocumentInput)!;
            Assert.That(decoded.Dependencies, Has.Count.EqualTo(1));
            Assert.That(decoded.Dependencies[0].Reference, Is.EqualTo("schema"));
            Assert.That(decoded.RequiredConformance.ToArray(), Is.EqualTo(s_conformanceClaims));
            Assert.That(decoded.PreservationMode, Is.EqualTo(WotNodeSetPreservationMode.Never));
            Assert.That(decoded.StrictAuthoring, Is.True);
            WotDocumentInput empty = JsonSerializer.Deserialize(
                """{"source":{"text":"{}"},"dependencies":[],"requiredConformance":[]}""",
                WotInputJsonContext.Default.WotDocumentInput)!;
            Assert.That(empty.Dependencies.IsEmpty, Is.True);
            Assert.That(empty.RequiredConformance.IsEmpty, Is.True);
        }

        private static readonly string[] s_linkedNodeIds = ["ns=1;s=Pump", "ns=1;s=Child", "ns=1;s=Speed"];
        private static readonly string[] s_linkedArtifacts = ["linked.zip", "linked.xml"];
        private static readonly string[] s_conformanceClaims = ["Core"];
    }
}
#endif
