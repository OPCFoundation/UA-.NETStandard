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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Machinery.Server.StateMachines;

namespace MachinerySample
{
    /// <summary>
    /// Runs the press. One stroke every
    /// <see cref="PressDatasheet.CycleSeconds"/> seconds: the item state moves
    /// to <c>Executing</c>, the operation mode to <c>Processing</c>, and each
    /// finished stroke publishes an OPC 40001-101 result whose payload a client
    /// can download.
    /// </summary>
    /// <remarks>
    /// OPC 40001-1 declares no cause method on either state machine, so state
    /// only ever moves from here. A client observes it; it cannot request it.
    /// </remarks>
    internal sealed class PressSimulation : BackgroundService
    {
        public PressSimulation(ILogger<PressSimulation> logger)
        {
            m_logger = logger;
        }

        /// <summary>
        /// The ISA-95 Job Control V2 provider the OPC 40001-3 job verbs are
        /// bound to.
        /// </summary>
        public InMemoryIsa95JobControlProvider JobProvider { get; } = new();

        public void Attach(
            IMachineHandle<BaseObjectState> press,
            IProcessValueHandle oilTemperature,
            IMachineryNotificationPublisher notifications)
        {
            m_press = press ?? throw new ArgumentNullException(nameof(press));
            m_oilTemperature = oilTemperature ??
                throw new ArgumentNullException(nameof(oilTemperature));
            m_notifications = notifications ??
                throw new ArgumentNullException(nameof(notifications));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            TimeSpan cycle = TimeSpan.FromSeconds(PressDatasheet.CycleSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(cycle, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                IMachineHandle<BaseObjectState>? press = m_press;
                if (press == null)
                {
                    continue;
                }

                try
                {
                    await RunStrokeAsync(press, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    m_logger.PressStrokeFailed(ex);
                }
            }
        }

        private async ValueTask RunStrokeAsync(
            IMachineHandle<BaseObjectState> press,
            CancellationToken cancellationToken)
        {
            m_cycle++;

            if (press.OperationMode != null)
            {
                await press.OperationMode
                    .SetModeAsync(MachineryOperationModeValue.Processing, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (press.ItemState != null)
            {
                await press.ItemState
                    .SetStateAsync(MachineryItemStateValue.Executing, cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(
                    TimeSpan.FromSeconds(PressDatasheet.CycleSeconds / 2),
                    cancellationToken)
                .ConfigureAwait(false);

            if (press.ItemState != null)
            {
                await press.ItemState
                    .SetStateAsync(MachineryItemStateValue.NotExecuting, cancellationToken)
                    .ConfigureAwait(false);
            }

            // The oil warms while pressing and cools between strokes. Writing
            // it through the handle is what drives the percentage value and
            // both alarms — crossing a limit reports an alarm event without
            // the simulation having to know the alarm exists.
            await m_oilTemperature!
                .SetValueAsync(OilTemperature(m_cycle), cancellationToken)
                .ConfigureAwait(false);

            if (press.Results != null)
            {
                await press.Results
                    .PublishAsync(CreateResult(m_cycle), cancellationToken)
                    .ConfigureAwait(false);
            }

            await PublishStrokeNotificationAsync(cancellationToken).ConfigureAwait(false);
            m_logger.PressStrokeCompleted(m_cycle);
        }

        /// <summary>
        /// Reports a stroke event on the machine's OPC 40001-1
        /// <c>Notifications</c> add-in.
        /// </summary>
        /// <remarks>
        /// OPC 40001-1 says where a machine publishes its events but not what
        /// they are, so the sample uses the stock <c>SystemEventType</c> —
        /// concrete, and therefore something a client can filter for.
        /// </remarks>
        private ValueTask PublishStrokeNotificationAsync(CancellationToken cancellationToken)
        {
            IMachineryNotificationPublisher notifications = m_notifications!;
            ISystemContext context = notifications.Context;
            var notification = new SystemEventState(null);
            notification.Initialize(
                context,
                notifications.Notifier,
                EventSeverity.Low,
                new LocalizedText(
                    FormattableString.Invariant($"Press stroke {m_cycle} completed.")));
            notification.TypeDefinitionId = Opc.Ua.ObjectTypeIds.SystemEventType;
            notification.EventType!.Value = Opc.Ua.ObjectTypeIds.SystemEventType;
            return notifications.PublishAsync(notification, cancellationToken);
        }

        /// <summary>
        /// A slow triangular ramp that walks the oil temperature across the
        /// datasheet's high limit every few dozen strokes, so a client
        /// watching the press sees the limit alarm activate and clear.
        /// </summary>
        private static double OilTemperature(long cycle)
        {
            const double amplitude =
                PressDatasheet.OilTemperatureHighHighLimit -
                PressDatasheet.OilTemperatureSetpoint + 5.0;
            return PressDatasheet.OilTemperatureSetpoint +
                (amplitude * Math.Sin(cycle / 6.0));
        }

        private static MachineryResult CreateResult(long cycle)
        {
            string resultId = FormattableString.Invariant($"stroke-{cycle:D6}");
            double peakForce = PressDatasheet.NominalForceKiloNewton *
                (0.92 + (0.05 * Math.Sin(cycle / 7.0)));

            var metaData = new ResultMetaDataType
            {
                ResultId = resultId,
                CreationTime = DateTimeUtc.Now,
                ResultEvaluation = ResultEvaluationEnum.OK,
                EncodingMask = (uint)(
                    ResultMetaDataTypeFields.CreationTime |
                    ResultMetaDataTypeFields.ResultEvaluation)
            };

            var data = new ResultDataType
            {
                ResultMetaData = metaData,
                ResultContent = new[] { Variant.From(peakForce) }.ToArrayOf()
            };

            string payload = FormattableString.Invariant(
                $"resultId,{resultId}\npeakForceKN,{peakForce:F1}\n");
            return new MachineryResult(
                data,
                new ByteString(Encoding.UTF8.GetBytes(payload)),
                "text/csv");
        }

        private readonly ILogger<PressSimulation> m_logger;
        private IMachineHandle<BaseObjectState>? m_press;
        private IProcessValueHandle? m_oilTemperature;
        private IMachineryNotificationPublisher? m_notifications;
        private long m_cycle;
    }

    internal static partial class PressSimulationLog
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Press stroke {Cycle} completed; result published.")]
        public static partial void PressStrokeCompleted(this ILogger logger, long cycle);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Error,
            Message = "A press stroke failed.")]
        public static partial void PressStrokeFailed(this ILogger logger, Exception exception);
    }
}
