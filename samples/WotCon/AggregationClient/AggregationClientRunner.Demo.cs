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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.WotCon.Client;

namespace AggregationClient
{
    /// <summary>
    /// Exercises explicitly enabled pump control and alarm round trips through the aggregation server,
    /// verifying the resulting state independently on both upstream sources.
    /// </summary>
    public static partial class AggregationClientRunner
    {
        private static async Task<ArrayOf<WotPumpControlResult>> ExerciseControlsAsync(
            ManagedSession aggregate,
            AggregationClientOptions options,
            ArrayOf<WotRegistryDocument> documents,
            ArrayOf<WotPumpResult> pumps,
            CancellationToken cancellationToken)
        {
            (List<DemoAction> actions, List<DemoEvent> events) = ReadDemoDeclarations(aggregate, documents);
            var results = new List<WotPumpControlResult>();
            foreach (string sourceName in s_demoSources)
            {
                string endpoint = sourceName == "SourceA" ? options.SourceAEndpoint : options.SourceBEndpoint;
                var sourceOptions = new AggregationClientOptions
                {
                    AggregationEndpoint = endpoint,
                    ApplicationName = options.ApplicationName,
                    AutoAcceptUntrustedCertificates = options.AutoAcceptUntrustedCertificates,
                    UseSecurityPolicyNone = options.UseSecurityPolicyNone,
                    PkiRoot = options.PkiRoot
                };
                using IHost sourceHost = BuildHost(sourceOptions);
                await sourceHost.StartAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Func<CancellationToken, Task<ManagedSession>> connect = sourceHost.Services
                        .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
                    ManagedSession source = await connect(cancellationToken).ConfigureAwait(false);
                    await using var sourceLifetime = source.ConfigureAwait(false);
                    ITelemetryContext telemetry = sourceHost.Services.GetRequiredService<ITelemetryContext>();
                    for (int pumpIndex = 0; pumpIndex < pumps.Count; pumpIndex++)
                    {
                        WotPumpResult pump = pumps[pumpIndex];
                        string sourceNamespace = "urn:opcfoundation.org:UA:WotAggregation:" + sourceName;
                        string upstreamPump = $"nsu={sourceNamespace};s={pump.Name}";
                        DemoAction start = FindManagementAction(actions, endpoint, upstreamPump, "Start");
                        DemoAction stop = FindManagementAction(actions, endpoint, upstreamPump, "Stop");
                        DemoAction reset = FindManagementAction(actions, endpoint, upstreamPump, "Reset");

                        await InvokeDemoActionAsync(aggregate, stop, [], cancellationToken).ConfigureAwait(false);
                        await RequireBooleanAsync(
                            source, ResolvePortableId(source, upstreamPump + ".Running"), false, cancellationToken)
                            .ConfigureAwait(false);
                        await InvokeDemoActionAsync(aggregate, start, [], cancellationToken).ConfigureAwait(false);
                        await RequireBooleanAsync(
                            source, ResolvePortableId(source, upstreamPump + ".Running"), true, cancellationToken)
                            .ConfigureAwait(false);

                        DemoEvent alarmEvent = events.Single(item =>
                            item.Endpoint == endpoint && item.UpstreamNotifier == upstreamPump);
                        DemoAction acknowledge = actions.Single(item =>
                            item.ConditionAction == "Acknowledge" && item.EventKey == alarmEvent.Key);
                        DemoAction confirm = actions.Single(item =>
                            item.ConditionAction == "Confirm" && item.EventKey == alarmEvent.Key);
                        string signalPath = sourceName == "SourceA"
                            ? ".Events.SupervisionProcessFluid.Cavitation"
                            : ".Events.SupervisionPumpOperation.MotorOverheat";
                        NodeId signal = ResolvePortableId(source, upstreamPump + signalPath);
                        NodeId condition = ResolvePortableId(source, upstreamPump + signalPath + ".Alarm");
                        await WriteSignalAsync(source, signal, false, cancellationToken).ConfigureAwait(false);
                        await CompleteSourceAttentionAsync(source, condition, telemetry, cancellationToken)
                            .ConfigureAwait(false);

                        await ExerciseAlarmAsync(
                            aggregate, source, pump.RootNodeId, signal, condition, alarmEvent,
                            acknowledge, confirm, reset, cancellationToken).ConfigureAwait(false);
                        results.Add(new WotPumpControlResult(pump.Name, sourceName));
                    }
                }
                finally
                {
                    await sourceHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            if (results.Count != 4)
            {
                throw new InvalidOperationException("The demonstration must complete both pumps on both sources.");
            }
            return new ArrayOf<WotPumpControlResult>(results.ToArray());
        }

        private static async Task ExerciseAlarmAsync(
            ManagedSession aggregate,
            ManagedSession source,
            NodeId notifier,
            NodeId signal,
            NodeId condition,
            DemoEvent alarm,
            DemoAction acknowledge,
            DemoAction confirm,
            DemoAction reset,
            CancellationToken cancellationToken)
        {
            CreateSubscriptionResponse subscription = await aggregate.CreateSubscriptionAsync(
                null, 50, 6000, 10, 0, true, 0, cancellationToken).ConfigureAwait(false);
            uint subscriptionId = subscription.SubscriptionId;
            try
            {
                var clauses = new SimpleAttributeOperand[s_alarmFields.Length];
                for (int i = 0; i < clauses.Length; i++)
                {
                    clauses[i] = new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.AlarmConditionType,
                        BrowsePath = s_alarmFields[i].Split('/').Select(QualifiedName.From).ToArrayOf(),
                        AttributeId = Attributes.Value
                    };
                }
                var request = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = notifier, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(new EventFilter
                        {
                            SelectClauses = new ArrayOf<SimpleAttributeOperand>(clauses)
                        })
                    }
                };
                CreateMonitoredItemsResponse created = await aggregate.CreateMonitoredItemsAsync(
                    null, subscriptionId, TimestampsToReturn.Both, [request], cancellationToken).ConfigureAwait(false);
                if (created.Results.Count != 1 || StatusCode.IsBad(created.Results[0].StatusCode))
                {
                    throw new ServiceResultException(
                        created.Results.Count == 0 ? StatusCodes.BadUnexpectedError : created.Results[0].StatusCode,
                        "The aggregate alarm monitored item could not be created.");
                }

