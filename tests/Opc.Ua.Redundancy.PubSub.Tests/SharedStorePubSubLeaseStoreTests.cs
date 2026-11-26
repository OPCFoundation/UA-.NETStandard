// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Redundancy.Tests
{
    /// <summary>
    /// Unit tests for the CAS-backed PubSub lease store.
    /// </summary>
    [TestFixture]
    public class SharedStorePubSubLeaseStoreTests
    {
        [Test]
        public async Task TryAcquireAsyncSucceedsWhenFreeAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(store, time);

            PubSubLease? lease = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);

            Assert.That(lease, Is.Not.Null);
            Assert.That(lease!.Value.LeaseKey, Is.EqualTo(LeaseKey));
            Assert.That(lease.Value.OwnerId, Is.EqualTo(OwnerA));
            Assert.That(lease.Value.FencingToken, Is.EqualTo(1));
            Assert.That(lease.Value.ExpiresAt, Is.EqualTo(time.GetUtcNow() + LeaseDuration));
        }

        [Test]
        public async Task TryAcquireAsyncRejectsSecondOwnerWhileHeldAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(store, time);

            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            PubSubLease? second = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Null);
        }

        [Test]
        public async Task TryAcquireAsyncRenewsSameOwnerWithStableTokenAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(5));

            PubSubLease? renewed = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);

            Assert.That(first, Is.Not.Null);
            Assert.That(renewed, Is.Not.Null);
            Assert.That(renewed!.Value.FencingToken, Is.EqualTo(first!.Value.FencingToken));
            Assert.That(renewed.Value.ExpiresAt, Is.EqualTo(time.GetUtcNow() + LeaseDuration));
        }

        [Test]
        public async Task TryAcquireAsyncAfterExpiryLetsNewOwnerAcquireWithIncrementedTokenAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            time.Advance(LeaseDuration + TimeSpan.FromSeconds(1));

            PubSubLease? takeover = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false);

            Assert.That(first, Is.Not.Null);
            Assert.That(takeover, Is.Not.Null);
            Assert.That(takeover!.Value.OwnerId, Is.EqualTo(OwnerB));
            Assert.That(takeover.Value.FencingToken, Is.EqualTo(first!.Value.FencingToken + 1));
        }

        [Test]
        public async Task TryRenewAsyncFailsAfterTakeoverAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            time.Advance(LeaseDuration + TimeSpan.FromSeconds(1));
            PubSubLease? takeover = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false);

            PubSubLease? renewed = await leaseStore.TryRenewAsync(first!.Value, LeaseDuration).ConfigureAwait(false);

            Assert.That(takeover, Is.Not.Null);
            Assert.That(renewed, Is.Null);
        }

        [Test]
        public async Task TryRenewAsyncExtendsLeaseForCurrentOwnerAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(5));

            PubSubLease? renewed = await leaseStore.TryRenewAsync(first!.Value, LeaseDuration).ConfigureAwait(false);

            Assert.That(renewed, Is.Not.Null);
            Assert.That(renewed!.Value.OwnerId, Is.EqualTo(OwnerA));
            Assert.That(renewed.Value.FencingToken, Is.EqualTo(first!.Value.FencingToken));
            Assert.That(renewed.Value.ExpiresAt, Is.EqualTo(time.GetUtcNow() + LeaseDuration));
        }

        [Test]
        public async Task ReleaseAsyncLetsAnotherOwnerAcquireImmediatelyAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);

            await leaseStore.ReleaseAsync(first!.Value).ConfigureAwait(false);
            PubSubLease? next = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false);

            Assert.That(next, Is.Not.Null);
            Assert.That(next!.Value.OwnerId, Is.EqualTo(OwnerB));
            Assert.That(next.Value.FencingToken, Is.EqualTo(first.Value.FencingToken + 1));
        }

        [Test]
        public async Task TryAcquireAsyncRetriesAfterContendedCompareAndSwapAsync()
        {
            var time = new FakeTimeProvider();
            using var payload = new SimulatedSharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            using var store = new CoordinatedReadSharedKeyValueStore(
                authority,
                PubSubRedundancyStoreKeys.LeasePrefix + LeaseKey);
            var protectedStore = new FreshnessProtectedKeyValueStore(
                payload, store, protector, Guid.NewGuid(), Guid.NewGuid());
            await protectedStore.EnrollAsync().ConfigureAwait(false);
            using var first = new SharedStorePubSubLeaseStore(protectedStore, time);
            using var second = new SharedStorePubSubLeaseStore(protectedStore, time);

            Task<PubSubLease?> firstAcquire = first.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).AsTask();
            Task<PubSubLease?> secondAcquire = second.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).AsTask();
            PubSubLease?[] results = await Task.WhenAll(firstAcquire, secondAcquire).ConfigureAwait(false);

            Assert.That(results, Has.Exactly(1).Not.Null);
            Assert.That(results, Has.Exactly(1).Null);
        }

        [Test]
        public async Task FreshnessEnrollmentAndRetainedReleaseKeepFencesAcrossReplicasAsync()
        {
            var clock = new FakeTimeProvider();
            using var payload = new SimulatedSharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            Guid deployment = Guid.NewGuid();
            Guid epoch = Guid.NewGuid();
            var fresh = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            using var first = new SharedStorePubSubLeaseStore(fresh, clock);

            Assert.That(
                async () => await first.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));

            await fresh.EnrollAsync().ConfigureAwait(false);
            PubSubLease? firstLease = await first.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration)
                .ConfigureAwait(false);
            Assert.That(firstLease, Is.Not.Null);
            await first.ReleaseAsync(firstLease!.Value).ConfigureAwait(false);

            var replica = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            using var second = new SharedStorePubSubLeaseStore(replica, clock);
            PubSubLease? next = await second.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false);
            Assert.That(next?.FencingToken, Is.EqualTo(firstLease.Value.FencingToken + 1));
            Assert.That(
                await first.TryRenewAsync(firstLease.Value, LeaseDuration).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task ProtectedLeaseWithDeletedHistoryCannotBeRecreatedAsync()
        {
            using var payload = new SimulatedSharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            var fresh = new FreshnessProtectedKeyValueStore(
                payload, authority, protector, Guid.NewGuid(), Guid.NewGuid());
            await fresh.EnrollAsync().ConfigureAwait(false);
            using var leaseStore = new SharedStorePubSubLeaseStore(fresh, new FakeTimeProvider());
            PubSubLease? held = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            Assert.That(held, Is.Not.Null);
            string key = PubSubRedundancyStoreKeys.LeasePrefix + LeaseKey;
            Assert.That(await fresh.DeleteAsync(key).ConfigureAwait(false), Is.True);

            Assert.That(
                async () => await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(
                async () => await fresh.CompareAndSwapRetainedAsync(key, default, ByteString.Empty)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task InvalidOwnerOrExpiryCannotBeRecordedOrReplaceAValidLeaseAsync()
        {
            var clock = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            using var leaseStore = new SharedStorePubSubLeaseStore(backend, clock, LeaseDuration);
            Assert.That(
                async () => await leaseStore.TryAcquireAsync(LeaseKey, " ", LeaseDuration).ConfigureAwait(false),
                Throws.ArgumentException);
            Assert.That(
                async () => await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, TimeSpan.Zero).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());

            PubSubLease? held = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            Assert.That(held, Is.Not.Null);
            Assert.That(async () =>
                    await leaseStore.TryRenewAsync(held!.Value, LeaseDuration + TimeSpan.FromSeconds(1))
                        .ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task ExcessiveStoredExpiryCannotBeTreatedAsAnUnownedLeaseAsync()
        {
            var clock = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            using var original = new SharedStorePubSubLeaseStore(backend, clock);
            PubSubLease? held = await original.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            Assert.That(held, Is.Not.Null);
            TimeSpan limit = TimeSpan.FromSeconds(10);
            using var replica = new SharedStorePubSubLeaseStore(backend, clock, limit);

            Assert.That(
                async () => await replica.TryAcquireAsync(LeaseKey, OwnerB, limit).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));

            clock.Advance(LeaseDuration);
            PubSubLease? next = await replica.TryAcquireAsync(LeaseKey, OwnerB, limit).ConfigureAwait(false);
            Assert.That(next?.FencingToken, Is.EqualTo(held!.Value.FencingToken + 1));
        }

        [Test]
        public async Task ReusedOwnerCannotJoinLiveGrantAcrossOverlappingRestartAsync()
        {
            var clock = new FakeTimeProvider();
            using var payload = new SimulatedSharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            Guid deployment = Guid.NewGuid();
            Guid epoch = Guid.NewGuid();
            var fresh = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            await fresh.EnrollAsync().ConfigureAwait(false);
            using var original = new SharedStorePubSubLeaseStore(fresh, clock);
            using var replacement = new SharedStorePubSubLeaseStore(
                new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch), clock);

            PubSubLease? first = await original.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            PubSubLease grant = first!.Value;
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(
                await replacement.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false), Is.Null);
            Assert.That(await replacement.TryRenewAsync(grant, LeaseDuration).ConfigureAwait(false), Is.Null);
            await replacement.ReleaseAsync(grant).ConfigureAwait(false);

            PubSubLease? renewal = await original.TryRenewAsync(grant, LeaseDuration).ConfigureAwait(false);
            Assert.That(renewal, Is.Not.Null);
            Assert.That(renewal!.Value.FencingToken, Is.EqualTo(grant.FencingToken));
            original.Dispose();
            Assert.That(
                async () => await original.TryRenewAsync(renewal.Value, LeaseDuration).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                await replacement.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false), Is.Null);

            clock.Advance(LeaseDuration);
            PubSubLease? takeover =
                await replacement.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            Assert.That(takeover?.FencingToken, Is.EqualTo(grant.FencingToken + 1));
        }

        [Test]
        public async Task RenewalAndReleaseRequireTheLatestExactLocalGrantAsync()
        {
            var clock = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            using var owner = new SharedStorePubSubLeaseStore(backend, clock);
            using var standby = new SharedStorePubSubLeaseStore(backend, clock);
            PubSubLease? first = await owner.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            clock.Advance(TimeSpan.FromSeconds(5));
            PubSubLease? latest = await owner.TryRenewAsync(first!.Value, LeaseDuration).ConfigureAwait(false);
            Assert.That(latest, Is.Not.Null);
            Assert.That(
                await owner.TryRenewAsync(latest!.Value, LeaseDuration - TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false),
                Is.EqualTo(latest));

            await owner.ReleaseAsync(first.Value).ConfigureAwait(false);
            Assert.That(await owner.TryRenewAsync(first.Value, LeaseDuration).ConfigureAwait(false), Is.Null);
            Assert.That(
                await standby.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration).ConfigureAwait(false), Is.Null);

            await owner.ReleaseAsync(latest!.Value).ConfigureAwait(false);
            PubSubLease? takeover = await standby.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration)
                .ConfigureAwait(false);
            Assert.That(takeover?.FencingToken, Is.EqualTo(latest.Value.FencingToken + 1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task LegacyProtectedGrantMigratesWithoutResettingItsFenceAsync(bool live)
        {
            var clock = new FakeTimeProvider();
            using var payload = new SimulatedSharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            var fresh = new FreshnessProtectedKeyValueStore(
                payload, authority, protector, Guid.NewGuid(), Guid.NewGuid());
            await fresh.EnrollAsync().ConfigureAwait(false);
            using var replica = new SharedStorePubSubLeaseStore(fresh, clock);
            const long legacyFence = 42;
            var legacyGrant = new PubSubLease(LeaseKey, live ? OwnerA : string.Empty, legacyFence,
                live ? clock.GetUtcNow() + LeaseDuration : DateTimeOffset.MinValue);
            await fresh.SetAsync(PubSubRedundancyStoreKeys.LeasePrefix + LeaseKey, EncodeLegacyLease(legacyGrant))
                .ConfigureAwait(false);
            if (live)
            {
                Assert.That(
                    await replica.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false), Is.Null);
                Assert.That(await replica.TryRenewAsync(legacyGrant, LeaseDuration).ConfigureAwait(false), Is.Null);
                clock.Advance(LeaseDuration);
            }

            PubSubLease? acquired = await replica.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration)
                .ConfigureAwait(false);
            Assert.That(acquired?.FencingToken, Is.EqualTo(legacyFence + 1));
            PubSubLease? renewed = await replica.TryRenewAsync(acquired!.Value, LeaseDuration).ConfigureAwait(false);
            Assert.That(renewed, Is.EqualTo(acquired));
        }

        [Test]
        public async Task ExternalLeaseCheckpointAndStateRejectUnanchoredStoreAsync()
        {
            using var backend = new SimulatedSharedKeyValueStore();
            Assert.That(() => new SharedStorePubSubLeaseStore(backend), Throws.ArgumentException);
            Assert.That(() => new SharedStorePubSubWriterCheckpointStore(backend), Throws.ArgumentException);
            Assert.That(() => new Configuration.SharedStorePubSubRuntimeStateStore(backend), Throws.ArgumentException);

            var services = new ServiceCollection();
            services.AddSingleton<ISharedKeyValueStore>(backend);
            services.AddPubSubRedundancy(options =>
            {
                options.Election = PubSubRedundancyElection.LeaseStore;
                options.Mode = PubSubRedundancyMode.Hot;
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(() => provider.GetRequiredService<IPubSubLeaseStore>(), Throws.InvalidOperationException);
            Assert.That(
                () => provider.GetRequiredService<IPubSubWriterCheckpointStore>(), Throws.InvalidOperationException);
            Assert.That(
                () => provider.GetRequiredService<Configuration.IPubSubRuntimeStateStore>(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task ExternalHotLeaseRegistrationUsesTheSameEnrolledFreshnessAuthorityAsync()
        {
            using var payload = new SimulatedSharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            var services = new ServiceCollection();
            services.AddSingleton<ISharedKeyValueStore>(payload);
            services.AddSingleton<IRecordProtector>(protector);
            services.AddSharedRecordFreshness(_ => authority, Guid.NewGuid(), Guid.NewGuid());
            services.AddPubSubRedundancy(options =>
            {
                options.Election = PubSubRedundancyElection.LeaseStore;
                options.Mode = PubSubRedundancyMode.Hot;
                options.LeaseDuration = LeaseDuration;
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            await provider.GetRequiredService<FreshnessProtectedKeyValueStore>().EnrollAsync().ConfigureAwait(false);

            IPubSubLeaseStore leases = provider.GetRequiredService<IPubSubLeaseStore>();
            IPubSubWriterCheckpointStore checkpoints = provider.GetRequiredService<IPubSubWriterCheckpointStore>();
            var runtime = provider.GetRequiredService<Configuration.IPubSubRuntimeStateStore>();
            PubSubLease? lease = await leases.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration).ConfigureAwait(false);
            Assert.That(lease, Is.Not.Null);
            await checkpoints.SetSequenceNumberAsync(LeaseKey, 1, 25u, lease!.Value.FencingToken).ConfigureAwait(false);
            await runtime.SetStateAsync(LeaseKey, PubSubState.Operational, lease.Value.FencingToken)
                .ConfigureAwait(false);
            Assert.That(await checkpoints.GetSequenceNumberAsync(LeaseKey, 1).ConfigureAwait(false), Is.EqualTo(25u));
            Assert.That(
                await runtime.GetStateAsync(LeaseKey).ConfigureAwait(false), Is.EqualTo(PubSubState.Operational));
        }

        private static ByteString EncodeLegacyLease(PubSubLease lease)
        {
            byte[] owner = System.Text.Encoding.UTF8.GetBytes(lease.OwnerId);
            byte[] result = new byte[24 + owner.Length];
            Span<byte> fields = result;
            BinaryPrimitives.WriteInt32LittleEndian(fields, 1);
            BinaryPrimitives.WriteInt32LittleEndian(fields[4..], owner.Length);
            owner.CopyTo(result, 8);
            BinaryPrimitives.WriteInt64LittleEndian(fields[(8 + owner.Length)..], lease.FencingToken);
            BinaryPrimitives.WriteInt64LittleEndian(fields[(16 + owner.Length)..], lease.ExpiresAt.UtcTicks);
            return ByteString.From(result);
        }

        private const string LeaseKey = "writer-group";
        private const string OwnerA = "owner-a";
        private const string OwnerB = "owner-b";
        private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

        private sealed class CoordinatedReadSharedKeyValueStore :
            ISharedKeyValueStore,
            ISharedKeyValueStoreConsistency,
            IDisposable
        {
            public CoordinatedReadSharedKeyValueStore(ISharedKeyValueStore innerStore, string coordinatedKey)
            {
                m_innerStore = innerStore;
                m_coordinatedKey = coordinatedKey;
            }

            public async ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                (bool found, ByteString value) = await m_innerStore.TryGetAsync(key, ct).ConfigureAwait(false);
                if (key.EndsWith(m_coordinatedKey, StringComparison.Ordinal))
                {
                    int count = Interlocked.Increment(ref m_coordinatedReads);
                    if (count == 2)
                    {
                        m_secondReadArrived.TrySetResult(true);
                    }

                    await m_secondReadArrived.Task.WaitAsync(ct).ConfigureAwait(false);
                }

                return (found, value);
            }

            public bool IsLinearizable(string key)
            {
                return true;
            }

            public bool IsProcessLocal(string key)
            {
                return false;
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                return m_innerStore.SetAsync(key, value, ct);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_innerStore.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_innerStore.DeleteAsync(key, ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_innerStore.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_innerStore.WatchAsync(keyPrefix, ct);
            }

            public void Dispose()
            {
                m_secondReadArrived.TrySetCanceled();
            }

            private readonly ISharedKeyValueStore m_innerStore;
            private readonly string m_coordinatedKey;

            private readonly TaskCompletionSource<bool> m_secondReadArrived =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_coordinatedReads;
        }
    }
}
