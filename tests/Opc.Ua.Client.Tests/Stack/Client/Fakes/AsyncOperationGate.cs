// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Coordinates a scripted asynchronous phase with one-shot signals for entry, cancellation, and exit
    /// and an explicit release that can be held beyond cancellation.
    /// </summary>
    internal sealed class AsyncOperationGate
    {
        /// <summary>
        /// Gets a task completed when the first wait enters the gate.
        /// </summary>
        public Task Entered => m_entered.Task;

        /// <summary>
        /// Gets a task completed when a waiting operation observes its token being canceled,
        /// even if cancellation is configured not to end the wait.
        /// </summary>
        public Task Cancelled => m_cancelled.Task;

        /// <summary>
        /// Gets a task completed when the first wait exits, whether released or canceled.
        /// </summary>
        public Task Exited => m_exited.Task;

        /// <summary>
        /// Gets whether the gate has been explicitly released, independently of cancellation or exit.
        /// </summary>
        public bool IsReleased => m_release.Task.IsCompleted;

        /// <summary>
        /// Gets or sets whether newly started waits remain blocked until release despite cancellation of their token.
        /// Cancellation is still reported through <see cref="Cancelled"/>.
        /// </summary>
        public bool IgnoreCancellation { get; set; }

        /// <summary>
        /// Signals entry and waits for release, allowing cancellation to end the wait unless it is ignored.
        /// Signals exit when the wait finishes.
        /// </summary>
        /// <param name="ct">The token whose cancellation is observed and optionally ends the wait.</param>
        public async ValueTask WaitAsync(CancellationToken ct)
        {
            m_entered.TrySetResult(true);
            using CancellationTokenRegistration registration = ct.Register(() => m_cancelled.TrySetResult(true));
            try
            {
                await m_release.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : ct).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                m_cancelled.TrySetResult(true);
                throw;
            }
            finally
            {
                m_exited.TrySetResult(true);
            }
        }

        /// <summary>
        /// Permanently opens the gate so current and subsequent waits can complete.
        /// </summary>
        public void Release()
        {
            m_release.TrySetResult(true);
        }

        private readonly TaskCompletionSource<bool> m_entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_cancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
