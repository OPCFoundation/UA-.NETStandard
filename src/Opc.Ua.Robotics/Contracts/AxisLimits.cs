/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Engineering limits for the standard AxisType telemetry values.
    /// </summary>
    public sealed record AxisLimits
    {
        /// <summary>
        /// The supported position range.
        /// </summary>
        public Range? Position { get; init; }

        /// <summary>
        /// The supported speed range.
        /// </summary>
        public Range? Speed { get; init; }

        /// <summary>
        /// The supported acceleration range.
        /// </summary>
        public Range? Acceleration { get; init; }
    }
}
