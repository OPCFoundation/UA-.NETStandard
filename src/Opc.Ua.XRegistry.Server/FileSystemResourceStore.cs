/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// <see cref="IXRegistryResourceStore"/> that keeps each resource document in a file, so the
    /// documents outlive the server process and a shared volume can back a distributed deployment.
    /// Built on the <see cref="IFileSystem"/> abstraction, which makes the store testable against a
    /// virtual file system and lets a deployment substitute its own.
    /// </summary>
    public sealed class FileSystemResourceStore : IXRegistryResourceStore, IDisposable
    {
        /// <summary>
        /// Initializes the store over a directory of a file system.
        /// </summary>
        /// <param name="rootPath">The directory that holds the resource documents.</param>
        /// <param name="fileSystem">The file system to use; defaults to the local one.</param>
        /// <exception cref="ArgumentException"><paramref name="rootPath"/> is null or empty.</exception>
        public FileSystemResourceStore(string rootPath, IFileSystem? fileSystem = null)
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
                // IFileSystem only exposes OpenRead/OpenWrite, so a random-access write is a
                // read-modify-write of the document. Documents are bounded by MaxResourceBytes.
                byte[] existing = [];
                if (m_fileSystem.Exists(path))
                {
                    using Stream input = m_fileSystem.OpenRead(path);
                    existing = new byte[input.Length];
                    int read = 0;
                    while (read < existing.Length)
                    {
                        int n = await ReadBlockAsync(
                            input, existing, read, existing.Length - read, ct).ConfigureAwait(false);
                        if (n == 0)
                        {
                            break;
                        }
                        read += n;
                    }
                }

                // Writing past the end grows the document; the gap reads back as zero bytes.
                int end = (int)Math.Max(existing.Length, offset + data.Length);
                byte[] merged = existing.Length == end ? existing : new byte[end];
                if (!ReferenceEquals(merged, existing))
                {
                    existing.CopyTo(merged.AsSpan());
                }
                data.Span.CopyTo(merged.AsSpan((int)offset));

                using Stream output = m_fileSystem.OpenWrite(path);
                await WriteBlockAsync(output, merged, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default)
        {
            string path = PathFor(resourceKey);

            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return m_fileSystem.Exists(path) ? m_fileSystem.GetLength(path) : -1;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
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

        /// <summary>
        /// Reads into <paramref name="buffer"/> from <paramref name="offset"/>. Wraps the two Stream
        /// overloads so the callers stay free of conditional compilation.
        /// </summary>
        private static async ValueTask<int> ReadBlockAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
#if NETFRAMEWORK
            return await stream.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
#else
            return await stream.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Writes <paramref name="buffer"/> to <paramref name="stream"/>. Wraps the two Stream
        /// overloads so the callers stay free of conditional compilation.
        /// </summary>
        private static async ValueTask WriteBlockAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken ct)
        {
#if NETFRAMEWORK
            await stream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#else
            await stream.WriteAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Maps a store key to a file name. The key is hex-encoded rather than used directly, so a
        /// key can never escape the root directory or collide with the file system's own rules.
        /// </summary>
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <exception cref="ArgumentException"><paramref name="resourceKey"/> is null or empty.</exception>
        private string PathFor(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }

            var name = new StringBuilder(resourceKey.Length * 2);
            foreach (byte b in Encoding.UTF8.GetBytes(resourceKey))
            {
                name.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return Path.Combine(m_rootPath, name.ToString());
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly string m_rootPath;
        private readonly IFileSystem m_fileSystem;
    }
}
