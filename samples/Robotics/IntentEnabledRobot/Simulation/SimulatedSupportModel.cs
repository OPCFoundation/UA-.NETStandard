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
    /// An axis-aligned solid a part can come to rest on: the bench, a fixture plate, a
    /// locating peg, or another part.
    /// </summary>
    /// <param name="Name">
    /// Identifies the solid, so a caller can tell what a part ended up standing on.
    /// </param>
    /// <param name="CentreX">
    /// Centre of the footprint along X, in the world frame.
    /// </param>
    /// <param name="CentreY">
    /// Centre of the footprint along Y, in the world frame.
    /// </param>
    /// <param name="SizeX">
    /// Full extent of the footprint along X.
    /// </param>
    /// <param name="SizeY">
    /// Full extent of the footprint along Y.
    /// </param>
    /// <param name="Top">
    /// World height of the upper face, which is what something resting on it stands on.
    /// </param>
    public readonly record struct SimulatedSupportSolid(
        string Name,
        double CentreX,
        double CentreY,
        double SizeX,
        double SizeY,
        double Top);

    /// <summary>
    /// Works out what a part comes to rest on, so a simulated cell can put a released part
    /// where gravity would leave it instead of where the tool happened to let go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a resting model, not a physics engine: it answers "what is the highest
    /// solid under this footprint" and nothing else. There is no toppling, no friction and
    /// no sliding, because none of those change the answer a pick-and-place cell needs -
    /// which is that a part released over a bench ends up on the bench, a part released
    /// over another part ends up on top of it, and neither ends up inside the other.
    /// </para>
    /// <para>
    /// A part released without such a model stays wherever the tool centre point was, which
    /// in this cell left it a measured 165 mm in the air.
    /// </para>
    /// </remarks>
    public sealed class SimulatedSupportModel
    {
        /// <summary>
        /// Initializes the model with the solids that never move.
        /// </summary>
        /// <param name="fixtures">
        /// Benches, plates, pegs and anything else a part can stand on but that a robot
        /// does not carry.
        /// </param>
        /// <param name="groundLevel">
        /// The height a part falls to when nothing at all is under it, which stops a
        /// footprint off the edge of every solid returning negative infinity.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="fixtures"/> is <c>null</c>.
        /// </exception>
        public SimulatedSupportModel(ArrayOf<SimulatedSupportSolid> fixtures, double groundLevel)
        {
            m_fixtures = fixtures;
            GroundLevel = groundLevel;
        }

        /// <summary>
        /// Gets the height a part falls to when nothing is under it.
        /// </summary>
        public double GroundLevel { get; }

        /// <summary>
        /// Gets the height of the highest solid under a footprint, which is the surface
        /// something placed there comes to rest on.
        /// </summary>
        /// <param name="centreX">
        /// Centre of the footprint along X, in the world frame.
        /// </param>
        /// <param name="centreY">
        /// Centre of the footprint along Y, in the world frame.
        /// </param>
        /// <param name="sizeX">
        /// Full extent of the footprint along X.
        /// </param>
        /// <param name="sizeY">
        /// Full extent of the footprint along Y.
        /// </param>
        /// <param name="movable">
        /// Solids that move, typically the other parts. Anything the caller wants ignored -
        /// the part being placed, or a part currently in the gripper - is simply left out.
        /// </param>
        /// <returns>
        /// The world height of the supporting surface.
        /// </returns>
        public double SupportHeight(
            double centreX,
            double centreY,
            double sizeX,
            double sizeY,
            ArrayOf<SimulatedSupportSolid> movable)
        {
            double top = GroundLevel;
            top = Highest(top, m_fixtures, centreX, centreY, sizeX, sizeY);
            top = Highest(top, movable, centreX, centreY, sizeX, sizeY);
            return top;
        }

        /// <summary>
        /// Gets the height a part's centre settles at when released over a footprint, which
        /// is the supporting surface plus half the part's own height.
        /// </summary>
        /// <param name="centreX">
        /// Centre of the part along X, in the world frame.
        /// </param>
        /// <param name="centreY">
        /// Centre of the part along Y, in the world frame.
        /// </param>
        /// <param name="sizeX">
        /// Full extent of the part along X.
        /// </param>
        /// <param name="sizeY">
        /// Full extent of the part along Y.
        /// </param>
        /// <param name="sizeZ">
        /// Full height of the part.
        /// </param>
        /// <param name="movable">
        /// The other parts, excluding the one being placed.
        /// </param>
        /// <returns>
        /// The world height of the part's centre once it is resting.
        /// </returns>
        public double RestingCentreHeight(
            double centreX,
            double centreY,
            double sizeX,
            double sizeY,
            double sizeZ,
            ArrayOf<SimulatedSupportSolid> movable)
        {
            return SupportHeight(centreX, centreY, sizeX, sizeY, movable) + (sizeZ * 0.5);
        }

        /// <summary>
        /// Gets the height a part's centre settles at, never allowing it to end up lower
        /// than its support even when a caller asks for that.
        /// </summary>
        /// <remarks>
        /// A caller that has its own idea of where the part should go - the height the tool
        /// let go at, say - passes it as <paramref name="requestedCentreZ"/>. A request
        /// above the resting height is honoured, because a part can be held higher than it
        /// would fall to; a request below it is not, because that is a part inside a solid.
        /// </remarks>
        /// <param name="centreX">
        /// Centre of the part along X, in the world frame.
        /// </param>
        /// <param name="centreY">
        /// Centre of the part along Y, in the world frame.
        /// </param>
        /// <param name="sizeX">
        /// Full extent of the part along X.
        /// </param>
        /// <param name="sizeY">
        /// Full extent of the part along Y.
        /// </param>
        /// <param name="sizeZ">
        /// Full height of the part.
        /// </param>
        /// <param name="requestedCentreZ">
        /// The height the caller would otherwise use.
        /// </param>
        /// <param name="movable">
        /// The other parts, excluding the one being placed.
        /// </param>
        /// <returns>
        /// The requested height, or the resting height when the request is below it.
        /// </returns>
        public double ClampAboveSupport(
            double centreX,
            double centreY,
            double sizeX,
            double sizeY,
            double sizeZ,
            double requestedCentreZ,
            ArrayOf<SimulatedSupportSolid> movable)
        {
            double resting = RestingCentreHeight(centreX, centreY, sizeX, sizeY, sizeZ, movable);
            return requestedCentreZ < resting ? resting : requestedCentreZ;
        }

        /// <summary>
        /// Gets a value indicating whether two footprints overlap, which is what decides
        /// whether one solid can hold another up.
        /// </summary>
        /// <param name="aCentreX">
        /// Centre of the first footprint along X.
        /// </param>
        /// <param name="aCentreY">
        /// Centre of the first footprint along Y.
        /// </param>
        /// <param name="aSizeX">
        /// Full extent of the first footprint along X.
        /// </param>
        /// <param name="aSizeY">
        /// Full extent of the first footprint along Y.
        /// </param>
        /// <param name="b">
        /// The second footprint.
        /// </param>
        /// <returns>
        /// <c>true</c> when the footprints overlap.
        /// </returns>
        public static bool FootprintsOverlap(
            double aCentreX,
            double aCentreY,
            double aSizeX,
            double aSizeY,
            in SimulatedSupportSolid b)
        {
            return Math.Abs(aCentreX - b.CentreX) < ((aSizeX + b.SizeX) * 0.5)
                && Math.Abs(aCentreY - b.CentreY) < ((aSizeY + b.SizeY) * 0.5);
        }

        private static double Highest(
            double top,
            ArrayOf<SimulatedSupportSolid> solids,
            double centreX,
            double centreY,
            double sizeX,
            double sizeY)
        {
            ReadOnlySpan<SimulatedSupportSolid> span = solids.Span;
            for (int ii = 0; ii < span.Length; ii++)
            {
                if (span[ii].Top > top && FootprintsOverlap(centreX, centreY, sizeX, sizeY, span[ii]))
                {
                    top = span[ii].Top;
                }
            }
            return top;
        }

        private readonly ArrayOf<SimulatedSupportSolid> m_fixtures;
    }
}
