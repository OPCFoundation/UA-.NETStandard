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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// One local writer per state directory, durable staged snapshots and atomic
    /// IFileSystem.Replace publication. Provision the directory with private ACLs.
    /// Uncertain commits retain a recovery marker and never fall back to empty state.
    /// Construction and read-only sessions do not create files.
    /// </summary>
    /// <remarks>
    /// The caller must qualify atomic replacement and the ownership and durability guarantees of any custom
    /// filesystem. Recovery artifacts, missing initialized state, and uncertain publication require explicit recovery;
    /// they are not repaired, deleted, or treated as a fresh job. The supplied filesystem remains caller-owned.
    /// </remarks>
    public sealed class FileXRegistrySyncStateStore : IXRegistrySyncStateStore
    {
        /// <summary>
        /// Configures a bounded file-backed state store without performing filesystem I/O.
        /// </summary>
        /// <param name="fileSystem">The caller-owned filesystem with qualified atomic replacement semantics.</param>
        /// <param name="directory">
        /// The nonblank state directory, normalized to a full path. Provision private permissions before writer use.
        /// </param>
        /// <param name="maximumBytes">The positive snapshot byte limit; defaults to 67,108,864 bytes (64 MiB).</param>
        /// <param name="durability">
        /// Explicitly qualified single-writer and storage barriers. Null selects
        /// <see cref="LocalXRegistrySyncFileDurability"/> only for <see cref="LocalFileSystem"/> on a local path.
        /// Custom filesystems and UNC or device paths require an explicitly supplied qualification.
        /// </param>
        /// <exception cref="ArgumentNullException">The filesystem or directory is null.</exception>
        /// <exception cref="ArgumentException">
        /// The directory is blank or invalid, or the filesystem or path requires an explicit durability provider.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">The snapshot byte limit is not positive.</exception>
        public FileXRegistrySyncStateStore(
            IFileSystem fileSystem,
            string directory,
            int maximumBytes = 67_108_864,
            IXRegistrySyncFileDurability? durability = null)
        {
            fileSystem.ThrowIfNull(nameof(fileSystem));
            directory.ThrowIfNull(nameof(directory));
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A state directory is required.", nameof(directory));
            }
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            if (durability is null && fileSystem.GetType() != typeof(LocalFileSystem))
            {
                throw new ArgumentException(
                    "A nonlocal IFileSystem needs an explicitly qualified ownership and durability provider.",
                    nameof(durability));
            }
            m_fileSystem = fileSystem;
            m_directory = Path.GetFullPath(directory);
            if (durability is null && m_directory.StartsWith("\\\\", StringComparison.Ordinal))
            {
                throw new ArgumentException("UNC and device paths require explicit filesystem qualification.",
                    nameof(directory));
            }
            m_maximumBytes = maximumBytes;
            m_durability = durability ?? new LocalXRegistrySyncFileDurability();
            m_statePath = Path.Combine(m_directory, "sync.state");
            m_stagingPath = Path.Combine(m_directory, "sync.staged");
            m_pendingPath = Path.Combine(m_directory, "sync.pending");
            m_initializedPath = Path.Combine(m_directory, "sync.initialized");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A read-only session creates no directory, ownership file, or other persistent artifact.
        /// A writer session acquires the qualified ownership handle and may initialize the state directory.
        /// </remarks>
        public async ValueTask<IXRegistrySyncStateSession> OpenAsync(
            bool readOnly = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureUsable();
            if (readOnly)
            {
                return new Session(this, null);
            }
            if (Interlocked.CompareExchange(ref m_writer, 1, 0) != 0)
            {
                throw new IOException("Synchronization state already has an active writer.");
            }
            IAsyncDisposable? ownership = null;
            bool opened = false;
            try
            {
                ownership = await m_durability.AcquireWriterAsync(m_fileSystem, m_directory, cancellationToken)
                    .ConfigureAwait(false);
                ownership.ThrowIfNull(nameof(ownership));
                EnsureUsable();
                var session = new Session(this, ownership);
                opened = true;
                return session;
            }
            finally
            {
                if (!opened)
                {
                    if (ownership is not null)
                    {
                        await ownership.DisposeAsync().ConfigureAwait(false);
                    }
                    Interlocked.Exchange(ref m_writer, 0);
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Dispose writer sessions first. Disposing this store neither deletes state or recovery artifacts
        /// nor disposes the caller-supplied filesystem.
        /// </remarks>
        /// <exception cref="InvalidOperationException">A writer session is still active.</exception>
        public ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref m_writer) != 0)
            {
                throw new InvalidOperationException("Dispose the synchronization writer session before its store.");
            }
            Interlocked.Exchange(ref m_disposed, 1);
            return default;
        }

        private async ValueTask<ByteString> ReadCurrentAsync(CancellationToken cancellationToken)
        {
            EnsureUsable();
            EnsureNoRecoveryArtifacts();
            bool exists = m_fileSystem.Exists(m_statePath);
            bool initialized = m_fileSystem.Exists(m_initializedPath);
            if (exists != initialized)
            {
                throw new InvalidDataException(
                    "Synchronization state or its initialization marker is missing; explicit recovery is required.");
            }
            if (!exists)
            {
                return default;
            }
            long length = m_fileSystem.GetLength(m_statePath);
            if (length <= 0 || length > m_maximumBytes)
            {
                throw new InvalidDataException("Persisted synchronization state is empty or exceeds its quota.");
            }
            using Stream stream = m_fileSystem.OpenRead(m_statePath);
            byte[] bytes = new byte[(int)length];
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
                    throw new EndOfStreamException("Persisted synchronization state is truncated.");
                }
                offset += count;
            }
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("Synchronization state changed during its read.");
            }
            EnsureNoRecoveryArtifacts();
            return new ByteString(bytes);
        }

        private async ValueTask CommitAsync(ByteString expected, ByteString state, CancellationToken cancellationToken)
        {
            if (state.IsNull || state.Length == 0 || state.Length > m_maximumBytes)
            {
                throw new InvalidDataException("Synchronization state is empty or exceeds its storage quota.");
            }
            ByteString current = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (current.IsNull != expected.IsNull || !current.Span.SequenceEqual(expected.Span))
            {
                throw new IOException("Synchronization state changed outside the exclusive writer.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref m_uncertain, 1);
            await WriteDurableAsync(m_pendingPath, ByteString.From("pending"u8), cancellationToken)
                .ConfigureAwait(false);
            await m_durability.FlushDirectoryAsync(m_directory, cancellationToken).ConfigureAwait(false);
            if (!m_fileSystem.Exists(m_initializedPath))
            {
                await WriteDurableAsync(m_initializedPath, ByteString.From("initialized"u8), cancellationToken)
                    .ConfigureAwait(false);
                await m_durability.FlushDirectoryAsync(m_directory, cancellationToken).ConfigureAwait(false);
            }
            await WriteDurableAsync(m_stagingPath, state, cancellationToken).ConfigureAwait(false);
            await m_durability.FlushDirectoryAsync(m_directory, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            m_fileSystem.Replace(m_stagingPath, m_statePath);
            await m_durability.FlushDirectoryAsync(m_directory, CancellationToken.None).ConfigureAwait(false);
            m_fileSystem.Delete(m_pendingPath);
            await m_durability.FlushDirectoryAsync(m_directory, CancellationToken.None).ConfigureAwait(false);
            Interlocked.Exchange(ref m_uncertain, 0);
        }

        private async ValueTask WriteDurableAsync(string path, ByteString bytes, CancellationToken cancellationToken)
        {
            if (m_fileSystem.Exists(path))
            {
                throw new InvalidDataException("A synchronization recovery artifact must not be overwritten.");
            }
            using Stream stream = m_fileSystem.OpenWrite(path);
#if NETSTANDARD2_1_OR_GREATER || NET
            await stream.WriteAsync(bytes.Memory, cancellationToken).ConfigureAwait(false);
#else
            byte[] buffer = bytes.ToArray();
            await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#endif
            await m_durability.FlushFileAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        private void EnsureUsable()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FileXRegistrySyncStateStore));
            }
            if (Volatile.Read(ref m_uncertain) != 0)
            {
                throw new IOException("Synchronization state durability is uncertain; explicit recovery is required.");
            }
        }

        private void EnsureNoRecoveryArtifacts()
        {
            if (m_fileSystem.Exists(m_pendingPath) || m_fileSystem.Exists(m_stagingPath))
            {
                throw new InvalidDataException(
                    "An interrupted synchronization state commit requires explicit recovery; no reset is permitted.");
            }
        }

        private readonly IFileSystem m_fileSystem;
        private readonly IXRegistrySyncFileDurability m_durability;
        private readonly string m_directory;
        private readonly string m_statePath;
        private readonly string m_stagingPath;
        private readonly string m_pendingPath;
        private readonly string m_initializedPath;
        private readonly int m_maximumBytes;
        private int m_writer;
        private int m_disposed;
        private int m_uncertain;

        private sealed class Session(FileXRegistrySyncStateStore owner, IAsyncDisposable? ownership)
            : IXRegistrySyncStateSession
        {
            public bool IsReadOnly => ownership is null;

            public async ValueTask<ByteString> ReadAsync(CancellationToken cancellationToken = default)
            {
                await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EnsureUsable();
                    m_expected = await owner.ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
                    m_loaded = true;
                    return m_expected.IsNull ? default : ByteString.From(m_expected.Span);
                }
                finally
                {
                    m_serial.Release();
                }
            }

            public async ValueTask CommitAsync(ByteString state, CancellationToken cancellationToken = default)
            {
                await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EnsureUsable();
                    if (IsReadOnly || !m_loaded)
                    {
                        throw new InvalidOperationException("Read a writer session's current state before committing.");
                    }
                    await owner.CommitAsync(m_expected, state, cancellationToken).ConfigureAwait(false);
                    m_expected = ByteString.From(state.Span);
                }
                finally
                {
                    m_serial.Release();
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                await m_serial.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (ownership is not null)
                    {
                        await ownership.DisposeAsync().ConfigureAwait(false);
                        Interlocked.Exchange(ref owner.m_writer, 0);
                    }
                }
                finally
                {
                    m_serial.Release();
                    m_serial.Dispose();
                }
            }

            private void EnsureUsable()
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(Session));
                }
            }

            private readonly SemaphoreSlim m_serial = new(1, 1);
            private ByteString m_expected;
            private bool m_loaded;
            private int m_disposed;
        }
    }
}
