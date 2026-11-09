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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises the actual OPC 40501-1 notifier and modern lifetime-counter conformance surfaces.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    [Category("Integration")]
    public sealed class MachineToolConformanceSurfaceTests
    {
        /// <summary>
        /// Every required notifier in a two-job, four-program, four-part plan delivers a provider transition
        /// when monitored directly, not merely through the machine root or a server-side callback.
        /// </summary>
        [TestCase("ProductionPlan", 0, 0)]
        [TestCase("Job", 0, 0)]
        [TestCase("Job", 1, 0)]
        [TestCase("Job.State", 0, 0)]
        [TestCase("Job.State", 1, 0)]
        [TestCase("ActiveProgram.State", 0, 0)]
        [TestCase("Program.State", 0, 0)]
        [TestCase("Program.State", 0, 1)]
        [TestCase("Program.State", 1, 0)]
        [TestCase("Program.State", 1, 1)]
        [TestCase("Part.State", 0, 0)]
        [TestCase("Part.State", 0, 1)]
        [TestCase("Part.State", 1, 0)]
        [TestCase("Part.State", 1, 1)]
        public async Task ProductionNotifiersDeliverProviderTransitionsDirectlyAsync(
            string surface, int jobIndex, int memberIndex)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var initialSample = new MachineToolSampleInfo(
                new DateTimeUtc(2026, 10, 11, 8, 9, 10), StatusCodes.Good);
            var finalSample = new MachineToolSampleInfo(
                new DateTimeUtc(2026, 10, 11, 8, 10, 11), StatusCodes.UncertainLastUsableValue);
            var provider = new InMemoryMachineToolProvider(ProductionSnapshot(false, initialSample));
            MachineToolSnapshot finished = ProductionSnapshot(true, finalSample);
            await using MachineToolRuntime<MachineToolState> runtime = await Builder(
                fixture, provider, MachineToolFeatures.None, "DirectProduction")
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts
                }).BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(fixture.Root, "ProductionConformance").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                MachineToolProductionReadSnapshot before = await client.ReadProductionAsync(runtime.NodeId)
                    .ConfigureAwait(false);
                Assert.That(before.Jobs.Count, Is.EqualTo(2));
                foreach (MachineToolJobReadSnapshot publishedJob in before.Jobs)
                {
                    Assert.That(publishedJob.Programs.Count, Is.EqualTo(2));
                    Assert.That(publishedJob.PartSets.Count, Is.EqualTo(1));
                    Assert.That(publishedJob.PartSets[0].Parts.Count, Is.EqualTo(2));
                }
                MachineToolJobReadSnapshot job = before.Jobs[jobIndex];
                NodeId notifier;
                NodeId[] sources;
                NodeId eventType;
                EventFilter filter;
                var registry = new EventRecordDecoderRegistry();
                switch (surface)
                {
                    case "ProductionPlan":
                    case "Job":
                    case "Job.State":
                        notifier = surface == "ProductionPlan" ? before.ProductionPlanId :
                            surface == "Job" ? job.NodeId : job.State.NodeId;
                        sources = surface == "ProductionPlan"
                            ? before.Jobs.ToArray()!.Select(value => value.State.NodeId).ToArray()
                            : [job.State.NodeId];
                        eventType = ExpandedNodeId.ToNodeId(
                            ObjectTypeIds.ProductionJobTransitionEventType, session.NamespaceUris);
                        registry.Register(eventType,
                            ProductionJobTransitionEventTypeRecord.Decoder.GetStandardFields(session.NamespaceUris),
                            ProductionJobTransitionEventTypeRecord.Decoder.Decode);
                        filter = ProductionJobTransitionEventTypeRecord.EventFilters.Build(
                            session.NamespaceUris, registry);
                        break;
                    case "ActiveProgram.State":
                    case "Program.State":
                        notifier = surface == "ActiveProgram.State"
                            ? before.ActiveProgram.State!.NodeId : job.Programs[memberIndex].State!.NodeId;
                        sources = [notifier];
                        eventType = ExpandedNodeId.ToNodeId(
                            ObjectTypeIds.ProductionProgramTransitionEventType, session.NamespaceUris);
                        registry.Register(eventType,
                            ProductionProgramTransitionEventTypeRecord.Decoder.GetStandardFields(session.NamespaceUris),
                            ProductionProgramTransitionEventTypeRecord.Decoder.Decode);
                        filter = ProductionProgramTransitionEventTypeRecord.EventFilters.Build(
                            session.NamespaceUris, registry);
                        break;
                    case "Part.State":
                        notifier = job.PartSets[0].Parts[memberIndex].State.NodeId;
                        sources = [notifier];
                        eventType = ExpandedNodeId.ToNodeId(
                            ObjectTypeIds.ProductionPartTransitionEventType, session.NamespaceUris);
                        registry.Register(eventType,
                            ProductionPartTransitionEventTypeRecord.Decoder.GetStandardFields(session.NamespaceUris),
                            ProductionPartTransitionEventTypeRecord.Decoder.Decode);
                        filter = ProductionPartTransitionEventTypeRecord.EventFilters.Build(
                            session.NamespaceUris, registry);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(surface));
                }
                await AssertNotifierAsync(session, notifier).ConfigureAwait(false);
                Assert.That(notifier, Is.Not.EqualTo(runtime.NodeId));
                Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
                await using var streaming = new StreamingSubscription(manager!,
                    new SubscriptionOptions { PublishingInterval = TimeSpan.FromMilliseconds(25) });
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWhenReadyAsync(
                    notifier, filter, async ct =>
                    {
                        await provider.SetSnapshotAsync(finished, ct).ConfigureAwait(false);
                        await runtime.RefreshAsync(ct).ConfigureAwait(false);
                    },
                    new MonitoredItemOptions { QueueSize = 16, DiscardOldest = false },
                    ct: timeout.Token).GetAsyncEnumerator(timeout.Token);
                var receivedSources = new HashSet<NodeId>();
                for (int index = 0; index < sources.Length; index++)
                {
                    Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True, surface);
                    Assert.That(events.Current.Fields.Count, Is.EqualTo(registry.StandardFields.Length));
                    var record = registry.DecodeAs(
                        eventType, events.Current.Fields.ToArray()!) as TransitionEventTypeRecord;
                    Assert.That(record, Is.Not.Null);
                    int sourceIndex = Array.IndexOf(sources, record!.SourceNode);
                    Assert.That(sourceIndex, Is.GreaterThanOrEqualTo(0), surface);
                    Assert.That(receivedSources.Add(record.SourceNode), Is.True,
                        "Duplicate delivery must not stand in for another required source.");
                    Assert.That(record.FromState, Is.EqualTo(new LocalizedText("Running")));
                    Assert.That(record.ToState, Is.EqualTo(new LocalizedText("Ended")));
                    Assert.That(record.Transition, Is.EqualTo(new LocalizedText("RunningToEnded")));
                    Assert.That(record.Message, Is.EqualTo(new LocalizedText("Running to Ended")));
                    Assert.That(record.Time, Is.EqualTo(finalSample.SourceTimestamp));
                    Assert.That(record.EventType, Is.Not.EqualTo(eventType),
                        "The abstract released event declaration must not be instantiated.");
                    Assert.That(await session.NodeCache.IsTypeOfAsync(
                        record.EventType, eventType, timeout.Token).ConfigureAwait(false), Is.True);
                    MachineToolProductionJob expected = finished.Production.Jobs[
                        surface == "ProductionPlan" ? sourceIndex : jobIndex];
                    switch (record)
                    {
                        case ProductionJobTransitionEventTypeRecord transition:
                            Assert.That(transition.Identifier, Is.EqualTo(expected.Identifier));
                            Assert.That(transition.CustomerOrderIdentifier, Is.EqualTo(expected.CustomerOrderIdentifier));
                            Assert.That(transition.RunsCompleted, Is.EqualTo(1u));
                            Assert.That(transition.RunsPlanned, Is.EqualTo(1u));
                            break;
                        case ProductionProgramTransitionEventTypeRecord transition:
                            Assert.That(transition.JobIdentifier, Is.EqualTo(expected.Identifier));
                            Assert.That(transition.Name, Is.EqualTo(expected.Programs[memberIndex].Name));
                            break;
                        case ProductionPartTransitionEventTypeRecord transition:
                            MachineToolProductionPart part = expected.PartSets[0].Parts[memberIndex];
                            Assert.That(transition.JobIdentifier, Is.EqualTo(expected.Identifier));
                            Assert.That(transition.Identifier, Is.EqualTo(part.Identifier));
                            Assert.That(transition.Name, Is.EqualTo(part.Name));
                            break;
                        default:
                            Assert.Fail("The generated decoder did not return a concrete production transition.");
                            break;
                    }
                }
                Assert.That(receivedSources, Is.EquivalentTo(sources));
                Assert.That(streaming.DroppedNotificationCount, Is.Zero);
                MachineToolProductionReadSnapshot after = await client.ReadProductionAsync(runtime.NodeId)
                    .ConfigureAwait(false);
                Assert.That(after.Jobs[jobIndex].State.CurrentState.WrappedValue.TryGetValue(
                    out LocalizedText currentState), Is.True);
                Assert.That(currentState.Text, Is.EqualTo("Ended"));
                Assert.That(after.Jobs[jobIndex].State.CurrentState.StatusCode, Is.EqualTo(finalSample.StatusCode));
                Assert.That(after.Jobs[jobIndex].State.CurrentState.SourceTimestamp,
                    Is.EqualTo(finalSample.SourceTimestamp));
                Assert.That(after.Jobs[jobIndex].RunsCompleted.WrappedValue.TryGetValue(out uint runs), Is.True);
                Assert.That(runs, Is.EqualTo(1u));
                Assert.That(after.Jobs[jobIndex].PartsGood.WrappedValue.TryGetValue(out uint good), Is.True);
                Assert.That(good, Is.EqualTo(2u));
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Messages itself accepts a real monitored item and delivers the provider's concrete message or alert,
        /// including occurrence time and source identity, without substituting the machine-root event helper.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MessagesNotifierDeliversProviderEventsDirectlyAsync(bool condition)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider, "DirectMessages").ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTimeUtc(2026, 10, 11, 9, 10, 11), StatusCodes.UncertainLastUsableValue);
            var alert = new MachineToolAlert
            {
                Key = "Coolant",
                ErrorCode = "E-DIRECT",
                Message = new LocalizedText("Coolant empty"),
                Severity = 800,
                IsActive = true,
                Sample = sample
            };
            var message = new MachineToolMessage
            {
                Sequence = 1,
                Identifier = "M-DIRECT",
                Message = new LocalizedText("Program released"),
                Severity = 200,
                Time = sample.SourceTimestamp
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(fixture.Root, "MessagesConformance").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                var machine = new MachineToolTypeClient(session, runtime.NodeId, telemetry);
                NotificationTypeClient? notification = await machine.GetNotificationAsync(telemetry)
                    .ConfigureAwait(false);
                Assert.That(notification, Is.Not.Null);
                MessagesTypeClient? messages = await notification!.GetMessagesAsync(telemetry).ConfigureAwait(false);
                Assert.That(messages, Is.Not.Null);
                Assert.That(messages!.ObjectId, Is.Not.EqualTo(runtime.NodeId));
                await AssertNotifierAsync(session, messages.ObjectId).ConfigureAwait(false);
                Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
                await using var streaming = new StreamingSubscription(manager!,
                    new SubscriptionOptions { PublishingInterval = TimeSpan.FromMilliseconds(25) });
                NodeId declaration = ExpandedNodeId.ToNodeId(
                    condition ? ObjectTypeIds.AlertType : ObjectTypeIds.NotificationEventType, session.NamespaceUris);
                var registry = new EventRecordDecoderRegistry();
                EventFilter filter;
                if (condition)
                {
                    registry.Register(declaration, AlertTypeRecord.Decoder.GetStandardFields(session.NamespaceUris),
                        AlertTypeRecord.Decoder.Decode);
                    filter = AlertTypeRecord.EventFilters.Build(session.NamespaceUris, registry);
                }
                else
                {
                    registry.Register(declaration,
                        NotificationEventTypeRecord.Decoder.GetStandardFields(session.NamespaceUris),
                        NotificationEventTypeRecord.Decoder.Decode);
                    filter = NotificationEventTypeRecord.EventFilters.Build(session.NamespaceUris, registry);
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWhenReadyAsync(
                    messages.ObjectId, filter, async ct =>
                    {
                        provider.Snapshot = new MachineToolNotificationSnapshot
                        {
                            Revision = 1,
                            Messages = [message],
                            Alerts = [alert]
                        };
                        await runtime.RefreshNotificationsAsync(ct).ConfigureAwait(false);
                    },
                    new MonitoredItemOptions { QueueSize = 8, DiscardOldest = false },
                    ct: timeout.Token).GetAsyncEnumerator(timeout.Token);
                Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(events.Current.Fields.Count, Is.EqualTo(registry.StandardFields.Length));
                var record = registry.DecodeAs(
                    declaration, events.Current.Fields.ToArray()!) as BaseEventTypeRecord;
                Assert.That(record, Is.Not.Null);
                Assert.That(record!.SourceNode, Is.EqualTo(messages.ObjectId));
                Assert.That(record.SourceName, Is.EqualTo("DirectMessages"));
                Assert.That(record.Time, Is.EqualTo(sample.SourceTimestamp));
                if (condition)
                {
                    Assert.That(record, Is.TypeOf<AlertTypeRecord>());
                    var received = (AlertTypeRecord)record;
                    Assert.That(received.ErrorCode, Is.EqualTo("E-DIRECT"));
                    Assert.That(received.Message, Is.EqualTo(alert.Message));
                    Assert.That(received.Severity, Is.EqualTo(800));
                    Assert.That(received.Quality, Is.EqualTo(sample.StatusCode));
                    Assert.That(received.ActiveStateId, Is.True);
                    Assert.That(received.AckedStateId, Is.False);
                    Assert.That(received.Retain, Is.True);
                    Assert.That(runtime.Notifications!.TryGetAlert(alert.Key, out AlertState? state), Is.True);
                    Assert.That(received.ConditionId, Is.EqualTo(state!.NodeId));
                    Assert.That(state.AckedState!.Id!.Value, Is.False,
                        "A direct passive event subscription must not acknowledge the retained condition.");
                }
                else
                {
                    Assert.That(record, Is.TypeOf<NotificationEventTypeRecord>());
                    var received = (NotificationEventTypeRecord)record;
                    Assert.That(received.Identifier, Is.EqualTo("M-DIRECT"));
                    Assert.That(received.Message, Is.EqualTo(message.Message));
                    Assert.That(received.Severity, Is.EqualTo(200));
                    Assert.That(received.EventType, Is.Not.EqualTo(declaration));
                    Assert.That(await session.NodeCache.IsTypeOfAsync(
                        received.EventType, declaration, timeout.Token).ConfigureAwait(false), Is.True);
                }
                Assert.That(streaming.DroppedNotificationCount, Is.Zero);
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The modern Machinery operation-counter component is UInt64 on the wire, retains independent
        /// acquisition metadata and updates across the UInt32 boundary without relying on legacy statistics.
        /// </summary>
        [Test]
        public async Task LifetimePartsUseModernUInt64ComponentAndPreserveUpdatedWireSamplesAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var initialSample = new MachineToolSampleInfo(
                new DateTimeUtc(2026, 10, 11, 10, 11, 12), StatusCodes.UncertainLastUsableValue);
            MachineToolSnapshot initial = CounterSnapshot(412, 9_000_123, initialSample);
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await Builder(
                fixture, provider,
                MachineToolFeatures.OperationCounters | MachineToolFeatures.PartsProducedInLifetime,
                "ModernLifetime").BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "LifetimeConformance").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                MachineOperationMonitoringTypeClient operation = await OperationAsync(
                    session, runtime.NodeId, telemetry).ConfigureAwait(false);
                var counters = await operation.GetOperationCountersAsync(telemetry).ConfigureAwait(false);
                Assert.That(counters, Is.Not.Null);
                DataValue counterName = await ReadAttributeAsync(session, counters!.ObjectId, Attributes.BrowseName)
                    .ConfigureAwait(false);
                Assert.That(counterName.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(counterName.WrappedValue.TryGetValue(out QualifiedName publishedName), Is.True);
                Assert.That(publishedName, Is.EqualTo(new QualifiedName("OperationCounters",
                    (ushort)session.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi))));
                ArrayOf<ReferenceDescription> components = await ComponentsAsync(session, counters.ObjectId)
                    .ConfigureAwait(false);
                var canonicalName = new QualifiedName(BrowseNames.PartsProducedInLifetime,
                    (ushort)session.NamespaceUris.GetIndex(Namespaces.MachineTool));
                ReferenceDescription[] matches = components.ToArray()!
                    .Where(reference => reference.BrowseName == canonicalName).ToArray();
                Assert.That(matches, Has.Length.EqualTo(1));
                Assert.That(matches[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(matches[0].NodeClass, Is.EqualTo(NodeClass.Variable));
                NodeId counterId = ExpandedNodeId.ToNodeId(matches[0].NodeId, session.NamespaceUris);
                DataValue dataType = await ReadAttributeAsync(session, counterId, Attributes.DataType)
                    .ConfigureAwait(false);
                Assert.That(dataType.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(dataType.WrappedValue.TryGetValue(out NodeId declaredType), Is.True);
                Assert.That(declaredType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt64));
                AssertLifetimeSample(await ReadAttributeAsync(session, counterId, Attributes.Value)
                    .ConfigureAwait(false), 412, initialSample);
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                MachineToolProductionReadSnapshot production = await client.ReadProductionAsync(runtime.NodeId)
                    .ConfigureAwait(false);
                Assert.That(production.PartsProducedInLifetime.IsNull, Is.True,
                    "The deprecated UInt32 Production.Statistics counter must not satisfy this modern CU.");

                const ulong updatedParts = (ulong)uint.MaxValue + 41;
                var updatedSample = new MachineToolSampleInfo(
                    new DateTimeUtc(2026, 10, 11, 10, 12, 13), StatusCodes.UncertainDataSubNormal);
                await provider.SetSnapshotAsync(CounterSnapshot(updatedParts, 9_060_456, updatedSample))
                    .ConfigureAwait(false);
                await runtime.RefreshAsync().ConfigureAwait(false);
                DataValue updated = await ReadAttributeAsync(session, counterId, Attributes.Value)
                    .ConfigureAwait(false);
                AssertLifetimeSample(updated, updatedParts, updatedSample);
                Assert.That(updated.WrappedValue.TryGetValue(out ulong actualParts), Is.True);
                Assert.That(actualParts, Is.GreaterThan((ulong)uint.MaxValue));
                ArrayOf<ReferenceDescription> refreshed = await ComponentsAsync(session, counters.ObjectId)
                    .ConfigureAwait(false);
                Assert.That(ExpandedNodeId.ToNodeId(refreshed.ToArray()!
                    .Single(reference => reference.BrowseName == canonicalName).NodeId, session.NamespaceUris),
                    Is.EqualTo(counterId), "A counter update must retain the published component identity.");
                await AssertPowerOnSampleAsync(session, counters.ObjectId, 9_060_456, updatedSample)
                    .ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Provider data cannot implicitly select the lifetime-parts feature, with or without its counter parent.
        /// The selected neighboring observations remain available with the equipment's quality and timestamp.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LifetimePartsRemainAbsentWhenFeatureNotSelectedAsync(bool operationCounters)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTimeUtc(2026, 10, 11, 11, 12, 13), StatusCodes.UncertainLastUsableValue);
            var provider = new InMemoryMachineToolProvider(
                CounterSnapshot((ulong)uint.MaxValue + 41, 9_000_123, sample));
            await using MachineToolRuntime<MachineToolState> runtime = await Builder(
                fixture, provider,
                operationCounters ? MachineToolFeatures.OperationCounters : MachineToolFeatures.None,
                "UnselectedLifetime").BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "LifetimeAbsence").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                MachineOperationMonitoringTypeClient operation = await OperationAsync(
                    session, runtime.NodeId, telemetry).ConfigureAwait(false);
                var counters = await operation.GetOperationCountersAsync(telemetry).ConfigureAwait(false);
                Assert.That(counters == null, Is.EqualTo(!operationCounters));
                ArrayOf<ReferenceDescription> children = await ComponentsAsync(
                    session, counters?.ObjectId ?? operation.ObjectId).ConfigureAwait(false);
                Assert.That(children.ToArray()!.Any(reference => reference.BrowseName.Name ==
                    BrowseNames.PartsProducedInLifetime), Is.False);
                if (operationCounters)
                {
                    await AssertPowerOnSampleAsync(session, counters!.ObjectId, 9_000_123, sample)
                        .ConfigureAwait(false);
                }
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                MachineToolReadSnapshot read = await client.ReadSnapshotAsync(runtime.NodeId).ConfigureAwait(false);
                Assert.That(read.OperationMode.WrappedValue.TryGetValue(out int mode), Is.True);
                Assert.That(mode, Is.EqualTo((int)MachineOperationMode.Automatic));
                Assert.That(read.OperationMode.StatusCode, Is.EqualTo(sample.StatusCode));
                Assert.That(read.OperationMode.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Uses the public provider builder and only the explicitly selected optional features.
        /// </summary>
        private static MachineToolBuilder<MachineToolState> Builder(
            MachineToolRuntimeFixture fixture, IMachineToolProvider provider, MachineToolFeatures features, string name)
        {
            return fixture.Context.AddMachineTool(name).WithProvider(provider).WithFeatures(features)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Conformance surface tests");
                    identity.SerialNumber = name;
                    identity.ProductInstanceUri = "urn:conformance-surface:" + name;
                });
        }

        /// <summary>
        /// Supplies distinct jobs, programs and workpieces, with coherent completed-run and part counts.
        /// </summary>
        private static MachineToolSnapshot ProductionSnapshot(bool completed, MachineToolSampleInfo sample)
        {
            var jobs = new MachineToolProductionJob[2];
            MachineToolProductionPhase phase = completed
                ? MachineToolProductionPhase.Ended : MachineToolProductionPhase.Running;
            for (int jobIndex = 0; jobIndex < jobs.Length; jobIndex++)
            {
                string identifier = jobIndex == 0 ? "Order-A" : "Order-B";
                var programs = new MachineToolProgram[2];
                var parts = new MachineToolProductionPart[2];
                for (int memberIndex = 0; memberIndex < programs.Length; memberIndex++)
                {
                    programs[memberIndex] = new MachineToolProgram
                    {
                        Name = $"{identifier}-P{memberIndex}.NC",
                        JobIdentifier = identifier,
                        Phase = phase,
                        TransitionSequence = completed ? 1u : 0u,
                        Sample = sample
                    };
                    parts[memberIndex] = new MachineToolProductionPart
                    {
                        Key = $"Part-{memberIndex}",
                        Name = $"{identifier}-Housing-{memberIndex}",
                        Identifier = $"{identifier}-H{memberIndex}",
                        Phase = phase,
                        TransitionSequence = completed ? 1u : 0u,
                        Quality = completed ? PartQuality.Good : PartQuality.NotYetMeasured,
                        Sample = sample
                    };
                }
                jobs[jobIndex] = new MachineToolProductionJob
                {
                    Identifier = identifier,
                    CustomerOrderIdentifier = "Customer-" + identifier,
                    Phase = phase,
                    TransitionSequence = completed ? 1u : 0u,
                    RunsPlanned = 1,
                    RunsPlannedIsValid = true,
                    RunsCompleted = completed ? 1u : 0u,
                    PartsCompleted = completed ? 2u : 0u,
                    PartsGood = completed ? 2u : 0u,
                    Programs = programs,
                    PartSets =
                    [
                        new MachineToolProductionPartSet
                        {
                            Key = "Lot",
                            Name = "Housings",
                            PartsPlannedPerRun = 2,
                            PartsCompletedPerRun = completed ? 2u : 0u,
                            Parts = parts,
                            Sample = sample
                        }
                    ],
                    Sample = sample
                };
            }
            return new MachineToolSnapshot
            {
                ActiveProgram = jobs[0].Programs[0],
                Production = new MachineToolProductionObservation { Jobs = jobs }
            };
        }

        /// <summary>
        /// Supplies the modern UInt64 observation without enabling or populating deprecated statistics.
        /// </summary>
        private static MachineToolSnapshot CounterSnapshot(
            ulong parts, double milliseconds, MachineToolSampleInfo sample) => new()
            {
                Operation = new MachineToolOperation
                {
                    Mode = MachineOperationMode.Automatic,
                    PartsProducedInLifetime = parts,
                    PowerOnDurationMilliseconds = milliseconds,
                    Sample = sample
                }
            };

        /// <summary>
        /// Reads the actual attribute through the connected session, never from NodeState.EventNotifier.
        /// </summary>
        private static async ValueTask AssertNotifierAsync(ISession session, NodeId node)
        {
            DataValue attribute = await ReadAttributeAsync(session, node, Attributes.EventNotifier)
                .ConfigureAwait(false);
            Assert.That(attribute.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(attribute.WrappedValue.TryGetValue(out byte notifier), Is.True);
            Assert.That(notifier & EventNotifiers.SubscribeToEvents, Is.EqualTo(EventNotifiers.SubscribeToEvents));
        }

        /// <summary>
        /// Preserves the raw DataValue, including uncertain quality, rather than using a value-only convenience read.
        /// </summary>
        private static async ValueTask<DataValue> ReadAttributeAsync(ISession session, NodeId node, uint attribute)
        {
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = node, AttributeId = attribute }], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        /// <summary>
        /// Navigates the canonical Monitoring/MachineTool path using generated typed proxies.
        /// </summary>
        private static async ValueTask<MachineOperationMonitoringTypeClient> OperationAsync(
            ISession session, NodeId machineId, ITelemetryContext telemetry)
        {
            var machine = new MachineToolTypeClient(session, machineId, telemetry);
            MonitoringTypeClient? monitoring = await machine.GetMonitoringAsync(telemetry).ConfigureAwait(false);
            Assert.That(monitoring, Is.Not.Null);
            MachineOperationMonitoringTypeClient? operation = await monitoring!.GetMachineToolAsync(telemetry)
                .ConfigureAwait(false);
            Assert.That(operation, Is.Not.Null);
            return operation!;
        }

        /// <summary>
        /// Uses the existing browser to enumerate exact HasComponent references, including continuation handling.
        /// </summary>
        private static ValueTask<ArrayOf<ReferenceDescription>> ComponentsAsync(ISession session, NodeId parent)
        {
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = false,
                ResultMask = (uint)BrowseResultMask.All,
                ContinueUntilDone = true
            };
            return browser.BrowseAsync(parent);
        }

        /// <summary>
        /// Asserts both UInt64 decoding and original acquisition metadata for a lifetime observation.
        /// </summary>
        private static void AssertLifetimeSample(DataValue value, ulong expected, MachineToolSampleInfo sample)
        {
            Assert.That(value.WrappedValue.TryGetValue(out ulong actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(value.StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(value.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
        }

        /// <summary>
        /// Checks a second, independently typed observation under the same modern counter building block.
        /// </summary>
        private static async ValueTask AssertPowerOnSampleAsync(
            ISession session, NodeId counters, double expected, MachineToolSampleInfo sample)
        {
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                IncludeSubtypes = false,
                ResultMask = (uint)BrowseResultMask.All,
                ContinueUntilDone = true
            };
            ArrayOf<ReferenceDescription> children = await browser.BrowseAsync(counters).ConfigureAwait(false);
            var name = new QualifiedName(BrowseNames.PowerOnDuration,
                (ushort)session.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi));
            ReferenceDescription reference = children.ToArray()!.Single(child => child.BrowseName == name);
            Assert.That(reference.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
            DataValue value = await ReadAttributeAsync(
                session, ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris), Attributes.Value)
                .ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(value.StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(value.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
        }
    }
}
