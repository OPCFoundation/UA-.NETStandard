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

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Keeps cancellation resources alive until every admitted operation, including gate waiters, has exited.
    /// </summary>
    internal sealed class GranulateDryingOperationLifetime : IAsyncDisposable
    {
        /// <summary>
        /// Creates the shared terminal drain operation.
        /// </summary>
        internal GranulateDryingOperationLifetime()
        {
            m_shutdown = new Lazy<Task>(DrainAsync);
        }

        /// <summary>
        /// Registers an operation before it waits at a runtime or device gate.
        /// </summary>
        internal Lease Enter(CancellationToken cancellationToken)
        {
            CancellationToken stopping;
            lock (m_sync)
            {
                if (m_closed)
                {
                    throw new ObjectDisposedException(nameof(GranulateDryingOperationLifetime));
                }
                m_active++;
                stopping = m_stopping.Token;
            }
            try
            {
                return new Lease(this, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping));
            }
            catch
            {
                Leave();
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await m_shutdown.Value.ConfigureAwait(false);
            }
            finally
            {
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                {
                    m_stopping.Dispose();
                }
            }
        }

        /// <summary>
        /// Closes admission and cancels before awaiting every admitted operation.
        /// </summary>
        private async Task DrainAsync()
        {
            Task drained;
            lock (m_sync)
            {
                m_closed = true;
                drained = m_active == 0 ? Task.CompletedTask : m_drained.Task;
            }
            try
            {
                m_stopping.Cancel();
            }
            finally
            {
                await drained.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Signals terminal drainage only after a caller releases its execution gate.
        /// </summary>
        private void Leave()
        {
            lock (m_sync)
            {
                m_active--;
                if (m_closed && m_active == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Represents one admitted asynchronous operation.
        /// </summary>
        internal sealed class Lease : IDisposable
        {
            /// <summary>
            /// Owns the caller's linked cancellation source until the operation finishes.
            /// </summary>
            internal Lease(GranulateDryingOperationLifetime owner, CancellationTokenSource linked)
            {
                m_owner = owner;
                m_linked = linked;
            }

            /// <summary>
            /// Gets cancellation linked to both the caller and the runtime lifetime.
            /// </summary>
            internal CancellationToken Token => m_linked.Token;

            /// <summary>
            /// Gets shutdown-only cancellation for publishing an operation that has already committed.
            /// </summary>
            internal CancellationToken StoppingToken => m_owner.m_stopping.Token;

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    m_linked.Dispose();
                    m_owner.Leave();
                }
            }

            /// <summary>
            /// The admission owner.
            /// </summary>
            private readonly GranulateDryingOperationLifetime m_owner;

            /// <summary>
            /// The operation-owned linked token source.
            /// </summary>
            private readonly CancellationTokenSource m_linked;

            /// <summary>
            /// Guards against repeated lease release.
            /// </summary>
            private int m_disposed;
        }

        /// <summary>
        /// Protects admission and drainage counters only.
        /// </summary>
        private readonly Lock m_sync = new();

        /// <summary>
        /// Cancels admitted operations during shutdown.
        /// </summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>
        /// Completes when every admitted caller leaves.
        /// </summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Shares one shutdown task between all disposers.
        /// </summary>
        private readonly Lazy<Task> m_shutdown;

        /// <summary>
        /// The active and queued operation count.
        /// </summary>
        private int m_active;

        /// <summary>
        /// Whether admission has closed.
        /// </summary>
        private bool m_closed;

        /// <summary>
        /// Whether the stopped source has been released.
        /// </summary>
        private int m_released;
    }
}
