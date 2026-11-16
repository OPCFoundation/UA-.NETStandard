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
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("The path cannot be null, empty, or whitespace.", nameof(directory));
            }
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
                return await ReadCoreAsync(key, ct).ConfigureAwait(false);
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
            byte[] snapshot = bytes.ToArray();
            if (snapshot.Length == 0 || snapshot.Length > MaximumBytes)
            {
                throw new InvalidDataException("A public push checkpoint has an invalid size.");
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteCoreAsync(key, snapshot, ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Atomically replaces a checkpoint only when its current bytes match the expected value.
        /// </summary>
        public async ValueTask<bool> CompareExchangeAsync(
            string key,
            byte[]? expected,
            byte[] replacement,
            CancellationToken ct)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }
            byte[]? expectedSnapshot = expected == null ? null : (byte[])expected.Clone();
            byte[] replacementSnapshot = (byte[])replacement.Clone();
            if (replacementSnapshot.Length == 0 || replacementSnapshot.Length > MaximumBytes)
            {
                throw new InvalidDataException("A public push checkpoint has an invalid size.");
            }

            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                byte[]? current = await ReadCoreAsync(key, ct).ConfigureAwait(false);
                if (!AreEqual(current, expectedSnapshot))
                {
                    return false;
                }
                await WriteCoreAsync(key, replacementSnapshot, ct).ConfigureAwait(false);
                return true;
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
            using SHA256 sha256 = SHA256.Create();
            byte[] digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            return Path.Combine(m_directory, BitConverter.ToString(digest).Replace("-", string.Empty) + ".bin");
        }

        private async ValueTask<byte[]?> ReadCoreAsync(string key, CancellationToken ct)
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
            while ((count = await input.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > MaximumBytes)
                {
                    throw new InvalidDataException("A public push checkpoint exceeds its size limit.");
                }
                await buffer.WriteAsync(chunk, 0, count, ct).ConfigureAwait(false);
            }
            return buffer.ToArray();
        }

        private async ValueTask WriteCoreAsync(string key, byte[] bytes, CancellationToken ct)
        {
            string path = GetPath(key);
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            ExceptionDispatchInfo? failure = null;
            try
            {
                using (Stream output = m_fileSystem.OpenWrite(staging))
                {
                    await output.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
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

        private static bool AreEqual(byte[]? left, byte[]? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
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
