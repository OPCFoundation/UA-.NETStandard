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
    /// paths that enter it again. Disposing the underlying semaphore would turn
    /// those into <see cref="ObjectDisposedException"/> where they previously
    /// worked. Nothing leaks: the semaphore only allocates an operating system
    /// wait handle if <see cref="SemaphoreSlim.AvailableWaitHandle"/> is read, and
    /// it never is.
    /// </para>
    /// <para>
    /// <b>What it costs.</b> Measured by <c>ChannelGateBenchmarks</c>, an
    /// uncontended acquisition costs what the underlying
    /// <see cref="SemaphoreSlim"/> costs and allocates nothing, against about
    /// 13 ns for the monitor it replaced. The per-message work it is taken
    /// around — <c>SymmetricChannelCryptoBenchmarks.EncryptSignThenDecryptVerify</c>
    /// — is about 10,500 ns, so the gate does not register against it. The
    /// asynchronous entry completes synchronously when uncontended, which a test
    /// asserts, so it costs no suspension and no task.
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
        /// <b>The handle this returns must not be held across an
        /// <see langword="await"/>.</b> Use <see cref="EnterAsync"/> on any path
        /// that awaits, so the waiting thread is not occupied.
        /// </remarks>
        public Releaser Enter()
        {
            m_semaphore.Wait();

            return new Releaser(this);
        }

        /// <summary>
        /// Enters the gate without occupying the calling thread while it waits.
        /// </summary>
        /// <param name="ct">Cancels the wait.</param>
        /// <returns>A handle that leaves the gate when disposed.</returns>
        public ValueTask<Releaser> EnterAsync(CancellationToken ct = default)
        {
            Task wait = m_semaphore.WaitAsync(ct);

            // Uncontended is the common case. Completing synchronously keeps the
            // caller's sequencing identical to the lock this replaces.
            // Task.IsCompletedSuccessfully does not exist on the .NET Framework
            // targets, so the status is read directly.
            if (wait.Status == TaskStatus.RanToCompletion)
            {
                return new ValueTask<Releaser>(new Releaser(this));
            }

            return AwaitEntryAsync(wait);
        }

        /// <summary>
        /// Whether the gate is held by any context.
        /// </summary>
        /// <remarks>
        /// Used by tests that need to establish contention before entering.
        /// </remarks>
        internal bool IsHeldBySomeContextForTest => m_semaphore.CurrentCount == 0;

        private async ValueTask<Releaser> AwaitEntryAsync(Task wait)
        {
            await wait.ConfigureAwait(false);

            return new Releaser(this);
        }

        private void Leave()
        {
            m_semaphore.Release();
        }

        private readonly SemaphoreSlim m_semaphore = new(1, 1);

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
