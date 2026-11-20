// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Snapshot of a topology element's lock state at a point in time.
    /// </summary>
    /// <param name="Locked">
    /// <see langword="true"/> when the element is currently locked.
    /// </param>
    /// <param name="LockingClient">
    /// Free-text identifier supplied by the client when calling
    /// <c>InitLock</c>; empty string when not locked.
    /// </param>
    /// <param name="LockingUser">
    /// User identity that owns the lock (typically the session's
    /// <see cref="ISystemContext.UserId"/>); empty when not locked.
    /// </param>
    /// <param name="RemainingLockTimeSeconds">
    /// Seconds remaining before the lock auto-expires. Zero when not
    /// locked.
    /// </param>
    public sealed record LockState(
        bool Locked,
        string LockingClient,
        string LockingUser,
        double RemainingLockTimeSeconds);
}
