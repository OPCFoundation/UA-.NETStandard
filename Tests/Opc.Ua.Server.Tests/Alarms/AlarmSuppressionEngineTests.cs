/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.Alarms;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Alarms
{
    [TestFixture, Category("AlarmSuppressionEngine"), Parallelizable]
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

            engine.RegisterSuppressionGroup(group, () => sourceVal, new[] { a1, a2 });

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

            engine.RegisterSuppressionGroup(group, () => sourceVal, new[] { a1 });

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

            engine.RegisterFirstInGroupAlarm(first, group, new[] { other1, other2 });

            engine.OnFirstInGroupActiveChanged(m_context, first, group, firstActive: true);

            Assert.That(other1.SuppressedState.Id.Value, Is.True);
            Assert.That(other2.SuppressedState.Id.Value, Is.True);

            engine.OnFirstInGroupActiveChanged(m_context, first, group, firstActive: false);
            Assert.That(other1.SuppressedState.Id.Value, Is.False);
            Assert.That(other2.SuppressedState.Id.Value, Is.False);
        }
    }

    [TestFixture, Category("AlarmGroup"), Parallelizable]
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

            List<NodeId> members = new();
            foreach (NodeId id in group.GetMemberIds(m_context))
            {
                members.Add(id);
            }
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
    }
}