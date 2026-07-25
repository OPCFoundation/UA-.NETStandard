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
        Fixed,
        FigureEight,
        Circle,
        Shuttle
    }

    /// <summary>
    /// Per-robot motion configuration.
    /// </summary>
    public sealed class RobotMotionOptions
    {
        public RobotMotionMode Mode { get; set; } = RobotMotionMode.FigureEight;

        public double OriginX { get; set; }

        public double OriginY { get; set; }

        public double OriginZ { get; set; }

        public double AmplitudeX { get; set; } = 0.6;

        public double AmplitudeY { get; set; } = 0.35;

        public double Radius { get; set; } = 0.5;

        public double ShuttleDistance { get; set; } = 0.8;

        public double PeriodSeconds { get; set; } = 20.0;

        public double PhaseSeconds { get; set; }

        public double FixedHeadingDegrees { get; set; }

        public bool HeadingFollowsPath { get; set; } = true;
    }

    /// <summary>
    /// Mobility configuration for both robots.
    /// </summary>
    public sealed class RobotMobilityOptions
    {
        public RobotMobilityOptions()
        {
            R1.OriginX = -1.2;
            R2.OriginX = 1.2;
            R2.PhaseSeconds = 10.0;
        }

        public RobotMotionOptions R1 { get; set; } = new();

        public RobotMotionOptions R2 { get; set; } = new();

        public int UpdateIntervalMilliseconds { get; set; } = 200;
    }
}
