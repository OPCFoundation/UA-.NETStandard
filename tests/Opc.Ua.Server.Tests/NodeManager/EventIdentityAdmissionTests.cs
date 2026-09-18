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

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [NonParallelizable]
    public sealed class EventIdentityAdmissionTests
    {
        [Test]
        public void NativePublicationBeforeReservationPreservesOriginalOwner()
        {
            using var fixture = new IdentityFixture();
            BaseEventState native = Event(s_id);
            fixture.Manager.AdmitEvent(fixture.Context, native);

            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, new Origin()).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
            fixture.Manager.AdmitEvent(fixture.Context, Event(s_id));
            Assert.That(native.EventId!.Value, Is.EqualTo(s_id));
        }

        [Test]
        public void ReservationBeforeNativePublicationRejectsCopiedFields()
        {
            using var fixture = new IdentityFixture();
            using EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin());
            BaseEventState projected = Event(s_id);
            reservation.Attach(fixture.Context, projected);
            fixture.Manager.AdmitEvent(fixture.Context, projected);

            AssertStatus(() => fixture.Manager.AdmitEvent(fixture.Context, Event(s_id)),
                StatusCodes.BadSecurityChecksFailed);
            Assert.That(projected.EventId!.Value, Is.EqualTo(s_id));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndependentProjectionRolesCannotCollideInEitherOrder(bool reverse)
        {
            using var fixture = new IdentityFixture();
            var local = new Origin();
            var forwarded = new Origin();
            using EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_id, reverse ? forwarded : local);

            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, reverse ? local : forwarded).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
        }

        [Test]
        public void OccurrenceEquivalenceRequiresBilateralAgreement()
        {
            using var fixture = new IdentityFixture();
            using EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_id, new TrustsAnyOrigin());

            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, new Origin()).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
        }

        [Test]
        public void ReleasingOneSharedReservationDoesNotReleaseItsOtherOwner()
        {
            using var fixture = new IdentityFixture();
            var source = new Origin();
            using EventManager.EventIdentityReservation first =
                fixture.Manager.ReserveEventIdentity(s_id, source);
            using (EventManager.EventIdentityReservation second =
                fixture.Manager.ReserveEventIdentity(s_id, source))
            {
                second.Dispose();
            }

            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, new Origin()).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
        }

        [Test]
        public void UnattachedReservationRollbackImmediatelyReclaimsItsBudget()
        {
            using var fixture = new IdentityFixture(1);
            using (EventManager.EventIdentityReservation rejected =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin()))
            {
                rejected.Dispose();
            }
            using EventManager.EventIdentityReservation replacement =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin());
            BaseEventState state = Event(s_id);
            replacement.Attach(fixture.Context, state);
            fixture.Manager.AdmitEvent(fixture.Context, state);
            Assert.That(state.EventId!.Value, Is.EqualTo(s_id));
        }

        [Test]
        public void NativeRetransmissionPreservesEventIdWithoutConsumingCapacity()
        {
            using var fixture = new IdentityFixture(1);
            EnableAdmission(fixture);
            BaseEventState first = Event(s_id);
            fixture.Manager.AdmitEvent(fixture.Context, first);
            BaseEventState replay = Event(s_id);
            replay.ReceiveTime!.Value = s_laterReceipt;
            fixture.Manager.AdmitEvent(fixture.Context, replay);

            Assert.That(replay.EventId!.Value, Is.EqualTo(first.EventId!.Value));
            AssertStatus(() => fixture.Manager.AdmitEvent(fixture.Context, Event(s_otherId)),
                StatusCodes.BadTooManyOperations);
            fixture.Manager.AdmitEvent(fixture.Context, first);
        }

        [Test]
        public void NativeRetainedConditionRefreshPreservesIdentityAndPermittedProperties()
        {
            using var fixture = new IdentityFixture();
            EnableAdmission(fixture);
            LimitAlarmState condition = Condition(s_id);
            fixture.Manager.AdmitEvent(fixture.Context, condition);
            var first = new InstanceStateSnapshot();
            first.Initialize(fixture.Context, condition);
            fixture.Manager.AdmitEvent(fixture.Context, first);

            condition.ReceiveTime!.Value = s_laterReceipt;
            condition.HighLimit!.Value = 110;
            var refresh = new InstanceStateSnapshot();
            refresh.Initialize(fixture.Context, condition);
            fixture.Manager.AdmitEvent(fixture.Context, refresh);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(condition.EventId!.Value, Is.EqualTo(s_id));
                Assert.That(condition.Time!.Value, Is.EqualTo(s_time));
                Assert.That(condition.HighLimit.Value, Is.EqualTo(110));
                Assert.That(condition.Retain!.Value, Is.True);
            }
            condition.BranchId!.Value = new NodeId("another-branch", 1);
            AssertStatus(() => fixture.Manager.AdmitEvent(fixture.Context, condition),
                StatusCodes.BadSecurityChecksFailed);
        }

        [Test]
        public void ChangedNativeCoreStateIsRejectedAfterAdmissionIsRequired()
        {
            using var fixture = new IdentityFixture();
            EnableAdmission(fixture);
            fixture.Manager.AdmitEvent(fixture.Context, Event(s_id));
            BaseEventState changed = Event(s_id);
            changed.Severity!.Value = 999;

            AssertStatus(() => fixture.Manager.AdmitEvent(fixture.Context, changed),
                StatusCodes.BadSecurityChecksFailed);
            fixture.Manager.AdmitEvent(fixture.Context, Event(s_id));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeStatePropertiesCannotReuseRetainedIdentity(bool audible)
        {
            using var fixture = new IdentityFixture();
            EnableAdmission(fixture);
            LimitAlarmState condition = Condition(s_id);
            PropertyState<bool> property;
            if (audible)
            {
                condition.AudibleEnabled ??= PropertyState<bool>.With<VariantBuilder>(condition);
                condition.AudibleEnabled.BrowseName = QualifiedName.From(BrowseNames.AudibleEnabled);
                property = condition.AudibleEnabled;
            }
            else
            {
                condition.SuppressedOrShelved ??= PropertyState<bool>.With<VariantBuilder>(condition);
                condition.SuppressedOrShelved.BrowseName = QualifiedName.From(BrowseNames.SuppressedOrShelved);
                property = condition.SuppressedOrShelved;
            }
            property.Value = false;
            fixture.Manager.AdmitEvent(fixture.Context, condition);
            var retained = new InstanceStateSnapshot();
            retained.Initialize(fixture.Context, condition);
            fixture.Manager.AdmitEvent(fixture.Context, retained);

            property.Value = true;
            AssertStatus(() => fixture.Manager.AdmitEvent(fixture.Context, condition),
                StatusCodes.BadSecurityChecksFailed);
        }

        [Test]
        public void LegacyNativeConflictMakesLaterOptionalAdmissionUnavailable()
        {
            using var fixture = new IdentityFixture();
            fixture.Manager.AdmitEvent(fixture.Context, Event(s_id));
            BaseEventState changed = Event(s_id);
            changed.Severity!.Value = 999;
            fixture.Manager.AdmitEvent(fixture.Context, changed);

            Assert.That(fixture.Manager.EventIdentityAdmissionStatus, Is.EqualTo(StatusCodes.BadNotSupported));
            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_otherId, new Origin()).Dispose(),
                StatusCodes.BadNotSupported);
        }

        [Test]
        public void LegacyUnidentifiedFieldsCannotBeForgottenWhenAdmissionIsRequested()
        {
            using var fixture = new IdentityFixture();
            using MonitoredItem item = fixture.CreateItem();
            item.QueueEvent(new EventFieldList { EventFields = [s_id] });
            var notifications = new Queue<EventFieldList>();
            using var context = NewContext();
            item.Publish(context, notifications, 8);

            Assert.That(notifications, Has.Count.EqualTo(1), "Legacy native output must not be silently dropped.");
            Assert.That(fixture.Manager.EventIdentityAdmissionStatus, Is.EqualTo(StatusCodes.BadNotSupported));
            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_otherId, new Origin()).Dispose(),
                StatusCodes.BadNotSupported);
        }

        [Test]
        public void PreselectedFieldsCannotForgeAnAdmittedOccurrenceHandle()
        {
            using var fixture = new IdentityFixture();
            using EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin());
            BaseEventState state = Event(s_id);
            reservation.Attach(fixture.Context, state);
            using MonitoredItem item = fixture.CreateItem();

            AssertStatus(() => item.QueueEvent(new EventFieldList { Handle = state, EventFields = [s_otherId] }),
                StatusCodes.BadNotSupported);
        }

        [Test]
        public void SelectedFieldsCannotChangeTheirIdentityAfterQueueAdmission()
        {
            using var fixture = new IdentityFixture();
            EnableAdmission(fixture);
            using MonitoredItem item = fixture.CreateItem();
            item.QueueEvent(Event(s_id));
            var notifications = new Queue<EventFieldList>();
            using var context = NewContext();
            item.Publish(context, notifications, 8);
            Assert.That(notifications, Has.Count.EqualTo(1));
            EventFieldList fields = notifications.Dequeue();
            fields.EventFields = [s_otherId];

            AssertStatus(() => item.QueueEvent(fields), StatusCodes.BadSecurityChecksFailed);
        }

        [Test]
        public async Task AsyncEventManagerHelperCannotBypassNativeOwnershipAsync()
        {
            using var fixture = new IdentityFixture();
            using EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin());
            using MonitoredItem item = fixture.CreateItem();
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.Setup(manager => manager.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            ServiceResultException? error = null;
            try
            {
                await EventManager.ReportEventAsync(Event(s_id), nodeManager.Object, [item]).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                error = exception;
            }

            Assert.That(error, Is.Not.Null);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void QueuedAndPublishedFieldsOutliveTheirGenerationReservation()
        {
            using var fixture = new IdentityFixture(1);
            using MonitoredItem item = fixture.CreateItem();
            EnqueueProjectedOccurrence(fixture, item);
            CollectUnownedTargets();
            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, new Origin()).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
            PublishAndKeepIdentity(fixture, item);
            CollectUnownedTargets();

            using EventManager.EventIdentityReservation reclaimed =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin());
            BaseEventState replacement = Event(s_id);
            reclaimed.Attach(fixture.Context, replacement);
            fixture.Manager.AdmitEvent(fixture.Context, replacement);
            Assert.That(replacement.EventId!.Value, Is.EqualTo(s_id));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SentMessageDrainReleasesOwnershipBeforePayloadPoolReuse(bool acknowledge)
        {
            using var fixture = new IdentityFixture(1);
            using MonitoredItem item = fixture.CreateItem();
            DrainSentMessage(fixture, item, acknowledge);
            CollectUnownedTargets();

            item.QueueEvent(Event(s_otherId));
            var notifications = new Queue<EventFieldList>();
            using var context = NewContext();
            item.Publish(context, notifications, 8);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications.Peek().EventFields[0].TryGetValue(out ByteString id), Is.True);
            Assert.That(id, Is.EqualTo(s_otherId));
        }

        [Test]
        public void IndependentServersDoNotShareIdentityOwnership()
        {
            using var first = new IdentityFixture();
            using var second = new IdentityFixture();
            first.Manager.AdmitEvent(first.Context, Event(s_id));
            using EventManager.EventIdentityReservation reservation =
                second.Manager.ReserveEventIdentity(s_id, new Origin());
            BaseEventState projected = Event(s_id);
            reservation.Attach(second.Context, projected);
            second.Manager.AdmitEvent(second.Context, projected);
            Assert.That(projected.EventId!.Value, Is.EqualTo(s_id));
        }

        [Test]
        public void RetainedTargetKeepsIdentityAfterReservationDisposal()
        {
            using var fixture = new IdentityFixture(1);
            LimitAlarmState retained = Condition(s_id);
            using (EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin()))
            {
                reservation.Attach(fixture.Context, retained);
            }
            CollectUnownedTargets();
            var refresh = new InstanceStateSnapshot();
            refresh.Initialize(fixture.Context, retained);
            fixture.Manager.AdmitEvent(fixture.Context, refresh);

            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, new Origin()).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
            GC.KeepAlive(retained);
            GC.KeepAlive(refresh);
        }

        private static void EnableAdmission(IdentityFixture fixture)
        {
            using EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_otherId, new Origin());
        }

        private static void AssertStatus(Action action, StatusCode expected)
        {
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() => action());
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
        }

        private static BaseEventState Event(ByteString id)
        {
            var state = new BaseEventState(null) { TypeDefinitionId = ObjectTypeIds.BaseEventType };
            PopulateEvent(state, id);
            return state;
        }

        private static LimitAlarmState Condition(ByteString id)
        {
            var state = new LimitAlarmState(null)
            {
                NodeId = new NodeId("condition", 1),
                TypeDefinitionId = ObjectTypeIds.LimitAlarmType
            };
            PopulateEvent(state, id);
            state.BranchId = PropertyState<NodeId>.With<VariantBuilder>(state, NodeId.Null);
            state.Retain = PropertyState<bool>.With<VariantBuilder>(state, true);
            state.HighLimit = PropertyState<double>.With<VariantBuilder>(state, 100);
            state.BranchId.BrowseName = QualifiedName.From(BrowseNames.BranchId);
            state.Retain.BrowseName = QualifiedName.From(BrowseNames.Retain);
            state.HighLimit.BrowseName = QualifiedName.From(BrowseNames.HighLimit);
            return state;
        }

        private static void PopulateEvent(BaseEventState state, ByteString id)
        {
            state.EventId = PropertyState<ByteString>.With<VariantBuilder>(state, id);
            state.EventType = PropertyState<NodeId>.With<VariantBuilder>(state, state.TypeDefinitionId);
            state.SourceNode = PropertyState<NodeId>.With<VariantBuilder>(state, new NodeId("source", 1));
            state.SourceName = PropertyState<string>.With<VariantBuilder>(state, "source");
            state.Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, s_time);
            state.ReceiveTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, s_time);
            state.Message = PropertyState<LocalizedText>.With<VariantBuilder>(state, new LocalizedText("occurrence"));
            state.Severity = PropertyState<ushort>.With<VariantBuilder>(state, 412);
            state.EventId.BrowseName = QualifiedName.From(BrowseNames.EventId);
            state.EventType.BrowseName = QualifiedName.From(BrowseNames.EventType);
            state.SourceNode.BrowseName = QualifiedName.From(BrowseNames.SourceNode);
            state.SourceName.BrowseName = QualifiedName.From(BrowseNames.SourceName);
            state.Time.BrowseName = QualifiedName.From(BrowseNames.Time);
            state.ReceiveTime.BrowseName = QualifiedName.From(BrowseNames.ReceiveTime);
            state.Message.BrowseName = QualifiedName.From(BrowseNames.Message);
            state.Severity.BrowseName = QualifiedName.From(BrowseNames.Severity);
        }

        private static OperationContext NewContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnqueueProjectedOccurrence(IdentityFixture fixture, MonitoredItem item)
        {
            using EventManager.EventIdentityReservation reservation =
                fixture.Manager.ReserveEventIdentity(s_id, new Origin());
            BaseEventState state = Event(s_id);
            reservation.Attach(fixture.Context, state);
            item.QueueEvent(state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PublishAndKeepIdentity(IdentityFixture fixture, MonitoredItem item)
        {
            var published = new Queue<EventFieldList>();
            using var context = NewContext();
            item.Publish(context, published, 8);
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published.Peek().EventFields[0].TryGetValue(out ByteString id), Is.True);
            Assert.That(id, Is.EqualTo(s_id));
            CollectUnownedTargets();
            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, new Origin()).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
            GC.KeepAlive(published);
        }

        private static void CollectUnownedTargets()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DrainSentMessage(IdentityFixture fixture, MonitoredItem item, bool acknowledge)
        {
            EnqueueProjectedOccurrence(fixture, item);
            var published = new Queue<EventFieldList>();
            using var context = NewContext();
            item.Publish(context, published, 8);
            Assert.That(published, Has.Count.EqualTo(1));
            var sent = new SentMessageQueue(
                () => 1, 1, null,
                fixture.Context.Telemetry.CreateLogger<EventIdentityAdmissionTests>(), fixture.Manager);
            var message = (NotificationMessage)NotificationMessageActivator.Instance.CreateInstance();
            message.SequenceNumber = sent.AssignSequenceNumber();
            var events = (EventNotificationList)EventNotificationListActivator.Instance.CreateInstance();
            events.Events = published.ToArrayOf();
            message.NotificationData = [new ExtensionObject(events)];
            _ = sent.Enqueue([message], [], out _, out _);
            CollectUnownedTargets();
            AssertStatus(() => fixture.Manager.ReserveEventIdentity(s_id, new Origin()).Dispose(),
                StatusCodes.BadSecurityChecksFailed);
            if (acknowledge)
            {
                Assert.That(sent.TryAcknowledge(message.SequenceNumber), Is.True);
            }
            else
            {
                sent.Clear();
            }
        }

        private sealed class Origin : IEventIdentitySource
        {
            public bool IsSameOccurrence(IEventIdentitySource other)
            {
                return ReferenceEquals(this, other);
            }
        }

        private sealed class TrustsAnyOrigin : IEventIdentitySource
        {
            public bool IsSameOccurrence(IEventIdentitySource other)
            {
                return true;
            }
        }

        private sealed class IdentityFixture : IDisposable
        {
            public IdentityFixture(int capacity = 16)
            {
                m_server = DeterministicServerMock.Create(out _);
                m_server.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
                var types = new TypeTable(m_server.Object.NamespaceUris);
                types.AddSubtype(ObjectTypeIds.BaseEventType, NodeId.Null);
                types.AddSubtype(ObjectTypeIds.ConditionType, ObjectTypeIds.BaseEventType);
                types.AddSubtype(ObjectTypeIds.LimitAlarmType, ObjectTypeIds.ConditionType);
                m_server.Setup(server => server.TypeTree).Returns(types);
                Context = new ServerSystemContext(m_server.Object);
                m_server.Setup(server => server.DefaultSystemContext).Returns(Context);
                Manager = new EventManager(m_server.Object, 8, 8,
                    new EventIdentityAdmissionOptions { MaxEventIdentities = capacity });
                m_server.Setup(server => server.EventManager).Returns(Manager);
            }

            public EventManager Manager { get; }

            public ServerSystemContext Context { get; }

            public MonitoredItem CreateItem()
            {
                var request = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        QueueSize = 8,
                        ClientHandle = 1,
                        DiscardOldest = false
                    }
                };
                var filter = new EventFilter
                {
                    SelectClauses =
                    [
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            BrowsePath = [QualifiedName.From(BrowseNames.EventId)],
                            AttributeId = Attributes.Value
                        }
                    ]
                };
                using var context = NewContext();
                return (MonitoredItem)Manager.CreateMonitoredItem(
                    context, new Mock<IAsyncNodeManager>().Object, null!, 1, new MonitoredItemIdFactory(),
                    TimestampsToReturn.Neither, 0, request, filter, false);
            }

            public void Dispose()
            {
                Manager.Dispose();
            }

            private readonly Mock<IServerInternal> m_server;
        }

        private static readonly ByteString s_id = ByteString.From(new byte[] { 0xD3, 0xC3, 0x01 });
        private static readonly ByteString s_otherId = ByteString.From(new byte[] { 0xD3, 0xC3, 0x02 });
        private static readonly DateTimeUtc s_time = new(2026, 9, 18, 12, 0, 0);
        private static readonly DateTimeUtc s_laterReceipt = new(2026, 9, 18, 12, 0, 20);
    }
}
