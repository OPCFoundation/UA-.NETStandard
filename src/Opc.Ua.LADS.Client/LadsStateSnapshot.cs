/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The current state of a state machine: <c>DeviceState</c>,
    /// <c>FunctionalUnitState</c>, <c>RunningStateMachine</c>,
    /// <c>ControlFunctionState</c>, <c>CoverState</c>, <c>MaintenanceState</c>
    /// or the Machinery item and operation mode states.
    /// </summary>
    /// <param name="StateMachineId">The NodeId of the state machine.</param>
    /// <param name="CurrentState">The display text of the current state.</param>
    /// <param name="CurrentStateId">The NodeId of the current state, when the server exposes it.</param>
    public sealed record LadsStateSnapshot(NodeId StateMachineId, string CurrentState, NodeId CurrentStateId)
    {
        /// <summary>
        /// Gets whether the current state has the given name, ignoring case.
        /// </summary>
        /// <param name="stateName">The state name, for example <c>Running</c>.</param>
        public bool IsIn(string stateName)
        {
            return string.Equals(CurrentState, stateName, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return CurrentState;
        }
    }
}
