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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Pumps;
using static PumpsSample.PumpDatasheet;
using BrowseNames = Opc.Ua.Pumps.BrowseNames;

namespace PumpsSample
{
    /// <summary>
    /// Runs the duty/standby pair once a second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One pump runs while the other stands by, and every
    /// <see cref="PumpDatasheet.ExchangeIntervalSeconds"/> seconds they swap:
    /// the time-based distribution OPC 40223 calls
    /// <c>ConcerningTimeDistribution</c>. The running pump's flow wanders
    /// around the rated point; pressure, power, efficiency and motor current
    /// follow it.
    /// </para>
    /// <para>
    /// Once a minute the running pump reports cavitation for a few seconds,
    /// so a client watching the supervision group sees a signal raise and
    /// clear.
    /// </para>
    /// </remarks>
    internal sealed class PumpSimulation : BackgroundService
    {
        public PumpSimulation(PumpFleet fleet, ILogger<PumpSimulation> logger)
        {
            m_fleet = fleet;
            m_logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    IReadOnlyList<PumpUnit> units = m_fleet.Pumps;
                    if (units.Count == 0)
                    {
                        // ConfigurePumps has not run yet.
                        continue;
                    }
                    try
                    {
                        Advance(units);
                    }
                    catch (Exception ex)
                    {
                        m_logger.SimulationTickFailed(ex);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutdown.
            }
        }

        private void Advance(IReadOnlyList<PumpUnit> units)
        {
            long tick = ++m_tick;
            int duty = (int)(tick / ExchangeIntervalSeconds % units.Count);
            for (int ii = 0; ii < units.Count; ii++)
            {
                Update(units[ii], running: ii == duty, tick);
            }
        }

        private void Update(PumpUnit unit, bool running, long tick)
        {
            bool known = m_running.TryGetValue(unit.Name, out bool wasRunning);
            m_running[unit.Name] = running;
            if (!known || wasRunning != running)
            {
                ChangeRole(unit, running, started: known && running);
            }

            double load = running ? 0.85 + (0.1 * Math.Sin(tick / 15.0)) : 0;

            unit.Analog(unit.Measurements, BrowseNames.MassFlow, RatedMassFlow * load);
            unit.Analog(
                unit.Measurements,
                BrowseNames.DifferentialPressure,
                running ? RatedDifferentialPressure * (1.2 - (0.2 * load * load)) : 0);
            unit.Analog(
                unit.Measurements,
                BrowseNames.Speed,
                running ? RatedSpeed * (0.97 + (0.01 * Math.Sin(tick / 7.0))) : 0);
            unit.Analog(
                unit.Measurements,
                BrowseNames.PumpPowerInput,
                running ? RatedPowerInput * Math.Pow(load, 0.9) : 0);
            unit.Analog(
                unit.Measurements,
                BrowseNames.PumpEfficiency,
                running ? BestEfficiency * (1 - (2 * (load - 1) * (load - 1))) : 0);

            // The bearing warms towards its running temperature and cools
            // towards ambient while standing by.
            double bearing = m_bearing.TryGetValue(unit.Name, out double last) ? last : 25;
            bearing += ((running ? BearingTemperature : 25) - bearing) * 0.05;
            m_bearing[unit.Name] = bearing;
            unit.Analog(unit.Measurements, BrowseNames.BearingTemperature, bearing);

            unit.Analog(unit.Motor, BrowseNames.MotorCurrent, running ? 21.5 * Math.Pow(load, 0.9) : 0);
            unit.Analog(unit.Motor, BrowseNames.MotorTemperature, bearing + 8);

            // Vibration rises during a cavitation episode, as it would on a
            // real pump; 2.8 mm/s is zone A/B of ISO 10816-3 for this class.
            bool cavitating = running && tick % 60 is >= 40 and < 46;
            unit.Analog(
                unit.Vibration,
                BrowseNames.OverallVibrationVelocityRMS,
                running ? (cavitating ? 7.1 : 2.8 + (0.2 * Math.Sin(tick / 5.0))) : 0.1);

            // Supervision: a short cavitation episode once a minute. Only the
            // transitions are written, as a real supervision would report them.
            bool cavitation = running && tick % 60 is >= 40 and < 46;
            if (cavitation != m_cavitation.Contains(unit.Name))
            {
                if (cavitation)
                {
                    m_cavitation.Add(unit.Name);
                    m_logger.CavitationRaised(unit.Name);
                }
                else
                {
                    m_cavitation.Remove(unit.Name);
                }
                unit.Discrete(unit.ProcessFluid, BrowseNames.Cavitation, cavitation);
            }

            if (running)
            {
                double hours = m_hours.TryGetValue(unit.Name, out double h) ? h : InitialOperatingHours;
                hours += 1.0 / 3600.0;
                m_hours[unit.Name] = hours;
                unit.Analog(unit.GeneralMaintenance, BrowseNames.OperatingTime, hours);
            }
        }

        /// <summary>
        /// Writes a duty/standby exchange: signals, multi-pump role and
        /// maintenance state change only here, not every second.
        /// </summary>
        private void ChangeRole(PumpUnit unit, bool running, bool started)
        {
            unit.Discrete(unit.Signals, BrowseNames.PumpOperation, running);
            unit.Discrete(unit.Signals, BrowseNames.StandBy, !running);
            unit.Value(
                unit.MultiPump,
                BrowseNames.PumpRole,
                Variant.From(running ? PumpRoleEnum.Master : PumpRoleEnum.Slave));
            unit.Value(
                unit.GeneralMaintenance,
                BrowseNames.StateOfTheItem,
                Variant.From(running
                    ? StateOfTheItemEnum.OperatingState
                    : StateOfTheItemEnum.StandByState));

            if (running)
            {
                unit.Value(unit.MultiPump, BrowseNames.ExchangeTime, Variant.From(DateTimeUtc.Now));
                m_logger.DutyExchanged(unit.Name);
            }
            if (started)
            {
                uint starts = m_starts.TryGetValue(unit.Name, out uint count) ? count + 1 : 1;
                m_starts[unit.Name] = starts;
                unit.Value(unit.Measurements, BrowseNames.NumberOfStarts, Variant.From(starts));
            }
        }

        private readonly PumpFleet m_fleet;
        private readonly ILogger<PumpSimulation> m_logger;
        private readonly Dictionary<string, bool> m_running = [];
        private readonly HashSet<string> m_cavitation = [];
        private readonly Dictionary<string, double> m_bearing = [];
        private readonly Dictionary<string, double> m_hours = [];
        private readonly Dictionary<string, uint> m_starts = [];
        private long m_tick;
    }

    internal static partial class PumpSimulationLog
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "{Pump} took over duty.")]
        public static partial void DutyExchanged(this ILogger logger, string pump);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "{Pump} reports cavitation.")]
        public static partial void CavitationRaised(this ILogger logger, string pump);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Error,
            Message = "A simulation tick failed.")]
        public static partial void SimulationTickFailed(this ILogger logger, Exception exception);
    }
}
