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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.Alarms;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Alarms
{
    [TestFixture]
    [Category("AlarmSuppressionEngine")]
    [Parallelizable]
    public class AlarmSuppressionEngineTests
    {
        private ISystemContext m_context = null!;
        private ITelemetryContext m_telemetry = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris
            };
        }

        private AlarmConditionState CreateAlarm(uint id)
        {
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_context, new NodeId(id), QualifiedName.From("a" + id), default, true);
            alarm.SetEnableState(m_context, true);
            var sup = new TwoStateVariableState(alarm);
            sup.Create(m_context, default, QualifiedName.From(BrowseNames.SuppressedState), default, false);
            alarm.SuppressedState = sup;
            return alarm;
        }

        private AlarmGroupState CreateGroup(uint id)
        {
            var g = new AlarmGroupState(null);
            g.Create(m_context, new NodeId(id), QualifiedName.From("g" + id), default, true);
            return g;
        }

        [Test]
        public void EvaluateAppliesSuppressionWhenSourceBecomesTrue()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(100);
            AlarmConditionState a1 = CreateAlarm(101);
            AlarmConditionState a2 = CreateAlarm(102);
            bool sourceVal = false;

            engine.RegisterSuppressionGroup(group, () => sourceVal, [a1, a2]);

            sourceVal = true;
            engine.Evaluate(m_context);

            Assert.That(a1.SuppressedState.Id.Value, Is.True);
            Assert.That(a2.SuppressedState.Id.Value, Is.True);
            Assert.That(a1.SuppressedOrShelved.Value, Is.True);
        }

        [Test]
        public void EvaluateClearsSuppressionWhenSourceBecomesFalse()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(200);
            AlarmConditionState a1 = CreateAlarm(201);
            bool sourceVal = true;

            engine.RegisterSuppressionGroup(group, () => sourceVal, [a1]);

            engine.Evaluate(m_context);
            Assert.That(a1.SuppressedState.Id.Value, Is.True);

            sourceVal = false;
            engine.Evaluate(m_context);
            Assert.That(a1.SuppressedState.Id.Value, Is.False);
        }

        [Test]
        public void FirstInGroupSuppressesOtherMembersWhenActive()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(300);
            AlarmConditionState first = CreateAlarm(301);
            AlarmConditionState other1 = CreateAlarm(302);
            AlarmConditionState other2 = CreateAlarm(303);

            engine.RegisterFirstInGroupAlarm(first, group, [other1, other2]);

            engine.OnFirstInGroupActiveChanged(m_context, first, group, firstActive: true);

            Assert.That(other1.SuppressedState.Id.Value, Is.True);
            Assert.That(other2.SuppressedState.Id.Value, Is.True);

            engine.OnFirstInGroupActiveChanged(m_context, first, group, firstActive: false);
            Assert.That(other1.SuppressedState.Id.Value, Is.False);
            Assert.That(other2.SuppressedState.Id.Value, Is.False);
        }

        [Test]
        public void RegisterSuppressionGroupWithNullGroupThrows()
        {
            using var engine = new AlarmSuppressionEngine();
            Assert.That(
                () => engine.RegisterSuppressionGroup(
                    null!, () => false, []),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterSuppressionGroupWithNullSourceThrows()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(400);
            Assert.That(
                () => engine.RegisterSuppressionGroup(
                    group, null!, []),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterSuppressionGroupWithNullMembersThrows()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(410);
            Assert.That(
                () => engine.RegisterSuppressionGroup(group, () => false, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterFirstInGroupAlarmWithNullFirstAlarmThrows()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(420);
            Assert.That(
                () => engine.RegisterFirstInGroupAlarm(
                    null!, group, []),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterFirstInGroupAlarmWithNullGroupThrows()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmConditionState first = CreateAlarm(421);
            Assert.That(
                () => engine.RegisterFirstInGroupAlarm(
                    first, null!, []),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterFirstInGroupAlarmWithNullOtherMembersThrows()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(430);
            AlarmConditionState first = CreateAlarm(431);
            Assert.That(
                () => engine.RegisterFirstInGroupAlarm(first, group, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void EvaluateWithNullContextThrows()
        {
            using var engine = new AlarmSuppressionEngine();
            Assert.That(
                () => engine.Evaluate(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void EvaluateSwallowsExceptionsFromSourceAndContinuesOtherGroups()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState bad = CreateGroup(500);
            AlarmGroupState good = CreateGroup(501);
            AlarmConditionState badMember = CreateAlarm(502);
            AlarmConditionState goodMember = CreateAlarm(503);

            bool arm = false;
            engine.RegisterSuppressionGroup(
                bad,
                () => arm
                    ? throw new InvalidOperationException("boom")
                    : false,
                [badMember]);
            engine.RegisterSuppressionGroup(good, () => true, [goodMember]);

            arm = true;
            Assert.That(() => engine.Evaluate(m_context), Throws.Nothing);
            Assert.That(goodMember.SuppressedState.Id.Value, Is.True);
            Assert.That(badMember.SuppressedState.Id.Value, Is.False);
        }

        [Test]
        public void EvaluateIsIdempotentWhenSourceStateUnchanged()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(600);
            var member = new CountingAlarm(m_telemetry, null);
            member.Create(m_context, new NodeId(601U), QualifiedName.From("a601"), default, true);
            member.SetEnableState(m_context, true);
            var sup = new TwoStateVariableState(member);
            sup.Create(m_context, default, QualifiedName.From(BrowseNames.SuppressedState), default, false);
            member.SuppressedState = sup;

            engine.RegisterSuppressionGroup(group, () => true, [member]);

            engine.Evaluate(m_context);
            int afterFirst = member.SuppressedWriteCount;
            engine.Evaluate(m_context);
            int afterSecond = member.SuppressedWriteCount;

            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(afterSecond, Is.EqualTo(1));
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var engine = new AlarmSuppressionEngine();
            engine.Dispose();
            Assert.That(engine.Dispose, Throws.Nothing);
        }

        [Test]
        public void RegisterSuppressionGroupAfterDisposeThrowsObjectDisposed()
        {
            var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(700);
            engine.Dispose();
            Assert.That(
                () => engine.RegisterSuppressionGroup(
                    group, () => false, []),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void RegisterFirstInGroupAlarmAfterDisposeThrowsObjectDisposed()
        {
            var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(710);
            AlarmConditionState first = CreateAlarm(711);
            engine.Dispose();
            Assert.That(
                () => engine.RegisterFirstInGroupAlarm(
                    first, group, []),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void EvaluateAfterDisposeThrowsObjectDisposed()
        {
            var engine = new AlarmSuppressionEngine();
            engine.Dispose();
            Assert.That(
                () => engine.Evaluate(m_context),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void OnFirstInGroupActiveChangedForUnregisteredGroupIsNoOp()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(800);
            AlarmConditionState first = CreateAlarm(801);
            AlarmConditionState other = CreateAlarm(802);

            // Do not register the group; the call must not throw and
            // must not mutate the other member's suppressed state.
            engine.OnFirstInGroupActiveChanged(m_context, first, group, firstActive: true);

            Assert.That(other.SuppressedState.Id.Value, Is.False);
        }

        [Test]
        public void OnFirstInGroupActiveChangedDoesNotSuppressFirstAlarmItself()
        {
            using var engine = new AlarmSuppressionEngine();
            AlarmGroupState group = CreateGroup(900);
            AlarmConditionState first = CreateAlarm(901);
            AlarmConditionState other = CreateAlarm(902);

            // first is intentionally included in otherMembers; the
            // ReferenceEquals guard must skip it.
            engine.RegisterFirstInGroupAlarm(first, group, [first, other]);

            engine.OnFirstInGroupActiveChanged(m_context, first, group, firstActive: true);

            Assert.That(first.SuppressedState.Id.Value, Is.False);
            Assert.That(other.SuppressedState.Id.Value, Is.True);
        }

        private sealed class CountingAlarm : AlarmConditionState
        {
            public CountingAlarm(ITelemetryContext telemetry, NodeState parent)
                : base(telemetry, parent)
            {
            }

            public int SuppressedWriteCount { get; private set; }

            public override void SetSuppressedState(ISystemContext context, bool suppressed)
            {
                SuppressedWriteCount++;
                base.SetSuppressedState(context, suppressed);
            }
        }
    }

    [TestFixture]
    [Category("AlarmGroup")]
    [Parallelizable]
    public class AlarmGroupTests
    {
        private ISystemContext m_context = null!;
        private ITelemetryContext m_telemetry = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris
            };
        }

        [Test]
        public void AddMemberAddsReference()
        {
            var groupState = new AlarmGroupState(null);
            groupState.Create(m_context, new NodeId(1000U), QualifiedName.From("g1000"), default, true);
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_context, new NodeId(1001U), QualifiedName.From("a1001"), default, true);

            var group = new AlarmGroup(groupState);
            group.AddMember(alarm);

            List<NodeId> members = [.. group.GetMemberIds(m_context)];
            Assert.That(members, Has.Count.EqualTo(1));
            Assert.That(members[0], Is.EqualTo(alarm.NodeId));
        }

        [Test]
        public void RemoveMemberRemovesReference()
        {
            var groupState = new AlarmGroupState(null);
            groupState.Create(m_context, new NodeId(2000U), QualifiedName.From("g2000"), default, true);
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_context, new NodeId(2001U), QualifiedName.From("a2001"), default, true);

            var group = new AlarmGroup(groupState);
            group.AddMember(alarm);
            group.RemoveMember(alarm);

            int count = 0;
            foreach (NodeId _ in group.GetMemberIds(m_context))
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void ConstructorWithNullStateThrowsArgumentNullException()
        {
            Assert.That(() => new AlarmGroup(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void AddMemberWithNullArgumentThrowsArgumentNullException()
        {
            var groupState = new AlarmGroupState(null);
            groupState.Create(m_context, new NodeId(3000U), QualifiedName.From("g3000"), default, true);
            var group = new AlarmGroup(groupState);

            Assert.That(() => group.AddMember(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void RemoveMemberWithNullArgumentThrowsArgumentNullException()
        {
            var groupState = new AlarmGroupState(null);
            groupState.Create(m_context, new NodeId(3100U), QualifiedName.From("g3100"), default, true);
            var group = new AlarmGroup(groupState);

            Assert.That(() => group.RemoveMember(null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void GetMemberIdsSkipsReferencesWithNullTarget()
        {
            var groupState = new AlarmGroupState(null);
            groupState.Create(m_context, new NodeId(3200U), QualifiedName.From("g3200"), default, true);

            // Add one valid member reference and one whose ExpandedNodeId
            // points to a namespace URI that is NOT registered in the
            // system context — ExpandedNodeId.ToNodeId returns NodeId.Null
            // in that case, exercising the GetMemberIds null-skip branch.
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_context, new NodeId(3201U), QualifiedName.From("a3201"), default, true);
            groupState.AddReference(ReferenceTypeIds.AlarmGroupMember, false, alarm.NodeId);
            var unresolvable = new ExpandedNodeId(
                new NodeId(1u), "urn:does:not:exist:in:namespace:table");
            groupState.AddReference(ReferenceTypeIds.AlarmGroupMember, false, unresolvable);

            var group = new AlarmGroup(groupState);
            List<NodeId> members = [.. group.GetMemberIds(m_context)];

            Assert.That(members, Has.Count.EqualTo(1));
            Assert.That(members[0], Is.EqualTo(alarm.NodeId));
        }

        [Test]
        public void StateAndNodeIdPropertiesExposeWrappedValues()
        {
            var groupState = new AlarmGroupState(null);
            var nodeId = new NodeId(3300U);
            groupState.Create(m_context, nodeId, QualifiedName.From("g3300"), default, true);

            var group = new AlarmGroup(groupState);

            Assert.That(group.State, Is.SameAs(groupState));
            Assert.That(group.NodeId, Is.EqualTo(nodeId));
        }
    }
}
