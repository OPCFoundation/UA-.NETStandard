// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Guards the default element-NodeId convention on
    /// <see cref="FiniteStateMachineState"/>. The
    /// <c>GetStateNodeId</c> / <c>GetStateId</c> /
    /// <c>GetTransitionNodeId</c> hooks exist so the fluent builder can
    /// point <c>CurrentState/Id</c> at per-instance state nodes; every
    /// stack-shipped and generator-emitted machine must keep reporting
    /// the numeric <c>(elementId, ElementNamespaceIndex)</c> form.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class FiniteStateMachineElementIdTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void StackStateMachineKeepsTheNumericElementConvention()
        {
            var sm = new ExclusiveLimitStateMachineState(null);
            sm.Create(
                m_context,
                new NodeId(4200, 1),
                new QualifiedName("LimitState", 1),
                new LocalizedText("LimitState"),
                true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    sm.GetStateNodeId(Objects.ExclusiveLimitStateMachineType_High),
                    Is.EqualTo(new NodeId(Objects.ExclusiveLimitStateMachineType_High)));
                Assert.That(
                    sm.GetTransitionNodeId(
                        Objects.ExclusiveLimitStateMachineType_HighToHighHigh),
                    Is.EqualTo(new NodeId(
                        Objects.ExclusiveLimitStateMachineType_HighToHighHigh)));
                Assert.That(
                    sm.GetStateId(new NodeId(Objects.ExclusiveLimitStateMachineType_Low)),
                    Is.EqualTo(Objects.ExclusiveLimitStateMachineType_Low));

                // OnAfterCreate seeds CurrentState with High.
                Assert.That(
                    sm.CurrentState!.Id!.Value,
                    Is.EqualTo(new NodeId(Objects.ExclusiveLimitStateMachineType_High)));
            });
        }

        [Test]
        public void ZeroElementIdsMapToNull()
        {
            var sm = new ExclusiveLimitStateMachineState(null);

            Assert.Multiple(() =>
            {
                Assert.That(sm.GetStateNodeId(0), Is.EqualTo(NodeId.Null));
                Assert.That(sm.GetTransitionNodeId(0), Is.EqualTo(NodeId.Null));
                Assert.That(sm.GetStateId(NodeId.Null), Is.Zero);
            });
        }
    }
}
