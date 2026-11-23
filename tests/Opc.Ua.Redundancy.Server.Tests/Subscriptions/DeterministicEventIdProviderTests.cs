// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="DeterministicEventIdProvider"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Events")]
    public class DeterministicEventIdProviderTests
    {
        [Test]
        public void SameEventContentProducesSameEventIdAcrossReplicas()
        {
            var providerA = new DeterministicEventIdProvider("replica-set");
            var providerB = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState eventA = CreateEvent("started");
            BaseEventState eventB = CreateEvent("started");
            eventA.ReceiveTime!.Value = new DateTimeUtc(638000000000000001);
            eventB.ReceiveTime!.Value = new DateTimeUtc(638000000000000999);

            ByteString idA = providerA.CreateEventId(notifier, context, eventA);
            ByteString idB = providerB.CreateEventId(notifier, context, eventB);

            Assert.That(idA.ToArray(), Is.EqualTo(idB.ToArray()));
            Assert.That(idA.Length, Is.EqualTo(32));
        }

        [Test]
        public void DifferentSeedProducesDifferentEventId()
        {
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState e = CreateEvent("started");

            ByteString idA = new DeterministicEventIdProvider("set-a").CreateEventId(notifier, context, e);
            ByteString idB = new DeterministicEventIdProvider("set-b").CreateEventId(notifier, context, e);

            Assert.That(idA.ToArray(), Is.Not.EqualTo(idB.ToArray()));
        }

        [Test]
        public void ConstructorRejectsEmptySeed()
        {
            Assert.That(
                () => new DeterministicEventIdProvider(" "),
                Throws.ArgumentException);
        }

        [Test]
        public void DifferentEventTimeProducesDifferentEventId()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseEventState event1 = CreateEvent("started");
            event1.Time!.Value = new DateTimeUtc(638000000000000001);
            BaseEventState event2 = CreateEvent("started");
            event2.Time!.Value = new DateTimeUtc(638000000000000002);

            ByteString id1 = provider.CreateEventId(context, event1);
            ByteString id2 = provider.CreateEventId(context, event2);

            Assert.That(id1.ToArray(), Is.Not.EqualTo(id2.ToArray()));
        }

        [Test]
        public void DistinctEventMessagesProduceDifferentEventIds()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState event1 = CreateEvent("started");
            BaseEventState event2 = CreateEvent("stopped");

            ByteString id1 = provider.CreateEventId(notifier, context, event1);
            ByteString id2 = provider.CreateEventId(notifier, context, event2);

            Assert.That(id1.ToArray(), Is.Not.EqualTo(id2.ToArray()));
        }

        [Test]
        public void EventSourceRegistryComputesEventIdAfterDefaultDistinguishingFields()
        {
            var provider = new CapturingEventIdProvider();
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            var e = new BaseEventState(null);

            InvokePopulateDefaults(notifier, context, e, provider);

            Assert.That(provider.ObservedMessage, Is.Not.Null);
            Assert.That(provider.ObservedSeverity, Is.EqualTo((ushort)EventSeverity.Medium));
            Assert.That(e.EventId, Is.Not.Null);
            Assert.That(e.EventId!.Value.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void DifferentNotifierProducesDifferentEventId()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier1 = CreateNotifier();
            notifier1.NodeId = new NodeId("Notifier1", 2);
            BaseObjectState notifier2 = CreateNotifier();
            notifier2.NodeId = new NodeId("Notifier2", 2);
            BaseEventState e = CreateEvent("started");

            ByteString id1 = provider.CreateEventId(notifier1, context, e);
            ByteString id2 = provider.CreateEventId(notifier2, context, e);

            Assert.That(id1.ToArray(), Is.Not.EqualTo(id2.ToArray()));
        }

        [Test]
        public void EventIdLengthIs32Bytes()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext();
            BaseObjectState notifier = CreateNotifier();
            BaseEventState e = CreateEvent("started");

            ByteString id = provider.CreateEventId(notifier, context, e);

            Assert.That(id.Length, Is.EqualTo(32));
        }

        [Test]
        public void ConstructorRejectsNullSeed()
        {
            Assert.That(
                () => new DeterministicEventIdProvider(null!),
                Throws.ArgumentException);
        }

        [TestCase(RedundancySupport.Transparent)]
        [TestCase(RedundancySupport.HotAndMirrored)]
        public async Task HostedRegistrationUsesReplicaSeedAndFollowsModeChangesAsync(RedundancySupport mode)
        {
            var builder = new DiTestServerBuilder();
            builder.AddServerRedundancy(o => o.Mode = mode);
            builder.UseReplicaNodeIdentity("replica-set", ["urn:example:model"]);
            await using ServiceProvider services = builder.Services.BuildServiceProvider();
            var provider = services.GetRequiredService<IEventOccurrenceIdProvider>();
            ServerSystemContext context = CreateContext(provider);
            BaseEventState e = CreateEvent("started");
            ByteString expected = new DeterministicEventIdProvider("replica-set").CreateEventId(context, e);

            Assert.That(provider.CreateEventId(context, e), Is.EqualTo(expected));
            await services.GetRequiredService<IServerRedundancyController>()
                .ChangeModeAsync(RedundancySupport.Hot).ConfigureAwait(false);
            Assert.That(provider.CreateEventId(context, e).IsNull, Is.True);
        }

        [Test]
        public async Task RawAndFluentEventsUseSameIdentityAcrossNamespaceTablesAsync()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext first = CreateContext(provider);
            ServerSystemContext second = CreateContext(provider);
            second.NamespaceUris.GetIndexOrAppend("urn:other");
            BaseObjectState sourceA = CreateNotifier();
            sourceA.NodeId = new NodeId("source", first.NamespaceUris.GetIndexOrAppend("urn:shared"));
            BaseObjectState sourceB = CreateNotifier();
            sourceB.NodeId = new NodeId("source", second.NamespaceUris.GetIndexOrAppend("urn:shared"));
            var eventA = new BaseEventState(null);
            var eventB = new BaseEventState(null);
            eventA.Initialize(first, sourceA, EventSeverity.Medium, LocalizedText.From("started"));
            eventA.Time!.Value = new DateTimeUtc(638000000000000001);
            eventB.Initialize(second, sourceB, EventSeverity.Medium, LocalizedText.From("started"));
            eventB.Time!.Value = eventA.Time.Value;
            ByteString provisional = eventA.EventId!.Value;

            await sourceA.ReportEventAsync(first.Copy(), eventA).ConfigureAwait(false);
            InvokePopulateDefaults(sourceB, second, eventB, null);

            Assert.That(eventA.EventId.Value, Is.Not.EqualTo(provisional));
            Assert.That(eventA.EventId.Value, Is.EqualTo(eventB.EventId!.Value));
        }

        [Test]
        public void ExplicitImportedIdSurvivesSnapshotAndReport()
        {
            ServerSystemContext context = CreateContext(new DeterministicEventIdProvider("replica-set"));
            BaseObjectState source = CreateNotifier();
            var e = new BaseEventState(null);
            e.Initialize(context, source, EventSeverity.Medium, LocalizedText.From("imported"));
            ByteString explicitId = ByteString.From(5, 6, 7);
            e.EventId!.Value = explicitId;

            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, e);
            source.ReportEvent(context, snapshot);

            Assert.That(ReadSnapshotId(snapshot), Is.EqualTo(explicitId));
            Assert.That(e.EventId.Value, Is.EqualTo(explicitId));
        }

        [Test]
        public void ConditionSnapshotIdCanBeAcknowledgedAndConfirmedOnReplica()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext contextA = CreateContext(provider);
            ServerSystemContext contextB = CreateContext(provider);
            TestAlarm alarmA = CreateAlarm(contextA);
            TestAlarm alarmB = CreateAlarm(contextB);
            InstanceStateSnapshot? snapshot = null;
            alarmA.OnReportEvent = (_, _, e) => snapshot = e as InstanceStateSnapshot;
            alarmA.SetAreEventsMonitored(contextA, true, false);
            var time = new DateTimeUtc(638000000000000001);

            alarmA.ReportTransition(contextA, time);
            alarmB.ReportTransition(contextB, time);
            ByteString publishedId = ReadSnapshotId(snapshot!);
            Assert.That(publishedId.Length, Is.EqualTo(32));
            Assert.That(alarmB.EventId!.Value, Is.EqualTo(publishedId));

            // Supersede the occurrence on the unmonitored replica. The published ID must
            // already have been recorded there, rather than only changed on the wire.
            alarmB.ReportTransition(contextB, new DateTimeUtc(time.Value + 1));
            Assert.That(alarmB.EventId.Value, Is.Not.EqualTo(publishedId));
            Assert.That(ServiceResult.IsGood(alarmB.CallAcknowledge(contextB, publishedId)), Is.True);
            Assert.That(ServiceResult.IsGood(alarmB.CallConfirm(contextB, alarmB.EventId.Value)), Is.True);
        }

        [Test]
        public void ConditionAndBranchIdsDistinguishSimultaneousTransitions()
        {
            var provider = new DeterministicEventIdProvider("replica-set");
            ServerSystemContext context = CreateContext(provider);
            TestAlarm first = CreateAlarm(context);
            TestAlarm second = CreateAlarm(context);
            second.NodeId = new NodeId(2);
            var time = new DateTimeUtc(638000000000000001);
            first.ReportTransition(context, time);
            second.ReportTransition(context, time);
            ByteString trunk = first.EventId!.Value;

            first.BranchId!.Value = new NodeId(10);
            first.ReportTransition(context, time);

            Assert.That(trunk, Is.Not.EqualTo(second.EventId!.Value));
            Assert.That(trunk, Is.Not.EqualTo(first.EventId.Value));
        }

        private static ByteString ReadSnapshotId(InstanceStateSnapshot snapshot)
        {
            Variant id = snapshot.GetAttributeValue(
                null!,
                default,
                [QualifiedName.From(BrowseNames.EventId)],
                Attributes.Value,
                default);
            Assert.That(id.TryGetValue(out ByteString result), Is.True);
            return result;
        }

        private static TestAlarm CreateAlarm(ISystemContext context)
        {
            var alarm = new TestAlarm();
            alarm.Create(context, new NodeId(1), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(context, true);
            alarm.SetAcknowledgedState(context, false);
            alarm.ConfirmedState = new TwoStateVariableState(alarm);
            alarm.ConfirmedState.Create(
                context, default, QualifiedName.From(BrowseNames.ConfirmedState), default, false);
            alarm.SetConfirmedState(context, false);
            alarm.AutoReportStateChanges = true;
            return alarm;
        }

        private static ServerSystemContext CreateContext(IEventOccurrenceIdProvider? provider = null)
        {
            var messageContext = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.ServerUris).Returns(messageContext.ServerUris);
            server.Setup(s => s.Factory).Returns(messageContext.Factory);
            server.Setup(s => s.TypeTree).Returns(new TypeTable(messageContext.NamespaceUris));
            var context = new TestContext(server.Object, provider);
            server.Setup(s => s.DefaultSystemContext).Returns(context);
            return context;
        }

        private sealed class TestContext(IServerInternal server, IEventOccurrenceIdProvider? provider)
            : ServerSystemContext(server), IEventOccurrenceIdContext
        {
            IEventOccurrenceIdProvider? IEventOccurrenceIdContext.EventOccurrenceIdProvider => provider;
        }

        private static BaseObjectState CreateNotifier()
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId("Notifier", 2),
                BrowseName = new QualifiedName("Notifier", 2)
            };
        }

        private static BaseEventState CreateEvent(string message)
        {
            return new BaseEventState(null)
            {
                Message = PropertyState<LocalizedText>.With<VariantBuilder>(null!, new LocalizedText(message)),
                Severity = PropertyState<ushort>.With<VariantBuilder>(null!, 500),
                Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(null!, new DateTimeUtc(638000000000000000)),
                ReceiveTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(
                    null!,
                    new DateTimeUtc(638000000000000000))
            };
        }

        private static void InvokePopulateDefaults(
            BaseObjectState notifier,
            ISystemContext context,
            BaseEventState e,
            IEventIdProvider? provider)
        {
            Type registryType = typeof(IEventIdProvider).Assembly.GetType(
                "Opc.Ua.Server.Fluent.EventSourceRegistry")!;
            MethodInfo method = registryType.GetMethod(
                "PopulateDefaults",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            method.Invoke(null, [notifier, context, e, provider]);
        }

        private sealed class TestAlarm() : AlarmConditionState(null)
        {
            public void ReportTransition(ISystemContext context, DateTimeUtc sourceTime)
            {
                ReportStateChange(context, false, sourceTime);
            }

            public ServiceResult CallAcknowledge(ISystemContext context, ByteString eventId)
            {
                return OnAcknowledgeCalled(context, Acknowledge!, NodeId, eventId, LocalizedText.From("ack"));
            }

            public ServiceResult CallConfirm(ISystemContext context, ByteString eventId)
            {
                return OnConfirmCalled(context, new MethodState(this), NodeId, eventId, LocalizedText.From("confirm"));
            }
        }

        private sealed class CapturingEventIdProvider : IEventIdProvider
        {
            public LocalizedText? ObservedMessage { get; private set; }

            public ushort? ObservedSeverity { get; private set; }

            public ByteString CreateEventId(BaseObjectState notifier, ISystemContext context, BaseEventState eventState)
            {
                _ = notifier;
                _ = context;
                ObservedMessage = eventState.Message?.Value;
                ObservedSeverity = eventState.Severity?.Value;
                return ByteString.From(new byte[] { 1, 2, 3, 4 });
            }
        }
    }
}
