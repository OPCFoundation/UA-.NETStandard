// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// PubSub redundancy behaviour for a redundant set of publishers or
    /// subscribers, per OPC UA Part 14 §9.1.6.
    /// </summary>
    /// <remarks>
    /// The mode selects how much runtime state a standby instance keeps
    /// warm so it can take over from a failed active instance. Cold rebuilds
    /// from the shared configuration/runtime-state stores on failover, warm
    /// keeps the configuration enabled but paused, and hot additionally
    /// tracks live sequence state so take-over introduces no gap.
    /// </remarks>
    public enum PubSubRedundancyMode
    {
        /// <summary>
        /// No redundancy — the instance is always active.
        /// </summary>
        None = 0,

        /// <summary>
        /// Cold standby: a standby instance rebuilds configuration and
        /// runtime state from the shared stores only after the active
        /// instance fails.
        /// </summary>
        Cold = 1,

        /// <summary>
        /// Warm standby: a standby instance keeps its configuration loaded
        /// and paused, ready to resume quickly on failover.
        /// </summary>
        Warm = 2,

        /// <summary>
        /// Hot standby: a standby instance additionally tracks live
        /// sequence/keep-alive state so take-over is seamless.
        /// </summary>
        Hot = 3
    }
}
