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

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Counts active and queued calls before they access cancellation or execution gates.
    /// </summary>
    internal sealed class ExtrusionOperationLifetime(string owner) : IAsyncDisposable
    {
        /// <summary>
        /// Registers a call before it can queue, retaining cancellation until its final cleanup.
        /// </summary>
        internal Operation Begin(CancellationToken cancellationToken)
        {
            CancellationToken stopping;
            lock (m_sync)
            {
                ThrowIfStopped();
                m_requests++;
                stopping = m_stopping.Token;
            }
            try
            {
                return new Operation(this, cancellationToken, stopping);
            }
            catch
            {
                Complete();
                throw;
            }
        }

        /// <summary>
        /// Rejects work once terminal disposal has closed admission.
        /// </summary>
        internal void ThrowIfStopped()
        {
            if (Volatile.Read(ref m_stopped))
            {
                throw new ObjectDisposedException(owner);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref m_disposalStarted, 1, 0) == 0)
            {
                try
                {
                    Task drained;
                    lock (m_sync)
                    {
                        m_stopped = true;
                        drained = m_requests == 0 ? Task.CompletedTask : m_drained.Task;
                    }
                    try
                    {
                        try
                        {
                            m_stopping.Cancel();
                        }
                        finally
                        {
                            await drained.ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        m_stopping.Dispose();
                    }
                    m_disposed.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    m_disposed.TrySetException(failure);
                }
            }
            await m_disposed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Signals the drain only after a call has released its gates and cancellation registration.
        /// </summary>
        private void Complete()
        {
            lock (m_sync)
            {
                m_requests--;
                if (m_requests == 0 && m_stopped)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Owns one registered call, including time spent waiting for its execution gate.
        /// </summary>
        internal sealed class Operation : IDisposable
        {
            /// <summary>
            /// Links caller cancellation only after the owner has registered the call.
            /// </summary>
            internal Operation(
                ExtrusionOperationLifetime lifetime,
                CancellationToken cancellationToken,
                CancellationToken stopping)
            {
                m_lifetime = lifetime;
                m_pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);
                Token = m_pending.Token;
            }

            /// <summary>
            /// Gets cancellation from either the caller or its owner.
            /// </summary>
            internal CancellationToken Token { get; }

            /// <inheritdoc/>
            public void Dispose()
            {
                ExtrusionOperationLifetime? lifetime = Interlocked.Exchange(ref m_lifetime, null);
                if (lifetime != null)
                {
                    try
                    {
                        m_pending.Dispose();
                    }
                    finally
                    {
                        lifetime.Complete();
                    }
                }
            }

            /// <summary>The owner that must outlive this call's cleanup.</summary>
            private ExtrusionOperationLifetime? m_lifetime;

            /// <summary>The call's owned cancellation registration.</summary>
            private readonly CancellationTokenSource m_pending;
        }

        /// <summary>Protects admission and completion without holding a lock across user code.</summary>
        private readonly Lock m_sync = new();

        /// <summary>Cancels all registered calls before waiting for their cleanup.</summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>Completes after active and queued calls have all left.</summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Shares the entire terminal operation, including resource release.</summary>
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The number of registered calls, not just current gate holders.</summary>
        private int m_requests;

        /// <summary>Claims the single terminal operation.</summary>
        private int m_disposalStarted;

        /// <summary>Whether admission has closed.</summary>
        private bool m_stopped;
    }
}
