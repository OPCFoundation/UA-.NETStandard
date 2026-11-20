/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a MotorType instance.
    /// </summary>
    public sealed record MotorSnapshot
    {
        /// <summary>
        /// The motor identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The MotorTemperature value, including status and timestamps.
        /// </summary>
        public DataValue MotorTemperature { get; init; } = DataValue.Null;

        /// <summary>
        /// The MotorTemperature variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId MotorTemperatureId { get; init; } = NodeId.Null;

        /// <summary>
        /// The engineering units and range of MotorTemperature.
        /// </summary>
        public RoboticsEngineeringValue MotorTemperatureEngineering { get; init; } = new();

        /// <summary>
        /// The optional BrakeReleased value, including status and timestamps.
        /// </summary>
        public DataValue BrakeReleased { get; init; } = DataValue.Null;

        /// <summary>
        /// The BrakeReleased variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId BrakeReleasedId { get; init; } = NodeId.Null;

        /// <summary>
        /// The optional EffectiveLoadRate value, including status and timestamps.
        /// </summary>
        public DataValue EffectiveLoadRate { get; init; } = DataValue.Null;

        /// <summary>
        /// The EffectiveLoadRate variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId EffectiveLoadRateId { get; init; } = NodeId.Null;
    }
}
