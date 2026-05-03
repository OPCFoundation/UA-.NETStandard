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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// End-to-end integration tests for <see cref="ManagedSessionType"/>
    /// reconnect behavior against the in-process reference fixture
    /// server. Disposing the underlying transport channel forces the
    /// keep-alive timer to detect the loss; the
    /// <see cref="ConnectionStateMachine"/> must transition through
    /// <see cref="ConnectionState.Reconnecting"/> back to
    /// <see cref="ConnectionState.Connected"/> automatically without
    /// caller intervention.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [Category("ManagedSession")]
    [Category("Reconnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ManagedSessionReconnectIntegrationTests
        : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        [CancelAfter(120_000)]
        public async Task ChannelLossTriggersAutomaticReconnect(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(ChannelLossTriggersAutomaticReconnect))
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Exponential,
                    InitialDelay = TimeSpan.FromMilliseconds(200),
                    MaxDelay = TimeSpan.FromSeconds(2),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                })
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            // Speed up loss detection.
            session.KeepAliveInterval = 1_000;

            var states = new ConcurrentQueue<ConnectionState>();
            var reconnected = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.ConnectionStateChanged += (_, e) =>
            {
                states.Enqueue(e.NewState);
                if (e.PreviousState == ConnectionState.Reconnecting &&
                    e.NewState == ConnectionState.Connected)
                {
                    reconnected.TrySetResult(true);
                }
            };

            try
            {
                Assert.That(session.Connected, Is.True);

                // Read once to confirm the session works.
                DataValue value = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(value, Is.Not.Null);

                TestContext.Out.WriteLine(
                    "Closing transport channel to force reconnect…");
                session.InnerSession.TransportChannel.Dispose();

                // Wait for the state machine to detect loss and recover.
                bool ok = await WaitOrCanceledAsync(
                    reconnected.Task, TimeSpan.FromSeconds(60), ct)
                    .ConfigureAwait(false);

                Assert.That(
                    ok,
                    Is.True,
                    "ManagedSession should automatically transition " +
                    "Reconnecting -> Connected after the channel is " +
                    "disposed.");

                // The stable state must be Connected again.
                Assert.That(session.Connected, Is.True);

                // A read service call must succeed against the recovered
                // session.
                DataValue valueAfter = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(valueAfter, Is.Not.Null);

                // We must have observed the Reconnecting state.
                Assert.That(states, Has.Member(ConnectionState.Reconnecting));
                Assert.That(states, Has.Member(ConnectionState.Connected));

                TestContext.Out.WriteLine(
                    "Observed state transitions: {0}",
                    string.Join(" -> ", states));
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(120_000)]
        public async Task ServiceCallsResumeAfterAutomaticReconnect(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(ServiceCallsResumeAfterAutomaticReconnect))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(500),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                })
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            session.KeepAliveInterval = 1_000;

            var reconnected = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.ConnectionStateChanged += (_, e) =>
            {
                if (e.PreviousState == ConnectionState.Reconnecting &&
                    e.NewState == ConnectionState.Connected)
                {
                    reconnected.TrySetResult(true);
                }
            };

            try
            {
                // Confirm the session works before forcing the loss.
                DataValue valueBefore = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(valueBefore.StatusCode), Is.True);

                // Kill the transport channel and wait for the
                // KeepAlive-driven reconnect to recover.
                session.InnerSession.TransportChannel.Dispose();

                bool ok = await WaitOrCanceledAsync(
                    reconnected.Task, TimeSpan.FromSeconds(60), ct)
                    .ConfigureAwait(false);
                Assert.That(
                    ok,
                    Is.True,
                    "ManagedSession should auto-reconnect after channel " +
                    "loss before any new service call is issued.");

                // Subsequent service calls must succeed. Try a few in
                // sequence to confirm the recovered session is
                // operational.
                for (int i = 0; i < 3; i++)
                {
                    DataValue valueAfter = await session
                        .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                        .ConfigureAwait(false);

                    Assert.That(valueAfter, Is.Not.Null);
                    Assert.That(
                        StatusCode.IsGood(valueAfter.StatusCode),
                        Is.True,
                        $"Read #{i} after reconnect must return Good; was " +
                        valueAfter.StatusCode);
                }

                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(300)]
        [CancelAfter(60_000)]
        public async Task DisposeDuringReconnectTransitionsToClosed(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(DisposeDuringReconnectTransitionsToClosed))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromSeconds(2),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                })
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            session.KeepAliveInterval = 500;

            var enteredReconnecting = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.ConnectionStateChanged += (_, e) =>
            {
                if (e.NewState == ConnectionState.Reconnecting)
                {
                    enteredReconnecting.TrySetResult(true);
                }
            };

            // Trigger reconnect.
            session.InnerSession.TransportChannel.Dispose();

            bool reconnecting = await WaitOrCanceledAsync(
                enteredReconnecting.Task, TimeSpan.FromSeconds(15), ct)
                .ConfigureAwait(false);
            Assert.That(
                reconnecting,
                Is.True,
                "Session should enter Reconnecting after channel loss.");

            // Now dispose while still reconnecting.
            await session.DisposeAsync().ConfigureAwait(false);

            // After dispose, the state machine must end in Closed.
            Assert.That(
                session.StateMachine.State,
                Is.EqualTo(ConnectionState.Closed));
        }

        [Test]
        [Order(400)]
        [CancelAfter(60_000)]
        public async Task ConnectionStateChangedReportsExpectedSequence(
            CancellationToken ct)
        {
            var observed = new ConcurrentQueue<ConnectionStateChangedEventArgs>();
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            var builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(ConnectionStateChangedReportsExpectedSequence))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(200),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                });

            ManagedSessionType session = await builder.ConnectAsync(ct)
                .ConfigureAwait(false);

            try
            {
                session.ConnectionStateChanged +=
                    (_, e) => observed.Enqueue(e);

                Assert.That(session.Connected, Is.True);

                await session.CloseAsync().ConfigureAwait(false);

                // After Close we expect at least: Connected -> Closing
                // and Closing -> Closed.
                bool sawClosing = await WaitForAsync(
                    () =>
                    {
                        foreach (ConnectionStateChangedEventArgs e in observed)
                        {
                            if (e.NewState == ConnectionState.Closing)
                            {
                                return true;
                            }
                        }
                        return false;
                    },
                    TimeSpan.FromSeconds(10),
                    ct).ConfigureAwait(false);
                Assert.That(
                    sawClosing,
                    Is.True,
                    "Session must announce Closing state on close.");

                bool sawClosed = await WaitForAsync(
                    () =>
                    {
                        foreach (ConnectionStateChangedEventArgs e in observed)
                        {
                            if (e.NewState == ConnectionState.Closed)
                            {
                                return true;
                            }
                        }
                        return false;
                    },
                    TimeSpan.FromSeconds(10),
                    ct).ConfigureAwait(false);
                Assert.That(sawClosed, Is.True);

                // Every transition's PreviousState must match the prior
                // event's NewState (events are serialized through the
                // state machine).
                ConnectionStateChangedEventArgs[] arr = [.. observed];
                for (int i = 1; i < arr.Length; i++)
                {
                    Assert.That(
                        arr[i].PreviousState,
                        Is.EqualTo(arr[i - 1].NewState),
                        $"State events must form a chain; broke at index {i}.");
                }
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(500)]
        [CancelAfter(120_000)]
        public async Task FailoverInvokesRedundancyHandlerWhenReconnectExhausted(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            // The fake handler returns the same endpoint as the failover
            // target — we only have one reference fixture, so "failover"
            // recreates a session against the same server. This is
            // enough to exercise the entire ManagedSession.HandleFailoverAsync
            // path: SelectFailoverTarget -> SessionFactory.CreateAsync ->
            // WireSessionEvents -> dispose old session.
            var fakeHandler = new FakeRedundancyHandler(endpoint);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(FailoverInvokesRedundancyHandlerWhenReconnectExhausted))
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(50),
                    MaxRetries = 1,
                    JitterFactor = 0.0
                })
                .WithServerRedundancy(fakeHandler)
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            // Force the reconnect attempt itself to fail so the state
            // machine exhausts its retries and falls through to the
            // Failover state. Without this, the channel is restored on
            // the same alive server and reconnect succeeds on attempt 0.
            session.StateMachine.ReconnectAsync = _ =>
                Task.FromResult(new ServiceResult(StatusCodes.BadNotConnected));

            var states = new ConcurrentQueue<ConnectionState>();
            var failoverEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var connectedAfterFailover = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.ConnectionStateChanged += (_, e) =>
            {
                states.Enqueue(e.NewState);
                if (e.NewState == ConnectionState.Failover)
                {
                    failoverEntered.TrySetResult(true);
                }
                if (e.PreviousState == ConnectionState.Failover &&
                    e.NewState == ConnectionState.Connected)
                {
                    connectedAfterFailover.TrySetResult(true);
                }
            };

            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(
                    fakeHandler.FetchCount, Is.GreaterThanOrEqualTo(1),
                    "ManagedSession should fetch redundancy info on connect.");

                TestContext.Out.WriteLine(
                    "Triggering reconnect with stub returning BadNotConnected " +
                    "so retries exhaust and Failover engages…");
                session.StateMachine.TriggerReconnect();

                bool sawFailover = await WaitOrCanceledAsync(
                    failoverEntered.Task,
                    TimeSpan.FromSeconds(60),
                    ct).ConfigureAwait(false);

                Assert.That(
                    sawFailover, Is.True,
                    "State machine should reach Failover after reconnect retries are exhausted.");

                bool sawConnectedAfter = await WaitOrCanceledAsync(
                    connectedAfterFailover.Task,
                    TimeSpan.FromSeconds(60),
                    ct).ConfigureAwait(false);

                Assert.That(
                    sawConnectedAfter, Is.True,
                    "After failover the session should return to Connected.");
                Assert.That(
                    fakeHandler.SelectCount, Is.GreaterThanOrEqualTo(1),
                    "ManagedSession.HandleFailoverAsync should call SelectFailoverTarget.");
                Assert.That(session.Connected, Is.True);

                // The session that completes the failover must answer
                // service calls.
                DataValue valueAfter = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(valueAfter, Is.Not.Null);

                // We must have observed Reconnecting → Failover → Connected.
                Assert.That(states, Has.Member(ConnectionState.Reconnecting));
                Assert.That(states, Has.Member(ConnectionState.Failover));
                Assert.That(states, Has.Member(ConnectionState.Connected));

                TestContext.Out.WriteLine(
                    "Observed state transitions: {0}",
                    string.Join(" -> ", states));
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(600)]
        [CancelAfter(60_000)]
        public async Task FailoverWithNullTargetReturnsToDisconnected(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            // Handler always picks "no failover target available".
            var fakeHandler = new FakeRedundancyHandler(target: null);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(FailoverWithNullTargetReturnsToDisconnected))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(50),
                    MaxRetries = 1,
                    JitterFactor = 0.0
                })
                .WithServerRedundancy(fakeHandler)
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            // Force reconnect attempts to fail so the state machine
            // proceeds to Failover.
            session.StateMachine.ReconnectAsync = _ =>
                Task.FromResult(new ServiceResult(StatusCodes.BadNotConnected));

            var failoverEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var leftFailover = new TaskCompletionSource<ConnectionState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.ConnectionStateChanged += (_, e) =>
            {
                if (e.NewState == ConnectionState.Failover)
                {
                    failoverEntered.TrySetResult(true);
                }
                if (e.PreviousState == ConnectionState.Failover)
                {
                    leftFailover.TrySetResult(e.NewState);
                }
            };

            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(
                    fakeHandler.FetchCount, Is.GreaterThanOrEqualTo(1),
                    "FetchRedundancyInfoAsync should have been called on connect.");

                session.StateMachine.TriggerReconnect();

                bool sawFailover = await WaitOrCanceledAsync(
                    failoverEntered.Task,
                    TimeSpan.FromSeconds(30),
                    ct).ConfigureAwait(false);
                Assert.That(sawFailover, Is.True);

                using var leftCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(30));
                using var leftLinked = CancellationTokenSource
                    .CreateLinkedTokenSource(leftCts.Token, ct);
                ConnectionState next = await leftFailover.Task
                    .WaitAsync(leftLinked.Token)
                    .ConfigureAwait(false);

                // SelectFailoverTarget runs inside HandleFailoverAsync
                // before the state leaves Failover, so it must be
                // observed by the time leftFailover signals.
                Assert.That(
                    fakeHandler.SelectCount, Is.GreaterThanOrEqualTo(1),
                    "SelectFailoverTarget must be called even when it returns null.");

                // BadNothingToDo from HandleFailoverAsync drops the state
                // machine back to Disconnected (not Connected).
                Assert.That(
                    next, Is.EqualTo(ConnectionState.Disconnected),
                    "When no failover target is available the session " +
                    "should return to Disconnected.");
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test double for <see cref="IServerRedundancyHandler"/> that
        /// records call counts and returns a caller-supplied failover
        /// endpoint. Always reports a non-transparent redundancy mode so
        /// the failover path is engaged.
        /// </summary>
        private sealed class FakeRedundancyHandler : IServerRedundancyHandler
        {
            public FakeRedundancyHandler(ConfiguredEndpoint? target)
            {
                m_target = target;
            }

            public int FetchCount { get; private set; }
            public int SelectCount { get; private set; }

            public ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
                ISession session,
                CancellationToken ct = default)
            {
                FetchCount++;
                return new ValueTask<ServerRedundancyInfo>(
                    new ServerRedundancyInfo
                    {
                        Mode = RedundancyMode.Cold,
                        ServiceLevel = 200,
                        RedundantServers =
                        [
                            new RedundantServer
                            {
                                ServerUri = "urn:fake:redundant",
                                ServerState = ServerState.Running,
                                ServiceLevel = 250
                            }
                        ]
                    });
            }

            public ConfiguredEndpoint? SelectFailoverTarget(
                ServerRedundancyInfo redundancyInfo,
                ConfiguredEndpoint currentEndpoint)
            {
                SelectCount++;
                return m_target;
            }

            private readonly ConfiguredEndpoint? m_target;
        }

        private static async Task<bool> WaitOrCanceledAsync(
            Task<bool> waitTask,
            TimeSpan timeout,
            CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, ct);
            Task delay = Task.Delay(Timeout.Infinite, linked.Token);
            Task winner = await Task.WhenAny(waitTask, delay).ConfigureAwait(false);
            if (winner == waitTask)
            {
                return await waitTask.ConfigureAwait(false);
            }
            return false;
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> predicate,
            TimeSpan timeout,
            CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (predicate())
                {
                    return true;
                }
                try
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return predicate();
                }
            }
            return predicate();
        }
    }
}
