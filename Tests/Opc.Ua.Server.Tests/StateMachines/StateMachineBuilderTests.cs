/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for the fluent <see cref="StateMachineBuilder"/>
    /// and <see cref="FluentFiniteStateMachineState"/> wrapper. Tests
    /// run against the validation logic and the resulting
    /// <see cref="StateMachineDefinition"/> shape — no server
    /// transport is needed.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderTests
    {
        [Test]
        public void BuildWithNoStatesThrows()
        {
            var b = new StateMachineBuilder();
            Assert.That(() => b.Build(),
                Throws.TypeOf<System.InvalidOperationException>());
        }

        [Test]
        public void DuplicateStateIdThrows()
        {
            var b = new StateMachineBuilder();
            b.AddState(1, "Off");
            Assert.That(() => b.AddState(1, "Off2"),
                Throws.ArgumentException);
        }

        [Test]
        public void DuplicateTransitionIdThrows()
        {
            var b = new StateMachineBuilder();
            b.AddState(1, "Off");
            b.AddState(2, "On");
            b.AddTransition(10, "OffToOn", from: 1, to: 2);
            Assert.That(() => b.AddTransition(10, "OffToOn2", from: 2, to: 1),
                Throws.ArgumentException);
        }

        [Test]
        public void TransitionWithUnknownFromStateFailsAtBuild()
        {
            var b = new StateMachineBuilder();
            b.AddState(1, "Off");
            b.AddTransition(10, "OffToOn", from: 99, to: 1);
            Assert.That(() => b.Build(),
                Throws.TypeOf<System.InvalidOperationException>());
        }

        [Test]
        public void CauseMappingWithUnknownTransitionFailsAtBuild()
        {
            var b = new StateMachineBuilder();
            b.AddState(1, "Off");
            b.OnCause(causeId: 100, from: 1, transition: 999);
            Assert.That(() => b.Build(),
                Throws.TypeOf<System.InvalidOperationException>());
        }

        [Test]
        public void TwoInitialStatesThrows()
        {
            var b = new StateMachineBuilder();
            b.AddState(1, "Off", isInitial: true);
            Assert.That(() => b.AddState(2, "On", isInitial: true),
                Throws.InvalidOperationException);
        }

        [Test]
        public void BuildProducesExpectedDefinition()
        {
            var def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .AddTransition(20, "OnToOff", from: 2, to: 1)
                .OnCause(causeId: 100, from: 1, transition: 10)
                .OnCause(causeId: 200, from: 2, transition: 20)
                .Build();

            Assert.That(def.States, Has.Count.EqualTo(2));
            Assert.That(def.Transitions, Has.Count.EqualTo(2));
            Assert.That(def.CauseMappings, Has.Count.EqualTo(2));
            Assert.That(def.InitialStateId, Is.EqualTo(1u));
        }

        [Test]
        public void FluentFiniteStateMachineExposesDefinition()
        {
            var def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .OnCause(causeId: 100, from: 1, transition: 10)
                .Build();

            var sm = new FluentFiniteStateMachineState(null!, def);
            Assert.That(sm.Definition, Is.SameAs(def));
        }
    }
}
