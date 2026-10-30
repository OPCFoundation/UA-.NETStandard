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

// CA2025: the fake takeover factories hand out sessions the coordinator owns and disposes;
//   the tests never dispose them while such a task is pending.
#pragma warning disable CA2025

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;
using Opc.Ua.Redundancy;
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

        [TestCase(false)]
        [TestCase(true)]
        public async Task LeaderPromotionReusesMirroredTokenAndReportsFastActivationAsync(bool authenticated)
        {
            using var store = new InMemorySharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var encryption = new AesCbcHmacRecordProtector(key);
            IRecordProtector protector = authenticated ? encryption : NullRecordProtector.Instance;
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            SetServerNonce(seedSession, [1, 2, 3, 4]);
            await store.SetAsync("client-replica/session", CreateHandoffRecord(protector, seedSession))
                .ConfigureAwait(false);

            ConfiguredEndpoint expectedEndpoint = seedSession.ConfiguredEndpoint;
            ManagedSession standbySession = CreateManagedSessionForTokenReuse(expectedEndpoint, NodeId.Parse("s=auth"));
            ManagedSession leaderSession = CreateManagedSessionForTokenReuse(expectedEndpoint, NodeId.Parse("s=auth"));
            var handoffs = new List<SessionHandoff>();
            SetTakeoverFactory(standbySession, (handoff, _, _) =>
            {
                handoffs.Add(handoff);
                return Task.FromResult(leaderSession);
            });
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                EnableTokenReuse = true,
                CreateSessionAsync = _ => new ValueTask<ManagedSession>(standbySession),
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
                protector,
                m_telemetry);

            await coordinator.StartAsync().ConfigureAwait(false);

            bool fastActivated = await configured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(fastActivated, Is.True);
            Assert.That(handoffs, Has.Count.EqualTo(1), "the leader session is created as a takeover");
            Assert.That(handoffs[0].Session.SessionId, Is.EqualTo(seedSession.SessionId));
            Assert.That(coordinator.CurrentSession, Is.SameAs(leaderSession));
            Assert.That(standbySession.Disposed, Is.True, "the replaced standby session is closed");
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
        public async Task LeaderPromotionKeepsTheStandbySessionWhenTheTakeoverFailsAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            SetServerNonce(seedSession, [1, 2, 3, 4]);
            // A handoff from a since-superseded leader: taking it over fails because its
            // server session no longer exists.
            await store.SetAsync("client-replica/session", CreateHandoffRecord(NullRecordProtector.Instance, seedSession))
                .ConfigureAwait(false);

            ConfiguredEndpoint expectedEndpoint = seedSession.ConfiguredEndpoint;
            ManagedSession standbySession = CreateManagedSessionForTokenReuse(
                expectedEndpoint, NodeId.Parse("s=fresh-auth"));
            int takeovers = 0;
            SetTakeoverFactory(standbySession, (_, _, _) =>
            {
                takeovers++;
                return Task.FromException<ManagedSession>(new ServiceResultException(
                    StatusCodes.BadSessionIdInvalid,
                    "simulated: the prior leader's session no longer exists on the failover server"));
            });
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int created = 0;

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                EnableTokenReuse = true,
                CreateSessionAsync = _ =>
                {
                    created++;
                    return new ValueTask<ManagedSession>(standbySession);
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

            Assert.That(takeovers, Is.EqualTo(1));
            Assert.That(created, Is.EqualTo(1), "the untouched standby session becomes the leader session");
            Assert.That(fastActivated, Is.False);
            Assert.That(coordinator.CurrentSession, Is.SameAs(standbySession));
            Assert.That(standbySession.Disposed, Is.False);
        }

        [Test]
        public async Task LeaderPromotionIgnoresAHandoffRecordItCannotDecodeAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            using var stream = new MemoryStream();
            seedSession.SaveSessionConfiguration(stream);
            // A plain session configuration is not a session handoff record.
            await store.SetAsync("client-replica/session", new ByteString(stream.ToArray())).ConfigureAwait(false);

            ManagedSession standbySession = CreateManagedSessionForTokenReuse(
                seedSession.ConfiguredEndpoint, NodeId.Parse("s=auth"));
            int takeovers = 0;
            SetTakeoverFactory(standbySession, (_, _, _) =>
            {
                takeovers++;
                return Task.FromResult(standbySession);
            });
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                EnableTokenReuse = true,
                CreateSessionAsync = _ => new ValueTask<ManagedSession>(standbySession),
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

            Assert.That(fastActivated, Is.False);
            Assert.That(takeovers, Is.Zero);
            Assert.That(coordinator.CurrentSession, Is.SameAs(standbySession));
        }

        [TestCase(ClientStandbyMode.Warm)]
        [TestCase(ClientStandbyMode.Hot)]
        public async Task DemotedWarmOrHotLeaderReplacesItsLeaderSessionWithAFreshStandbyAsync(
            ClientStandbyMode mode)
        {
            using var store = new InMemorySharedKeyValueStore();
            var endpoint = new ConfiguredEndpoint(null!, new EndpointDescription("opc.tcp://demote:4840"));
            ManagedSession leaderSession = CreateManagedSessionForTokenReuse(endpoint, NodeId.Parse("s=auth"));
            ManagedSession standbySession = CreateManagedSessionForTokenReuse(endpoint, NodeId.Parse("s=auth"));
            int created = 0;
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var demoted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var election = new ControllableLeaderElection();

            var options = new ClientReplicaOptions
            {
                Mode = mode,
                EnableTokenReuse = false,
                CreateSessionAsync = _ =>
                {
                    created++;
                    return new ValueTask<ManagedSession>(created == 1 ? leaderSession : standbySession);
                },
                ConfigureLeaderAsync = (_, fastActivated, _) =>
                {
                    configured.TrySetResult(fastActivated);
                    return default;
                }
            };
            await using var coordinator = new ClientReplicaCoordinator(
                options, election, store, NullRecordProtector.Instance, m_telemetry);
            coordinator.RoleChanged += isLeader =>
            {
                if (!isLeader)
                {
                    demoted.TrySetResult(true);
                }
            };

            await coordinator.StartAsync().ConfigureAwait(false);
            election.SetLeader(true);
            await configured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(coordinator.CurrentSession, Is.SameAs(leaderSession));

            // The leader loses the lease while still alive.
            election.SetLeader(false);
            await demoted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(
                leaderSession.Disposed,
                Is.True,
                "the ex-leader must not keep running the leader session next to the new leader");
            // The standby connect follows the demotion notification.
            Assert.That(() => coordinator.CurrentSession, Is.SameAs(standbySession).After(10000, 20));
            Assert.That(created, Is.EqualTo(2));
        }

        [Test]
        public async Task TokenReusePromotionClosesTheStandbySessionAfterTheTakeoverAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            SetServerNonce(seedSession, [1, 2, 3, 4]);
            await store.SetAsync("client-replica/session", CreateHandoffRecord(NullRecordProtector.Instance, seedSession))
                .ConfigureAwait(false);

            ConfiguredEndpoint expectedEndpoint = seedSession.ConfiguredEndpoint;
            ManagedSession standbySession = CreateManagedSessionForTokenReuse(
                expectedEndpoint,
                NodeId.Parse("s=auth"),
                NodeId.Parse("s=follower-auth"));
            ManagedSession leaderSession = CreateManagedSessionForTokenReuse(expectedEndpoint, NodeId.Parse("s=auth"));
            SetTakeoverFactory(standbySession, (_, _, _) =>
            {
                Assert.That(
                    standbySession.Disposed,
                    Is.False,
                    "the standby session stays usable until the takeover succeeded");
                return Task.FromResult(leaderSession);
            });
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Warm,
                EnableTokenReuse = true,
                CreateSessionAsync = _ => new ValueTask<ManagedSession>(standbySession),
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
            Assert.That(coordinator.CurrentSession, Is.SameAs(leaderSession));
            Assert.That(
                standbySession.Disposed,
                Is.True,
                "the replaced standby session is disposed normally, which closes its own server session");
        }

        [Test]
        public async Task PromotionRetryKeepsTheTakenOverLeaderSessionAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            SetServerNonce(seedSession, [1, 2, 3, 4]);
            await store.SetAsync("client-replica/session", CreateHandoffRecord(NullRecordProtector.Instance, seedSession))
                .ConfigureAwait(false);

            ConfiguredEndpoint endpoint = seedSession.ConfiguredEndpoint;
            var closedOnSharedChannel = new List<(NodeId Token, bool DeleteSubscriptions)>();
            ManagedSession standbySession = CreateManagedSessionForTokenReuse(endpoint, NodeId.Parse("s=auth"));
            ManagedSession leaderSession = CreateManagedSessionForTokenReuse(
                endpoint,
                NodeId.Parse("s=auth"),
                closedOnReactivatedChannel: closedOnSharedChannel);
            int takeovers = 0;
            SetTakeoverFactory(standbySession, (_, _, _) =>
            {
                takeovers++;
                return Task.FromResult(leaderSession);
            });
            int configureCalls = 0;
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Warm,
                EnableTokenReuse = true,
                CreateSessionAsync = _ => new ValueTask<ManagedSession>(standbySession),
                ConfigureLeaderAsync = (_, fastActivated, _) =>
                {
                    if (++configureCalls == 1)
                    {
                        // A transient failure after the server session was taken over.
                        throw new ServiceResultException(StatusCodes.BadCommunicationError);
                    }
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

            Assert.That(configureCalls, Is.EqualTo(2));
            Assert.That(takeovers, Is.EqualTo(1), "the retry does not take the server session over again");
            Assert.That(fastActivated, Is.True, "the retry still runs on the taken-over leader session");
            Assert.That(coordinator.CurrentSession, Is.SameAs(leaderSession));
            Assert.That(leaderSession.Disposed, Is.False);
            Assert.That(
                closedOnSharedChannel,
                Is.Empty,
                "the retry must not close the taken-over leader session");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DemotionIsReportedEvenWhenTheStandbyConnectFailsOrHangsAsync(bool hang)
        {
            using var store = new InMemorySharedKeyValueStore();
            var endpoint = new ConfiguredEndpoint(null!, new EndpointDescription("opc.tcp://demote:4840"));
            ManagedSession leaderSession = CreateManagedSessionForTokenReuse(endpoint, NodeId.Parse("s=auth"));
            int created = 0;
            var releaseStandby = new TaskCompletionSource<ManagedSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var demoted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var election = new ControllableLeaderElection();

            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Warm,
                EnableTokenReuse = false,
                CreateSessionAsync = _ =>
                {
                    created++;
                    if (created == 1)
                    {
                        return new ValueTask<ManagedSession>(leaderSession);
                    }
                    if (hang)
                    {
                        return new ValueTask<ManagedSession>(releaseStandby.Task);
                    }
                    throw new ServiceResultException(StatusCodes.BadTooManySessions);
                },
                ConfigureLeaderAsync = (_, fastActivated, _) =>
                {
                    configured.TrySetResult(fastActivated);
                    return default;
                }
            };
            await using var coordinator = new ClientReplicaCoordinator(
                options, election, store, NullRecordProtector.Instance, m_telemetry);
            coordinator.RoleChanged += isLeader =>
            {
                if (!isLeader)
                {
                    demoted.TrySetResult(true);
                }
            };

            await coordinator.StartAsync().ConfigureAwait(false);
            election.SetLeader(true);
            await configured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            election.SetLeader(false);
            try
            {
                await demoted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(coordinator.CurrentSession, Is.Null);
                Assert.That(leaderSession.Disposed, Is.True);
            }
            finally
            {
                // Unblock the pending standby connect so the coordinator can dispose.
                releaseStandby.TrySetCanceled();
            }
        }

        [TestCase(ClientStandbyMode.Warm)]
        [TestCase(ClientStandbyMode.Hot)]
        public async Task DemotedTokenReuseLeaderAbandonsTheSharedSessionWithoutClosingItAsync(
            ClientStandbyMode mode)
        {
            using var store = new InMemorySharedKeyValueStore();
            using var seedSession = SessionMock.Create();
            seedSession.SetConnected();
            SetServerNonce(seedSession, [1, 2, 3, 4]);
            await store.SetAsync("client-replica/session", CreateHandoffRecord(NullRecordProtector.Instance, seedSession))
                .ConfigureAwait(false);

            ConfiguredEndpoint endpoint = seedSession.ConfiguredEndpoint;
            var closedOnSharedChannel = new List<(NodeId Token, bool DeleteSubscriptions)>();
            ManagedSession firstStandby = CreateManagedSessionForTokenReuse(endpoint, NodeId.Parse("s=auth"));
            ManagedSession leaderSession = CreateManagedSessionForTokenReuse(
                endpoint,
                NodeId.Parse("s=auth"),
                closedOnReactivatedChannel: closedOnSharedChannel);
            SetTakeoverFactory(firstStandby, (_, _, _) => Task.FromResult(leaderSession));
            ManagedSession secondStandby = CreateManagedSessionForTokenReuse(endpoint, NodeId.Parse("s=auth"));
            int created = 0;
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var demoted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var election = new ControllableLeaderElection();

            var options = new ClientReplicaOptions
            {
                Mode = mode,
                EnableTokenReuse = true,
                CreateSessionAsync = _ =>
                {
                    created++;
                    return new ValueTask<ManagedSession>(created == 1 ? firstStandby : secondStandby);
                },
                ConfigureLeaderAsync = (_, fastActivated, _) =>
                {
                    configured.TrySetResult(fastActivated);
                    return default;
                }
            };
            await using var coordinator = new ClientReplicaCoordinator(
                options, election, store, NullRecordProtector.Instance, m_telemetry);
            coordinator.RoleChanged += isLeader =>
            {
                if (!isLeader)
                {
                    demoted.TrySetResult(true);
                }
            };

            await coordinator.StartAsync().ConfigureAwait(false);
            election.SetLeader(true);
            Assert.That(
                await configured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                Is.True,
                "the leader session must have taken the server session over");
            Assert.That(coordinator.CurrentSession, Is.SameAs(leaderSession));

            // The leader loses the lease while it can still reach the server.
            election.SetLeader(false);
            await demoted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(leaderSession.Disposed, Is.True, "the ex-leader must stop using the shared session");
            Assert.That(
                closedOnSharedChannel,
                Is.Empty,
                "the ex-leader must not close the server session the new leader takes over");
            Assert.That(() => coordinator.CurrentSession, Is.SameAs(secondStandby).After(10000, 20));
        }

        private sealed class ControllableLeaderElection : ILeaderElection
        {
            public bool IsLeader { get; private set; }

            public event Action<bool>? LeadershipChanged;

            public void SetLeader(bool isLeader)
            {
                IsLeader = isLeader;
                LeadershipChanged?.Invoke(isLeader);
            }

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(IsLeader);
            }

            public void Start()
            {
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
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
            NodeId expectedAuthenticationToken,
            NodeId ownAuthenticationToken = default,
            List<(NodeId Token, bool DeleteSubscriptions)>? closedTokens = null,
            List<(NodeId Token, bool DeleteSubscriptions)>? closedOnReactivatedChannel = null)
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
            if (closedOnReactivatedChannel != null)
            {
                // A live channel: a dispose would send CloseSession over it.
                managedChannel.SetupGet(c => c.State).Returns(ChannelState.Ready);
            }
            managedChannel
                .Setup(c => c.SendRequestAsync(It.IsAny<CloseSessionRequest>(), It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest request, CancellationToken _) =>
                {
                    var close = (CloseSessionRequest)request;
                    closedOnReactivatedChannel?.Add((close.RequestHeader.AuthenticationToken, close.DeleteSubscriptions));
                    return new ValueTask<IServiceResponse>(new CloseSessionResponse());
                });
            // The standby's own session still runs on its original channel until the
            // mirrored reactivation builds a new one.
            innerSession.Channel
                .Setup(c => c.SendRequestAsync(It.IsAny<CloseSessionRequest>(), It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest request, CancellationToken _) =>
                {
                    var close = (CloseSessionRequest)request;
                    closedTokens?.Add((close.RequestHeader.AuthenticationToken, close.DeleteSubscriptions));
                    return new ValueTask<IServiceResponse>(new CloseSessionResponse());
                });
            var channelManager = new Mock<IClientChannelManager>();
            // Match by URL: ManagedSession.ConfiguredEndpoint reports the inner
            // session's endpoint, which is an equal but different instance.
            channelManager
                .Setup(m => m.GetAsync(
                    It.Is<ConfiguredEndpoint>(e => e.EndpointUrl == endpoint.EndpointUrl),
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
            if (!ownAuthenticationToken.IsNull)
            {
                innerSession.SessionCreated(NodeId.Parse("s=follower-session"), ownAuthenticationToken);
            }

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
                    typeof(ServerRedundancyOptions),
                    typeof(IClientChannelManager),
                    typeof(IClientConnectGate)
                ],
                null)!;

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
                null,
                null
            ]);

            typeof(ManagedSession)
                .GetField("m_session", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(managedSession, innerSession);
            return managedSession;
        }

        private static ByteString CreateHandoffRecord(IRecordProtector protector, Session leader)
        {
            leader.Snapshot(out SessionConfiguration saved);
            var handoff = new SessionHandoff
            {
                Session = saved,
                RevisedSessionTimeout = leader.SessionTimeout
            };
            return SessionHandoffCodec.Protect(
                handoff,
                protector,
                RecordProtectionContext.Create("client-replica-session", "client-replica/session"),
                leader.MessageContext);
        }

        private static void SetTakeoverFactory(
            ManagedSession standby,
            Func<SessionHandoff, SessionTakeoverOptions, CancellationToken, Task<ManagedSession>> factory)
        {
            typeof(ManagedSession)
                .GetField("m_takeoverFactory", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(standby, factory);
        }

        private static void SetServerNonce(Session session, byte[] value)
        {
            typeof(Session)
                .GetField("m_serverNonce", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(session, ByteString.From(value));
        }
    }
}
