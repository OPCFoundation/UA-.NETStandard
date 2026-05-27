/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for lifecycle-mode behavior on the unified
    /// <see cref="StateMachineBuilder{TState}"/> — exercises
    /// <c>WithInitialState</c>, <c>OnEnterState</c>, <c>OnExitState</c>,
    /// <c>OnTransition</c>, <c>OnBeforeTransition</c>, <c>WithCause</c>,
    /// <c>WithTimedTransition</c>, and <c>ConfigureStateMachine</c>.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderLifecycleTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void WithInitialStateSetsCurrentState()
        {
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .StateMachine;

            Assert.That(CurrentStateId(sm), Is.EqualTo(1u));
        }

        [Test]
        public void WithInitialStateRejectsUndeclaredStateId()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();

            Assert.That(() => b.WithInitialState(99),
                Throws.InvalidOperationException);
        }

        [Test]
        public void OnEnterStateFiresWhenStateChanges()
        {
            int enterCount = 0;
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnEnterState(2, (ctx, m) => enterCount++)
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(enterCount, Is.EqualTo(1));
            Assert.That(CurrentStateId(sm), Is.EqualTo(2u));
        }

        [Test]
        public void OnExitStateFiresWhenLeavingState()
        {
            int exitCount = 0;
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnExitState(1, (ctx, m) => exitCount++)
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(exitCount, Is.EqualTo(1));
        }

        [Test]
        public void OnTransitionReceivesFromAndToIds()
        {
            uint observedFrom = 0;
            uint observedTo = 0;
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnTransition((ctx, m, from, to) =>
                {
                    observedFrom = from;
                    observedTo = to;
                })
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(observedFrom, Is.EqualTo(1u));
            Assert.That(observedTo, Is.EqualTo(2u));
        }

        [Test]
        public void OnBeforeTransitionCanVetoTransition()
        {
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnBeforeTransition((ctx, m, transitionId, causeId)
                    => StatusCodes.BadUserAccessDenied)
                .StateMachine;

            ServiceResult result = sm.DoTransition(m_context,
                transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(CurrentStateId(sm), Is.EqualTo(1u),
                "veto must keep machine in the original state");
        }

        [Test]
        public void OnBeforeTransitionReceivesTransitionAndCauseIds()
        {
            uint observedTransition = 0;
            uint observedCause = 0;
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnBeforeTransition((ctx, m, transitionId, causeId) =>
                {
                    observedTransition = transitionId;
                    observedCause = causeId;
                    return ServiceResult.Good;
                })
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 100,
                inputArguments: default, outputArguments: []);

            Assert.That(observedTransition, Is.EqualTo(10u));
            Assert.That(observedCause, Is.EqualTo(100u));
        }

        [Test]
        public void WithCauseRejectsNullOrNonNumericNodeId()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();

            Assert.That(() => b.WithCause(NodeId.Null),
                Throws.ArgumentException);
            Assert.That(() => b.WithCause(new NodeId("text-id", 0)),
                Throws.ArgumentException);
        }

        [Test]
        public void WithCauseRejectsUnknownMethodNodeId()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() => b.WithCause(new NodeId(99999u, 0)),
                Throws.ArgumentException);
        }

        [Test]
        public void WithTimedTransitionFiresAfterTimeout()
        {
            using var done = new ManualResetEventSlim(false);
            _ = BuildOnOffMachine()
                .WithInitialState(1)
                .OnEnterState(2, (ctx, m) => done.Set())
                .WithTimedTransition(
                    fromStateId: 1,
                    timeout: TimeSpan.FromMilliseconds(50),
                    transitionId: 10)
                .StateMachine;

            Assert.That(done.Wait(TimeSpan.FromSeconds(2)), Is.True,
                "timed transition should fire within 2 s");
        }

        [Test]
        public void WithTimedTransitionArmsOnEntryAfterTransition()
        {
            using var done = new ManualResetEventSlim(false);
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(2)
                .OnEnterState(2, (ctx, m) => done.Set())
                .WithTimedTransition(
                    fromStateId: 1,
                    timeout: TimeSpan.FromMilliseconds(50),
                    transitionId: 10)
                .StateMachine;

            // Now move to state 1 — this should arm the timer (via
            // DispatchAfter), which fires the transition back to state 2.
            sm.DoTransition(m_context, transitionId: 20, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(done.Wait(TimeSpan.FromSeconds(2)), Is.True,
                "timed transition should fire within 2 s of state-1 entry");
        }

        [Test]
        public void WithTimedTransitionRejectsNonPositiveTimeout()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() =>
                b.WithTimedTransition(1, TimeSpan.Zero, 10),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() =>
                b.WithTimedTransition(1, TimeSpan.FromMilliseconds(-1), 10),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void WithTimedTransitionRejectsUnknownFromStateInDefinitionMode()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() =>
                b.WithTimedTransition(99, TimeSpan.FromMilliseconds(10), 10),
                Throws.InvalidOperationException);
        }

        [Test]
        public void WithTimedTransitionRejectsUnknownTransitionInDefinitionMode()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() =>
                b.WithTimedTransition(1, TimeSpan.FromMilliseconds(10), 999),
                Throws.InvalidOperationException);
        }

        [Test]
        public void WithTimedTransitionRejectsTransitionWithMismatchedFromState()
        {
            // Transition 20 has from=2; passing fromStateId=1 should fail.
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() =>
                b.WithTimedTransition(1, TimeSpan.FromMilliseconds(10), 20),
                Throws.InvalidOperationException);
        }

        [Test]
        public void WithInitialStateDoesNotFireOnEnterState()
        {
            int enterCount = 0;
            _ = BuildOnOffMachine()
                .OnEnterState(1, (ctx, m) => enterCount++)
                .WithInitialState(1)
                .StateMachine;

            Assert.That(enterCount, Is.Zero,
                "WithInitialState must not fire lifecycle handlers — " +
                "initial state assignment is not a transition.");
        }

        [Test]
        public void ConfigureStateMachineRunsCallback()
        {
            FluentFiniteStateMachineState captured = null!;
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .ConfigureStateMachine(s => captured = s)
                .StateMachine;

            Assert.That(captured, Is.SameAs(sm));
        }

        [Test]
        public void ConfigureStateMachineRejectsNullCallback()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() => b.ConfigureStateMachine(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ExistingOnAfterTransitionRunsBeforeBuilderHandlers()
        {
            // Build a machine, then install a pre-existing OnAfter
            // BEFORE handing it to a lifecycle builder. Verify the
            // existing handler fires first and the builder handlers
            // see the same final state.
            FluentFiniteStateMachineState sm =
                StateMachineTestFixtures.NewBuilder(m_context)
                    .AddState(1, "Off", isInitial: true)
                    .AddState(2, "On")
                    .AddTransition(10, "OffToOn", from: 1, to: 2)
                    .StateMachine;
            sm.SetState(m_context, 1);

            var order = new List<string>();
            sm.OnAfterTransition = (ctx, m, t, c, ins, outs) =>
            {
                order.Add("original");
                return ServiceResult.Good;
            };

            StateMachineBuilder.For(sm, m_context)
                .OnTransition((ctx, m, from, to) => order.Add("builder"));

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(order, Is.EqualTo(s_originalThenBuilder));
        }

        [Test]
        public void LifecycleOrderIsExitTransitionEnter()
        {
            var order = new List<string>();
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnExitState(1, (ctx, m) => order.Add("exit-1"))
                .OnTransition((ctx, m, from, to) => order.Add($"trans-{from}-{to}"))
                .OnEnterState(2, (ctx, m) => order.Add("enter-2"))
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(order, Is.EqualTo(s_lifecycleOrder));
        }

        private static readonly string[] s_originalThenBuilder = ["original", "builder"];
        private static readonly string[] s_lifecycleOrder = ["exit-1", "trans-1-2", "enter-2"];

        [Test]
        public void HandlerExceptionsDoNotBreakSubsequentHandlers()
        {
            int laterCount = 0;
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnEnterState(2, (ctx, m) =>
                    throw new InvalidOperationException("simulated"))
                .OnEnterState(2, (ctx, m) => laterCount++)
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            Assert.That(laterCount, Is.EqualTo(1),
                "second handler should run even if the first threw");
        }

        [Test]
        public async System.Threading.Tasks.Task OnEnterStateAsyncFiresWhenStateChanges()
        {
            var signaled = new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnEnterStateAsync(2, async (ctx, m, ct) =>
                {
                    await System.Threading.Tasks.Task.Yield();
                    signaled.TrySetResult(true);
                })
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => signaled.TrySetCanceled());
            await signaled.Task.ConfigureAwait(false);
            Assert.That(CurrentStateId(sm), Is.EqualTo(2u));
        }

        [Test]
        public async System.Threading.Tasks.Task OnExitStateAsyncFiresWhenLeavingState()
        {
            var signaled = new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnExitStateAsync(1, async (ctx, m, ct) =>
                {
                    await System.Threading.Tasks.Task.Yield();
                    signaled.TrySetResult(true);
                })
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => signaled.TrySetCanceled());
            await signaled.Task.ConfigureAwait(false);
        }

        [Test]
        public async System.Threading.Tasks.Task OnTransitionAsyncReceivesFromAndToIds()
        {
            var signaled = new System.Threading.Tasks.TaskCompletionSource<(uint from, uint to)>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .OnTransitionAsync(async (ctx, m, from, to, ct) =>
                {
                    await System.Threading.Tasks.Task.Yield();
                    signaled.TrySetResult((from, to));
                })
                .StateMachine;

            sm.DoTransition(m_context, transitionId: 10, causeId: 0,
                inputArguments: default, outputArguments: []);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => signaled.TrySetCanceled());
            (uint from, uint to) = await signaled.Task.ConfigureAwait(false);
            Assert.That(from, Is.EqualTo(1u));
            Assert.That(to, Is.EqualTo(2u));
        }

        [Test]
        public void OnEnterStateAsyncRejectsNullHandler()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() => b.OnEnterStateAsync(1, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void OnExitStateAsyncRejectsNullHandler()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() => b.OnExitStateAsync(1, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void OnTransitionAsyncRejectsNullHandler()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() => b.OnTransitionAsync(null!),
                Throws.ArgumentNullException);
        }

        private StateMachineBuilder<FluentFiniteStateMachineState> BuildOnOffMachine()
        {
            return StateMachineTestFixtures.NewBuilder(m_context)
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .AddTransition(20, "OnToOff", from: 2, to: 1)
                .OnCause(100, from: 1, transition: 10)
                .OnCause(200, from: 2, transition: 20);
        }

        private static uint CurrentStateId(FiniteStateMachineState sm)
        {
            if (sm.CurrentState?.Id?.Value is { } id &&
                !id.IsNull &&
                id.TryGetValue(out uint stateId))
            {
                return stateId;
            }
            return 0;
        }
    }
}
