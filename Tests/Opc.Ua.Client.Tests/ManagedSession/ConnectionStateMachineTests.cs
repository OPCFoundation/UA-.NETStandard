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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Sessions;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Unit tests for <see cref="ConnectionStateMachine"/> state
    /// transitions, reconnect policy integration, and lifecycle.
    /// </summary>
    [TestFixture]
    public sealed class ConnectionStateMachineTests
    {
        private ILogger m_logger;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_logger = new Mock<ILogger>().Object;
        }

        #region Helper Methods

        private ConnectionStateMachine CreateMachine(
            IReconnectPolicy policy = null)
        {
            return new ConnectionStateMachine(
                policy ?? new ReconnectPolicy {
                    JitterFactor = 0.0,
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(10)
                },
                m_logger);
        }

        /// <summary>
        /// Helper that waits for the state machine to reach a
        /// specific state, with a timeout. Also handles the case
        /// where the target state was already reached and passed.
        /// </summary>
        private static async Task WaitForStateAsync(
            ConnectionStateMachine sm,
            ConnectionState target,
            int timeoutMs = 5000)
        {
            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object sender, ConnectionStateChangedEventArgs e)
            {
                if (e.NewState == target)
                {
                    tcs.TrySetResult(true);
                }
            }

            sm.StateChanged += Handler;
            try
            {
                if (sm.State == target)
                {
                    return;
                }

                using var cts = new CancellationTokenSource(timeoutMs);
                await using (cts.Token.Register(
                    () => tcs.TrySetCanceled()).ConfigureAwait(false))
                {
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                sm.StateChanged -= Handler;
            }
        }

        /// <summary>
        /// Attaches a recorder that captures all state transitions.
        /// </summary>
        private static StateTransitionRecorder AttachRecorder(
            ConnectionStateMachine sm)
        {
            var recorder = new StateTransitionRecorder();
            sm.StateChanged += recorder.Handler;
            return recorder;
        }

        /// <summary>
        /// Records all state transitions for later assertion.
        /// </summary>
        private sealed class StateTransitionRecorder
        {
            private readonly List<ConnectionStateChangedEventArgs> m_transitions = new();
            private readonly TaskCompletionSource<bool> m_done = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            private ConnectionState? m_waitTarget;

            public void Handler(
                object sender, ConnectionStateChangedEventArgs e)
            {
                lock (m_transitions)
                {
                    m_transitions.Add(e);
                    if (m_waitTarget.HasValue
                        && e.NewState == m_waitTarget.Value)
                    {
                        m_done.TrySetResult(true);
                    }
                }
            }

            public async Task WaitForStateAsync(
                ConnectionState target,
                int timeoutMs = 5000)
            {
                lock (m_transitions)
                {
                    if (m_transitions.Any(t => t.NewState == target))
                    {
                        return;
                    }
                    m_waitTarget = target;
                }

                using var cts = new CancellationTokenSource(timeoutMs);
                await using (cts.Token.Register(
                    () => m_done.TrySetCanceled()).ConfigureAwait(false))
                {
                    await m_done.Task.ConfigureAwait(false);
                }
            }

            public List<ConnectionStateChangedEventArgs> GetTransitions()
            {
                lock (m_transitions)
                {
                    return new List<ConnectionStateChangedEventArgs>(
                        m_transitions);
                }
            }

            public bool HasVisited(ConnectionState state)
            {
                lock (m_transitions)
                {
                    return m_transitions.Any(
                        t => t.NewState == state);
                }
            }
        }

        #endregion

        #region State Transition Tests

        [Test]
        public void InitialStateIsDisconnected()
        {
            var sm = CreateMachine();
            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(sm.IsConnected, Is.False);
        }

        [Test]
        public async Task StartTransitionsToConnecting()
        {
            await using var sm = CreateMachine();

            var connectTcs = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            sm.ConnectAsync = _ => connectTcs.Task;

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connecting)
                .ConfigureAwait(false);

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Connecting));

            connectTcs.SetResult(ServiceResult.Good);
        }

        [Test]
        public async Task SuccessfulConnectTransitionsToConnected()
        {
            await using var sm = CreateMachine();

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Connected));
            Assert.That(sm.IsConnected, Is.True);
        }

        [Test]
        public async Task FailedConnectTransitionsToReconnecting()
        {
            await using var sm = CreateMachine();

            sm.ConnectAsync = _ => Task.FromResult(
                new ServiceResult(StatusCodes.BadConnectionClosed));

            sm.ReconnectAsync = async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct)
                    .ConfigureAwait(false);
                return ServiceResult.Good;
            };

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(
                    sm, ConnectionState.Reconnecting)
                .ConfigureAwait(false);

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Reconnecting));
        }

        [Test]
        public async Task TriggerReconnectFromConnectedTransitionsToReconnecting()
        {
            await using var sm = CreateMachine();

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.ReconnectAsync = async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct)
                    .ConfigureAwait(false);
                return ServiceResult.Good;
            };

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            sm.TriggerReconnect();

            await WaitForStateAsync(
                    sm, ConnectionState.Reconnecting)
                .ConfigureAwait(false);

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Reconnecting));
        }

        [Test]
        public async Task RequestCloseTransitionsToClosing()
        {
            await using var sm = CreateMachine();

            var closeTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.CloseSessionAsync = async _ =>
            {
                await closeTcs.Task.ConfigureAwait(false);
            };

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            var recorder = AttachRecorder(sm);

            sm.RequestClose();

            await recorder.WaitForStateAsync(ConnectionState.Closing)
                .ConfigureAwait(false);

            Assert.That(
                recorder.HasVisited(ConnectionState.Closing),
                Is.True);

            closeTcs.SetResult(true);
        }

        [Test]
        public async Task DisposeTransitionsToClosed()
        {
            var sm = CreateMachine();

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            await sm.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Closed));
        }

        [Test]
        public async Task StateChangedEventFires()
        {
            await using var sm = CreateMachine();
            var recorder = AttachRecorder(sm);

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.Start();
            sm.RequestConnect();

            await recorder.WaitForStateAsync(
                    ConnectionState.Connected)
                .ConfigureAwait(false);

            var transitions = recorder.GetTransitions();

            Assert.That(
                transitions, Has.Count.GreaterThanOrEqualTo(2));

            Assert.That(
                transitions[0].PreviousState,
                Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(
                transitions[0].NewState,
                Is.EqualTo(ConnectionState.Connecting));

            Assert.That(
                transitions[1].PreviousState,
                Is.EqualTo(ConnectionState.Connecting));
            Assert.That(
                transitions[1].NewState,
                Is.EqualTo(ConnectionState.Connected));
        }

        [Test]
        public async Task WaitForConnectedAsyncReturnsWhenConnected()
        {
            await using var sm = CreateMachine();

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(2));

            await sm.WaitForConnectedAsync(cts.Token)
                .ConfigureAwait(false);

            Assert.That(sm.IsConnected, Is.True);
        }

        [Test]
        public async Task WaitForConnectedAsyncBlocksWhenDisconnected()
        {
            await using var sm = CreateMachine();

            var connectTcs = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            sm.ConnectAsync = _ => connectTcs.Task;

            sm.Start();
            sm.RequestConnect();

            using var cts = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(200));

            var waitTask = sm.WaitForConnectedAsync(cts.Token);

            Assert.That(waitTask.IsCompleted, Is.False);

            bool cancelled = false;
            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.That(cancelled, Is.True,
                "WaitForConnectedAsync should cancel when " +
                "token fires before connected");

            connectTcs.SetResult(ServiceResult.Good);
        }

        #endregion

        #region Reconnect Policy Integration

        [Test]
        public async Task ReconnectUsesBackoffDelays()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Exponential,
                InitialDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromSeconds(5),
                JitterFactor = 0.0,
                MaxRetries = 5
            };

            await using var sm = CreateMachine(policy);

            int attempt = 0;
            var attemptTimes = new List<DateTime>();

            sm.ConnectAsync = _ => Task.FromResult(
                new ServiceResult(StatusCodes.BadConnectionClosed));

            sm.ReconnectAsync = _ =>
            {
                Interlocked.Increment(ref attempt);
                lock (attemptTimes)
                {
                    attemptTimes.Add(DateTime.UtcNow);
                }

                return Task.FromResult(
                    new ServiceResult(StatusCodes.BadConnectionClosed));
            };

            sm.FailoverAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            var recorder = AttachRecorder(sm);

            sm.Start();
            sm.RequestConnect();

            await recorder.WaitForStateAsync(
                    ConnectionState.Connected, 10000)
                .ConfigureAwait(false);

            lock (attemptTimes)
            {
                Assert.That(
                    attemptTimes.Count,
                    Is.GreaterThanOrEqualTo(2),
                    "Expected multiple reconnect attempts");

                if (attemptTimes.Count >= 3)
                {
                    var gap1 = attemptTimes[1] - attemptTimes[0];
                    var gap2 = attemptTimes[2] - attemptTimes[1];

                    Assert.That(
                        gap2.TotalMilliseconds,
                        Is.GreaterThanOrEqualTo(
                            gap1.TotalMilliseconds * 0.8),
                        "Exponential backoff should increase " +
                        "delays");
                }
            }
        }

        [Test]
        public async Task ReconnectStopsAfterMaxRetries()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromMilliseconds(5),
                MaxRetries = 3,
                JitterFactor = 0.0
            };

            await using var sm = CreateMachine(policy);

            sm.ConnectAsync = _ => Task.FromResult(
                new ServiceResult(StatusCodes.BadConnectionClosed));

            sm.ReconnectAsync = _ => Task.FromResult(
                new ServiceResult(StatusCodes.BadConnectionClosed));

            sm.FailoverAsync = _ => Task.FromResult(
                new ServiceResult(StatusCodes.BadNotSupported));

            var recorder = AttachRecorder(sm);

            sm.Start();
            sm.RequestConnect();

            // Failover → Disconnected can be fast; use recorder
            // to check that Failover was visited.
            await recorder.WaitForStateAsync(
                    ConnectionState.Disconnected, 5000)
                .ConfigureAwait(false);

            Assert.That(
                recorder.HasVisited(ConnectionState.Failover),
                Is.True,
                "Should have visited Failover state");

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Disconnected));
        }

        [Test]
        public async Task ReconnectPolicyResetAfterSuccess()
        {
            var mockPolicy = new Mock<IReconnectPolicy>();

            mockPolicy.Setup(p => p.GetNextDelay(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
                .Returns(TimeSpan.FromMilliseconds(5));

            await using var sm = new ConnectionStateMachine(
                mockPolicy.Object, m_logger);

            int connectCallCount = 0;
            sm.ConnectAsync = _ =>
            {
                int call = Interlocked.Increment(
                    ref connectCallCount);
                if (call == 1)
                {
                    return Task.FromResult(
                        new ServiceResult(
                            StatusCodes.BadConnectionClosed));
                }
                return Task.FromResult(ServiceResult.Good);
            };

            sm.ReconnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            mockPolicy.Verify(
                p => p.Reset(),
                Times.AtLeastOnce,
                "Policy should be reset after successful " +
                "reconnect");
        }

        #endregion

        #region Service Call Gating

        [Test]
        public async Task WaitForConnectedAsyncCompletesAfterReconnect()
        {
            await using var sm = CreateMachine();

            var connectTcs = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            sm.ConnectAsync = _ => connectTcs.Task;

            sm.Start();
            sm.RequestConnect();

            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));

            var waitTask = sm.WaitForConnectedAsync(cts.Token);

            Assert.That(waitTask.IsCompleted, Is.False);

            connectTcs.SetResult(ServiceResult.Good);

            await waitTask.ConfigureAwait(false);

            Assert.That(sm.IsConnected, Is.True);
        }

        [Test]
        public async Task WaitForConnectedBlocksDuringReconnect()
        {
            var sm = CreateMachine();

            int reconnectCount = 0;
            var reconnectTcs = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.ReconnectAsync = ct =>
            {
                int count = Interlocked.Increment(
                    ref reconnectCount);
                if (count == 1)
                {
                    return reconnectTcs.Task.WaitAsync(ct);
                }
                return Task.FromResult(ServiceResult.Good);
            };

            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            sm.TriggerReconnect();

            await WaitForStateAsync(
                    sm, ConnectionState.Reconnecting)
                .ConfigureAwait(false);

            using var cts = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(200));

            var waitTask = sm.WaitForConnectedAsync(cts.Token);

            Assert.That(waitTask.IsCompleted, Is.False);

            bool cancelled = false;
            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.That(cancelled, Is.True,
                "WaitForConnectedAsync should block during " +
                "reconnect");

            reconnectTcs.SetResult(ServiceResult.Good);

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            await sm.DisposeAsync().ConfigureAwait(false);
        }

        #endregion

        #region Edge Cases

        [Test]
        public async Task DoubleDisposeDoesNotThrow()
        {
            var sm = CreateMachine();
            sm.Start();

            await sm.DisposeAsync().ConfigureAwait(false);
            await sm.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Closed));
        }

        [Test]
        public async Task TriggerReconnectWhenDisconnectedIsNoOp()
        {
            await using var sm = CreateMachine();

            sm.TriggerReconnect();

            Assert.That(
                sm.State,
                Is.EqualTo(ConnectionState.Disconnected));
        }

        [Test]
        public void RequestCloseWhenAlreadyClosedIsNoOp()
        {
            var sm = CreateMachine();

            sm.RequestClose();

            Assert.That(
                sm.State,
                Is.AnyOf(
                    ConnectionState.Closing,
                    ConnectionState.Disconnected));
        }

        [Test]
        public async Task StartIsIdempotent()
        {
            await using var sm = CreateMachine();

            sm.ConnectAsync = _ =>
                Task.FromResult(ServiceResult.Good);

            sm.Start();
            sm.Start();
            sm.RequestConnect();

            await WaitForStateAsync(sm, ConnectionState.Connected)
                .ConfigureAwait(false);

            Assert.That(sm.IsConnected, Is.True);
        }

        #endregion
    }
}
