/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Diagnostics
{
    /// <summary>
    /// Verbosity tier of <see cref="IPubSubDiagnostics"/>. Higher tiers
    /// retain more information at the cost of additional memory and a
    /// small per-operation overhead.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11.2">
    /// Part 14 §9.1.11.2 DiagnosticsLevel</see>. The three tiers map to the
    /// repository research supplement (§8): <see cref="Low"/> tracks
    /// monotonic counters only, <see cref="Medium"/> additionally records
    /// the most recent error <see cref="StatusCode"/> per component, and
    /// <see cref="High"/> keeps a bounded ring buffer of recent error
    /// events suitable for live troubleshooting.
    /// </remarks>
    public enum PubSubDiagnosticsLevel
    {
        /// <summary>
        /// Counter-only: <see cref="IPubSubDiagnostics.Increment"/> updates
        /// monotonic counters but <see cref="IPubSubDiagnostics.RecordError"/>
        /// is a no-op.
        /// </summary>
        Low,

        /// <summary>
        /// Counters plus last-error per component: the most recent
        /// <see cref="StatusCode"/> reported via
        /// <see cref="IPubSubDiagnostics.RecordError"/> is retained.
        /// </summary>
        Medium,

        /// <summary>
        /// Counters, last-error, and a bounded ring buffer of recent error
        /// events with timestamps. Suitable for live troubleshooting.
        /// </summary>
        High
    }
}
