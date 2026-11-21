// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    [TestFixture]
    [Category("StateMachine")]
    public sealed class FiniteStateMachineDispatcherTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void MoveWritesCurrentStateAndLastTransition()
        {
            FluentFiniteStateMachineState machine = StateMachineTestFixtures
                .NewBuilder(m_context)
                .AddState(1, "Idle", isInitial: true)
                .AddState(2, "Ready")
                .AddTransition(10, "IdleToReady", from: 1, to: 2)
                .StateMachine;
            var dispatcher = new FiniteStateMachineDispatcher(
                1,
                [new FiniteStateMachineEntry(1, 100, "Idle"), new FiniteStateMachineEntry(2, 200, "Ready")],
                [new FiniteStateMachineEntry(10, 300, "IdleToReady")]);

            dispatcher.InitializeToInitialState(machine, 1, m_context);
            dispatcher.Move(machine, 2, 10, m_context);

            Assert.That(machine.CurrentState!.Value.Text, Is.EqualTo("Ready"));
            Assert.That(machine.CurrentState.Number!.Value, Is.EqualTo(200u));
            Assert.That(machine.LastTransition!.Value.Text, Is.EqualTo("IdleToReady"));
            Assert.That(machine.LastTransition.Number!.Value, Is.EqualTo(300u));
            Assert.That(dispatcher.TryGetCurrentState(machine, out uint stateId), Is.True);
            Assert.That(stateId, Is.EqualTo(2u));

            // The element NodeIds come from the machine, so for a
            // machine that materializes its own element nodes they
            // resolve to real, browsable nodes rather than to the
            // dispatcher's raw numeric convention.
            Assert.That(machine.CurrentState.Id!.Value,
                Is.EqualTo(machine.GetStateNodeId(2)));
            Assert.That(machine.LastTransition.Id!.Value,
                Is.EqualTo(machine.GetTransitionNodeId(10)));
            Assert.That(FindChild(machine, "Ready").NodeId,
                Is.EqualTo(machine.CurrentState.Id.Value));
            Assert.That(FindChild(machine, "IdleToReady").NodeId,
                Is.EqualTo(machine.LastTransition.Id.Value));
        }

        [Test]
        public void ApplyStateUsesTheMachinesElementNamespace()
        {
            // A machine that does NOT materialize element nodes keeps
            // the numeric convention, qualified by the namespace its
            // ElementNamespaceUri resolves to — the shape generated
            // companion state machines rely on. Create() is what
            // resolves that namespace, so this machine is created
            // properly rather than assembled by hand.
            var machine = new ExclusiveLimitStateMachineState(null);
            machine.Create(
                m_context,
                new NodeId(4400, 1),
                new QualifiedName("LimitState", 1),
                new LocalizedText("LimitState"),
                true);
            var dispatcher = new FiniteStateMachineDispatcher(
                0,
                [new FiniteStateMachineEntry(
                    Objects.ExclusiveLimitStateMachineType_Low, 3, "Low")],
                []);

            dispatcher.ApplyState(
                machine, Objects.ExclusiveLimitStateMachineType_Low, m_context);

            Assert.That(machine.CurrentState!.Id!.Value,
                Is.EqualTo(new NodeId(Objects.ExclusiveLimitStateMachineType_Low)));
            Assert.That(
                dispatcher.TryGetCurrentState(machine, out uint stateId), Is.True);
            Assert.That(stateId,
                Is.EqualTo(Objects.ExclusiveLimitStateMachineType_Low));
        }

        [Test]
        public void InitializeToInitialStateMaterializesLastTransition()
        {
            // The optional LastTransition (and its Number) must exist
            // by the time the machine is registered — minted later, at
            // the first ApplyTransition, the nodes would be browsable
            // but not readable.
            var machine = new ExclusiveLimitStateMachineState(null);
            machine.Create(
                m_context,
                new NodeId(4500, 1),
                new QualifiedName("LimitState", 1),
                new LocalizedText("LimitState"),
                true);
            machine.LastTransition?.Parent?.RemoveChild(machine.LastTransition);
            var dispatcher = new FiniteStateMachineDispatcher(
                0,
                [new FiniteStateMachineEntry(
                    Objects.ExclusiveLimitStateMachineType_High, 2, "High")],
                []);

            dispatcher.InitializeToInitialState(
                machine, Objects.ExclusiveLimitStateMachineType_High, m_context);

            Assert.Multiple(() =>
            {
                Assert.That(machine.LastTransition, Is.Not.Null);
                Assert.That(machine.LastTransition!.Number, Is.Not.Null);
                Assert.That(machine.LastTransition.Value.IsNullOrEmpty, Is.True);
            });
        }

        private BaseInstanceState FindChild(FluentFiniteStateMachineState parent, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            return children.Find(c => c.BrowseName.Name == browseName)!;
        }
    }
}
