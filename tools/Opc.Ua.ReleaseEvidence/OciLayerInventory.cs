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
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Describes an effective image filesystem entry and its content or recorded link target.
    /// </summary>
    /// <param name="Path">The normalized entry path within the image filesystem.</param>
    /// <param name="Digest">The regular-file content digest or digest of the recorded symbolic-link target.</param>
    /// <param name="Size">The length of the referenced content in bytes.</param>
    /// <param name="Kind">Whether the entry represents a regular file, hard link, or symbolic link.</param>
    /// <param name="Target">The optional recorded hard-link or symbolic-link target.</param>
    internal sealed record OciFileEvidence(string Path, string Digest, long Size, string Kind, string? Target = null);

    /// <summary>
    /// Builds a bounded final-image filesystem inventory without extracting entries or following host links.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class OciLayerInventory(EvidenceFiles files)
    {
        /// <summary>
        /// Verifies compressed and expanded layer digests and applies whiteouts and links to the final-image inventory.
        /// </summary>
        public async Task<Dictionary<string, OciFileEvidence>> ReadAsync(
            string layout,
            JsonElement manifest,
            JsonElement configuration,
            CancellationToken cancellationToken)
        {
            JsonElement layers = manifest.GetProperty("layers");
            JsonElement rootfs = configuration.GetProperty("rootfs");
            JsonElement diffIds = rootfs.GetProperty("diff_ids");
            if (rootfs.GetProperty("type").GetString() != "layers" ||
                layers.GetArrayLength() != diffIds.GetArrayLength() || layers.GetArrayLength() > 256)
            {
                throw new InvalidDataException("OCI layer and uncompressed digest scope disagree.");
            }
            var final = new Dictionary<string, OciFileEvidence>(StringComparer.Ordinal);
            long totalBytes = 0;
            int totalEntries = 0;
            int layerIndex = 0;
            foreach (JsonElement layer in layers.EnumerateArray())
            {
                string digest = layer.GetProperty("digest").GetString()!;
                string path = OciReconciler.BlobPath(layout, digest);
                long size = layer.GetProperty("size").GetInt64();
                if (size < 0 || size > kMaximumLayerBytes || new FileInfo(path).Length != size ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != digest)
                {
                    throw new InvalidDataException("OCI layer bytes do not match their bounded descriptor.");
                }
                string mediaType = layer.GetProperty("mediaType").GetString()!;
                using Stream decoded = OpenLayer(path, mediaType);
                using var bounded = new BoundedHashStream(decoded, kMaximumLayerBytes);
                using var reader = new TarReader(bounded, leaveOpen: true);
                var additions = new Dictionary<string, OciFileEvidence>(StringComparer.Ordinal);
                var deletions = new HashSet<string>(StringComparer.Ordinal);
                var opaque = new HashSet<string>(StringComparer.Ordinal);
                var directories = new HashSet<string>(StringComparer.Ordinal);
                TarEntry? entry;
                while ((entry = await reader.GetNextEntryAsync(
                    copyData: false, cancellationToken).ConfigureAwait(false)) != null)
                {
                    if (++totalEntries > 500000 || entry.Length > kMaximumFileBytes)
                    {
                        throw new InvalidDataException("OCI filesystem inventory exceeds its entry or file limit.");
                    }
                    string name = Normalize(entry.Name, entry.EntryType == TarEntryType.Directory);
                    if (name.Length == 0 && entry.EntryType == TarEntryType.Directory)
                    {
                        continue;
                    }
                    string leaf = name[(name.LastIndexOf('/') + 1)..];
                    string parent = name.Contains('/', StringComparison.Ordinal)
                        ? name[..(name.LastIndexOf('/') + 1)] : string.Empty;
                    if (leaf.StartsWith(".wh.", StringComparison.Ordinal))
                    {
                        if (entry.Length != 0 ||
                            entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                        {
                            throw new InvalidDataException("Invalid OCI whiteout.");
                        }
                        if (leaf == ".wh..wh..opq")
                        {
                            opaque.Add(parent);
                        }
                        else
                        {
                            string deleted = parent + leaf[4..];
                            _ = Normalize(deleted);
                            deletions.Add(deleted);
                        }
                        continue;
                    }
                    if (additions.ContainsKey(name) || directories.Contains(name))
                    {
                        throw new InvalidDataException("Ambiguous duplicate OCI layer entry.");
                    }
                    switch (entry.EntryType)
                    {
                        case TarEntryType.Directory:
                            directories.Add(name);
                            break;
                        case TarEntryType.RegularFile:
                        case TarEntryType.V7RegularFile:
                            string hash = entry.DataStream == null
                                ? EvidenceFiles.Digest([])
                                : "sha256:" + Convert.ToHexStringLower(
                                    await SHA256.HashDataAsync(entry.DataStream, cancellationToken)
                                        .ConfigureAwait(false));
                            additions.Add(name, new OciFileEvidence(name, hash, entry.Length, "file"));
                            break;
                        case TarEntryType.HardLink:
                            additions.Add(name, new OciFileEvidence(name, string.Empty, 0, "hardlink",
                                Normalize(entry.LinkName)));
                            break;
                        case TarEntryType.SymbolicLink:
                            // Links are recorded, never followed on the host or used as extraction destinations.
                            if (string.IsNullOrWhiteSpace(entry.LinkName) || entry.LinkName.Length > 4096 ||
                                entry.LinkName.Contains('\0', StringComparison.Ordinal))
                            {
                                throw new InvalidDataException("Invalid OCI symbolic link.");
                            }
                            additions.Add(name, new OciFileEvidence(name,
                                EvidenceFiles.Digest(System.Text.Encoding.UTF8.GetBytes(entry.LinkName)),
                                0, "symlink", entry.LinkName));
                            break;
                        default:
                            throw new InvalidDataException("Unsupported OCI filesystem entry type.");
                    }
                }
                // Include tar padding/trailing bytes in the diff_id, not only the entries TarReader consumed.
                byte[] buffer = new byte[81920];
                while (await bounded.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) != 0)
                {
                }
                totalBytes = checked(totalBytes + bounded.BytesRead);
                if (totalBytes > kMaximumImageBytes || bounded.GetDigest() != diffIds[layerIndex++].GetString())
                {
                    throw new InvalidDataException("OCI uncompressed layer digest or total size differs.");
                }
                // Whiteouts affect lower layers only, regardless of their position in this layer's tar stream.
                foreach (string existing in final.Keys.ToArray())
                {
                    if (ShouldRemove(existing, opaque, deletions, additions, directories))
                    {
                        final.Remove(existing);
                    }
                }
                foreach ((string name, OciFileEvidence addition) in additions)
                {
                    if (addition.Kind != "hardlink")
                    {
                        final[name] = addition;
                    }
                }
                foreach (OciFileEvidence link in additions.Values.Where(e => e.Kind == "hardlink"))
                {
                    string target = link.Target!;
                    var visited = new HashSet<string>(StringComparer.Ordinal) { link.Path };
                    while (additions.TryGetValue(target, out OciFileEvidence? next) && next.Kind == "hardlink")
                    {
                        if (!visited.Add(target))
                        {
                            throw new InvalidDataException("Cyclic OCI hard link.");
                        }
                        target = next.Target!;
                    }
                    if (!final.TryGetValue(target, out OciFileEvidence? content) || content.Kind != "file")
                    {
                        throw new InvalidDataException("OCI hard link does not identify final regular file bytes.");
                    }
                    final[link.Path] = content with { Path = link.Path };
                }
                foreach (string name in final.Keys)
                {
                    int slash = name.IndexOf('/', StringComparison.Ordinal);
                    while (slash >= 0)
                    {
                        if (final.ContainsKey(name[..slash]))
                        {
                            throw new InvalidDataException("OCI path traverses a non-directory image entry.");
                        }
                        slash = name.IndexOf('/', slash + 1);
                    }
                }
            }
            return final;
        }

        /// <summary>
        /// Normalizes relative OCI entry paths and rejects traversal, ambiguous segments, and unsupported path forms.
        /// </summary>
        internal static string Normalize(string value, bool directory = false)
        {
            while (value.StartsWith("./", StringComparison.Ordinal))
            {
                value = value[2..];
            }
            if (directory)
            {
                value = value.TrimEnd('/');
                if (value is "" or ".")
                {
                    return string.Empty;
                }
            }
            if (value.Length > 4096 || value.StartsWith('/') ||
                value.Contains('\\', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal) ||
                value.Contains('\0', StringComparison.Ordinal) ||
                value.Split('/').Any(p => p is "" or "." or ".."))
            {
                throw new InvalidDataException("Unsafe OCI filesystem path.");
            }
            return value;
        }

        private static bool ShouldRemove(
            string path,
            HashSet<string> opaque,
            HashSet<string> deletions,
            Dictionary<string, OciFileEvidence> additions,
            HashSet<string> directories)
        {
            if (opaque.Contains(string.Empty) || deletions.Contains(path) || directories.Contains(path))
            {
                return true;
            }
            int slash = path.IndexOf('/', StringComparison.Ordinal);
            while (slash >= 0)
            {
                if (opaque.Contains(path[..(slash + 1)]) || deletions.Contains(path[..slash]) ||
                    additions.ContainsKey(path[..slash]))
                {
                    return true;
                }
                slash = path.IndexOf('/', slash + 1);
            }
            return false;
        }

        private static Stream OpenLayer(string path, string mediaType)
        {
            FileStream? source = null;
            try
            {
                source = File.OpenRead(path);
                Stream result = mediaType switch
                {
                    "application/vnd.oci.image.layer.v1.tar" => source,
                    "application/vnd.oci.image.layer.v1.tar+gzip" or
                    "application/vnd.docker.image.rootfs.diff.tar.gzip" =>
                        new GZipStream(source, CompressionMode.Decompress),
                    _ => throw new InvalidDataException("Unsupported OCI layer compression.")
                };
                source = null;
                return result;
            }
            finally
            {
                source?.Dispose();
            }
        }

        private const long kMaximumLayerBytes = 4L * 1024 * 1024 * 1024;
        private const long kMaximumImageBytes = 16L * 1024 * 1024 * 1024;
        private const long kMaximumFileBytes = 1024L * 1024 * 1024;

        private sealed class BoundedHashStream(Stream inner, long limit) : Stream
        {
            /// <summary>
            /// Gets the total number of bytes consumed from the wrapped stream.
            /// </summary>
            public long BytesRead { get; private set; }

            /// <summary>
            /// Gets a value indicating that sequential reads are supported.
            /// </summary>
            public override bool CanRead => true;

            /// <summary>
            /// Gets a value indicating that seeking is not supported.
            /// </summary>
            public override bool CanSeek => false;

            /// <summary>
            /// Gets a value indicating that writing is not supported.
            /// </summary>
            public override bool CanWrite => false;

            /// <summary>
            /// Rejects length queries because the wrapped read stream does not expose a supported length operation.
            /// </summary>
            public override long Length => throw new NotSupportedException();

            /// <summary>
            /// Gets the number of consumed bytes and rejects attempts to change the stream position.
            /// </summary>
            public override long Position
            {
                get => BytesRead;
                set => throw new NotSupportedException();
            }

            /// <summary>
            /// Returns the SHA-256 digest of bytes consumed since the previous digest reset and resets the hash state.
            /// </summary>
            public string GetDigest()
            {
                return "sha256:" + Convert.ToHexStringLower(m_hash.GetHashAndReset());
            }

            /// <summary>
            /// Reads into an array segment, enforces the expanded-byte limit, and includes consumed bytes in the hash.
            /// </summary>
            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = inner.Read(buffer, offset, count);
                Observe(buffer.AsSpan(offset, read));
                return read;
            }

            /// <summary>
            /// Asynchronously reads into memory, enforces the expanded-byte limit, and hashes the consumed bytes.
            /// </summary>
            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                Observe(buffer.Span[..read]);
                return read;
            }

            /// <summary>
            /// Asynchronously reads an array segment through the bounded, hashing memory-based read implementation.
            /// </summary>
            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            /// <summary>
            /// Rejects flushing because this wrapper only supports reads.
            /// </summary>
            public override void Flush()
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Rejects seeking because reads must remain sequential for byte accounting and hashing.
            /// </summary>
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Rejects changes to the length of the read-only stream.
            /// </summary>
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Rejects writes to the read-only stream.
            /// </summary>
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_hash.Dispose();
                }
                base.Dispose(disposing);
            }

            private void Observe(ReadOnlySpan<byte> bytes)
            {
                BytesRead = checked(BytesRead + bytes.Length);
                if (BytesRead > limit)
                {
                    throw new InvalidDataException("OCI expanded layer exceeds the byte limit.");
                }
                m_hash.AppendData(bytes);
            }

            private readonly IncrementalHash m_hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }
    }
}
