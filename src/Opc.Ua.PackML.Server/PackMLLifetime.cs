/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// Serializes admitted work and drains both active and queued operations
    /// before releasing shutdown resources.
    /// </summary>
    internal sealed class PackMLLifetime : IAsyncDisposable
    {
        /// <summary>
        /// Executes an operation without exposing admission or serialization ownership.
        /// </summary>
        /// <typeparam name="T">The operation result type.</typeparam>
        public async ValueTask<T> RunAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken)
        {
            if (operation is null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            RejectReentry();
            CancellationTokenSource pending;
            Task predecessor;
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (m_sync)
            {
                if (m_closed)
                {
                    throw new ObjectDisposedException(nameof(PackMLLifetime));
                }
                if (m_providerActive > 0)
                {
                    throw new PackMLBusyException();
                }
                pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_stopping.Token);
                predecessor = m_tail;
                m_tail = completed.Task;
                m_active++;
            }
            var execution = new Execution();
            try
            {
                await predecessor.ConfigureAwait(false);
                pending.Token.ThrowIfCancellationRequested();
                m_execution.Value = execution;
                return await operation(pending.Token).ConfigureAwait(false);
            }
            finally
            {
                execution.Active = false;
                m_execution.Value = null;
                pending.Dispose();
                completed.TrySetResult(true);
                Exit();
            }
        }

        /// <summary>
        /// Executes an operation with no result using the same lifecycle.
        /// </summary>
        public async ValueTask RunAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken)
        {
            if (operation is null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            await RunAsync(async token =>
            {
                await operation(token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Drains the owner and attempts its cleanup without accepting callback reentry.
        /// </summary>
        public async ValueTask StopAsync(Func<ValueTask> cleanup)
        {
            if (cleanup is null)
            {
                throw new ArgumentNullException(nameof(cleanup));
            }
            RejectReentry();
            await CompleteShutdownAsync(ShutdownAsync, cleanup).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes admission and joins shutdown for concurrent disposal callers.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return DisposeCoreAsync(false);
        }

        /// <summary>
        /// Cancels and drains work on behalf of the hosting owner.
        /// </summary>
        internal ValueTask ShutdownAsync()
        {
            return DisposeCoreAsync(true);
        }

        /// <summary>
        /// Invokes application provider code without holding a synchronization primitive.
        /// </summary>
        /// <typeparam name="T">The provider result type.</typeparam>
        internal async ValueTask<T> CallProviderAsync<T>(Func<ValueTask<T>> callback)
        {
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            lock (m_sync)
            {
                m_providerActive++;
            }
            try
            {
                return await callback().ConfigureAwait(false);
            }
            finally
            {
                lock (m_sync)
                {
                    m_providerActive--;
                }
            }
        }

        /// <summary>
        /// Attempts owner cleanup after shutdown and retains errors from both phases.
        /// </summary>
        internal static async ValueTask CompleteShutdownAsync(Func<ValueTask> shutdown, Func<ValueTask> cleanup)
        {
            if (shutdown is null)
            {
                throw new ArgumentNullException(nameof(shutdown));
            }
            if (cleanup is null)
            {
                throw new ArgumentNullException(nameof(cleanup));
            }
            Exception? stopError = null;
            try
            {
                await shutdown().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                stopError = exception;
            }
            try
            {
                await cleanup().ConfigureAwait(false);
            }
            catch (Exception cleanupError) when (stopError is not null)
            {
                throw new AggregateException("PackML shutdown and cleanup both failed.", stopError, cleanupError);
            }
            if (stopError is not null)
            {
                ExceptionDispatchInfo.Capture(stopError).Throw();
            }
        }

        /// <summary>
        /// Applies the distinction between direct disposal and host-owned shutdown.
        /// </summary>
        private async ValueTask DisposeCoreAsync(bool hostOwned)
        {
            RejectReentry();
            bool first;
            lock (m_sync)
            {
                if (!hostOwned && m_providerActive > 0)
                {
                    throw new InvalidOperationException(
                        "Direct PackML disposal is unavailable while an application callback is active.");
                }
                first = !m_closed;
                m_closed = true;
                if (m_active == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            if (!first)
            {
                await m_disposed.Task.ConfigureAwait(false);
                return;
            }
            try
            {
                try
                {
                    await m_stopping.CancelAsync().ConfigureAwait(false);
                }
                finally
                {
                    await m_drained.Task.ConfigureAwait(false);
                    m_stopping.Dispose();
                }
                m_disposed.TrySetResult(true);
            }
            catch (Exception exception)
            {
                m_disposed.TrySetException(exception);
            }
            await m_disposed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Completes admission after release or cancellation.
        /// </summary>
        private void Exit()
        {
            lock (m_sync)
            {
                if (--m_active == 0 && m_closed)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Rejects calls from an operation's asynchronous callback chain before admission.
        /// </summary>
        internal void RejectReentry()
        {
            if (m_execution.Value is { Active: true })
            {
                throw new InvalidOperationException("A PackML callback cannot re-enter its owning unit or runtime.");
            }
        }

        /// <summary>
        /// Tracks whether an inherited callback context still belongs to active work.
        /// </summary>
        private sealed class Execution
        {
            /// <summary>
            /// Whether the originating operation is still executing.
            /// </summary>
            public volatile bool Active = true;
        }

        /// <summary>The current operation in this asynchronous execution context.</summary>
        private readonly AsyncLocal<Execution?> m_execution = new();

        /// <summary>Protects admission counts and shutdown flags.</summary>
        private readonly System.Threading.Lock m_sync = new();

        /// <summary>The completion of the last admitted operation.</summary>
        private Task m_tail = Task.CompletedTask;

        /// <summary>The number of equipment or persistence callbacks currently executing.</summary>
        private int m_providerActive;

        /// <summary>Requests cancellation of active and queued operations.</summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>Completes when every admitted operation has exited.</summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Shares the shutdown outcome with concurrent disposal callers.</summary>
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Whether shutdown has closed admission.</summary>
        private bool m_closed;

        /// <summary>The number of active and queued operations.</summary>
        private int m_active;
    }

    /// <summary>
    /// Reports an operation that cannot enter while application provider code is outstanding.
    /// </summary>
    internal sealed class PackMLBusyException : InvalidOperationException
    {
        /// <summary>
        /// Creates the documented per-unit busy error.
        /// </summary>
        public PackMLBusyException()
            : base("The PackML unit has an application callback outstanding; retry after that operation finishes.")
        {
        }

        /// <summary>
        /// Creates a busy error with the supplied explanation.
        /// </summary>
        public PackMLBusyException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a busy error retaining its original cause.
        /// </summary>
        public PackMLBusyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
