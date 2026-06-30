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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="RaftSharedKeyValueStore"/>: the linearizable CP store built on
    /// <see cref="IRaftConsensus"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class RaftSharedKeyValueStoreTests
    {
        [Test]
        public async Task SetAndTryGetReturnsStoredValueAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            ByteString payload = ByteString.From(new byte[] { 1, 2, 3 });

            await store.SetAsync("k1", payload);
            (bool found, ByteString value) = await store.TryGetAsync("k1");

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        [Test]
        public async Task TryGetMissingKeyReturnsFalseAsync()
        {
            await using var store = new RaftSharedKeyValueStore();

            (bool found, ByteString value) = await store.TryGetAsync("missing");

            Assert.That(found, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public async Task CompareAndSwapCreatesWhenAbsentAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            ByteString value = ByteString.From(new byte[] { 9 });

            bool created = await store.CompareAndSwapAsync("k", default, value);
            bool createdAgain = await store.CompareAndSwapAsync("k", default, value);

            Assert.That(created, Is.True);
            Assert.That(createdAgain, Is.False, "second create-if-absent must fail because the key now exists");
        }

        [Test]
        public async Task CompareAndSwapSwapsWhenValueMatchesAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            ByteString first = ByteString.From(new byte[] { 1 });
            ByteString second = ByteString.From(new byte[] { 2 });
            await store.SetAsync("k", first);

            bool swapped = await store.CompareAndSwapAsync("k", first, second);
            (bool found, ByteString value) = await store.TryGetAsync("k");

            Assert.That(swapped, Is.True);
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(second.ToArray()));
        }

        [Test]
        public async Task CompareAndSwapFailsWhenValueMismatchAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            ByteString actual = ByteString.From(new byte[] { 1 });
            ByteString wrongExpected = ByteString.From(new byte[] { 7 });
            ByteString desired = ByteString.From(new byte[] { 2 });
            await store.SetAsync("k", actual);

            bool swapped = await store.CompareAndSwapAsync("k", wrongExpected, desired);
            (bool found, ByteString value) = await store.TryGetAsync("k");

            Assert.That(swapped, Is.False);
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(actual.ToArray()), "value must be unchanged on a failed CAS");
        }

        [Test]
        public async Task DeleteRemovesKeyAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            await store.SetAsync("k", ByteString.From(new byte[] { 1 }));

            bool deleted = await store.DeleteAsync("k");
            bool deletedAgain = await store.DeleteAsync("k");
            (bool found, _) = await store.TryGetAsync("k");

            Assert.That(deleted, Is.True);
            Assert.That(deletedAgain, Is.False);
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task ScanReturnsMatchingPrefixOnlyAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            await store.SetAsync("a/1", ByteString.From(new byte[] { 1 }));
            await store.SetAsync("a/2", ByteString.From(new byte[] { 2 }));
            await store.SetAsync("b/1", ByteString.From(new byte[] { 3 }));

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync("a/"))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Is.EquivalentTo(new[] { "a/1", "a/2" }));
        }

        [Test]
        public async Task WatchObservesSetAndDeleteForPrefixAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Pre-start the store so the watcher registers synchronously on the
            // first MoveNextAsync, before any mutation is proposed.
            await store.TryGetAsync("warmup", cts.Token);

            await using IAsyncEnumerator<KeyValueChange> enumerator =
                store.WatchAsync("a/", cts.Token).GetAsyncEnumerator();

            ValueTask<bool> first = enumerator.MoveNextAsync();
            await store.SetAsync("b/ignored", ByteString.From(new byte[] { 0 }), cts.Token);
            await store.SetAsync("a/1", ByteString.From(new byte[] { 1 }), cts.Token);

            Assert.That(await first, Is.True);
            Assert.That(enumerator.Current.Kind, Is.EqualTo(KeyValueChangeKind.Set));
            Assert.That(enumerator.Current.Key, Is.EqualTo("a/1"));

            ValueTask<bool> second = enumerator.MoveNextAsync();
            await store.DeleteAsync("a/1", cts.Token);

            Assert.That(await second, Is.True);
            Assert.That(enumerator.Current.Kind, Is.EqualTo(KeyValueChangeKind.Delete));
            Assert.That(enumerator.Current.Key, Is.EqualTo("a/1"));
        }

        [Test]
        public async Task ConcurrentCompareAndSwapHasExactlyOneWinnerAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            const int contenders = 24;

            // Every contender races to create the same key from absent. Because
            // the consensus log is a single total order, exactly one wins.
            IEnumerable<Task<bool>> races = Enumerable.Range(0, contenders).Select(ii =>
                store.CompareAndSwapAsync("leader", default, ByteString.From(new[] { (byte)ii })).AsTask());
            bool[] results = await Task.WhenAll(races);

            Assert.That(results.Count(won => won), Is.EqualTo(1), "exactly one compare-and-swap may win");
        }

        [Test]
        public async Task TwoReplicasConvergeOnSharedClusterAsync()
        {
            var cluster = new InProcessRaftCluster();
            await using InProcessRaftConsensus consensus1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus consensus2 = cluster.CreateNode(2);
            await using var store1 = new RaftSharedKeyValueStore(consensus1);
            await using var store2 = new RaftSharedKeyValueStore(consensus2);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Start both replicas (registers each consensus node with the
            // cluster) before writing, so the broadcast reaches both.
            await store1.TryGetAsync("warmup", cts.Token);
            await store2.TryGetAsync("warmup", cts.Token);

            ByteString payload = ByteString.From(new byte[] { 42 });
            await store1.SetAsync("shared", payload, cts.Token);

            ByteString observed = await WaitForValueAsync(store2, "shared", cts.Token);
            Assert.That(observed.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        private static async Task<ByteString> WaitForValueAsync(
            RaftSharedKeyValueStore store,
            string key,
            CancellationToken ct)
        {
            while (true)
            {
                (bool found, ByteString value) = await store.TryGetAsync(key, ct);
                if (found)
                {
                    return value;
                }
                await Task.Delay(10, ct);
            }
        }
    }
}
