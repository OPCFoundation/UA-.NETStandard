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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Reads the committed blob directory of a <see cref="FileWotRegistryStore"/>
    /// through the <see cref="IXRegistryResourceStore"/> contract.
    /// </summary>
    /// <remarks>
    /// The blob directory names a document <c>{sha256}.bin</c>, which is the
    /// layout the store's own durable write path produces and the layout an
    /// operator inspecting a registry root sees. The general-purpose
    /// <c>FileSystemResourceStore</c> hex-encodes its keys into file names
    /// instead, so it cannot read this directory. This adapter supplies the
    /// committed half of a <see cref="StagedResourceStore"/> without changing
    /// either layout.
    /// <para>
    /// It is read-only. Content reaches the blob directory only by promotion
    /// during a commit, which goes through the store's durable write path so
    /// that a corrupt or unwritable blob is reported the same way on every
    /// path into the directory.
    /// </para>
    /// </remarks>
    internal sealed class BlobDirectoryResourceStore : IXRegistryResourceStore
    {
        /// <summary>
        /// Initializes the adapter over a blob directory.
        /// </summary>
        /// <param name="blobsFolder">The blob directory of the registry root.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="blobsFolder"/> is null or empty.
        /// </exception>
        public BlobDirectoryResourceStore(string blobsFolder)
        {
            if (string.IsNullOrEmpty(blobsFolder))
            {
                throw new ArgumentException("A blobs folder is required.", nameof(blobsFolder));
            }
            m_blobsFolder = blobsFolder;
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> ReadAsync(
            string resourceKey,
            long offset,
            int count,
            CancellationToken ct = default)
        {
            string path = PathFor(resourceKey);
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (!File.Exists(path))
            {
                return new ValueTask<ByteString>(default(ByteString));
            }
            return ReadCoreAsync(path, offset, count, ct);
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            string resourceKey,
            long offset,
            ByteString data,
            CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "The committed WoT registry blob directory is written only by " +
                "promotion during a commit.");
        }

        /// <inheritdoc/>
        public ValueTask<long> GetLengthAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            string path = PathFor(resourceKey);
            var info = new FileInfo(path);
            return new ValueTask<long>(info.Exists ? info.Length : -1L);
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            string path = PathFor(resourceKey);
            if (!File.Exists(path))
            {
                return new ValueTask<bool>(false);
            }
            File.Delete(path);
            return new ValueTask<bool>(true);
        }

        private static async ValueTask<ByteString> ReadCoreAsync(
            string path,
            long offset,
            int count,
            CancellationToken ct)
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (offset >= stream.Length)
            {
                return ByteString.From([]);
            }
            stream.Seek(offset, SeekOrigin.Begin);
            int take = (int)Math.Min(count, stream.Length - offset);
            byte[] chunk = new byte[take];
            int read = 0;
            while (read < take)
            {
#if NETFRAMEWORK
                int block = await stream.ReadAsync(chunk, read, take - read, ct)
                    .ConfigureAwait(false);
#else
                int block = await stream.ReadAsync(chunk.AsMemory(read, take - read), ct)
                    .ConfigureAwait(false);
#endif
                if (block <= 0)
                {
                    break;
                }
                read += block;
            }
            return ByteString.From(read == take ? chunk : chunk.AsSpan(0, read).ToArray());
        }

        /// <summary>
        /// Maps a store key to its blob file. The key must be a bare file-name
        /// token so that it cannot reach outside the blob directory.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="resourceKey"/> is null, empty, or not a bare name.
        /// </exception>
        private string PathFor(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }
            if (resourceKey.IndexOfAny(s_forbidden) >= 0 ||
                resourceKey is "." or "..")
            {
                throw new ArgumentException(
                    "A blob resource key must be a bare file-name token.",
                    nameof(resourceKey));
            }
            return Path.Combine(m_blobsFolder, resourceKey + ".bin");
        }

        private static readonly char[] s_forbidden =
            [.. Path.GetInvalidFileNameChars(), Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar];

        private readonly string m_blobsFolder;
    }
}
