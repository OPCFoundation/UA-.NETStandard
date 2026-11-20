/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a MotionDeviceSystemType instance.
    /// </summary>
    public sealed record MotionDeviceSystemSnapshot
    {
        /// <summary>
        /// The system identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// Controller instance NodeIds contained by the system.
        /// </summary>
        public ArrayOf<NodeId> ControllerIds { get; init; } = [];

        /// <summary>
        /// Motion-device instance NodeIds contained by the system.
        /// </summary>
        public ArrayOf<NodeId> MotionDeviceIds { get; init; } = [];

        /// <summary>
        /// Safety-state instance NodeIds contained by the system.
        /// </summary>
        public ArrayOf<NodeId> SafetyStateIds { get; init; } = [];
    }
}
