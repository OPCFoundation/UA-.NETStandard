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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Server.Tests.Identity
{
    /// <summary>
    /// Covers protected contract binding without certifying an unknown eventual or legacy store.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ReplicaIdentityStoreTests
    {
        /// <summary>
        /// Reuses a matching descriptor and refuses a different layout without changing stored bytes.
        /// </summary>
        [Test]
        public async Task StoreBindsOneProtectedIdentityContractAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var identity = new ReplicaNodeIdFactory("identity-set", ["urn:shared:first", "urn:shared:second"]);
            await identity.InitializeNewStoreAsync(store, protector).ConfigureAwait(false);
            (bool found, ByteString original) = await store.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(original, Is.Not.EqualTo(identity.Descriptor));
            Assert.That(protector.TryUnprotect(original, out ByteString descriptor), Is.True);
            Assert.That(descriptor, Is.EqualTo(identity.Descriptor));
            await identity.InitializeNewStoreAsync(store, protector).ConfigureAwait(false);

            var incompatible = new ReplicaNodeIdFactory("identity-set", ["urn:shared:second", "urn:shared:first"]);
            await Assert.ThatAsync(
                async () => await incompatible.InitializeNewStoreAsync(store, protector).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("different replica identity"))
                .ConfigureAwait(false);
            (_, ByteString retained) = await store.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            Assert.That(retained, Is.EqualTo(original));
        }

        /// <summary>
        /// Refuses existing unbound address-space data rather than silently adopting its numeric namespace indexes.
        /// </summary>
        [TestCase("n/ns=2;s=legacy")]
        [TestCase("v/ns=2;s=legacy")]
        [TestCase("dlog/00000000000000000001")]
        [TestCase("snapmeta/head")]
        [TestCase("partition/model")]
        public async Task LegacyStateWithoutContractIsNotAdoptedAsync(string key)
        {
            using var store = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            ByteString original = new(new byte[] { 7, 9 });
            await store.SetAsync(key, original).ConfigureAwait(false);
            var identity = new ReplicaNodeIdFactory("identity-set", ["urn:shared:first"]);

            await Assert.ThatAsync(
                async () => await identity.InitializeNewStoreAsync(store, protector).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("no replica identity")).ConfigureAwait(false);
            (bool contractExists, _) = await store.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            (_, ByteString retained) = await store.TryGetAsync(key).ConfigureAwait(false);
            Assert.That(contractExists, Is.False);
            Assert.That(retained, Is.EqualTo(original));
        }

        /// <summary>
        /// Rejects corrupt protected metadata without replacing it or considering the store uninitialized.
        /// </summary>
        [Test]
        public async Task CorruptContractFailsClosedAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var identity = new ReplicaNodeIdFactory("identity-set", ["urn:shared:first"]);
            ByteString corrupt = new(new byte[] { 1, 2, 3 });
            await store.SetAsync(ReplicaIdentityStore.Key, corrupt).ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await identity.InitializeNewStoreAsync(store, protector).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed))
                .ConfigureAwait(false);
            (_, ByteString retained) = await store.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            Assert.That(retained, Is.EqualTo(corrupt));
        }

        /// <summary>
        /// Requires explicit new-store provisioning before an eventual payload backend can use the shared contract.
        /// </summary>
        [Test]
        public async Task HybridStoreCannotBootstrapFromAnEmptyEventualScanAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var bulk = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await using var strong = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong);
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var identity = new ReplicaNodeIdFactory("identity-set", ["urn:shared:first"]);

            await Assert.ThatAsync(
                async () => await ReplicaIdentityStore.VerifyAsync(hybrid, protector, identity.Descriptor, default)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("eventual replica view")).ConfigureAwait(false);
            (bool prematurelyBound, _) = await strong.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            Assert.That(prematurelyBound, Is.False);

            await identity.InitializeNewStoreAsync(strong, protector).ConfigureAwait(false);
            await ReplicaIdentityStore.VerifyAsync(hybrid, protector, identity.Descriptor, default)
                .ConfigureAwait(false);
            (bool bound, _) = await strong.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            (bool misplaced, _) = await bulk.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            Assert.That(bound, Is.True);
            Assert.That(misplaced, Is.False);
        }

        /// <summary>
        /// Rechecks the winning descriptor when another initializer wins the real store's compare-and-swap.
        /// </summary>
        [Test]
        public async Task CompetingInitializersCannotOverwriteTheWinnerAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var bothRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            var wrapper = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            wrapper.As<ISharedKeyValueStoreConsistency>()
                .Setup(s => s.IsLinearizable(It.IsAny<string>()))
                .Returns(true);
            wrapper.As<ISharedKeyValueStoreConsistency>()
                .Setup(s => s.IsProcessLocal(It.IsAny<string>()))
                .Returns(true);
            wrapper.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string prefix, CancellationToken ct) => backend.ScanAsync(prefix, ct));
            wrapper.Setup(s => s.TryGetAsync(ReplicaIdentityStore.Key, It.IsAny<CancellationToken>()))
                .Returns(async (string key, CancellationToken ct) =>
                {
                    (bool found, ByteString value) = await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                    if (Interlocked.Increment(ref reads) <= 2)
                    {
                        if (Volatile.Read(ref reads) == 2)
                        {
                            bothRead.TrySetResult(true);
                        }
                        await bothRead.Task.ConfigureAwait(false);
                    }
                    return (found, value);
                });
            wrapper.Setup(s => s.CompareAndSwapAsync(
                    ReplicaIdentityStore.Key,
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            var left = new ReplicaNodeIdFactory("identity-set", ["urn:shared:first"]);
            var right = new ReplicaNodeIdFactory("identity-set", ["urn:shared:second"]);
            Task first = left.InitializeNewStoreAsync(wrapper.Object, protector).AsTask();
            Task second = right.InitializeNewStoreAsync(wrapper.Object, protector).AsTask();

            await Assert.ThatAsync(async () => await Task.WhenAll(first, second).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("different replica identity"))
                .ConfigureAwait(false);
            Assert.That(new[] { first.Status, second.Status },
                Is.EquivalentTo([TaskStatus.RanToCompletion, TaskStatus.Faulted]));
            (bool found, ByteString stored) = await backend.TryGetAsync(ReplicaIdentityStore.Key).ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(protector.TryUnprotect(stored, out ByteString descriptor), Is.True);
            Assert.That(descriptor,
                Is.EqualTo(first.Status == TaskStatus.RanToCompletion ? left.Descriptor : right.Descriptor));
            wrapper.Verify(s => s.CompareAndSwapAsync(
                ReplicaIdentityStore.Key,
                default,
                It.IsAny<ByteString>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        private static AesCbcHmacRecordProtector CreateProtector()
        {
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            return new AesCbcHmacRecordProtector(key);
        }
    }
}
