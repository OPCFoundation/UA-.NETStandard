// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.LADS.Server;

namespace LadsSample.Server
{
    /// <summary>
    /// Simulates temperature, shaker speed and CO2, and runs the timer while a program is active.
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
                    runId = Advance(
                        temperature, shaker, co2, timer, chamber.Programs?.ActiveRun?.RunId, runId,
                        chamber.RunningState == LadsRunningStateValue.Execute);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        /// <summary>
        /// Advances the simulated measurements and returns the active run identifier.
        /// </summary>
        internal static string? Advance(
            IAnalogControlFunction temperature,
            IAnalogControlFunction shaker,
            IAnalogScalarSensorFunction co2,
            ITimerFunction timer,
            string? activeRunId,
            string? previousRunId,
            bool processing = true)
        {
            double temperatureTarget = temperature.State == LadsFunctionalStateValue.Running
                ? temperature.TargetValue
                : ShakingIncubator.AmbientTemperature;
            double shakerTarget = processing && shaker.State == LadsFunctionalStateValue.Running
                ? shaker.TargetValue
                : 0;
            temperature.UpdateCurrentValue(Approach(temperature.CurrentValue, temperatureTarget, 2));
            shaker.UpdateCurrentValue(Approach(shaker.CurrentValue, shakerTarget, 50));
            double co2Target = temperature.CurrentValue > 30 ? 5 : 0.04;
            co2.UpdateValue(Math.Round(Approach(co2.SensorValue, co2Target, 0.5), 2));

            if (activeRunId != previousRunId)
            {
                timer.UpdateCurrentValue(0);
            }
            else if (activeRunId is not null && processing)
            {
                timer.UpdateCurrentValue(timer.CurrentValue + s_tick.TotalMilliseconds);
            }
            return activeRunId;
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
