// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Engineering units and limits for the standard AxisType telemetry values.
    /// </summary>
    public sealed record AxisEngineeringOptions
    {
        /// <summary>
        /// The engineering unit for ActualPosition.
        /// </summary>
        public EUInformation? PositionUnit { get; init; }

        /// <summary>
        /// The engineering unit for ActualSpeed.
        /// </summary>
        public EUInformation? SpeedUnit { get; init; }

        /// <summary>
        /// The engineering unit for ActualAcceleration.
        /// </summary>
        public EUInformation? AccelerationUnit { get; init; }

        /// <summary>
        /// The advertised axis ranges.
        /// </summary>
        public AxisLimits Limits { get; init; } = new();
    }
}
