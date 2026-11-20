// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Engineering metadata for a measured or commanded scalar value.
    /// </summary>
    public sealed record RoboticsEngineeringValue
    {
        /// <summary>
        /// The engineering unit advertised by the variable.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The supported or advertised engineering range.
        /// </summary>
        public Range? Range { get; init; }
    }
}
