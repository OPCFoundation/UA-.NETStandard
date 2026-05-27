/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for definition-mode chaining on the unified
    /// <see cref="StateMachineBuilder{TState}"/>. Validation,
    /// freeze semantics, and the <see cref="FluentFiniteStateMachineState"/>
    /// table projections are covered here.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void StateMachineGetterThrowsWhenNoStatesDeclared()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);

            Assert.That(() => _ = b.StateMachine,
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void DuplicateStateIdThrows()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);
            b.AddState(1, "Off");
            Assert.That(() => b.AddState(1, "Off2"),
                Throws.ArgumentException);
        }

        [Test]
        public void DuplicateTransitionIdThrows()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);
            b.AddState(1, "Off")
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2);
            Assert.That(() => b.AddTransition(10, "OffToOn2", from: 2, to: 1),
                Throws.ArgumentException);
        }

        [Test]
        public void TransitionWithUnknownFromStateFailsAtFreeze()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off")
                    .AddTransition(10, "OffToOn", from: 99, to: 1);
            Assert.That(() => _ = b.StateMachine,
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CauseMappingWithUnknownTransitionFailsAtFreeze()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off")
                    .OnCause(causeId: 100, from: 1, transition: 999);
            Assert.That(() => _ = b.StateMachine,
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void TwoInitialStatesThrows()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);
            b.AddState(1, "Off", isInitial: true);
            Assert.That(() => b.AddState(2, "On", isInitial: true),
                Throws.InvalidOperationException);
        }

        [Test]
        public void BuildProducesExpectedDefinition()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddTransition(10, "OffToOn", from: 1, to: 2)
                    .AddTransition(20, "OnToOff", from: 2, to: 1)
                    .OnCause(causeId: 100, from: 1, transition: 10)
                    .OnCause(causeId: 200, from: 2, transition: 20)
                    .StateMachine;

            StateMachineDefinition def = sm.Definition;
            Assert.That(def.States, Has.Count.EqualTo(2));
            Assert.That(def.Transitions, Has.Count.EqualTo(2));
            Assert.That(def.CauseMappings, Has.Count.EqualTo(2));
            Assert.That(def.InitialStateId, Is.EqualTo(1u));
        }

        [TestCase("")]
        [TestCase(null)]
        public void AddStateWithEmptyBrowseNameThrowsArgumentException(string browseName)
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);
            Assert.That(() => b.AddState(1, browseName!),
                Throws.ArgumentException);
        }

        [TestCase("")]
        [TestCase(null)]
        public void AddTransitionWithEmptyBrowseNameThrowsArgumentException(string browseName)
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off")
                    .AddState(2, "On");
            Assert.That(() => b.AddTransition(10, browseName!, from: 1, to: 2),
                Throws.ArgumentException);
        }

        [Test]
        public void AddTransitionWithDanglingToStateFailsAtFreeze()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off")
                    .AddTransition(10, "OffToGhost", from: 1, to: 99);
            Assert.That(() => _ = b.StateMachine,
                Throws.TypeOf<InvalidOperationException>());
        }

        [TestCase("")]
        [TestCase(null)]
        public void UseElementNamespaceWithNullOrEmptyThrowsArgumentException(string namespaceUri)
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);
            Assert.That(() => b.UseElementNamespace(namespaceUri!),
                Throws.ArgumentException);
        }

        [Test]
        public void UseElementNamespacePropagatesToDefinition()
        {
            const string customUri = "urn:test:custom";
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .UseElementNamespace(customUri)
                    .AddState(1, "Off", isInitial: true)
                    .StateMachine;

            Assert.That(sm.Definition.ElementNamespaceUri, Is.EqualTo(customUri));
        }

        [Test]
        public void AddStateInitialTrueTwiceWithSameIdDoesNotResetInitialState()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);
            b.AddState(1, "Off", isInitial: true);

            Assert.That(() => b.AddState(1, "Off", isInitial: true),
                Throws.ArgumentException);

            b.AddState(2, "On");
            Assert.That(b.StateMachine.Definition.InitialStateId,
                Is.EqualTo(1u));
        }

        [Test]
        public void HasEffectFalseSurvivesBuildAndIsReflectedOnDefinitionTransitions()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddTransition(10, "OffToOn", from: 1, to: 2, hasEffect: false)
                    .AddTransition(20, "OnToOff", from: 2, to: 1)
                    .StateMachine;

            StateMachineTransitionDefinition silent = null!;
            StateMachineTransitionDefinition loud = null!;
            foreach (StateMachineTransitionDefinition t in sm.Definition.Transitions)
            {
                if (t.Id == 10u)
                {
                    silent = t;
                }
                if (t.Id == 20u)
                {
                    loud = t;
                }
            }

            Assert.That(silent, Is.Not.Null);
            Assert.That(silent.HasEffect, Is.False);
            Assert.That(loud, Is.Not.Null);
            Assert.That(loud.HasEffect, Is.True);
        }

        [Test]
        public void DefinitionMethodsThrowAfterFreeze()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context);
            b.AddState(1, "Off", isInitial: true);
            _ = b.StateMachine;

            Assert.That(() => b.AddState(2, "On"),
                Throws.InvalidOperationException);
            Assert.That(() => b.AddTransition(10, "x", 1, 1),
                Throws.InvalidOperationException);
            Assert.That(() => b.OnCause(1, 1, 1),
                Throws.InvalidOperationException);
            Assert.That(() => b.UseElementNamespace("urn:x"),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ForThrowsForDefinitionMethods()
        {
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .StateMachine;
            var lifecycle =
                StateMachineBuilder.For(sm, m_context);

            Assert.That(() => lifecycle.AddState(2, "On"),
                Throws.InvalidOperationException);
        }

        [Test]
        public void DuplicateCauseMappingFailsAtFreeze()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddTransition(10, "OffToOn", from: 1, to: 2)
                    .AddTransition(11, "OffToOn2", from: 1, to: 2)
                    .OnCause(causeId: 100, from: 1, transition: 10)
                    .OnCause(causeId: 100, from: 1, transition: 11);

            Assert.That(() => _ = b.StateMachine,
                Throws.InvalidOperationException);
        }

        [Test]
        public void UseElementNamespacePropagatesToRuntimeCurrentStateNodeId()
        {
            const string customUri = "urn:test:custom-ns";
            // The element namespace must be registered with the
            // context's NamespaceUris for OnAfterCreate to resolve a
            // non-default ElementNamespaceIndex.
            ushort registeredIndex = m_context.NamespaceUris.GetIndexOrAppend(customUri);

            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .UseElementNamespace(customUri)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .WithInitialState(1)
                    .StateMachine;

            // The CurrentState.Id NodeId should now use the custom
            // namespace index — proving that UseElementNamespace
            // applied before NodeState.Create cached it.
            NodeId stateId = sm.CurrentState!.Id!.Value;
            Assert.That(stateId.NamespaceIndex, Is.EqualTo(registeredIndex),
                "ElementNamespaceUri override must take effect before " +
                "OnAfterCreate caches ElementNamespaceIndex.");
        }
    }

    /// <summary>
    /// Shared setup for state-machine builder tests — produces a
    /// minimal <see cref="ServerSystemContext"/> and a builder ready
    /// for definition-mode chaining.
    /// </summary>
    internal static class StateMachineTestFixtures
    {
        public static ServerSystemContext CreateContext()
        {
            var mockServer = new Mock<IServerInternal>();
            var namespaceTable = new NamespaceTable();
            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());

            // Telemetry: a plain Moq instance is enough — the
            // TelemetryExtensions.CreateLogger&lt;T&gt;() extension falls
            // back to a no-op LoggerFactory when GetLoggerFactory()
            // returns null.
            mockServer.Setup(s => s.Telemetry)
                .Returns(new Mock<ITelemetryContext>().Object);

            return new ServerSystemContext(mockServer.Object);
        }

        public static StateMachineBuilder<FluentFiniteStateMachineState> NewBuilder(
            ISystemContext context, uint nodeIdNumber = 5000)
        {
            return StateMachineBuilder.Create(
                parent: null,
                context: context,
                nodeId: new NodeId(nodeIdNumber, 1),
                browseName: new QualifiedName("TestStateMachine", 1));
        }
    }
}
