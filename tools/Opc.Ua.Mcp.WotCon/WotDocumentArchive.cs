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

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Carries an unchanged linked WoT document set in one atomically publishable MCP artifact.
    /// Never extracts archive paths onto the host filesystem.
    /// </summary>
    internal static class WotDocumentArchive
    {
        /// <summary>
        /// Writes every document with its exact href and digest in a tool-artifact manifest.
        /// </summary>
        /// <exception cref="IOException"></exception>
        /// <exception cref="FormatException"></exception>
        internal static async ValueTask WriteAsync(
            WotDocumentSet documents, Stream target, WotNodeSetConverterOptions options,
            long maxTotalBytes, CancellationToken ct)
        {
            if (documents.Entries.IsEmpty || documents.Entries.Count > options.MaxResolverDocuments)
            {
                throw new IOException("The generated document set exceeds the host document count limit.");
            }
            var entries = new List<WotArchiveEntry>();
            var hrefs = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                WotDocumentSetEntry document = documents.Entries[index];
                if (string.IsNullOrWhiteSpace(document.Href) ||
                    document.Href.Length > 4096 ||
                    !hrefs.Add(document.Href))
                {
                    throw new FormatException("The generated document set has invalid or duplicate references.");
                }
                total += document.Document.Utf8Json.Length;
                if (document.Document.Utf8Json.Length > options.MaxJsonDocumentSize || total > maxTotalBytes)
                {
                    throw new IOException("The generated document set exceeds the host content byte budget.");
                }
                string name = "document-" + index.ToString("D4", CultureInfo.InvariantCulture) + ".json";
                entries.Add(new WotArchiveEntry
                {
                    Href = document.Href,
                    Entry = name,
                    Sha256 = Convert.ToHexString(SHA256.HashData(document.Document.Utf8Json.Span))
                });
            }
            if (!hrefs.Contains(documents.RootHref))
            {
                throw new FormatException("The generated document-set root is missing.");
            }
            using var manifestBytes = new McpBoundedMemoryStream(Math.Min(262_144, maxTotalBytes - total), ct);
            await JsonSerializer.SerializeAsync(manifestBytes, new WotArchiveManifest
            {
                Format = kFormat,
                Version = 1,
                RootHref = documents.RootHref,
                Documents = entries.ToArrayOf()
            }, WotInputJsonContext.Default.WotArchiveManifest, ct).ConfigureAwait(false);
            using var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                ZipArchiveEntry entry = archive.CreateEntry(entries[index].Entry, CompressionLevel.Fastest);
#if NET10_0_OR_GREATER
                Stream stream = await entry.OpenAsync(ct).ConfigureAwait(false);
#else
                Stream stream = entry.Open();
#endif
                await using ConfiguredAsyncDisposable streamScope = stream.ConfigureAwait(false);
                await stream.WriteAsync(documents.Entries[index].Document.Utf8Json, ct).ConfigureAwait(false);
            }
            ZipArchiveEntry manifest = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
#if NET10_0_OR_GREATER
            Stream manifestStream = await manifest.OpenAsync(ct).ConfigureAwait(false);
#else
            Stream manifestStream = manifest.Open();
