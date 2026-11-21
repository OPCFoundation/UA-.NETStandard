// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// Explicit unit of angular measure. The positioning APIs never guess the
    /// unit of an angle: every method that consumes or produces angles requires
    /// the caller to state whether the values are expressed in radians or in
    /// degrees.
    /// </summary>
    public enum AngleUnit
    {
        /// <summary>
        /// Angles are expressed in radians.
        /// </summary>
        Radians,

        /// <summary>
        /// Angles are expressed in degrees.
        /// </summary>
        Degrees
    }
}
