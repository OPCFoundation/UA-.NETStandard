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
    /// It is <b>re-entrant</b>, because the code it replaces relies on that:
    /// <c>HandleIncomingMessage</c> holds the lock and calls
    /// <c>ForceChannelFault</c>, which takes it again, and there are seven more
    /// paths like it. A plain <see cref="SemaphoreSlim"/> would deadlock on each
    /// of them. Re-entrancy is tracked per logical call context, which is what a
    /// monitor does per thread and what an asynchronous continuation needs
    /// instead.
    /// </para>
    /// <para>
    /// One consequence has to be handled explicitly. A logical context is
    /// inherited by work started from it, so a task started <em>while the gate is
    /// held</em> would inherit the right to re-enter and could then run
    /// concurrently with its parent inside the guarded region. Any such
    /// fire-and-forget work must call <see cref="LeaveInheritedContext"/> first,
    /// which drops the inherited entitlement for that branch only.
    /// </para>
    /// <para>
    /// It is deliberately <b>not</b> disposable. It replaces a monitor, which has
    /// no disposed state, and channel teardown enters it and then continues to run
    /// paths that enter it again. Disposing the underlying semaphore would turn
    /// those into <see cref="ObjectDisposedException"/> where they previously
    /// worked. Nothing leaks: the semaphore only allocates an operating system
    /// wait handle if <see cref="SemaphoreSlim.AvailableWaitHandle"/> is read, and
    /// it never is.
    /// </para>
    /// </remarks>
    // CA1001: the semaphore is deliberately not disposed. This type replaces a
    // monitor, which has no disposed state, and channel teardown enters the gate
    // and then runs paths that enter it again; disposing would turn those into
    // ObjectDisposedException where they previously worked. SemaphoreSlim only
    // allocates an operating system wait handle when AvailableWaitHandle is read,
    // which this type never does, so there is nothing to release.
#pragma warning disable CA1001
    internal sealed class ChannelGate
#pragma warning restore CA1001
    {
        /// <summary>
        /// Enters the gate, blocking until it is free.
        /// </summary>
        /// <returns>A handle that leaves the gate when disposed.</returns>
        /// <remarks>
        /// This is the direct replacement for <c>lock (DataLock)</c> and behaves
        /// the same way, including when the caller already holds the gate.
        /// </remarks>
        public Releaser Enter()
        {
            Holder? holder = m_current.Value;

            if (holder is { Depth: > 0 })
            {
                holder.Depth++;
                return new Releaser(this, holder, owner: false);
            }

            // A monitor is re-entrant per thread, and the code this replaces
            // relies on that in a way the logical context cannot express: a
            // completion callback invoked inline runs on the thread that already
            // holds the gate but under the context captured when the operation
            // was started. Honouring thread identity as well keeps those paths
            // working instead of deadlocking against themselves.
            Holder? onThread = m_owner;

            if (onThread is { Depth: > 0 } &&
                m_owningThreadId == Environment.CurrentManagedThreadId)
            {
                onThread.Depth++;
                return new Releaser(this, onThread, owner: false);
            }

            m_semaphore.Wait();

            return TakeOwnership();
        }

        /// <summary>
        /// Enters the gate without occupying the calling thread while it waits.
        /// </summary>
        /// <param name="ct">Cancels the wait.</param>
        /// <returns>A handle that leaves the gate when disposed.</returns>
        public ValueTask<Releaser> EnterAsync(CancellationToken ct = default)
        {
            Holder? holder = m_current.Value;

            if (holder is { Depth: > 0 })
            {
                holder.Depth++;
                return new ValueTask<Releaser>(new Releaser(this, holder, owner: false));
            }

            Task wait = m_semaphore.WaitAsync(ct);

            // Uncontended is the common case. Completing synchronously keeps the
            // caller's sequencing identical to the lock this replaces.
            // Task.IsCompletedSuccessfully does not exist on the .NET Framework
            // targets, so the status is read directly.
            if (wait.Status == TaskStatus.RanToCompletion)
            {
                return new ValueTask<Releaser>(TakeOwnership());
            }

            return AwaitEntryAsync(wait);
        }

        /// <summary>
        /// Drops an entitlement to re-enter that was inherited from the context
        /// this work was started in.
        /// </summary>
        /// <remarks>
        /// Call this as the first statement of any work started with
        /// fire-and-forget semantics from a path that may hold the gate. Without
        /// it that work would believe it already holds the gate, and would run
        /// inside the guarded region alongside whatever started it.
        /// <para>
        /// The write only affects the branch that makes it, so the caller keeps
        /// the gate it actually holds.
        /// </para>
        /// </remarks>
        public void LeaveInheritedContext()
        {
            m_current.Value = null;
        }

        /// <summary>
        /// Whether the current context holds the gate.
        /// </summary>
        /// <remarks>
        /// Intended for assertions and for code that must not assume it is
        /// already inside the guarded region.
        /// </remarks>
        public bool IsHeldByCurrentContext => m_current.Value is { Depth: > 0 };

        private async ValueTask<Releaser> AwaitEntryAsync(Task wait)
        {
            await wait.ConfigureAwait(false);
            return TakeOwnership();
        }

        private Releaser TakeOwnership()
        {
            var holder = new Holder { Depth = 1 };
            m_current.Value = holder;
            m_owner = holder;
            m_owningThreadId = Environment.CurrentManagedThreadId;
            return new Releaser(this, holder, owner: true);
        }

        private void Leave(Holder holder, bool owner)
        {
            if (--holder.Depth > 0)
            {
                return;
            }

            if (owner)
            {
                m_current.Value = null;
                m_owner = null;
                m_owningThreadId = 0;
                m_semaphore.Release();
            }
        }

        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private readonly AsyncLocal<Holder?> m_current = new();
        private volatile Holder? m_owner;
        private volatile int m_owningThreadId;

        /// <summary>
        /// Counts how deeply one logical context has entered the gate.
        /// </summary>
        /// <remarks>
        /// A mutable instance rather than a counter held directly in the
        /// <see cref="AsyncLocal{T}"/>: a nested frame has to be able to record
        /// its exit where the frame that entered can see it, and a value written
        /// to an <see cref="AsyncLocal{T}"/> by a callee is not visible to its
        /// caller.
        /// </remarks>
        internal sealed class Holder
        {
            public int Depth;
        }

        /// <summary>
        /// Leaves the gate when disposed.
        /// </summary>
        internal readonly struct Releaser : IDisposable, IEquatable<Releaser>
        {
            internal Releaser(ChannelGate gate, Holder holder, bool owner)
            {
                m_gate = gate;
                m_holder = holder;
                m_owner = owner;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_gate?.Leave(m_holder, m_owner);
            }

            /// <inheritdoc/>
            public bool Equals(Releaser other)
            {
                return ReferenceEquals(m_gate, other.m_gate) &&
                    ReferenceEquals(m_holder, other.m_holder) &&
                    m_owner == other.m_owner;
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is Releaser other && Equals(other);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCode.Combine(m_gate, m_holder, m_owner);
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
            private readonly Holder m_holder;
            private readonly bool m_owner;
        }
    }
}
