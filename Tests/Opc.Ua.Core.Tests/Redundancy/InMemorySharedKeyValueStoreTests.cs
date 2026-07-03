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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Core.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="InMemorySharedKeyValueStore"/>.
    /// </summary>
    [TestFixture]
    [Category("Redundancy")]
    [Parallelizable(ParallelScope.All)]
    public sealed class InMemorySharedKeyValueStoreTests
    {
        private static readonly ByteString s_valueA = new(new byte[] { 1, 2, 3 });
        private static readonly ByteString s_valueB = new(new byte[] { 4, 5, 6, 7 });

        [Test]
        public async Task TryGetReturnsFalseWhenKeyMissingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            (bool found, ByteString value) = await store.TryGetAsync("missing");

            Assert.That(found, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public async Task SetThenTryGetReturnsStoredValueAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            await store.SetAsync("key", s_valueA);
            (bool found, ByteString value) = await store.TryGetAsync("key");

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        [Test]
        public async Task SetOverwritesExistingValueAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            await store.SetAsync("key", s_valueA);
            await store.SetAsync("key", s_valueB);
            (bool found, ByteString value) = await store.TryGetAsync("key");

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueB.ToArray()));
        }

        [Test]
        public void TryGetWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(async () => await store.TryGetAsync(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void SetWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(async () => await store.SetAsync(null!, s_valueA), Throws.ArgumentNullException);
        }

        [Test]
        public async Task CompareAndSwapInsertsWhenAbsentAndExpectedNullAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            bool swapped = await store.CompareAndSwapAsync("key", default, s_valueA);

            Assert.That(swapped, Is.True);
            (bool found, ByteString value) = await store.TryGetAsync("key");
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        [Test]
        public async Task CompareAndSwapFailsWhenAbsentButExpectedNonNullAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            bool swapped = await store.CompareAndSwapAsync("key", s_valueA, s_valueB);

            Assert.That(swapped, Is.False);
            (bool found, ByteString _) = await store.TryGetAsync("key");
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task CompareAndSwapFailsWhenPresentButExpectedNullAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA);

            bool swapped = await store.CompareAndSwapAsync("key", default, s_valueB);

            Assert.That(swapped, Is.False);
            (bool _, ByteString value) = await store.TryGetAsync("key");
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        [Test]
        public async Task CompareAndSwapReplacesWhenExpectedMatchesAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA);

            bool swapped = await store.CompareAndSwapAsync("key", s_valueA, s_valueB);

            Assert.That(swapped, Is.True);
            (bool _, ByteString value) = await store.TryGetAsync("key");
            Assert.That(value.ToArray(), Is.EqualTo(s_valueB.ToArray()));
        }

        [Test]
        public async Task CompareAndSwapFailsWhenExpectedDiffersAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA);

            bool swapped = await store.CompareAndSwapAsync("key", s_valueB, s_valueA);

            Assert.That(swapped, Is.False);
            (bool _, ByteString value) = await store.TryGetAsync("key");
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        [Test]
        public void CompareAndSwapWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(
                async () => await store.CompareAndSwapAsync(null!, default, s_valueA),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task DeleteRemovesExistingKeyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA);

            bool removed = await store.DeleteAsync("key");

            Assert.That(removed, Is.True);
            (bool found, ByteString _) = await store.TryGetAsync("key");
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task DeleteReturnsFalseWhenKeyMissingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            bool removed = await store.DeleteAsync("missing");

            Assert.That(removed, Is.False);
        }

        [Test]
        public void DeleteWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(async () => await store.DeleteAsync(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task ScanReturnsOnlyEntriesMatchingPrefixAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("a/1", s_valueA);
            await store.SetAsync("a/2", s_valueB);
            await store.SetAsync("b/1", s_valueA);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync("a/"))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys, Does.Contain("a/1"));
            Assert.That(keys, Does.Contain("a/2"));
            Assert.That(keys, Does.Not.Contain("b/1"));
        }

        [Test]
        public async Task ScanWithNullPrefixReturnsAllEntriesAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("a", s_valueA);
            await store.SetAsync("b", s_valueB);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync(null!))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ScanHonorsCancellationAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("a", s_valueA);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () =>
                {
                    await foreach (KeyValuePair<string, ByteString> _ in store.ScanAsync("a", cts.Token))
                    {
                    }
                },
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task WatchObservesSetAndDeleteChangesAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var cts = new CancellationTokenSource();
            IAsyncEnumerator<KeyValueChange> changes = store
                .WatchAsync("k", cts.Token)
                .GetAsyncEnumerator(cts.Token);
            try
            {
                ValueTask<bool> firstMove = changes.MoveNextAsync();
                await store.SetAsync("k1", s_valueA);

                Assert.That(await firstMove, Is.True);
                Assert.That(changes.Current.Kind, Is.EqualTo(KeyValueChangeKind.Set));
                Assert.That(changes.Current.Key, Is.EqualTo("k1"));
                Assert.That(changes.Current.Value.ToArray(), Is.EqualTo(s_valueA.ToArray()));

                ValueTask<bool> secondMove = changes.MoveNextAsync();
                await store.DeleteAsync("k1");

                Assert.That(await secondMove, Is.True);
                Assert.That(changes.Current.Kind, Is.EqualTo(KeyValueChangeKind.Delete));
                Assert.That(changes.Current.Key, Is.EqualTo("k1"));
                Assert.That(changes.Current.Value.IsNull, Is.True);
            }
            finally
            {
                cts.Cancel();
                await changes.DisposeAsync();
            }
        }

        [Test]
        public async Task WatchIgnoresChangesOutsidePrefixAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var cts = new CancellationTokenSource();
            IAsyncEnumerator<KeyValueChange> changes = store
                .WatchAsync("watched/", cts.Token)
                .GetAsyncEnumerator(cts.Token);
            try
            {
                ValueTask<bool> move = changes.MoveNextAsync();
                await store.SetAsync("other/1", s_valueA);
                await store.SetAsync("watched/1", s_valueB);

                Assert.That(await move, Is.True);
                Assert.That(changes.Current.Key, Is.EqualTo("watched/1"));
                Assert.That(changes.Current.Value.ToArray(), Is.EqualTo(s_valueB.ToArray()));
            }
            finally
            {
                cts.Cancel();
                await changes.DisposeAsync();
            }
        }

        [Test]
        public async Task WatchStopsWhenTokenIsCanceledAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var cts = new CancellationTokenSource();
            IAsyncEnumerator<KeyValueChange> changes = store
                .WatchAsync("k", cts.Token)
                .GetAsyncEnumerator(cts.Token);
            try
            {
                ValueTask<bool> move = changes.MoveNextAsync();
                cts.Cancel();

                Assert.That(async () => await move, Throws.InstanceOf<OperationCanceledException>());
            }
            finally
            {
                await changes.DisposeAsync();
            }
        }

        [Test]
        public async Task DisposeCompletesOutstandingWatchersAsync()
        {
            var store = new InMemorySharedKeyValueStore();
            IAsyncEnumerator<KeyValueChange> changes = store
                .WatchAsync(string.Empty, CancellationToken.None)
                .GetAsyncEnumerator(CancellationToken.None);
            try
            {
                ValueTask<bool> move = changes.MoveNextAsync();
                store.Dispose();

                Assert.That(await move, Is.False);
            }
            finally
            {
                await changes.DisposeAsync();
            }
        }

        [Test]
        public async Task DisposeClearsStoredDataAsync()
        {
            var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA);

            store.Dispose();

            (bool found, ByteString _) = await store.TryGetAsync("key");
            Assert.That(found, Is.False);
        }
    }
}
