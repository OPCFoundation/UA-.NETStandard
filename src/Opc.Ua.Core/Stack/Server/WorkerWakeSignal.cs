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
using System.Threading.Tasks.Sources;

namespace Opc.Ua
{
    /// <summary>
    /// A single coalesced wake token for the workers of a <see cref="FairRequestQueue"/>,
    /// handed to the most recently parked worker.
    /// </summary>
    /// <remarks>
    /// At most one token is outstanding. <see cref="TryIssue"/> creates it when none is, and
    /// hands it to the top of a stack of parked waiters, or leaves it for the next waiter,
    /// which then does not park. While a woken waiter has not consumed its token yet, further
    /// issues are coalesced: a waiter that runs again looks at the queue anyway, so no more
    /// than one worker is woken per change. The stack is last in, first out, so the workers
    /// that wake are the ones that parked last; the others stay parked while they are not
    /// needed. Each waiter is reused for every wait of its worker, so parking allocates
    /// nothing, and a woken waiter continues on the thread pool, never on the thread that
    /// issued the token.
    /// </remarks>
    internal sealed class WorkerWakeSignal
    {
        /// <summary>
        /// Whether a token is outstanding, issued or already handed to a waiter.
        /// </summary>
        public bool IsPending => Volatile.Read(ref m_token) != kNone;

        /// <summary>
        /// Issues the token unless one is outstanding and wakes the most recently parked
        /// waiter with it.
        /// </summary>
        /// <returns>True when this call issued the token.</returns>
        public bool TryIssue()
        {
            if (Volatile.Read(ref m_token) != kNone ||
                Interlocked.CompareExchange(ref m_token, kIssued, kNone) != kNone)
            {
                return false;
            }
            Waiter? waiter = null;
            lock (m_gate)
            {
                // A waiter that parked meanwhile may already have taken the token.
                if (m_token == kIssued && m_top != null)
                {
                    waiter = m_top;
                    Remove(waiter);
                    waiter.State = Waiter.Woken;
                    Volatile.Write(ref m_token, kAssigned);
                }
            }
            waiter?.Wake();
            return true;
        }

        /// <summary>
        /// Waits for the token. Completes at once when a token is issued and not handed to
        /// another waiter yet, and faults with <see cref="OperationCanceledException"/> once
        /// the signal is closed or <paramref name="cancellationToken"/> is cancelled while
        /// parked.
        /// </summary>
        /// <remarks>
        /// A waiter waits once at a time. Awaiting the result consumes the token, so the next
        /// change issues a new one.
        /// </remarks>
        public ValueTask WaitAsync(Waiter waiter, CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                if (m_closed)
                {
                    return new ValueTask(Task.FromCanceled(
                        cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true)));
                }
                if (m_token == kIssued)
                {
                    // the token was issued while nobody was parked
                    Volatile.Write(ref m_token, kNone);
                    return default;
                }
                waiter.Prepare();
                waiter.State = Waiter.Parked;
                waiter.Next = m_top;
                if (m_top != null)
                {
                    m_top.Previous = waiter;
                }
                m_top = waiter;
                Volatile.Write(ref m_parkedCount, m_parkedCount + 1);
            }
            if (cancellationToken.CanBeCanceled)
            {
                waiter.RegisterCancellation(cancellationToken);
            }
            return new ValueTask(waiter, waiter.Version);
        }

        /// <summary>
        /// Gets the number of parked waiters, as a snapshot.
        /// </summary>
        public int ParkedCount => Volatile.Read(ref m_parkedCount);

        /// <summary>
        /// Faults every parked waiter and every later wait with
        /// <see cref="OperationCanceledException"/>.
        /// </summary>
        public void Close()
        {
            Waiter? parked;
            lock (m_gate)
            {
                if (m_closed)
                {
                    return;
                }
                m_closed = true;
                parked = m_top;
                m_top = null;
                for (Waiter? waiter = parked; waiter != null; waiter = waiter.Next)
                {
                    waiter.State = Waiter.Cancelled;
                }
                Volatile.Write(ref m_parkedCount, 0);
            }
            while (parked != null)
            {
                Waiter next = parked.Next!;
                parked.Next = null;
                parked.Previous = null;
                parked.Cancel(default);
                parked = next;
            }
        }

        /// <summary>
        /// Takes a parked waiter off the stack after its wait was cancelled.
        /// </summary>
        private void Cancel(Waiter waiter, CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                if (waiter.State != Waiter.Parked)
                {
                    // woken or closed first: the wake completes the wait
                    return;
                }
                Remove(waiter);
                waiter.State = Waiter.Cancelled;
            }
            waiter.Cancel(cancellationToken);
        }

        /// <summary>
        /// Consumes the token handed to a woken waiter.
        /// </summary>
        private void Consume()
        {
            Volatile.Write(ref m_token, kNone);
        }

        /// <summary>
        /// Unlinks a parked waiter; called under the gate.
        /// </summary>
        private void Remove(Waiter waiter)
        {
            if (waiter.Previous != null)
            {
                waiter.Previous.Next = waiter.Next;
            }
            else
            {
                m_top = waiter.Next;
            }
            if (waiter.Next != null)
            {
                waiter.Next.Previous = waiter.Previous;
            }
            waiter.Next = null;
            waiter.Previous = null;
            Volatile.Write(ref m_parkedCount, m_parkedCount - 1);
        }

        /// <summary>
        /// The reusable wait of one worker on a <see cref="WorkerWakeSignal"/>.
        /// </summary>
        internal sealed class Waiter : IValueTaskSource
