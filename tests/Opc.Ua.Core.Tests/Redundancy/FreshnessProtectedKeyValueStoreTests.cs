// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Core.Tests.Redundancy
{
    /// <summary>
    /// Exercises freshness across independent payload and authority boundaries.
    /// </summary>
    [TestFixture]
    [Category("Redundancy")]
    [Parallelizable(ParallelScope.All)]
    public sealed class FreshnessProtectedKeyValueStoreTests
    {
        /// <summary>
        /// A replica must not infer that a deployment is empty when its epoch is not enrolled.
        /// </summary>
        [Test]
        public async Task ReadsRequireTheConfiguredEpochAsync()
        {
            using var stores = new Stores();
            FreshnessProtectedKeyValueStore replica = stores.NewReplica();
            Assert.That(async () => await replica.TryGetAsync("key").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());

            await replica.EnrollAsync().ConfigureAwait(false);
            await stores.NewReplica().VerifyEpochAsync().ConfigureAwait(false);

            FreshnessProtectedKeyValueStore unrecognized = stores.NewReplica(Guid.NewGuid());
            Assert.That(async () => await unrecognized.EnrollAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// A payload staged during an interrupted update cannot replace the last committed value.
        /// </summary>
        [Test]
        public async Task InterruptedCommitRemainsInvisibleAsync()
        {
            using var stores = new Stores();
            FreshnessProtectedKeyValueStore replica = stores.NewReplica();
            await replica.EnrollAsync().ConfigureAwait(false);
            await replica.SetAsync("key", s_original).ConfigureAwait(false);

            stores.RejectNextCommit = true;
            Assert.That(async () => await replica.SetAsync("key", s_updated).ConfigureAwait(false),
                Throws.TypeOf<TimeoutException>());

            (bool found, ByteString value) = await stores.NewReplica().TryGetAsync("key").ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo(s_original));
        }

        /// <summary>
        /// A payload-only restore cannot roll back a newer generation or a committed deletion.
        /// </summary>
        [Test]
        public async Task OldPayloadSnapshotCannotRollBackCommittedStateAsync()
        {
            using var stores = new Stores();
            FreshnessProtectedKeyValueStore writer = stores.NewReplica();
            await writer.EnrollAsync().ConfigureAwait(false);
            await writer.SetAsync("key", s_original).ConfigureAwait(false);
            List<string> originalPayloadKeys = await stores.ReadPayloadKeysAsync().ConfigureAwait(false);

            await writer.SetAsync("key", s_updated).ConfigureAwait(false);
            List<string> updatedPayloadKeys = await stores.ReadPayloadKeysAsync().ConfigureAwait(false);
            updatedPayloadKeys.RemoveAll(originalPayloadKeys.Contains);
            Assert.That(updatedPayloadKeys, Has.Count.EqualTo(1));
            string updatedKey = updatedPayloadKeys[0];
            (_, ByteString update) = await stores.Payload.TryGetAsync(updatedKey).ConfigureAwait(false);

            await stores.Payload.DeleteAsync(updatedKey).ConfigureAwait(false);
            FreshnessProtectedKeyValueStore reader = stores.NewReplica();
            Assert.That(async () => await reader.TryGetAsync("key").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());

            await stores.Payload.SetAsync(updatedKey, update).ConfigureAwait(false);
            Assert.That(await writer.DeleteAsync("key").ConfigureAwait(false), Is.True);
            (bool found, ByteString missing) = await stores.NewReplica().TryGetAsync("key").ConfigureAwait(false);
            Assert.That(found, Is.False);
            Assert.That(missing.IsNull, Is.True);
        }

        /// <summary>
        /// The trusted commit compares the current generation, not merely the staged data.
        /// </summary>
        [Test]
        public async Task CompareAndSwapHasOneWinnerAcrossReplicasAsync()
        {
            using var stores = new Stores();
            FreshnessProtectedKeyValueStore first = stores.NewReplica();
            FreshnessProtectedKeyValueStore second = stores.NewReplica();
            await first.EnrollAsync().ConfigureAwait(false);

            bool[] results = await Task.WhenAll(
                first.CompareAndSwapAsync("key", default, s_original).AsTask(),
                second.CompareAndSwapAsync("key", default, s_updated).AsTask()).ConfigureAwait(false);
            Assert.That(results, Has.Exactly(1).EqualTo(true));
            (_, ByteString value) = await second.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(value, Is.EqualTo(results[0] ? s_original : s_updated));
        }

        /// <summary>
        /// A staged mutation cannot commit if a successor acquired the grant before publication.
        /// </summary>
        [Test]
        public async Task GuardIsComparedInTheSameCommitAsTheProtectedRecordAsync()
        {
            using var stores = new Stores();
            FreshnessProtectedKeyValueStore former = stores.NewReplica();
            FreshnessProtectedKeyValueStore successor = stores.NewReplica();
            await former.EnrollAsync().ConfigureAwait(false);
            await former.SetAsync("grant", s_original).ConfigureAwait(false);

            stores.BeforeGuardedCommit = async () =>
                await successor.SetAsync("grant", s_updated).ConfigureAwait(false);
            Assert.That(await former.CompareAndSwapWhenAsync("target", default, s_original, "grant", s_original)
                .ConfigureAwait(false), Is.False);
            Assert.That((await successor.TryGetAsync("target").ConfigureAwait(false)).Found, Is.False);

            Assert.That(await successor.CompareAndSwapWhenAsync("target", default, s_updated, "grant", s_updated)
                .ConfigureAwait(false), Is.True);
            Assert.That((await former.TryGetAsync("target").ConfigureAwait(false)).Value, Is.EqualTo(s_updated));
        }

        /// <summary>
        /// An attempted write cannot publish if the authority's deadline passes during staging.
        /// </summary>
        [Test]
        public async Task DeadlineIsCheckedWhenThePointerCommitsAsync()
        {
            var clock = new FakeTimeProvider();
            using var stores = new Stores(clock);
            FreshnessProtectedKeyValueStore replica = stores.NewReplica();
            await replica.EnrollAsync().ConfigureAwait(false);
            await replica.SetAsync("grant", s_original).ConfigureAwait(false);
            DateTimeOffset deadline = clock.GetUtcNow() + TimeSpan.FromSeconds(4);
            stores.BeforeGuardedCommit = () =>
            {
                clock.Advance(TimeSpan.FromSeconds(4));
                return default;
            };

            await Assert.ThatAsync(
                async () => await replica.CompareAndSwapBeforeAsync(
                    "target", default, s_updated, "grant", s_original, deadline).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotWritable)).ConfigureAwait(false);
            Assert.That((await replica.TryGetAsync("grant").ConfigureAwait(false)).Value, Is.EqualTo(s_original));
            Assert.That((await replica.TryGetAsync("target").ConfigureAwait(false)).Found, Is.False);
        }

        private sealed class Stores : IDisposable
        {
            public Stores(TimeProvider? clock = null)
            {
                m_authority = new InMemorySharedKeyValueStore(clock ?? TimeProvider.System);
                using var rng = RandomNumberGenerator.Create();
                var key = new byte[32];
                rng.GetBytes(key);
                m_protector = new AesCbcHmacRecordProtector(key);
                Array.Clear(key, 0, key.Length);

                var authority = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
                Mock<ISharedKeyValueStoreConsistency> consistency = authority.As<ISharedKeyValueStoreConsistency>();
                consistency.Setup(store => store.IsLinearizable(It.IsAny<string>())).Returns(true);
                consistency.Setup(store => store.IsProcessLocal(It.IsAny<string>())).Returns(false);
                Mock<IDeadlineGuardedSharedKeyValueStore> guarded = authority.As<IDeadlineGuardedSharedKeyValueStore>();
                guarded.SetupGet(store => store.SupportsGuardedDeadlines).Returns(true);
                guarded.Setup(store => store.CompareAndSwapWhenAsync(
                        It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<string>(),
                        It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns(async (
                        string key, ByteString expected, ByteString value, string guard, ByteString grant,
                        CancellationToken ct) =>
                    {
                        if (BeforeGuardedCommit is { } before)
                        {
                            BeforeGuardedCommit = null;
                            await before().ConfigureAwait(false);
                        }
                        return await m_authority.CompareAndSwapWhenAsync(key, expected, value, guard, grant, ct)
                            .ConfigureAwait(false);
                    });
                guarded.Setup(store => store.CompareAndSwapBeforeAsync(
                        It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<string>(),
                        It.IsAny<ByteString>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                    .Returns(async (
                        string key, ByteString expected, ByteString value, string guard, ByteString grant,
                        DateTimeOffset deadline, CancellationToken ct) =>
                    {
                        if (BeforeGuardedCommit is { } before)
                        {
                            BeforeGuardedCommit = null;
                            await before().ConfigureAwait(false);
                        }
                        return await m_authority.CompareAndSwapBeforeAsync(key, expected, value, guard, grant, deadline, ct)
                            .ConfigureAwait(false);
                    });
                authority.Setup(store => store.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string key, CancellationToken ct) => m_authority.TryGetAsync(key, ct));
                authority.Setup(store => store.CompareAndSwapAsync(
                        It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    {
                        if (RejectNextCommit)
                        {
                            RejectNextCommit = false;
                            throw new TimeoutException("Authority unavailable before committing.");
                        }
                        return m_authority.CompareAndSwapAsync(key, expected, value, ct);
                    });
                m_sharedAuthority = authority.Object;
            }

            public InMemorySharedKeyValueStore Payload { get; } = new();

            public bool RejectNextCommit { get; set; }

            public Func<ValueTask>? BeforeGuardedCommit { get; set; }

            public FreshnessProtectedKeyValueStore NewReplica(Guid epoch = default)
            {
                return new FreshnessProtectedKeyValueStore(
                    Payload, m_sharedAuthority, m_protector, m_deployment, epoch == default ? m_epoch : epoch);
            }

            public async ValueTask<List<string>> ReadPayloadKeysAsync()
            {
                List<string> keys = [];
                await foreach (KeyValuePair<string, ByteString> entry in Payload.ScanAsync(string.Empty)
                    .ConfigureAwait(false))
                {
                    keys.Add(entry.Key);
                }
                return keys;
            }

            public void Dispose()
            {
                m_protector.Dispose();
                m_authority.Dispose();
                Payload.Dispose();
            }

            private readonly AesCbcHmacRecordProtector m_protector;
            private readonly InMemorySharedKeyValueStore m_authority;
            private readonly ISharedKeyValueStore m_sharedAuthority;
            private readonly Guid m_deployment = Guid.NewGuid();
            private readonly Guid m_epoch = Guid.NewGuid();
        }

        private static readonly ByteString s_original = ByteString.From(new byte[] { 1, 2, 3 });
        private static readonly ByteString s_updated = ByteString.From(new byte[] { 4, 5, 6 });
    }
}
