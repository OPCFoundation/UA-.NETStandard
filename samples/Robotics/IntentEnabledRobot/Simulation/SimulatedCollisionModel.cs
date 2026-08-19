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

namespace Robotics.IntentEnabledRobot.Simulation
{
    /// <summary>
    /// An axis-aligned solid the arm must not pass through, in the arm's base frame.
    /// </summary>
    /// <param name="Name">
    /// What the box represents, for diagnostics.
    /// </param>
    /// <param name="CentreX">
    /// Centre of the box on X.
    /// </param>
    /// <param name="CentreY">
    /// Centre of the box on Y.
    /// </param>
    /// <param name="SizeX">
    /// Full extent on X.
    /// </param>
    /// <param name="SizeY">
    /// Full extent on Y.
    /// </param>
    /// <param name="MinZ">
    /// Underside of the box.
    /// </param>
    /// <param name="MaxZ">
    /// Top of the box.
    /// </param>
    public readonly record struct SimulatedObstacleBox(
        string Name,
        double CentreX,
        double CentreY,
        double SizeX,
        double SizeY,
        double MinZ,
        double MaxZ);

    /// <summary>
    /// Decides whether an arm configuration puts any part of the arm inside the cell's
    /// furniture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a test that compared each joint <i>origin</i> against a single
    /// horizontal plane. That test passes a configuration whose links pass clean through
    /// the bench, because the point where a link ends can sit above the plane while the
    /// length of the link between two joints dips below it - which is exactly what "the
    /// arm moves through the table" looks like. It also had nothing to say about the bin
    /// or the fixture, so the wrist could come to rest inside the bin and no part of the
    /// simulation objected.
    /// </para>
    /// <para>
    /// Each link is treated as a capsule between consecutive joint origins and tested
    /// against axis-aligned boxes. That is a coarse model - it is not a physics engine,
    /// there is no contact response and nothing here pushes back - but it answers the one
    /// question the arm needs answered before it moves: would this configuration put a
    /// link inside something solid.
    /// </para>
    /// </remarks>
    public sealed class SimulatedCollisionModel
    {
        /// <summary>
        /// Creates a collision model over a set of obstacles.
        /// </summary>
        /// <param name="obstacles">
        /// The solids the arm must stay out of, in the arm's base frame.
        /// </param>
        /// <param name="linkRadius">
        /// How thick the arm's links are treated as being, in metres.
        /// </param>
        /// <param name="toolRadius">
        /// How thick the tool beyond the flange is treated as being, in metres. It is
        /// slender next to a link, and unlike a link it is meant to approach surfaces
        /// closely - a gripper that cannot come within a link's thickness of the bench
        /// cannot pick anything up off it.
        /// </param>
        public SimulatedCollisionModel(
            ArrayOf<SimulatedObstacleBox> obstacles,
            double linkRadius,
            double toolRadius)
        {
            Obstacles = obstacles;
            LinkRadius = linkRadius;
            ToolRadius = toolRadius;
        }

        /// <summary>
        /// Gets the solids the arm must stay out of.
        /// </summary>
        public ArrayOf<SimulatedObstacleBox> Obstacles { get; }

        /// <summary>
        /// Gets the radius the arm's links are treated as having.
        /// </summary>
        public double LinkRadius { get; }

        /// <summary>
        /// Gets the radius the tool beyond the flange is treated as having.
        /// </summary>
        public double ToolRadius { get; }

        /// <summary>
        /// Gets whether a chain of joint origins, plus the tool point beyond the last one,
        /// stays clear of every obstacle.
        /// </summary>
        /// <param name="points">
        /// Successive points along the arm, each as three coordinates in the arm's base
        /// frame: the joint origins followed by the tool centre point.
        /// </param>
        /// <param name="hit">
        /// The first obstacle the arm intersects, when the result is <c>false</c>.
        /// </param>
        public bool IsClear(ReadOnlySpan<double> points, out string hit)
        {
            hit = string.Empty;
            if (Obstacles.Count == 0 || points.Length < 6)
            {
                return true;
            }
            ReadOnlySpan<SimulatedObstacleBox> boxes = Obstacles.Span;
            int lastSegment = points.Length - 6;
            for (int segment = 0; segment <= lastSegment; segment += 3)
            {
                double radius = segment == lastSegment ? ToolRadius : LinkRadius;
                double ax = points[segment];
                double ay = points[segment + 1];
                double az = points[segment + 2];
                double bx = points[segment + 3];
                double by = points[segment + 4];
                double bz = points[segment + 5];
                for (int ii = 0; ii < boxes.Length; ii++)
                {
                    if (IntersectsSegment(boxes[ii], ax, ay, az, bx, by, bz, radius))
                    {
                        hit = boxes[ii].Name;
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Gets whether a capsule of <paramref name="radius"/> around the segment from A to
        /// B reaches inside a box.
        /// </summary>
        /// <remarks>
        /// The segment is sampled rather than solved analytically. Sampling is enough here
        /// because the boxes are large next to the sample spacing, and it keeps the test
        /// short enough to run for every candidate solution and every step of a path.
        /// </remarks>
        private static bool IntersectsSegment(
            SimulatedObstacleBox box,
            double ax, double ay, double az,
            double bx, double by, double bz,
            double radius)
        {
            double minX = box.CentreX - (box.SizeX * 0.5) - radius;
            double maxX = box.CentreX + (box.SizeX * 0.5) + radius;
            double minY = box.CentreY - (box.SizeY * 0.5) - radius;
            double maxY = box.CentreY + (box.SizeY * 0.5) + radius;
            double minZ = box.MinZ - radius;
            double maxZ = box.MaxZ + radius;

            // A segment can cross a box while both ends sit outside it, so walk the
            // segment: testing only the endpoints is the mistake this class exists to fix.
            double length = Norm(bx - ax, by - ay, bz - az);
            int samples = Math.Max(2, (int)Math.Ceiling(length / SampleSpacingMetres) + 1);
            for (int ii = 0; ii <= samples; ii++)
            {
                double t = (double)ii / samples;
                double x = ax + ((bx - ax) * t);
                double y = ay + ((by - ay) * t);
                double z = az + ((bz - az) * t);
                if (x >= minX && x <= maxX && y >= minY && y <= maxY && z >= minZ && z <= maxZ)
                {
                    return true;
                }
            }
            return false;
        }

        private static double Norm(double x, double y, double z)
        {
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        // Fine enough to catch a link clipping the corner of a bin wall, coarse enough that
        // a whole configuration costs a few hundred comparisons.
        private const double SampleSpacingMetres = 0.02;
    }
}
