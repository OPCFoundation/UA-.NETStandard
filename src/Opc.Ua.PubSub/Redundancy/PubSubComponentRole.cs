/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// The redundancy role a PubSub component (connection, writer group, or
    /// reader group) currently holds on this instance.
    /// </summary>
    public enum PubSubComponentRole
    {
        /// <summary>
        /// The component is active on this instance and drives its transport
        /// (publishes network messages / dispatches received messages).
        /// </summary>
        Active = 0,

        /// <summary>
        /// The component is on standby on this instance and does not drive
        /// its transport; it waits to take over from a failed active peer.
        /// </summary>
        Standby = 1
    }
}
