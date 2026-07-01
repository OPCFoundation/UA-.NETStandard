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

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="InMemorySharedKeyValueStore"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class InMemorySharedKeyValueStoreTests
    {
        [Test]
        public async Task SetAndTryGetReturnsStoredValueAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var payload = ByteString.From(new byte[] { 1, 2, 3 });

            await store.SetAsync("k1", payload).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("k1").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        [Test]
        public async Task TryGetMissingKeyReturnsFalseAsync()
        {
            using var store = new InMemorySharedKeyValueStore();

            (bool found, ByteString value) = await store.TryGetAsync("missing").ConfigureAwait(false);

            Assert.That(found, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public async Task CompareAndSwapCreatesWhenAbsentAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var value = ByteString.From(new byte[] { 9 });

            bool created = await store.CompareAndSwapAsync("k", default, value).ConfigureAwait(false);
            bool createdAgain = await store.CompareAndSwapAsync("k", default, value).ConfigureAwait(false);

            Assert.That(created, Is.True);
            Assert.That(createdAgain, Is.False, "second create-if-absent must fail because the key now exists");
        }

        [Test]
        public async Task CompareAndSwapSwapsWhenValueMatchesAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var first = ByteString.From(new byte[] { 1 });
            var second = ByteString.From(new byte[] { 2 });
            await store.SetAsync("k", first).ConfigureAwait(false);

            bool swapped = await store.CompareAndSwapAsync("k", first, second).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("k").ConfigureAwait(false);

            Assert.That(swapped, Is.True);
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(second.ToArray()));
        }

        [Test]
        public async Task CompareAndSwapFailsWhenValueMismatchAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var actual = ByteString.From(new byte[] { 1 });
            var wrongExpected = ByteString.From(new byte[] { 7 });
            var desired = ByteString.From(new byte[] { 2 });
            await store.SetAsync("k", actual).ConfigureAwait(false);

            bool swapped = await store.CompareAndSwapAsync("k", wrongExpected, desired).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("k").ConfigureAwait(false);

            Assert.That(swapped, Is.False);
            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(actual.ToArray()), "value must be unchanged on a failed CAS");
        }

        [Test]
        public async Task DeleteRemovesKeyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("k", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);

            bool deleted = await store.DeleteAsync("k").ConfigureAwait(false);
            bool deletedAgain = await store.DeleteAsync("k").ConfigureAwait(false);
            (bool found, _) = await store.TryGetAsync("k").ConfigureAwait(false);

            Assert.That(deleted, Is.True);
            Assert.That(deletedAgain, Is.False);
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task ScanReturnsMatchingPrefixOnlyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            await store.SetAsync("a/1", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            await store.SetAsync("a/2", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);
            await store.SetAsync("b/1", ByteString.From(new byte[] { 3 })).ConfigureAwait(false);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync("a/"))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Is.EquivalentTo(["a/1", "a/2"]));
        }

        [Test]
        public async Task WatchObservesSetAndDeleteForPrefixAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var cts = new CancellationTokenSource();

            // Driving the enumerator manually guarantees the watcher is
            // registered (synchronous prefix of the iterator runs on the
            // first MoveNextAsync) before any mutation is published — no
            // delays, no flakiness.
            await using IAsyncEnumerator<KeyValueChange> enumerator =
                store.WatchAsync("a/", cts.Token).GetAsyncEnumerator();

            ValueTask<bool> first = enumerator.MoveNextAsync();
            await store.SetAsync("b/ignored", ByteString.From(new byte[] { 0 })).ConfigureAwait(false);
            await store.SetAsync("a/1", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);

            Assert.That(await first.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Kind, Is.EqualTo(KeyValueChangeKind.Set));
            Assert.That(enumerator.Current.Key, Is.EqualTo("a/1"));

            ValueTask<bool> second = enumerator.MoveNextAsync();
            await store.DeleteAsync("a/1").ConfigureAwait(false);

            Assert.That(await second.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Kind, Is.EqualTo(KeyValueChangeKind.Delete));
            Assert.That(enumerator.Current.Key, Is.EqualTo("a/1"));

            cts.Cancel();
        }
    }
}
