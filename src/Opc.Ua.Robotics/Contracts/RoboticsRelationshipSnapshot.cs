/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Categorized semantic relationships defined by the Robotics NodeSet.
    /// </summary>
    public sealed record RoboticsRelationshipSnapshot
    {
        /// <summary>
        /// Controller-to-motion-device and task-control-to-motion-device Controls relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> Controls { get; init; } = [];

        /// <summary>
        /// Axis-to-power-train Requires relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> Requires { get; init; } = [];

        /// <summary>
        /// Power-train-to-axis Moves relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> Moves { get; init; } = [];

        /// <summary>
        /// Motor-to-drive IsDrivenBy relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> IsDrivenBy { get; init; } = [];

        /// <summary>
        /// Power-train HasSlave relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> HasSlave { get; init; } = [];

        /// <summary>
        /// Symmetric IsConnectedTo relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> IsConnectedTo { get; init; } = [];

        /// <summary>
        /// Controller-to-safety-state HasSafetyStates relationships.
        /// </summary>
        public ArrayOf<RoboticsRelationshipEntry> HasSafetyStates { get; init; } = [];
    }
}
