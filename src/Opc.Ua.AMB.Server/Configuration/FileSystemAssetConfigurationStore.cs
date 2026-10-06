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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.AMB.Server.Configuration
{
    /// <summary>
    /// <see cref="IAssetConfigurationStore"/> that keeps the configured
    /// values of each asset in a JSON document inside an
    /// <see cref="IFileSystemProvider"/>, so they survive a server restart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each asset is stored as <c>{root}/{hash}.json</c>, where <c>hash</c> is
    /// the hexadecimal SHA-256 of its <c>ProductInstanceUri</c>. Hashing keeps
    /// the URI, which a client may have influenced, out of the path, and the
    /// document records the URI it was written for so a collision is detected
    /// rather than silently shared.
    /// </para>
    /// <para>
    /// A document is never overwritten in place: the new one is written
    /// completely to <c>{hash}.json.tmp</c> first and then takes the place of
    /// the old one. <see cref="IFileSystemProvider"/> has no atomic replace,
    /// so the old document is deleted before the new one is moved in; a crash
    /// in between leaves the complete new document behind, which the next
    /// read picks up. A crash while the new document is written leaves the old
    /// one, the last acknowledged state.
    /// </para>
    /// <para>
    /// The provider must permit writes. Reads and writes of one store
    /// instance are serialized; two stores over the same directory are not
    /// coordinated with each other.
    /// </para>
    /// </remarks>
    public sealed class FileSystemAssetConfigurationStore : IAssetConfigurationStore, IDisposable
    {
        /// <summary>
        /// The provider-relative directory used when none is given.
        /// </summary>
        public const string DefaultRootPath = "/AssetConfiguration";

        /// <summary>
        /// Creates a store kept in a directory of the local file system.
        /// </summary>
        /// <param name="directory">The directory; created on the first write.</param>
        /// <param name="logger">Receives a warning for a document that cannot be read.</param>
        /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
        public FileSystemAssetConfigurationStore(string directory, ILogger? logger = null)
            : this(CreateProvider(directory), "/", logger)
        {
        }

        /// <summary>
        /// Creates a store kept below <paramref name="rootPath"/> inside a file
        /// system provider.
        /// </summary>
        /// <param name="provider">The writable provider that backs the store.</param>
        /// <param name="rootPath">
        /// The provider-relative directory of the documents, created on the
        /// first write.
        /// </param>
        /// <param name="logger">Receives a warning for a document that cannot be read.</param>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="rootPath"/> is not an absolute provider path or
        /// navigates upwards.
        /// </exception>
        public FileSystemAssetConfigurationStore(
            IFileSystemProvider provider,
            string rootPath = DefaultRootPath,
            ILogger? logger = null)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_rootPath = ValidateRootPath(rootPath);
            m_logger = logger;
        }

        /// <inheritdoc/>
        /// <remarks>Always <see langword="true"/>: the values are written to the provider.</remarks>
        public bool IsPersistent => true;

        /// <inheritdoc/>
        public async ValueTask<string?> GetValueAsync(
            string productInstanceUri,
            string name,
            CancellationToken cancellationToken = default)
        {
            MemoryAssetConfigurationStore.Validate(productInstanceUri, name);
            await m_semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AssetConfigurationDocument? document = await ReadAsync(
                    productInstanceUri,
                    cancellationToken).ConfigureAwait(false);
                return document?.Values != null &&
                    document.Values.TryGetValue(name, out string? value)
                    ? value
                    : null;
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SetValueAsync(
            string productInstanceUri,
            string name,
            string? value,
            CancellationToken cancellationToken = default)
        {
            MemoryAssetConfigurationStore.Validate(productInstanceUri, name, value);
            await m_semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AssetConfigurationDocument document = await ReadAsync(
                    productInstanceUri,
                    cancellationToken).ConfigureAwait(false) ??
                    new AssetConfigurationDocument { ProductInstanceUri = productInstanceUri };
                document.Values ??= [];
                if (value == null)
                {
                    document.Values.Remove(name);
                }
                else
                {
                    document.Values[name] = value;
                }
                await WriteAsync(document, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_semaphore.Dispose();
        }

        private async ValueTask<AssetConfigurationDocument?> ReadAsync(
            string productInstanceUri,
            CancellationToken cancellationToken)
        {
            string path = PathOf(productInstanceUri);
            FileSystemEntry? entry = await m_provider
                .GetEntryAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (entry is not { IsDirectory: false })
            {
                // A write that stopped after the old document was deleted
                // left the complete new one under its temporary name.
                path = TemporaryPathOf(path);
                entry = await m_provider.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
                if (entry is not { IsDirectory: false })
                {
                    return null;
                }
            }

            AssetConfigurationDocument? document;
            try
            {
                using Stream stream = await m_provider
                    .OpenReadAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                document = await JsonSerializer.DeserializeAsync(
                    stream,
                    AssetConfigurationJsonContext.Default.AssetConfigurationDocument,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                // A document cut short by a crash during a write must not stop
                // the server from starting; the asset falls back to the value
                // the application configures until a client writes again.
                m_logger?.UnreadableDocument(ex, path);
                return null;
            }

            if (document == null ||
                !string.Equals(document.ProductInstanceUri, productInstanceUri, StringComparison.Ordinal))
            {
                m_logger?.ForeignDocument(path, productInstanceUri);
                return null;
            }
            return document;
        }

        private async ValueTask WriteAsync(
            AssetConfigurationDocument document,
            CancellationToken cancellationToken)
        {
            FileSystemEntry? root = await m_provider
                .GetEntryAsync(m_rootPath, cancellationToken)
                .ConfigureAwait(false);
            if (root == null)
            {
                await m_provider.CreateDirectoryAsync(m_rootPath, cancellationToken).ConfigureAwait(false);
            }

            string path = PathOf(document.ProductInstanceUri!);
            string temporary = TemporaryPathOf(path);
            using (Stream stream = await m_provider
                .OpenWriteAsync(temporary, FileWriteMode.Truncate, cancellationToken)
                .ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    AssetConfigurationJsonContext.Default.AssetConfigurationDocument,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // The new document is complete; it takes the place of the old one.
            if (await m_provider.GetEntryAsync(path, cancellationToken).ConfigureAwait(false) != null)
            {
                await m_provider.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            }
            await m_provider.MoveAsync(temporary, path, cancellationToken).ConfigureAwait(false);
        }

        private static string TemporaryPathOf(string path)
        {
            return path + ".tmp";
        }

        private string PathOf(string productInstanceUri)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(productInstanceUri);
#if NET8_0_OR_GREATER
            string hash = Convert.ToHexString(SHA256.HashData(utf8)).ToLowerInvariant();
#else
            using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(utf8))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
#endif
            return m_rootPath == "/" ? "/" + hash + ".json" : m_rootPath + "/" + hash + ".json";
        }

        private static string ValidateRootPath(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || rootPath[0] != '/' || rootPath.AsSpan().IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "The root path must be an absolute provider path such as '/AssetConfiguration'.",
                    nameof(rootPath));
            }
            foreach (string segment in rootPath.Split('/'))
            {
                if (segment is "." or "..")
                {
                    throw new ArgumentException(
                        "The root path must not navigate upwards.",
                        nameof(rootPath));
                }
            }
            string trimmed = rootPath.TrimEnd('/');
            return trimmed.Length == 0 ? "/" : trimmed;
        }

        private static PhysicalFileSystemProvider CreateProvider(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("The directory must not be empty.", nameof(directory));
            }
            Directory.CreateDirectory(directory);
            return new PhysicalFileSystemProvider(directory, mountName: "AssetConfiguration");
        }

        private readonly IFileSystemProvider m_provider;
        private readonly string m_rootPath;
        private readonly ILogger? m_logger;
        private readonly SemaphoreSlim m_semaphore = new(1, 1);
    }

    /// <summary>
    /// The JSON document of one asset.
    /// </summary>
    internal sealed class AssetConfigurationDocument
    {
        /// <summary>
        /// The <c>ProductInstanceUri</c> the document was written for.
        /// </summary>
        public string? ProductInstanceUri { get; set; }

        /// <summary>
        /// The configured values by name.
        /// </summary>
        public Dictionary<string, string>? Values { get; set; }
    }

    /// <summary>
    /// Source-generated serialization of <see cref="AssetConfigurationDocument"/>.
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AssetConfigurationDocument))]
    internal sealed partial class AssetConfigurationJsonContext : JsonSerializerContext;

    internal static partial class FileSystemAssetConfigurationStoreLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.AssetConfigurationStore + 0,
            Level = LogLevel.Warning,
            Message = "Asset configuration document {Path} cannot be read and is ignored.")]
        public static partial void UnreadableDocument(this ILogger logger, Exception exception, string path);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetConfigurationStore + 1,
            Level = LogLevel.Warning,
            Message = "Asset configuration document {Path} does not belong to {ProductInstanceUri} and is ignored.")]
        public static partial void ForeignDocument(this ILogger logger, string path, string productInstanceUri);
    }
}
