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

// CA1861: inline literal arrays here are one-shot test fixtures, not hot-path
//   allocations, so hoisting them to static readonly fields adds no value. Suppressed file-level.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="HybridSharedKeyValueStore"/>: prefix routing between a bulk (CRDT) backend and a
    /// strong (Raft) backend. Two <see cref="InMemorySharedKeyValueStore"/> instances stand in for the backends so
    /// routing is directly observable.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class HybridSharedKeyValueStoreTests
    {
        [Test]
        public async Task BulkKeyRoutesToBulkStoreAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var strong = new InMemorySharedKeyValueStore();
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong);

            await hybrid.SetAsync("node/1", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);

            (bool inBulk, _) = await bulk.TryGetAsync("node/1").ConfigureAwait(false);
            (bool inStrong, _) = await strong.TryGetAsync("node/1").ConfigureAwait(false);
            Assert.That(inBulk, Is.True, "bulk keys live in the CRDT backend");
            Assert.That(inStrong, Is.False);
        }

        [Test]
        public async Task StrongKeyRoutesToStrongStoreAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var strong = new InMemorySharedKeyValueStore();
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong);

            await hybrid.SetAsync("nonce/abc", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);

            (bool inStrong, _) = await strong.TryGetAsync("nonce/abc").ConfigureAwait(false);
            (bool inBulk, _) = await bulk.TryGetAsync("nonce/abc").ConfigureAwait(false);
            Assert.That(inStrong, Is.True, "strong keys live in the Raft backend");
            Assert.That(inBulk, Is.False);
        }

        [Test]
        public async Task CompareAndSwapOnStrongKeyUsesStrongStoreAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var strong = new InMemorySharedKeyValueStore();
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong);
            var value = ByteString.From(new byte[] { 7 });

            bool created = await hybrid.CompareAndSwapAsync("lease/leader", default, value).ConfigureAwait(false);
            (bool inStrong, _) = await strong.TryGetAsync("lease/leader").ConfigureAwait(false);

            Assert.That(created, Is.True);
            Assert.That(inStrong, Is.True);
        }

        [Test]
        public async Task WatchOnStrongPrefixObservesStrongStoreAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var strong = new InMemorySharedKeyValueStore();
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            await using IAsyncEnumerator<KeyValueChange> enumerator =
                hybrid.WatchAsync("election/", cts.Token).GetAsyncEnumerator();

            ValueTask<bool> first = enumerator.MoveNextAsync();
            await hybrid.SetAsync("election/leader", ByteString.From(new byte[] { 1 }), cts.Token).ConfigureAwait(false);

            Assert.That(await first.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Key, Is.EqualTo("election/leader"));
        }

        [Test]
        public async Task EmptyPrefixScanMergesBothStoresAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var strong = new InMemorySharedKeyValueStore();
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong);

            await hybrid.SetAsync("node/1", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            await hybrid.SetAsync("nonce/a", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in hybrid.ScanAsync(string.Empty))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Is.EquivalentTo(["node/1", "nonce/a"]));
        }

        [Test]
        public async Task StrongPrefixScanReturnsStrongKeysOnlyAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var strong = new InMemorySharedKeyValueStore();
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong);

            await hybrid.SetAsync("node/1", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            await hybrid.SetAsync("nonce/a", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);
            await hybrid.SetAsync("nonce/b", ByteString.From(new byte[] { 3 })).ConfigureAwait(false);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in hybrid.ScanAsync("nonce/"))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Is.EquivalentTo(["nonce/a", "nonce/b"]));
        }

        [Test]
        public async Task OwnsStoresDisposesBothBackendsAsync()
        {
            var bulk = new InMemorySharedKeyValueStore();
            var strong = new InMemorySharedKeyValueStore();
            var hybrid = new HybridSharedKeyValueStore(bulk, strong, default, ownsStores: true);

            await hybrid.SetAsync("nonce/a", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            await hybrid.DisposeAsync().ConfigureAwait(false);

            // InMemorySharedKeyValueStore.Dispose clears its data, so a hit here
            // proves the dispose propagated to the owned backend.
            (bool found, _) = await strong.TryGetAsync("nonce/a").ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task CustomStrongPrefixReplacesDefaultsAsync()
        {
            using var bulk = new InMemorySharedKeyValueStore();
            using var strong = new InMemorySharedKeyValueStore();
            var prefixes = new ArrayOf<string>(new[] { "cas/" }.AsMemory());
            await using var hybrid = new HybridSharedKeyValueStore(bulk, strong, prefixes);

            await hybrid.SetAsync("cas/x", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            await hybrid.SetAsync("nonce/y", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);

            (bool casInStrong, _) = await strong.TryGetAsync("cas/x").ConfigureAwait(false);
            (bool nonceInBulk, _) = await bulk.TryGetAsync("nonce/y").ConfigureAwait(false);
            Assert.That(casInStrong, Is.True, "the custom prefix routes to the strong store");
            Assert.That(nonceInBulk, Is.True, "custom prefixes replace the defaults, so nonce/ is now a bulk key");
        }
    }
}
