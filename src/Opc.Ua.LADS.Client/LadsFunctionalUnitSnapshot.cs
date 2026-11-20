/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A snapshot of a LADS functional unit.
    /// </summary>
    /// <param name="FunctionalUnitId">The NodeId of the functional unit.</param>
    /// <param name="DisplayName">The display name of the functional unit.</param>
    public sealed record LadsFunctionalUnitSnapshot(NodeId FunctionalUnitId, string DisplayName)
    {
        /// <summary>
        /// Gets the state of the <c>FunctionalUnitState</c> state machine.
        /// </summary>
        public LadsStateSnapshot? State { get; init; }

        /// <summary>
        /// Gets the state of the nested <c>RunningStateMachine</c>, when present.
        /// </summary>
        public LadsStateSnapshot? RunningState { get; init; }

        /// <summary>
        /// Gets whether the functional unit has a <c>ProgramManager</c>.
        /// </summary>
        public bool HasProgramManager { get; init; }

        /// <summary>
        /// Gets the active program, when the functional unit has a program manager.
        /// </summary>
        public LadsActiveProgram? ActiveProgram { get; init; }

        /// <summary>
        /// Gets the functions of the functional unit, including nested functions.
        /// </summary>
        public ArrayOf<LadsFunctionSnapshot> Functions { get; init; } = [];
    }
}
