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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.StateMachines;
using DefinitionBuilder = Opc.Ua.Server.StateMachines.StateMachineBuilder;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Verifies timed-transition ownership and invocation-specific state snapshots across nested and concurrent
    /// transitions.
    /// </summary>
    [TestFixture]
    public sealed class StateMachineLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies that rearming disposes the old timer and only the current callback can commit a transition.
        /// </summary>
        [Test]
        public void RearmingCancelsThePreviousTimerAndOnlyTheCurrentCallbackCanTransition()
        {
            ServerSystemContext context = StateMachineTestFixtures.CreateContext();
            FluentFiniteStateMachineState machine = NewMachine(context);
            var clock = new CapturingClock();
            var dispatcher = new StateMachineDispatcher<FluentFiniteStateMachineState>(machine, context, clock);
            int transitions = 0;
            dispatcher.AddTransitionObserver((_, _, _, _) => transitions++);
            dispatcher.AddTimedTransition(1, TimeSpan.FromSeconds(10), 10, 0);
            CapturedTimer first = clock.Timers[0];
            dispatcher.SynchronizeInitialState(context, 1);
            Assert.That(first.Disposed, Is.True);
            first.Fire();
            Assert.That(transitions, Is.Zero);
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.That(transitions, Is.EqualTo(1));
            Assert.That(Current(machine), Is.EqualTo(2));
            Assert.That(clock.Timers, Has.All.Property(nameof(CapturedTimer.Disposed)).True);
        }

        /// <summary>
        /// Verifies that leaving and reentering a state does not revive a callback from its earlier lifetime.
        /// </summary>
        [Test]
        public void LeavingAndReenteringTheSameStateInvalidatesItsOldTimer()
        {
            ServerSystemContext context = StateMachineTestFixtures.CreateContext();
            FluentFiniteStateMachineState machine = NewMachine(context);
            var clock = new CapturingClock();
            var dispatcher = new StateMachineDispatcher<FluentFiniteStateMachineState>(machine, context, clock);
            dispatcher.AddTimedTransition(1, TimeSpan.FromSeconds(10), 10, 0);
            machine.SetState(context, 2);
            machine.SetState(context, 1);
            clock.Timers[0].Fire();
            Assert.That(Current(machine), Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that stopping timers prevents queued callbacks from transitioning and rejects new timer
        /// registration.
        /// </summary>
        [Test]
        public void StoppingTimerLifetimeDisposesTimersAndRejectsQueuedCallbacksAndRearming()
        {
            ServerSystemContext context = StateMachineTestFixtures.CreateContext();
            FluentFiniteStateMachineState machine = NewMachine(context);
            var clock = new CapturingClock();
            var dispatcher = new StateMachineDispatcher<FluentFiniteStateMachineState>(machine, context, clock);
            dispatcher.AddTimedTransition(1, TimeSpan.FromSeconds(10), 10, 0);
            dispatcher.StopTimers();
            clock.Timers[0].Fire();
            Assert.That(Current(machine), Is.EqualTo(1));
            Assert.That(clock.Timers[0].Disposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() =>
                dispatcher.AddTimedTransition(1, TimeSpan.FromSeconds(10), 10, 0));
        }

        /// <summary>
        /// Verifies that nested after-transition callbacks preserve each invocation's observer and event state pair.
        /// </summary>
        [Test]
        public void NestedAfterCallbacksRetainEachInvocationsFromAndToStates(
            [Values(false, true)] bool fluent)
        {
            ServerSystemContext context = StateMachineTestFixtures.CreateContext();
            FluentFiniteStateMachineState machine = NewMachine(context);
            machine.OnAfterTransition = (ctx, _, transition, _, _, _) =>
            {
                if (transition == 10)
                {
                    Assert.That(ServiceResult.IsGood(machine.DoTransition(ctx, 20, 0, default, [])), Is.True);
                }
                return ServiceResult.Good;
            };
            var observed = new List<(uint From, uint To)>();
            var events = new List<(string From, string To)>();
            machine.SetAreEventsMonitored(context, true, false);
            machine.OnReportEvent = (_, _, value) =>
            {
                if (value is TransitionEventState transition)
                {
                    events.Add((transition.FromState.Value.Text, transition.ToState.Value.Text));
                }
            };
            Observe(machine, context, fluent, (from, to) => observed.Add((from, to)));
            Assert.That(ServiceResult.IsGood(machine.DoTransition(context, 10, 0, default, [])), Is.True);
            Assert.That(observed, Is.EqualTo(new[] { (2u, 3u), (1u, 2u) }));
            Assert.That(events, Is.EquivalentTo(new[] { ("One", "Two"), ("Two", "Three") }));
        }

        /// <summary>
        /// Verifies that a transition committed inside a before callback invalidates the outer transition.
        /// </summary>
        [Test]
        public void NestedBeforeTransitionCannotCommitItsNowStaleOuterTransition(
            [Values(false, true)] bool fluent)
        {
            ServerSystemContext context = StateMachineTestFixtures.CreateContext();
            FluentFiniteStateMachineState machine = NewMachine(context);
            machine.OnBeforeTransition = (ctx, _, transition, _, _, _) =>
            {
                if (transition == 10)
                {
                    Assert.That(ServiceResult.IsGood(machine.DoTransition(ctx, 30, 0, default, [])), Is.True);
                }
                return ServiceResult.Good;
            };
            var observed = new List<(uint From, uint To)>();
            Observe(machine, context, fluent, (from, to) => observed.Add((from, to)));
            ServiceResult result = machine.DoTransition(context, 10, 0, default, []);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Current(machine), Is.EqualTo(3));
            Assert.That(observed, Is.EqualTo(new[] { (1u, 3u) }));
        }

        /// <summary>
        /// Verifies that nonmethod causes can report transition events without generating method-specific audit events.
        /// </summary>
        [Test]
        public void NonMethodCauseReportsTheTransitionWithoutFabricatingAMethodAudit(
            [Values(false, true)] bool methodCause,
            [Values(false, true)] bool monitored)
        {
            ServerSystemContext context = StateMachineTestFixtures.CreateContext();
            FluentFiniteStateMachineState machine = NewMachine(context);
            var events = new List<IFilterTarget>();
            machine.SetAreEventsMonitored(context, monitored, false);
            machine.OnReportEvent = (_, _, value) => events.Add(value);
            MethodState method = methodCause
                ? new MethodState(machine)
                {
                    NodeId = new NodeId(100u, 1),
                    DisplayName = new LocalizedText("Start"),
                    BrowseName = new QualifiedName("Start", 1)
                }
                : null;
            ServiceResult result = machine.DoCause(context, method, 100, default, []);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Current(machine), Is.EqualTo(2));
            Assert.That(events.FindAll(value => value is TransitionEventState),
                Has.Count.EqualTo(monitored ? 1 : 0));
            Assert.That(events.FindAll(value => value.GetType() == typeof(AuditUpdateStateEventState)),
                Has.Count.EqualTo(monitored && methodCause ? 1 : 0));
        }

        /// <summary>
        /// Verifies that concurrent transitions complete in order and observers receive each transition's own state
        /// pair.
        /// </summary>
        [Test]
        public async Task ConcurrentTransitionsKeepTheirOwnOrderedStateSnapshotsAsync(
            [Values(false, true)] bool fluent)
        {
            ServerSystemContext context = StateMachineTestFixtures.CreateContext();
            FluentFiniteStateMachineState machine = NewMachine(context);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            machine.OnAfterTransition = (_, _, transition, _, _, _) =>
            {
                if (transition == 10)
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Transition barrier was not released.");
                    }
                }
                return ServiceResult.Good;
            };
            var observed = new List<(uint From, uint To)>();
            Observe(machine, context, fluent, (from, to) => observed.Add((from, to)));
            Task<ServiceResult> first = Task.Run(() => machine.DoTransition(context, 10, 0, default, []));
            Task<ServiceResult> second = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                second = Task.Run(() => machine.DoTransition(context, 20, 0, default, []));
                Task completion = await Task.WhenAny(second, Task.Delay(100)).ConfigureAwait(false);
                Assert.That(completion, Is.Not.SameAs(second), "A concurrent transition must wait for its predecessor.");
            }
            finally
            {
                release.Set();
                Assert.That(ServiceResult.IsGood(await first.ConfigureAwait(false)), Is.True);
                if (second != null)
                {
                    Assert.That(ServiceResult.IsGood(await second.ConfigureAwait(false)), Is.True);
                }
            }
            Assert.That(observed, Is.EqualTo(new[] { (1u, 2u), (2u, 3u) }));
        }

        /// <summary>
        /// Verifies that fluent timed simulation uses the injected server clock and emits a transition event.
        /// </summary>
        [Test]
        public void FluentSimulationUsesTheServerClockAndReportsANonMethodTransition()
        {
            var clock = new CapturingClock();
            ServerSystemContext context = CreateTimedContext(clock);
            FluentFiniteStateMachineState machine = NewMachine(context);
            using var manager = new SimulationManager(context.Server);
            var root = new NodeManagerBuilder(context, manager, 1, _ => machine, _ => machine, _ => []);
            root.AttachSimulations(manager.Simulations);
            var node = new Mock<INodeBuilder<FluentFiniteStateMachineState>>();
            node.SetupGet(value => value.Node).Returns(machine);
            node.SetupGet(value => value.Builder).Returns(root);
            var builder = new Ua.Server.Fluent.StateMachineBuilder<FluentFiniteStateMachineState>(node.Object);
            builder.WithTimedTransition(1, TimeSpan.FromSeconds(1), 100);
            Assert.That(manager.Simulations.HasLoops, Is.True);
            var events = new List<IFilterTarget>();
            machine.SetAreEventsMonitored(context, true, false);
            machine.OnReportEvent = (_, _, value) => events.Add(value);
            MethodInfo method = builder.GetType().GetMethod("OnSimulationTick", BindingFlags.Instance | BindingFlags.NonPublic);
#if NET5_0_OR_GREATER
            Action<ISystemContext, TimeSpan> tick = method.CreateDelegate<Action<ISystemContext, TimeSpan>>(builder);
#else
            var tick = (Action<ISystemContext, TimeSpan>)method.CreateDelegate(typeof(Action<ISystemContext, TimeSpan>), builder);
#endif
            tick(context, TimeSpan.Zero);
            clock.Advance(TimeSpan.FromSeconds(2));
            tick(context, TimeSpan.FromSeconds(2));
            Assert.That(Current(machine), Is.EqualTo(2));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.InstanceOf<TransitionEventState>());
        }

        /// <summary>
        /// Verifies that stopping timed transitions through the public builder preserves the current state.
        /// </summary>
        [Test]
        public void PublicBuilderCanStopItsTimerWithoutChangingTheMachinesState()
        {
            var clock = new CapturingClock();
            ServerSystemContext context = CreateTimedContext(clock);
            FluentFiniteStateMachineState machine = NewMachine(context);
            Ua.Server.StateMachines.StateMachineBuilder<FluentFiniteStateMachineState> builder =
                DefinitionBuilder.For(machine, context)
                    .WithTimedTransition(1, TimeSpan.FromSeconds(1), 10);
            builder.StopTimedTransitions();
            clock.Timers[0].Fire();
            Assert.That(Current(machine), Is.EqualTo(1));
            Assert.That(clock.Timers[0].Disposed, Is.True);
        }

        /// <summary>
        /// Creates a server context that exposes the supplied time provider to fluent simulations.
        /// </summary>
        private static ServerSystemContext CreateTimedContext(TimeProvider clock)
        {
            var server = new Mock<IServerInternal>();
            server.As<ITimeProviderProvider>().SetupGet(value => value.TimeProvider).Returns(clock);
            var namespaces = new NamespaceTable();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(new Mock<ITelemetryContext>().Object);
            var context = new ServerSystemContext(server.Object);
            server.SetupGet(value => value.DefaultSystemContext).Returns(context);
            return context;
        }

        /// <summary>
        /// Builds the three-state machine used to distinguish nested, concurrent, and timed transition paths.
        /// </summary>
        private static FluentFiniteStateMachineState NewMachine(ISystemContext context)
        {
            return StateMachineTestFixtures.NewBuilder(context)
                .AddState(1, "One", isInitial: true)
                .AddState(2, "Two")
                .AddState(3, "Three")
                .AddTransition(10, "OneToTwo", 1, 2, hasEffect: true)
                .AddTransition(20, "TwoToThree", 2, 3, hasEffect: true)
                .AddTransition(30, "OneToThree", 1, 3, hasEffect: true)
                .OnCause(100, 1, 10)
                .WithInitialState(1)
                .StateMachine;
        }

        /// <summary>
        /// Resolves the machine's active state node to its numeric state identifier.
        /// </summary>
        private static uint Current(FluentFiniteStateMachineState machine)
        {
            return machine.GetStateId(machine.CurrentState.Id.Value);
        }

        /// <summary>
        /// Registers a transition observer through either the fluent or definition-builder API.
        /// </summary>
        private static void Observe(
            FluentFiniteStateMachineState machine, ISystemContext context, bool fluent, Action<uint, uint> observe)
        {
            if (fluent)
            {
                var root = new Mock<INodeManagerBuilder>();
                root.SetupGet(value => value.Context).Returns(context);
                var node = new Mock<INodeBuilder<FluentFiniteStateMachineState>>();
                node.SetupGet(value => value.Node).Returns(machine);
                node.SetupGet(value => value.Builder).Returns(root.Object);
                var builder = new Ua.Server.Fluent.StateMachineBuilder<FluentFiniteStateMachineState>(node.Object);
                builder.OnTransition((_, _, from, to) => observe(from, to));
            }
            else
            {
                DefinitionBuilder.For(machine, context).OnTransition((_, _, from, to) => observe(from, to));
            }
        }

        /// <summary>
        /// Wraps a fake clock and retains timers so stale callbacks can be fired explicitly.
        /// </summary>
        private sealed class CapturingClock : TimeProvider
        {
            /// <summary>
            /// Gets the timers created for checking disposal and delivering captured callbacks.
            /// </summary>
            public List<CapturedTimer> Timers { get; } = [];

            /// <inheritdoc/>
            public override long TimestampFrequency => m_clock.TimestampFrequency;

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return m_clock.GetUtcNow();
            }

            /// <inheritdoc/>
            public override long GetTimestamp()
            {
                return m_clock.GetTimestamp();
            }

            /// <summary>
            /// Creates a fake-clock timer and retains a wrapper that can replay its captured callback.
            /// </summary>
            public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = new CapturedTimer(callback, state, m_clock.CreateTimer(callback, state, dueTime, period));
                Timers.Add(timer);
                return timer;
            }

            /// <summary>
            /// Advances the fake clock and runs timers that become due.
            /// </summary>
            public void Advance(TimeSpan time)
            {
                m_clock.Advance(time);
            }

            /// <summary>
            /// Advances the monotonic clock independently of captured timer callback execution.
            /// </summary>
            private readonly FakeTimeProvider m_clock = new();
        }

        /// <summary>
        /// Supplies fluent simulation ownership for tests that use the server's injected clock.
        /// </summary>
        private sealed class SimulationManager(IServerInternal server)
            : FluentNodeManagerBase(server, "urn:state-lifetime-regression")
        {
        }

        /// <summary>
        /// Tracks timer disposal while retaining the original callback to simulate queued invocations.
        /// </summary>
        private sealed class CapturedTimer(TimerCallback callback, object state, ITimer inner) : ITimer
        {
            /// <summary>
            /// Gets whether the wrapper has disposed its underlying timer.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <inheritdoc/>
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                return inner.Change(dueTime, period);
            }

            /// <summary>
            /// Invokes the captured callback independently of timer scheduling or disposal.
            /// </summary>
            public void Fire()
            {
                callback(state);
            }

            /// <summary>
            /// Records disposal and releases the underlying fake-clock timer.
            /// </summary>
            public void Dispose()
            {
                Disposed = true;
                inner.Dispose();
            }

            /// <summary>
            /// Disposes the captured timer and completes without asynchronous work.
            /// </summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }
    }
}
