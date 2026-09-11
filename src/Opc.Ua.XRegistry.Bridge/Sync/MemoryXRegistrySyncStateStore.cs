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

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// Process-local state with one writer and immutable published snapshots.
    /// It intentionally makes no process-restart durability claim.
    /// </summary>
    public sealed class MemoryXRegistrySyncStateStore : IXRegistrySyncStateStore
    {
        /// <summary>
        /// Creates a pristine, process-local store with a bounded snapshot size and no filesystem artifacts.
        /// </summary>
        /// <param name="maximumBytes">The positive snapshot byte limit; defaults to 67,108,864 bytes (64 MiB).</param>
        /// <exception cref="ArgumentOutOfRangeException">The snapshot byte limit is not positive.</exception>
        public MemoryXRegistrySyncStateStore(int maximumBytes = 67_108_864)
        {
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            m_maximumBytes = maximumBytes;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Writer ownership is exclusive only within this store instance. Readers receive copies of published state;
        /// no session provides durability across process restarts.
        /// </remarks>
        public async ValueTask<IXRegistrySyncStateSession> OpenAsync(
            bool readOnly = false,
            CancellationToken cancellationToken = default)
        {
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                if (!readOnly && m_writer)
                {
                    throw new IOException("Synchronization state already has an active writer.");
                }
                if (!readOnly)
                {
                    m_writer = true;
                }
                return new Session(this, readOnly);
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        /// <remarks>Dispose writer sessions first; disposed stores and their sessions cannot be reused.</remarks>
        /// <exception cref="InvalidOperationException">A writer session is still active.</exception>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            await m_serial.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_writer)
                {
                    throw new InvalidOperationException("Dispose the synchronization writer session before its store.");
                }
                m_disposed = true;
            }
            finally
            {
                m_serial.Release();
            }
            m_serial.Dispose();
        }

        private void EnsureUsable()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(MemoryXRegistrySyncStateStore));
            }
        }

        private readonly SemaphoreSlim m_serial = new(1, 1);
        private readonly int m_maximumBytes;
        private ByteString m_state;
        private bool m_writer;
        private bool m_disposed;

        private sealed class Session(MemoryXRegistrySyncStateStore owner, bool readOnly) : IXRegistrySyncStateSession
        {
            public bool IsReadOnly => readOnly;

            public async ValueTask<ByteString> ReadAsync(CancellationToken cancellationToken = default)
            {
                await owner.m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EnsureUsable();
                    return owner.m_state.IsNull ? default : ByteString.From(owner.m_state.Span);
                }
                finally
                {
                    owner.m_serial.Release();
                }
            }

            public async ValueTask CommitAsync(ByteString state, CancellationToken cancellationToken = default)
            {
                await owner.m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EnsureUsable();
                    if (IsReadOnly)
                    {
                        throw new InvalidOperationException("A read-only synchronization session cannot commit.");
                    }
                    if (state.IsNull || state.Length == 0 || state.Length > owner.m_maximumBytes)
                    {
                        throw new InvalidDataException("Synchronization state is empty or exceeds its storage quota.");
                    }
                    owner.m_state = ByteString.From(state.Span);
                }
                finally
                {
                    owner.m_serial.Release();
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0 || IsReadOnly)
                {
                    return;
                }
                await owner.m_serial.WaitAsync().ConfigureAwait(false);
                try
                {
                    owner.m_writer = false;
                }
                finally
                {
                    owner.m_serial.Release();
                }
            }

            private void EnsureUsable()
            {
                owner.EnsureUsable();
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(Session));
                }
            }

            private int m_disposed;
        }
    }
}
