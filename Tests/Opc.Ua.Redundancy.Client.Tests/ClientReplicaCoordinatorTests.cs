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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Redundancy;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Unit tests for the client replica coordinator and builder seams that do not
    /// require a live server. Full lifecycle (token reuse, failover, subscription
    /// transfer) is exercised by the integration tests.
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class ClientReplicaCoordinatorTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void FailClosedRejectsNetworkedStoreWithoutProtector()
        {
            var options = new ClientReplicaOptions { CreateSessionAsync = _ => default };
            var election = new StaticLeaderElection(false);
            var store = new FakeNetworkedStore();
            Assert.That(
                () => new ClientReplicaCoordinator(
                    options, election, store, NullRecordProtector.Instance, m_telemetry),
                Throws.InvalidOperationException);
        }

        [Test]
        public void InMemoryStoreWithoutProtectorIsAllowed()
        {
            var options = new ClientReplicaOptions { CreateSessionAsync = _ => default };
            var election = new StaticLeaderElection(false);
            using var store = new InMemorySharedKeyValueStore();
            Assert.That(
                () => new ClientReplicaCoordinator(
                    options, election, store, NullRecordProtector.Instance, m_telemetry),
                Throws.Nothing);
        }

        [Test]
        public void MissingSessionFactoryThrows()
        {
            var election = new StaticLeaderElection(false);
            using var store = new InMemorySharedKeyValueStore();
            Assert.That(
                () => new ClientReplicaCoordinator(
                    new ClientReplicaOptions(), election, store, NullRecordProtector.Instance, m_telemetry),
                Throws.ArgumentException);
        }

        [Test]
        public async Task ColdStandbyDoesNotConnectBeforeLeadershipAsync()
        {
            int created = 0;
            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                CreateSessionAsync = _ =>
                {
                    created++;
                    return default;
                }
            };
            var election = new StaticLeaderElection(false);
            using var store = new InMemorySharedKeyValueStore();
            await using var coordinator = new ClientReplicaCoordinator(
                options, election, store, NullRecordProtector.Instance, m_telemetry);
            await coordinator.StartAsync().ConfigureAwait(false);
            Assert.That(created, Is.Zero);
            Assert.That(coordinator.CurrentSession, Is.Null);
            Assert.That(coordinator.IsLeader, Is.False);
        }

        [Test]
        public async Task WarmStandbyConnectsBeforeLeadershipAsync()
        {
            int created = 0;
            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Warm,
                CreateSessionAsync = _ =>
                {
                    created++;
                    return default;
                }
            };
            var election = new StaticLeaderElection(false);
            using var store = new InMemorySharedKeyValueStore();
            await using var coordinator = new ClientReplicaCoordinator(
                options, election, store, NullRecordProtector.Instance, m_telemetry);

            await coordinator.StartAsync().ConfigureAwait(false);

            // Unlike a cold standby, a warm/hot standby pre-connects its session
            // on start so it can take over quickly, even before it is the leader.
            Assert.That(created, Is.EqualTo(1));
            Assert.That(coordinator.IsLeader, Is.False);
        }

        [Test]
        public void BuilderRequiresRedundancySeams()
        {
            ClientReplicaSetBuilder builder = new ClientReplicaSetBuilder(m_telemetry)
                .WithNodeId("a")
                .WithStandbyMode(ClientStandbyMode.Hot)
                .UseSession(_ => default);
            Assert.That(() => builder.Build(), Throws.InvalidOperationException);
        }

        [Test]
        public async Task LeaderPromotionReusesMirroredTokenAndReportsFastActivationAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            SetServerNonce(seedSession, [1, 2, 3, 4]);
            using var stream = new MemoryStream();
            seedSession.SaveSessionConfiguration(stream);
            await store.SetAsync("client-replica/session", new ByteString(stream.ToArray())).ConfigureAwait(false);

            ConfiguredEndpoint expectedEndpoint = seedSession.ConfiguredEndpoint;
            NodeId expectedAuthenticationToken = NodeId.Parse("s=auth");
            ManagedSession managedSession = CreateManagedSessionForTokenReuse(expectedEndpoint, expectedAuthenticationToken);
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                EnableTokenReuse = true,
                CreateSessionAsync = _ => new ValueTask<ManagedSession>(managedSession),
                ConfigureLeaderAsync = (_, fastActivated, _) =>
                {
                    configured.TrySetResult(fastActivated);
                    return default;
                }
            };
            await using var coordinator = new ClientReplicaCoordinator(
                options,
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry);

            await coordinator.StartAsync().ConfigureAwait(false);

            bool fastActivated = await configured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(fastActivated, Is.True);
        }

        [Test]
        public async Task LeaderPromotionRetriesSessionCreationUntilItSucceedsAsync()
        {
            int attempts = 0;
            using var store = new InMemorySharedKeyValueStore();
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ManagedSession session = CreateManagedSessionForTokenReuse(
                new ConfiguredEndpoint(null!, new EndpointDescription("opc.tcp://retry:4840")),
                NodeId.Parse("s=auth"));

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                EnableTokenReuse = false,
                CreateSessionAsync = _ =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadCommunicationError);
                    }

                    return new ValueTask<ManagedSession>(session);
                },
                ConfigureLeaderAsync = (_, fastActivated, _) =>
                {
                    configured.TrySetResult(fastActivated);
                    return default;
                }
            };
            await using var coordinator = new ClientReplicaCoordinator(
                options,
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry);

            await coordinator.StartAsync().ConfigureAwait(false);

            bool fastActivated = await configured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(fastActivated, Is.False);
            Assert.That(coordinator.CurrentSession, Is.SameAs(session));
        }

        [Test]
        public async Task LeaderPromotionRecoversWithFreshSessionWhenTokenReuseReactivationFailsAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            SetServerNonce(seedSession, [1, 2, 3, 4]);
            using var stream = new MemoryStream();
            seedSession.SaveSessionConfiguration(stream);
            // A stored config from a since-superseded leader: reactivating it fails
            // (its server-side session no longer exists), exercising the
            // failure-recovery path in EnsureLeaderSessionAsync.
            await store.SetAsync("client-replica/session", new ByteString(stream.ToArray())).ConfigureAwait(false);

            ConfiguredEndpoint expectedEndpoint = seedSession.ConfiguredEndpoint;
            ManagedSession brokenSession = CreateManagedSessionThatFailsReactivation(expectedEndpoint);
            ManagedSession freshSession = CreateManagedSessionForTokenReuse(
                expectedEndpoint, NodeId.Parse("s=fresh-auth"));
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int created = 0;

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                EnableTokenReuse = true,
                CreateSessionAsync = _ =>
                {
                    created++;
                    return new ValueTask<ManagedSession>(created == 1 ? brokenSession : freshSession);
                },
                ConfigureLeaderAsync = (_, fastActivated, _) =>
                {
                    configured.TrySetResult(fastActivated);
                    return default;
                }
            };
            await using var coordinator = new ClientReplicaCoordinator(
                options,
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry);

            await coordinator.StartAsync().ConfigureAwait(false);

            bool fastActivated = await configured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(created, Is.EqualTo(2), "a failed reactivation must fall back to creating a fresh session");
            Assert.That(fastActivated, Is.False);
            Assert.That(
                coordinator.CurrentSession,
                Is.SameAs(freshSession),
                "the coordinator must not be left holding the broken, half-reactivated session");
        }

        private sealed class FakeNetworkedStore : ISharedKeyValueStore
        {
            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
            {
                return new((false, default));
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key, ByteString expected, ByteString value, CancellationToken ct = default)
            {
                return new(true);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return new(true);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }
        }

        private static ManagedSession CreateManagedSessionForTokenReuse(
            ConfiguredEndpoint endpoint,
            NodeId expectedAuthenticationToken)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = new(telemetry)
            {
                ApplicationName = "ClientReplicaCoordinatorTests",
                ApplicationUri = "urn:localhost:ClientReplicaCoordinatorTests",
                ProductUri = "urn:localhost:ClientReplicaCoordinatorTests",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                }
            };
            var innerSession = SessionMock.Create(new EndpointDescription
            {
                SecurityMode = endpoint.Description.SecurityMode,
                SecurityPolicyUri = endpoint.Description.SecurityPolicyUri,
                EndpointUrl = endpoint.Description.EndpointUrl,
                UserIdentityTokens = [new UserTokenPolicy()]
            });
            IServiceMessageContext messageContext = configuration.CreateMessageContext();
            var managedChannel = new Mock<IManagedTransportChannel>();
            managedChannel.SetupGet(c => c.MessageContext).Returns(messageContext);
            managedChannel.SetupGet(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
            managedChannel.SetupGet(c => c.EndpointDescription).Returns(endpoint.Description);
            managedChannel.SetupGet(c => c.EndpointConfiguration).Returns(new EndpointConfiguration());
            managedChannel.SetupGet(c => c.ChannelThumbprint).Returns([]);
            managedChannel.SetupGet(c => c.ClientChannelCertificate).Returns([]);
            managedChannel.SetupGet(c => c.ServerChannelCertificate).Returns([]);
            managedChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == expectedAuthenticationToken),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ActivateSessionResponse
                {
                    ServerNonce = [5, 6, 7, 8],
                    Results = [],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);
            var channelManager = new Mock<IClientChannelManager>();
            channelManager
                .Setup(m => m.GetAsync(
                    endpoint,
                    It.IsAny<Func<IManagedTransportChannel, IReconnectParticipant>>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IManagedTransportChannel>(managedChannel.Object))
                .Verifiable(Times.Once);
            typeof(Session)
                .GetMethod(
                    "BindManagedChannel",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(innerSession, [channelManager.Object, managedChannel.Object]);

            ManagedSession managedSession = CreateManagedSessionWithInner(
                configuration,
                endpoint,
                innerSession,
                telemetry);
            managedSession.EnableTokenReuseFailover = true;
            return managedSession;
        }

        private static ManagedSession CreateManagedSessionThatFailsReactivation(ConfiguredEndpoint endpoint)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = new(telemetry)
            {
                ApplicationName = "ClientReplicaCoordinatorTests",
                ApplicationUri = "urn:localhost:ClientReplicaCoordinatorTests",
                ProductUri = "urn:localhost:ClientReplicaCoordinatorTests",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                }
            };
            var innerSession = SessionMock.Create(new EndpointDescription
            {
                SecurityMode = endpoint.Description.SecurityMode,
                SecurityPolicyUri = endpoint.Description.SecurityPolicyUri,
                EndpointUrl = endpoint.Description.EndpointUrl,
                UserIdentityTokens = [new UserTokenPolicy()]
            });
            IServiceMessageContext messageContext = configuration.CreateMessageContext();
            var managedChannel = new Mock<IManagedTransportChannel>();
            managedChannel.SetupGet(c => c.MessageContext).Returns(messageContext);
            managedChannel.SetupGet(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
            managedChannel.SetupGet(c => c.EndpointDescription).Returns(endpoint.Description);
            managedChannel.SetupGet(c => c.EndpointConfiguration).Returns(new EndpointConfiguration());
            managedChannel.SetupGet(c => c.ChannelThumbprint).Returns([]);
            managedChannel.SetupGet(c => c.ClientChannelCertificate).Returns([]);
            managedChannel.SetupGet(c => c.ServerChannelCertificate).Returns([]);
            // Simulate the prior leader's session no longer existing server-side:
            // the failover server rejects the reactivation's ActivateSession call.
            managedChannel
                .Setup(c => c.SendRequestAsync(It.IsAny<ActivateSessionRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(
                    StatusCodes.BadSessionIdInvalid,
                    "simulated: the prior leader's session no longer exists on the failover server"));
            var channelManager = new Mock<IClientChannelManager>();
            channelManager
                .Setup(m => m.GetAsync(
                    endpoint,
                    It.IsAny<Func<IManagedTransportChannel, IReconnectParticipant>>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IManagedTransportChannel>(managedChannel.Object));
            typeof(Session)
                .GetMethod(
                    "BindManagedChannel",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(innerSession, [channelManager.Object, managedChannel.Object]);

            ManagedSession managedSession = CreateManagedSessionWithInner(
                configuration,
                endpoint,
                innerSession,
                telemetry);
            managedSession.EnableTokenReuseFailover = true;
            return managedSession;
        }

        private static ManagedSession CreateManagedSessionWithInner(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Session innerSession,
            ITelemetryContext telemetry)
        {
            ILogger<ManagedSession> logger = telemetry.CreateLogger<ManagedSession>();
            var sessionFactory = new Mock<ISessionFactory>();
            sessionFactory.SetupGet(f => f.Telemetry).Returns(telemetry);
            var reconnectPolicy = new ReconnectPolicy();

            ConstructorInfo ctor = typeof(ManagedSession).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [
                    typeof(ApplicationConfiguration),
                    typeof(ConfiguredEndpoint),
                    typeof(ISessionFactory),
                    typeof(IReconnectPolicy),
                    typeof(IServerRedundancyHandler),
                    typeof(ILogger),
                    typeof(IUserIdentity),
                    typeof(IClientIdentityProvider),
                    typeof(TimeProvider),
                    typeof(ArrayOf<string>),
                    typeof(string),
                    typeof(uint),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(NetworkRedundancyOptions),
                    typeof(IClientChannelManager),
                    typeof(IClientConnectGate)
                ],
                null);

            Assert.That(ctor, Is.Not.Null);

            var managedSession = (ManagedSession)ctor.Invoke(
            [
                configuration,
                endpoint,
                sessionFactory.Object,
                reconnectPolicy,
                null,
                logger,
                null,
                null,
                null,
                default(ArrayOf<string>),
                "ReplicaManagedSession",
                60000u,
                false,
                false,
                false,
                false,
                null,
                null,
                null
            ]);

            typeof(ManagedSession)
                .GetField("m_session", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(managedSession, innerSession);
            return managedSession;
        }

        private static void SetServerNonce(Session session, byte[] value)
        {
            typeof(Session)
                .GetField("m_serverNonce", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(session, ByteString.From(value));
        }
    }
}
