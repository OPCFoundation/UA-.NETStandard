/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Optional capability for an <see cref="ILockService"/> to authorize access
    /// using the calling session's ownership of an unexpired lock.
    /// </summary>
    /// <remarks>
    /// Consumers requiring exclusive access must deny access when the configured
    /// lock service does not implement this capability. A successful check is a
    /// snapshot, not a reservation for a subsequent operation.
    /// </remarks>
    public interface ILockOwnershipService
    {
        /// <summary>
        /// Returns whether the calling session owns the element's unexpired lock,
        /// without changing the lock or its timeout. Returns false when no session
        /// identifier is available, the lock has expired, or its session has closed.
        /// User names and client-supplied context strings do not establish ownership.
        /// </summary>
        bool IsLockOwner(ISystemContext context, NodeId elementId);
    }
}
