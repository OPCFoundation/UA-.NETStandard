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

// CA1861: inline literal arrays here are one-shot test fixtures, not hot-path
//   allocations, so hoisting them to static readonly fields adds no value. Suppressed file-level.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Coverage for the client-side CRDT shared key/value store.
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class ReplicatedSharedKeyValueStoreTests
    {
        [Test]
        public async Task SetThenGetRoundTripsAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var store = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            var value = new ByteString(new byte[] { 1, 2, 3, 4 });
            await store.SetAsync("session/a", value).ConfigureAwait(false);
            (bool found, ByteString got) = await store.TryGetAsync("session/a").ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(got.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        public async Task ReplicatesSetAndDeleteAcrossReplicasAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore storeA = CreateStore(network);
            await using ReplicatedSharedKeyValueStore storeB = CreateStore(network);

            // Warm up both transports so neither misses the other's broadcasts.
            await storeA.TryGetAsync("warmup").ConfigureAwait(false);
            await storeB.TryGetAsync("warmup").ConfigureAwait(false);

            var value = new ByteString(new byte[] { 1, 2, 3, 4 });
            await storeA.SetAsync("session/abc", value).ConfigureAwait(false);

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, ByteString stored) = await storeB.TryGetAsync("session/abc").ConfigureAwait(false);
                    return found && stored.ToArray().AsSpan().SequenceEqual(value.ToArray());
                },
                "a value set on A replicates to B").ConfigureAwait(false);

            bool existed = await storeA.DeleteAsync("session/abc").ConfigureAwait(false);
            Assert.That(existed, Is.True, "delete reports the entry existed locally");

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, _) = await storeB.TryGetAsync("session/abc").ConfigureAwait(false);
                    return !found;
                },
                "a delete on A replicates to B").ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteReturnsFalseWhenKeyAbsentAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore store = CreateStore(network);

            bool existed = await store.DeleteAsync("missing").ConfigureAwait(false);

            Assert.That(existed, Is.False);
        }

        [Test]
        public async Task CompareAndSwapThrowsNotSupportedAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore store = CreateStore(network);

            Assert.That(
                async () => await store.CompareAndSwapAsync("k", default, new ByteString(new byte[] { 1 }))
                    .ConfigureAwait(false),
                Throws.TypeOf<NotSupportedException>(),
                "CRDT stores cannot provide a linearizable compare-and-swap");
        }

        [Test]
        public async Task ScanReturnsOnlyMatchingPrefixAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore store = CreateStore(network);

            await store.SetAsync("session/a", new ByteString(new byte[] { 1 })).ConfigureAwait(false);
            await store.SetAsync("session/b", new ByteString(new byte[] { 2 })).ConfigureAwait(false);
            await store.SetAsync("other/c", new ByteString(new byte[] { 3 })).ConfigureAwait(false);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> pair in store.ScanAsync("session/").ConfigureAwait(false))
            {
                keys.Add(pair.Key);
            }

            Assert.That(keys, Is.EquivalentTo(["session/a", "session/b"]));
        }

        [Test]
        public async Task WatchThrowsNotSupportedAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore store = CreateStore(network);

            // WatchAsync throws synchronously (its body throws, so it is not a
            // deferred iterator), hence the delegate is not awaited.
            Assert.That(() => store.WatchAsync("session/"), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public async Task ConstructorRejectsNullArgumentsAsync()
        {
            await using var network = new InMemoryNetwork();

            Assert.That(
                () => new ReplicatedSharedKeyValueStore(
                    ReplicaId.New(), null!, TimeProvider.System, CrdtReaderOptions.Default),
                Throws.TypeOf<ArgumentNullException>(), "transport must not be null");
            Assert.That(
                () => new ReplicatedSharedKeyValueStore(
                    ReplicaId.New(), network.CreateTransport(), TimeProvider.System, null!),
                Throws.TypeOf<ArgumentNullException>(), "reader options must not be null");
            Assert.That(
                () => new ReplicatedSharedKeyValueStore(
                    ReplicaId.New(), network.CreateTransport(), null!, CrdtReaderOptions.Default),
                Throws.TypeOf<ArgumentNullException>(), "time provider must not be null");
        }

        [Test]
        public async Task DisposeIsIdempotentAsync()
        {
            await using var network = new InMemoryNetwork();
            var store = CreateStore(network);

            await store.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await store.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

        private static ReplicatedSharedKeyValueStore CreateStore(InMemoryNetwork network)
        {
            return new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
        }

        private static async Task AssertEventuallyAsync(Func<Task<bool>> condition, string message)
        {
            // Generous deadline: CRDT convergence over the in-memory gossip network is normally
            // sub-second, but background loops can be CPU-starved on a loaded CI runner.
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition().ConfigureAwait(false))
                {
                    return;
                }
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.Fail(message);
        }
    }
}
