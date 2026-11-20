/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// The class of transform estimated when fitting ground control points.
    /// </summary>
    public enum GroundControlPointFitMode
    {
        /// <summary>
        /// A rigid body transform: proper rotation and translation only, no
        /// scaling. The determinant of the rotation is always +1.
        /// </summary>
        Rigid,

        /// <summary>
        /// A similarity transform: proper rotation, uniform scale and
        /// translation. The rotation is always a proper rotation (+1).
        /// </summary>
        Similarity,

        /// <summary>
        /// A general affine transform: an arbitrary invertible linear map plus
        /// translation. Reflections (negative determinant) are rejected unless
        /// explicitly allowed by the fit options.
        /// </summary>
        Affine
    }
}
