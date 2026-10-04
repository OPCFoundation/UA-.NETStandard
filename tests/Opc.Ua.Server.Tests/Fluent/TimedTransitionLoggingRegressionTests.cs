/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Tests.StateMachines;
using FluentFiniteStateMachineState = Opc.Ua.Server.StateMachines.FluentFiniteStateMachineState;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Verifies timed-transition rejection log suppression without suppressing retries or recovery.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class TimedTransitionLoggingRegressionTests
    {
        /// <summary>
        /// Verifies that repeated identical rejections log once while retries continue until the guard permits
        /// transition.
        /// </summary>
        [TestCaseSource(nameof(s_rejectionStatuses))]
        public async Task RepeatedTimedRejectionLogsOnceAndRecoversAsync(StatusCode rejectionStatus)
        {
            await using var harness = new TimedHarness(rejectionStatus);
            long initialRevision = harness.Machine.StateRevision;
            harness.Clock.Advance(TimeSpan.FromMilliseconds(99));
            Assert.That(harness.GuardCalls, Is.Zero);
            Assert.That(harness.Rejections, Is.Empty);

            await harness.TickAsync(TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);
            for (int i = 0; i < 10; i++)
            {
                await harness.TickAsync().ConfigureAwait(false);
            }
            Assert.That(harness.GuardCalls, Is.EqualTo(11));
            Assert.That(harness.CurrentState, Is.EqualTo(1));
            Assert.That(harness.Machine.StateRevision, Is.EqualTo(initialRevision));

            harness.GuardStatus = StatusCodes.Good;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.StopAsync().ConfigureAwait(false);

            ArrayOf<(uint State, uint Status)> expected = [(1, rejectionStatus.Code)];
            Assert.That(harness.Rejections, Is.EqualTo(expected));
            Assert.That(harness.GuardCalls, Is.EqualTo(12), "A suppressed warning must not suppress retries.");
            Assert.That(harness.CurrentState, Is.EqualTo(2));
            Assert.That(harness.Machine.StateRevision, Is.GreaterThan(initialRevision));
        }

        /// <summary>
        /// Verifies that changed rejection statuses permit new warnings while BadInvalidState remains silent.
        /// </summary>
        [Test]
        public async Task ChangedRejectionStatusLogsAgainIncludingAfterInvalidStateAsync()
        {
            await using var harness = new TimedHarness(StatusCodes.BadWaitingForInitialData);
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.GuardStatus = StatusCodes.BadUserAccessDenied;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.GuardStatus = StatusCodes.BadWaitingForInitialData;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.GuardStatus = StatusCodes.BadInvalidState;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.GuardStatus = StatusCodes.BadWaitingForInitialData;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.GuardStatus = StatusCodes.Good;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.StopAsync().ConfigureAwait(false);

            ArrayOf<(uint State, uint Status)> expected =
            [
                (1, StatusCodes.BadWaitingForInitialData.Code),
                (1, StatusCodes.BadUserAccessDenied.Code),
                (1, StatusCodes.BadWaitingForInitialData.Code),
                (1, StatusCodes.BadWaitingForInitialData.Code)
            ];
            Assert.That(harness.Rejections, Is.EqualTo(expected),
                "Status changes must permit a new warning, but BadInvalidState itself remains silent.");
            Assert.That(harness.GuardCalls, Is.EqualTo(11));
            Assert.That(harness.CurrentState, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that reentering a state creates a new revision that may log the same rejection again.
        /// </summary>
        [Test]
        public async Task ReenteringSameStateAllowsRejectionWarningForNewRevisionAsync()
        {
            await using var harness = new TimedHarness(StatusCodes.BadUserAccessDenied);
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            long rejectedRevision = harness.Machine.StateRevision;
            harness.Transition(11);
            Assert.That(harness.CurrentState, Is.EqualTo(1));
            Assert.That(harness.Machine.StateRevision, Is.GreaterThan(rejectedRevision));
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.GuardStatus = StatusCodes.Good;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.StopAsync().ConfigureAwait(false);

            ArrayOf<(uint State, uint Status)> expected =
            [
                (1, StatusCodes.BadUserAccessDenied.Code),
                (1, StatusCodes.BadUserAccessDenied.Code)
            ];
            Assert.That(harness.Rejections, Is.EqualTo(expected));
            Assert.That(harness.GuardCalls, Is.EqualTo(5));
            Assert.That(harness.CurrentState, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that entering another state resets warning suppression and still permits later recovery.
        /// </summary>
        [Test]
        public async Task EnteringDifferentStateAllowsRejectionWarningAndRecoveryAsync()
        {
            await using var harness = new TimedHarness(StatusCodes.BadStateNotActive);
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.Transition(10);
            Assert.That(harness.CurrentState, Is.EqualTo(2));
            await harness.TickAsync().ConfigureAwait(false);
            await harness.TickAsync().ConfigureAwait(false);
            harness.GuardStatus = StatusCodes.Good;
            await harness.TickAsync().ConfigureAwait(false);
            await harness.StopAsync().ConfigureAwait(false);

            ArrayOf<(uint State, uint Status)> expected =
            [
                (1, StatusCodes.BadStateNotActive.Code),
                (2, StatusCodes.BadStateNotActive.Code)
            ];
            Assert.That(harness.Rejections, Is.EqualTo(expected));
            Assert.That(harness.GuardCalls, Is.EqualTo(5));
            Assert.That(harness.CurrentState, Is.EqualTo(3));
        }

        /// <summary>
        /// Supplies guard failures that should be warned once per unchanged state revision and status.
        /// </summary>
        private static readonly StatusCode[] s_rejectionStatuses =
        [
            StatusCodes.BadWaitingForInitialData,
            StatusCodes.BadUserAccessDenied,
            StatusCodes.BadStateNotActive
        ];

        /// <summary>
        /// Runs a three-state machine with a fake clock, a configurable guard, and captured rejection warnings.
        /// </summary>
        private sealed class TimedHarness : IAsyncDisposable
        {
            /// <summary>
            /// Configures and starts timed transitions with the requested initial guard rejection.
            /// </summary>
            public TimedHarness(StatusCode rejectionStatus)
            {
                GuardStatus = rejectionStatus;
                m_logger.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
                m_logger.Setup(logger => logger.Log(
                    LogLevel.Warning,
                    It.Is<EventId>(id => id.Name == "FluentTimedTransitionRejected"),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                    .Callback(new InvocationAction(CaptureRejection));
                var factory = new Mock<ILoggerFactory>();
                factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(m_logger.Object);
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
                var server = new Mock<IServerInternal>();
                server.As<ITimeProviderProvider>().SetupGet(value => value.TimeProvider).Returns(Clock);
                var namespaces = new NamespaceTable();
                namespaces.Append("urn:timed-transition-logging");
                server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
                server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                server.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.Create(telemetry.Object));
                Context = new ServerSystemContext(server.Object);
                server.SetupGet(value => value.DefaultSystemContext).Returns(Context);
                m_manager = new TimedManager(server.Object);
                Machine = StateMachineTestFixtures.NewBuilder(Context)
                    .AddState(1, "One", isInitial: true)
                    .AddState(2, "Two")
                    .AddState(3, "Three")
                    .AddTransition(10, "OneToTwo", 1, 2)
                    .AddTransition(11, "ReenterOne", 1, 1)
                    .AddTransition(20, "TwoToThree", 2, 3)
                    .OnCause(100, 1, 10)
                    .OnCause(200, 2, 20)
                    .WithInitialState(1)
                    .StateMachine;
                var root = new NodeManagerBuilder(Context, m_manager, 1, _ => Machine, _ => Machine, _ => []);
                root.AttachSimulations(m_manager.Simulations);
                root.Node<FluentFiniteStateMachineState>(Machine.NodeId)
                    .AsStateMachine()
                    .OnBeforeTransition((_, _, _) => BeforeTransition())
                    .WithTimedTransition(1, TimeSpan.FromMilliseconds(100), 100)
                    .WithTimedTransition(2, TimeSpan.FromMilliseconds(100), 200);
                Transition(11);
                m_manager.Simulations.Start();
            }

            /// <summary>
            /// Gets the clock used to advance timed transitions deterministically.
            /// </summary>
            public FakeTimeProvider Clock { get; } = new();

            /// <summary>
            /// Gets the server context used by manual and timed state transitions.
            /// </summary>
            public ServerSystemContext Context { get; }

            /// <summary>
            /// Gets the state machine whose revision and current state are observed.
            /// </summary>
            public FluentFiniteStateMachineState Machine { get; }

            /// <summary>
            /// Gets the number of timed transition attempts that reached the guard.
            /// </summary>
            public int GuardCalls => Volatile.Read(ref m_guardCalls);

            /// <summary>
            /// Gets the numeric identifier of the machine's active state.
            /// </summary>
            public uint CurrentState => Machine.GetStateId(Machine.CurrentState!.Id!.Value);

            /// <summary>
            /// Gets the captured warning payloads as state identifiers paired with rejection status codes.
            /// </summary>
            public ArrayOf<(uint State, uint Status)> Rejections => m_rejections.ToArray();

            /// <summary>
            /// Gets or sets the result returned by the next timed-transition guard evaluation.
            /// </summary>
            public StatusCode GuardStatus
            {
                get => new(Volatile.Read(ref m_guardStatus));
                set => Volatile.Write(ref m_guardStatus, value.Code);
            }

            /// <summary>
            /// Advances the fake clock and waits for a timed transition to reach its guard.
            /// </summary>
            public async ValueTask TickAsync(TimeSpan? elapsed = null)
            {
                Clock.Advance(elapsed ?? TimeSpan.FromMilliseconds(100));
                bool reached = await m_guardReached.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(reached, Is.True, "The registered simulation did not attempt its timed transition.");
            }

            /// <summary>
            /// Performs a manual transition while bypassing the timed guard's configured rejection.
            /// </summary>
            public void Transition(uint transitionId)
            {
                Volatile.Write(ref m_manualTransition, true);
                try
                {
                    ServiceResult result = Machine.DoTransition(Context, transitionId, 0, default, []);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                finally
                {
                    Volatile.Write(ref m_manualTransition, false);
                }
            }

            /// <summary>
            /// Stops simulations and verifies that transition retries produced no error-level logs.
            /// </summary>
            public async ValueTask StopAsync()
            {
                await m_manager.Simulations.StopAsync().ConfigureAwait(false);
                m_logger.Verify(logger => logger.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
            }

            /// <summary>
            /// Stops simulations, disposes the node manager, and releases the guard notification semaphore.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await m_manager.Simulations.StopAsync().ConfigureAwait(false);
                await m_manager.DisposeAsync().ConfigureAwait(false);
                m_guardReached.Dispose();
            }

            /// <summary>
            /// Records timed attempts and supplies their configured rejection while allowing manual transitions.
            /// </summary>
            private ServiceResult BeforeTransition()
            {
                if (Volatile.Read(ref m_manualTransition))
                {
                    return ServiceResult.Good;
                }
                var result = new ServiceResult(GuardStatus);
                Interlocked.Increment(ref m_guardCalls);
                m_guardReached.Release();
                return result;
            }

            /// <summary>
            /// Extracts and validates the structured state and status recorded by a rejection warning.
            /// </summary>
            private void CaptureRejection(IInvocation invocation)
            {
                if (invocation.Arguments[2] is not IReadOnlyList<KeyValuePair<string, object?>> values)
                {
                    throw new AssertionException("The rejection event did not contain structured properties.");
                }
                uint state = 0;
                uint status = 0;
                foreach (KeyValuePair<string, object?> value in values)
                {
                    if (value.Key == "StateId" && value.Value is uint stateId)
                    {
                        state = stateId;
                    }
                    if (value.Key == "Result" && value.Value is ServiceResult result)
                    {
                        status = result.StatusCode.Code;
                    }
                }
                Assert.That(state, Is.Not.Zero);
                Assert.That(StatusCode.IsBad(status), Is.True);
                m_rejections.Add((state, status));
            }

            /// <summary>
            /// Owns the state machine and real simulation loop exercised with the fake clock.
            /// </summary>
            private readonly TimedManager m_manager;

            /// <summary>
            /// Captures rejection warnings and verifies the absence of unexpected errors.
            /// </summary>
            private readonly Mock<ILogger> m_logger = new();

            /// <summary>
            /// Preserves rejection payloads in emission order for exact logging assertions.
            /// </summary>
            private readonly List<(uint State, uint Status)> m_rejections = [];

            /// <summary>
            /// Signals that a clock tick reached the timed-transition guard.
            /// </summary>
            private readonly SemaphoreSlim m_guardReached = new(0);

            /// <summary>
            /// Supplies the next timed-transition guard result.
            /// </summary>
            private uint m_guardStatus;

            /// <summary>
            /// Counts retry attempts independently of how many warnings are emitted.
            /// </summary>
            private int m_guardCalls;

            /// <summary>
            /// Bypasses the controlled rejection while a test performs a manual state change.
            /// </summary>
            private bool m_manualTransition;
        }

        /// <summary>
        /// Supplies the fluent node manager and simulation lifetime for timed-transition logging tests.
        /// </summary>
        private sealed class TimedManager(IServerInternal server)
            : FluentNodeManagerBase(server, "urn:timed-transition-logging")
        {
        }
    }
}
