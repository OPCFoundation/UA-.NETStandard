// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Opt-in marker for encodeable types that participate in
    /// activator-level pooling. Implementing this interface is the
    /// single signal the framework uses to decide whether an instance
    /// should be returned to its activator's pool after the framework
    /// is done with it. Plain <see cref="IEncodeable"/> implementations
    /// are unaffected.
    /// </para>
    /// <para>
    /// The framework calls <see cref="Reuse"/> after the handler
    /// dispatch completes for instances it created via a pooled
    /// activator (see <see cref="PooledEncodeableType{T}"/>). Callers
    /// must not retain a reference to the instance past their own
    /// <see cref="Reuse"/> call: the pool may re-rent the instance to
    /// an unrelated consumer at any time after <see cref="Reuse"/>
    /// returns.
    /// </para>
    /// </summary>
    public interface IPooledEncodeable : IEncodeable
    {
        /// <summary>
        /// <para>
        /// Reset every mutable field on the instance to its default
        /// value and return the instance to its activator's pool for
        /// reuse.
        /// </para>
        /// <para>
        /// Implementations must be idempotent against accidental
        /// double invocation: the first call resets state and pools
        /// the instance; subsequent calls on the same reference are
        /// no-ops. The field reset must complete before the pool
        /// return so a future rent observes a fully cleared instance.
        /// </para>
        /// <para>
        /// Implementations must not assume the calling thread. The
        /// framework's only Reuse caller is the V2 subscription
        /// dispatcher, which runs on the subscription's single-reader
        /// message loop, but third-party callers may invoke Reuse on
        /// other threads.
        /// </para>
        /// </summary>
        void Reuse();
    }
}
