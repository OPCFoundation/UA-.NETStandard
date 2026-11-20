/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua;
using Robotics.IntentEnabledRobot.Simulation;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// The cell's dimensions and furniture as solids the arm must not move through,
    /// expressed in the arm's base frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers mirror <c>Assets/Cell.usda</c>. The arm's base frame is on a riser at
    /// world z = 0.920 while the bench top is at z = 0.720, so the work surface is
    /// 0.200 m below the base-frame origin.
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
        /// Height of the work surface in the world frame.
        /// </summary>
        public const double BenchTopMetres = 0.720;

        /// <summary>
        /// Height of the robot's base-frame origin in the world frame.
        /// </summary>
        public const double RobotBaseHeightMetres = 0.920;

        /// <summary>
        /// Centre of the fixture in the world frame.
        /// </summary>
        public const double FixtureCentreX = BinPickingPartsCatalog.FixtureCentreX;

        /// <summary>
        /// Top face of the fixture plate in the world frame.
        /// </summary>
        public const double FixturePlateTopMetres = BenchTopMetres + 0.009;

        /// <summary>
        /// Top face of the fixture locating pegs in the world frame.
        /// </summary>
        public const double FixturePegTopMetres = FixturePlateTopMetres + 0.040;
        public const double FixturePegOffsetMetres = 0.050;

        /// <summary>
        /// Builds the collision model the arm's solver checks its configurations against.
        /// </summary>
        public static SimulatedCollisionModel CreateCollisionModel()
        {
            return new SimulatedCollisionModel(
                ArrayOf.Create(s_obstacles.AsSpan()),
                ArrayOf.Create(s_segmentRadii.AsSpan()));
        }

        /// <summary>
        /// Point pairs are J1->J2 (same shoulder origin), shoulder->elbow,
        /// elbow->wrist, and wrist->TCP.
        /// </summary>
        private static readonly double[] s_segmentRadii = [0.0, 0.047, 0.042, 0.018];

        /// <summary>
        /// Bench top: world z 0.650 to 0.720, which is 0.200 m below the robot base frame.
        /// This is the one that stops a forearm crossing the table: the old height test only
        /// sampled joint origins, which a link can straddle. Its clearance is zero because
        /// it is a surface, not an obstacle standing on one - a link may come down to it,
        /// and with the tool vertical this arm's wrist has to.
        /// Bin walls: world z 0.713 to 0.753, 6 mm thick, around a 0.28 x 0.24 tray centred
        /// on the Bin location. These are objects, so they keep the full link radius. The
        /// tray's inside is deliberately left open so the tool can still descend into it.
        /// </summary>
        private static readonly SimulatedObstacleBox[] s_obstacles =
        [
            new("Bench", 0.0, 0.0, 1.4000, 0.9000, -0.2700, -0.2000, Clearance: 0.0),
            new("BinWallN", BinPickingPartsCatalog.BinCentreX, 0.1170,
                0.2800, 0.0060, -0.2070, -0.1670),
            new("BinWallS", BinPickingPartsCatalog.BinCentreX, -0.1170,
                0.2800, 0.0060, -0.2070, -0.1670),
            new("BinWallE", BinPickingPartsCatalog.BinCentreX + 0.1370, 0.0000,
                0.0060, 0.2400, -0.2070, -0.1670),
            new("BinWallW", BinPickingPartsCatalog.BinCentreX - 0.1370, 0.0000,
                0.0060, 0.2400, -0.2070, -0.1670)
        ];
    }
}
