/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Async-compatible reader/writer lock with writer priority.
    /// Many concurrent readers may hold the lock simultaneously; while
    /// a writer holds the lock, both readers and other writers are
    /// excluded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Designed as a focused, no-dependency replacement for
    /// <c>Nito.AsyncEx.AsyncReaderWriterLock</c>. The implementation
    /// uses a single <see cref="SemaphoreSlim"/> as the writer-entry
    /// gate and a small synchronous critical section to atomically
    /// pair active-reader-count mutations with drain-signal mutations.
    /// </para>
    /// <para>
    /// <b>Reader path (hot path):</b> acquires the writer-entry
    /// semaphore briefly, increments the active-reader counter under
    /// <c>lock (m_state)</c>, releases the semaphore, returns. While
    /// readers are active, the writer-entry semaphore is free, so
    /// readers do not contend with each other on it.
    /// </para>
    /// <para>
    /// <b>Writer path (cold path):</b> acquires and HOLDS the
    /// writer-entry semaphore for the full duration of the write.
    /// New readers therefore block on the semaphore until the writer
    /// releases. The writer then waits, under <c>lock (m_state)</c>,
    /// for the active-reader count to reach zero — readers that were
    /// already in flight at the time the writer acquired the
    /// semaphore can drain. The drain wait is signaled by the LAST
    /// reader to leave (counter -> 0). Both the counter mutation and
    /// the drain signal are coordinated under the same
    /// <c>lock (m_state)</c>, so the writer cannot observe a stale
    /// "drained" signal that misses a fresh reader entry.
    /// </para>
    /// <para>
    /// <b>Cancellation:</b> both <see cref="ReaderLockAsync"/> and
    /// <see cref="WriterLockAsync"/> respect the supplied
    /// <see cref="CancellationToken"/>. If the writer is cancelled
    /// while waiting for the drain signal, the writer-entry
    /// semaphore is released so subsequent writers and readers can
    /// proceed.
    /// </para>
    /// <para>
    /// <b>Fairness:</b> ordering of waiters on the writer-entry
    /// semaphore follows .NET's <see cref="SemaphoreSlim"/>
    /// scheduling. This is not strictly FIFO but is adequate for a
    /// scenario where writer entries are rare (e.g. session
    /// reconnect / failover) and reader entries are the common case.
    /// </para>
    /// <para>
    /// <b>Reentrancy:</b> not supported. A task that already holds the
    /// reader lock and then asks for the writer lock will deadlock
    /// (the writer will wait for itself to drain). Callers must avoid
    /// nested acquisition.
    /// </para>
    /// </remarks>
    public sealed class AsyncReaderWriterLock
    {
        /// <summary>
        /// Asynchronously acquires a reader lock. Multiple readers
        /// may hold the lock concurrently; a reader cannot proceed
        /// while a writer holds the lock. Dispose the returned
        /// <see cref="Releaser"/> to release.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<Releaser> ReaderLockAsync(
            CancellationToken ct = default)
        {
            await m_writer.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                lock (m_state)
                {
                    m_activeReaders++;
                }
            }
            finally
            {
                m_writer.Release();
            }
            return new Releaser(this, isWriter: false);
        }

        /// <summary>
        /// Asynchronously acquires the writer lock. Excludes both
        /// other writers and all readers for the duration the lock
        /// is held. Dispose the returned <see cref="Releaser"/> to
        /// release.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<Releaser> WriterLockAsync(
            CancellationToken ct = default)
        {
            await m_writer.WaitAsync(ct).ConfigureAwait(false);
            Task? wait;
            lock (m_state)
            {
                if (m_activeReaders == 0)
                {
                    wait = null;
                }
                else
                {
                    m_drained = new TaskCompletionSource<object?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = m_drained.Task;
                }
            }
            if (wait != null)
            {
                try
                {
                    await wait.WaitAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    lock (m_state)
                    {
                        // Readers may have drained between the wait
                        // throwing and this catch; clear the TCS
                        // either way so the next writer creates a
                        // fresh one.
                        m_drained = null;
                    }
                    m_writer.Release();
                    throw;
                }
            }
            return new Releaser(this, isWriter: true);
        }

        /// <summary>
        /// Releases a reader lock. Internal — invoked by
        /// <see cref="Releaser.Dispose"/>.
        /// </summary>
        private void ReleaseReader()
        {
            lock (m_state)
            {
                if (--m_activeReaders == 0)
                {
                    // Notify a waiting writer (if any) that the last
                    // active reader has departed.
                    m_drained?.TrySetResult(null);
                    m_drained = null;
                }
            }
        }

        /// <summary>
        /// Releases the writer lock. Internal — invoked by
        /// <see cref="Releaser.Dispose"/>.
        /// </summary>
        private void ReleaseWriter()
        {
            m_writer.Release();
        }

        /// <summary>
        /// Disposable handle returned from
        /// <see cref="ReaderLockAsync"/> /
        /// <see cref="WriterLockAsync"/>. Releases the corresponding
        /// lock on dispose.
        /// </summary>
        public readonly struct Releaser : IDisposable
        {
            private readonly AsyncReaderWriterLock m_owner;
            private readonly bool m_isWriter;

            internal Releaser(
                AsyncReaderWriterLock owner, bool isWriter)
            {
                m_owner = owner;
                m_isWriter = isWriter;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (m_owner == null)
                {
                    return;
                }
                if (m_isWriter)
                {
                    m_owner.ReleaseWriter();
                }
                else
                {
                    m_owner.ReleaseReader();
                }
            }
        }

        private readonly SemaphoreSlim m_writer = new(1, 1);
        private readonly object m_state = new();
        private int m_activeReaders;
        private TaskCompletionSource<object?>? m_drained;
    }
}
