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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Managed session connect and recovery over reverse connections that an
    /// <see cref="IReverseConnectionSource"/> supplies. The reference server
    /// keeps dialing the client's reverse connect listener; the tests wrap the
    /// listener's <see cref="ReverseConnectManager"/> in a recording source.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("Reconnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class ManagedSessionReverseConnectionSourceTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override async Task OneTimeSetUpAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Reverse connect fails on mac OS.");
            }

            PkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            ServerFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                ReverseConnectTimeout = MaxTimeout,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };
            ReferenceServer = await ServerFixture.StartAsync(PkiRoot).ConfigureAwait(false);

            ClientFixture = new ClientFixture(telemetry: Telemetry);
            await ClientFixture.LoadClientConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ClientConfiguration clientConfiguration = ClientFixture.Config.ClientConfiguration ??
                throw new InvalidOperationException("Client configuration is missing.");
            ReverseConnectClientConfiguration reverseConnect =
                clientConfiguration.ReverseConnect ??= new ReverseConnectClientConfiguration();
            reverseConnect.HoldTime = MaxTimeout / 2;
            await ClientFixture.StartReverseConnectHostAsync().ConfigureAwait(false);
            m_endpointUrl = new Uri(
                Utils.ReplaceLocalhost(
                    "opc.tcp://localhost:" + ServerFixture.Port.ToString(CultureInfo.InvariantCulture)));
            ReferenceServer.AddReverseConnection(new Uri(ClientFixture.ReverseConnectUri), MaxTimeout);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ITransportWaitingConnection connection = await ClientFixture.ReverseConnectManager
                .WaitForConnectionAsync(m_endpointUrl, null, timeout.Token).ConfigureAwait(false);
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = MaxTimeout;
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ClientFixture.Config,
                connection,
                endpointConfiguration,
                ct: timeout.Token).ConfigureAwait(false);
            Endpoints = await client.GetEndpointsAsync(default, timeout.Token).ConfigureAwait(false);
            await client.CloseAsync(timeout.Token).ConfigureAwait(false);
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
        [CancelAfter(60_000)]
        public async Task InitialConnectAwaitsTheSourceOnceAsync(CancellationToken ct)
        {
            var source = new RecordingSource(ClientFixture.ReverseConnectManager, _ => SourceBehavior.Deliver);

            ManagedSessionType session = await ConnectAsync(source, nameof(InitialConnectAwaitsTheSourceOnceAsync),
                ConstantPolicy(TimeSpan.FromMilliseconds(200)), ct).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(source.CallCount, Is.EqualTo(1));
                Assert.That(source.Requests.Single().ServerUri, Is.EqualTo(ServerUri));
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(state.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task RecoveryReactivatesTheSessionOverAConnectionFromTheSourceAsync(CancellationToken ct)
        {
            var source = new RecordingSource(ClientFixture.ReverseConnectManager, _ => SourceBehavior.Deliver);
            ManagedSessionType session = await ConnectAsync(source,
                nameof(RecoveryReactivatesTheSessionOverAConnectionFromTheSourceAsync),
                ConstantPolicy(TimeSpan.FromMilliseconds(200)), ct).ConfigureAwait(false);
            session.KeepAliveInterval = 1_000;
            var states = new StateRecorder(session);
            var handler = new SequenceRecordingHandler();
            try
            {
                ISubscription subscription = AddCurrentTimeSubscription(session, handler);
                Assert.That(await WaitForAsync(() => handler.MaxSequenceNumber >= 2, ct).ConfigureAwait(false),
                    Is.True, "The subscription must deliver data before the connection is lost.");
                NodeId sessionId = session.SessionId;
                int callsBefore = source.CallCount;

                CloseTransport(source.LastConnection!);
                Assert.That(await WaitForAsync(() => states.Reconnected, ct).ConfigureAwait(false), Is.True,
                    "The session must recover over a connection from the source.");
                uint lastBefore = handler.MaxSequenceNumberBefore(states.LossObservedAt);

                Assert.That(session.SessionId, Is.EqualTo(sessionId), "Recovery must reactivate the session.");
                Assert.That(source.CallCount - callsBefore, Is.EqualTo(states.FailedAttempts + 1),
                    "Every recovery attempt awaits the source exactly once.");
                int afterReconnect = handler.Count;
                Assert.That(await WaitForAsync(() => handler.Count > afterReconnect + 1, ct).ConfigureAwait(false),
                    Is.True, "The subscription must continue to deliver data.");
                Assert.That(subscription.Created, Is.True);
                Assert.That(handler.MinSequenceNumberFrom(afterReconnect), Is.GreaterThan(lastBefore),
                    "The subscription continues its sequence; it was not recreated.");
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task EachRecoveryAttemptAwaitsTheSourceOnceFollowingThePolicyAsync(CancellationToken ct)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(500);
            var source = new RecordingSource(
                ClientFixture.ReverseConnectManager,
                call => call is 2 or 3 ? SourceBehavior.Fail : SourceBehavior.Deliver);
            ManagedSessionType session = await ConnectAsync(source,
                nameof(EachRecoveryAttemptAwaitsTheSourceOnceFollowingThePolicyAsync),
                ConstantPolicy(delay), ct).ConfigureAwait(false);
            session.KeepAliveInterval = 1_000;
            var states = new StateRecorder(session);
            try
            {
                NodeId sessionId = session.SessionId;
                CloseTransport(source.LastConnection!);
                Assert.That(await WaitForAsync(() => states.Reconnected, ct).ConfigureAwait(false), Is.True);

                Assert.That(source.CallCount, Is.EqualTo(4));
                Assert.That(states.FailedAttempts, Is.EqualTo(2));
                Assert.That(session.SessionId, Is.EqualTo(sessionId));
                DateTime[] calls = [.. source.Requests.Select(r => r.At)];
                TimeSpan tolerance = TimeSpan.FromMilliseconds(100);
                Assert.That(calls[2] - calls[1], Is.GreaterThanOrEqualTo(delay - tolerance));
                Assert.That(calls[3] - calls[2], Is.GreaterThanOrEqualTo(delay - tolerance));
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task ASourceThatNeverYieldsEndsRecoveryWhenTheBudgetIsExhaustedAsync(CancellationToken ct)
        {
            TimeSpan budget = TimeSpan.FromSeconds(3);
            var source = new RecordingSource(
                ClientFixture.ReverseConnectManager,
                call => call == 1 ? SourceBehavior.Deliver : SourceBehavior.Block);
            ManagedSessionType session = await ConnectAsync(source,
                nameof(ASourceThatNeverYieldsEndsRecoveryWhenTheBudgetIsExhaustedAsync),
                ConstantPolicy(TimeSpan.FromMilliseconds(200)) with { MaxTotalReconnectTime = budget },
                ct).ConfigureAwait(false);
            session.KeepAliveInterval = 1_000;
            var states = new StateRecorder(session);
            try
            {
                CloseTransport(source.LastConnection!);
                Assert.That(await WaitForAsync(() => states.Disconnected, ct).ConfigureAwait(false), Is.True,
                    "The session must give up once the reconnect budget is exhausted.");

                Assert.That(source.CallCount, Is.EqualTo(2), "One wait consumed the whole budget.");
                Assert.That(source.CancelledCalls, Does.Contain(2));
                TimeSpan waited = source.CancelledAt(2) - source.Requests[1].At;
                Assert.That(waited, Is.GreaterThan(budget - TimeSpan.FromSeconds(1)));
                Assert.That(waited, Is.LessThanOrEqualTo(budget + TimeSpan.FromMilliseconds(500)));
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task RetriesAfterAFailedInitialConnectWaitWithinTheBudgetAsync(CancellationToken ct)
        {
            TimeSpan budget = TimeSpan.FromSeconds(3);
            var source = new RecordingSource(
                ClientFixture.ReverseConnectManager,
                call => call == 1 ? SourceBehavior.Fail : SourceBehavior.Block);

            Task<ManagedSessionType> connect = ConnectAsync(source,
                nameof(RetriesAfterAFailedInitialConnectWaitWithinTheBudgetAsync),
                ConstantPolicy(TimeSpan.FromMilliseconds(200)) with { MaxTotalReconnectTime = budget },
                ct);
            Task completed = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(30), ct))
                .ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(connect),
                "The connect must give up once the reconnect budget is exhausted.");

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await connect.ConfigureAwait(false)))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(source.CallCount, Is.EqualTo(2), "One wait consumed the rest of the budget.");
            Assert.That(source.CancelledCalls, Does.Contain(2));
            TimeSpan waited = source.CancelledAt(2) - source.Requests[0].At;
            Assert.That(waited, Is.LessThanOrEqualTo(budget + TimeSpan.FromMilliseconds(500)));
        }

        [Test]
        [CancelAfter(60_000)]
        public async Task DiscoveryConsumesTheInitialConnectionAndTheSessionUsesTheSourceAsync(CancellationToken ct)
        {
            var source = new RecordingSource(ClientFixture.ReverseConnectManager, _ => SourceBehavior.Deliver);
            ITransportWaitingConnection initial = await ClientFixture.ReverseConnectManager
                .WaitForConnectionAsync(m_endpointUrl, ServerUri, ct).ConfigureAwait(false);

            ManagedSessionType session = await ManagedSessionType.CreateAsync(
                new ManagedSessionOptions
                {
                    Endpoint = CreateEndpoint(),
                    SessionName = nameof(DiscoveryConsumesTheInitialConnectionAndTheSessionUsesTheSourceAsync),
                    ReconnectPolicy = ConstantPolicy(TimeSpan.FromMilliseconds(200)),
                    ReverseConnectionSource = source
                },
                ClientFixture.Config,
                new DefaultSessionFactory(Telemetry),
                connection: initial,
                updateBeforeConnect: true,
                ct: ct).ConfigureAwait(false);
            try
            {
                Assert.That(source.CallCount, Is.EqualTo(1),
                    "The session must use a connection from the source after discovery.");
                Assert.That(source.LastConnection, Is.Not.SameAs(initial));
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(state.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task DisposingWhileRecoveryWaitsCancelsTheSourceAsync(CancellationToken ct)
        {
            var source = new RecordingSource(
                ClientFixture.ReverseConnectManager,
                call => call == 1 ? SourceBehavior.Deliver : SourceBehavior.Block);
            ManagedSessionType session = await ConnectAsync(source,
                nameof(DisposingWhileRecoveryWaitsCancelsTheSourceAsync),
                ConstantPolicy(TimeSpan.FromMilliseconds(200)), ct).ConfigureAwait(false);
            session.KeepAliveInterval = 1_000;

            CloseTransport(source.LastConnection!);
            Assert.That(await WaitForAsync(() => source.CallCount == 2, ct).ConfigureAwait(false), Is.True);
            Assert.That(source.CancelledCalls, Is.Empty);

            await CloseAndDisposeAsync(session).ConfigureAwait(false);

            Assert.That(await WaitForAsync(() => source.CancelledCalls.Contains(2), ct).ConfigureAwait(false),
                Is.True, "Disposal must cancel the pending wait.");
            Assert.That(source.CallCount, Is.EqualTo(2));
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task ReverseConnectManagerRecoveryIsUnchangedAsync(CancellationToken ct)
        {
            ManagedSessionType session = await new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(CreateEndpoint())
                .UseReverseConnect(ClientFixture.ReverseConnectManager, new Uri(ServerUri))
                .WithSessionName(nameof(ReverseConnectManagerRecoveryIsUnchangedAsync))
                .WithReconnectPolicy(_ => ConstantPolicy(TimeSpan.FromMilliseconds(200)))
                .ConnectAsync(ct)
                .ConfigureAwait(false);
            session.KeepAliveInterval = 1_000;
            var states = new StateRecorder(session);
            try
            {
                NodeId sessionId = session.SessionId;
                session.InnerSession.TransportChannel.Dispose();
                Assert.That(await WaitForAsync(() => states.Reconnected, ct).ConfigureAwait(false), Is.True);
                Assert.That(session.SessionId, Is.EqualTo(sessionId));
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(state.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        private string ServerUri => Endpoints[0].Server.ApplicationUri!;

        private ConfiguredEndpoint CreateEndpoint()
        {
            EndpointDescription description = ClientFixture.SelectEndpoint(
                ClientFixture.Config, Endpoints, m_endpointUrl, SecurityPolicies.None);
            return new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(ClientFixture.Config))
            {
                UpdateBeforeConnect = false
            };
        }

        private Task<ManagedSessionType> ConnectAsync(
            IReverseConnectionSource source,
            string name,
            ReconnectPolicyOptions policy,
            CancellationToken ct)
        {
            return new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(CreateEndpoint())
                .UseReverseConnect(source, new Uri(ServerUri))
                .WithSessionName(name)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(_ => policy)
                .ConnectAsync(ct);
        }

        private static ReconnectPolicyOptions ConstantPolicy(TimeSpan delay)
        {
            return new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = delay,
                MaxDelay = delay,
                MaxRetries = 0,
                JitterFactor = 0.0
            };
        }

        private static ISubscription AddCurrentTimeSubscription(
            ManagedSessionType session,
            SequenceRecordingHandler handler)
        {
            ISubscription subscription = session.AddSubscription(
                handler,
                new Client.Subscriptions.SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(250),
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    Priority = 0
                });
            Assert.That(
                subscription.TryAddMonitoredItem(
                    "ServerCurrentTime",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    opt => opt with { SamplingInterval = TimeSpan.FromMilliseconds(250), QueueSize = 10 },
                    out _),
                Is.True);
            return subscription;
        }

        private static void CloseTransport(ITransportWaitingConnection connection)
        {
            ((IUaSCByteTransport)connection.Handle).Close();
        }

        private static async Task CloseAndDisposeAsync(ManagedSessionType session)
        {
            using var closeCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await session.CloseAsync(closeCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ServiceResultException)
            {
            }
            await session.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<bool> WaitForAsync(Func<bool> predicate, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
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
                    break;
                }
            }
            return predicate();
        }

        private enum SourceBehavior
        {
            Deliver,
            Fail,
            Block
        }

        private sealed record SourceRequest(int Call, string? ServerUri, DateTime At);

        /// <summary>
        /// Delivers connections from a reverse connect manager, fails or blocks
        /// a call as configured, and records every call and cancellation.
        /// </summary>
        private sealed class RecordingSource : IReverseConnectionSource
        {
            public RecordingSource(IReverseConnectionSource inner, Func<int, SourceBehavior> behavior)
            {
                m_inner = inner;
                m_behavior = behavior;
            }

            public int CallCount => Volatile.Read(ref m_callCount);

            public IReadOnlyList<SourceRequest> Requests => [.. m_requests.OrderBy(r => r.Call)];

            public ICollection<int> CancelledCalls => m_cancelled.Keys;

            public ITransportWaitingConnection? LastConnection { get; private set; }

            public DateTime CancelledAt(int call)
            {
                return m_cancelled[call];
            }

            public async ValueTask<ITransportWaitingConnection> WaitForConnectionAsync(
                Uri endpointUrl,
                string? serverUri,
                CancellationToken ct)
            {
                int call = Interlocked.Increment(ref m_callCount);
                m_requests.Add(new SourceRequest(call, serverUri, DateTime.UtcNow));
                try
                {
                    switch (m_behavior(call))
                    {
                        case SourceBehavior.Fail:
                            throw new ServiceResultException(
                                StatusCodes.BadTimeout, "No connection for this attempt.");
                        case SourceBehavior.Block:
                            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                            throw new InvalidOperationException("The blocked wait completed without cancellation.");
                        default:
                            ITransportWaitingConnection connection = await m_inner
                                .WaitForConnectionAsync(endpointUrl, serverUri, ct).ConfigureAwait(false);
                            LastConnection = connection;
                            return connection;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    m_cancelled.TryAdd(call, DateTime.UtcNow);
                    throw;
                }
            }

            private readonly IReverseConnectionSource m_inner;
            private readonly Func<int, SourceBehavior> m_behavior;
            private readonly ConcurrentBag<SourceRequest> m_requests = [];
            private readonly ConcurrentDictionary<int, DateTime> m_cancelled = new();
            private int m_callCount;
        }

        /// <summary>
        /// Records the connection state changes of a managed session.
        /// </summary>
        private sealed class StateRecorder
        {
            public StateRecorder(ManagedSessionType session)
            {
                session.ConnectionStateChanged += (_, e) =>
                {
                    if (e.NewState == ConnectionState.Reconnecting && e.PreviousState != ConnectionState.Reconnecting)
                    {
                        LossObservedAt = DateTime.UtcNow;
                    }
                    if (e.PreviousState == ConnectionState.Reconnecting && e.NewState == ConnectionState.Reconnecting)
                    {
                        Interlocked.Increment(ref m_failedAttempts);
                    }
                    if (e.PreviousState == ConnectionState.Reconnecting && e.NewState == ConnectionState.Connected)
                    {
                        Reconnected = true;
                    }
                    if (e.NewState == ConnectionState.Disconnected)
                    {
                        Disconnected = true;
                    }
                };
            }

            public DateTime LossObservedAt { get; private set; } = DateTime.MaxValue;

            public int FailedAttempts => Volatile.Read(ref m_failedAttempts);

            public bool Reconnected { get; private set; }

            public bool Disconnected { get; private set; }

            private int m_failedAttempts;
        }

        /// <summary>
        /// Records the sequence numbers of data change notifications.
        /// </summary>
        private sealed class SequenceRecordingHandler : ISubscriptionNotificationHandler
        {
            public int Count
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_notifications.Count;
                    }
                }
            }

            public uint MaxSequenceNumber
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_notifications.Count == 0 ? 0 : m_notifications.Max(n => n.SequenceNumber);
                    }
                }
            }

            public uint MaxSequenceNumberBefore(DateTime time)
            {
                lock (m_lock)
                {
                    return m_notifications.Where(n => n.At < time).Max(n => n.SequenceNumber);
                }
            }

            public uint MinSequenceNumberFrom(int index)
            {
                lock (m_lock)
                {
                    return m_notifications.Skip(index).Min(n => n.SequenceNumber);
                }
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                lock (m_lock)
                {
                    m_notifications.Add((sequenceNumber, DateTime.UtcNow));
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private readonly Lock m_lock = new();
            private readonly List<(uint SequenceNumber, DateTime At)> m_notifications = [];
        }

        private Uri m_endpointUrl = null!;
    }
}
