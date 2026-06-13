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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;
using IServerRedundancyHandler = Opc.Ua.Client.IServerRedundancyHandler;
using ISession = Opc.Ua.Client.ISession;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;
using ServerRedundancyInfo = Opc.Ua.Client.ServerRedundancyInfo;

// CA2000: integration-test disposables are released by helper cleanup paths.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Live-server tests for managed-channel session lifecycle edges.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("ChannelManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ChannelManagerSessionLifecycleIntegrationTests
        : ChannelManagerIntegrationTestBase
    {
        [Test]
        [Order(100)]
        [CancelAfter(180_000)]
        public async Task BadSessionIdInvalidDuringReactivationRecreatesOnlyThatSession(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 2,
                    ParticipantTimeout = TimeSpan.FromSeconds(5)
                });
            ManagedSessionType? recreatedSession = null;
            ManagedSessionType? pinnedSession = null;

            try
            {
                ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                    .ConfigureAwait(false);
                recreatedSession = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(BadSessionIdInvalidDuringReactivationRecreatesOnlyThatSession) + "Recreate",
                    ct).ConfigureAwait(false);
                pinnedSession = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(BadSessionIdInvalidDuringReactivationRecreatesOnlyThatSession) + "Pinned",
                    ct).ConfigureAwait(false);

                IManagedTransportChannel sharedChannel = GetManagedChannel(recreatedSession);
                Assert.That(GetManagedChannel(pinnedSession).Key, Is.EqualTo(sharedChannel.Key));
                NodeId oldRecreatedSessionId = recreatedSession.InnerSession.SessionId;
                NodeId oldPinnedSessionId = pinnedSession.InnerSession.SessionId;

                await ReferenceServer.CurrentInstance.SessionManager
                    .CloseSessionAsync(oldRecreatedSessionId, ct)
                    .ConfigureAwait(false);

                await manager.ReconnectAsync(sharedChannel, ct).ConfigureAwait(false);

                Assert.That(
                    await WaitForAsync(
                            () => !Equals(recreatedSession.InnerSession.SessionId, oldRecreatedSessionId),
                            DefaultWait,
                            ct)
                        .ConfigureAwait(false),
                    Is.True,
                    "The participant recreate callback should create a new server-side session.");
                Assert.That(pinnedSession.InnerSession.SessionId, Is.EqualTo(oldPinnedSessionId));

                await AssertReadServerStatusAsync(recreatedSession, ct).ConfigureAwait(false);
                await AssertReadServerStatusAsync(pinnedSession, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(recreatedSession).ConfigureAwait(false);
                await CloseAndDisposeAsync(pinnedSession).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(150)]
        [CancelAfter(180_000)]
        public async Task ParticipantTimeoutBoundsSlowReconnectParticipantAsync(CancellationToken ct)
        {
            TimeSpan participantTimeout = TimeSpan.FromMilliseconds(500);
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 2,
                    ParticipantTimeout = participantTimeout
                });
            ManagedSessionType? session = null;
            IManagedTransportChannel? slowLease = null;

            try
            {
                ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                    .ConfigureAwait(false);
                session = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ParticipantTimeoutBoundsSlowReconnectParticipantAsync),
                    ct).ConfigureAwait(false);
                IManagedTransportChannel sessionLease = GetManagedChannel(session);
                var slowParticipant = new SlowReconnectParticipant(
                    endpoint,
                    TimeSpan.FromSeconds(1));
                slowLease = await manager.GetAsync(slowParticipant, ct).ConfigureAwait(false);
                Assert.That(slowLease.Key, Is.EqualTo(sessionLease.Key));

                Stopwatch sw = Stopwatch.StartNew();
                await manager.ReconnectAsync(sessionLease, ct).ConfigureAwait(false);
                sw.Stop();

                ManagedChannelDiagnostic diagnostic = GetDiagnostic(manager, sessionLease.Key);
                Assert.Multiple(() =>
                {
                    Assert.That(sw.Elapsed, Is.GreaterThanOrEqualTo(participantTimeout));
                    Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
                    Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(slowParticipant.NotificationCount, Is.GreaterThanOrEqualTo(2));
                });
                await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
            }
            finally
            {
                slowLease?.Dispose();
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(180_000)]
        public async Task FailoverSwapsOnlyOneLeaseWhenChannelsAreSharedAsync(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager();
            ServerFixture<ReferenceServer>? secondaryFixture = null;
            ManagedSessionType? failoverSession = null;
            ManagedSessionType? pinnedSession = null;

            try
            {
                ConfiguredEndpoint endpointA = await GetEndpointAsync(SecurityPolicies.None)
                    .ConfigureAwait(false);
                (secondaryFixture, Uri secondaryUrl) = await StartSecondaryServerAsync()
                    .ConfigureAwait(false);
                ConfiguredEndpoint endpointB = await GetEndpointAsync(
                    SecurityPolicies.None,
                    secondaryUrl).ConfigureAwait(false);

                var redundancyHandler = new FakeRedundancyHandler(endpointB);
                failoverSession = await new ManagedSessionBuilder(
                        ClientFixture.Config,
                        Telemetry)
                    .UseEndpoint(endpointA)
                    .WithChannelManager(manager)
                    .WithSessionName(nameof(FailoverSwapsOnlyOneLeaseWhenChannelsAreSharedAsync) + "Failover")
                    .WithServerRedundancy(redundancyHandler)
                    .WithReconnectPolicy(p => p with
                    {
                        Strategy = BackoffStrategy.Constant,
                        InitialDelay = TimeSpan.FromMilliseconds(50),
                        MaxRetries = 1,
                        JitterFactor = 0.0
                    })
                    .ConnectAsync(ct).ConfigureAwait(false);
                pinnedSession = await ConnectManagedSessionAsync(
                    endpointA,
                    manager,
                    nameof(FailoverSwapsOnlyOneLeaseWhenChannelsAreSharedAsync) + "Pinned",
                    ct).ConfigureAwait(false);

                ManagedChannelKey keyA = GetManagedChannel(failoverSession).Key;
                Assert.That(GetManagedChannel(pinnedSession).Key, Is.EqualTo(keyA));
                Assert.That(GetDiagnostic(manager, keyA).Refcount, Is.EqualTo(2));

                IManagedTransportChannel oldLease = GetManagedChannel(failoverSession);
                var connectedAfterFailover = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                failoverSession.ConnectionStateChanged += (_, e) =>
                {
                    if (e.PreviousState == ConnectionState.Failover &&
                        e.NewState == ConnectionState.Connected)
                    {
                        connectedAfterFailover.TrySetResult(true);
                    }
                };

                failoverSession.StateMachine.ReconnectWithBudgetAsync = (_, _) =>
                    Task.FromResult(new ServiceResult(StatusCodes.BadNotConnected));
                failoverSession.StateMachine.TriggerReconnect();

                Assert.That(
                    await connectedAfterFailover.Task
                        .WaitAsync(DefaultWait, ct)
                        .ConfigureAwait(false),
                    Is.True,
                    "ManagedSession should complete failover to endpoint B.");

                IManagedTransportChannel newLease = GetManagedChannel(failoverSession);
                Assert.That(newLease, Is.Not.SameAs(oldLease));
                Assert.That(newLease.Key, Is.Not.EqualTo(keyA));
                Assert.That(newLease.Key, Is.EqualTo(ManagedChannelKey.FromEndpoint(endpointB)));

                ManagedChannelDiagnostic diagnosticA = GetDiagnostic(manager, keyA);
                ManagedChannelDiagnostic diagnosticB = GetDiagnostic(manager, newLease.Key);
                Assert.That(diagnosticA.Refcount, Is.EqualTo(1));
                Assert.That(diagnosticB.Refcount, Is.EqualTo(1));
                Assert.That(diagnosticA.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(diagnosticB.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(redundancyHandler.SelectCount, Is.GreaterThanOrEqualTo(1));

                await AssertReadServerStatusAsync(failoverSession, ct).ConfigureAwait(false);
                await AssertReadServerStatusAsync(pinnedSession, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(failoverSession).ConfigureAwait(false);
                await CloseAndDisposeAsync(pinnedSession).ConfigureAwait(false);
                if (secondaryFixture != null)
                {
                    await secondaryFixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task<(ServerFixture<ReferenceServer> Fixture, Uri Url)> StartSecondaryServerAsync()
        {
            var fixture = new ServerFixture<ReferenceServer>(
                telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                OperationLimits = true
            };

            await fixture.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ApplicationConfiguration config = fixture.Config
                ?? throw new InvalidOperationException("Secondary server configuration was not loaded.");
            TransportQuotas transportQuotas = config.TransportQuotas
                ?? throw new InvalidOperationException("Secondary server transport quotas were not loaded.");
            ServerConfiguration serverConfiguration = config.ServerConfiguration
                ?? throw new InvalidOperationException("Secondary server configuration section was not loaded.");
            transportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            transportQuotas.MaxByteStringLength = TransportQuotaMaxStringLength;
            transportQuotas.MaxStringLength = TransportQuotaMaxStringLength;
            transportQuotas.SecurityTokenLifetime = SecurityTokenLifetime;
            serverConfiguration.MaxChannelCount = MaxChannelCount;
            serverConfiguration.MaxSubscriptionCount = 1000;
            serverConfiguration.MaxQueuedRequestCount = 100000;

            ReferenceServer secondary = await fixture.StartAsync().ConfigureAwait(false);
            secondary.TokenValidator = TokenValidator;

            return (fixture, new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"));
        }

        private sealed class SlowReconnectParticipant : IReconnectParticipant
        {
            public SlowReconnectParticipant(ConfiguredEndpoint endpoint, TimeSpan firstDelay)
            {
                Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
                Id = nameof(SlowReconnectParticipant) + "-" + Guid.NewGuid().ToString("N");
                m_firstDelay = firstDelay;
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public int NotificationCount => Volatile.Read(ref m_notificationCount);

            public async ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                if (channel == null)
                {
                    throw new ArgumentNullException(nameof(channel));
                }

                Interlocked.Increment(ref m_notificationCount);
                if (reconnectAttempt == 0)
                {
                    await Task.Delay(m_firstDelay, ct).ConfigureAwait(false);
                }

                return ParticipantReconnectResult.Reactivated;
            }

            private readonly TimeSpan m_firstDelay;
            private int m_notificationCount;
        }

        private sealed class FakeRedundancyHandler : IServerRedundancyHandler
        {
            public FakeRedundancyHandler(ConfiguredEndpoint target)
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
                                ServerUri = "urn:channel-manager:secondary",
                                ServerState = ServerState.Running,
                                ServiceLevel = 250
                            }
                        ]
                    });
            }

            public ConfiguredEndpoint SelectFailoverTarget(
                ServerRedundancyInfo redundancyInfo,
                ConfiguredEndpoint currentEndpoint)
            {
                SelectCount++;
                return m_target;
            }

            private readonly ConfiguredEndpoint m_target;
        }
    }
}
