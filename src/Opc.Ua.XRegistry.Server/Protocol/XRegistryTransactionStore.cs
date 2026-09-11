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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Atomically publishes an entire registry generation, including content and outcomes.
    /// A storage exception can mean an indeterminate commit; callers must stop writing.
    /// </summary>
    public interface IXRegistryTransactionStore
    {
        /// <summary>
        /// Whether acknowledged generations and replay outcomes survive a process restart.
        /// </summary>
        bool SupportsDurableReplay { get; }

        /// <summary>
        /// Loads a complete generation. Null means proven pristine storage, never corruption.
        /// </summary>
        ValueTask<ByteString> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Compares the entire previous generation and atomically publishes its replacement.
        /// Returns false only when the expected generation differs, before any publication.
        /// </summary>
        ValueTask<bool> CommitAsync(
            ByteString expected, ByteString replacement, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Process-local atomic storage. Replay outcomes do not survive process termination.
    /// </summary>
    public sealed class InMemoryXRegistryTransactionStore : IXRegistryTransactionStore
    {
        /// <inheritdoc/>
        public bool SupportsDurableReplay => false;

        /// <inheritdoc/>
        public ValueTask<ByteString> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<ByteString>(m_generation.IsNull ? default : ByteString.From(m_generation.Span));
            }
        }

        /// <inheritdoc/>
        public ValueTask<bool> CommitAsync(
            ByteString expected, ByteString replacement, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (replacement.IsNull)
            {
                throw new ArgumentException("A committed generation cannot be null.", nameof(replacement));
            }
            lock (m_gate)
            {
                if (expected.IsNull != m_generation.IsNull || !expected.Span.SequenceEqual(m_generation.Span))
                {
                    return new ValueTask<bool>(false);
                }
                m_generation = ByteString.From(replacement.Span);
                return new ValueTask<bool>(true);
            }
        }

        private readonly Lock m_gate = new();
        private ByteString m_generation;
    }

    /// <summary>
    /// Single-writer local-disk storage with durable staged publication and compare-and-swap.
    /// The caller owns its lifetime. Recovery artifacts and ambiguous failures fail closed.
    /// </summary>
    public sealed class FileXRegistryTransactionStore : IXRegistryTransactionStore, IDisposable
    {
        /// <summary>
        /// Opens a private persistent registry directory and claims exclusive writer ownership.
        /// This provider is not intended for shared network or eventually-consistent filesystems.
        /// </summary>
        public FileXRegistryTransactionStore(string directory, int maximumBytes = 134_217_728)
        {
            directory.ThrowIfNull(nameof(directory));
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            m_root = Path.GetFullPath(directory);
            m_maximumBytes = maximumBytes;
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
            m_path = Path.Combine(m_root, "registry.json");
            m_staging = Path.Combine(m_root, "registry.pending");
            m_initialized = Path.Combine(m_root, "registry.initialized");
            m_ownership = new FileStream(Path.Combine(m_root, "registry.writer"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        /// <inheritdoc/>
        public bool SupportsDurableReplay => true;

        /// <inheritdoc/>
        public async ValueTask<ByteString> LoadAsync(CancellationToken cancellationToken = default)
        {
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                if (File.Exists(m_staging))
                {
                    throw new InvalidDataException(
                        "A registry staging artifact requires explicit recovery; no empty baseline will be created.");
                }
                return await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> CommitAsync(
            ByteString expected, ByteString replacement, CancellationToken cancellationToken = default)
        {
            if (replacement.IsNull || replacement.Length > m_maximumBytes)
            {
                throw new ArgumentException("A replacement generation is absent or exceeds the storage limit.",
                    nameof(replacement));
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                ByteString current = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
                if (current.IsNull != expected.IsNull || !current.Span.SequenceEqual(expected.Span))
                {
                    return false;
                }
                using (var stream = new FileStream(m_staging, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
#if NETSTANDARD2_1_OR_GREATER || NET
                    await stream.WriteAsync(replacement.Memory, cancellationToken).ConfigureAwait(false);
#else
                    byte[] bytes = replacement.ToArray();
                    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#endif
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                DirectoryDurability.Flush(m_root);
                cancellationToken.ThrowIfCancellationRequested();
                m_indeterminate = true;
                if (current.IsNull)
                {
                    using var marker = new FileStream(m_initialized, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
#if NETSTANDARD2_1_OR_GREATER || NET
                    await marker.WriteAsync(s_initializationMarker.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
                    await marker.WriteAsync(s_initializationMarker, 0, s_initializationMarker.Length, cancellationToken)
                        .ConfigureAwait(false);
#endif
                    await marker.FlushAsync(cancellationToken).ConfigureAwait(false);
                    marker.Flush(flushToDisk: true);
                    DirectoryDurability.Flush(m_root);
                }
                LocalFileSystem.Instance.Replace(m_staging, m_path);
                DirectoryDurability.Flush(m_root);
                m_indeterminate = false;
                return true;
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_ownership.Dispose();
            m_disposed = true;
            m_serial.Dispose();
        }

        private async ValueTask<ByteString> ReadCurrentAsync(CancellationToken cancellationToken)
        {
            bool exists = File.Exists(m_path);
            if (exists != File.Exists(m_initialized))
            {
                throw new InvalidDataException(
                    "Registry data or its initialization marker is missing; explicit recovery is required.");
            }
            if (!exists)
            {
                return default;
            }
            using var stream = new FileStream(m_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 || stream.Length > m_maximumBytes)
            {
                throw new InvalidDataException("The persisted registry is empty or exceeds its storage limit.");
            }
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
#if NETSTANDARD2_1_OR_GREATER || NET
                int count = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
#else
                int count = await stream.ReadAsync(bytes, offset, bytes.Length - offset, cancellationToken)
                    .ConfigureAwait(false);
#endif
                if (count == 0)
                {
                    throw new EndOfStreamException("The persisted registry is incomplete.");
                }
                offset += count;
            }
            return new ByteString(bytes);
        }

        private void EnsureUsable()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(FileXRegistryTransactionStore));
            }
            if (m_indeterminate)
            {
                throw new IOException("Registry publication durability is uncertain; close and recover the store.");
            }
        }

        private readonly string m_root;
        private readonly string m_path;
        private readonly string m_staging;
        private readonly string m_initialized;
        private readonly int m_maximumBytes;
        private readonly FileStream m_ownership;
        private readonly SemaphoreSlim m_serial = new(1, 1);
        private bool m_disposed;
        private bool m_indeterminate;
        private static readonly byte[] s_initializationMarker = [1];
    }
}
