// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.AMB
{
    /// <summary>
    /// The states of the <c>MaintenanceEventStateMachineType</c>
    /// (OPC 10000-110 §12.3), valued with the state numbers the model
    /// publishes.
    /// </summary>
    /// <remarks>
    /// The values are taken from the generated
    /// <see cref="MaintenanceEventStateMachineTypeIds.StateNumbers"/>, so a
    /// model revision that renumbers a state fails the build here instead of
    /// silently diverging.
    /// </remarks>
    public enum MaintenanceStateKind : uint
    {
        /// <summary>
        /// The maintenance activity is planned; the initial state.
        /// </summary>
        Planned = MaintenanceEventStateMachineTypeIds.StateNumbers.Planned,

        /// <summary>
        /// The maintenance activity is being executed.
        /// </summary>
        Executing = MaintenanceEventStateMachineTypeIds.StateNumbers.Executing,

        /// <summary>
        /// The maintenance activity is finished, successfully or not.
        /// </summary>
        Finished = MaintenanceEventStateMachineTypeIds.StateNumbers.Finished
    }
}