                var pendingAcknowledgements = new List<SubscriptionAcknowledgement>();
                await WriteSignalAsync(source, signal, true, cancellationToken).ConfigureAwait(false);
                ByteString eventId = await WaitForAlarmAsync(
                    aggregate, alarm.LocalId, pendingAcknowledgements, true, false, false, true, cancellationToken)
                    .ConfigureAwait(false);
                ArrayOf<Variant> arguments =
                    [Variant.From(eventId), Variant.From(new LocalizedText("Demo acknowledge"))];
                await InvokeDemoActionAsync(aggregate, acknowledge, arguments, cancellationToken).ConfigureAwait(false);
                eventId = await WaitForAlarmAsync(
                    aggregate, alarm.LocalId, pendingAcknowledgements, true, true, false, true, cancellationToken)
                    .ConfigureAwait(false);
                await RequireStateAsync(source, condition, "AckedState/Id", true, cancellationToken)
                    .ConfigureAwait(false);

                arguments = [Variant.From(eventId), Variant.From(new LocalizedText("Demo confirm"))];
                await InvokeDemoActionAsync(aggregate, confirm, arguments, cancellationToken).ConfigureAwait(false);
                _ = await WaitForAlarmAsync(
                    aggregate, alarm.LocalId, pendingAcknowledgements, true, true, true, true, cancellationToken)
                    .ConfigureAwait(false);
                await RequireStateAsync(source, condition, "ConfirmedState/Id", true, cancellationToken)
                    .ConfigureAwait(false);
                await InvokeDemoActionAsync(aggregate, reset, [], cancellationToken).ConfigureAwait(false);
                _ = await WaitForAlarmAsync(
                    aggregate, alarm.LocalId, pendingAcknowledgements, false, true, true, false, cancellationToken)
                    .ConfigureAwait(false);
                await RequireBooleanAsync(source, signal, false, cancellationToken).ConfigureAwait(false);
                await RequireStateAsync(source, condition, "Retain", false, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await DeleteDemoSubscriptionAsync(aggregate, subscriptionId).ConfigureAwait(false);
            }
        }

