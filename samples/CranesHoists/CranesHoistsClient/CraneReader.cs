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
using Opc.Ua;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Client;

namespace CranesHoistsSample.Client
{
    /// <summary>
    /// Exercises typed, subtype-aware reads and owned, bounded subscriptions without raw service plumbing.
    /// </summary>
    internal static class CraneReader
    {
        /// <summary>
        /// Reads the selected native systems, then optionally observes and explicitly requests a limit.
        /// </summary>
        /// <exception cref="InvalidOperationException">A required system, axis or proxy is unavailable.</exception>
        internal static async Task RunAsync(
            CranesHoistsClient client,
            CraneClientOptions options,
            CancellationToken cancellationToken)
        {
            ArrayOf<CraneEntry> systems = await client.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"DISCOVERED {systems.Count} crane systems (vendor subtypes included).");
            CraneSystemSnapshot? selected = null;
            int matches = 0;
            for (int i = 0; i < systems.Count; i++)
            {
                CraneEntry system = systems[i];
                if (options.SystemName != null &&
                    !string.Equals(system.BrowseName.Name, options.SystemName, StringComparison.Ordinal))
                {
                    continue;
                }
                CraneSystemSnapshot snapshot = await client.ReadSystemAsync(system.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                selected ??= snapshot;
                matches++;
                PrintElement(snapshot.System);
                foreach (CraneElementSnapshot element in snapshot.Elements)
                {
                    PrintElement(element);
                }
            }
            if (selected == null || (options.SystemName != null && matches != 1))
            {
                throw new InvalidOperationException("Expected an unambiguous selected crane system.");
            }
            CraneElementSnapshot? axis = null;
            var monitored = new List<NodeId>();
            foreach (CraneElementSnapshot element in selected.Elements)
            {
                if (element.Entry.Kind == CraneElementKind.Axis &&
                    string.Equals(element.Entry.BrowseName.Name, options.AxisName, StringComparison.Ordinal))
                {
                    if (axis != null)
                    {
                        throw new InvalidOperationException("The selected axis browse name is ambiguous.");
                    }
                    axis = element;
                }
                foreach (CraneSignalValue signal in element.Signals)
                {
                    if (signal.Signal == CraneSignal.MotorTemperature && !signal.NodeId.IsNull && monitored.Count < 4)
                    {
                        monitored.Add(signal.NodeId);
                    }
                }
            }
            if (axis != null)
            {
                _ = await client.Axis(axis.Entry.NodeId)
                    .GetParameterSetAsync(client.Telemetry, cancellationToken).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The generated axis proxy cannot resolve its ParameterSet.");
                Console.WriteLine("GENERATED_PROXY: native axis ParameterSet resolved.");
                CraneElementSnapshot reread = await client.ReadElementAsync(axis.Entry.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine($"READ_ELEMENT {reread.Entry.NodeId} signals={reread.Signals.Count}");
            }
            if (options.RequestedLimit.HasValue)
            {
                if (axis == null)
                {
                    throw new InvalidOperationException("The explicitly selected axis is not present.");
                }
                await RequestLimitAsync(client, axis, options, cancellationToken).ConfigureAwait(false);
            }
            if (options.Seconds > 0)
            {
                await ObserveAsync(client, monitored.ToArrayOf(), options, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Preserves native identities, optional absence, access flags, raw values, nameplates and references.
        /// </summary>
        private static void PrintElement(CraneElementSnapshot element)
        {
            Console.WriteLine(
                $"ELEMENT {element.Entry.Kind} {element.Entry.BrowseName} " +
                $"node={element.Entry.NodeId} type={element.Entry.TypeDefinition}");
            if (element.Identification is CraneIdentificationSnapshot nameplate)
            {
                Console.WriteLine($"  NAMEPLATE {nameplate.NodeId}");
                PrintValue("Manufacturer", nameplate.Manufacturer);
                PrintValue("SerialNumber", nameplate.SerialNumber);
                PrintValue("ProductInstanceUri", nameplate.ProductInstanceUri);
                PrintValue("Model", nameplate.Model);
                PrintValue("ProductCode", nameplate.ProductCode);
            }
            foreach (CraneSignalValue signal in element.Signals)
            {
                if (signal.NodeId.IsNull)
                {
                    Console.WriteLine($"  {signal.Signal}: absent (not selected), not a Good measurement.");
                    continue;
                }
                Console.WriteLine($"  SIGNAL {signal.Signal} node={signal.NodeId} writable={signal.Writable}");
                PrintValue("reading", signal.Value);
            }
            foreach (EnumValueType function in element.AxisFunctionValues)
            {
                Console.WriteLine($"  AXIS_FUNCTION {function.Value}: {function.DisplayName}");
            }
            foreach (CraneRelationship relationship in element.Relationships)
            {
                Console.WriteLine(
                    $"  RELATION source={relationship.Source} type={relationship.ReferenceType} " +
                    $"target={relationship.Target}");
            }
        }

        /// <summary>
        /// Displays the original value, quality and acquisition time without unboxing Variant.
        /// </summary>
        private static void PrintValue(string name, in DataValue value)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"    {name}: raw={value.WrappedValue} status={value.StatusCode}"));
            Console.WriteLine(FormattableString.Invariant(
                $"    source={value.SourceTimestamp:O} server={value.ServerTimestamp:O}"));
        }

        /// <summary>
        /// Sends only the standard plus-direction setpoint request and rereads requested versus active state.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sample request/refusal invariants failed.</exception>
        private static async Task RequestLimitAsync(
            CranesHoistsClient client,
            CraneElementSnapshot axis,
            CraneClientOptions options,
            CancellationToken cancellationToken)
        {
            const CraneSignal requestSignal = CraneSignal.SpeedLimitDirPlusSetpoint;
            const CraneSignal activeSignal = CraneSignal.SpeedLimitDirPlus;
            CraneSignalValue beforeRequest = FindSignal(axis, requestSignal);
            CraneSignalValue beforeActive = FindSignal(axis, activeSignal);
            PrintValue("requested before", beforeRequest.Value);
            PrintValue("active before", beforeActive.Value);
            double requested = options.RequestedLimit!.Value;
            StatusCode accepted = await client.ConfigureLimitAsync(
                axis.Entry.NodeId, requestSignal, Variant.From(requested), cancellationToken).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"LIMIT_REQUEST accepted={accepted} requested={requested}; this is NOT a hardware-effect report."));
            CraneElementSnapshot after = await client.ReadElementAsync(axis.Entry.NodeId, cancellationToken)
                .ConfigureAwait(false);
            CraneSignalValue afterRequest = FindSignal(after, requestSignal);
            CraneSignalValue afterActive = FindSignal(after, activeSignal);
            PrintValue("requested after", afterRequest.Value);
            PrintValue("active after (independent provider acquisition)", afterActive.Value);
            if (!options.ProveRefusal)
            {
                return;
            }
            double secondRequest = requested == 0 ? 100 : 0;
            try
            {
                await client.ConfigureLimitAsync(
                    axis.Entry.NodeId, requestSignal, Variant.From(secondRequest), cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException("The fresh sample provider should refuse its second request.");
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadResourceUnavailable)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"PROVIDER_REFUSAL requested={secondRequest} status={exception.StatusCode}; no automatic retry."));
            }
            CraneElementSnapshot refused = await client.ReadElementAsync(axis.Entry.NodeId, cancellationToken)
                .ConfigureAwait(false);
            CraneSignalValue refusedRequest = FindSignal(refused, requestSignal);
            CraneSignalValue refusedActive = FindSignal(refused, activeSignal);
            if (!afterRequest.Value.WrappedValue.TryGetValue(out double retained) ||
                retained != requested ||
                !refusedRequest.Value.WrappedValue.Equals(afterRequest.Value.WrappedValue) ||
                !refusedActive.Value.WrappedValue.Equals(beforeActive.Value.WrappedValue))
            {
                throw new InvalidOperationException("The demo request/refusal did not preserve separate active state.");
            }
            Console.WriteLine("REFUSAL_VERIFIED: requested value retained; provider-reported active limit unchanged.");
        }

        /// <summary>
        /// Enforces a window, count and payload bound and verifies that only temporary subscriptions were used.
        /// </summary>
        /// <exception cref="InvalidOperationException">Subscription availability or cleanup failed.</exception>
        private static async Task ObserveAsync(
            CranesHoistsClient client,
            ArrayOf<NodeId> monitored,
            CraneClientOptions options,
            CancellationToken cancellationToken)
        {
            if (!client.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw new InvalidOperationException("An options-based managed subscription session is required.");
            }
            int before = manager.Count;
            var limits = new CraneObservationOptions
            {
                Window = TimeSpan.FromSeconds(options.Seconds),
                MaximumNotifications = options.Count,
                MaximumPayloadBytes = 128 * 1024
            };
            try
            {
                if (!monitored.IsEmpty)
                {
                    CraneObservation values = await client.ObserveValuesAsync(monitored, limits, cancellationToken)
                        .ConfigureAwait(false);
                    Console.WriteLine(
                        $"OBSERVED_VALUES count={values.Values.Count} limitReached={values.LimitReached} " +
                        $"dropped={values.DroppedNotifications}");
                    foreach (CraneObservedValue reading in values.Values)
                    {
                        Console.WriteLine($"  NOTIFICATION node={reading.NodeId}");
                        PrintValue("acquisition", reading.Value);
                    }
                }
                if (options.Events)
                {
                    CraneObservation events = await client.ObserveEventsAsync(
                        Opc.Ua.ObjectIds.Server, limits, cancellationToken).ConfigureAwait(false);
                    Console.WriteLine(
                        $"OBSERVED_EVENTS count={events.Events.Count} limitReached={events.LimitReached} " +
                        $"dropped={events.DroppedNotifications}");
                    foreach (CraneObservedEvent notification in events.Events)
                    {
                        Console.WriteLine($"  EVENT id={notification.EventId} type={notification.EventType}");
                        for (int i = 0; i < notification.Fields.Count; i++)
                        {
                            Console.WriteLine(
                                $"    field[{i}] path={events.FieldPaths[i]} value={notification.Fields[i]}");
                        }
                    }
                }
            }
            finally
            {
                Console.WriteLine(
                    $"OBSERVATION_CLEANUP subscriptionsBefore={before} subscriptionsAfter={manager.Count} " +
                    $"sessionConnected={client.Session.Connected}");
            }
            if (manager.Count != before)
            {
                throw new InvalidOperationException("The observation retained a subscription.");
            }
        }

        /// <summary>
        /// Requires a selected signal rather than treating optional absence as a readable measurement.
        /// </summary>
        /// <exception cref="InvalidOperationException">The signal is not selected on this element.</exception>
        private static CraneSignalValue FindSignal(CraneElementSnapshot element, CraneSignal signal)
        {
            foreach (CraneSignalValue value in element.Signals)
            {
                if (value.Signal == signal && !value.NodeId.IsNull)
                {
                    return value;
                }
            }
            throw new InvalidOperationException($"The selected axis does not expose {signal}.");
        }
    }
}