#if NETCOREAPP3_0_OR_GREATER
            , IThreadPoolWorkItem
#endif
        {
            /// <summary>
            /// Creates a waiter for one worker of <paramref name="signal"/>.
            /// </summary>
            public Waiter(WorkerWakeSignal signal)
            {
                m_signal = signal ?? throw new ArgumentNullException(nameof(signal));
            }

            /// <summary>
            /// Not waiting.
            /// </summary>
            internal const int Idle = 0;

            /// <summary>
            /// On the signal's stack.
            /// </summary>
            internal const int Parked = 1;

            /// <summary>
            /// Handed the token.
            /// </summary>
            internal const int Woken = 2;

            /// <summary>
            /// Cancelled or closed while parked.
            /// </summary>
            internal const int Cancelled = 3;

            /// <summary>
            /// The wait state, changed under the signal's gate.
            /// </summary>
            internal int State;

            /// <summary>
            /// The waiter parked before this one.
            /// </summary>
            internal Waiter? Next;

            /// <summary>
            /// The waiter parked after this one.
            /// </summary>
            internal Waiter? Previous;

            /// <summary>
            /// The version of the current wait.
            /// </summary>
            internal short Version => m_core.Version;

            /// <summary>
            /// Starts a new wait.
            /// </summary>
            internal void Prepare()
            {
                m_core.Reset();
            }

            /// <summary>
            /// Ends a parked wait with cancellation unless the wake came first.
            /// </summary>
            internal void RegisterCancellation(CancellationToken cancellationToken)
            {
                m_cancellationToken = cancellationToken;
#if NETCOREAPP3_0_OR_GREATER
                m_registration = cancellationToken.UnsafeRegister(
                    static state => ((Waiter)state!).OnCancelled(), this);
#else
                m_registration = cancellationToken.Register(
                    static state => ((Waiter)state!).OnCancelled(), this);
#endif
            }

            /// <summary>
            /// Completes the wait with the token on a thread-pool thread.
            /// </summary>
            internal void Wake()
            {
                m_cancelled = false;
                Schedule();
            }

            /// <summary>
            /// Completes the wait with cancellation on a thread-pool thread.
            /// </summary>
            internal void Cancel(CancellationToken cancellationToken)
            {
                m_cancelledBy = cancellationToken;
                m_cancelled = true;
                Schedule();
            }

            /// <summary>
            /// Runs the continuation of a completed wait.
            /// </summary>
            public void Execute()
            {
                if (m_cancelled)
                {
                    CancellationToken cancelledBy = m_cancelledBy;
                    m_cancelledBy = default;
                    m_core.SetException(new OperationCanceledException(cancelledBy));
                }
                else
                {
                    m_core.SetResult(true);
                }
            }

            /// <summary>
            /// Queues the completion, so that the continuation never runs on the thread that
            /// woke or cancelled the wait.
            /// </summary>
            private void Schedule()
            {
#if NETCOREAPP3_0_OR_GREATER
                ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
#else
                ThreadPool.UnsafeQueueUserWorkItem(static state => ((Waiter)state!).Execute(), this);
#endif
            }

            /// <inheritdoc/>
            public ValueTaskSourceStatus GetStatus(short token)
            {
                return m_core.GetStatus(token);
            }

            /// <inheritdoc/>
            public void OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags)
            {
                m_core.OnCompleted(continuation, state, token, flags);
            }

            /// <inheritdoc/>
            public void GetResult(short token)
            {
                // A cancellation that raced the wake has nothing left to do once disposed.
                m_registration.Dispose();
                m_registration = default;
                m_cancellationToken = default;
                bool woken = State == Woken;
                State = Idle;
                if (woken)
                {
                    m_signal.Consume();
                }
                m_core.GetResult(token);
            }

            /// <summary>
            /// Called when the token of a parked wait is cancelled.
            /// </summary>
            private void OnCancelled()
            {
                m_signal.Cancel(this, m_cancellationToken);
            }

            private readonly WorkerWakeSignal m_signal;
            private ManualResetValueTaskSourceCore<bool> m_core;
            private CancellationTokenRegistration m_registration;
            private CancellationToken m_cancellationToken;
            private CancellationToken m_cancelledBy;
            private bool m_cancelled;
        }

        /// <summary>
        /// No token is outstanding.
        /// </summary>
        private const int kNone = 0;

        /// <summary>
        /// The token is issued and waits for the next waiter.
        /// </summary>
        private const int kIssued = 1;

        /// <summary>
        /// The token is handed to a woken waiter that has not consumed it yet.
        /// </summary>
        private const int kAssigned = 2;

        /// <summary>
        /// Protects the stack, the waiter states and the hand-over of an issued token.
        /// </summary>
        private readonly Lock m_gate = new();

        /// <summary>
        /// The most recently parked waiter.
        /// </summary>
        private Waiter? m_top;

        /// <summary>
        /// Number of parked waiters.
        /// </summary>
        private int m_parkedCount;

        /// <summary>
        /// The token state: <see cref="kNone"/>, <see cref="kIssued"/> or
        /// <see cref="kAssigned"/>.
        /// </summary>
        private int m_token;

        /// <summary>
        /// Whether the signal is closed; set under the gate.
        /// </summary>
        private bool m_closed;
    }
}
