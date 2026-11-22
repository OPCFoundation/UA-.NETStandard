// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Focused server-independent read projection of a discovered Robotics topology.
    /// </summary>
    public sealed record RoboticsTopologySnapshot
    {
        /// <summary>
        /// Motion-device systems in the topology.
        /// </summary>
        public ArrayOf<MotionDeviceSystemSnapshot> Systems { get; init; } = [];

        /// <summary>
        /// Controllers in the topology.
        /// </summary>
        public ArrayOf<ControllerSnapshot> Controllers { get; init; } = [];

        /// <summary>
        /// Motion devices in the topology.
        /// </summary>
        public ArrayOf<MotionDeviceSnapshot> MotionDevices { get; init; } = [];

        /// <summary>
        /// Axes in the topology.
        /// </summary>
        public ArrayOf<AxisSnapshot> Axes { get; init; } = [];

        /// <summary>
        /// Loads in the topology.
        /// </summary>
        public ArrayOf<LoadSnapshot> Loads { get; init; } = [];

        /// <summary>
        /// Power trains in the topology.
        /// </summary>
        public ArrayOf<PowerTrainSnapshot> PowerTrains { get; init; } = [];

        /// <summary>
        /// Motors in the topology.
        /// </summary>
        public ArrayOf<MotorSnapshot> Motors { get; init; } = [];

        /// <summary>
        /// Gears in the topology.
        /// </summary>
        public ArrayOf<GearSnapshot> Gears { get; init; } = [];

        /// <summary>
        /// Drives in the topology.
        /// </summary>
        public ArrayOf<DriveSnapshot> Drives { get; init; } = [];

        /// <summary>
        /// Safety states in the topology.
        /// </summary>
        public ArrayOf<SafetyStateSnapshot> SafetyStates { get; init; } = [];

        /// <summary>
        /// Task controls in the topology.
        /// </summary>
        public ArrayOf<TaskControlSnapshot> TaskControls { get; init; } = [];

        /// <summary>
        /// Task modules in the topology.
        /// </summary>
        public ArrayOf<TaskModuleSnapshot> TaskModules { get; init; } = [];

        /// <summary>
        /// Semantic Robotics relationships observed in the topology.
        /// </summary>
        public RoboticsRelationshipSnapshot Relationships { get; init; } = new();
    }
}
