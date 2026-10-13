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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsClient
    {
        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (m_gate)
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
        /// Cancels native reads and shared subscriptions before releasing an explicitly factory-owned session.
        /// </summary>
        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            var failures = new List<Exception>();
            try
            {
                m_shutdown.Cancel();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            await m_drained.Task.ConfigureAwait(false);
            try
            {
                await m_inherited.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            if (m_ownsSession)
            {
                try
                {
                    await Session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
                try
                {
                    if (Session is IAsyncDisposable asynchronous)
                    {
                        await asynchronous.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        Session.Dispose();
                    }
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            m_shutdown.Dispose();
            if (failures.Count == 0)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(new AggregateException("GMS client cleanup failed.", failures));
            }
        }

        /// <summary>
        /// Admits a complete native workflow atomically with respect to facade disposal.
        /// </summary>
        private Operation Begin(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                EnsureActive();
                var operation = new Operation(this, cancellationToken, m_shutdown.Token);
                m_active++;
                return operation;
            }
        }

        /// <summary>
        /// Rejects new service workflows after facade disposal without closing a caller-owned session.
        /// </summary>
        private void EnsureActive()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(GmsClient));
            }
        }

        /// <summary>The short workflow-admission gate.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Cancellation shared by this facade's admitted native operations.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>Completion of native service and subscription cleanup.</summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The idempotent disposal completion.</summary>
        private Task? m_disposal;

        /// <summary>The current admitted workflow count.</summary>
        private int m_active;

        /// <summary>Whether new workflows are refused.</summary>
        private volatile bool m_disposed;

        /// <summary>
        /// Releases workflow admission only after its continuation points and shared subscriptions have drained.
        /// </summary>
        private sealed class Operation : IDisposable
        {
            /// <summary>
            /// Owns cancellation for one already admitted native workflow.
            /// </summary>
            internal Operation(GmsClient owner, CancellationToken caller, CancellationToken lifetime)
            {
                m_owner = owner;
                m_cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime);
            }

            /// <summary>
            /// Gets this operation's linked lifetime token.
            /// </summary>
            internal CancellationToken Token => m_cancellation.Token;

            /// <inheritdoc/>
            public void Dispose()
            {
                m_cancellation.Dispose();
                lock (m_owner.m_gate)
                {
                    if (--m_owner.m_active == 0 && m_owner.m_disposed)
                    {
                        m_owner.m_drained.TrySetResult(true);
                    }
                }
            }

            /// <summary>The workflow owner whose admission is held.</summary>
            private readonly GmsClient m_owner;

            /// <summary>The linked source owned by this admission.</summary>
            private readonly CancellationTokenSource m_cancellation;
        }
    }
}
