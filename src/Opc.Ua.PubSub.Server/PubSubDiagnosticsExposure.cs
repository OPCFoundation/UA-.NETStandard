// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Controls how much of the standard <c>PubSubDiagnosticsType</c>
    /// node-set (Part 14 §9.1.11) is bound to the runtime
    /// <see cref="Diagnostics.IPubSubDiagnostics"/>
    /// instance.
    /// </summary>
    /// <remarks>
    /// Implements the exposure dial referenced by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11">
    /// Part 14 §9.1.11 PubSubDiagnosticsType</see>. The default
    /// (<see cref="Counters"/>) wires every cumulative counter from
    /// <see cref="Diagnostics.PubSubDiagnosticsCounterKind"/>
    /// onto the corresponding <c>Counters_*</c> Variable in the
    /// address space.
    /// </remarks>
    public enum PubSubDiagnosticsExposure
    {
        /// <summary>
        /// Do not bind any diagnostics counters. The
        /// <c>PublishSubscribe_Diagnostics</c> sub-tree stays at its
        /// default zero values loaded from the stack NodeSet.
        /// </summary>
        None,

        /// <summary>
        /// Bind the cumulative counter Variables in
        /// <c>PublishSubscribe_Diagnostics_Counters_*</c>.
        /// </summary>
        Counters,

        /// <summary>
        /// Bind the cumulative counters and the <c>TotalError</c>
        /// summary Variable, surfacing the most recent error captured
        /// by <see cref="Diagnostics.IPubSubDiagnostics"/>.
        /// </summary>
        Errors,

        /// <summary>
        /// Bind every PubSubDiagnostics Variable supported by the
        /// PubSub runtime, including <c>LiveValues_*</c> counters
        /// (configured and operational writer / reader totals).
        /// </summary>
        Full
    }
}
