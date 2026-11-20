// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The version-neutral canonical life-cycle state of a job order tracked by
    /// the in-memory engine. It mirrors the Job Control V2 receiver state machine
    /// (with the two interrupted sub-states) and is projected to the Job Control
    /// V1 <c>ISA95JobOrderStateEnum</c> by the conversion helpers.
    /// </summary>
    internal enum Isa95JobCanonicalState
    {
        /// <summary>
        /// The order is stored but is not yet allowed to start
        /// (V2 <c>NotAllowedToStart</c>, V1 <c>Waiting</c>).
        /// </summary>
        NotAllowedToStart = 1,

        /// <summary>
        /// The order is stored and allowed to start
        /// (V2 <c>AllowedToStart</c>, V1 <c>Ready</c>).
        /// </summary>
        AllowedToStart = 2,

        /// <summary>
        /// The order is executing (V2 <c>Running</c>, V1 <c>Running</c>).
        /// </summary>
        Running = 3,

        /// <summary>
        /// The order is interrupted and held by an internal condition
        /// (V2 <c>Interrupted/Held</c>, V1 <c>Held</c>).
        /// </summary>
        Held = 4,

        /// <summary>
        /// The order is interrupted and suspended by an external pause
        /// (V2 <c>Interrupted/Suspended</c>, V1 <c>Suspended</c>).
        /// </summary>
        Suspended = 5,

        /// <summary>
        /// The order has completed (V2 <c>Ended/Completed</c>,
        /// V1 <c>Completed</c>). Terminal.
        /// </summary>
        Completed = 6,

        /// <summary>
        /// The order was aborted (V2 <c>Aborted</c>, V1 <c>Aborted</c>). Terminal.
        /// </summary>
        Aborted = 7,

        /// <summary>
        /// The completed order has been closed (V2 <c>Ended/Closed</c>,
        /// V1 <c>Closed</c>). Terminal.
        /// </summary>
        Closed = 8,

        /// <summary>
        /// The order has been loaded into the execution system (V1 <c>Loaded</c>).
        /// This is a V1-only response state with no distinct V2 equivalent; it is
        /// preserved so that V1 responses round-trip and is projected to
        /// V2 <c>Running</c> when a V2 view is requested.
        /// </summary>
        Loaded = 9,

        /// <summary>
        /// The order is in an error condition (V1 <c>Error</c>). This is a V1-only
        /// response state with no distinct V2 equivalent; it is preserved so that
        /// V1 responses round-trip and is projected to V2 <c>Aborted</c> when a
        /// V2 view is requested.
        /// </summary>
        Error = 10
    }
}
