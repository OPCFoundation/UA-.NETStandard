/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoIdSample
{
    /// <summary>
    /// Moves a third pallet in and out of the antenna field and lets the reader scan on its own now and then.
    /// </summary>
    internal sealed class DockDoorSimulation : BackgroundService
    {
        public DockDoorSimulation(DockDoor dock, ILogger<DockDoorSimulation> logger)
        {
            m_dock = dock;
            m_logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            bool inRange = false;
            int tick = 0;
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                    tick++;
                    if (m_dock.Reader is null)
                    {
                        continue;
                    }
                    if (inRange)
                    {
                        await m_dock.Hardware.RemoveTagAsync(DockDoor.EpcOf(3), stoppingToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await m_dock.Hardware.PlaceTagAsync(DockDoor.Pallet(3), stoppingToken).ConfigureAwait(false);
                    }
                    inRange = !inRange;
                    m_logger.PalletMoved(inRange ? "in range" : "out of range");

                    // Option 3 of OPC 30010 6.1.1: a trigger button scans without any client calling a method.
                    if (tick % 3 == 0)
                    {
                        await m_dock.Hardware.TriggerScanAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        private readonly DockDoor m_dock;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated logging of the simulation.
    /// </summary>
    internal static partial class DockDoorSimulationLog
    {
        /// <summary>
        /// Reports where pallet 3 is.
        /// </summary>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Pallet 3 is {State}.")]
        public static partial void PalletMoved(this ILogger logger, string state);
    }
}
