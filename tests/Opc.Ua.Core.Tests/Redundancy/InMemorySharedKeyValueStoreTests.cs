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

        /// <summary>
        /// Verifies that a missing key returns false and a null byte string.
        /// </summary>
        [Test]
        public async Task TryGetReturnsFalseWhenKeyMissingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            (bool found, ByteString value) = await store.TryGetAsync("missing").ConfigureAwait(false);

            Assert.That(found, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        /// <summary>
        /// Verifies that a value can be retrieved after it is stored.
        /// </summary>
        [Test]
        public async Task SetThenTryGetReturnsStoredValueAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            await store.SetAsync("key", s_valueA).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("key").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        /// <summary>
        /// Verifies that setting an existing key replaces its stored value.
        /// </summary>
        [Test]
        public async Task SetOverwritesExistingValueAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            await store.SetAsync("key", s_valueA).ConfigureAwait(false);
            await store.SetAsync("key", s_valueB).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("key").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueB.ToArray()));
        }

        /// <summary>
        /// Verifies that reading a null key throws ArgumentNullException.
        /// </summary>
        [Test]
        public void TryGetWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(async () => await store.TryGetAsync(null!).ConfigureAwait(false), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Verifies that setting a null key throws ArgumentNullException.
        /// </summary>
        [Test]
        public void SetWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(async () => await store.SetAsync(null!, s_valueA).ConfigureAwait(false), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Verifies that compare-and-swap inserts an absent key when the expected value is null.
        /// </summary>
        [Test]
        public async Task CompareAndSwapInsertsWhenAbsentAndExpectedNullAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            bool swapped = await store.CompareAndSwapAsync("key", default, s_valueA).ConfigureAwait(false);

            Assert.That(swapped, Is.True);
            (bool found, ByteString value) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        /// <summary>
        /// Verifies that compare-and-swap fails for an absent key when a non-null value is expected.
        /// </summary>
        [Test]
        public async Task CompareAndSwapFailsWhenAbsentButExpectedNonNullAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            bool swapped = await store.CompareAndSwapAsync("key", s_valueA, s_valueB).ConfigureAwait(false);

            Assert.That(swapped, Is.False);
            (bool found, ByteString _) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        /// <summary>
        /// Verifies that compare-and-swap fails for an existing key when absence is expected.
        /// </summary>
        [Test]
        public async Task CompareAndSwapFailsWhenPresentButExpectedNullAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA).ConfigureAwait(false);

            bool swapped = await store.CompareAndSwapAsync("key", default, s_valueB).ConfigureAwait(false);

            Assert.That(swapped, Is.False);
            (bool _, ByteString value) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        /// <summary>
        /// Verifies that compare-and-swap replaces a value only when the expected value matches.
        /// </summary>
        [Test]
        public async Task CompareAndSwapReplacesWhenExpectedMatchesAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA).ConfigureAwait(false);

            bool swapped = await store.CompareAndSwapAsync("key", s_valueA, s_valueB).ConfigureAwait(false);

            Assert.That(swapped, Is.True);
            (bool _, ByteString value) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueB.ToArray()));
        }

        /// <summary>
        /// Verifies that compare-and-swap deletes a matching entry when the replacement is null.
        /// </summary>
        [Test]
        public async Task CompareAndSwapDeletesWhenReplacementIsNullAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA).ConfigureAwait(false);

            bool swapped = await store.CompareAndSwapAsync(
                "key",
                s_valueA,
                default).ConfigureAwait(false);

            Assert.That(swapped, Is.True);
            (bool found, _) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        /// <summary>
        /// Verifies that compare-and-swap preserves an entry when the expected value differs.
        /// </summary>
        [Test]
        public async Task CompareAndSwapFailsWhenExpectedDiffersAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA).ConfigureAwait(false);

            bool swapped = await store.CompareAndSwapAsync("key", s_valueB, s_valueA).ConfigureAwait(false);

            Assert.That(swapped, Is.False);
            (bool _, ByteString value) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(value.ToArray(), Is.EqualTo(s_valueA.ToArray()));
        }

        /// <summary>
        /// Verifies that compare-and-swap rejects a null key.
        /// </summary>
        [Test]
        public void CompareAndSwapWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(
                async () => await store.CompareAndSwapAsync(null!, default, s_valueA).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Verifies that deletion removes an existing key.
        /// </summary>
        [Test]
        public async Task DeleteRemovesExistingKeyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA).ConfigureAwait(false);

            bool removed = await store.DeleteAsync("key").ConfigureAwait(false);

            Assert.That(removed, Is.True);
            (bool found, ByteString _) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        /// <summary>
        /// Verifies that deleting a missing key returns false.
        /// </summary>
        [Test]
        public async Task DeleteReturnsFalseWhenKeyMissingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            bool removed = await store.DeleteAsync("missing").ConfigureAwait(false);

            Assert.That(removed, Is.False);
        }

        /// <summary>
        /// Verifies that deletion rejects a null key.
        /// </summary>
        [Test]
        public void DeleteWithNullKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(async () => await store.DeleteAsync(null!).ConfigureAwait(false), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Verifies that scanning returns only entries whose keys match the requested prefix.
        /// </summary>
        [Test]
        public async Task ScanReturnsOnlyEntriesMatchingPrefixAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("a/1", s_valueA).ConfigureAwait(false);
            await store.SetAsync("a/2", s_valueB).ConfigureAwait(false);
            await store.SetAsync("b/1", s_valueA).ConfigureAwait(false);

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

        /// <summary>
        /// Verifies that scanning with a null prefix returns all stored entries.
        /// </summary>
        [Test]
        public async Task ScanWithNullPrefixReturnsAllEntriesAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("a", s_valueA).ConfigureAwait(false);
            await store.SetAsync("b", s_valueB).ConfigureAwait(false);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync(null!))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies that scanning observes cancellation.
        /// </summary>
        [Test]
        public async Task ScanHonorsCancellationAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("a", s_valueA).ConfigureAwait(false);
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

        /// <summary>
        /// Verifies that a watcher receives both set and delete notifications.
        /// </summary>
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
                await store.SetAsync("k1", s_valueA).ConfigureAwait(false);

                Assert.That(await firstMove.ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Kind, Is.EqualTo(KeyValueChangeKind.Set));
                Assert.That(changes.Current.Key, Is.EqualTo("k1"));
                Assert.That(changes.Current.Value.ToArray(), Is.EqualTo(s_valueA.ToArray()));

                ValueTask<bool> secondMove = changes.MoveNextAsync();
                await store.DeleteAsync("k1").ConfigureAwait(false);

                Assert.That(await secondMove.ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Kind, Is.EqualTo(KeyValueChangeKind.Delete));
                Assert.That(changes.Current.Key, Is.EqualTo("k1"));
                Assert.That(changes.Current.Value.IsNull, Is.True);
            }
            finally
            {
                cts.Cancel();
                await changes.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a watcher ignores changes outside its requested prefix.
        /// </summary>
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
                await store.SetAsync("other/1", s_valueA).ConfigureAwait(false);
                await store.SetAsync("watched/1", s_valueB).ConfigureAwait(false);

                Assert.That(await move.ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Key, Is.EqualTo("watched/1"));
                Assert.That(changes.Current.Value.ToArray(), Is.EqualTo(s_valueB.ToArray()));
            }
            finally
            {
                cts.Cancel();
                await changes.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that canceling a watch token stops its enumeration.
        /// </summary>
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

                Assert.That(async () => await move.ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
            }
            finally
            {
                await changes.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that disposing the store completes outstanding watchers.
        /// </summary>
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

                Assert.That(await move.ConfigureAwait(false), Is.False);
            }
            finally
            {
                await changes.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that disposing the store clears its stored data.
        /// </summary>
        [Test]
        public async Task DisposeClearsStoredDataAsync()
        {
            var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("key", s_valueA).ConfigureAwait(false);

            store.Dispose();

            (bool found, ByteString _) = await store.TryGetAsync("key").ConfigureAwait(false);
            Assert.That(found, Is.False);
        }
    }
}
