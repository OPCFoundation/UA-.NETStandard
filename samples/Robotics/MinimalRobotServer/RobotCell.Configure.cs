// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

            // Emergency-stop pulses active (~2 s) roughly every 30 s so the safety
            // beacon (cell) and warning zones (robots) toggle live. It now also halts the
            // choreography rather than only blinking a lamp.
            bool estop = (t % EstopPeriodTicks) >= EstopActiveFromTick;
            UpdateBool(m_estopVar, estop);

            CellChoreographer cell = m_choreographer;
            cell.EmergencyStop = estop;
            cell.Advance(TickSeconds);
            PublishTwinState(cell);

            // Publish the axis positions the choreography produced. They come from the arm
            // solver, so the arm genuinely reaches the slot it is working rather than
            // sweeping through a canned pose list.
            foreach (AxisRuntime ax in m_axes)
            {
                RobotAgent? agent = FindAgent(cell, ax.RobotId);
                if (agent == null || ax.Index >= agent.Axes.Length)
                {
                    continue;
                }
                double target = agent.Axes[ax.Index];
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
        }

        private static RobotAgent? FindAgent(CellChoreographer cell, string robotId)
        {
            foreach (RobotAgent agent in cell.Robots)
            {
                if (string.Equals(agent.Id, robotId, StringComparison.Ordinal))
                {
                    return agent;
                }
            }
            return null;
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
