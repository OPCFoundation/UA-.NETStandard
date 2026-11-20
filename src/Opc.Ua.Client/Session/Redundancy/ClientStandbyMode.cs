/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client.Redundancy
{
    /// <summary>
    /// Standby behavior of a follower replica in a client replica set, mirroring
    /// the server-side redundancy modes.
    /// </summary>
    public enum ClientStandbyMode
    {
        /// <summary>
        /// Followers run leader election only and connect + activate the session
        /// on promotion. Lowest cost, highest failover latency.
        /// </summary>
        Cold,

        /// <summary>
        /// Followers keep a connected session but create no subscriptions; the
        /// promoted leader creates subscriptions on promotion.
        /// </summary>
        Warm,

        /// <summary>
        /// Followers keep a connected session with subscriptions sampling-only
        /// (publishing disabled); the promoted leader enables publishing. Lowest
        /// failover latency.
        /// </summary>
        Hot
    }
}
