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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Provides bounded evidence-file access, portable path validation, JSON handling, and SHA-256 identities.
    /// </summary>
    internal sealed class EvidenceFiles
    {
        /// <summary>
        /// Resolves a validated portable relative path beneath a local root while rejecting links and reparse points.
        /// </summary>
        public static string Confined(string root, string relative)
        {
            ValidateRelative(relative);
            string fullRoot = Path.GetFullPath(root);
            string result = Path.GetFullPath(Path.Combine(
                fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            RejectLinks(fullRoot);
            string current = fullRoot;
            foreach (string segment in relative.Split('/'))
            {
                current = Path.Combine(current, segment);
                RejectLinks(current);
            }
            return result;
        }

        /// <summary>
        /// Rejects absolute, nonportable, or ambiguous path segments in an evidence-bundle relative path.
        /// </summary>
        public static void ValidateRelative(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) ||
                Path.IsPathRooted(relative) ||
                relative.Contains('\\', StringComparison.Ordinal) ||
                relative.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidDataException("A bundle path is not a portable relative path.");
            }
            foreach (string part in relative.Split('/'))
            {
                if (part is "" or "." or ".." ||
                    part.EndsWith(' ') ||
                    part.EndsWith('.') ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new InvalidDataException("A bundle path contains an unsafe segment.");
                }
            }
        }

        /// <summary>
        /// Rejects network or device paths and existing symlink or reparse-point ancestors.
        /// </summary>
        public static void RejectLinks(string path)
        {
            if (Path.GetFullPath(path).StartsWith(@"\\", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Network shares and device paths are not offline local inputs.");
            }
            for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Symlink and reparse-point paths are not accepted.");
                }
            }
        }

        /// <summary>
        /// Reads a local evidence document after rejecting links and enforcing the document-size limit.
        /// </summary>
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
        {
            RejectLinks(path);
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("An input document exceeds the 64 MiB limit.");
            }
            return File.ReadAllBytesAsync(path, cancellationToken);
        }

        /// <summary>
        /// Reads a bounded evidence document and parses JSON with duplicate-property rejection.
        /// </summary>
        public async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
        {
            byte[] bytes = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            return ParseJson(bytes);
        }

        /// <summary>
        /// Parses depth-bounded JSON and rejects duplicate property names throughout the document.
        /// </summary>
        public static JsonDocument ParseJson(ReadOnlyMemory<byte> bytes)
        {
            var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            try
            {
                CheckDuplicateProperties(document.RootElement);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Deserializes a bounded JSON document with supplied type metadata after rejecting duplicate and null values.
        /// </summary>
        public async Task<T> ReadModelAsync<T>(
            string path,
            JsonTypeInfo<T> type,
            CancellationToken cancellationToken)
        {
            using JsonDocument document = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
            CheckModelValues(document.RootElement);
            return document.Deserialize(type) ?? throw new JsonException("A required JSON record is null.");
        }

        /// <summary>
        /// Serializes a model to a newly created local file without overwriting an existing evidence document.
        /// </summary>
        public async Task WriteModelAsync<T>(
            string path,
            T value,
            JsonTypeInfo<T> type,
            CancellationToken cancellationToken)
        {
            RejectLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, type);
            using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            });
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Computes the canonical SHA-256 digest of a local file after rejecting links.
        /// </summary>
        public async Task<string> DigestAsync(string path, CancellationToken cancellationToken)
        {
            RejectLinks(path);
            using FileStream stream = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return "sha256:" + Convert.ToHexStringLower(hash);
        }

        /// <summary>
        /// Computes a lowercase SHA-256 content identifier with the canonical algorithm prefix.
        /// </summary>
        public static string Digest(ReadOnlySpan<byte> bytes)
        {
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        /// <summary>
        /// Indexes top-level NuGet and symbol archives by digest while enforcing archive-count and size limits.
        /// </summary>
        public async Task<Dictionary<string, string>> IndexArchivesAsync(
            string root, CancellationToken cancellationToken)
        {
            RejectLinks(root);
            var archives = new Dictionary<string, string>(StringComparer.Ordinal);
            int count = 0;
            foreach (string path in Directory.EnumerateFiles(root).Where(p =>
                p.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)))
            {
                if (++count > 1024 || new FileInfo(path).Length > 512L * 1024 * 1024)
                {
                    throw new InvalidDataException("Archive set exceeds the bounded file-count or size limit.");
                }
                string digest = await DigestAsync(path, cancellationToken).ConfigureAwait(false);
                archives.TryAdd(digest, path);
            }
            return archives;
        }

        private static void CheckDuplicateProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new JsonException("Duplicate JSON property.");
                    }

                    CheckDuplicateProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CheckDuplicateProperties(item);
                }
            }
        }

        private static void CheckModelValues(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                throw new JsonException("Null record values are not accepted; omit optional fields.");
            }
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    CheckModelValues(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CheckModelValues(item);
                }
            }
        }
    }
}
