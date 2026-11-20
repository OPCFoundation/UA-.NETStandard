/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a MotionDeviceType instance.
    /// </summary>
    public sealed record MotionDeviceSnapshot
    {
        /// <summary>
        /// The motion-device identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The generated OPC 40010 motion-device category.
        /// </summary>
        public MotionDeviceCategoryEnumeration Category { get; init; }

        /// <summary>
        /// The SpeedOverride value, including status and timestamps.
        /// </summary>
        public DataValue SpeedOverride { get; init; } = DataValue.Null;

        /// <summary>
        /// The SpeedOverride variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId SpeedOverrideId { get; init; } = NodeId.Null;

        /// <summary>
        /// Axis instance NodeIds contained by the motion device.
        /// </summary>
        public ArrayOf<NodeId> AxisIds { get; init; } = [];

        /// <summary>
        /// Power-train instance NodeIds contained by the motion device.
        /// </summary>
        public ArrayOf<NodeId> PowerTrainIds { get; init; } = [];

        /// <summary>
        /// Additional DI component instance NodeIds contained by the motion device.
        /// </summary>
        public ArrayOf<NodeId> AdditionalComponentIds { get; init; } = [];

        /// <summary>
        /// The contained flange-load instance, or <see cref="NodeId.Null"/> when absent.
        /// </summary>
        public NodeId FlangeLoadId { get; init; } = NodeId.Null;
    }
}
