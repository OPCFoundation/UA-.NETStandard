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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Di.Server.SoftwareUpdate
{
    /// <summary>
    /// <see cref="ISoftwarePackageStore"/> implementation that composes
    /// over the server's existing
    /// <see cref="IFileSystemProvider"/> abstraction. Lets a single
    /// file-system mount serve both general file storage and software
    /// packages — addressing the rubber-duck constraint that the
    /// software-update layer should reuse the provider model rather
    /// than re-implementing file I/O.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each package is stored as two files inside the provider:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>{root}/{id}/payload.bin</c> — the package binary
    ///   </description></item>
    ///   <item><description>
    ///     <c>{root}/{id}/metadata.json</c> — the
    ///     <see cref="SoftwarePackage"/> record as JSON
    ///   </description></item>
    /// </list>
    /// <para>
    /// The provider must permit write operations for AddAsync / Delete
    /// to work; read-only providers can still serve ListAsync /
    /// GetAsync / OpenReadAsync.
    /// </para>
    /// </remarks>
    public sealed class FileSystemPackageStore : ISoftwarePackageStore
    {
        private const string PayloadFileName = "payload.bin";
        private const string MetadataFileName = "metadata.json";

        private readonly IFileSystemProvider m_provider;
        private readonly string m_rootPath;
        private readonly TimeProvider m_timeProvider;

#if NET8_0_OR_GREATER
        private static readonly System.Buffers.SearchValues<char> s_pathSeparators =
            System.Buffers.SearchValues.Create("/\\");
#else
        private static readonly char[] s_pathSeparators = ['/', '\\'];
#endif

        /// <summary>
        /// Creates a new store rooted at <paramref name="rootPath"/>
        /// inside <paramref name="provider"/>.
        /// </summary>
        /// <param name="provider">
        /// File system provider that backs the store.
        /// </param>
        /// <param name="rootPath">
        /// Provider-relative path under which packages are kept.
        /// Defaults to <c>"/SoftwarePackages"</c>; created on demand.
        /// </param>
        /// <param name="timeProvider">
        /// Source of timestamps stored in <see cref="SoftwarePackage.CreatedAt"/>.
        /// Defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        public FileSystemPackageStore(
            IFileSystemProvider provider,
            string rootPath = "/SoftwarePackages",
            TimeProvider? timeProvider = null)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException(
                    "Root path must be a non-empty string.", nameof(rootPath));
            }
            m_rootPath = rootPath;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<SoftwarePackage> ListAsync(
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
                SoftwarePackage? package = await TryReadMetadataAsync(
                    child.Name, cancellationToken).ConfigureAwait(false);
                if (package != null)
                {
                    yield return package;
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<SoftwarePackage?> GetAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            return TryReadMetadataAsync(packageId, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> ExistsAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(BuildPath(packageId, MetadataFileName), cancellationToken)
                .ConfigureAwait(false);
            return entry is { IsDirectory: false };
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenReadAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            string path = BuildPath(packageId, PayloadFileName);
            return m_provider.OpenReadAsync(path, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<SoftwarePackage> AddAsync(
            SoftwarePackage metadata,
            Stream payload,
            CancellationToken cancellationToken = default)
        {
            if (metadata == null) { throw new ArgumentNullException(nameof(metadata)); }
            ValidateId(metadata.Id);
            if (payload == null) { throw new ArgumentNullException(nameof(payload)); }
            if (!m_provider.IsWritable)
            {
                throw new InvalidOperationException(
                    "The configured IFileSystemProvider is read-only.");
            }

            string packageRoot = BuildPath(metadata.Id);
            await m_provider.CreateDirectoryAsync(packageRoot, cancellationToken)
                .ConfigureAwait(false);

            // Stream the payload to disk and track total size on the way through.
            long size = 0;
            string payloadPath = BuildPath(metadata.Id, PayloadFileName);
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

            SoftwarePackage final = metadata with
            {
                SizeBytes = size,
                CreatedAt = m_timeProvider.GetUtcNow()
            };

            await WriteMetadataAsync(final, cancellationToken).ConfigureAwait(false);
            return final;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(
            string packageId, CancellationToken cancellationToken = default)
        {
            ValidateId(packageId);
            if (!m_provider.IsWritable)
            {
                throw new InvalidOperationException(
                    "The configured IFileSystemProvider is read-only.");
            }

            string packageRoot = BuildPath(packageId);
            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(packageRoot, cancellationToken).ConfigureAwait(false);
            if (entry == null)
            {
                return false;
            }
            await m_provider.DeleteAsync(packageRoot, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        private async ValueTask<SoftwarePackage?> TryReadMetadataAsync(
            string packageId, CancellationToken cancellationToken)
        {
            string path = BuildPath(packageId, MetadataFileName);
            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
            if (entry is not { IsDirectory: false })
            {
                return null;
            }

            using Stream stream = await m_provider
                .OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
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
            string path = BuildPath(metadata.Id, MetadataFileName);
            using Stream stream = await m_provider
                .OpenWriteAsync(path, FileWriteMode.Truncate, cancellationToken)
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

        private string BuildPath(string packageId, string? leaf = null)
        {
            string root = m_rootPath.TrimEnd('/');
            string path = $"{root}/{packageId}";
            return leaf == null ? path : $"{path}/{leaf}";
        }

        private static void ValidateId(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException(
                    "Package id must be a non-empty string.", nameof(packageId));
            }
            if (packageId.AsSpan().IndexOfAny(s_pathSeparators) >= 0)
            {
                throw new ArgumentException(
                    "Package id must not contain path separators.", nameof(packageId));
            }
        }
    }

    /// <summary>
    /// JSON serialization context for <see cref="SoftwarePackage"/>.
    /// Hosted here so the source-generated metadata is available to
    /// AOT trimming without polluting the public namespace surface.
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(SoftwarePackage))]
    internal sealed partial class SoftwarePackageJsonContext : JsonSerializerContext
    {
    }
}
