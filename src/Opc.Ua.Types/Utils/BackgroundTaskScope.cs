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
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Owns background work that a component starts but cannot await inline,
    /// so that the work is bounded, its failures are observed, and it is
    /// finished before the component that started it goes away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern this replaces is a bare <c>_ = Task.Run(...)</c>. That
    /// hands the work to the thread pool and immediately forgets it: nothing
    /// observes the exception if it throws, nothing bounds how many run at
    /// once, and — most damaging — disposal races it. A component can finish
    /// tearing itself down while work it started is still touching its
    /// fields, which surfaces as <see cref="ObjectDisposedException"/> from
    /// unrelated places, or as a test host that will not exit.
    /// </para>
    /// <para>
    /// <see cref="Run"/> never blocks and never throws, so it is safe to call
    /// from inside a lock — which is exactly why most of these call sites
    /// wanted a background task in the first place. <see cref="DisposeAsync"/>
    /// cancels <see cref="ShutdownToken"/> and then waits for the work still
    /// in flight, bounded by a drain timeout so one wedged operation cannot
    /// hang shutdown for ever.
    /// </para>
    /// </remarks>
    public sealed class BackgroundTaskScope : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Creates a scope.
        /// </summary>
        /// <param name="owner">Name of the component that owns the work, used
        /// in log messages to identify where a failure came from.</param>
        /// <param name="telemetry">Telemetry used to report work that
        /// faulted. Failures are swallowed when this is <c>null</c>.</param>
        /// <param name="maxConcurrency">Maximum number of scheduled
        /// operations allowed to run at once, or zero for no limit. Work over
        /// the limit waits asynchronously, so it occupies no thread.</param>
        /// <param name="drainTimeout">How long <see cref="DisposeAsync"/>
        /// waits for work in flight. Defaults to thirty seconds.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/>
        /// is negative.</exception>
        public BackgroundTaskScope(
            string owner,
            ITelemetryContext? telemetry = null,
            int maxConcurrency = 0,
            TimeSpan? drainTimeout = null)
        {
            if (maxConcurrency < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            }

            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            m_logger = telemetry?.CreateLogger<BackgroundTaskScope>();
            m_slots = maxConcurrency > 0
                ? new SemaphoreSlim(maxConcurrency, maxConcurrency)
                : null;
            m_drainTimeout = drainTimeout ?? TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Cancelled when the scope starts shutting down. Scheduled work
        /// receives this token and should stop promptly once it is signalled.
        /// </summary>
        public CancellationToken ShutdownToken => m_cts.Token;

        /// <summary>
        /// Number of scheduled operations that have not finished yet.
        /// </summary>
        public int PendingCount => Volatile.Read(ref m_pending);

        /// <summary>
        /// Schedules <paramref name="work"/> to run in the background.
        /// </summary>
        /// <param name="operation">Short name of the operation, used in log
        /// messages when it faults.</param>
        /// <param name="work">The work to run.</param>
        /// <returns><c>true</c> when the work was scheduled; <c>false</c>
        /// when the scope is already shutting down, in which case the work is
        /// not run at all.</returns>
        /// <remarks>
        /// Never blocks and never throws, so it is safe to call while holding
        /// a lock.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="work"/> is
        /// <c>null</c>.</exception>
        public bool Run(string operation, Func<CancellationToken, ValueTask> work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            if (Volatile.Read(ref m_shuttingDown) != 0)
            {
                return false;
            }

            Interlocked.Increment(ref m_pending);

            // Re-check after the increment. DisposeAsync sets the flag and then
            // reads the counter, so this ordering guarantees that either it sees
            // this operation and waits for it, or this sees the shutdown and
            // stands down. Without the re-check an operation could be scheduled
            // after the drain had already decided the scope was empty.
            if (Volatile.Read(ref m_shuttingDown) != 0)
            {
                CompleteOne();
                return false;
            }

            _ = Task.Run(() => RunCoreAsync(operation, work), CancellationToken.None);
            return true;
        }

        /// <summary>
        /// Signals shutdown without waiting for the work in flight.
        /// </summary>
        /// <remarks>
        /// For owners whose teardown is synchronous and therefore cannot await
        /// the drain — blocking on it would be sync over async. Scheduled work
        /// is cancelled and no further work is accepted, but this returns
        /// immediately and work already running may still be in flight when it
        /// does. Prefer <see cref="DisposeAsync"/> wherever the owner has an
        /// asynchronous teardown to hang the drain on.
        /// </remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_shuttingDown, 1) != 0)
            {
                return;
            }

            CancelShutdownToken();

            if (Volatile.Read(ref m_pending) == 0)
            {
                m_drained.TrySetResult(true);
            }
        }

        /// <summary>
        /// Signals shutdown and waits for the work still in flight.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_shuttingDown, 1) == 0)
            {
                CancelShutdownToken();

                if (Volatile.Read(ref m_pending) == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }

            try
            {
                await m_drained.Task.WaitAsync(m_drainTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                m_logger?.BackgroundTaskDrainTimedOut(
                    m_owner,
                    Volatile.Read(ref m_pending),
                    m_drainTimeout.TotalSeconds);
            }

            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_cts.Dispose();
                m_slots?.Dispose();
            }
        }

        private void CancelShutdownToken()
        {
            try
            {
                m_cts.Cancel();
            }
            catch (AggregateException ex)
            {
                // A callback registered on the token threw. It is not this
                // scope's failure to propagate, but it must not prevent the drain.
                m_logger?.BackgroundTaskCancellationFailed(ex, m_owner);
            }
        }

        private async Task RunCoreAsync(string operation, Func<CancellationToken, ValueTask> work)
        {
            try
            {
                CancellationToken ct = m_cts.Token;
                if (m_slots != null)
                {
                    await m_slots.WaitAsync(ct).ConfigureAwait(false);
                }

                try
                {
                    await work(ct).ConfigureAwait(false);
                }
                finally
                {
                    m_slots?.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown, or the work honoured the token. Either way expected.
            }
            catch (ObjectDisposedException)
            {
                // The owner was torn down under the work. Expected during shutdown.
            }
            catch (Exception ex)
            {
                m_logger?.BackgroundTaskFailed(ex, m_owner, operation);
            }
            finally
            {
                CompleteOne();
            }
        }

        private void CompleteOne()
        {
            if (Interlocked.Decrement(ref m_pending) == 0 &&
                Volatile.Read(ref m_shuttingDown) != 0)
            {
                m_drained.TrySetResult(true);
            }
        }

        private readonly string m_owner;
        private readonly ILogger? m_logger;
        private readonly SemaphoreSlim? m_slots;
        private readonly TimeSpan m_drainTimeout;
        private readonly CancellationTokenSource m_cts = new();
        private readonly TaskCompletionSource<bool> m_drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_pending;
        private int m_shuttingDown;
        private int m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="BackgroundTaskScope"/>.
    /// </summary>
    internal static partial class BackgroundTaskScopeLog
    {
        [LoggerMessage(EventId = TypesEventIds.BackgroundTaskScope + 0, Level = LogLevel.Error,
            Message = "Background operation {Operation} started by {Owner} failed.")]
        public static partial void BackgroundTaskFailed(
            this ILogger logger, Exception exception, string owner, string operation);

        [LoggerMessage(EventId = TypesEventIds.BackgroundTaskScope + 1, Level = LogLevel.Warning,
            Message = "Background work started by {Owner} did not drain within {TimeoutSeconds}s; " +
                "{Pending} operation(s) still in flight.")]
        public static partial void BackgroundTaskDrainTimedOut(
            this ILogger logger, string owner, int pending, double timeoutSeconds);

        [LoggerMessage(EventId = TypesEventIds.BackgroundTaskScope + 2, Level = LogLevel.Warning,
            Message = "A cancellation callback threw while shutting down background work of {Owner}.")]
        public static partial void BackgroundTaskCancellationFailed(
            this ILogger logger, Exception exception, string owner);
    }
}
