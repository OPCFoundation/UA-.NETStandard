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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Stores each device in one bounded, versioned XML snapshot, including result file bytes.
    /// </summary>
    /// <remarks>
    /// The file system must support atomic <see cref="IFileSystem.Replace"/>. Writes are staged in the
    /// same directory, flushed and then published; failures never select an in-memory fallback.
    /// Operations on one instance are serialized. Use one writer per directory; separate processes
    /// are not coordinated and the last successful complete snapshot wins.
    /// The caller owns the injected file system and must not dispose this store during an operation.
    /// </remarks>
    public sealed class FileSystemLadsStore : ILadsStore, IDisposable
    {
        /// <summary>
        /// Creates a file-system-backed store without reading or creating any files.
        /// The file system creates the directory on the first write.
        /// The byte limit applies to the complete serialized snapshot, including base64 overhead.
        /// </summary>
        public FileSystemLadsStore(IFileSystem fs, string directory, int maxBytes = 64 * 1024 * 1024)
        {
            m_fileSystem = fs ?? throw new ArgumentNullException(nameof(fs));
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A storage directory is required.", nameof(directory));
            }
            LadsSnapshotCodec.ValidateLimit(maxBytes);
            m_directory = directory;
            m_maxBytes = maxBytes;
        }

        /// <inheritdoc/>
        public async ValueTask<LadsStoredDeviceData?> LoadAsync(
            string deviceIdentity,
            CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path = GetPath(deviceIdentity);
                cancellationToken.ThrowIfCancellationRequested();
                using Stream? source = OpenSnapshot(path);
                if (source == null)
                {
                    return null;
                }
                if (source.CanSeek && source.Length > m_maxBytes)
                {
                    throw new InvalidDataException("The LADS snapshot exceeds the configured byte limit.");
                }
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[Math.Min(m_maxBytes, 8192)];
                while (true)
                {
#if NET5_0_OR_GREATER
                    int count = await source.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
                    int count = await source.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
#endif
                    if (count == 0)
                    {
                        break;
                    }
                    if (buffer.Length > m_maxBytes - count)
                    {
                        throw new InvalidDataException("The LADS snapshot exceeds the configured byte limit.");
                    }
                    buffer.Write(chunk, 0, count);
                }
                cancellationToken.ThrowIfCancellationRequested();
                LadsStoredDeviceData data = LadsSnapshotCodec.Decode(buffer.ToArray(), deviceIdentity, m_maxBytes);
                cancellationToken.ThrowIfCancellationRequested();
                return data;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SaveAsync(
            string deviceIdentity,
            LadsStoredDeviceData data,
            CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] content = LadsSnapshotCodec.Encode(data, deviceIdentity, m_maxBytes);
                cancellationToken.ThrowIfCancellationRequested();
                string path = GetPath(deviceIdentity);
                string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
                try
                {
                    using (Stream destination = m_fileSystem.OpenWrite(pending))
                    {
#if NET5_0_OR_GREATER
                        await destination.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
                        await destination.WriteAsync(content, 0, content.Length, cancellationToken)
                            .ConfigureAwait(false);
#endif
                        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                        if (destination is FileStream file)
                        {
                            // FlushAsync alone does not force file contents onto durable storage.
                            file.Flush(flushToDisk: true);
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    m_fileSystem.Replace(pending, path);
                }
                catch (Exception failure)
                {
                    try
                    {
                        if (m_fileSystem.Exists(pending))
                        {
                            m_fileSystem.Delete(pending);
                        }
                    }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException(
                            "Snapshot publication and staging cleanup failed.", failure, cleanup);
                    }
                    throw;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    m_fileSystem.Delete(GetPath(deviceIdentity));
                }
                catch (FileNotFoundException)
                {
                    // Deleting an absent snapshot is idempotent.
                }
                catch (DirectoryNotFoundException)
                {
                    // The store directory has not been created or was already removed.
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the instance's operation gate, not the injected file system.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        /// <summary>
        /// Opens a snapshot, treating only genuinely missing files or directories as an absent snapshot.
        /// </summary>
        private Stream? OpenSnapshot(string path)
        {
            try
            {
                return m_fileSystem.OpenRead(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Derives a path containing only a fixed-size SHA-256 identity digest and a fixed extension.
        /// </summary>
        private string GetPath(string deviceIdentity)
        {
            byte[] identity = Encoding.UTF8.GetBytes(deviceIdentity);
#if NET5_0_OR_GREATER
            byte[] digest = SHA256.HashData(identity);
#else
            using SHA256 algorithm = SHA256.Create();
            byte[] digest = algorithm.ComputeHash(identity);
#endif
#if NET9_0_OR_GREATER
            string hash = Convert.ToHexStringLower(digest);
#elif NET5_0_OR_GREATER
            string hash = Convert.ToHexString(digest).ToLowerInvariant();
#else
            var hex = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
            {
                hex.Append("0123456789abcdef"[value >> 4]);
                hex.Append("0123456789abcdef"[value & 15]);
            }
            string hash = hex.ToString();
#endif
            return Path.Combine(m_directory, hash + ".lads.xml");
        }

        /// <summary>The injected file system.</summary>
        private readonly IFileSystem m_fileSystem;

        /// <summary>The configured storage directory.</summary>
        private readonly string m_directory;

        /// <summary>The complete snapshot byte limit.</summary>
        private readonly int m_maxBytes;

        /// <summary>Serializes operations on this store instance.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
    }
}
