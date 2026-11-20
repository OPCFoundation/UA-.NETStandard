// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a LoadType instance.
    /// </summary>
    public sealed record LoadSnapshot
    {
        /// <summary>
        /// The load instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The load mass, including status and timestamps.
        /// </summary>
        public DataValue Mass { get; init; } = DataValue.Null;

        /// <summary>
        /// The Mass variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId MassId { get; init; } = NodeId.Null;

        /// <summary>
        /// Engineering metadata for the load mass.
        /// </summary>
        public RoboticsEngineeringValue MassEngineering { get; init; } = new();

        /// <summary>
        /// The load center of mass, including status and timestamps.
        /// </summary>
        public DataValue CenterOfMass { get; init; } = DataValue.Null;

        /// <summary>
        /// The load inertia, including status and timestamps.
        /// </summary>
        public DataValue Inertia { get; init; } = DataValue.Null;
    }
}
