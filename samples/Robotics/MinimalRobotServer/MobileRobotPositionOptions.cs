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

namespace Robotics
{
    /// <summary>
    /// Supported deterministic motion styles for a robot mount.
    /// </summary>
    public enum RobotMotionMode
    {
        /// <summary>
        /// The robot stays at its origin and keeps a fixed heading.
        /// </summary>
        Fixed,

        /// <summary>
        /// The robot traces a lemniscate around its origin.
        /// </summary>
        FigureEight,

        /// <summary>
        /// The robot traces a circle around its origin.
        /// </summary>
        Circle,

        /// <summary>
        /// The robot travels back and forth along the X axis.
        /// </summary>
        Shuttle
    }

    /// <summary>
    /// Per-robot motion configuration.
    /// </summary>
    public sealed class RobotMotionOptions
    {
        /// <summary>
        /// The motion style the robot follows.
        /// </summary>
        public RobotMotionMode Mode { get; set; } = RobotMotionMode.FigureEight;

        /// <summary>
        /// The X coordinate the path is centred on, in metres.
        /// </summary>
        public double OriginX { get; set; }

        /// <summary>
        /// The Y coordinate the path is centred on, in metres.
        /// </summary>
        public double OriginY { get; set; }

        /// <summary>
        /// The Z coordinate the path is centred on, in metres.
        /// </summary>
        public double OriginZ { get; set; }

        /// <summary>
        /// Half the width of the <see cref="RobotMotionMode.FigureEight"/>
        /// path, in metres.
        /// </summary>
        public double AmplitudeX { get; set; } = 0.6;

        /// <summary>
        /// Half the height of the <see cref="RobotMotionMode.FigureEight"/>
        /// path, in metres.
        /// </summary>
        public double AmplitudeY { get; set; } = 0.35;

        /// <summary>
        /// The radius of the <see cref="RobotMotionMode.Circle"/> path, in
        /// metres.
        /// </summary>
        public double Radius { get; set; } = 0.5;

        /// <summary>
        /// The travel distance of the <see cref="RobotMotionMode.Shuttle"/>
        /// path, in metres.
        /// </summary>
        public double ShuttleDistance { get; set; } = 0.8;

        /// <summary>
        /// The time the robot takes to complete one full path, in seconds.
        /// </summary>
        public double PeriodSeconds { get; set; } = 20.0;

        /// <summary>
        /// The offset into the path the robot starts from, in seconds. Lets
        /// two robots sharing a path stay apart.
        /// </summary>
        public double PhaseSeconds { get; set; }

        /// <summary>
        /// The heading held when <see cref="HeadingFollowsPath"/> is
        /// <c>false</c>, in degrees.
        /// </summary>
        public double FixedHeadingDegrees { get; set; }

        /// <summary>
        /// Whether the heading is derived from the direction of travel rather
        /// than from <see cref="FixedHeadingDegrees"/>.
        /// </summary>
        public bool HeadingFollowsPath { get; set; } = true;
    }

    /// <summary>
    /// Position simulation configuration for both mobile robots.
    /// </summary>
    public sealed class MobileRobotPositionOptions
    {
        /// <summary>
        /// Creates the options with the two robots placed either side of the
        /// origin and offset in phase so they do not overlap.
        /// </summary>
        public MobileRobotPositionOptions()
        {
            R1.OriginX = -1.2;
            R2.OriginX = 1.2;
            R2.PhaseSeconds = 10.0;
        }

        /// <summary>
        /// Motion configuration for the first robot.
        /// </summary>
        public RobotMotionOptions R1 { get; set; } = new();

        /// <summary>
        /// Motion configuration for the second robot.
        /// </summary>
        public RobotMotionOptions R2 { get; set; } = new();

        /// <summary>
        /// How often a new position is published, in milliseconds.
        /// </summary>
        /// <remarks>
        /// This matches the cell's simulation tick on purpose. Publishing the platform
        /// slower than the arm and the workpieces makes the robot lag its own gripper: at
        /// 200 ms and cruise speed the platform trails by 170 mm, and a carried part - which
        /// rides the tool centre point - visibly floats out in front of the jaws.
        /// </remarks>
        public int UpdateIntervalMilliseconds { get; set; } = 50;
    }
}
