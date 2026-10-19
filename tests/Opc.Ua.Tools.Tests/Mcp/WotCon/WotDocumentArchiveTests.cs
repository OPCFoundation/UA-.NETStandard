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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Wot;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies the flat, bounded document-set artifact rather than trusting ZIP metadata or extracting paths.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WotDocumentArchiveTests
    {
        /// <summary>
        /// A produced archive maps exact hrefs and SHA-256 values and keeps all documents.
        /// </summary>
        [Test]
        public async Task ArchiveWriterPreservesExactHrefsDigestsAndAllDocumentsAsync()
        {
            using var first = WotDocument.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"title":"first"}"""));
            using var second = WotDocument.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"title":"second"}"""));
            using var firstEntry = new WotDocumentSetEntry("root", first);
            using var secondEntry = new WotDocumentSetEntry("nested-exact", second);
            using var documents = new WotDocumentSet("root", [firstEntry, secondEntry]);
            using var buffer = new McpBoundedMemoryStream(16_384, default);
            await WotDocumentArchive.WriteAsync(
                documents, buffer, new WotNodeSetConverterOptions(), 16_384, default).ConfigureAwait(false);
            buffer.Position = 0;
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            using var reader = new StreamReader(
                await archive.GetEntry("manifest.json")!.OpenAsync().ConfigureAwait(false), Encoding.UTF8);
            JsonObject manifest = JsonNode.Parse(await reader.ReadToEndAsync().ConfigureAwait(false))!.AsObject();

            Assert.That(archive.Entries.Select(entry => entry.FullName),
                Is.EquivalentTo(s_archiveEntries));
            Assert.That(manifest["rootHref"]!.GetValue<string>(), Is.EqualTo("root"));
            Assert.That(manifest["documents"]![1]!["href"]!.GetValue<string>(), Is.EqualTo("nested-exact"));
            Assert.That(manifest["documents"]![1]!["sha256"]!.GetValue<string>(),
                Is.EqualTo(Convert.ToHexString(SHA256.HashData(second.Utf8Json.Span))));
        }

        /// <summary>
        /// Every producer-owned manifest member is validated before a set is accepted.
        /// </summary>
        [TestCase("format", "FormatException")]
        [TestCase("version", "FormatException")]
        [TestCase("root", "FormatException")]
        [TestCase("duplicateHref", "FormatException")]
        [TestCase("normalizedDuplicateHref", "FormatException")]
        [TestCase("missingMember", "FormatException")]
        [TestCase("unlistedMember", "FormatException")]
        [TestCase("duplicateEntry", "FormatException")]
        [TestCase("pathEntry", "FormatException")]
        [TestCase("invalidDigest", "FormatException")]
        [TestCase("wrongDigest", "FormatException")]
        [TestCase("nullMember", "InvalidJson")]
        [TestCase("duplicateManifestProperty", "FormatException")]
        [TestCase("unknownManifestProperty", "InvalidJson")]
        [TestCase("noManifest", "FormatException")]
        [TestCase("falseDirectoryCount", "FormatException")]
        public async Task ArchiveRejectsInvalidManifestAndDirectoryWithoutExtractionAsync(
            string mutation, string errorType)
        {
            (byte[] bytes, _) = Archive(mutation);
            using var context = new WotDocumentTestContext();
            await File.WriteAllBytesAsync(Path.Combine(context.Root, "input.zip"), bytes).ConfigureAwait(false);
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                new WotDocumentInput
                {
                    Source = new WotDocumentSource { FilePath = "input.zip" },
                    DocumentSetArchive = true
                }).ConfigureAwait(false), true);

            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo(errorType));
            Assert.That(result["documents"], Is.Null);
            Assert.That(Directory.GetFiles(context.Root).Select(Path.GetFileName), Is.EqualTo(s_inputArtifacts));
            Assert.That(Directory.GetDirectories(context.Root), Is.Empty);
        }

        /// <summary>
        /// The cumulative budget includes compressed source, decompressed manifest and every member.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(-1, true)]
        public async Task ArchiveCumulativeDecompressedBudgetHasAnExactBoundaryAsync(int adjustment, bool error)
        {
            (byte[] bytes, int expanded) = Archive("none");
            long budget = bytes.Length + expanded + adjustment;
            using var context = new WotDocumentTestContext(new WotMcpOptions { MaxTotalBytes = budget });
            await File.WriteAllBytesAsync(Path.Combine(context.Root, "input.zip"), bytes).ConfigureAwait(false);
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                new WotDocumentInput
                {
                    Source = new WotDocumentSource { FilePath = "input.zip" },
                    DocumentSetArchive = true
                }).ConfigureAwait(false), error);
            if (!error)
            {
                Assert.That(result["totalDocuments"]!.GetValue<int>(), Is.EqualTo(2));
                Assert.That(result["provenance"]!["totalInputBytes"]!.GetValue<long>(), Is.EqualTo(budget));
                Assert.That(result["provenance"]!["primarySource"]!["sha256"]!.GetValue<string>(),
                    Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes))));
            }
            Assert.That(Directory.GetFiles(context.Root), Has.Length.EqualTo(1));
        }

        /// <summary>
        /// A highly compressible member cannot bypass the JSON limit using a small compressed archive.
        /// </summary>
        [Test]
        public async Task ArchiveRejectsOversizedDecompressedDocumentAsync()
        {
            (byte[] bytes, _) = Archive("largeMember");
            using var context = new WotDocumentTestContext(new WotMcpOptions { MaxDocumentBytes = 1024 });
            await File.WriteAllBytesAsync(Path.Combine(context.Root, "input.zip"), bytes).ConfigureAwait(false);
            Assert.That(bytes, Has.Length.LessThan(2048));
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                new WotDocumentInput
                {
                    Source = new WotDocumentSource { FilePath = "input.zip" },
                    DocumentSetArchive = true
                }).ConfigureAwait(false), true);
            Assert.That(result["message"]!.GetValue<string>(), Does.Contain("budget"));
            Assert.That(result["documents"], Is.Null);
        }

        /// <summary>
        /// Output set count and byte constraints are checked before any ZIP member is emitted.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ArchiveWriterRejectsOversizedSetsBeforeWriting(bool count)
        {
            using var document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"title":"bounded"}"""));
            using var firstEntry = new WotDocumentSetEntry("root", document);
            using var secondEntry = new WotDocumentSetEntry("second", document);
            using var documents = new WotDocumentSet("root", [firstEntry, secondEntry]);
            var options = new WotNodeSetConverterOptions { MaxResolverDocuments = count ? 1 : 2 };
            using var buffer = new McpBoundedMemoryStream(1024, default);
            Assert.That(async () => await WotDocumentArchive.WriteAsync(
                documents, buffer, options, count ? 1024 : 4, default).ConfigureAwait(false),
                Throws.TypeOf<IOException>());
            Assert.That(buffer.Length, Is.Zero);
        }

        /// <summary>
        /// Every serializer write shape respects both the limit and pre-cancellation without partial mutation.
        /// </summary>
        [Test]
        public void BufferBoundsSynchronousWritesLengthAndCancellationWithoutPartialGrowth()
        {
            using var cancellation = new CancellationTokenSource();
            using var buffer = new McpBoundedMemoryStream(4, cancellation.Token);
            buffer.Write([1, 2], 0, 2);
            buffer.Write(new ReadOnlySpan<byte>([3]));
            buffer.WriteByte(4);
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(buffer.Capacity, Is.LessThanOrEqualTo(4));
            Assert.That(() => buffer.WriteByte(5), Throws.TypeOf<IOException>());
            Assert.That(() => buffer.SetLength(5), Throws.TypeOf<IOException>());
            Assert.That(buffer.Length, Is.EqualTo(4));
            buffer.Position = 0;
            cancellation.Cancel();
            Assert.That(() => buffer.WriteByte(9), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        /// <summary>
        /// Both asynchronous write overloads enforce the exact byte ceiling and cancellation before mutation.
        /// </summary>
        [Test]
        public async Task BufferBoundsAsynchronousWritesWithoutPartialGrowthAsync()
        {
            using var cancellation = new CancellationTokenSource();
            using var buffer = new McpBoundedMemoryStream(4, cancellation.Token);
            Func<byte[], int, int, CancellationToken, Task> writeArray = buffer.WriteAsync;
            byte[] first = [1, 2];
            await writeArray(first, 0, first.Length, default).ConfigureAwait(false);
            await buffer.WriteAsync(new ReadOnlyMemory<byte>([3, 4])).ConfigureAwait(false);
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            await Assert.ThrowsAsync<IOException>(async () =>
                await buffer.WriteAsync(new ReadOnlyMemory<byte>([5])).ConfigureAwait(false));
            await cancellation.CancelAsync().ConfigureAwait(false);
            buffer.Position = 0;
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await buffer.WriteAsync(new ReadOnlyMemory<byte>([9])).ConfigureAwait(false));
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        /// <summary>
        /// Inline source bounds cannot be raised by host configuration or an invalid concurrency policy.
        /// </summary>
        [TestCase("bytes", 0)]
        [TestCase("bytes", (16 * 1024 * 1024) + 1)]
        [TestCase("total", 0)]
        [TestCase("total", (128 * 1024 * 1024) + 1)]
        [TestCase("documents", 0)]
        [TestCase("documents", 257)]
        [TestCase("depth", 0)]
        [TestCase("depth", 17)]
        [TestCase("timeout", 0)]
        [TestCase("timeout", 120_001)]
        [TestCase("concurrency", 0)]
        [TestCase("concurrency", 33)]
        public void HostOptionsRejectEveryInvalidBoundary(string field, int value)
        {
            var options = new WotMcpOptions();
            switch (field)
            {
                case "bytes":
                    options.MaxDocumentBytes = value;
                    break;
                case "total":
                    options.MaxTotalBytes = value;
                    break;
                case "documents":
                    options.MaxDocuments = value;
                    break;
                case "depth":
                    options.MaxResolverDepth = value;
                    break;
                case "timeout":
                    options.OperationTimeoutMilliseconds = value;
                    break;
                default:
                    options.MaxConcurrentOperations = value;
                    break;
            }
            Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Inclusive minimum and maximum policies are accepted and frozen rather than following later host edits.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void HostOptionsAcceptExactBoundariesAndSnapshotIsIndependent(bool maximum)
        {
            var options = new WotMcpOptions
            {
                MaxDocumentBytes = maximum ? 16 * 1024 * 1024 : 1,
                MaxTotalBytes = maximum ? 128L * 1024 * 1024 : 1,
                MaxDocuments = maximum ? 256 : 1,
                MaxResolverDepth = maximum ? 16 : 1,
                OperationTimeoutMilliseconds = maximum ? 120_000 : 1,
                MaxConcurrentOperations = maximum ? 32 : 1,
                AllowedDependencyOrigins = ["https://example.test"]
            };
            WotMcpOptions captured = options.Snapshot();
            Assert.That(captured.MaxDocumentBytes, Is.EqualTo(maximum ? 16 * 1024 * 1024 : 1));
            Assert.That(captured.MaxTotalBytes, Is.EqualTo(maximum ? 128L * 1024 * 1024 : 1));
            Assert.That(captured.MaxDocuments, Is.EqualTo(maximum ? 256 : 1));
            Assert.That(captured.MaxResolverDepth, Is.EqualTo(maximum ? 16 : 1));
            Assert.That(captured.OperationTimeoutMilliseconds, Is.EqualTo(maximum ? 120_000 : 1));
            Assert.That(captured.MaxConcurrentOperations, Is.EqualTo(maximum ? 32 : 1));
            options.AllowedDependencyOrigins = [];
            options.MaxDocuments = 2;
            Assert.That(captured.AllowedDependencyOrigins.ToArray(), Is.EqualTo(s_allowedOrigins));
            Assert.That(captured.MaxDocuments, Is.EqualTo(maximum ? 256 : 1));
        }

        /// <summary>
        /// Builds a complete in-memory archive, then mutates one independent manifest or directory invariant.
        /// </summary>
        private static (byte[] Bytes, int Expanded) Archive(string mutation)
        {
            byte[] first = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"title":"first"}""");
            byte[] second = Encoding.UTF8.GetBytes(mutation == "largeMember"
                ? "{\"title\":\"" + new string('a', 4096) + "\"}" : /*lang=json,strict*/ """{"title":"second"}""");
            var manifest = new JsonObject
            {
                ["format"] = "opcua-mcp-wot-document-set",
                ["version"] = 1,
                ["rootHref"] = "root",
                ["documents"] = new JsonArray(Member("root", "document-0000.json", first),
                    Member("second", "document-0001.json", second))
            };
            var entries = new List<(string Name, byte[] Content)>
            {
                ("document-0000.json", first), ("document-0001.json", second)
            };
            switch (mutation)
            {
                case "format":
                    manifest["format"] = "other-format";
                    break;
                case "version":
                    manifest["version"] = 2;
                    break;
                case "root":
                    manifest["rootHref"] = "absent";
                    break;
                case "duplicateHref":
                    manifest["documents"]![1]!["href"] = "root";
                    break;
                case "normalizedDuplicateHref":
                    manifest["documents"]![1]!["href"] = "./root";
                    break;
                case "missingMember":
                    manifest["documents"]![1]!["entry"] = "document-0042.json";
                    break;
                case "unlistedMember":
                    entries.Add(("document-0002.json", second));
                    break;
                case "duplicateEntry":
                    entries.Add(("document-0000.json", first));
                    break;
                case "pathEntry":
                    entries[1] = ("../document-0001.json", second);
                    manifest["documents"]![1]!["entry"] = "../document-0001.json";
                    break;
                case "invalidDigest":
                    manifest["documents"]![0]!["sha256"] = new string('z', 64);
                    break;
                case "wrongDigest":
                    manifest["documents"]![0]!["sha256"] = new string('0', 64);
                    break;
                case "nullMember":
                    manifest["documents"]![0] = null;
                    break;
                case "unknownManifestProperty":
                    manifest["unsupported"] = true;
                    break;
            }
            string json = manifest.ToJsonString();
            if (mutation == "duplicateManifestProperty")
            {
                json = "{\"version\":1," + json[1..];
            }
            if (mutation != "noManifest")
            {
                entries.Add(("manifest.json", Encoding.UTF8.GetBytes(json)));
            }
            int expanded = entries.Sum(entry => entry.Content.Length);
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach ((string name, byte[] content) in entries)
                {
                    using Stream stream = archive.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                    stream.Write(content);
                }
            }
            byte[] bytes = buffer.ToArray();
            if (mutation == "falseDirectoryCount")
            {
                bytes[^12] = 1;
            }
            return (bytes, expanded);
        }

        /// <summary>
        /// Creates the exact manifest member without reflection serialization.
        /// </summary>
        private static JsonObject Member(string href, string entry, ReadOnlySpan<byte> content)
        {
            return new JsonObject
            {
                ["href"] = href,
                ["entry"] = entry,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(content))
            };
        }

        private static readonly string[] s_archiveEntries =
            ["document-0000.json", "document-0001.json", "manifest.json"];

        private static readonly string[] s_inputArtifacts = ["input.zip"];
        private static readonly string[] s_allowedOrigins = ["https://example.test"];
    }
}
#endif
