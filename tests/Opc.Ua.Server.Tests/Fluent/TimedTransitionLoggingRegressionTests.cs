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
    [TestFixture]
    [Category("Fluent")]
    public sealed class TimedTransitionLoggingRegressionTests
    {
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

        private static readonly StatusCode[] s_rejectionStatuses =
        [
            StatusCodes.BadWaitingForInitialData,
            StatusCodes.BadUserAccessDenied,
            StatusCodes.BadStateNotActive
        ];

        private sealed class TimedHarness : IAsyncDisposable
        {
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

            public FakeTimeProvider Clock { get; } = new();
            public ServerSystemContext Context { get; }
            public FluentFiniteStateMachineState Machine { get; }
            public int GuardCalls => Volatile.Read(ref m_guardCalls);
            public uint CurrentState => Machine.GetStateId(Machine.CurrentState!.Id!.Value);
            public ArrayOf<(uint State, uint Status)> Rejections => m_rejections.ToArray();

            public StatusCode GuardStatus
            {
                get => new(Volatile.Read(ref m_guardStatus));
                set => Volatile.Write(ref m_guardStatus, value.Code);
            }

            public async ValueTask TickAsync(TimeSpan? elapsed = null)
            {
                Clock.Advance(elapsed ?? TimeSpan.FromMilliseconds(100));
                bool reached = await m_guardReached.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(reached, Is.True, "The registered simulation did not attempt its timed transition.");
            }

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

            public async ValueTask DisposeAsync()
            {
                await m_manager.Simulations.StopAsync().ConfigureAwait(false);
                await m_manager.DisposeAsync().ConfigureAwait(false);
                m_guardReached.Dispose();
            }

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

            private readonly TimedManager m_manager;
            private readonly Mock<ILogger> m_logger = new();
            private readonly List<(uint State, uint Status)> m_rejections = [];
            private readonly SemaphoreSlim m_guardReached = new(0);
            private uint m_guardStatus;
            private int m_guardCalls;
            private bool m_manualTransition;
        }

        private sealed class TimedManager(IServerInternal server)
            : FluentNodeManagerBase(server, "urn:timed-transition-logging")
        {
        }
    }
}
