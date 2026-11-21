// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for the <c>Executable</c> / <c>UserExecutable</c>
    /// attributes of a cause method wired by
    /// <see cref="StateMachineBuilder{TState}.WithCause"/>. Part 3 §5.7
    /// defines the attributes as "may this Method be called right now",
    /// which for a Part 16 cause is
    /// <see cref="FiniteStateMachineState.IsCausePermitted"/>; Part 4
    /// §5.11.2 then has <c>Call</c> refuse a non-executable method with
    /// <c>Bad_NotExecutable</c> rather than letting the state machine
    /// answer <c>Bad_NotSupported</c> at call time.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderCauseExecutableTests
    {
        private const uint kStartCauseId = 7001;
        private const uint kOff = 1;
        private const uint kOn = 2;
        private const uint kOffToOn = 10;

        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void WithCauseReportsExecutableOnlyWhereTheCauseApplies()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder = Build();
            FluentFiniteStateMachineState sm = builder.StateMachine;
            MethodState start = AddCauseMethod(sm);

            builder.WithCause(start.NodeId);

            // Off is the initial state and the only from-state of the
            // cause, so the method is executable there ...
            Assert.Multiple(() =>
            {
                Assert.That(ReadExecutable(start), Is.True);
                Assert.That(ReadUserExecutable(start), Is.True);
            });

            sm.SetState(m_context, kOn);

            // ... and not once the machine has left it.
            Assert.Multiple(() =>
            {
                Assert.That(ReadExecutable(start), Is.False);
                Assert.That(ReadUserExecutable(start), Is.False);
            });
        }

        [Test]
        public void WithCauseLeavesTheAttributesAloneWhenOptedOut()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder = Build();
            FluentFiniteStateMachineState sm = builder.StateMachine;
            MethodState start = AddCauseMethod(sm);

            builder.WithCause(start.NodeId, reportExecutable: false);

            sm.SetState(m_context, kOn);

            Assert.Multiple(() =>
            {
                Assert.That(start.OnReadExecutable, Is.Null);
                Assert.That(start.OnReadUserExecutable, Is.Null);

                // The node keeps the value it was constructed with, and
                // the refusal happens at call time instead.
                Assert.That(ReadExecutable(start), Is.True);
                Assert.That(ReadUserExecutable(start), Is.True);
            });
        }

        [Test]
        public void UserExecutableAlsoHonoursTheUserPermissionCallback()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder = Build();
            FluentFiniteStateMachineState sm = builder.StateMachine;
            MethodState start = AddCauseMethod(sm);

            builder.WithCause(start.NodeId);

            sm.OnCheckUserPermission = (ctx, machine, transitionId, causeId, inputs, outputs)
                => StatusCodes.BadUserAccessDenied;

            Assert.Multiple(() =>
            {
                // Executable ignores user rights, UserExecutable does not.
                Assert.That(ReadExecutable(start), Is.True);
                Assert.That(ReadUserExecutable(start), Is.False);
            });
        }

        [Test]
        public void ASuspendedMachineReportsNoneOfItsCausesAsExecutable()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder = Build();
            FluentFiniteStateMachineState sm = builder.StateMachine;
            MethodState start = AddCauseMethod(sm);

            builder.WithCause(start.NodeId);

            sm.SetSuspended(m_context, true);

            Assert.Multiple(() =>
            {
                Assert.That(ReadExecutable(start), Is.False);
                Assert.That(ReadUserExecutable(start), Is.False);
            });
        }

        private StateMachineBuilder<FluentFiniteStateMachineState> Build()
        {
            return StateMachineTestFixtures.NewBuilder(m_context)
                .AddState(kOff, "Off", isInitial: true)
                .AddState(kOn, "On")
                .AddTransition(kOffToOn, "OffToOn", from: kOff, to: kOn)
                .AddTransition(20, "OnToOff", from: kOn, to: kOff)
                .OnCause(kStartCauseId, from: kOff, transition: kOffToOn);
        }

        /// <summary>
        /// A cause method has to be a child of the machine before
        /// <c>WithCause</c> can resolve it. Constructed executable, the
        /// way the node builders and the model generator leave it.
        /// </summary>
        private static MethodState AddCauseMethod(FluentFiniteStateMachineState sm)
        {
            var start = new MethodState(sm)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(kStartCauseId, 1),
                BrowseName = new QualifiedName("Start", 1),
                DisplayName = new LocalizedText("Start"),
                Executable = true,
                UserExecutable = true
            };
            sm.AddChild(start);
            return start;
        }

        private bool ReadExecutable(MethodState method)
        {
            return ReadAttribute(method, Attributes.Executable);
        }

        private bool ReadUserExecutable(MethodState method)
        {
            return ReadAttribute(method, Attributes.UserExecutable);
        }

        /// <summary>
        /// Reads through the real attribute path, so the test fails if
        /// the handler is installed but never consulted.
        /// </summary>
        private bool ReadAttribute(MethodState method, uint attributeId)
        {
            var value = new DataValue();
            ServiceResult result = method.ReadAttribute(
                m_context, attributeId, default, default, ref value);

            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"read of {Attributes.GetBrowseName(attributeId)} failed: {result}");
            Assert.That(value.WrappedValue.TryGetValue(out bool flag), Is.True,
                $"{Attributes.GetBrowseName(attributeId)} must read as a Boolean.");
            return flag;
        }
    }
}
