/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Admits UA requests against the same authoritative records used by the DI locking methods.
    /// </summary>
    public interface ILockActivityService : ILockService
    {
        /// <summary>
        /// Gets the server-wide inactivity timeout advertised in ServerCapabilities.
        /// </summary>
        TimeSpan LockDuration { get; }

        /// <summary>
        /// Rejects another session with BadLocked, or admits the request. An owner's returned activity
        /// suspends inactivity expiry until disposal and restarts the timeout after its last activity.
        /// Unlocked-element activity prevents a conflicting InitLock until the request finishes.
        /// An admitted request is not cancelled by ExitLock or BreakLock.
        /// </summary>
        /// <param name="context">The requesting session context.</param>
        /// <param name="elementId">The protected topology element.</param>
        /// <param name="modifying">Whether another session's lock prevents this request.</param>
        /// <returns>The owned activity, or null for a read that does not belong to the lock owner.</returns>
        IDisposable? EnterActivity(ISystemContext context, NodeId elementId, bool modifying = true);

        /// <summary>
        /// Releases this element's lock only when it belongs to the closing session.
        /// Completion of an older admitted activity must not restore released ownership.
        /// </summary>
        /// <param name="elementId">The protected topology element.</param>
        /// <param name="sessionId">The closing session.</param>
        void ReleaseSession(NodeId elementId, NodeId sessionId);

        /// <summary>
        /// Releases lock ownership when the topology element is removed. Admitted activities continue,
        /// and completion does not restore the removed element's ownership.
        /// </summary>
        /// <param name="elementId">The removed topology element.</param>
        void ReleaseElement(NodeId elementId);
    }
}
