/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Application-facing facade over the OPC 10000-100 §10.5 locking
    /// service. The service tracks ownership of every
    /// <c>TopologyElementType.Lock</c> in the address space, enforces
    /// timeouts, and propagates state changes to the
    /// <see cref="LockingServicesState.Locked"/> /
    /// <see cref="LockingServicesState.LockingClient"/> /
    /// <see cref="LockingServicesState.LockingUser"/> /
    /// <see cref="LockingServicesState.RemainingLockTime"/> properties.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations <em>must</em> be thread-safe — the four lock
    /// methods are typically invoked from per-session worker threads
    /// and the time-tick refresher runs from a background timer.
    /// </para>
    /// <para>
    /// The default <see cref="DefaultLockService"/> implementation
    /// keeps a per-element record (client tag, owning session, expiry
    /// timestamp) and uses the session-close events emitted by the
    /// hosting <c>ISessionManager</c> to release locks when their
    /// owning session disappears.
    /// </para>
    /// </remarks>
    public interface ILockService
    {
        /// <summary>
        /// Returns a snapshot of the current lock state for the
        /// topology element identified by <paramref name="elementId"/>.
        /// Returns <see langword="false"/> for <c>Locked</c> if no
        /// lock has ever been acquired (or the lock has expired).
        /// </summary>
        LockState GetState(NodeId elementId);

        /// <summary>
        /// Attempts to acquire the lock on the supplied element on
        /// behalf of the calling session. Returns one of the
        /// <see cref="LockStatus"/> codes; the lock is held for the
        /// service's configured default timeout (see
        /// <see cref="DefaultLockService"/>).
        /// </summary>
        int InitLock(
            ISystemContext context,
            NodeId elementId,
            string clientContext);

        /// <summary>
        /// Renews an existing lock owned by the calling session.
        /// Resets the timeout to the configured default.
        /// </summary>
        int RenewLock(ISystemContext context, NodeId elementId);

        /// <summary>
        /// Releases a lock owned by the calling session.
        /// </summary>
        int ExitLock(ISystemContext context, NodeId elementId);

        /// <summary>
        /// Forcibly releases a lock regardless of ownership. Intended
        /// for administrative use after a client has crashed and
        /// abandoned a lock.
        /// </summary>
        int BreakLock(ISystemContext context, NodeId elementId);
    }
}
