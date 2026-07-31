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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// An <see cref="IXRegistryResourceStore"/> that keeps each document in one file named after
    /// its store key, which for the WoT registry is the SHA-256 content digest.
    /// <para>
    /// The layout is deliberately the one <see cref="FileWotRegistryStore"/> has always written —
    /// <c>{root}/{digest}.bin</c> — so moving the byte layer behind the injectable xRegistry
    /// interface needs no on-disk migration and existing registry folders keep working. Because the
    /// key is a content digest the files are immutable: writing a document that is already present
    /// is a no-op, and two resource versions with identical bytes share one file.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Substituting a different <see cref="IXRegistryResourceStore"/> is what lets a WoT registry
    /// run in a high-availability or distributed deployment, because the documents then live in a
    /// store every node can reach rather than in the server process.
    /// </remarks>
    public sealed class WotBlobResourceStore : IXRegistryResourceStore, IDisposable
    {
        /// <summary>
        /// Initializes the store over a directory of a file system.
        /// </summary>
        /// <param name="rootPath">The directory that holds the document files.</param>
        /// <param name="fileSystem">The file system to use; defaults to the local one.</param>
        /// <exception cref="ArgumentException"><paramref name="rootPath"/> is null or empty.</exception>
        public WotBlobResourceStore(string rootPath, IFileSystem? fileSystem = null)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                throw new ArgumentException("A root path is required.", nameof(rootPath));
            }

            m_rootPath = rootPath;
            m_fileSystem = fileSystem ?? LocalFileSystem.Instance;
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ReadAsync(
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

            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_fileSystem.Exists(path))
                {
                    return default;
                }

                using Stream stream = m_fileSystem.OpenRead(path);
                if (offset >= stream.Length || count == 0)
                {
                    return ByteString.From([]);
                }

                int take = (int)Math.Min(count, stream.Length - offset);
                stream.Seek(offset, SeekOrigin.Begin);
                var chunk = new byte[take];
                int read = 0;
                while (read < take)
                {
                    int n = await ReadBlockAsync(stream, chunk, read, take - read, ct)
                        .ConfigureAwait(false);
                    if (n == 0)
                    {
                        break;
                    }
                    read += n;
                }
                return ByteString.From(read == take ? chunk : chunk.AsSpan(0, read).ToArray());
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask WriteAsync(
            string resourceKey,
            long offset,
            ByteString data,
            CancellationToken ct = default)
        {
            string path = PathFor(resourceKey);
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                byte[] existing = m_fileSystem.Exists(path)
                    ? await ReadAllAsync(path, ct).ConfigureAwait(false)
                    : [];
                ReadOnlySpan<byte> chunk = data.IsNull ? default : data.Span;
                long mergedLength = Math.Max(existing.Length, offset + chunk.Length);
                if (mergedLength > int.MaxValue)
                {
                    throw new IOException("The blob is too large to stage in memory.");
                }
                var merged = new byte[(int)mergedLength];
                existing.CopyTo(merged.AsSpan());
                chunk.CopyTo(merged.AsSpan((int)offset));

                await DurableWriteAsync(path, merged, ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<long> GetLengthAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            string path = PathFor(resourceKey);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_fileSystem.Exists(path))
                {
                    return -1;
                }
                return m_fileSystem.GetLength(path);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(
            string resourceKey,
            CancellationToken ct = default)
        {
            string path = PathFor(resourceKey);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_fileSystem.Exists(path))
                {
                    return false;
                }
                m_fileSystem.Delete(path);
                return true;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the semaphore that serializes access to the backing files.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private static async ValueTask<int> ReadBlockAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
#if NETFRAMEWORK || NETSTANDARD2_0
            return await stream.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
#else
            return await stream.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
#endif
        }

        private static async ValueTask WriteBlockAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken ct)
        {
#if NETFRAMEWORK || NETSTANDARD2_0
            await stream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#else
            await stream.WriteAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
#endif
        }

        private async ValueTask<byte[]> ReadAllAsync(string path, CancellationToken ct)
        {
            using Stream stream = m_fileSystem.OpenRead(path);
            var buffer = new byte[stream.Length];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await ReadBlockAsync(stream, buffer, read, buffer.Length - read, ct)
                    .ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }
            return read == buffer.Length ? buffer : buffer.AsSpan(0, read).ToArray();
        }

        private async ValueTask DurableWriteAsync(string path, byte[] bytes, CancellationToken ct)
        {
            string directory = Path.GetDirectoryName(path)!;
            string tempPath = Path.Combine(
                directory,
                Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (Stream output = m_fileSystem.OpenWrite(tempPath))
                {
                    await WriteBlockAsync(output, bytes, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                }

                if (m_fileSystem is not IAtomicFileReplace atomic)
                {
                    throw new IOException(
                        "Durable WoT blob storage requires a file system that supports atomic file replacement.");
                }

                atomic.Replace(tempPath, path);
            }
            finally
            {
                try
                {
                    if (m_fileSystem.Exists(tempPath))
                    {
                        m_fileSystem.Delete(tempPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>
        /// Maps a store key onto a file name. A content digest — the only key the WoT registry
        /// uses — is already a safe file name and is kept verbatim so the layout matches what the
        /// registry has always written. Any other key is hex-encoded and prefixed, which keeps the
        /// store usable for arbitrary keys without letting an encoded key collide with a verbatim
        /// one or escape the root directory.
        /// </summary>
        private string PathFor(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }

            return Path.Combine(m_rootPath, FileNameFor(resourceKey) + ".bin");
        }

        private static string FileNameFor(string resourceKey)
        {
            if (IsVerbatimSafe(resourceKey))
            {
                return resourceKey;
            }

            var name = new StringBuilder((resourceKey.Length * 2) + 1);
            name.Append('_');
            foreach (byte b in Encoding.UTF8.GetBytes(resourceKey))
            {
                name.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return name.ToString();
        }

        private static bool IsVerbatimSafe(string resourceKey)
        {
            foreach (char c in resourceKey)
            {
                // char.IsAsciiLetterOrDigit is .NET 7+; this also targets net48 and
                // netstandard2.0, and char.IsLetterOrDigit would accept non-ASCII letters that are
                // not safe in a file name.
                bool ascii = (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z');
                if (!ascii && c != '-')
                {
                    return false;
                }
            }
            return true;
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly string m_rootPath;
        private readonly IFileSystem m_fileSystem;
    }
}
