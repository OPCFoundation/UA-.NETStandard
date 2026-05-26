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
    /// probing subclass. Tests run purely against in-memory
    /// definitions and require no transport.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class FluentFiniteStateMachineStateTests
    {
        [Test]
        public void ConstructorWithNullDefinitionThrows()
        {
            Assert.That(
                () => new FluentFiniteStateMachineState(null, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorStoresDefinitionReference()
        {
            StateMachineDefinition def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .Build();

            var sm = new FluentFiniteStateMachineState(null, def);

            Assert.That(sm.Definition, Is.SameAs(def));
        }

        [Test]
        public void ConstructorAcceptsParentNodeState()
        {
            StateMachineDefinition def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .Build();
            var parent = new BaseObjectState(null);

            var sm = new FluentFiniteStateMachineState(parent, def);

            Assert.That(sm.Parent, Is.SameAs(parent));
            Assert.That(sm.Definition, Is.SameAs(def));
        }

        [Test]
        public void TablesReflectDefinitionShape()
        {
            StateMachineDefinition def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddState(3, "Fault")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .AddTransition(11, "OnToOff", from: 2, to: 1)
                .AddTransition(12, "OnToFault", from: 2, to: 3, hasEffect: false)
                .OnCause(100, from: 1, transition: 10)
                .OnCause(101, from: 2, transition: 11)
                .Build();

            var sm = new ProbingFluentFiniteStateMachineState(null, def);

            Assert.That(sm.StateTableLength, Is.EqualTo(3));
            Assert.That(sm.TransitionTableLength, Is.EqualTo(3));
            Assert.That(sm.TransitionMappings.GetLength(0), Is.EqualTo(3));
            Assert.That(sm.TransitionMappings.GetLength(1), Is.EqualTo(4));
            Assert.That(sm.CauseMappings.GetLength(0), Is.EqualTo(2));
            Assert.That(sm.CauseMappings.GetLength(1), Is.EqualTo(3));
        }

        [Test]
        public void TransitionMappingsContainExpectedRows()
        {
            StateMachineDefinition def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .Build();

            var sm = new ProbingFluentFiniteStateMachineState(null, def);

            Assert.That(sm.TransitionMappings[0, 0], Is.EqualTo(10U));
            Assert.That(sm.TransitionMappings[0, 1], Is.EqualTo(1U));
            Assert.That(sm.TransitionMappings[0, 2], Is.EqualTo(2U));
            Assert.That(sm.TransitionMappings[0, 3], Is.EqualTo(1U));
        }

        [Test]
        public void CauseMappingsContainExpectedRows()
        {
            StateMachineDefinition def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .OnCause(100, from: 1, transition: 10)
                .Build();

            var sm = new ProbingFluentFiniteStateMachineState(null, def);

            Assert.That(sm.CauseMappings[0, 0], Is.EqualTo(100U));
            Assert.That(sm.CauseMappings[0, 1], Is.EqualTo(1U));
            Assert.That(sm.CauseMappings[0, 2], Is.EqualTo(10U));
        }

        [Test]
        public void ElementNamespaceUriReflectsBuilderOverride()
        {
            StateMachineDefinition def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .UseElementNamespace("urn:test")
                .Build();

            var sm = new ProbingFluentFiniteStateMachineState(null, def);

            Assert.That(sm.ElementNamespaceUri, Is.EqualTo("urn:test"));
        }

        [Test]
        public void TransitionWithHasEffectFalseSurfacesAsZeroInMappingColumnThree()
        {
            StateMachineDefinition def = new StateMachineBuilder()
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2, hasEffect: false)
                .Build();

            var sm = new ProbingFluentFiniteStateMachineState(null, def);

            Assert.That(sm.TransitionMappings[0, 3], Is.Zero);
        }

        /// <summary>
        /// Test-only derived class that exposes the protected table
        /// overrides via public surface, while keeping the
        /// <c>ElementInfo</c> type internal to the base class (it is
        /// <c>protected sealed</c> and cannot leak through public API).
        /// </summary>
        private sealed class ProbingFluentFiniteStateMachineState
            : FluentFiniteStateMachineState
        {
            public ProbingFluentFiniteStateMachineState(
                NodeState parent,
                StateMachineDefinition definition)
                : base(parent, definition)
            {
            }

            public int StateTableLength => StateTable.Length;

            public int TransitionTableLength => TransitionTable.Length;

            public new uint[,] TransitionMappings => base.TransitionMappings;

            public new uint[,] CauseMappings => base.CauseMappings;

            public new string ElementNamespaceUri => base.ElementNamespaceUri;
        }
    }
}
