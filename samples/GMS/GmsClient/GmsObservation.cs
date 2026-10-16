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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace GmsSample.Client
{
    /// <summary>
    /// Owns finite data, event, condition-refresh and typed intermediate-result observations.
    /// </summary>
    internal static class GmsObservation
    {
        /// <summary>
        /// Reuses bounded library collectors and verifies that their temporary subscriptions are removed.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Managed subscriptions or their cleanup are unavailable.
        /// </exception>
        internal static async Task RunAsync(
            GmsClient client, GmsDeviceEntry entry, GmsClientArguments options, CancellationToken cancellationToken)
        {
            if (!client.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw new InvalidOperationException("The managed GMS session has no subscription manager.");
            }
            int before = manager.Count;
            var bounds = new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(options.Seconds),
                MaximumNotifications = options.Count
            };
            try
            {
                MachineToolDataObservation data = await client.ObserveDataAsync(entry.NodeId, bounds, cancellationToken)
                    .ConfigureAwait(false);
                long bytes = 0;
                foreach (MachineToolObservedValue change in data.Changes)
                {
                    bytes += Measure(client, Variant.From(change.Value));
                    Console.WriteLine($"DATA node={change.NodeId} sequence={change.SequenceNumber}");
                    GmsReader.PrintValue("notification", change.Value);
                }
                Console.WriteLine(
                    $"OBSERVED_DATA {entry.BrowseName} count={data.Changes.Count} bytes={bytes} maximumBytes=1048576 " +
                    $"limitReached={data.LimitReached} dropped={data.DroppedNotifications}");
                if (options.Events)
                {
                    MachineToolEventObservation conditions = await client.ObserveConditionsAsync(
                        entry.NodeId, bounds, cancellationToken).ConfigureAwait(false);
                    PrintEvents(client, "CONDITIONS", conditions);
                    MachineToolEventObservation events = await client.ObserveEventsAsync(
                        entry.NodeId, bounds, cancellationToken).ConfigureAwait(false);
                    PrintEvents(client, "EVENTS", events);
                    if (options.Characteristic != null)
                    {
                        await ObserveIntermediateAsync(client, entry, options, manager, false, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (options.VerifyCleanup)
                {
                    await ObserveIntermediateAsync(client, entry, options, manager, true, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                Console.WriteLine(
                    $"OBSERVATION_CLEANUP before={before} after={manager.Count} connected={client.Session.Connected}");
            }
            if (manager.Count != before)
            {
                throw new InvalidOperationException("A GMS observation retained a temporary subscription.");
            }
        }

        /// <summary>
        /// Displays decoded native environmental warnings alongside unmodified event identities and raw fields.
        /// </summary>
        private static void PrintEvents(GmsClient client, string label, MachineToolEventObservation observation)
        {
            long bytes = 0;
            foreach (MachineToolObservedEvent notification in observation.Events)
            {
                Console.WriteLine(
                    $"EVENT id={notification.Event.EventId} type={notification.Event.EventType} " +
                    $"source={notification.Event.SourceNode} time={notification.Event.Time}");
                var fields = new Variant[notification.Fields.Count];
                for (int index = 0; index < fields.Length; index++)
                {
                    fields[index] = notification.Fields[index].Value;
                }
                bytes += Measure(client, Variant.From(ArrayOf.Wrapped(fields)));
                if (notification.Event is SensorWarningAlarmTypeRecord warning)
                {
                    Console.WriteLine(
                        $"SENSOR_WARNING condition={warning.ConditionId} input={warning.InputNode} " +
                        $"source={warning.SourceNode} error={warning.ErrorCode} retain={warning.Retain}; " +
                        "environmental measurement, not primary probe qualification.");
                    foreach (MachineToolEventField field in notification.Fields)
                    {
                        Console.WriteLine($"  WARNING_FIELD path={field.BrowsePath} raw={field.Value}");
                    }
                }
            }
            Console.WriteLine(
                $"OBSERVED_{label} count={observation.Events.Count} bytes={bytes} maximumBytes=1048576 " +
                $"limitReached={observation.LimitReached} dropped={observation.DroppedNotifications} " +
                $"refreshCompleted={observation.RefreshCompleted}");
            if (observation.Events.IsEmpty)
            {
                Console.WriteLine("No matching events in this bounded observation; not a hardware all-clear claim.");
            }
        }

        /// <summary>
        /// Distinguishes server-side subscription readiness from empty-window expiry and delivered cancellation.
        /// </summary>
        /// <exception cref="TimeoutException">The finite deadline expired before monitored-item creation.</exception>
        /// <exception cref="InvalidOperationException">
        /// The explicitly requested cancellation was not delivered.
        /// </exception>
        private static async Task ObserveIntermediateAsync(
            GmsClient client, GmsDeviceEntry entry, GmsClientArguments options, ISubscriptionManager manager,
            bool cancelOnReady, CancellationToken cancellationToken)
        {
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds));
            using var running = CancellationTokenSource.CreateLinkedTokenSource(window.Token, cancellationToken);
            var streaming = new StreamingSubscription(manager,
                new SubscriptionOptions { PublishingInterval = TimeSpan.FromMilliseconds(100) });
            bool ready = false;
            bool cancelled = false;
            int count = 0;
            await using (streaming.ConfigureAwait(false))
            {
                ArrayOf<QualifiedName> selection = options.Characteristic == null
                    ? []
                    : [new QualifiedName(options.Characteristic, entry.NodeId.NamespaceIndex)];
                GmsResultClient results = client.GetResults(entry.NodeId, GmsResultReader.Limits(options));
                try
                {
                    await foreach (GmsIntermediateResultNotification notification in
                        results.ObserveIntermediateResultsAsync(streaming, options.Count, OnReady,
                            selection, running.Token).ConfigureAwait(false))
                    {
                        count++;
                        Console.WriteLine(
                            $"INTERMEDIATE event={notification.Event.EventId} type={notification.Event.EventType} " +
                            $"program={notification.Event.ProgramName} time={notification.Event.Time}");
                        foreach (GmsCharacteristicEventValues characteristic in notification.Characteristics)
                        {
                            Console.WriteLine(
                                $"CHARACTERISTIC {characteristic.Name} id={characteristic.Identifier} " +
                                $"value={characteristic.ResultValue} units={characteristic.EngineeringUnits} " +
                                $"nominal={characteristic.Nominal} lower={characteristic.LowerTolerance} " +
                                $"upper={characteristic.UpperTolerance} " +
                                $"lowerForm={characteristic.LowerToleranceForm} " +
                                $"upperForm={characteristic.UpperToleranceForm} " +
                                $"class={characteristic.CharacteristicsClass} " +
                                $"evaluation={characteristic.Evaluation} valid={characteristic.IsValid} " +
                                $"formula={characteristic.Formula}");
                        }
                    }
                }
                catch (OperationCanceledException) when (running.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
                {
                    if (!ready)
                    {
                        throw new TimeoutException("Intermediate observation expired before server-side readiness.");
                    }
                    cancelled = cancelOnReady;
                    Console.WriteLine(cancelOnReady
                        ? "EXPECTED_OBSERVATION_CANCELLATION after monitored-item creation."
                        : "INTERMEDIATE_WINDOW_COMPLETE: only received notifications are reported.");
                }
                if (cancelOnReady && !cancelled)
                {
                    throw new InvalidOperationException("Expected observation cancellation was not delivered.");
                }
            }
            Console.WriteLine(
                $"INTERMEDIATE_COMPLETE ready={ready} count={count} maximum={options.Count} " +
                $"maximumMetadataBytes=131072 dropped={streaming.DroppedNotificationCount} " +
                "ownedSubscription=disposed eventFieldsHaveNoInventedDataValueQuality=true");

            ValueTask OnReady(CancellationToken _)
            {
                ready = true;
                Console.WriteLine($"INTERMEDIATE_READY system={entry.BrowseName} cancellationCheck={cancelOnReady}");
                if (cancelOnReady)
                {
                    running.Cancel();
                }
                return default;
            }
        }

        /// <summary>
        /// Reports actual encoded payload sizes within the shared collectors' one-mebibyte bound.
        /// </summary>
        private static int Measure(GmsClient client, in Variant value)
        {
            var limits = new GmsResultLimits { MaximumMetadataBytes = 1024 * 1024 };
            return limits.Measure(value, client.Session.MessageContext);
        }
    }
}
