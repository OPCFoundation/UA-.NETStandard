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
using System.Runtime.CompilerServices;
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
    /// <see cref="WriterLockAsync(CancellationToken)"/> respect the supplied
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
    internal sealed class AsyncReaderWriterLock : IDisposable
    {
        /// <summary>
        /// Creates the lock.
        /// </summary>
        public AsyncReaderWriterLock()
        {
#if !NET6_0_OR_GREATER
            m_releaseReader = ReleaseReader;
#endif
        }

        /// <summary>
        /// Optional asynchronous operation that completes before a reader
        /// acquires the lock.
        /// </summary>
        public Func<CancellationToken, ValueTask>? BeforeReaderLockAsync { get; set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_isDisposed)
            {
                m_writer.Dispose();
                m_isDisposed = true;
            }
        }

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
            Func<CancellationToken, ValueTask>? beforeReaderLock = BeforeReaderLockAsync;
            if (beforeReaderLock != null)
            {
                await beforeReaderLock(ct).ConfigureAwait(false);
            }

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
        /// Acquires a reader lock without waiting when no writer holds or
        /// waits for the lock. Does not run <see cref="BeforeReaderLockAsync"/>;
        /// the caller checks the precondition it stands for. Release with
        /// <see cref="ExitReader"/> or <see cref="ExitReaderOnCompletion{T}"/>.
        /// </summary>
        /// <returns>True if the reader lock was acquired.</returns>
        public bool TryEnterReader()
        {
            if (!m_writer.Wait(0))
            {
                return false;
            }
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
            return true;
        }

        /// <summary>
        /// Releases a reader lock acquired with <see cref="TryEnterReader"/>.
        /// </summary>
        public void ExitReader()
        {
            ReleaseReader();
        }

        /// <summary>
        /// Releases a reader lock acquired with <see cref="TryEnterReader"/>
        /// when <paramref name="operation"/> completes, and returns a value
        /// task for the same result.
        /// </summary>
        /// <remarks>
        /// The lock is free again by the time an awaiter of the returned task
        /// resumes, as with a <c>using</c> block around the awaited operation.
        /// On .NET 6 and later the returned task awaits the operation in a pooled
        /// state machine, the only continuation of the operation, so this
        /// allocates nothing in the steady state; the returned task must be
        /// awaited once, like any value task. Otherwise the release is the first
        /// continuation registered on the operation's task and the caller's await
        /// the second, which allocates the task's continuation list; only if the
        /// task completes while the release is being registered does it run on
        /// the thread pool, shortly after.
        /// </remarks>
        /// <typeparam name="T">The result of the operation.</typeparam>
        public ValueTask<T> ExitReaderOnCompletion<T>(ValueTask<T> operation)
        {
            if (operation.IsCompleted)
            {
                ReleaseReader();
                return operation;
            }
#if NET6_0_OR_GREATER
            return ExitReaderAfterAsync(operation);
#else
            Task<T> task = operation.AsTask();
            task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(m_releaseReader);
            return new ValueTask<T>(task);
#endif
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Awaits <paramref name="operation"/> and releases the reader lock.
        /// </summary>
        /// <typeparam name="T">The result of the operation.</typeparam>
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        private async ValueTask<T> ExitReaderAfterAsync<T>(ValueTask<T> operation)
        {
            try
            {
                return await operation.ConfigureAwait(false);
            }
            finally
            {
                ReleaseReader();
            }
        }
#endif

        /// <summary>
        /// Asynchronously acquires the writer lock. Excludes both
        /// other writers and all readers for the duration the lock
        /// is held. Dispose the returned <see cref="Releaser"/> to
        /// release.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public ValueTask<Releaser> WriterLockAsync(
            CancellationToken ct = default)
        {
            return WriterLockAsync(Timeout.InfiniteTimeSpan, null, static () => false, ct);
        }

        /// <summary>
        /// Asynchronously acquires the writer lock, checking every
        /// <paramref name="drainTimeout"/> while readers that are already
        /// in flight drain. When <paramref name="proceedWhileReadersActive"/>
        /// returns true at such a check, the writer proceeds while those
        /// readers are still active (new readers stay excluded); otherwise
        /// it stays exclusive and keeps waiting. Used by recovery, which
        /// fails the stale readers itself by replacing the dead channel
        /// they wait on, so waiting for them first would only delay it by
        /// their full request timeout. On a healthy channel the readers
        /// complete normally and must not overlap with the writer.
        /// </summary>
        /// <param name="drainTimeout">Interval at which
        /// <paramref name="proceedWhileReadersActive"/> is checked, or
        /// <see cref="Timeout.InfiniteTimeSpan"/> to always drain fully.</param>
        /// <param name="timeProvider">Time provider for the drain timeout.</param>
        /// <param name="proceedWhileReadersActive">Whether the writer may
        /// stop waiting for the readers still in flight.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<Releaser> WriterLockAsync(
            TimeSpan drainTimeout,
            TimeProvider? timeProvider,
            Func<bool> proceedWhileReadersActive,
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
                    while (!await WaitForDrainAsync(wait, drainTimeout, timeProvider, ct)
                        .ConfigureAwait(false))
                    {
                        if (proceedWhileReadersActive())
                        {
                            // Proceed holding the writer gate. The readers
                            // still in flight release on their own later.
                            lock (m_state)
                            {
                                m_drained = null;
                            }
                            break;
                        }
                    }
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
        /// Waits for the readers to drain. Returns false when
        /// <paramref name="drainTimeout"/> elapsed first.
        /// </summary>
        private static async Task<bool> WaitForDrainAsync(
            Task wait,
            TimeSpan drainTimeout,
            TimeProvider? timeProvider,
            CancellationToken ct)
        {
            if (drainTimeout == Timeout.InfiniteTimeSpan)
            {
                await wait.WaitAsync(ct).ConfigureAwait(false);
                return true;
            }
            using CancellationTokenSource drain =
                (timeProvider ?? TimeProvider.System).CreateCancellationTokenSource(drainTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, drain.Token);
            try
            {
                await wait.WaitAsync(linked.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (
                drain.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return wait.IsCompleted;
            }
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
        /// <see cref="WriterLockAsync(CancellationToken)"/>. Releases the corresponding
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

#if !NET6_0_OR_GREATER
        private readonly Action m_releaseReader;
#endif
        private bool m_isDisposed;
        private readonly SemaphoreSlim m_writer = new(1, 1);
        private readonly Lock m_state = new();
        private int m_activeReaders;
        private TaskCompletionSource<object?>? m_drained;
    }
}
