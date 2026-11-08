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
using Opc.Ua.Scales;
using Opc.Ua.Scales.Server.Runtime;

namespace ScalesSample
{
    /// <summary>
    /// Drives the sample equipment: packages cross the checkweigher and are
    /// accepted or rejected, the balance settles, the counting scale fills
    /// and trucks cross the weighbridge.
    /// </summary>
    internal sealed partial class ScaleSimulation : BackgroundService
    {
        public ScaleSimulation(PackingLine line, ILogger<ScaleSimulation> logger)
        {
            m_line = line;
            m_logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // The scales exist once ConfigureScales has run.
            await m_line.Ready.WaitAsync(stoppingToken).ConfigureAwait(false);
            var random = new Random(40200);
            int tick = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                tick++;
                await WeighPackageAsync(random, tick, stoppingToken).ConfigureAwait(false);
#pragma warning disable CA5394 // Simulated load noise, not security relevant
                m_line.Laboratory.PublishLoad(0.1 + (random.NextDouble() * 0.000004), stable: tick % 4 != 0);
#pragma warning restore CA5394
                double pieceLoad = 0.00498 * (tick % 200);
                m_line.PieceCounter.PublishLoad(pieceLoad);
                if (tick % 10 == 0 && pieceLoad > 0)
                {
                    ServiceResult registered = await m_line.PieceCounter.RegisterWeightAsync(stoppingToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(registered))
                    {
                        m_logger.LogPieceRegistrationRejected(registered.StatusCode);
                    }
                }
                await CrossWeighbridgeAsync(tick, stoppingToken).ConfigureAwait(false);
                await ReportItemsAsync(tick, stoppingToken).ConfigureAwait(false);
                ObserveRecipeConditions(tick);
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports equipment observations without issuing sorter, printer or hopper actuator commands.
        /// </summary>
        private async ValueTask ReportItemsAsync(int tick, CancellationToken cancellationToken)
        {
            DateTime timestamp = DateTime.UtcNow;
            ScaleItemReport Item(string product, double net) => new()
            {
                Sequence = (ulong)tick,
                ProductId = product,
                ItemId = FormattableString.Invariant($"ITEM-{tick}"),
                Weight = new ScaleReading
                {
                    Gross = net,
                    Net = net,
                    EngineeringUnits = ScaleUnits.Kilogram,
                    Timestamp = timestamp,
                    StatusCode = StatusCodes.Good,
                    Stable = true
                },
                Length = new ScaleItemMeasurement(200 + (tick % 20), PackingLine.Millimetre,
                    timestamp, StatusCodes.Good)
            };
            ServiceResult caught = await m_line.Catchweigher.Catchweigher!.ReportAsync(
                Item("ITEMS", 0.75 + ((tick % 3) * 0.25)), cancellationToken).ConfigureAwait(false);
            ServiceResult priced = await m_line.PriceLabeler.Catchweigher!.ReportAsync(
                Item("CHEESE", 0.35 + ((tick % 5) * 0.05)), cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(caught) || ServiceResult.IsBad(priced))
            {
                throw new ServiceResultException(ServiceResult.IsBad(caught) ? caught : priced);
            }
            if (tick % 10 == 0)
            {
                ServiceResult fill = await m_line.TotalizingHopper.TotalizingHopper!.ReportCompletedFillAsync(
                    Item("GRAIN", 100), cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(fill))
                {
                    throw new ServiceResultException(fill);
                }
            }
        }

        /// <summary>
        /// Supplies local condition observations and explicitly acknowledges the simulated operator intervention.
        /// </summary>
        private void ObserveRecipeConditions(int tick)
        {
            RecipeController recipes = m_line.ConditionBatching.Recipes!;
            if (recipes.State == RecipeRunState.Idle)
            {
                ServiceResult start = recipes.Start(m_line.ConditionRecipe.NodeId);
                if (ServiceResult.IsBad(start))
                {
                    throw new ServiceResultException(start);
                }
            }
            var analog = new DataValue(new Variant((double)(tick % 10)), StatusCodes.Good, DateTimeUtc.Now);
            var edge = new DataValue(new Variant(tick % 4 >= 2), StatusCodes.Good, DateTimeUtc.Now);
            recipes.ObserveConditionSource(1, analog);
            recipes.ObserveConditionSource(2, edge);
            foreach (RecipeElementState element in recipes.ConditionWaitsRequiringIntervention)
            {
                m_logger.LogIntervention(element.BrowseName.Name);
                ServiceResult completed = recipes.CompleteElement(element);
                if (ServiceResult.IsBad(completed))
                {
                    throw new ServiceResultException(completed);
                }
            }
        }

        /// <summary>
        /// Reports every simulated package's explicit disposition to scale and product statistics.
        /// </summary>
        private async ValueTask WeighPackageAsync(Random random, int tick, CancellationToken cancellationToken)
        {
            ScaleHandle checkweigher = m_line.Checkweigher;
#pragma warning disable CA5394 // Simulated load noise, not security relevant
            // 481 .. 515 g, so roughly one package in ten falls below T1.
            double weight = 0.5 + ((random.NextDouble() - 0.55) * 0.034);
#pragma warning restore CA5394
            // RegisterWeight would count the package into ProductionOutput as
            // well; a checkweigher counts accepted and rejected packages instead.
            checkweigher.PublishLoad(weight);

            // Nominal 500 g: packages below T1 (-15 g) are rejected, as OIML R 87 asks.
            if (weight < 0.485)
            {
                ServiceResult result = await checkweigher.Checkweigher!.RecordRejectedAsync(
                    weight, CheckweigherRejectReason.LowerToleranceLimit1, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
                checkweigher.Notifications.RaiseEvent(
                    ScaleNotificationId.BadPack,
                    new LocalizedText(FormattableString.Invariant($"Package of {weight:F4} kg rejected.")),
                    EventSeverity.Medium);
                m_logger.LogRejected(weight);
            }
            else
            {
                ServiceResult result = await checkweigher.Checkweigher!.RecordAcceptedAsync(
                    weight, belowLowerToleranceLimit1: weight < 0.5, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
            }

            // Every 20 s the label printer runs out of labels for a few seconds.
            bool outOfLabels = tick % 40 is >= 34;
            ScaleModuleHandle printer = checkweigher.Modules[1];
            printer.PublishPrinterStock(outOfLabels ? 0 : 100 - ((tick % 40) * 100.0 / 34), 100);
            checkweigher.Notifications.SetAlarm(
                ScaleNotificationId.PrinterFault,
                outOfLabels,
                new LocalizedText(outOfLabels ? "The labeler is out of labels." : "Labels restocked."));
        }

        /// <summary>
        /// Registers the simulated truck's inbound and completed outbound measurements.
        /// </summary>
        private async ValueTask CrossWeighbridgeAsync(int tick, CancellationToken cancellationToken)
        {
            // A truck arrives loaded, unloads and leaves empty every 60 ticks.
            int phase = tick % 60;
            ScaleHandle bridge = m_line.Weighbridge;
            bridge.PublishLoad(phase switch
            {
                < 10 => 0,
                < 20 => 38440,
                < 40 => 0,
                < 50 => 14220,
                _ => 0
            });
            ServiceResult result = phase switch
            {
                10 => await bridge.Vehicle!.InboundWeighingAsync("TRUCK-17", cancellationToken)
                    .ConfigureAwait(false),
                40 => await bridge.Vehicle!.OutboundWeighingAsync("TRUCK-17", cancellationToken)
                    .ConfigureAwait(false),
                _ => ServiceResult.Good
            };
            if (ServiceResult.IsBad(result))
            {
                m_logger.LogVehicleWeighingRejected(result.StatusCode);
            }
        }

        private readonly PackingLine m_line;
        private readonly ILogger<ScaleSimulation> m_logger;
    }

    /// <summary>
    /// Source-generated logging for the scale simulation.
    /// </summary>
    internal static partial class ScaleSimulationLog
    {
        /// <summary>
        /// Reports a rejected package.
        /// </summary>
        [LoggerMessage(
            EventId = global::Opc.Ua.ScalesServerEventIds.ScaleSimulation + 0,
            EventName = "LogRejected",
            Level = LogLevel.Information,
            Message = "Rejected a package of {Weight:F4} kg.")]
        public static partial void LogRejected(this ILogger logger, double weight);

        /// <summary>
        /// Reports a condition timeout before the simulated operator completes the element.
        /// </summary>
        [LoggerMessage(
            EventId = global::Opc.Ua.ScalesServerEventIds.ScaleSimulation + 1,
            Level = LogLevel.Warning,
            Message = "Recipe element {Element} timed out; the simulated operator explicitly completes it.")]
        public static partial void LogIntervention(this ILogger logger, string? element);

        /// <summary>
        /// Reports a rejected simulated vehicle measurement.
        /// </summary>
        [LoggerMessage(
            EventId = global::Opc.Ua.ScalesServerEventIds.ScaleSimulation + 2,
            Level = LogLevel.Warning,
            Message = "The simulated vehicle weighing was rejected with {StatusCode}.")]
        public static partial void LogVehicleWeighingRejected(this ILogger logger, StatusCode statusCode);

        /// <summary>
        /// Reports a rejected simulated piece-count registration.
        /// </summary>
        [LoggerMessage(
            EventId = global::Opc.Ua.ScalesServerEventIds.ScaleSimulation + 3,
            Level = LogLevel.Warning,
            Message = "The simulated piece-count registration was rejected with {StatusCode}.")]
        public static partial void LogPieceRegistrationRejected(this ILogger logger, StatusCode statusCode);
    }
}
