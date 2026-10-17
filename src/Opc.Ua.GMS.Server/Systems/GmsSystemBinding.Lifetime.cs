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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Rejects a callback's attempt to dispose or reenter its containing facade before any cleanup begins.
        /// </summary>
        internal void EnsureNotInCallback() => RejectReentry();

        /// <summary>
        /// Serializes the composite native/inherited acquisition, including pending native event publication.
        /// Shared inventory registration, cancellation and rollback remain in the MachineTool runtime.
        /// </summary>
        internal async ValueTask RefreshAsync(
            MachineToolRuntime<GMSState> runtime, CancellationToken cancellationToken, bool retry = false)
        {
            RejectReentry();
            CancellationTokenSource operation;
            lock (m_lifetimeGate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(GmsHandle));
                }
                operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
                m_active++;
            }
            bool entered = false;
            GmsSystemBinding? previous = s_callback.Value;
            try
            {
                await m_update.WaitAsync(operation.Token).ConfigureAwait(false);
                entered = true;
                s_callback.Value = this;
                await PublishWarningsAsync(operation.Token).ConfigureAwait(false);
                if (retry)
                {
                    await runtime.RetryPublicationAsync(operation.Token).ConfigureAwait(false);
                }
                else
                {
                    await runtime.RefreshAsync(operation.Token).ConfigureAwait(false);
                }
                await PublishWarningsAsync(operation.Token).ConfigureAwait(false);
            }
            finally
            {
                s_callback.Value = previous;
                if (entered)
                {
                    m_update.Release();
                }
                operation.Dispose();
                lock (m_lifetimeGate)
                {
                    if (--m_active == 0 && m_disposed)
                    {
                        m_drained.TrySetResult(true);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            RejectReentry();
            TaskCompletionSource<bool> completion;
            lock (m_lifetimeGate)
            {
                if (m_disposal != null)
                {
                    return new ValueTask(m_disposal);
                }
                m_disposed = true;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = completion.Task;
                if (m_active == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            _ = CompleteDisposalAsync(completion);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Cancels and drains only this instance's composite facade operations.
        /// </summary>
        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            Exception? failure = null;
            try
            {
                m_shutdown.Cancel();
            }
            catch (Exception error)
            {
                failure = error;
            }
            await m_drained.Task.ConfigureAwait(false);
            m_shutdown.Dispose();
            m_update.Dispose();
            m_committedSensors = new(StringComparer.Ordinal);
            if (failure == null)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(failure);
            }
        }

        /// <summary>
        /// Prevents a provider or native event callback from awaiting its own update or disposal.
        /// </summary>
        private void RejectReentry()
        {
            if (ReferenceEquals(s_callback.Value, this))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "A GMS observation callback cannot reenter its own update or disposal.");
            }
        }

        /// <summary>Serializes complete facade acquisitions, never exposed to callers.</summary>
        private readonly SemaphoreSlim m_update = new(1, 1);

        /// <summary>Protects only short operation-admission decisions.</summary>
        private readonly Lock m_lifetimeGate = new();

        /// <summary>Signals removal of this root to composite operations.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>Joins admitted callbacks before node-scoped resource release completes.</summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The idempotent completion shared by all disposal callers.</summary>
        private Task? m_disposal;

        /// <summary>The number of admitted composite operations, including queued updates.</summary>
        private int m_active;

        /// <summary>Whether the binding refuses further observations.</summary>
        private bool m_disposed;

        /// <summary>Detects asynchronous provider and publication reentry without exposing synchronization.</summary>
        private static readonly AsyncLocal<GmsSystemBinding?> s_callback = new();
    }
}
