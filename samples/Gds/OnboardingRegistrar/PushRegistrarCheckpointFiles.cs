/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Atomically publishes only this sample's public enrollment and signing-receipt checkpoints.
    /// The directory requires operator-controlled access and one owning process. It is not secret storage.
    /// </summary>
    internal sealed class PushRegistrarCheckpointFiles : IDisposable
    {
        /// <summary>
        /// Exclusively leases the checkpoint directory for this process; filesystem replacement must be atomic.
        /// </summary>
        public PushRegistrarCheckpointFiles(string directory, IFileSystem? fileSystem = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            m_directory = Path.GetFullPath(directory);
            m_fileSystem = fileSystem ?? LocalFileSystem.Instance;
            Directory.CreateDirectory(m_directory);
            m_lease = new FileStream(
                Path.Combine(m_directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        /// <summary>
        /// Reads a bounded complete public checkpoint or returns null if it has never been published.
        /// </summary>
        public async ValueTask<byte[]?> ReadAsync(string key, CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string path = GetPath(key);
                if (!m_fileSystem.Exists(path))
                {
                    return null;
                }
                if (m_fileSystem.GetLength(path) > MaximumBytes)
                {
                    throw new InvalidDataException("A public push checkpoint exceeds its size limit.");
                }
                using Stream input = m_fileSystem.OpenRead(path);
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false)) != 0)
                {
                    if (buffer.Length + count > MaximumBytes)
                    {
                        throw new InvalidDataException("A public push checkpoint exceeds its size limit.");
                    }
                    await buffer.WriteAsync(chunk.AsMemory(0, count), ct).ConfigureAwait(false);
                }
                return buffer.ToArray();
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Flushes a complete sibling staging file before indivisible publication, preserving cleanup failures.
        /// </summary>
        public async ValueTask WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            if (bytes.Length == 0 || bytes.Length > MaximumBytes)
            {
                throw new InvalidDataException("A public push checkpoint has an invalid size.");
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string path = GetPath(key);
                string staging = path + "." + Guid.NewGuid().ToString("N") + ".pending";
                ExceptionDispatchInfo? failure = null;
                try
                {
                    using (Stream output = m_fileSystem.OpenWrite(staging))
                    {
                        await output.WriteAsync(bytes, ct).ConfigureAwait(false);
                        await output.FlushAsync(ct).ConfigureAwait(false);
                        if (output is FileStream file)
                        {
                            file.Flush(flushToDisk: true);
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                    m_fileSystem.Replace(staging, path);
                }
                catch (Exception error)
                {
                    failure = ExceptionDispatchInfo.Capture(error);
                }
                try
                {
                    if (m_fileSystem.Exists(staging))
                    {
                        m_fileSystem.Delete(staging);
                    }
                }
                catch (Exception cleanupError) when (failure != null)
                {
                    throw new AggregateException(failure.SourceException, cleanupError);
                }
                failure?.Throw();
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Removes a public checkpoint only on an explicit provider removal request.
        /// </summary>
        public async ValueTask<bool> RemoveAsync(string key, CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string path = GetPath(key);
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
        /// Releases the exclusive writer lease after all provider operations have stopped.
        /// </summary>
        public void Dispose()
        {
            m_lease.Dispose();
            m_gate.Dispose();
        }

        /// <summary>
        /// Uses a digest filename so neither identifiers nor namespace URIs become path components.
        /// </summary>
        private string GetPath(string key)
        {
            return Path.Combine(
                m_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".bin");
        }

        /// <summary>The maximum size of one public checkpoint.</summary>
        internal const int MaximumBytes = 16 * 1024 * 1024;

        /// <summary>The exclusive public checkpoint directory.</summary>
        private readonly string m_directory;

        /// <summary>The existing filesystem abstraction used for complete-value publication.</summary>
        private readonly IFileSystem m_fileSystem;

        /// <summary>The process-lifetime exclusive writer lease.</summary>
        private readonly FileStream m_lease;

        /// <summary>Serializes publication, reads and removal within the owning process.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
    }
}
