// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// Providers can also implement <see cref="ILockOwnershipService"/> to support
    /// session-ownership authorization without renewing a lock. Existing providers
    /// need not implement this optional capability.
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
        /// The standard method binding requires an authenticated SecurityAdmin
        /// identity over an encrypted channel through the server role infrastructure.
        /// The default provider also enforces this policy for direct calls.
        /// </summary>
        int BreakLock(ISystemContext context, NodeId elementId);
    }
}
