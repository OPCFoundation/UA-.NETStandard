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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Serialises access to a channel's state, and can be entered from either a
    /// synchronous or an asynchronous path.
    /// </summary>
    /// <remarks>
    /// This replaces a <see langword="lock"/> that a channel used to take around
    /// every state transition. A monitor cannot be held across an
    /// <see langword="await"/>, and the secure channel open path has to await
    /// once a private key may be served over a network.
    /// <para>
    /// It is <b>not re-entrant</b>. A monitor is, and the code this replaced
    /// relied on that in about ten places, but re-entrancy cannot be tracked by
    /// thread for a holder that awaits — the thread is returned to the pool at
    /// every suspension point — so it had to be tracked per logical call context
    /// with an <see cref="AsyncLocal{T}"/> instead. That was the source of most
    /// of this type's defects: a logical context is inherited by work started
    /// from it, so anything started while the gate was held inherited the right
    /// to re-enter and ran <em>inside</em> the guarded region alongside whatever
    /// started it. Each such path had to be found by review and opted out by
    /// hand, with no compiler check.
    /// </para>
    /// <para>
    /// Every path that used to re-enter now calls a lock-free <c>Core</c> method
    /// instead, so the ownership tracking is gone and with it that whole class of
    /// defect. Entering twice from one flow now deadlocks, which is the ordinary
    /// contract of a mutex and is diagnosable, rather than silently running two
    /// flows inside a region that exists to keep them apart.
    /// </para>
    /// <para>
    /// It is deliberately <b>not</b> disposable. It replaces a monitor, which has
    /// no disposed state, and channel teardown enters it and then continues to run
    /// paths that enter it again. It holds no operating system resource.
    /// </para>
    /// <para>
    /// <b>What it costs.</b> An uncontended entry and exit are one interlocked
    /// compare-and-exchange each and allocate nothing. Only a contended entry
    /// takes a lock, to queue a waiter, and only an exit with waiters queued takes
    /// it to hand the gate to the first of them. A <see cref="SemaphoreSlim"/>,
    /// which this used to wrap, takes its monitor on every wait and release; the
    /// receive loop takes the gate for every message, and once that monitor had
    /// seen contention every entry went through its slow path. The asynchronous
    /// entry completes synchronously when uncontended, which a test asserts, so
    /// it costs no suspension and no task.
    /// </para>
    /// <para>
    /// Waiters are served first in, first out, and an exit with waiters hands the
    /// gate straight to the first one, so a new entrant cannot overtake them. A
    /// waiter resumes on the thread pool, never on the thread that left the gate.
    /// </para>
    /// </remarks>
    internal sealed class ChannelGate
    {
        /// <summary>
        /// Enters the gate, blocking until it is free.
        /// </summary>
        /// <returns>A handle that leaves the gate when disposed.</returns>
        /// <remarks>
        /// <b>The handle this returns must not be held across an
        /// <see langword="await"/>.</b> Use <see cref="EnterAsync"/> on any path
        /// that awaits, so the waiting thread is not occupied.
        /// </remarks>
        public Releaser Enter()
        {
            if (Interlocked.CompareExchange(ref m_state, kHeld, kFree) != kFree)
            {
                Waiter? waiter = QueueWaiter();
                // a queued waiter is completed only by the exit that hands it the gate.
                waiter?.Task.GetAwaiter().GetResult();
            }

            return new Releaser(this);
        }

        /// <summary>
        /// Enters the gate without occupying the calling thread while it waits.
        /// </summary>
        /// <param name="ct">Cancels the wait.</param>
        /// <returns>A handle that leaves the gate when disposed.</returns>
        public ValueTask<Releaser> EnterAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return new ValueTask<Releaser>(Task.FromCanceled<Releaser>(ct));
            }

            // Uncontended is the common case. Completing synchronously keeps the
            // caller's sequencing identical to the lock this replaces.
            if (Interlocked.CompareExchange(ref m_state, kHeld, kFree) == kFree)
            {
                return new ValueTask<Releaser>(new Releaser(this));
            }

            Waiter? waiter = QueueWaiter();
            if (waiter == null)
            {
                return new ValueTask<Releaser>(new Releaser(this));
            }

            return AwaitEntryAsync(waiter, ct);
        }

        /// <summary>
        /// Whether the gate is held by any context.
        /// </summary>
        /// <remarks>
        /// Used by tests that need to establish contention before entering.
        /// </remarks>
        internal bool IsHeldBySomeContextForTest => Volatile.Read(ref m_state) != kFree;

        private async ValueTask<Releaser> AwaitEntryAsync(Waiter waiter, CancellationToken ct)
        {
            CancellationTokenRegistration registration = default;
            if (ct.CanBeCanceled)
            {
                // runs at once when the token is already cancelled.
                registration = ct.Register(static state => ((Waiter)state!).Cancel(), waiter);
            }

            try
            {
                await waiter.Task.ConfigureAwait(false);
            }
            finally
            {
                registration.Dispose();
            }

            return new Releaser(this);
        }

        /// <summary>
        /// Queues a waiter for a gate that is held, or takes the gate when it was
        /// left in the meantime.
        /// </summary>
        /// <returns>The waiter, or <see langword="null"/> when the caller took the
        /// gate.</returns>
        private Waiter? QueueWaiter()
        {
            lock (m_waitersLock)
            {
                while (true)
                {
                    int state = Volatile.Read(ref m_state);
                    if (state == kFree)
                    {
                        if (Interlocked.CompareExchange(ref m_state, kHeld, kFree) == kFree)
                        {
                            return null;
                        }
                    }
                    else if (state == kContended ||
                        Interlocked.CompareExchange(ref m_state, kContended, kHeld) == kHeld)
                    {
                        // from here on the exit cannot leave the gate without the lock.
                        break;
                    }
                }

                var waiter = new Waiter(this);
                Append(waiter);
                return waiter;
            }
        }

        /// <summary>
        /// Leaves the gate, or hands it to the first waiter.
        /// </summary>
        private void Leave()
        {
            int state = Interlocked.CompareExchange(ref m_state, kFree, kHeld);
            if (state == kHeld)
            {
                return;
            }

            if (state == kFree)
            {
                throw new SynchronizationLockException("The channel gate is not held.");
            }

            Waiter? next;
            lock (m_waitersLock)
            {
                // waiters are queued while the state is contended; a cancelled one
                // is unlinked, and the last one to go returns the state to held,
                // possibly after the exchange above saw it contended.
                next = m_first;
                if (next == null)
                {
                    Volatile.Write(ref m_state, kFree);
                    return;
                }
                Unlink(next);
                if (m_first == null)
                {
                    Volatile.Write(ref m_state, kHeld);
                }
            }

            // the gate stays held: ownership passes to the waiter.
            next.TrySetResult(true);
        }

        /// <summary>
        /// Removes a waiter whose wait was cancelled, unless it has already been
        /// handed the gate.
        /// </summary>
        /// <returns><see langword="true"/> when the waiter was removed.</returns>
        private bool Remove(Waiter waiter)
        {
            lock (m_waitersLock)
            {
                if (!waiter.Queued)
                {
                    return false;
                }

                Unlink(waiter);
                if (m_first == null)
                {
                    // the holder still holds the gate, now without waiters.
                    Volatile.Write(ref m_state, kHeld);
                }
                return true;
            }
        }

        /// <summary>
        /// Appends a waiter; called under <see cref="m_waitersLock"/>.
        /// </summary>
        private void Append(Waiter waiter)
        {
            waiter.Previous = m_last;
            if (m_last == null)
            {
                m_first = waiter;
            }
            else
            {
                m_last.Next = waiter;
            }
            m_last = waiter;
            waiter.Queued = true;
        }

        /// <summary>
        /// Unlinks a queued waiter; called under <see cref="m_waitersLock"/>.
        /// </summary>
        private void Unlink(Waiter waiter)
        {
            if (waiter.Previous == null)
            {
                m_first = waiter.Next;
            }
            else
            {
                waiter.Previous.Next = waiter.Next;
            }

            if (waiter.Next == null)
            {
                m_last = waiter.Previous;
            }
            else
            {
                waiter.Next.Previous = waiter.Previous;
            }

            waiter.Next = null;
            waiter.Previous = null;
            waiter.Queued = false;
        }

        /// <summary>
        /// The gate is free.
        /// </summary>
        private const int kFree = 0;

        /// <summary>
        /// The gate is held and no waiter is queued; it is left without the lock.
        /// </summary>
        private const int kHeld = 1;

        /// <summary>
        /// The gate is held and waiters are queued; leaving it takes the lock.
        /// </summary>
        private const int kContended = 2;

        /// <summary>
        /// <see cref="kFree"/>, <see cref="kHeld"/> or <see cref="kContended"/>.
        /// The change to and from <see cref="kContended"/> is made under
        /// <see cref="m_waitersLock"/>, the others with a compare-and-exchange.
        /// </summary>
        private int m_state;

        /// <summary>
        /// Protects the waiter queue.
        /// </summary>
        private readonly Lock m_waitersLock = new();

        private Waiter? m_first;
        private Waiter? m_last;

        /// <summary>
        /// A queued entry, completed with the gate or cancelled.
        /// </summary>
        private sealed class Waiter : TaskCompletionSource<bool>
        {
            public Waiter(ChannelGate gate)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                m_gate = gate;
            }

            /// <summary>
            /// Whether the waiter is in the queue; changed under the gate's lock.
            /// </summary>
            public bool Queued;

            public Waiter? Next;
            public Waiter? Previous;

            /// <summary>
            /// Cancels the wait, unless the gate was handed over already.
            /// </summary>
            public void Cancel()
            {
                if (m_gate.Remove(this))
                {
                    TrySetCanceled();
                }
            }

            private readonly ChannelGate m_gate;
        }

        /// <summary>
        /// Leaves the gate when disposed.
        /// </summary>
        /// <remarks>
        /// A copyable struct, so disposing a copy releases as well. Every use in
        /// the channel code is a <see langword="using"/> over the value returned
        /// by <see cref="Enter"/> or <see cref="EnterAsync"/>, which is disposed
        /// exactly once.
        /// </remarks>
        internal readonly struct Releaser : IDisposable, IEquatable<Releaser>
        {
            internal Releaser(ChannelGate gate)
            {
                m_gate = gate;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_gate?.Leave();
            }

            /// <inheritdoc/>
            public bool Equals(Releaser other)
            {
                return ReferenceEquals(m_gate, other.m_gate);
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is Releaser other && Equals(other);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return m_gate?.GetHashCode() ?? 0;
            }

            public static bool operator ==(Releaser left, Releaser right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(Releaser left, Releaser right)
            {
                return !left.Equals(right);
            }

            private readonly ChannelGate m_gate;
        }
    }
}
