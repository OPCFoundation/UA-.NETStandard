// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a PowerTrainType instance.
    /// </summary>
    public sealed record PowerTrainSnapshot
    {
        /// <summary>
        /// The power-train identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// Motor instance NodeIds in this power train.
        /// </summary>
        public ArrayOf<NodeId> MotorIds { get; init; } = [];

        /// <summary>
        /// Gear instance NodeIds in this power train.
        /// </summary>
        public ArrayOf<NodeId> GearIds { get; init; } = [];
    }
}
