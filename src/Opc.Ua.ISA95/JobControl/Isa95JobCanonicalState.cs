// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// The version-neutral lifecycle state of a job order. V2 preparation states
    /// distinguish start permission from operational readiness.
    /// </summary>
    public enum Isa95JobCanonicalState
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
        /// Preserved in V1 round trips and projected to V2
        /// <c>NotAllowedToStart/Loaded</c>.
        /// </summary>
        Loaded = 9,

        /// <summary>
        /// The order is in an error condition (V1 <c>Error</c>). This is a V1-only
        /// response state with no distinct V2 equivalent; it is preserved so that
        /// V1 responses round-trip and is projected to V2 <c>Aborted</c> when a
        /// V2 view is requested.
        /// </summary>
        Error = 10,

        /// <summary>
        /// Permission to start has not been granted and the job is not ready.
        /// </summary>
        NotAllowedToStartWaiting = 11,

        /// <summary>
        /// The job is ready but is not allowed to start.
        /// </summary>
        NotAllowedToStartReady = 12,

        /// <summary>
        /// The job is loaded but is not allowed to start.
        /// </summary>
        NotAllowedToStartLoaded = 13,

        /// <summary>
        /// The job is allowed to start but is not ready.
        /// </summary>
        AllowedToStartWaiting = 14,

        /// <summary>
        /// The job is ready and allowed to start.
        /// </summary>
        AllowedToStartReady = 15,

        /// <summary>
        /// The job is loaded and allowed to start.
        /// </summary>
        AllowedToStartLoaded = 16
    }
}
