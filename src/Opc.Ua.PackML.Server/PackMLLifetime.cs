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
        /// Admits an operation unless shutdown has already started.
        /// </summary>
        public async ValueTask<Lease> EnterAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource pending;
            lock (m_sync)
            {
                if (m_closed)
                {
                    throw new ObjectDisposedException(nameof(PackMLLifetime));
                }
                pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_stopping.Token);
                m_active++;
            }
            try
            {
                await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
                return new Lease(this, pending);
            }
            catch
            {
                pending.Dispose();
                Exit();
                throw;
            }
        }

        /// <summary>
        /// Closes admission and joins shutdown for concurrent disposal callers.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            bool first;
            lock (m_sync)
            {
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
                    m_gate.Dispose();
                    m_stopping.Dispose();
                }
                m_disposed.TrySetResult(true);
            }
            catch (Exception exception)
            {
                m_disposed.TrySetException(exception);
                throw;
            }
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
        /// Owns one serialized operation and its linked cancellation source.
        /// </summary>
        internal sealed class Lease : IDisposable
        {
            /// <summary>
            /// Captures the admitted owner and linked token.
            /// </summary>
            public Lease(PackMLLifetime owner, CancellationTokenSource pending)
            {
                m_owner = owner;
                m_pending = pending;
            }

            /// <summary>
            /// Gets the caller/shutdown cancellation token.
            /// </summary>
            public CancellationToken Token => m_pending.Token;

            /// <summary>
            /// Releases admission only after all operation-local cleanup.
            /// </summary>
            public void Dispose()
            {
                PackMLLifetime? owner = Interlocked.Exchange(ref m_owner, null);
                if (owner is not null)
                {
                    m_pending.Dispose();
                    owner.m_gate.Release();
                    owner.Exit();
                }
            }

            /// <summary>The admission owner, cleared on the first release.</summary>
            private PackMLLifetime? m_owner;

            /// <summary>The linked caller and shutdown cancellation source.</summary>
            private readonly CancellationTokenSource m_pending;
        }

        /// <summary>Protects admission counts and shutdown flags.</summary>
        private readonly System.Threading.Lock m_sync = new();

        /// <summary>Serializes admitted operations.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

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
}
