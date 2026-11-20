/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Stable event identifiers for the independently authored sample.
    /// </summary>
    internal static class RecyclingServerEventIds
    {
        /// <summary>Durable command application.</summary>
        internal const int CommandCommitted = 1;

        /// <summary>Persistence failure requiring recovery.</summary>
        internal const int PersistenceFailed = 2;

        /// <summary>View connection unavailable.</summary>
        internal const int ViewUnavailable = 3;

        /// <summary>Bounded membership contention or previous-lease wait without session replacement.</summary>
        internal const int MembershipPending = 4;

        /// <summary>Explicit, non-retriable membership authorization/protocol failure.</summary>
        internal const int MembershipRejected = 5;

        /// <summary>Transient writer admission rejection while pacing the coordinator.</summary>
        internal const int CoordinatorTickDeferred = 6;
    }
}
