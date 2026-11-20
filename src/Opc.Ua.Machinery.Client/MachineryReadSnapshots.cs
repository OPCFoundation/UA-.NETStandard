// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Machinery.Result;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// A strictly read Machinery state or operation mode with per-field evidence.
    /// </summary>
    /// <param name="State">The compatible finite-state projection.</param>
    /// <param name="Values">Original state and transition fields, including optional absence.</param>
    public sealed record MachineryStateSnapshot(
        FiniteStateSnapshot State,
        ArrayOf<MachineryReadValue> Values)
    {
        /// <summary>
        /// Gets the optional, exactly UInt32 current-state number.
        /// </summary>
        public uint? CurrentStateNumber { get; init; }

        /// <summary>
        /// Gets the optional, exactly UInt32 last-transition number.
        /// </summary>
        public uint? LastTransitionNumber { get; init; }

        /// <summary>
        /// Gets the server's transition time, when published.
        /// </summary>
        public DateTime? TransitionTime { get; init; }
    }

    /// <summary>
    /// A bounded job-order list with its original source value. Empty, Good-null
    /// and absent lists remain distinguishable through Values.
    /// </summary>
    /// <param name="JobOrders">The decoded job orders and states.</param>
    /// <param name="Values">The list's original read or explicit absence evidence.</param>
    public sealed record MachineryJobOrdersSnapshot(
        ArrayOf<V2.ISA95JobOrderAndStateDataType> JobOrders,
        ArrayOf<MachineryReadValue> Values);

    /// <summary>
    /// A bounded job-response list with its original source value.
    /// </summary>
    /// <param name="JobResponses">The decoded job responses.</param>
    /// <param name="Values">The list's original read or explicit absence evidence.</param>
    public sealed record MachineryJobResponsesSnapshot(
        ArrayOf<V2.ISA95JobResponseDataType> JobResponses,
        ArrayOf<MachineryReadValue> Values);

    /// <summary>
    /// A published result variable with its original source, quality and time.
    /// This snapshot never downloads result payloads or dereferences result URIs.
    /// </summary>
    /// <param name="Result">The decoded result, or null for a present Good-null value.</param>
    /// <param name="Values">The original result variable value.</param>
    public sealed record MachineryResultSnapshot(
        ResultDataType? Result,
        ArrayOf<MachineryReadValue> Values);
}
