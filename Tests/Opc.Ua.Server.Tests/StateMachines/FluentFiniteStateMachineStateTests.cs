/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for <see cref="FluentFiniteStateMachineState"/>,
    /// exercising the protected table overrides through a derived
    /// probing subclass. Tests run via the unified
    /// <see cref="StateMachineBuilder{TState}"/>.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class FluentFiniteStateMachineStateTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void LegacyConstructorWithNullDefinitionThrows()
        {
            Assert.That(
                () => new FluentFiniteStateMachineState(null, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void TablesReflectDefinitionShape()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddState(3, "Fault")
                    .AddTransition(10, "OffToOn", from: 1, to: 2)
                    .AddTransition(11, "OnToOff", from: 2, to: 1)
                    .AddTransition(12, "OnToFault", from: 2, to: 3, hasEffect: false)
                    .OnCause(100, from: 1, transition: 10)
                    .OnCause(101, from: 2, transition: 11)
                    .StateMachine;

            var probe = ProbingView.Of(sm);

            Assert.That(probe.StateTableLength, Is.EqualTo(3));
            Assert.That(probe.TransitionTableLength, Is.EqualTo(3));
            Assert.That(probe.TransitionMappings.GetLength(0), Is.EqualTo(3));
            Assert.That(probe.TransitionMappings.GetLength(1), Is.EqualTo(4));
            Assert.That(probe.CauseMappings.GetLength(0), Is.EqualTo(2));
            Assert.That(probe.CauseMappings.GetLength(1), Is.EqualTo(3));
        }

        [Test]
        public void TransitionMappingsContainExpectedRows()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddTransition(10, "OffToOn", from: 1, to: 2)
                    .StateMachine;

            var probe = ProbingView.Of(sm);

            Assert.That(probe.TransitionMappings[0, 0], Is.EqualTo(10U));
            Assert.That(probe.TransitionMappings[0, 1], Is.EqualTo(1U));
            Assert.That(probe.TransitionMappings[0, 2], Is.EqualTo(2U));
            Assert.That(probe.TransitionMappings[0, 3], Is.EqualTo(1U));
        }

        [Test]
        public void CauseMappingsContainExpectedRows()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddTransition(10, "OffToOn", from: 1, to: 2)
                    .OnCause(100, from: 1, transition: 10)
                    .StateMachine;

            var probe = ProbingView.Of(sm);

            Assert.That(probe.CauseMappings[0, 0], Is.EqualTo(100U));
            Assert.That(probe.CauseMappings[0, 1], Is.EqualTo(1U));
            Assert.That(probe.CauseMappings[0, 2], Is.EqualTo(10U));
        }

        [Test]
        public void ElementNamespaceUriReflectsBuilderOverride()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .UseElementNamespace("urn:test")
                    .AddState(1, "Off", isInitial: true)
                    .StateMachine;

            Assert.That(ProbingView.Of(sm).ElementNamespaceUri,
                Is.EqualTo("urn:test"));
        }

        [Test]
        public void TransitionWithHasEffectFalseSurfacesAsZeroInMappingColumnThree()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddTransition(10, "OffToOn", from: 1, to: 2, hasEffect: false)
                    .StateMachine;

            Assert.That(ProbingView.Of(sm).TransitionMappings[0, 3],
                Is.Zero);
        }

        /// <summary>
        /// Wraps a <see cref="FluentFiniteStateMachineState"/> to expose
        /// the protected table overrides. Constructed via the legacy
        /// snapshot ctor so the probing subclass type can apply.
        /// </summary>
        private sealed class ProbingView : FluentFiniteStateMachineState
        {
            public ProbingView(NodeState parent, StateMachineDefinition definition)
                : base(parent, definition)
            {
            }

            public static ProbingView Of(FluentFiniteStateMachineState sm)
                => new(parent: null!, definition: sm.Definition);

            public int StateTableLength => StateTable!.Length;
            public int TransitionTableLength => TransitionTable!.Length;
            public new uint[,] TransitionMappings => base.TransitionMappings!;
            public new uint[,] CauseMappings => base.CauseMappings!;
            public new string ElementNamespaceUri => base.ElementNamespaceUri;
        }
    }
}
