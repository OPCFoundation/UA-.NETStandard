// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// OPC 10000-100 §10.5 lock-method status codes returned through the
    /// <c>InitLock</c> / <c>RenewLock</c> / <c>ExitLock</c> /
    /// <c>BreakLock</c> output arguments.
    /// </summary>
    public static class LockStatus
    {
        /// <summary>
        /// The operation completed successfully.
        /// </summary>
        public const int Ok = 0;

        /// <summary>
        /// The element is already locked by another client (returned
        /// from <c>InitLock</c>).
        /// </summary>
        public const int AlreadyLocked = -1;

        /// <summary>
        /// The element could not be locked (returned from
        /// <c>InitLock</c>).
        /// </summary>
        public const int CouldNotLock = -2;

        /// <summary>
        /// The element is not currently locked (returned from
        /// <c>RenewLock</c>, <c>ExitLock</c>, or <c>BreakLock</c>).
        /// </summary>
        public const int NotLocked = -1;

        /// <summary>
        /// The calling client does not own the lock (returned from
        /// <c>RenewLock</c> / <c>ExitLock</c>).
        /// </summary>
        public const int WrongClient = -2;
    }
}
