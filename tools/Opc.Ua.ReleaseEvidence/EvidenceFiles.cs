// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

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
    internal sealed class EvidenceFiles
    {
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

        public async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
        {
            byte[] bytes = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            return ParseJson(bytes);
        }

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

        public async Task<T> ReadModelAsync<T>(
            string path,
            JsonTypeInfo<T> type,
            CancellationToken cancellationToken)
        {
            using JsonDocument document = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
            CheckModelValues(document.RootElement);
            return document.Deserialize(type) ?? throw new JsonException("A required JSON record is null.");
        }

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

        public async Task<string> DigestAsync(string path, CancellationToken cancellationToken)
        {
            RejectLinks(path);
            using FileStream stream = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return "sha256:" + Convert.ToHexStringLower(hash);
        }

        public static string Digest(ReadOnlySpan<byte> bytes)
        {
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

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
