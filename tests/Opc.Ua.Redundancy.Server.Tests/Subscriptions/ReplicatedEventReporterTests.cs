// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
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
            var groupId = new Uuid("11223344-5566-7788-9000-aabbccddeeff");
            var same = new ReplicatedEventIdentity(groupId, new Uuid("00112233-4455-6677-8899-aabbccddeeff"));
            var distinct = new ReplicatedEventIdentity(groupId, Uuid.NewUuid());
            var expected = ByteString.FromHexString(
                "44332211665588779000AABBCCDDEEFF33221100554477668899AABBCCDDEEFF");
            var active = new ReplicatedEventReporter(mode, groupId, new TestRelay());
            var standby = new ReplicatedEventReporter(mode, groupId, new TestRelay());
            var source = new BaseObjectState(null) { NodeId = new NodeId(42) };
            var payload = new BaseEventState(null);
            payload.Initialize(context, source, EventSeverity.Low, LocalizedText.From("Same payload"));
            ByteString originalId = payload.EventId!.Value;

            var reports = new List<(ByteString EventId, Variant Message, Variant Time)>();
            source.OnReportEvent = (_, _, e) => reports.Add((
                ReadEventId(filterContext, e),
                ReadField(filterContext, e, BrowseNames.Message),
                ReadField(filterContext, e, BrowseNames.Time)));

            await active.ReportEventAsync(context, source, payload, same).ConfigureAwait(false);
            await active.ReportEventAsync(context, source, payload, distinct).ConfigureAwait(false);
            var standbySource = new BaseObjectState(null) { NodeId = source.NodeId };
            ByteString standbyId = default;
            standbySource.OnReportEvent = (_, _, e) => standbyId = ReadEventId(filterContext, e);
            await standby.ReportEventAsync(context, standbySource, payload, same).ConfigureAwait(false);

            Assert.That(reports, Has.Count.EqualTo(2));
            Assert.That(reports[0].EventId, Is.EqualTo(expected));
            Assert.That(reports[1].EventId, Is.Not.EqualTo(reports[0].EventId));
            Assert.That(reports[0].Message, Is.EqualTo(new Variant(payload.Message!.Value)));
            Assert.That(reports[0].Time, Is.EqualTo(new Variant(payload.Time!.Value)));
            Assert.That(reports[1].Message, Is.EqualTo(reports[0].Message));
            Assert.That(reports[1].Time, Is.EqualTo(reports[0].Time));
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
            var authority = new TestRelay();
            var active = new ReplicatedEventReporter(RedundancySupport.HotAndMirrored, groupId, authority);
            var standby = new ReplicatedEventReporter(RedundancySupport.HotAndMirrored, groupId, authority);
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
                RedundancySupport.HotAndMirrored, foreignGroup, authority);
            ByteString foreignId = foreignReporter.GetEventId(
                new ReplicatedEventIdentity(foreignGroup, Uuid.NewUuid()));
            authority.Handler = async (group, callContext, conditionId, method, eventId, callComment) =>
            {
                Assert.That(group, Is.EqualTo(groupId));
                Assert.That(callContext, Is.SameAs(context));
                Assert.That(conditionId, Is.EqualTo(standbyAlarm.NodeId));
                Assert.That(callComment, Is.EqualTo(comment));
                if (method == ReplicatedConditionMethod.Acknowledge)
                {
                    Assert.That(eventId, Is.EqualTo(firstEventId));
                    await ApplyBranchAsync(standby, standbyAlarm, acknowledged, acked: true, confirmedState: false)
                        .ConfigureAwait(false);
                }
                else
                {
                    Assert.That(method, Is.EqualTo(ReplicatedConditionMethod.Confirm));
                    Assert.That(eventId, Is.EqualTo(ackEventId));
                    await ApplyBranchAsync(standby, standbyAlarm, confirmed, acked: true, confirmedState: true)
                        .ConfigureAwait(false);
                    standby.RetireConditionBranch(standbyAlarm, branchId);
                }
                return ServiceResult.Good;
            };

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
            Assert.That(authority.Calls, Is.EqualTo(2));

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
        public void OtherModesDoNotSupportReplicatedReports(RedundancySupport mode)
        {
            Assert.That(() => new ReplicatedEventReporter(
                    mode, Uuid.NewUuid(), new TestRelay()),
                Throws.TypeOf<ArgumentOutOfRangeException>());
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
                RedundancySupport.Transparent, Uuid.NewUuid(), new TestRelay());

            Assert.That(() => reporter.BindConditionMethods(alarm), Throws.InvalidOperationException);
            Assert.That(alarm.AutoReportStateChanges, Is.True);
            Assert.That(alarm.Acknowledge!.OnCallAsync, Is.Null);
            Assert.That(alarm.Confirm!.OnCallAsync, Is.Null);
            Assert.That(calls, Is.Zero);
        }

        private sealed class TestRelay : IReplicatedConditionMethodRelay
        {
            public Func<Uuid, ISystemContext, NodeId, ReplicatedConditionMethod, ByteString, LocalizedText,
                ValueTask<ServiceResult>>? Handler { get; set; }

            public int Calls { get; private set; }

            public ValueTask<ServiceResult> CallAsync(
                Uuid groupId,
                ISystemContext context,
                NodeId conditionId,
                ReplicatedConditionMethod method,
                ByteString eventId,
                LocalizedText comment,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                return (Handler ?? throw new AssertionException("Unexpected condition method call."))(
                    groupId, context, conditionId, method, eventId, comment);
            }
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
