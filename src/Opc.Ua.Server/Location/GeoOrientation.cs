// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// The orientation of a located body, as the three rotation angles OPC
    /// 10000-211 carries alongside a global position.
    /// </summary>
    /// <param name="A">
    /// Rotation about the first axis, in degrees.
    /// </param>
    /// <param name="B">
    /// Rotation about the second axis, in degrees.
    /// </param>
    /// <param name="C">
    /// Rotation about the third axis, in degrees.
    /// </param>
    public readonly record struct GeoOrientation(
        double A,
        double B,
        double C);
}
