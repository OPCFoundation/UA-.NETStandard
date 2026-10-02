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
                WeighPackage(random, tick);
#pragma warning disable CA5394 // Simulated load noise, not security relevant
                m_line.Laboratory.PublishLoad(0.1 + (random.NextDouble() * 0.000004), stable: tick % 4 != 0);
#pragma warning restore CA5394
                m_line.PieceCounter.PublishLoad(0.00498 * (tick % 200));
                CrossWeighbridge(tick);
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
            }
        }

        private void WeighPackage(Random random, int tick)
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
                checkweigher.ProductionOutput?.RecordRejected(weight, CheckweigherRejectReason.LowerToleranceLimit1);
                checkweigher.Notifications.RaiseEvent(
                    ScaleNotificationId.BadPack,
                    new LocalizedText(FormattableString.Invariant($"Package of {weight:F4} kg rejected.")),
                    EventSeverity.Medium);
                LogRejected(weight);
            }
            else
            {
                checkweigher.ProductionOutput?.RecordAccepted(weight, belowLowerToleranceLimit1: weight < 0.5);
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

        private void CrossWeighbridge(int tick)
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
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Rejected a package of {Weight:F4} kg.")]
        private partial void LogRejected(double weight);

        private readonly PackingLine m_line;
        private readonly ILogger<ScaleSimulation> m_logger;
    }
}
