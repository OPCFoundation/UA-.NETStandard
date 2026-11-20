// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of an AxisType instance.
    /// </summary>
    public sealed record AxisSnapshot
    {
        /// <summary>
        /// The axis identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The generated OPC 40010 axis motion profile.
        /// </summary>
        public AxisMotionProfileEnumeration MotionProfile { get; init; }

        /// <summary>
        /// Engineering units and limits for axis telemetry.
        /// </summary>
        public AxisEngineeringOptions Engineering { get; init; } = new();

        /// <summary>
        /// Current axis telemetry values.
        /// </summary>
        public AxisStateSnapshot State { get; init; } = new();

        /// <summary>
        /// The contained additional-load instance, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId AdditionalLoadId { get; init; } = NodeId.Null;
    }
}
