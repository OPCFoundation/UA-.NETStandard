// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a GearType instance.
    /// </summary>
    public sealed record GearSnapshot
    {
        /// <summary>
        /// The gear identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The gear ratio expressed with the standard RationalNumber type.
        /// </summary>
        public RationalNumber? GearRatio { get; init; }

        /// <summary>
        /// The optional pitch value, including status and timestamps.
        /// </summary>
        public DataValue Pitch { get; init; } = DataValue.Null;

        /// <summary>
        /// The Pitch variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId PitchId { get; init; } = NodeId.Null;
    }
}
