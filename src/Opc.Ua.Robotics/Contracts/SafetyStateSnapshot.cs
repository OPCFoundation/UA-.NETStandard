/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a SafetyStateType instance.
    /// </summary>
    public sealed record SafetyStateSnapshot
    {
        /// <summary>
        /// The safety-state identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The EmergencyStop value, including status and timestamps.
        /// </summary>
        public DataValue EmergencyStop { get; init; } = DataValue.Null;

        /// <summary>
        /// The EmergencyStop variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId EmergencyStopId { get; init; } = NodeId.Null;

        /// <summary>
        /// The OperationalMode value, including status and timestamps.
        /// </summary>
        public DataValue OperationalMode { get; init; } = DataValue.Null;

        /// <summary>
        /// The OperationalMode variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId OperationalModeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The ProtectiveStop value, including status and timestamps.
        /// </summary>
        public DataValue ProtectiveStop { get; init; } = DataValue.Null;

        /// <summary>
        /// The ProtectiveStop variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId ProtectiveStopId { get; init; } = NodeId.Null;

        /// <summary>
        /// Emergency-stop function entries.
        /// </summary>
        public ArrayOf<SafetyFunctionSnapshot> EmergencyStopFunctions { get; init; } = [];

        /// <summary>
        /// Protective-stop function entries.
        /// </summary>
        public ArrayOf<SafetyFunctionSnapshot> ProtectiveStopFunctions { get; init; } = [];
    }
}
