/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// Angle unit conversion helpers. All internal trigonometry works in
    /// radians; these helpers translate to and from the caller supplied
    /// <see cref="AngleUnit"/>.
    /// </summary>
    internal static class AngleMath
    {
        private const double kDegreesToRadians = Math.PI / 180.0;
        private const double kRadiansToDegrees = 180.0 / Math.PI;

        /// <summary>
        /// Converts a value expressed in <paramref name="unit"/> to radians.
        /// </summary>
        public static double ToRadians(double value, AngleUnit unit)
        {
            return unit == AngleUnit.Degrees ? value * kDegreesToRadians : value;
        }

        /// <summary>
        /// Converts a value expressed in radians to <paramref name="unit"/>.
        /// </summary>
        public static double FromRadians(double radians, AngleUnit unit)
        {
            return unit == AngleUnit.Degrees ? radians * kRadiansToDegrees : radians;
        }
    }
}