        private static async Task DeleteDemoSubscriptionAsync(ManagedSession session, uint subscriptionId)
        {
            DeleteSubscriptionsResponse deleted = await session.DeleteSubscriptionsAsync(
                null, [subscriptionId], CancellationToken.None).ConfigureAwait(false);
            if (deleted.Results.Count != 1 || StatusCode.IsBad(deleted.Results[0]))
            {
                throw new ServiceResultException(
                    deleted.Results.Count == 0 ? StatusCodes.BadUnexpectedError : deleted.Results[0],
                    "The demonstration subscription could not be removed.");
            }
        }

        private static async Task<ByteString> WaitForAlarmAsync(
            ManagedSession session,
            NodeId eventType,
            List<SubscriptionAcknowledgement> acknowledgements,
            bool active,
            bool acknowledged,
            bool confirmed,
            bool retained,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (true)
            {
                PublishResponse response = await session.PublishAsync(
                    null, new ArrayOf<SubscriptionAcknowledgement>(acknowledgements.ToArray()), timeout.Token)
                    .ConfigureAwait(false);
                acknowledgements.Clear();
                if (!response.NotificationMessage.NotificationData.IsEmpty)
                {
                    acknowledgements.Add(new SubscriptionAcknowledgement
                    {
                        SubscriptionId = response.SubscriptionId,
                        SequenceNumber = response.NotificationMessage.SequenceNumber
                    });
                }
                foreach (ExtensionObject data in response.NotificationMessage.NotificationData)
                {
                    if (!data.TryGetValue(out EventNotificationList? notifications))
                    {
                        continue;
                    }
                    foreach (EventFieldList item in notifications.Events)
                    {
                        ArrayOf<Variant> fields = item.EventFields;
                        if (fields.Count != s_alarmFields.Length ||
                            !fields[1].TryGetValue(out NodeId receivedType) || receivedType != eventType)
                        {
                            continue;
                        }
                        if (!fields[0].TryGetValue(out ByteString eventId) || eventId.IsEmpty ||
                            !fields[2].TryGetValue(out bool isActive) ||
                            !fields[3].TryGetValue(out bool isAcknowledged) ||
                            !fields[4].TryGetValue(out bool isConfirmed) ||
                            !fields[5].TryGetValue(out bool isRetained))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError, "The aggregate omitted selected alarm state fields.");
                        }
                        if (isActive == active && isAcknowledged == acknowledged &&
                            isConfirmed == confirmed && isRetained == retained)
                        {
                            return eventId;
                        }
                    }
                }
            }
        }

        private static async Task CompleteSourceAttentionAsync(
            ManagedSession source, NodeId condition, ITelemetryContext telemetry, CancellationToken cancellationToken)
        {
            AlarmClient client = source.GetAlarmClient(telemetry);
            if (!await ReadStateAsync(source, condition, "AckedState/Id", cancellationToken).ConfigureAwait(false))
            {
                await client.AcknowledgeAsync(
                    condition, await ReadOccurrenceAsync(source, condition, cancellationToken).ConfigureAwait(false),
                    LocalizedText.Null, cancellationToken).ConfigureAwait(false);
            }
            if (!await ReadStateAsync(source, condition, "ConfirmedState/Id", cancellationToken).ConfigureAwait(false))
            {
                await client.ConfirmAsync(
                    condition, await ReadOccurrenceAsync(source, condition, cancellationToken).ConfigureAwait(false),
                    LocalizedText.Null, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<ByteString> ReadOccurrenceAsync(
            ManagedSession session, NodeId condition, CancellationToken cancellationToken)
        {
            NodeId node = await ResolveChildAsync(session, condition, "EventId", cancellationToken)
                .ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(node, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode) || !value.WrappedValue.TryGetValue(out ByteString eventId))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The source returned no EventId.");
            }
            return eventId;
        }

        private static async Task RequireStateAsync(
            ManagedSession session, NodeId condition, string path, bool expected, CancellationToken cancellationToken)
        {
            bool state = await ReadStateAsync(session, condition, path, cancellationToken).ConfigureAwait(false);
            if (state != expected)
            {
                throw new InvalidOperationException($"Source {condition}/{path} did not become {expected}.");
            }
        }

        private static async Task<bool> ReadStateAsync(
            ManagedSession session, NodeId condition, string path, CancellationToken cancellationToken)
        {
            NodeId node = await ResolveChildAsync(session, condition, path, cancellationToken).ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(node, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode) || !value.WrappedValue.TryGetValue(out bool state))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, $"The source returned no {path} state.");
            }
            return state;
        }

        private static async Task<NodeId> ResolveChildAsync(
            ManagedSession session, NodeId parent, string path, CancellationToken cancellationToken)
        {
            var browsePath = new BrowsePath
            {
                StartingNode = parent,
                RelativePath = RelativePath.Parse(path, session.TypeTree)
            };
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null, [browsePath], cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != 1 || StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, $"The source has no {parent}/{path}.");
            }
            return ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, session.NamespaceUris);
        }

        private static async Task RequireBooleanAsync(
            ManagedSession session, NodeId node, bool expected, CancellationToken cancellationToken)
        {
            DataValue value = await session.ReadValueAsync(node, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode) ||
                !value.WrappedValue.TryGetValue(out bool actual) || actual != expected)
            {
                throw new InvalidOperationException($"Source {node} did not become {expected}.");
            }
        }

        private static async Task WriteSignalAsync(
            ManagedSession session, NodeId nodeId, bool value, CancellationToken cancellationToken)
        {
            var write = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(Variant.From(value))
            };
            WriteResponse response = await session.WriteAsync(null, [write], cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != 1 || StatusCode.IsBad(response.Results[0]))
            {
                throw new ServiceResultException(
                    response.Results.Count == 0 ? StatusCodes.BadUnexpectedError : response.Results[0],
                    "The source alarm signal could not be changed.");
            }
        }

        private static async Task InvokeDemoActionAsync(
            ManagedSession session, DemoAction action, ArrayOf<Variant> inputs, CancellationToken cancellationToken)
        {
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true
            };
            ArrayOf<ReferenceDescription> parents = await browser.BrowseAsync(
                action.LocalId, cancellationToken).ConfigureAwait(false);
            if (parents.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, $"Action {action.LocalId} has no unique local owner.");
            }
            NodeId parent = ExpandedNodeId.ToNodeId(parents[0].NodeId, session.NamespaceUris);
            _ = await session.CallAsync(parent, action.LocalId, cancellationToken, inputs.ToArray() ?? [])
                .ConfigureAwait(false);
        }

        private static DemoAction FindManagementAction(
            List<DemoAction> actions, string endpoint, string pump, string method)
        {
            return actions.Single(action =>
                action.Endpoint == endpoint && action.UpstreamOwner == pump &&
                action.UpstreamMethod == pump + "." + method);
        }

        private static (List<DemoAction> Actions, List<DemoEvent> Events) ReadDemoDeclarations(
            ManagedSession session, ArrayOf<WotRegistryDocument> documents)
        {
            var actions = new List<DemoAction>();
            var events = new List<DemoEvent>();
            foreach (WotRegistryDocument document in documents)
            {
                using JsonDocument json = JsonDocument.Parse(document.Content.Memory);
                JsonElement root = json.RootElement;
                if (root.TryGetProperty("actions", out JsonElement actionMap))
                {
                    foreach (JsonProperty property in actionMap.EnumerateObject())
                    {
                        JsonElement action = property.Value;
                        if (!action.TryGetProperty("forms", out JsonElement forms))
                        {
                            continue;
                        }
                        if (forms.GetArrayLength() != 1)
                        {
                            throw new InvalidDataException($"Demo action {property.Name} must have one source owner.");
                        }
                        JsonElement form = forms[0];
                        string? actsOn = action.TryGetProperty("uav:actsOn", out JsonElement target)
                            ? target.GetString() : null;
                        actions.Add(new DemoAction(
                            ResolvePortableId(session, action.GetProperty("uav:id").GetString()!),
                            form.GetProperty("href").GetString()!,
                            form.GetProperty("uav:componentOf").GetString()!,
                            form.GetProperty("uav:id").GetString()!,
                            action.TryGetProperty("uav:conditionAction", out JsonElement kind)
                                ? kind.GetString() : null,
                            actsOn is null ? null : document.ResourceId + "#" + actsOn));
                    }
                }
                if (root.TryGetProperty("events", out JsonElement eventMap))
                {
                    foreach (JsonProperty property in eventMap.EnumerateObject())
                    {
                        JsonElement eventValue = property.Value;
                        if (!eventValue.TryGetProperty("forms", out JsonElement forms))
                        {
                            continue;
                        }
                        if (forms.GetArrayLength() != 1)
                        {
                            throw new InvalidDataException($"Demo event {property.Name} must have one source owner.");
                        }
                        events.Add(new DemoEvent(
                            document.ResourceId + "#" + property.Name,
                            ResolvePortableId(session, eventValue.GetProperty("uav:id").GetString()!),
                            forms[0].GetProperty("href").GetString()!,
                            forms[0].GetProperty("uav:id").GetString()!));
                    }
                }
            }
            return (actions, events);
        }

        private static NodeId ResolvePortableId(ManagedSession session, string value)
        {
            NodeId result = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(value), session.NamespaceUris);
            if (result.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, $"Could not resolve '{value}'.");
            }
            return result;
        }

        /// <summary>
        /// Associates a materialized aggregate action with its source-owned method and optional condition event.
        /// </summary>
        /// <param name="LocalId">Materialized method node ID invoked on the aggregation server.</param>
        /// <param name="Endpoint">Source endpoint from the action's sole form.</param>
        /// <param name="UpstreamOwner">Portable node ID of the source object owning the method.</param>
        /// <param name="UpstreamMethod">Portable node ID of the source method targeted by the form.</param>
        /// <param name="ConditionAction">
        /// Condition operation such as Acknowledge or Confirm, or null when absent.
        /// </param>
        /// <param name="EventKey">Resource-qualified event key referenced by actsOn, or null when absent.</param>
        private sealed record DemoAction(
            NodeId LocalId,
            string Endpoint,
            string UpstreamOwner,
            string UpstreamMethod,
            string? ConditionAction,
            string? EventKey);

        /// <summary>
        /// Associates an aggregate alarm event type with its document identity and upstream event notifier.
        /// </summary>
        /// <param name="Key">Resource ID and event name joined by # to match condition-action references.</param>
        /// <param name="LocalId">Materialized event type node ID used to filter aggregate notifications.</param>
        /// <param name="Endpoint">Source endpoint from the event's sole form.</param>
        /// <param name="UpstreamNotifier">Portable node ID of the source object that emits the event.</param>
        private sealed record DemoEvent(string Key, NodeId LocalId, string Endpoint, string UpstreamNotifier);

        private static readonly string[] s_demoSources = ["SourceA", "SourceB"];
        private static readonly string[] s_alarmFields =
            ["EventId", "EventType", "ActiveState/Id", "AckedState/Id", "ConfirmedState/Id", "Retain"];
    }
}
