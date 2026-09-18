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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// A content-addressed immutable document identity, separate from registry epochs and Version IDs.
    /// </summary>
    public sealed record XRegistryBlobReference
    {
        /// <summary>
        /// Validates a lowercase SHA-256 digest and nonnegative byte length.
        /// </summary>
        public XRegistryBlobReference(string sha256, long length)
        {
            if (sha256 is null ||
                sha256.Length != 64 ||
                sha256.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                length < 0)
            {
                throw new ArgumentException("A canonical SHA-256 identity and nonnegative length are required.");
            }
            Sha256 = sha256;
            Length = length;
        }

        /// <summary>
        /// Gets the content digest.
        /// </summary>
        public string Sha256 { get; }

        /// <summary>
        /// Gets the exact content length.
        /// </summary>
        public long Length { get; }
    }

    /// <summary>
    /// Stores immutable domain documents before a metadata-generation publication.
    /// Failed publication can leave unreferenced blobs; it must never leave dangling committed references.
    /// </summary>
    public interface IXRegistryDocumentStore
    {
        /// <summary>
        /// Whether acknowledged blobs survive process restart.
        /// </summary>
        bool IsDurable { get; }

        /// <summary>
        /// Streams and durably stages a bounded document without taking ownership of the input.
        /// </summary>
        ValueTask<XRegistryBlobReference> StoreAsync(
            Stream content, long maximumBytes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a verified immutable document. The caller owns and must dispose the returned stream.
        /// </summary>
        ValueTask<Stream> OpenReadAsync(
            XRegistryBlobReference reference, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes unreferenced blobs only under exclusive registry maintenance, with no outstanding preparations.
        /// The retained set must include all authoritative and recovery generations.
        /// </summary>
        ValueTask<int> CollectAsync(
            ArrayOf<XRegistryBlobReference> retained, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Single-writer local content-addressed storage with bounded streaming, digest verification
    /// and durable publication.
    /// The directory must be private to this store, not a shared/network filesystem.
    /// </summary>
    public sealed class FileXRegistryDocumentStore : IXRegistryDocumentStore, IAsyncDisposable
    {
        /// <summary>
        /// Claims a private document directory. A separate store instance cannot collect or publish concurrently.
        /// </summary>
        public FileXRegistryDocumentStore(string directory)
        {
            directory.ThrowIfNull(nameof(directory));
            m_root = Path.GetFullPath(directory);
            var parents = new Stack<string>();
            string? current = m_root;
            while (current is not null && !Directory.Exists(current))
            {
                string? parent = Path.GetDirectoryName(current);
                if (parent is not null)
                {
                    parents.Push(parent);
                }
                current = parent;
            }
            Directory.CreateDirectory(m_root);
            foreach (string parent in parents)
            {
                DirectoryDurability.Flush(parent);
            }
            if ((File.GetAttributes(m_root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("A document store cannot use a reparse-point directory.");
            }
            m_ownership = new FileStream(Path.Combine(m_root, "documents.writer"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        /// <inheritdoc/>
        public bool IsDurable => true;

        /// <inheritdoc/>
        public async ValueTask<XRegistryBlobReference> StoreAsync(
            Stream content, long maximumBytes, CancellationToken cancellationToken = default)
        {
            content.ThrowIfNull(nameof(content));
            if (!content.CanRead || maximumBytes < 0)
            {
                throw new ArgumentException("A readable document and nonnegative byte bound are required.");
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            string staging = Path.Combine(m_root, Guid.NewGuid().ToString("N") + ".pending");
            try
            {
                EnsureUsable();
                XRegistryBlobReference reference;
                using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    65_536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    reference =
                        await CopyAndHashAsync(content, output, maximumBytes, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    // FlushAsync does not promise a stable-storage barrier.
                    // TODO: Use an asynchronous flush-to-disk API when the supported frameworks expose one.
#pragma warning disable CA1849
                    output.Flush(flushToDisk: true);
#pragma warning restore CA1849
                }
                string target = BlobPath(reference);
                if (File.Exists(target))
                {
                    using Stream existing = await OpenVerifiedAsync(reference, cancellationToken).ConfigureAwait(false);
                    File.Delete(staging);
                }
                else
                {
                    File.Move(staging, target);
                }
                DirectoryDurability.Flush(m_root);
                return reference;
            }
            finally
            {
                try
                {
                    if (File.Exists(staging))
                    {
                        File.Delete(staging);
                    }
                }
                finally
                {
                    m_serial.Release();
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<Stream> OpenReadAsync(
            XRegistryBlobReference reference, CancellationToken cancellationToken = default)
        {
            reference.ThrowIfNull(nameof(reference));
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                return await OpenVerifiedAsync(reference, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<int> CollectAsync(
            ArrayOf<XRegistryBlobReference> retained, CancellationToken cancellationToken = default)
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRegistryBlobReference reference in retained)
            {
                keep.Add(reference.ThrowIfNull(nameof(retained)).Sha256);
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                foreach (XRegistryBlobReference reference in retained.Span.ToArray())
                {
                    using Stream verified = await OpenVerifiedAsync(reference, cancellationToken).ConfigureAwait(false);
                }
                int removed = 0;
                foreach (string path in Directory.EnumerateFiles(m_root, "*.blob", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Path.GetFileNameWithoutExtension(path);
                    _ = new XRegistryBlobReference(name, 0);
                    RejectReparsePoint(path);
                    if (!keep.Contains(name))
                    {
                        File.Delete(path);
                        removed++;
                    }
                }
                DirectoryDurability.Flush(m_root);
                return removed;
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposalStarted, 1) != 0)
            {
                return;
            }
            await m_serial.WaitAsync().ConfigureAwait(false);
            try
            {
                m_disposed = true;
                m_ownership.Dispose();
            }
            finally
            {
                m_serial.Release();
                m_serial.Dispose();
            }
        }

        private async ValueTask<Stream> OpenVerifiedAsync(XRegistryBlobReference reference, CancellationToken ct)
        {
            string path = BlobPath(reference);
            RejectReparsePoint(path);
            // The verified stream transfers to the caller; failed verification awaits disposal in finally.
            // TODO: Remove when CA2000 models this asynchronous ownership-transfer pattern.
#pragma warning disable CA2000
            FileStream? stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
#pragma warning restore CA2000
            try
            {
                if (stream.Length != reference.Length)
                {
                    throw new InvalidDataException("The document length differs from its committed reference.");
                }
                XRegistryBlobReference actual = await CopyAndHashAsync(stream, Stream.Null, reference.Length, ct)
                    .ConfigureAwait(false);
                if (actual != reference)
                {
                    throw new InvalidDataException("The document content digest is invalid.");
                }
                stream.Position = 0;
                FileStream result = stream;
                stream = null;
                return result;
            }
            finally
            {
                if (stream is not null)
                {
#if NETSTANDARD2_1_OR_GREATER || NET
                    await stream.DisposeAsync().ConfigureAwait(false);
#else
                    stream.Dispose();
#endif
                }
            }
        }

        private static async ValueTask<XRegistryBlobReference> CopyAndHashAsync(
            Stream input, Stream output, long maximumBytes, CancellationToken ct)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(65_536);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            try
            {
                while (true)
                {
#if NETSTANDARD2_1_OR_GREATER || NET
                    int read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
#else
                    int read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#endif
                    if (read == 0)
                    {
                        break;
                    }
                    if (read > maximumBytes - length)
                    {
                        throw new InvalidDataException("The document exceeds its byte bound.");
                    }
                    length += read;
                    hash.AppendData(buffer, 0, read);
#if NETSTANDARD2_1_OR_GREATER || NET
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
#else
                    await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
#endif
                }
#if NET9_0_OR_GREATER
                string digest = Convert.ToHexStringLower(hash.GetHashAndReset());
#elif NET5_0_OR_GREATER
                string digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
#else
                string digest = BitConverter.ToString(hash.GetHashAndReset())
                    .Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
#endif
                return new XRegistryBlobReference(digest, length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        private string BlobPath(XRegistryBlobReference reference)
        {
            return Path.Combine(m_root, reference.Sha256 + ".blob");
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("A document blob cannot be a reparse point.");
            }
        }

        private void EnsureUsable()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(FileXRegistryDocumentStore));
            }
        }

        private readonly string m_root;
        private readonly FileStream m_ownership;
        private readonly SemaphoreSlim m_serial = new(1, 1);
        private bool m_disposed;
        private int m_disposalStarted;
    }
}
