/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Opc.Ua;
using Robotics.IntentEnabledRobot.Simulation;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// The cell's furniture as solids the arm must not move through, expressed in the arm's
    /// base frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers mirror <c>Assets/Cell.usda</c>. The arm's base frame has its origin on
    /// the bench top at world z = 0.829, so every world height here is that much lower.
    /// </para>
    /// <para>
    /// Only the bench and the bin walls are declared. The fixture plate and its locating
    /// pegs stand a few millimetres proud of the bench and are exactly where the tool has
    /// to work, so treating them as obstacles would refuse the placements the cell exists
    /// to perform without changing what the arm visibly does wrong.
    /// </para>
    /// </remarks>
    internal static class BinPickingCellGeometry
    {
        /// <summary>
        /// Builds the collision model the arm's solver checks its configurations against.
        /// </summary>
        public static SimulatedCollisionModel CreateCollisionModel()
        {
            return new SimulatedCollisionModel(
                ArrayOf.Create(s_obstacles.AsSpan()),
                LinkRadiusMetres,
                ToolRadiusMetres);
        }

        // The arm's links are roughly 60 mm across. A capsule of this radius keeps a link
        // from grazing a surface it should have travelled around.
        private const double LinkRadiusMetres = 0.030;

        // The gripper's fingers are slender, and unlike a link the tool is meant to come
        // close to surfaces: it has to reach a part lying on the bench.
        private const double ToolRadiusMetres = 0.008;

        // Bench top: world z 0.760 to 0.829, so it fills everything below the base frame
        // origin. This is the one that stops a forearm crossing the table: the old height
        // test only sampled joint origins, which a link can straddle. Its clearance is zero
        // because it is a surface, not an obstacle standing on one - a link may come down
        // to it, and with the tool vertical this arm's wrist has to.
        // Bin walls: world z 0.822 to 0.862, 6 mm thick, around a 0.28 x 0.24 tray centred
        // on the Bin location. These are objects, so they keep the full link radius. The
        // tray's inside is deliberately left open so the tool can still descend into it.
        private static readonly SimulatedObstacleBox[] s_obstacles =
        [
            new("Bench", 0.0, 0.0, 1.4000, 0.9000, -0.0690, 0.0000, Clearance: 0.0),
            new("BinWallN", 0.3800, 0.1170, 0.2800, 0.0060, -0.0070, 0.0330),
            new("BinWallS", 0.3800, -0.1170, 0.2800, 0.0060, -0.0070, 0.0330),
            new("BinWallE", 0.5170, 0.0000, 0.0060, 0.2400, -0.0070, 0.0330),
            new("BinWallW", 0.2430, 0.0000, 0.0060, 0.2400, -0.0070, 0.0330)
        ];
    }
}