#endif
            await using ConfiguredAsyncDisposable manifestScope = manifestStream.ConfigureAwait(false);
            await manifestStream.WriteAsync(manifestBytes.ToArray(), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies the manifest and every member before converting a document set.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        /// <exception cref="IOException"></exception>
        internal static async ValueTask<WotDocumentSet> ReadAsync(ByteString bytes, WotDocumentScope scope)
        {
            ValidateDirectory(bytes.Span, scope.Options.MaxResolverDocuments + 1);
            using var source = new MemoryStream(bytes.ToArray(), writable: false);
            using var archive = new ZipArchive(source, ZipArchiveMode.Read);
            if (archive.Entries.Count < 2 || archive.Entries.Count > scope.Options.MaxResolverDocuments + 1)
            {
                throw new FormatException(
                    "The document archive exceeds the host count limit or contains no documents.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!names.Add(entry.FullName) || !IsEntryName(entry.FullName))
                {
                    throw new FormatException(
                        "Duplicate, unexpected or path-bearing archive entries are not permitted.");
                }
            }
            ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json") ??
                throw new FormatException("The WoT document-set archive has no manifest.");
            if (manifestEntry.Length > Math.Min(262_144, scope.Options.MaxJsonDocumentSize))
            {
                throw new IOException("The document-set manifest exceeds the host byte budget.");
            }
            WotArchiveManifest manifest;
#if NET10_0_OR_GREATER
            Stream manifestContent = await manifestEntry.OpenAsync(scope.Token).ConfigureAwait(false);
#else
            Stream manifestContent = manifestEntry.Open();
#endif
            await using (manifestContent.ConfigureAwait(false))
            {
                ByteString content = await scope.ReadArchiveContentAsync(
                    manifestContent, Math.Min(262_144, scope.Options.MaxJsonDocumentSize)).ConfigureAwait(false);
                if (content.Length != manifestEntry.Length)
                {
                    throw new FormatException("The archive manifest does not match its declared decompressed length.");
                }
                using (var parsed = JsonDocument.Parse(content.Memory))
                {
                    RejectDuplicateMembers(parsed.RootElement);
                }
                manifest = JsonSerializer.Deserialize(content.Span, WotInputJsonContext.Default.WotArchiveManifest) ??
                    throw new FormatException("The WoT document-set manifest is empty.");
            }
            if (manifest.Format != kFormat ||
                manifest.Version != 1 ||
                manifest.Documents.IsEmpty ||
                string.IsNullOrWhiteSpace(manifest.RootHref) ||
                manifest.RootHref.Length > 4096 ||
                manifest.Documents.Count != archive.Entries.Count - 1)
            {
                throw new FormatException("The document-set manifest version or entry count is invalid.");
            }
            var hrefs = new HashSet<string>(StringComparer.Ordinal);
            var listedEntries = new HashSet<string>(StringComparer.Ordinal);
            var documents = new List<WotDocumentSetEntry>();
            for (int index = 0; index < manifest.Documents.Count; index++)
            {
                WotArchiveEntry? item = manifest.Documents[index];
                if (item is null ||
                    string.IsNullOrWhiteSpace(item.Href) ||
                    item.Href.Length > 4096 ||
                    !hrefs.Add(item.Href) ||
                    string.IsNullOrEmpty(item.Entry) ||
                    !listedEntries.Add(item.Entry) ||
                    item.Entry == "manifest.json" ||
                    !IsEntryName(item.Entry) ||
                    item.Sha256 is not { Length: 64 })
                {
                    throw new FormatException("Document references and entry names must be explicit and unique.");
                }
                ZipArchiveEntry entry = archive.GetEntry(item.Entry) ??
                    throw new FormatException("A manifest document is missing from the archive.");
#if NET10_0_OR_GREATER
                Stream stream = await entry.OpenAsync(scope.Token).ConfigureAwait(false);
#else
                Stream stream = entry.Open();
#endif
                await using ConfiguredAsyncDisposable streamScope = stream.ConfigureAwait(false);
                WotDocument document = await scope.ImportArchiveDocumentAsync(item.Href, stream, entry.Length)
                    .ConfigureAwait(false);
                byte[] expected = Convert.FromHexString(item.Sha256);
                byte[] actual = SHA256.HashData(document.Utf8Json.Span);
                if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    throw new FormatException("A document-set member does not match its manifest digest.");
                }
                documents.Add(new WotDocumentSetEntry(item.Href, document));
            }
            if (!hrefs.Contains(manifest.RootHref))
            {
                throw new FormatException("The manifest root is not a document in the archive.");
            }
            return new WotDocumentSet(manifest.RootHref, documents.ToArrayOf());
        }

        /// <summary>
        /// Bounds central-directory records before ZipArchive allocates one object per untrusted entry.
        /// ZIP64 and multi-disk archives are unnecessary for this bounded artifact format.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private static void ValidateDirectory(ReadOnlySpan<byte> bytes, int maxEntries)
        {
            int end = -1;
            for (int index = bytes.Length - 22; index >= Math.Max(0, bytes.Length - 65_557); index--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[index..]) == 0x06054B50 &&
                    index + 22 + BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index + 20)..]) == bytes.Length)
                {
                    end = index;
                    break;
                }
            }
            if (end < 0)
            {
                throw new FormatException("The document archive has no valid bounded ZIP directory.");
            }
            ReadOnlySpan<byte> directory = bytes[end..];
            int count = BinaryPrimitives.ReadUInt16LittleEndian(directory[10..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(directory[12..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(directory[16..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(directory[4..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(directory[8..]) != count ||
                count < 2 ||
                count > maxEntries ||
                offset > end ||
                size != end - offset)
            {
                throw new FormatException("ZIP64, multi-disk or excessive archive directories are not permitted.");
            }
            int cursor = (int)offset;
            for (int index = 0; index < count; index++)
            {
                if (cursor > end - 46 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[cursor..]) != 0x02014B50)
                {
                    throw new FormatException("The archive directory does not match its declared entry count.");
                }
                int recordLength = 46 +
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes[(cursor + 28)..]) +
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes[(cursor + 30)..]) +
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes[(cursor + 32)..]);
                if (recordLength > end - cursor)
                {
                    throw new FormatException("An archive directory record exceeds its declared size.");
                }
                cursor += recordLength;
            }
            if (cursor != end)
            {
                throw new FormatException("Unlisted archive directory records are not permitted.");
            }
        }

        /// <summary>
        /// A producer-owned manifest has no last-property-wins interpretation.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private static void RejectDuplicateMembers(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new FormatException("Duplicate manifest members are not permitted.");
                    }
                    RejectDuplicateMembers(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    RejectDuplicateMembers(item);
                }
            }
        }

        /// <summary>
        /// Only flat, producer-owned names are valid; hrefs remain metadata rather than archive paths.
        /// </summary>
        private static bool IsEntryName(string name)
        {
            if (name == "manifest.json")
            {
                return true;
            }
            if (name.Length != 18 ||
                !name.StartsWith("document-", StringComparison.Ordinal) ||
                !name.EndsWith(".json", StringComparison.Ordinal))
            {
                return false;
            }
            for (int index = 9; index < 13; index++)
            {
                if (name[index] is < '0' or > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Identifies an MCP transfer artifact, not a new WoT vocabulary or information model.</summary>
        private const string kFormat = "opcua-mcp-wot-document-set";
    }

    /// <summary>
    /// Bounded metadata for a linked document-set artifact.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed class WotArchiveManifest
    {
        /// <summary>
        /// Gets or sets the tool-artifact format identifier.
        /// </summary>
        public required string Format { get; set; }

        /// <summary>
        /// Gets or sets the manifest format version.
        /// </summary>
        public required int Version { get; set; }

        /// <summary>
        /// Gets or sets the exact href of the root document.
        /// </summary>
        public required string RootHref { get; set; }

        /// <summary>
        /// Gets or sets every document stored in the archive.
        /// </summary>
        [JsonConverter(typeof(WotInputArrayJsonConverter<WotArchiveEntry>))]
        public required ArrayOf<WotArchiveEntry> Documents { get; set; }
    }

    /// <summary>
    /// Maps one exact document href to a flat archive entry and its integrity digest.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed class WotArchiveEntry
    {
        /// <summary>
        /// Gets or sets the document's exact, unmodified href.
        /// </summary>
        public required string Href { get; set; }

        /// <summary>
        /// Gets or sets the flat archive entry name.
        /// </summary>
        public required string Entry { get; set; }

        /// <summary>
        /// Gets or sets the SHA-256 digest of the uncompressed document bytes.
        /// </summary>
        public required string Sha256 { get; set; }
    }
}
