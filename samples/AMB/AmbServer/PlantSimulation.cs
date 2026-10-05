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
using Opc.Ua;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.AMB.Server.Maintenance;

namespace AmbSample
{
    /// <summary>
    /// Drives the plant once a second: the bearing temperature of the pump
    /// rises and falls, the self test of the sensor fails now and then, and
    /// the maintenance activities run through their cycle.
    /// </summary>
    internal sealed class PlantSimulation : BackgroundService
    {
        public PlantSimulation(Plant plant, ILogger<PlantSimulation> logger)
        {
            m_plant = plant;
            m_logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    if (m_plant.Pump == null || m_plant.Sensor == null)
                    {
                        // The node managers have not built the plant yet.
                        continue;
                    }
                    try
                    {
                        await AdvanceAsync(++m_tick, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
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

        private async ValueTask AdvanceAsync(long tick, CancellationToken ct)
        {
            // The bearing temperature alarm is active for 20 of every 60
            // seconds; its root cause is the sensor the pump relies on.
            IAssetHealth pumpHealth = m_plant.Pump!.Health!;
            if (pumpHealth.TryGetAlarm("BearingTemperature", out IAssetHealthAlarm? bearing))
            {
                long phase = tick % 60;
                if (phase == 20)
                {
                    await bearing.RaiseAsync(
                        AssetFaultSeverities.MinimumOf(AssetFaultSeverity.MinorRecoverableFault),
                        new LocalizedText("en", "The bearing temperature exceeds 80 °C."),
                        [AssetRootCauses.Of(m_plant.Sensor!.NodeId, new LocalizedText("en", "Low cooling flow"))],
                        ct).ConfigureAwait(false);
                }
                else if (phase == 40)
                {
                    await bearing.ClearAsync(new LocalizedText("en", "The bearing temperature is normal again."), ct)
                        .ConfigureAwait(false);
                }
            }

            // The self test of the sensor fails once every five minutes.
            if (m_plant.Sensor!.Health!.TryGetAlarm("SelfTest", out IAssetHealthAlarm? selfTest))
            {
                long phase = tick % 300;
                if (phase == 100)
                {
                    await selfTest.RaiseAsync(
                        AssetFaultSeverities.MinimumOf(AssetFaultSeverity.MaintenanceNeeded),
                        new LocalizedText("en", "The self test of the measuring tube failed."),
                        AssetRootCauses.Self,
                        ct).ConfigureAwait(false);
                }
                else if (phase == 130)
                {
                    await selfTest.ClearAsync(cancellationToken: ct).ConfigureAwait(false);
                }
            }

            // Every two minutes one maintenance activity moves on: planned,
            // executing, finished, and planned again a year later.
            if (tick % 120 == 0)
            {
                await AdvanceMaintenanceAsync(m_plant.Pump.Maintenance, ct).ConfigureAwait(false);
                await AdvanceMaintenanceAsync(m_plant.Press?.Maintenance, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask AdvanceMaintenanceAsync(IAssetMaintenance? maintenance, CancellationToken ct)
        {
            if (maintenance == null || maintenance.Activities.Count == 0)
            {
                return;
            }
            IMaintenanceActivity activity = maintenance.Activities[0];
            switch (activity.State)
            {
                case MaintenanceStateKind.Planned:
                    await activity.StartAsync(cancellationToken: ct).ConfigureAwait(false);
                    break;
                case MaintenanceStateKind.Executing:
                    await activity.FinishAsync(cancellationToken: ct).ConfigureAwait(false);
                    break;
                default:
                    await activity
                        .ReplanAsync(DateTime.UtcNow.AddYears(1), cancellationToken: ct)
                        .ConfigureAwait(false);
                    break;
            }
            m_logger.MaintenanceAdvanced(activity.Name, activity.State);
        }

        private readonly Plant m_plant;
        private readonly ILogger m_logger;
        private long m_tick;
    }

    internal static partial class PlantSimulationLog
    {
        [LoggerMessage(
            EventId = 10,
            Level = LogLevel.Warning,
            Message = "A simulation step failed.")]
        public static partial void SimulationTickFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 11,
            Level = LogLevel.Information,
            Message = "Maintenance activity {Name} is {State}.")]
        public static partial void MaintenanceAdvanced(this ILogger logger, string name, MaintenanceStateKind state);
    }
}
