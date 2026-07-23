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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Unit tests for the transparent redundant client session facade.
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class RedundantClientSessionTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public async Task AsyncCallBlocksUntilLeadershipHasLiveSessionAsync()
        {
            ISession? current = null;
            using var store = new InMemorySharedKeyValueStore();
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default },
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry
            );
            await using var facade = new RedundantClientSession(coordinator, () => current);
            var response = new ReadResponse();
            var session = new Mock<ISession>(MockBehavior.Strict);
            session
                .Setup(s =>
                    s.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .Returns(new ValueTask<ReadResponse>(response));

            Task<ReadResponse> readTask = facade
                .ReadAsync(null, 0, TimestampsToReturn.Neither, [], CancellationToken.None)
                .AsTask();
            Assert.That(readTask.IsCompleted, Is.False);

            current = session.Object;
            facade.RefreshActiveSessionForTesting();

            Assert.That(await readTask.ConfigureAwait(false), Is.SameAs(response));
            session.VerifyAll();
        }

        [Test]
        public async Task ConcurrentAsyncCallsUseNewSessionAfterLiveSwapAsync()
        {
            ISession? current = null;
            using var store = new InMemorySharedKeyValueStore();
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default },
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry);
            var first = new Mock<ISession>();
            var second = new Mock<ISession>();
            var response = new ReadResponse();
            second
                .Setup(session => session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(response));
            current = first.Object;
            await using var facade = new RedundantClientSession(coordinator, () => current);

            current = second.Object;
            facade.RefreshActiveSessionForTesting();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<ReadResponse>[] calls = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(
                    async () => await facade
                        .ReadAsync(null, 0, TimestampsToReturn.Neither, [], cts.Token)
                        .ConfigureAwait(false)))
                .ToArray();
            ReadResponse[] results = await Task.WhenAll(calls).ConfigureAwait(false);

            Assert.That(results, Is.All.SameAs(response));
            second.Verify(
                session => session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(calls.Length));
            first.Verify(
                session => session.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task FailedRememberedValueApplicationKeepsPreviousSessionAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default },
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry);
            var first = new Mock<ISession>();
            var second = new Mock<ISession>();
            var response = new ReadResponse();
            first
                .Setup(session => session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(response));
            second
                .SetupSet(session => session.DeleteSubscriptionsOnClose = true)
                .Throws(new InvalidOperationException("remembered value failed"));
            ISession? current = first.Object;
            await using var facade = new RedundantClientSession(coordinator, () => current);
            facade.DeleteSubscriptionsOnClose = true;

            current = second.Object;
            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                facade.RefreshActiveSessionForTesting);
            ReadResponse result = await facade
                .ReadAsync(null, 0, TimestampsToReturn.Neither, [], CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(exception!.Message, Is.EqualTo("remembered value failed"));
            Assert.That(facade.Current, Is.SameAs(first.Object));
            Assert.That(result, Is.SameAs(response));
        }

        [Test]
        public async Task EarlierConcurrentRefreshCannotReplaceLaterAppliedSessionAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default },
                new StaticLeaderElection(true),
                store,
                NullRecordProtector.Instance,
                m_telemetry);
            var first = new Mock<ISession>();
            var second = new Mock<ISession>();
            ISession? current = first.Object;
            using var staleObserved = new ManualResetEventSlim();
            using var releaseStale = new ManualResetEventSlim();
            int blockNextRefresh = 0;
            ISession? AccessCurrent()
            {
                ISession? observed = current;
                if (Interlocked.Exchange(ref blockNextRefresh, 0) == 1)
                {
                    staleObserved.Set();
                    if (!releaseStale.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The stale refresh was not released.");
                    }
                }
                return observed;
            }

            await using var facade = new RedundantClientSession(coordinator, AccessCurrent);
            Volatile.Write(ref blockNextRefresh, 1);
            Task staleRefresh = Task.Run(facade.RefreshActiveSessionForTesting);
            Assert.That(staleObserved.Wait(TimeSpan.FromSeconds(10)), Is.True);

            current = second.Object;
            facade.RefreshActiveSessionForTesting();
            releaseStale.Set();
            await staleRefresh.ConfigureAwait(false);

            Assert.That(facade.Current, Is.SameAs(second.Object));
        }

        [Test]
        public async Task SyncMemberThrowsBadInvalidStateBeforeLeadershipAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var coordinator = new ClientReplicaCoordinator(
                new ClientReplicaOptions { CreateSessionAsync = _ => default },
                new StaticLeaderElection(false),
                store,
                NullRecordProtector.Instance,
                m_telemetry
            );
            await using var facade = new RedundantClientSession(coordinator, () => null);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => _ = facade.SessionName)!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task AddRedundantClientSessionResolvesIsessionAndDisposesCoordinatorAsync()
        {
            var election = new DisposableLeaderElection(false);
            var services = new ServiceCollection();
            services.AddSingleton(m_telemetry);
            services.AddSingleton<ISharedKeyValueStore>(_ => new InMemorySharedKeyValueStore());
            services.AddSingleton<ILeaderElection>(election);
            services.AddRedundantClientSession(options =>
            {
                options.NodeId = "replica-a";
                options.CreateSessionAsync = _ => default;
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISession session = provider.GetRequiredService<ISession>();

            Assert.That(session, Is.InstanceOf<RedundantClientSession>());
            await provider.DisposeAsync().ConfigureAwait(false);
            Assert.That(election.Disposed, Is.True);
        }

        [Test]
        public void AddRedundantClientSessionSurfacesFailClosedProtectorRequirement()
        {
            var services = new ServiceCollection();
            services.AddSingleton(m_telemetry);
            services.AddSingleton<ISharedKeyValueStore>(new FakeNetworkedStore());
            services.AddSingleton<ILeaderElection>(new StaticLeaderElection(false));
            services.AddRedundantClientSession(options => options.CreateSessionAsync = _ => default);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(() => provider.GetRequiredService<RedundantClientSession>(), Throws.InvalidOperationException);
        }

        private sealed class DisposableLeaderElection : ILeaderElection
        {
            public DisposableLeaderElection(bool isLeader)
            {
                IsLeader = isLeader;
            }

            public bool IsLeader { get; }

            public bool Disposed { get; private set; }

            public event Action<bool>? LeadershipChanged;

            public void Start()
            {
                LeadershipChanged?.Invoke(IsLeader);
            }

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new(IsLeader);
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
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
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default
            )
            {
                return new(true);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return new(true);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default
            )
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default
            )
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }
        }
    }
}
