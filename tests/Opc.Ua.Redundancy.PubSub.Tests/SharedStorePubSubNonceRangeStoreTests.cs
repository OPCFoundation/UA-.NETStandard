// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Redundancy.PubSub.Tests
{
    /// <summary>
    /// Covers durable range allocation through normal restart and lease takeover.
    /// </summary>
    [TestFixture]
    [Category("Redundancy")]
    public sealed class SharedStorePubSubNonceRangeStoreTests
    {
        /// <summary>
        /// A restart abandons unused numbers, and a promoted writer cannot allocate under an older fence.
        /// </summary>
        [Test]
        public async Task ReservesAfterRestartAndTakeoverUsingFreshnessAuthorityAsync()
        {
            using var payload = new InMemorySharedKeyValueStore();
            using var backing = new InMemorySharedKeyValueStore();
            using var rng = RandomNumberGenerator.Create();
            var masterKey = new byte[32];
            rng.GetBytes(masterKey);
            using var protector = new AesCbcHmacRecordProtector(masterKey);
            CryptoUtils.ZeroMemory(masterKey);
            ISharedKeyValueStore authority = CreateDurableStoreFixture(backing);
            Guid deployment = Guid.NewGuid();
            Guid epoch = Guid.NewGuid();
            var enrolled = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            await enrolled.EnrollAsync().ConfigureAwait(false);
            var first = new SharedStorePubSubNonceRangeStore(enrolled);
            PublisherId publisher = PublisherId.FromString("replay-scope");
            const string component = "writer";
            var clock = new FakeTimeProvider();
            TimeSpan leaseDuration = TimeSpan.FromSeconds(15);
            var firstLeases = new PubSubNonceLeaseTracker(new SharedStorePubSubLeaseStore(enrolled, clock), clock);
            PubSubLease? firstLease = await firstLeases.TryAcquireAsync(component, "a", leaseDuration)
                .ConfigureAwait(false);
            Assert.That(firstLease, Is.Not.Null);
            Assert.That(firstLeases.TryGetActiveFence(component, out long firstFence), Is.True);
            PubSubNonceRange? original = await first.ReserveAsync(publisher, 5, 1, component, firstFence, 16)
                .ConfigureAwait(false);

            var replica = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            var restarted = new SharedStorePubSubNonceRangeStore(replica);
            PubSubNonceRange? resumed = await restarted.ReserveAsync(publisher, 5, 1, component, firstFence, 16)
                .ConfigureAwait(false);
            await firstLeases.ReleaseAsync(firstLease.GetValueOrDefault()).ConfigureAwait(false);
            Assert.That(firstLeases.TryGetActiveFence(component, out _), Is.False);
            var secondLeases = new PubSubNonceLeaseTracker(new SharedStorePubSubLeaseStore(replica, clock), clock);
            Assert.That(await secondLeases.TryAcquireAsync(component, "b", leaseDuration).ConfigureAwait(false),
                Is.Not.Null);
            Assert.That(secondLeases.TryGetActiveFence(component, out long secondFence), Is.True);
            PubSubNonceRange? promoted = await restarted.ReserveAsync(publisher, 5, 1, component, secondFence, 16)
                .ConfigureAwait(false);

            Assert.That(original, Is.EqualTo(new PubSubNonceRange(1, 16)));
            Assert.That(resumed, Is.EqualTo(new PubSubNonceRange(17, 32)));
            Assert.That(promoted, Is.EqualTo(new PubSubNonceRange(33, 48)));
            Assert.That(
                async () => await first.ReserveAsync(publisher, 5, 2, component, firstFence, 16).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// The last range is shortened and publication can only resume with a different token.
        /// </summary>
        [Test]
        public async Task ExhaustionStopsOneTokenWithoutWrappingAsync()
        {
            using var payload = new InMemorySharedKeyValueStore();
            using var backing = new InMemorySharedKeyValueStore();
            using var rng = RandomNumberGenerator.Create();
            var masterKey = new byte[32];
            rng.GetBytes(masterKey);
            using var protector = new AesCbcHmacRecordProtector(masterKey);
            CryptoUtils.ZeroMemory(masterKey);
            var durable = new FreshnessProtectedKeyValueStore(
                payload, CreateDurableStoreFixture(backing), protector, Guid.NewGuid(), Guid.NewGuid());
            await durable.EnrollAsync().ConfigureAwait(false);
            var store = new SharedStorePubSubNonceRangeStore(durable);
            PublisherId publisher = PublisherId.FromByte(8);
            _ = await store.ReserveAsync(publisher, 2, 1, "writer", 1, uint.MaxValue - 2).ConfigureAwait(false);

            PubSubNonceRange? other = await store.ReserveAsync(publisher, 3, 1, "other-writer", 1, 1)
                .ConfigureAwait(false);
            PubSubNonceRange? last = await store.ReserveAsync(publisher, 2, 1, "writer", 1, 10).ConfigureAwait(false);
            var restarted = new SharedStorePubSubNonceRangeStore(durable);
            PubSubNonceRange? exhausted = await restarted.ReserveAsync(publisher, 2, 1, "writer", 2, 10)
                .ConfigureAwait(false);
            PubSubNonceRange? rotated = await restarted.ReserveAsync(publisher, 2, 2, "writer", 2, 10)
                .ConfigureAwait(false);

            Assert.That(other, Is.EqualTo(new PubSubNonceRange(uint.MaxValue - 1, uint.MaxValue - 1)));
            Assert.That(last, Is.EqualTo(new PubSubNonceRange(uint.MaxValue, uint.MaxValue)));
            Assert.That(exhausted, Is.Null);
            Assert.That(rotated, Is.EqualTo(new PubSubNonceRange(1, 10)));
        }

        /// <summary>
        /// Linearizability alone cannot protect nonce reservations after a backend restore.
        /// </summary>
        [Test]
        public void ConstructorRejectsRawSharedStore()
        {
            using var backing = new InMemorySharedKeyValueStore();
            ISharedKeyValueStore shared = CreateDurableStoreFixture(backing);
            Assert.That(() => new SharedStorePubSubNonceRangeStore(shared), Throws.ArgumentException);
        }

        private static ISharedKeyValueStore CreateDurableStoreFixture(InMemorySharedKeyValueStore backing)
        {
            var store = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            var consistency = store.As<ISharedKeyValueStoreConsistency>();
            consistency.Setup(x => x.IsLinearizable(It.IsAny<string>())).Returns(true);
            consistency.Setup(x => x.IsProcessLocal(It.IsAny<string>())).Returns(false);
            store.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backing.TryGetAsync(key, ct));
            store.Setup(x => x.CompareAndSwapAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backing.CompareAndSwapAsync(key, expected, value, ct));
            return store.Object;
        }
    }
}
