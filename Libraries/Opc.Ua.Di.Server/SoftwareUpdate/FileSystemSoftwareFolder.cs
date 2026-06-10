/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Di.Server.SoftwareUpdate
{
    /// <summary>
    /// <see cref="ISoftwareFolder"/> implementation that persists
    /// versions on an <see cref="IFileSystemProvider"/>. Each version
    /// is stored as a subdirectory containing the payload binary and
    /// a metadata JSON document; an optional <c>.current</c> marker
    /// file records the active version.
    /// </summary>
    /// <remarks>
    /// <para>Layout under the root path:</para>
    /// <list type="bullet">
    ///   <item><description><c>{root}/{version}/payload.bin</c> — version payload</description></item>
    ///   <item><description><c>{root}/{version}/metadata.json</c> — <see cref="SoftwarePackage"/> record</description></item>
    ///   <item><description><c>{root}/.current</c> — text file with the currently active version identifier</description></item>
    /// </list>
    /// </remarks>
    public sealed class FileSystemSoftwareFolder : ISoftwareFolder
    {
        private const string PayloadFileName = "payload.bin";
        private const string MetadataFileName = "metadata.json";
        private const string CurrentMarkerFileName = ".current";

        /// <summary>
        /// Creates a new folder rooted at <paramref name="rootPath"/>
        /// inside <paramref name="provider"/>.
        /// </summary>
        public FileSystemSoftwareFolder(
            IFileSystemProvider provider,
            NodeId elementId,
            string rootPath = "/Software",
            TimeProvider? timeProvider = null)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException(
                    "Root path must be a non-empty string.", nameof(rootPath));
            }

            m_provider = provider;
            m_rootPath = rootPath;
            m_time = timeProvider ?? TimeProvider.System;
            ElementId = elementId;
        }

        /// <inheritdoc/>
        public NodeId ElementId { get; }

        /// <inheritdoc/>
        public async IAsyncEnumerable<SoftwarePackage> ListVersionsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            FileSystemEntry? rootEntry = await m_provider
                .GetEntryAsync(m_rootPath, cancellationToken)
                .ConfigureAwait(false);
            if (rootEntry == null)
            {
                yield break;
            }
            await foreach (FileSystemEntry child in m_provider
                .EnumerateAsync(m_rootPath, cancellationToken)
                .ConfigureAwait(false))
            {
                if (!child.IsDirectory)
                {
                    continue;
                }
                SoftwarePackage? metadata = await TryReadMetadataAsync(
                    child.Name, cancellationToken).ConfigureAwait(false);
                if (metadata != null)
                {
                    yield return metadata;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<SoftwarePackage?> GetCurrentVersionAsync(
            CancellationToken cancellationToken = default)
        {
            string markerPath = BuildPath(CurrentMarkerFileName);
            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(markerPath, cancellationToken)
                .ConfigureAwait(false);
            if (entry is null or { IsDirectory: true })
            {
                return null;
            }
            string version;
            using (Stream stream = await m_provider
                .OpenReadAsync(markerPath, cancellationToken)
                .ConfigureAwait(false))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                version = await ReadAllTextAsync(reader, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(version))
            { return null; }
            return await TryReadMetadataAsync(version, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<SoftwarePackage?> GetVersionAsync(
            string version, CancellationToken cancellationToken = default)
        {
            ValidateVersion(version);
            return TryReadMetadataAsync(version, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenVersionAsync(
            string version, CancellationToken cancellationToken = default)
        {
            ValidateVersion(version);
            string path = BuildPath(version, PayloadFileName);
            return m_provider.OpenReadAsync(path, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<SoftwarePackage> AddVersionAsync(
            SoftwarePackage metadata,
            Stream payload,
            CancellationToken cancellationToken = default)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }
            ValidateVersion(metadata.Version);
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if (!m_provider.IsWritable)
            {
                throw new InvalidOperationException(
                    "The configured IFileSystemProvider is read-only.");
            }

            await m_provider.CreateDirectoryAsync(
                BuildPath(metadata.Version), cancellationToken)
                .ConfigureAwait(false);

            long size = 0;
            string payloadPath = BuildPath(metadata.Version, PayloadFileName);
            using (Stream payloadStream = await m_provider
                .OpenWriteAsync(payloadPath, FileWriteMode.Truncate, cancellationToken)
                .ConfigureAwait(false))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
#if NETFRAMEWORK
                while ((bytesRead = await payload
                    .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .ConfigureAwait(false)) > 0)
                {
                    await payloadStream
                        .WriteAsync(buffer, 0, bytesRead, cancellationToken)
                        .ConfigureAwait(false);
                    size += bytesRead;
                }
#else
                while ((bytesRead = await payload
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false)) > 0)
                {
                    await payloadStream
                        .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                        .ConfigureAwait(false);
                    size += bytesRead;
                }
#endif
                await payloadStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            SoftwarePackage stamped = metadata with
            {
                SizeBytes = size,
                CreatedAt = m_time.GetUtcNow()
            };

            await WriteMetadataAsync(stamped, cancellationToken).ConfigureAwait(false);
            return stamped;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveVersionAsync(
            string version, CancellationToken cancellationToken = default)
        {
            ValidateVersion(version);
            if (!m_provider.IsWritable)
            {
                throw new InvalidOperationException(
                    "The configured IFileSystemProvider is read-only.");
            }

            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(BuildPath(version), cancellationToken)
                .ConfigureAwait(false);
            if (entry is null or { IsDirectory: false })
            {
                return false;
            }
            await DeleteEntryRecursivelyAsync(
                BuildPath(version), cancellationToken).ConfigureAwait(false);

            // Clear current marker if it pointed to this version.
            string? current = await ReadCurrentMarkerAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(current, version, StringComparison.Ordinal))
            {
                FileSystemEntry? markerEntry = await m_provider
                    .GetEntryAsync(BuildPath(CurrentMarkerFileName), cancellationToken)
                    .ConfigureAwait(false);
                if (markerEntry != null)
                {
                    await m_provider.DeleteAsync(
                        BuildPath(CurrentMarkerFileName), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public async ValueTask SetCurrentVersionAsync(
            string version, CancellationToken cancellationToken = default)
        {
            ValidateVersion(version);
            if (!m_provider.IsWritable)
            {
                throw new InvalidOperationException(
                    "The configured IFileSystemProvider is read-only.");
            }

            // Ensure the version exists before marking it active.
            SoftwarePackage? existing = await TryReadMetadataAsync(
                version, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                throw new ArgumentException(
                    $"Cannot set unknown version '{version}' as current.",
                    nameof(version));
            }

            byte[] payload = Encoding.UTF8.GetBytes(version);
            using Stream stream = await m_provider
                .OpenWriteAsync(
                    BuildPath(CurrentMarkerFileName),
                    FileWriteMode.Truncate, cancellationToken)
                .ConfigureAwait(false);
#if NETFRAMEWORK
            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken)
                .ConfigureAwait(false);
#else
            await stream.WriteAsync(payload.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
#endif
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        // ──────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────

        private async ValueTask<SoftwarePackage?> TryReadMetadataAsync(
            string version, CancellationToken cancellationToken)
        {
            string metadataPath = BuildPath(version, MetadataFileName);
            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(metadataPath, cancellationToken)
                .ConfigureAwait(false);
            if (entry is null or { IsDirectory: true })
            {
                return null;
            }
            using Stream stream = await m_provider
                .OpenReadAsync(metadataPath, cancellationToken)
                .ConfigureAwait(false);
            return await JsonSerializer
                .DeserializeAsync(
                    stream,
                    SoftwarePackageJsonContext.Default.SoftwarePackage,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask WriteMetadataAsync(
            SoftwarePackage metadata, CancellationToken cancellationToken)
        {
            string metadataPath = BuildPath(metadata.Version, MetadataFileName);
            using Stream stream = await m_provider
                .OpenWriteAsync(metadataPath, FileWriteMode.Truncate, cancellationToken)
                .ConfigureAwait(false);
            await JsonSerializer
                .SerializeAsync(
                    stream,
                    metadata,
                    SoftwarePackageJsonContext.Default.SoftwarePackage,
                    cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<string?> ReadCurrentMarkerAsync(
            CancellationToken cancellationToken)
        {
            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(BuildPath(CurrentMarkerFileName), cancellationToken)
                .ConfigureAwait(false);
            if (entry is null or { IsDirectory: true })
            {
                return null;
            }
            using Stream stream = await m_provider
                .OpenReadAsync(BuildPath(CurrentMarkerFileName), cancellationToken)
                .ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            return await ReadAllTextAsync(reader, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ReadAllTextAsync(
            StreamReader reader, CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            return (await reader.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false)).Trim();
#else
            return (await reader.ReadToEndAsync().ConfigureAwait(false)).Trim();
#endif
        }

        private async ValueTask DeleteEntryRecursivelyAsync(
            string path, CancellationToken cancellationToken)
        {
            await foreach (FileSystemEntry child in m_provider
                .EnumerateAsync(path, cancellationToken)
                .ConfigureAwait(false))
            {
                string childPath = path.TrimEnd('/') + "/" + child.Name;
                if (child.IsDirectory)
                {
                    await DeleteEntryRecursivelyAsync(childPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await m_provider.DeleteAsync(childPath, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            await m_provider.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
        }

        private static void ValidateVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException(
                    "Version identifier must be non-empty.", nameof(version));
            }
            if (version.IndexOfAny(s_invalidVersionChars) >= 0)
            {
                throw new ArgumentException(
                    "Version identifier must not contain path separators.",
                    nameof(version));
            }
        }

        private string BuildPath(string version)
        {
            return m_rootPath.TrimEnd('/') + "/" + version;
        }

        private string BuildPath(string version, string fileName)
        {
            return BuildPath(version) + "/" + fileName;
        }

        private readonly IFileSystemProvider m_provider;
        private readonly string m_rootPath;
        private readonly TimeProvider m_time;

        private static readonly char[] s_invalidVersionChars = new[] { '/', '\\' };
    }
}
