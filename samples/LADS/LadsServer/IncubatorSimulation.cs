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
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.LADS.Server;

namespace LadsSample.Server
{
    /// <summary>
    /// Moves temperature and shaker speed towards their targets, lets the CO2 level follow the
    /// temperature and runs the timer while a program is active.
    /// </summary>
    public sealed class IncubatorSimulation : BackgroundService
    {
        /// <summary>
        /// Creates the simulation.
        /// </summary>
        public IncubatorSimulation(ILadsDeviceRegistry registry, ILogger<IncubatorSimulation> logger)
        {
            m_registry = registry;
            m_logger = logger;
        }

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                ILadsDevice device = await m_registry
                    .WaitForDeviceAsync(ShakingIncubator.DeviceName, stoppingToken)
                    .ConfigureAwait(false);
                ILadsFunctionalUnit chamber = device.GetFunctionalUnit(ShakingIncubator.UnitName);
                IAnalogControlFunction temperature = chamber.GetFunction<IAnalogControlFunction>("Temperature");
                IAnalogControlFunction shaker = chamber.GetFunction<IAnalogControlFunction>("ShakerSpeed");
                IAnalogScalarSensorFunction co2 = chamber.GetFunction<IAnalogScalarSensorFunction>("CO2");
                ITimerFunction timer = chamber.GetFunction<ITimerFunction>("Timer");
                m_logger.Started(device.Name);
                string? runId = null;
                while (true)
                {
                    await Task.Delay(s_tick, stoppingToken).ConfigureAwait(false);
                    temperature.UpdateCurrentValue(Approach(temperature.CurrentValue, temperature.TargetValue, 2));
                    shaker.UpdateCurrentValue(Approach(shaker.CurrentValue, shaker.TargetValue, 50));
                    double co2Target = temperature.CurrentValue > 30 ? 5 : 0.04;
                    co2.UpdateValue(Math.Round(Approach(co2.SensorValue, co2Target, 0.5), 2));

                    LadsProgramRun? run = chamber.Programs?.ActiveRun;
                    if (run?.RunId != runId)
                    {
                        runId = run?.RunId;
                        timer.UpdateCurrentValue(0);
                    }
                    else if (run is not null)
                    {
                        timer.UpdateCurrentValue(timer.CurrentValue + s_tick.TotalSeconds);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        private static double Approach(double current, double target, double maxStep)
        {
            double delta = target - current;
            return Math.Abs(delta) <= maxStep ? target : current + (Math.Sign(delta) * maxStep);
        }

        private static readonly TimeSpan s_tick = TimeSpan.FromMilliseconds(500);
        private readonly ILadsDeviceRegistry m_registry;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated logging of the simulation.
    /// </summary>
    internal static partial class IncubatorSimulationLog
    {
        /// <summary>
        /// Reports that the simulation drives the device.
        /// </summary>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Simulating device {Device}.")]
        public static partial void Started(this ILogger logger, string device);
    }
}
