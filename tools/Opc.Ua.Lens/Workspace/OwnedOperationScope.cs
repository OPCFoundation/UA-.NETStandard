// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UaLens.Workspace
{
    /// <summary>
    /// Cancels and drains document-owned calls before an adapter or key-provider owner is released.
    /// </summary>
    internal sealed class OwnedOperationScope : IAsyncDisposable
    {
        /// <summary>
        /// Creates a call scope without acquiring or owning any external provider.
        /// </summary>
        public OwnedOperationScope(string stoppedMessage)
        {
            m_stoppedMessage = stoppedMessage;
            m_token = m_lifetime.Token;
        }

        /// <summary>
        /// Gets whether the scope has stopped accepting calls.
        /// </summary>
        public bool IsStopped => Volatile.Read(ref m_stopped);

        /// <summary>
        /// Executes a call with linked cancellation and tracks it until its actual completion.
        /// </summary>
        public async ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> operation, CancellationToken cancellationToken)
        {
            BeginOperation();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_token);
                linked.Token.ThrowIfCancellationRequested();
                return await operation(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Executes a call without a result using the same cancellation and completion boundary.
        /// </summary>
        public async ValueTask ExecuteAsync(
            Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken)
        {
            BeginOperation();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_token);
                linked.Token.ThrowIfCancellationRequested();
                await operation(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_stopped = true;
                m_disposal ??= DrainAsync(m_drained.Task);
                return new ValueTask(m_disposal);
            }
        }

        private void BeginOperation()
        {
            lock (m_gate)
            {
                if (m_stopped)
                {
                    throw new OperationCanceledException(m_stoppedMessage, m_token);
                }
                if (m_operations++ == 0)
                {
                    m_drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        private void EndOperation()
        {
            lock (m_gate)
            {
                if (--m_operations == 0)
                {
                    m_drained.TrySetResult();
                }
            }
        }

        private async Task DrainAsync(Task drained)
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            try
            {
                await m_lifetime.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await drained.ConfigureAwait(false);
                }
                finally
                {
                    m_lifetime.Dispose();
                }
            }
        }

        private static TaskCompletionSource CreateDrained()
        {
            var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            result.SetResult();
            return result;
        }

        private readonly Lock m_gate = new();
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly CancellationToken m_token;
        private readonly string m_stoppedMessage;
        private TaskCompletionSource m_drained = CreateDrained();
        private Task? m_disposal;
        private int m_operations;
        private bool m_stopped;
    }
}
