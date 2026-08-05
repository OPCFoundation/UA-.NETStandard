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
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server.Fluent;

namespace Robotics
{
    /// <summary>
    /// Sibling partial that animates the robot cell: a single manager-owned
    /// simulation tick sweeps every axis about its home pose (so the two arms move
    /// live and independently) and briefly asserts the cell emergency-stop so the
    /// safety beacon and per-robot warnings blink.
    /// </summary>
    public sealed partial class RobotCell
    {
        private long m_simulationTicks;

        // 20 Hz. The twin can never move more smoothly than the source publishes, so the
        // tick is chosen for animation rather than for a typical telemetry cadence.
        private const double TickSeconds = 0.05;
        private const int TickMilliseconds = 50;
        private const int EstopPeriodTicks = 600;
        private const int EstopActiveFromTick = 560;

        /// <summary>
        /// Key poses of one transfer cycle, in the axis order A1..A6 (degrees). The arm
        /// swings to the pick station, descends, closes on the part, lifts clear, traverses
        /// while rotating the part upright, places it, and retracts. Each entry moves from
        /// the previous pose over <c>MoveSeconds</c> and then holds for <c>DwellSeconds</c>,
        /// which is what makes the motion read as a real duty cycle instead of a sweep.
        /// </summary>
        private static readonly (double[] Pose, double MoveSeconds, double DwellSeconds)[] s_cycle =
        [
            ([0.0, -60.0, 75.0, 0.0, 45.0, 0.0], 1.6, 0.4),
            ([55.0, -45.0, 70.0, 0.0, 45.0, 0.0], 1.8, 0.2),
            ([55.0, -30.0, 62.0, 0.0, 58.0, 0.0], 1.0, 0.8),
            ([55.0, -55.0, 78.0, 0.0, 47.0, 0.0], 1.0, 0.2),
            ([-15.0, -55.0, 78.0, 0.0, 47.0, 90.0], 2.0, 0.2),
            ([-45.0, -45.0, 70.0, 0.0, 50.0, 90.0], 1.4, 0.2),
            ([-45.0, -28.0, 60.0, 0.0, 58.0, 90.0], 1.0, 0.8),
            ([-45.0, -60.0, 80.0, 0.0, 45.0, 0.0], 1.4, 0.3)
        ];

        private static readonly double s_cycleSeconds = ComputeCycleSeconds();

        partial void Configure(INodeManagerBuilder builder)
        {
            // Single manager-owned simulation tick advances every axis position and the
            // emergency-stop flag.
            builder.Simulation(TimeSpan.FromMilliseconds(TickMilliseconds))
                .OnTick((ctx, elapsed) => AdvanceSimulation());
        }

        private void AdvanceSimulation()
        {
            long t = Interlocked.Increment(ref m_simulationTicks);
            double time = t * TickSeconds;

            // Drive every axis from the shared pick-and-place cycle, offset per robot so
            // the two arms are not synchronised, and clamp to the axis' own limits.
            foreach (AxisRuntime ax in m_axes)
            {
                double target = EvaluateCycle(time + ax.PhaseSeconds, ax.Index);
                if (target < ax.Min)
                {
                    target = ax.Min;
                }
                if (target > ax.Max)
                {
                    target = ax.Max;
                }
                UpdateDouble(ax.Position, target);
            }

            // Emergency-stop pulses active (~2 s) roughly every 30 s so the safety
            // beacon (cell) and warning zones (robots) toggle live.
            bool estop = (t % EstopPeriodTicks) >= EstopActiveFromTick;
            UpdateBool(m_estopVar, estop);
        }

        /// <summary>
        /// Samples the cycle for one axis at a point in time, easing each move so the arm
        /// accelerates and settles rather than stepping between poses.
        /// </summary>
        private static double EvaluateCycle(double time, int axisIndex)
        {
            double position = time % s_cycleSeconds;
            if (position < 0.0)
            {
                position += s_cycleSeconds;
            }

            for (int i = 0; i < s_cycle.Length; i++)
            {
                (double[] pose, double move, double dwell) = s_cycle[i];
                if (position < move)
                {
                    double[] previous = s_cycle[(i + s_cycle.Length - 1) % s_cycle.Length].Pose;
                    double u = position / move;
                    double eased = u * u * (3.0 - (2.0 * u));
                    return previous[axisIndex] + ((pose[axisIndex] - previous[axisIndex]) * eased);
                }
                position -= move;
                if (position < dwell)
                {
                    return pose[axisIndex];
                }
                position -= dwell;
            }
            return s_cycle[^1].Pose[axisIndex];
        }

        private static double ComputeCycleSeconds()
        {
            double total = 0.0;
            foreach ((double[] _, double move, double dwell) in s_cycle)
            {
                total += move + dwell;
            }
            return total;
        }

        private void UpdateDouble(BaseDataVariableState? v, double value)
        {
            if (v == null)
            {
                return;
            }
            v.Value = value;
            v.Timestamp = DateTime.UtcNow;
            v.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        private void UpdateBool(BaseDataVariableState? v, bool value)
        {
            if (v == null)
            {
                return;
            }
            bool current = v.Value.TryGetValue(out bool b) && b;
            if (current != value)
            {
                v.Value = value;
                v.Timestamp = DateTime.UtcNow;
                v.ClearChangeMasks(SystemContext, includeChildren: false);
            }
        }
    }
}
