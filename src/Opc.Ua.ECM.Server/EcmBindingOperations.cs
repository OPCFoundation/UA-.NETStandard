/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Serializes provider callbacks and joins admitted work before releasing binding resources.
    /// </summary>
    internal sealed class EcmBindingOperations : IAsyncDisposable
    {
        /// <summary>
        /// Creates a callback lifetime with a retained shutdown token.
        /// </summary>
        public EcmBindingOperations()
        {
            m_stoppingToken = m_stopping.Token;
        }

        /// <summary>
        /// Gets or sets a test hook that runs after admission closes and before shutdown requests cancellation.
        /// </summary>
        internal Func<ValueTask>? BeforeStopCancellation { get; set; }

        /// <summary>
        /// Gets the token that shutdown cancels, so tests can observe cancellation ordering.
        /// </summary>
        internal CancellationToken StoppingToken => m_stoppingToken;

        /// <summary>
        /// Drains operations and releases resources when no graph callbacks need removal.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return StopAsync(static () => { });
        }

        /// <summary>
        /// Executes the entire callback within admission, including time queued for serialization.
        /// </summary>
        /// <typeparam name="T">The provider callback's result type.</typeparam>
        public async ValueTask<T> ExecuteAsync<T>(
            Func<CancellationToken, ValueTask<T>> callback,
            CancellationToken cancellationToken)
        {
            lock (m_lifetimeGate)
            {
                if (m_closing)
                {
                    throw new ObjectDisposedException(nameof(EcmBindingOperations));
                }
                m_operationCount++;
            }
            try
            {
                using var pending = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_stoppingToken);
                await m_commands.WaitAsync(pending.Token).ConfigureAwait(false);
                try
                {
                    bool closedWhileQueued;
                    lock (m_lifetimeGate)
                    {
                        // Release can grant the slot after admission closed but before shutdown cancels the
                        // queued waiter's linked token, so queued work rechecks admission after acquisition.
                        closedWhileQueued = m_closing;
                    }
                    if (closedWhileQueued)
                    {
                        throw new OperationCanceledException(m_stoppingToken);
                    }
                    pending.Token.ThrowIfCancellationRequested();
                    return await callback(pending.Token).ConfigureAwait(false);
                }
                finally
                {
                    m_commands.Release();
                }
            }
            finally
            {
                lock (m_lifetimeGate)
                {
                    m_operationCount--;
                    if (m_closing && m_operationCount == 0)
                    {
                        m_operationsDrained.TrySetResult(true);
                    }
                }
            }
        }

        /// <summary>
        /// Closes admission and removes callbacks only after every admitted operation completes.
        /// </summary>
        public ValueTask StopAsync(Action removeCallbacks)
        {
            TaskCompletionSource<bool>? completion = null;
            Task disposal;
            lock (m_lifetimeGate)
            {
                m_closing = true;
                if (m_operationCount == 0)
                {
                    m_operationsDrained.TrySetResult(true);
                }
                if (m_disposal == null)
                {
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposal = completion.Task;
                }
                disposal = m_disposal;
            }
            if (completion != null)
            {
                _ = CompleteStopAsync(removeCallbacks, completion);
            }
            return new ValueTask(disposal);
        }

        /// <summary>
        /// Completes the shared shutdown task without running provider cancellation callbacks under admission's lock.
        /// </summary>
        private async Task CompleteStopAsync(Action removeCallbacks, TaskCompletionSource<bool> completion)
        {
            try
            {
                await StopCoreAsync(removeCallbacks).ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        /// <summary>
        /// Cancels pending callbacks, drains work and releases synchronization resources.
        /// </summary>
        private async Task StopCoreAsync(Action removeCallbacks)
        {
            try
            {
                if (BeforeStopCancellation is { } beforeStopCancellation)
                {
                    await beforeStopCancellation().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    m_stopping.Cancel();
                }
                finally
                {
                    await m_operationsDrained.Task.ConfigureAwait(false);
                    try
                    {
                        removeCallbacks();
                    }
                    finally
                    {
                        m_commands.Dispose();
                        m_stopping.Dispose();
                    }
                }
            }
        }

        /// <summary>Serializes callbacks.</summary>
        private readonly SemaphoreSlim m_commands = new(1, 1);

        /// <summary>Cancels work when admission closes.</summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>The retained shutdown token.</summary>
        private readonly CancellationToken m_stoppingToken;

        /// <summary>Protects admission and disposal state.</summary>
        private readonly System.Threading.Lock m_lifetimeGate = new();

        /// <summary>Completes when active and queued callbacks have drained.</summary>
        private readonly TaskCompletionSource<bool> m_operationsDrained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The shared disposal task.</summary>
        private Task? m_disposal;

        /// <summary>The number of admitted callbacks.</summary>
        private int m_operationCount;

        /// <summary>Whether shutdown has closed admission.</summary>
        private bool m_closing;
    }
}
