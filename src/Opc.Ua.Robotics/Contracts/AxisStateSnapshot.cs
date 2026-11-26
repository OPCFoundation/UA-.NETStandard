// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Timestamped and status-bearing values for the standard AxisType telemetry.
    /// </summary>
    public sealed record AxisStateSnapshot
    {
        /// <summary>
        /// The ActualPosition value, including status and timestamps.
        /// </summary>
        public DataValue ActualPosition { get; init; } = DataValue.Null;

        /// <summary>
        /// The ActualPosition variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId ActualPositionId { get; init; } = NodeId.Null;

        /// <summary>
        /// The ActualSpeed value, including status and timestamps.
        /// </summary>
        public DataValue ActualSpeed { get; init; } = DataValue.Null;

        /// <summary>
        /// The ActualSpeed variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId ActualSpeedId { get; init; } = NodeId.Null;

        /// <summary>
        /// The ActualAcceleration value, including status and timestamps.
        /// </summary>
        public DataValue ActualAcceleration { get; init; } = DataValue.Null;

        /// <summary>
        /// The ActualAcceleration variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId ActualAccelerationId { get; init; } = NodeId.Null;
    }
}
