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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Part 9 compliance of the condition methods (shelving, Acknowledge, Confirm,
    /// AddComment, Respond) and of the event snapshot used to report conditions.
    /// </summary>
    [TestFixture]
    [Category("ConditionState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConditionMethodComplianceTests
    {
        private ISystemContext m_context;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris
            };
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (m_context as IDisposable)?.Dispose();
        }

        /// <summary>
        /// S1-1: a one shot shelve without MaxTimeShelved lasts until unshelved; it must
        /// neither overflow the unshelve timer nor expire after int.MaxValue milliseconds.
        /// </summary>
        [Test]
        public void OneShotShelveWithoutMaxTimeShelvedNeverExpires()
        {
            var time = new FakeTimeProvider();
            TestAlarm alarm = CreateShelvableAlarm(time, out int[] timedUnshelveCount);

            Assert.DoesNotThrow(() => alarm.SetShelvingState(m_context, true, true, 0));

            Assert.That(alarm.UnshelveTime, Is.EqualTo(DateTime.MaxValue));
            Assert.That(alarm.ShelvingState!.UnshelveTime!.Value, Is.EqualTo(double.MaxValue));

            time.Advance(TimeSpan.FromDays(60));

            Assert.That(timedUnshelveCount[0], Is.Zero);
            Assert.That(alarm.ShelvingState.CurrentState!.Id!.Value,
                Is.EqualTo(ObjectIds.ShelvedStateMachineType_OneShotShelved));
        }

        /// <summary>
        /// S1-1: a timed shelve beyond the maximum timer due time expires at the requested
        /// time, not at int.MaxValue milliseconds.
        /// </summary>
        [Test]
        public void TimedShelveBeyondTimerRangeExpiresAtRequestedTime()
        {
            var time = new FakeTimeProvider();
            TestAlarm alarm = CreateShelvableAlarm(time, out int[] timedUnshelveCount);
            double shelveTime = TimeSpan.FromDays(60).TotalMilliseconds;

            Assert.DoesNotThrow(() => alarm.SetShelvingState(m_context, true, false, shelveTime));

            time.Advance(TimeSpan.FromDays(50));
            Assert.That(timedUnshelveCount[0], Is.Zero);

            time.Advance(TimeSpan.FromDays(10) + TimeSpan.FromSeconds(1));
            Assert.That(timedUnshelveCount[0], Is.EqualTo(1));
        }

        /// <summary>
        /// A4-5: a backward step of the wall clock does not delay the timed unshelve; the
        /// shelve lasts the requested duration.
        /// </summary>
        [Test]
        public void TimedShelveIgnoresBackwardClockStep()
        {
            var time = new SteppedTimeProvider();
            TestAlarm alarm = CreateAlarm(time);
            alarm.ShelvingState = new ShelvedStateMachineState(alarm);
            alarm.ShelvingState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ShelvingState), default, false);
            alarm.ShelvingState.UnshelveTime = PropertyState<double>.With<VariantBuilder>(alarm.ShelvingState);
            int timedUnshelveCount = 0;
            alarm.OnTimedUnshelve = (_, _) =>
            {
                timedUnshelveCount++;
                return ServiceResult.Good;
            };

            alarm.SetShelvingState(m_context, true, false, TimeSpan.FromMinutes(10).TotalMilliseconds);

            time.Advance(TimeSpan.FromMinutes(5));
            time.Step = TimeSpan.FromHours(-1);
            time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

            Assert.That(timedUnshelveCount, Is.EqualTo(1));
        }

        /// <summary>
        /// S1-5: the shelved state is named OneShotShelved (Part 9 Table 73), not after the method.
        /// </summary>
        [Test]
        public void OneShotShelvedStateHasTheStateName()
        {
            TestAlarm alarm = CreateShelvableAlarm(new FakeTimeProvider(), out _);

            alarm.SetShelvingState(m_context, true, true, 0);

            Assert.That(alarm.ShelvingState!.CurrentState!.Value.Text, Is.EqualTo(BrowseNames.OneShotShelved));
        }

        /// <summary>
        /// S1-3 addendum: a shelve whose handler throws is audited as failed.
        /// </summary>
        [Test]
        public void ThrowingShelveHandlerIsAuditedAsFailed()
        {
            TestAlarm alarm = CreateShelvableAlarm(new FakeTimeProvider(), out _);
            alarm.OnShelve = (_, _, _, _, _) => throw new InvalidOperationException("boom");
            List<IFilterTarget> events = MonitorEvents(alarm);

            Assert.Throws<InvalidOperationException>(
                () => alarm.CallTimedShelve(m_context, 1000));

            AuditConditionShelvingEventState audit = FindEvent<AuditConditionShelvingEventState>(events);
            Assert.That(audit.Status!.Value, Is.False);
        }

        /// <summary>
        /// S1-2: Acknowledge rejects unknown EventIds and already acknowledged states.
        /// </summary>
        [Test]
        public void AcknowledgeRejectsUnknownEventIdAndAlreadyAcked()
        {
            TestAlarm alarm = CreateAlarm();
            alarm.SetAcknowledgedState(m_context, false);
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();

            ServiceResult unknown = alarm.CallAcknowledge(m_context, Uuid.NewUuid().ToByteString());
            Assert.That(unknown.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(alarm.AckedState!.Id!.Value, Is.False);

            ServiceResult first = alarm.CallAcknowledge(m_context, alarm.EventId.Value);
            Assert.That(ServiceResult.IsGood(first), Is.True, first.ToString());
            Assert.That(alarm.AckedState.Id.Value, Is.True);

            ServiceResult second = alarm.CallAcknowledge(m_context, alarm.EventId.Value);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadConditionBranchAlreadyAcked));
        }

        /// <summary>
        /// S1-2: Confirm rejects unknown EventIds and already confirmed states.
        /// </summary>
        [Test]
        public void ConfirmRejectsUnknownEventIdAndAlreadyConfirmed()
        {
            TestAlarm alarm = CreateAlarm();
            alarm.ConfirmedState = new TwoStateVariableState(alarm);
            alarm.ConfirmedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ConfirmedState), default, false);
            alarm.SetConfirmedState(m_context, false);
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();

            ServiceResult unknown = alarm.CallConfirm(m_context, Uuid.NewUuid().ToByteString());
            Assert.That(unknown.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(alarm.ConfirmedState.Id!.Value, Is.False);

            ByteString confirmedEventId = alarm.EventId.Value;
            ServiceResult first = alarm.CallConfirm(m_context, confirmedEventId);
            Assert.That(ServiceResult.IsGood(first), Is.True, first.ToString());
            Assert.That(alarm.ConfirmedState.Id.Value, Is.True);

            ServiceResult second = alarm.CallConfirm(m_context, alarm.EventId.Value);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadConditionBranchAlreadyConfirmed));

            // the EventId of the confirmed state stays "already confirmed" after a new state was reported.
            alarm.EventId.Value = Uuid.NewUuid().ToByteString();
            ServiceResult stale = alarm.CallConfirm(m_context, confirmedEventId);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadConditionBranchAlreadyConfirmed));
        }

        /// <summary>
        /// A4-3: an EventId superseded by a later state change (here AddComment) still
        /// identifies its unacknowledged state, so Acknowledge and Confirm accept it.
        /// </summary>
        [Test]
        public void SupersededEventIdOfUnackedStateCanBeAcknowledgedAndConfirmed()
        {
            TestAlarm alarm = CreateAlarm();
            alarm.AutoReportStateChanges = true;
            alarm.ConfirmedState = new TwoStateVariableState(alarm);
            alarm.ConfirmedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ConfirmedState), default, false);
            alarm.SetConfirmedState(m_context, true);
            alarm.SetAcknowledgedState(m_context, false);
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            MonitorEvents(alarm);
            ByteString t1 = alarm.EventId.Value;

            ServiceResult comment = alarm.CallAddComment(m_context, t1, LocalizedText.From("x"));
            Assert.That(ServiceResult.IsGood(comment), Is.True, comment.ToString());
            Assert.That(alarm.EventId.Value, Is.Not.EqualTo(t1));

            ServiceResult ack = alarm.CallAcknowledge(m_context, t1);
            Assert.That(ServiceResult.IsGood(ack), Is.True, ack.ToString());
            Assert.That(alarm.AckedState!.Id!.Value, Is.True);

            ServiceResult again = alarm.CallAcknowledge(m_context, t1);
            Assert.That(again.StatusCode, Is.EqualTo(StatusCodes.BadConditionBranchAlreadyAcked));

            // the acknowledgement left the condition unconfirmed; comment again, confirm the old EventId.
            ByteString t2 = alarm.EventId.Value;
            comment = alarm.CallAddComment(m_context, t2, LocalizedText.From("y"));
            Assert.That(ServiceResult.IsGood(comment), Is.True, comment.ToString());

            ServiceResult confirm = alarm.CallConfirm(m_context, t2);
            Assert.That(ServiceResult.IsGood(confirm), Is.True, confirm.ToString());
            Assert.That(alarm.ConfirmedState.Id!.Value, Is.True);

            // a new unacknowledged state does not make the acknowledged EventId acknowledgeable again.
            alarm.SetAcknowledgedState(m_context, false);
            ServiceResult stale = alarm.CallAcknowledge(m_context, t1);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadConditionBranchAlreadyAcked));
            Assert.That(alarm.AckedState.Id.Value, Is.False);
        }

        /// <summary>
        /// A4-3: a branch EventId superseded by a comment on the branch acknowledges the branch.
        /// </summary>
        [Test]
        public void SupersededBranchEventIdAcknowledgesTheBranch()
        {
            TestAlarm alarm = CreateAlarm();
            alarm.AutoReportStateChanges = true;
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            MonitorEvents(alarm);

            ConditionState branch = alarm.CreateBranch(m_context, new NodeId(42))!;
            Assert.That(branch, Is.Not.Null);
            ((AcknowledgeableConditionState)branch).SetAcknowledgedState(m_context, false);
            ByteString b1 = branch.EventId!.Value;

            ServiceResult comment = alarm.CallAddComment(m_context, b1, LocalizedText.From("branch"));
            Assert.That(ServiceResult.IsGood(comment), Is.True, comment.ToString());
            Assert.That(branch.EventId.Value, Is.Not.EqualTo(b1));

            ServiceResult ack = alarm.CallAcknowledge(m_context, b1);
            Assert.That(ServiceResult.IsGood(ack), Is.True, ack.ToString());
            Assert.That(((AcknowledgeableConditionState)branch).AckedState!.Id!.Value, Is.True);
            Assert.That(alarm.GetBranchCount(), Is.Zero);

            ServiceResult again = alarm.CallAcknowledge(m_context, b1);
            Assert.That(again.StatusCode, Is.EqualTo(StatusCodes.BadConditionBranchAlreadyAcked));
        }

        /// <summary>
        /// A4-4: a comment on a branch re-keys the branch table by the new EventId, so the
        /// branch is held (and refreshed) once and Acknowledge + Confirm remove it for good.
        /// </summary>
        [Test]
        public void BranchCommentKeepsBranchTableKeyedByCurrentEventId()
        {
            TestAlarm alarm = CreateAlarm();
            alarm.AutoReportStateChanges = true;
            alarm.ConfirmedState = new TwoStateVariableState(alarm);
            alarm.ConfirmedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ConfirmedState), default, false);
            alarm.SetConfirmedState(m_context, true);
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            MonitorEvents(alarm);

            var branch = (AcknowledgeableConditionState)alarm.CreateBranch(m_context, new NodeId(42))!;
            Assert.That(branch, Is.Not.Null);
            branch.SetAcknowledgedState(m_context, false);
            branch.SetConfirmedState(m_context, false);
            ByteString b1 = branch.EventId!.Value;

            ServiceResult comment = alarm.CallAddComment(m_context, b1, LocalizedText.From("branch"));
            Assert.That(ServiceResult.IsGood(comment), Is.True, comment.ToString());
            Assert.That(branch.EventId.Value, Is.Not.EqualTo(b1));
            Assert.That(alarm.GetBranches().Keys, Is.EquivalentTo(new[] { branch.EventId.Value.ToHexString() }));

            ServiceResult ack = alarm.CallAcknowledge(m_context, branch.EventId.Value);
            Assert.That(ServiceResult.IsGood(ack), Is.True, ack.ToString());
            Assert.That(alarm.GetBranchCount(), Is.EqualTo(1));

            alarm.Retain!.Value = true;
            var refreshed = new List<IFilterTarget>();
            alarm.ConditionRefresh(m_context, refreshed, true);
            Assert.That(refreshed.FindAll(e => ReferenceEquals(e, branch)), Has.Count.EqualTo(1));

            ServiceResult confirm = alarm.CallConfirm(m_context, branch.EventId.Value);
            Assert.That(ServiceResult.IsGood(confirm), Is.True, confirm.ToString());
            Assert.That(alarm.GetBranchCount(), Is.Zero);
        }

        /// <summary>
        /// A4-4: replacing or removing a branch entry by EventId never leaves a second entry
        /// of the same branch behind.
        /// </summary>
        [Test]
        public void ReplaceAndRemoveBranchEventKeepOneEntryPerBranch()
        {
            TestAlarm alarm = CreateAlarm();
            alarm.AutoReportStateChanges = true;
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();

            ConditionState branch = alarm.CreateBranch(m_context, new NodeId(42))!;
            Assert.That(branch, Is.Not.Null);
            ByteString b1 = branch.EventId!.Value;
            ServiceResult comment = alarm.CallAddComment(m_context, b1, LocalizedText.From("branch"));
            Assert.That(ServiceResult.IsGood(comment), Is.True, comment.ToString());

            // an original EventId the table does not hold must not leave the old entry behind.
            alarm.CallReplaceBranchEvent(Uuid.NewUuid().ToByteString(), branch);
            Assert.That(alarm.GetBranchCount(), Is.EqualTo(1));

            alarm.CallRemoveBranchEvent(branch.EventId.Value);
            Assert.That(alarm.GetBranchCount(), Is.Zero);
        }

        /// <summary>
        /// S1-3: AddComment rejects unknown EventIds and a branch comment only goes to the branch.
        /// </summary>
        [Test]
        public void AddCommentTargetsTheIdentifiedEventOnly()
        {
            TestAlarm alarm = CreateAlarm();
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            alarm.SetComment(m_context, LocalizedText.From("trunk"), "user");

            ServiceResult unknown = alarm.CallAddComment(
                m_context, Uuid.NewUuid().ToByteString(), LocalizedText.From("lost"));
            Assert.That(unknown.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(alarm.Comment!.Value.Text, Is.EqualTo("trunk"));

            ConditionState branch = alarm.CreateBranch(m_context, new NodeId(42))!;
            Assert.That(branch, Is.Not.Null);

            ServiceResult result = alarm.CallAddComment(
                m_context, branch.EventId!.Value, LocalizedText.From("branch"));

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(branch.Comment!.Value.Text, Is.EqualTo("branch"));
            Assert.That(alarm.Comment.Value.Text, Is.EqualTo("trunk"));
        }

        /// <summary>
        /// S1-6: selecting the NodeId attribute of a Variable returns its NodeId, not its Value,
        /// and a path below a SetChildValue leaf yields null instead of throwing.
        /// </summary>
        [Test]
        public void SnapshotReturnsNodeIdAttributeOfVariables()
        {
            TestAlarm alarm = CreateAlarm();
            Assert.That(alarm.EnabledState!.NodeId.IsNull, Is.False);

            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(m_context, alarm);

            Variant nodeId = snapshot.GetAttributeValue(
                null!,
                default,
                [QualifiedName.From(BrowseNames.EnabledState)],
                Attributes.NodeId,
                default);
            Assert.That(nodeId, Is.EqualTo(new Variant(alarm.EnabledState.NodeId)));

            Variant conditionId = snapshot.GetAttributeValue(null!, default, [], Attributes.NodeId, default);
            Assert.That(conditionId, Is.EqualTo(new Variant(alarm.NodeId)));

            snapshot.SetChildValue(QualifiedName.From("Leaf"), NodeClass.Variable, Variant.From(5));
            Variant below = default;
            Assert.DoesNotThrow(() => below = snapshot.GetAttributeValue(
                null!,
                default,
                [QualifiedName.From("Leaf"), QualifiedName.From("Below")],
                Attributes.Value,
                default));
            Assert.That(below.IsNull, Is.True);
        }

        /// <summary>
        /// S1-7: AuditConditionRespondEvent.SelectedResponse is a UInt32 (Part 9 Table 125).
        /// </summary>
        [Test]
        public void RespondAuditEventCarriesUInt32SelectedResponse()
        {
            var dialog = new TestDialog(null!);
            dialog.Create(m_context, new NodeId(2), QualifiedName.From("Dialog"), default, true);
            dialog.SetEnableState(m_context, true);
            dialog.DialogState!.Id!.Value = true;
            dialog.ResponseOptionSet!.Value = [LocalizedText.From("Yes"), LocalizedText.From("No")];
            dialog.OnRespond = (_, _, _) => ServiceResult.Good;
            List<IFilterTarget> events = MonitorEvents(dialog);

            ServiceResult result = dialog.CallRespond(m_context, 1);
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());

            AuditConditionRespondEventState audit = FindEvent<AuditConditionRespondEventState>(events);
            var property = audit.FindChild(
                m_context, QualifiedName.From(BrowseNames.SelectedResponse)) as BaseVariableState;
            Assert.That(property, Is.Not.Null);
            Variant selected = property.WrappedValue;
            Assert.That(selected.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt32));
            Assert.That(selected, Is.EqualTo(Variant.From(1u)));
        }

        /// <summary>
        /// A4-8: a rejected negative response is not audited as the valid option 0.
        /// </summary>
        [Test]
        public void RejectedNegativeRespondIsNotAuditedAsOptionZero()
        {
            var dialog = new TestDialog(null!);
            dialog.Create(m_context, new NodeId(2), QualifiedName.From("Dialog"), default, true);
            dialog.SetEnableState(m_context, true);
            dialog.DialogState!.Id!.Value = true;
            dialog.ResponseOptionSet!.Value = [LocalizedText.From("Yes"), LocalizedText.From("No")];
            dialog.OnRespond = (_, _, _) => ServiceResult.Good;
            List<IFilterTarget> events = MonitorEvents(dialog);

            ServiceResult result = dialog.CallRespond(m_context, -1);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadDialogResponseInvalid));

            AuditConditionRespondEventState audit = FindEvent<AuditConditionRespondEventState>(events);
            Assert.That(audit.Status!.Value, Is.False);
            var property = audit.FindChild(
                m_context, QualifiedName.From(BrowseNames.SelectedResponse)) as BaseVariableState;
            Assert.That(property == null || property.WrappedValue.IsNull, Is.True,
                property?.WrappedValue.ToString());
        }

        private TestAlarm CreateAlarm(TimeProvider? timeProvider = null)
        {
            var alarm = new TestAlarm(m_telemetry, null!, timeProvider!);
            alarm.Create(m_context, new NodeId(1), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(m_context, true);
            return alarm;
        }

        private TestAlarm CreateShelvableAlarm(FakeTimeProvider time, out int[] timedUnshelveCount)
        {
            TestAlarm alarm = CreateAlarm(time);
            alarm.ShelvingState = new ShelvedStateMachineState(alarm);
            alarm.ShelvingState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ShelvingState), default, false);
            alarm.ShelvingState.UnshelveTime = PropertyState<double>.With<VariantBuilder>(alarm.ShelvingState);

            int[] count = new int[1];
            alarm.OnTimedUnshelve = (_, _) =>
            {
                count[0]++;
                return ServiceResult.Good;
            };
            alarm.OnShelve = (context, a, shelving, oneShot, shelvingTime) =>
            {
                a.SetShelvingState(context, shelving, oneShot, shelvingTime);
                return ServiceResult.Good;
            };
            timedUnshelveCount = count;
            return alarm;
        }

        private List<IFilterTarget> MonitorEvents(ConditionState condition)
        {
            var events = new List<IFilterTarget>();
            condition.SetAreEventsMonitored(m_context, true, false);
            condition.OnReportEvent = (_, _, e) => events.Add(e);
            return events;
        }

        private static T FindEvent<T>(List<IFilterTarget> events)
            where T : class
        {
            foreach (IFilterTarget e in events)
            {
                if (e is T match)
                {
                    return match;
                }
            }

            Assert.Fail($"No {typeof(T).Name} was reported.");
            return null!;
        }

        /// <summary>
        /// Exposes the protected method handlers of the alarm.
        /// </summary>
        private sealed class TestAlarm : AlarmConditionState
        {
            public TestAlarm(NodeState parent)
                : base(parent)
            {
            }

            public TestAlarm(ITelemetryContext telemetry, NodeState parent, TimeProvider timeProvider)
                : base(telemetry, parent, timeProvider)
            {
            }

            public ServiceResult CallAcknowledge(ISystemContext context, ByteString eventId)
            {
                return OnAcknowledgeCalled(context, Acknowledge!, NodeId, eventId, LocalizedText.From("ack"));
            }

            public ServiceResult CallConfirm(ISystemContext context, ByteString eventId)
            {
                return OnConfirmCalled(context, new MethodState(this), NodeId, eventId, LocalizedText.From("confirm"));
            }

            public ServiceResult CallAddComment(ISystemContext context, ByteString eventId, LocalizedText comment)
            {
                return OnAddCommentCalled(context, AddComment!, NodeId, eventId, comment);
            }

            public void CallReplaceBranchEvent(ByteString originalEventId, ConditionState branch)
            {
                ReplaceBranchEvent(originalEventId, branch);
            }

            public void CallRemoveBranchEvent(ByteString eventId)
            {
                RemoveBranchEvent(eventId);
            }

            public ServiceResult CallTimedShelve(ISystemContext context, double shelvingTime)
            {
                return OnTimedShelve(context, ShelvingState!.TimedShelve ?? new MethodState(this), NodeId, shelvingTime);
            }
        }

        /// <summary>
        /// A fake time provider whose wall clock can be stepped while the monotonic
        /// timestamp and the timers keep running on the fake time.
        /// </summary>
        private sealed class SteppedTimeProvider : TimeProvider
        {
            private readonly FakeTimeProvider m_time = new();

            /// <summary>
            /// The offset applied to the wall clock.
            /// </summary>
            public TimeSpan Step { get; set; }

            public void Advance(TimeSpan delta)
            {
                m_time.Advance(delta);
            }

            public override DateTimeOffset GetUtcNow()
            {
                return m_time.GetUtcNow() + Step;
            }

            public override long GetTimestamp()
            {
                return m_time.GetTimestamp();
            }

            public override long TimestampFrequency => m_time.TimestampFrequency;

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                return m_time.CreateTimer(callback, state, dueTime, period);
            }
        }

        /// <summary>
        /// Exposes the protected Respond handler of the dialog.
        /// </summary>
        private sealed class TestDialog : DialogConditionState
        {
            public TestDialog(NodeState parent)
                : base(parent)
            {
            }

            public ServiceResult CallRespond(ISystemContext context, int selectedResponse)
            {
                return OnRespondCalled(context, Respond ?? new MethodState(this), NodeId, selectedResponse);
            }
        }
    }
}
