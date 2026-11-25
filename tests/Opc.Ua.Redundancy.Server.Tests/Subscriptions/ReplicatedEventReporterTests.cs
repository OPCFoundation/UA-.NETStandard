// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    [TestFixture]
    [Category("Distributed")]
    [Category("Events")]
    public sealed class ReplicatedEventReporterTests
    {
        [TestCase(RedundancySupport.Transparent)]
        [TestCase(RedundancySupport.HotAndMirrored)]
        public async Task NativeOrdinaryReportsPreserveOccurrenceIdOnBothReplicasAsync(RedundancySupport mode)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            var context = new SystemContext(telemetry) { NamespaceUris = messageContext.NamespaceUris };
            var filterContext = new FilterContext(messageContext.NamespaceUris, new TypeTable(
                messageContext.NamespaceUris), telemetry);
            using var store = new InMemorySharedKeyValueStore();
            var groupId = new Uuid("11223344-5566-7788-9000-aabbccddeeff");
            var same = new ReplicatedEventIdentity(groupId, new Uuid("00112233-4455-6677-8899-aabbccddeeff"));
            var distinct = new ReplicatedEventIdentity(groupId, Uuid.NewUuid());
            var expected = ByteString.FromHexString(
                "44332211665588779000AABBCCDDEEFF33221100554477668899AABBCCDDEEFF");
            using ServiceProvider activeServices = CreateServices(
                mode, groupId, Mock.Of<IReplicatedConditionMethodRelay>());
            using ServiceProvider standbyServices = CreateServices(
                mode, groupId, Mock.Of<IReplicatedConditionMethodRelay>());
            ReplicatedEventReporter active = activeServices.GetRequiredService<ReplicatedEventReporter>();
            ReplicatedEventReporter standby = standbyServices.GetRequiredService<ReplicatedEventReporter>();
            var source = new BaseObjectState(null) { NodeId = new NodeId(42) };
            var payload = new BaseEventState(null);
            payload.Initialize(context, source, EventSeverity.Low, LocalizedText.From("Same payload"));
            ByteString originalId = payload.EventId!.Value;

            await using var activeQueues = new SharedKeyValueMonitoredItemQueueFactory(
                store, messageContext, telemetry: telemetry);
            using IEventMonitoredItemQueue queue = activeQueues.CreateEventQueue(false, 10);
            queue.SetQueueSize(3, false);
            source.OnReportEvent = (_, _, e) => queue.Enqueue(new QueuedEvent(new EventFieldList
            {
                EventFields =
                [
                    new Variant(ReadEventId(filterContext, e)),
                    ReadField(filterContext, e, BrowseNames.Message),
                    ReadField(filterContext, e, BrowseNames.Time)
                ]
            }));

            await active.ReportEventAsync(context, source, payload, same).ConfigureAwait(false);
            await active.ReportEventAsync(context, source, payload, distinct).ConfigureAwait(false);
            await activeQueues.FlushAsync().ConfigureAwait(false);

            using IEventMonitoredItemQueue restored = (await activeQueues.RestoreEventQueueAsync(10)
                .ConfigureAwait(false))!;
            var standbySource = new BaseObjectState(null) { NodeId = source.NodeId };
            ByteString standbyId = default;
            standbySource.OnReportEvent = (_, _, e) => standbyId = ReadEventId(filterContext, e);
            await standby.ReportEventAsync(context, standbySource, payload, same).ConfigureAwait(false);

            Assert.That(restored.Dequeue(out QueuedEvent first), Is.True);
            Assert.That(restored.Dequeue(out QueuedEvent second), Is.True);
            Assert.That(first.EventFields[0], Is.EqualTo(new Variant(expected)));
            Assert.That(second.EventFields[0], Is.Not.EqualTo(first.EventFields[0]));
            Assert.That(first.EventFields[1], Is.EqualTo(new Variant(payload.Message!.Value)));
            Assert.That(first.EventFields[2], Is.EqualTo(new Variant(payload.Time!.Value)));
            Assert.That(second.EventFields[1], Is.EqualTo(first.EventFields[1]));
            Assert.That(second.EventFields[2], Is.EqualTo(first.EventFields[2]));
            Assert.That(standbyId, Is.EqualTo(expected));
            Assert.That(payload.EventId.Value, Is.EqualTo(originalId));
        }

        [Test]
        public async Task PromotedReplicaRefreshesTheSameBranchAndRelaysAckAndConfirmAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var message = ServiceMessageContext.Create(telemetry);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                TypeTable = new TypeTable(message.NamespaceUris)
            };
            var filterContext = new FilterContext(message.NamespaceUris, context.TypeTable, telemetry);
            Uuid groupId = Uuid.NewUuid();
            var trunk = new ReplicatedEventIdentity(groupId, Uuid.NewUuid());
            var firstBranch = new ReplicatedEventIdentity(groupId, Uuid.NewUuid());
            var laterBranch = new ReplicatedEventIdentity(groupId, Uuid.NewUuid());
            var acknowledged = new ReplicatedEventIdentity(groupId, Uuid.NewUuid());
            var confirmed = new ReplicatedEventIdentity(groupId, Uuid.NewUuid());
            var authority = new Mock<IReplicatedConditionMethodRelay>(MockBehavior.Strict);
            var active = new ReplicatedEventReporter(RedundancySupport.HotAndMirrored, groupId, authority.Object);
            var standby = new ReplicatedEventReporter(RedundancySupport.HotAndMirrored, groupId, authority.Object);
            AlarmConditionState activeAlarm = CreateAlarm(context);
            AlarmConditionState standbyAlarm = CreateAlarm(context);
            active.BindConditionMethods(activeAlarm);
            standby.BindConditionMethods(standbyAlarm);
            var branchId = new NodeId(52);
            var received = new List<ByteString>();
            activeAlarm.OnReportEvent = (_, _, e) => received.Add(ReadEventId(filterContext, e));
            standbyAlarm.OnReportEvent = (_, _, e) => received.Add(ReadEventId(filterContext, e));

            await active.ApplyConditionOccurrenceAsync(context, activeAlarm, default, trunk,
                state => state.Retain!.Value = true, publish: false).ConfigureAwait(false);
            await standby.ApplyConditionOccurrenceAsync(context, standbyAlarm, default, trunk,
                state => state.Retain!.Value = true, publish: false).ConfigureAwait(false);
            await ApplyBranchAsync(active, activeAlarm, firstBranch, acked: false, confirmedState: false)
                .ConfigureAwait(false);
            await ApplyBranchAsync(active, activeAlarm, laterBranch, acked: false, confirmedState: false)
                .ConfigureAwait(false);
            await ApplyBranchAsync(standby, standbyAlarm, firstBranch, false, false, publish: false)
                .ConfigureAwait(false);
            await ApplyBranchAsync(standby, standbyAlarm, laterBranch, false, false, publish: false)
                .ConfigureAwait(false);

            Assert.That(received, Has.Count.EqualTo(2));
            Assert.That(ReadRefreshedBranch(activeAlarm), Is.EqualTo(received[1]));
            Assert.That(ReadRefreshedBranch(standbyAlarm), Is.EqualTo(received[1]));
            Assert.That(standbyAlarm.EventId!.Value, Is.EqualTo(active.GetEventId(trunk)));
            Assert.That(standbyAlarm.GetBranchCount(), Is.EqualTo(1));
            var comment = LocalizedText.From("operator");
            ByteString firstEventId = received[0];
            ByteString ackEventId = standby.GetEventId(acknowledged);
            Uuid foreignGroup = Uuid.NewUuid();
            var foreignReporter = new ReplicatedEventReporter(
                RedundancySupport.HotAndMirrored, foreignGroup, authority.Object);
            ByteString foreignId = foreignReporter.GetEventId(
                new ReplicatedEventIdentity(foreignGroup, Uuid.NewUuid()));
            authority.Setup(a => a.CallAsync(
                    groupId, context, standbyAlarm.NodeId, ReplicatedConditionMethod.Acknowledge,
                    firstEventId, comment, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await ApplyBranchAsync(standby, standbyAlarm, acknowledged, acked: true, confirmedState: false)
                        .ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            authority.Setup(a => a.CallAsync(
                    groupId, context, standbyAlarm.NodeId, ReplicatedConditionMethod.Confirm,
                    ackEventId, comment, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await ApplyBranchAsync(standby, standbyAlarm, confirmed, acked: true, confirmedState: true)
                        .ConfigureAwait(false);
                    standby.RetireConditionBranch(standbyAlarm, branchId);
                    return ServiceResult.Good;
                });

            Assert.That(await InvokeAsync(standbyAlarm.Acknowledge!, foreignId).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(await InvokeAsync(standbyAlarm.Acknowledge!, firstEventId).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(ReadRefreshedBranch(standbyAlarm), Is.EqualTo(ackEventId));
            Assert.That(await InvokeAsync(standbyAlarm.Confirm!, ackEventId).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(received, Has.Count.EqualTo(4));
            Assert.That(received[3], Is.EqualTo(standby.GetEventId(confirmed)));
            Assert.That(standbyAlarm.GetBranchCount(), Is.Zero);
            authority.VerifyAll();

            ValueTask ApplyBranchAsync(
                ReplicatedEventReporter reporter,
                AlarmConditionState alarm,
                ReplicatedEventIdentity identity,
                bool acked,
                bool confirmedState,
                bool publish = true)
            {
                return reporter.ApplyConditionOccurrenceAsync(context, alarm, branchId, identity, state =>
                {
                    Assert.That(state, Is.InstanceOf<AlarmConditionState>());
                    var branch = (AlarmConditionState)state;
                    branch.SetAcknowledgedState(context, acked);
                    branch.SetConfirmedState(context, confirmedState);
                    branch.Retain!.Value = !confirmedState;
                }, publish);
            }

            ByteString ReadRefreshedBranch(AlarmConditionState alarm)
            {
                var retained = new List<IFilterTarget>();
                alarm.ConditionRefresh(context, retained, false);
                IFilterTarget branch = retained.Single(e =>
                    ReadField(filterContext, e, BrowseNames.BranchId)
                        .TryGetValue(out NodeId actualBranchId) && actualBranchId == branchId);
                return ReadEventId(filterContext, branch);
            }

            async Task<StatusCode> InvokeAsync(MethodState method, ByteString eventId)
            {
                ServiceResult result = await method.CallAsync(
                    context, standbyAlarm.NodeId, [new Variant(eventId), new Variant(comment)], [], [])
                    .ConfigureAwait(false);
                return result?.StatusCode ?? StatusCodes.Good;
            }
        }

        [TestCase(RedundancySupport.None)]
        [TestCase(RedundancySupport.Cold)]
        [TestCase(RedundancySupport.Warm)]
        [TestCase(RedundancySupport.Hot)]
        public void OtherModesDoNotInstallReplicatedReports(RedundancySupport mode)
        {
            using ServiceProvider services = CreateServices(
                mode, Uuid.NewUuid(), Mock.Of<IReplicatedConditionMethodRelay>());
            Assert.That(() => services.GetRequiredService<ReplicatedEventReporter>(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static ServiceProvider CreateServices(
            RedundancySupport mode,
            Uuid groupId,
            IReplicatedConditionMethodRelay relay)
        {
            var builder = new DiTestServerBuilder();
            builder.AddServerRedundancy(options => options.Mode = mode);
            builder.UseReplicatedEventReporting(groupId, _ => relay);
            return builder.Services.BuildServiceProvider();
        }

        [TestCase(ReplicatedConditionMethod.Acknowledge)]
        [TestCase(ReplicatedConditionMethod.Confirm)]
        public void BindingRejectsUnmigratedNativeConditionHooks(ReplicatedConditionMethod method)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = ServiceMessageContext.Create(telemetry).NamespaceUris
            };
            AlarmConditionState alarm = CreateAlarm(context);
            int calls = 0;
            ConditionAddCommentEventHandler hook = (_, _, _, _) =>
            {
                calls++;
                return StatusCodes.BadUserAccessDenied;
            };
            if (method == ReplicatedConditionMethod.Acknowledge)
            {
                alarm.OnAcknowledge = hook;
            }
            else
            {
                alarm.OnConfirm = hook;
            }
            var reporter = new ReplicatedEventReporter(
                RedundancySupport.Transparent, Uuid.NewUuid(), Mock.Of<IReplicatedConditionMethodRelay>());

            Assert.That(() => reporter.BindConditionMethods(alarm), Throws.InvalidOperationException);
            Assert.That(alarm.AutoReportStateChanges, Is.True);
            Assert.That(alarm.Acknowledge!.OnCallAsync, Is.Null);
            Assert.That(alarm.Confirm!.OnCallAsync, Is.Null);
            Assert.That(calls, Is.Zero);
        }

        private static AlarmConditionState CreateAlarm(SystemContext context)
        {
            var alarm = new AlarmConditionState(null);
            alarm.ConfirmedState = new TwoStateVariableState(alarm);
            alarm.Confirm = new AddCommentMethodState(alarm);
            alarm.Create(context, new NodeId(42), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(context, true);
            alarm.AutoReportStateChanges = true;
            alarm.Retain!.Value = true;
            return alarm;
        }

        private static ByteString ReadEventId(IFilterContext context, IFilterTarget occurrence)
        {
            Variant id = ReadField(context, occurrence, BrowseNames.EventId);
            Assert.That(id.TryGetValue(out ByteString eventId), Is.True);
            return eventId;
        }

        private static Variant ReadField(IFilterContext context, IFilterTarget occurrence, string name)
        {
            return occurrence.GetAttributeValue(
                context, default, [QualifiedName.From(name)], Attributes.Value, default);
        }
    }
}
