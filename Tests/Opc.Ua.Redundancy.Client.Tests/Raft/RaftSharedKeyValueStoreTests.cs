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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA1861: inline literal arrays here are one-shot test fixtures, not hot-path
//   allocations, so hoisting them to static readonly fields adds no value. Suppressed file-level.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Coverage for <see cref="RaftSharedKeyValueStore"/>: the linearizable CP store built on
    /// <see cref="IRaftConsensus"/> (set/get/CAS/delete/scan/watch, proposal bounding, disposal).
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class RaftSharedKeyValueStoreTests
    {
        [Test]
        public async Task SetAndTryGetReturnsStoredValueAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            var payload = ByteString.From(new byte[] { 1, 2, 3 });

            await store.SetAsync("k1", payload).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("k1").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        [Test]
        public async Task SetEmptyValueRoundTripsAsNonNullAsync()
        {
            await using var store = new RaftSharedKeyValueStore();

            await store.SetAsync("empty", ByteString.Empty).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("empty").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.IsNull, Is.False, "an empty value is stored and read back as a non-null empty string");
            Assert.That(value.ToArray(), Is.Empty);
        }

        [Test]
        public async Task TryGetMissingKeyReturnsFalseAsync()
        {
            await using var store = new RaftSharedKeyValueStore();

            (bool found, ByteString value) = await store.TryGetAsync("missing").ConfigureAwait(false);

            Assert.That(found, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public async Task TryGetNullKeyThrowsAsync()
        {
            await using var store = new RaftSharedKeyValueStore();

            Assert.That(
                async () => await store.TryGetAsync(null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task SetNullKeyThrowsAsync()
        {
            await using var store = new RaftSharedKeyValueStore();

            Assert.That(
                async () => await store.SetAsync(null!, ByteString.From(new byte[] { 1 })).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task CompareAndSwapCreatesWhenAbsentAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            var value = ByteString.From(new byte[] { 9 });

            bool created = await store.CompareAndSwapAsync("k", default, value).ConfigureAwait(false);
            bool createdAgain = await store.CompareAndSwapAsync("k", default, value).ConfigureAwait(false);

            Assert.That(created, Is.True);
            Assert.That(createdAgain, Is.False, "second create-if-absent must fail because the key now exists");
        }

        [Test]
        public async Task CompareAndSwapSwapsWhenValueMatchesAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
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
            await using var store = new RaftSharedKeyValueStore();
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
            await using var store = new RaftSharedKeyValueStore();
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
            await using var store = new RaftSharedKeyValueStore();
            await store.SetAsync("a/1", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            await store.SetAsync("a/2", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);
            await store.SetAsync("b/1", ByteString.From(new byte[] { 3 })).ConfigureAwait(false);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync("a/").ConfigureAwait(false))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Is.EquivalentTo(["a/1", "a/2"]));
        }

        [Test]
        public async Task ScanWithNullPrefixReturnsEverythingAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            await store.SetAsync("x", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            await store.SetAsync("y", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);

            var keys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync(null!).ConfigureAwait(false))
            {
                keys.Add(entry.Key);
            }

            Assert.That(keys, Is.EquivalentTo(["x", "y"]));
        }

        [Test]
        public async Task WatchObservesSetAndDeleteForPrefixAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Pre-start the store so the watcher registers synchronously on the
            // first MoveNextAsync, before any mutation is proposed.
            await store.TryGetAsync("warmup", cts.Token).ConfigureAwait(false);

            await using IAsyncEnumerator<KeyValueChange> enumerator =
                store.WatchAsync("a/", cts.Token).GetAsyncEnumerator(cts.Token);

            ValueTask<bool> first = enumerator.MoveNextAsync();
            await store.SetAsync("b/ignored", ByteString.From(new byte[] { 0 }), cts.Token).ConfigureAwait(false);
            await store.SetAsync("a/1", ByteString.From(new byte[] { 1 }), cts.Token).ConfigureAwait(false);

            Assert.That(await first.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Kind, Is.EqualTo(KeyValueChangeKind.Set));
            Assert.That(enumerator.Current.Key, Is.EqualTo("a/1"));

            ValueTask<bool> second = enumerator.MoveNextAsync();
            await store.DeleteAsync("a/1", cts.Token).ConfigureAwait(false);

            Assert.That(await second.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Kind, Is.EqualTo(KeyValueChangeKind.Delete));
            Assert.That(enumerator.Current.Key, Is.EqualTo("a/1"));
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
            // cluster) before writing, so the broadcast reaches both. The write
            // on store1 completes locally (originator matches) while store2
            // applies the same command with a non-matching originator.
            await store1.TryGetAsync("warmup", cts.Token).ConfigureAwait(false);
            await store2.TryGetAsync("warmup", cts.Token).ConfigureAwait(false);

            var payload = ByteString.From(new byte[] { 42 });
            await store1.SetAsync("shared", payload, cts.Token).ConfigureAwait(false);

            ByteString observed = await WaitForValueAsync(store2, "shared", cts.Token).ConfigureAwait(false);
            Assert.That(observed.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        [Test]
        public void ProposalTimesOutWhenNoCommitOccurs()
        {
            // A proposal must never hang when there is no leader / quorum to
            // commit it; the commit timeout fails it with a TimeoutException.
            Assert.That(async () =>
            {
                await using var consensus = new NeverCommitsConsensus();
                await using var store = new RaftSharedKeyValueStore(
                    consensus, ownsConsensus: false, commitTimeout: TimeSpan.FromMilliseconds(200));
                await store.SetAsync("k", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            }, Throws.TypeOf<TimeoutException>());
        }

        [Test]
        public async Task ProposalCanceledByCallerTokenThrowsAsync()
        {
            await using var consensus = new NeverCommitsConsensus();
            await using var store = new RaftSharedKeyValueStore(
                consensus, ownsConsensus: false, commitTimeout: Timeout.InfiniteTimeSpan);

            // Warm the store up so EnsureStartedAsync is a no-op and the
            // pre-canceled token bounds the pending proposal via its own path.
            await store.TryGetAsync("warmup").ConfigureAwait(false);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await store.SetAsync("k", ByteString.From(new byte[] { 1 }), cts.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task ProposePropagatesConsensusFailureAsync()
        {
            await using var consensus = new ThrowingProposeConsensus();
            await using var store = new RaftSharedKeyValueStore(consensus, ownsConsensus: false);

            Assert.That(
                async () => await store.SetAsync("k", ByteString.From(new byte[] { 1 })).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task PendingProposalCanceledOnDisposeAsync()
        {
            var consensus = new NeverCommitsConsensus();
            var store = new RaftSharedKeyValueStore(
                consensus, ownsConsensus: true, commitTimeout: Timeout.InfiniteTimeSpan);

            await store.TryGetAsync("warmup").ConfigureAwait(false);

            // The proposal registers a pending completion synchronously (before
            // the first real await), so disposing the store cancels it.
            Task pending = store.SetAsync("k", ByteString.From(new byte[] { 1 })).AsTask();
            await store.DisposeAsync().ConfigureAwait(false);

            Assert.That(async () => await pending.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task DisposeIsIdempotentAsync()
        {
            var store = new RaftSharedKeyValueStore();
            await store.SetAsync("k", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);

            await store.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await store.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

        private static async Task<ByteString> WaitForValueAsync(
            RaftSharedKeyValueStore store,
            string key,
            CancellationToken ct)
        {
            while (true)
            {
                (bool found, ByteString value) = await store.TryGetAsync(key, ct).ConfigureAwait(false);
                if (found)
                {
                    return value;
                }
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A consensus replica that accepts proposals but never commits them (it never yields on
        /// <see cref="IRaftConsensus.Committed"/>), modelling a no-leader / lost-quorum window.
        /// </summary>
        private sealed class NeverCommitsConsensus : IRaftConsensus
        {
            public bool IsLeader => true;

            public event Action<bool>? LeadershipChanged
            {
                add { }
                remove { }
            }

            public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask CampaignAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask DisposeAsync()
            {
                m_committed.Writer.TryComplete();
                return default;
            }

            private readonly Channel<ReadOnlyMemory<byte>> m_committed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        }

        /// <summary>
        /// A consensus replica whose <see cref="IRaftConsensus.ProposeAsync"/> always faults, so the store must
        /// propagate the failure and drop the pending proposal.
        /// </summary>
        private sealed class ThrowingProposeConsensus : IRaftConsensus
        {
            public bool IsLeader => true;

            public event Action<bool>? LeadershipChanged
            {
                add { }
                remove { }
            }

            public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
            {
                throw new InvalidOperationException("propose failed");
            }

            public ValueTask CampaignAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask DisposeAsync()
            {
                m_committed.Writer.TryComplete();
                return default;
            }

            private readonly Channel<ReadOnlyMemory<byte>> m_committed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        }
    }
}
