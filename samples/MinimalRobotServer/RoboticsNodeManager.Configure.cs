/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
    public partial class RoboticsNodeManager
    {
        private long m_simulationTicks;

        private const double SweepAmplitudeDeg = 35.0;
        private const double SweepPeriodSeconds = 12.0;
        private const double TickSeconds = 0.2;

        partial void Configure(INodeManagerBuilder builder)
        {
            // Single manager-owned simulation tick advances every axis position and the
            // emergency-stop flag at 200 ms intervals.
            builder.Simulation(TimeSpan.FromMilliseconds(200))
                .OnTick((ctx, elapsed) => AdvanceSimulation());
        }

        private void AdvanceSimulation()
        {
            long t = Interlocked.Increment(ref m_simulationTicks);
            double time = t * TickSeconds;
            double w = 2.0 * Math.PI / SweepPeriodSeconds;

            // Sweep each joint sinusoidally about its home pose, clamped to its limits.
            // A per-axis sub-phase makes the whole arm articulate rather than move rigidly.
            foreach (AxisRuntime ax in m_axes)
            {
                double target = ax.Home
                    + (SweepAmplitudeDeg * Math.Sin((w * (time + ax.PhaseSeconds)) + (ax.Index * 0.6)));
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
            // beacon (cell) and warning halos (robots) toggle live.
            bool estop = (t % 150) >= 140;
            UpdateBool(m_estopVar, estop);
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
            bool current = v.Value.AsBoxedObject() is bool b && b;
            if (current != value)
            {
                v.Value = value;
                v.Timestamp = DateTime.UtcNow;
                v.ClearChangeMasks(SystemContext, includeChildren: false);
            }
        }
    }
}
