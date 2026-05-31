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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Quickstarts
{
    /// <summary>
    /// Sample code demonstrating Part 9 Alarms and Conditions client
    /// API: <see cref="AlarmClient"/> for typed alarm operations and
    /// the streaming subscription API for alarm event monitoring.
    /// </summary>
    public class AlarmClientSample
    {
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Constructs a new sample.
        /// </summary>
        public AlarmClientSample(ILogger logger, ITelemetryContext telemetry)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Subscribes to alarm events from the Server notifier and prints
        /// a typed summary for each received alarm record.
        /// </summary>
        /// <param name="session">An active managed session.</param>
        /// <param name="durationMs">How long to listen for alarms.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task SubscribeToAlarmsAsync(
            ManagedSession session,
            int durationMs,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            Console.WriteLine("Subscribing to alarms via streaming subscription...");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(durationMs);

            IStreamingSubscription streaming = session.DefaultStreaming;

            try
            {
                await foreach (ConditionTypeRecord record in streaming
                    .SubscribeAlarmsAsync(ObjectIds.Server, ct: timeout.Token)
                    .ConfigureAwait(false))
                {
                    PrintAlarm(record);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Alarm subscription window elapsed.");
            }
        }

        /// <summary>
        /// Demonstrates acknowledging an alarm by EventId after waiting
        /// for an active record using the TakeUntilAsync helper.
        /// </summary>
        public async Task WaitForAndAcknowledgeAsync(
            ManagedSession session,
            NodeId conditionId,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            AlarmClient alarms = session.GetAlarmClient(m_telemetry);
            IStreamingSubscription streaming = session.DefaultStreaming;

            Console.WriteLine($"Waiting for next active record from {conditionId}...");

            await foreach (ConditionTypeRecord record in streaming
                .SubscribeAlarmsAsync(ObjectIds.Server, ct: ct)
                .TakeUntilAsync(r =>
                    r is AlarmConditionTypeRecord ar &&
                    ar.ConditionId == conditionId &&
                    ar.ActiveStateId == true, ct)
                .ConfigureAwait(false))
            {
                if (record is AlarmConditionTypeRecord alarm &&
                    alarm.ConditionId == conditionId &&
                    alarm.ActiveStateId == true)
                {
                    Console.WriteLine($"Acknowledging {alarm.ConditionName} EventId {alarm.EventId}");
                    try
                    {
                        await alarms.AcknowledgeAsync(
                            alarm.ConditionId,
                            alarm.EventId,
                            new LocalizedText("en", "Acknowledged by sample"),
                            ct).ConfigureAwait(false);
                        Console.WriteLine("Acknowledge succeeded.");
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex, "Acknowledge failed");
                    }
                }
            }
        }

        /// <summary>
        /// Demonstrates calling shelving methods on an alarm.
        /// </summary>
        public async Task ShelveAlarmAsync(
            ManagedSession session,
            NodeId conditionId,
            double shelvingTimeMs,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            AlarmClient alarms = session.GetAlarmClient(m_telemetry);

            Console.WriteLine($"Shelving {conditionId} for {shelvingTimeMs}ms...");
            await alarms.TimedShelveAsync(conditionId, shelvingTimeMs, ct).ConfigureAwait(false);
            Console.WriteLine("Alarm shelved.");
        }

        private static void PrintAlarm(ConditionTypeRecord record)
        {
            switch (record)
            {
                case AlarmConditionTypeRecord alarm:
                    Console.WriteLine(
                        $"[ALARM] {alarm.SourceName} sev={alarm.Severity} " +
                        $"active={alarm.ActiveStateId} acked={alarm.AckedStateId} " +
                        $"suppressed={alarm.SuppressedStateId} latched={alarm.LatchedStateId}");
                    break;
                case DialogConditionTypeRecord dialog:
                    Console.WriteLine(
                        $"[DIALOG] {dialog.SourceName} prompt='{dialog.Prompt}' " +
                        $"options={dialog.ResponseOptionSet?.Length ?? 0}");
                    break;
                case AcknowledgeableConditionTypeRecord ack:
                    Console.WriteLine(
                        $"[CONDITION] {ack.SourceName} sev={ack.Severity} acked={ack.AckedStateId}");
                    break;
                default:
                    Console.WriteLine(
                        $"[CONDITION] {record.SourceName} sev={record.Severity}");
                    break;
            }
        }
    }
}