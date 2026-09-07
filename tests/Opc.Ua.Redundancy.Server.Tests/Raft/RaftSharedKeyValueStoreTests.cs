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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
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
        /// <summary>
        /// Verifies that a committed Raft store value can be retrieved.
        /// </summary>
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

        /// <summary>
        /// Verifies that reading a missing Raft store key returns false.
        /// </summary>
        [Test]
        public async Task TryGetMissingKeyReturnsFalseAsync()
        {
            await using var store = new RaftSharedKeyValueStore();

            (bool found, ByteString value) = await store.TryGetAsync("missing").ConfigureAwait(false);

            Assert.That(found, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        /// <summary>
        /// Verifies that compare-and-swap creates a Raft store entry when the key is absent.
        /// </summary>
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

        /// <summary>
        /// Verifies that compare-and-swap replaces an entry when the expected value matches.
        /// </summary>
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

        /// <summary>
        /// Verifies that compare-and-swap deletes a matching entry when the replacement is null.
        /// </summary>
        [Test]
        public async Task CompareAndSwapDeletesWhenReplacementIsNullAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var value = ByteString.From(new byte[] { 1 });
            await store.SetAsync("k", value, cts.Token).ConfigureAwait(false);
            await using IAsyncEnumerator<KeyValueChange> enumerator =
                store.WatchAsync("k", cts.Token).GetAsyncEnumerator();
            ValueTask<bool> next = enumerator.MoveNextAsync();

            bool deleted = await store.CompareAndSwapAsync(
                "k",
                value,
                default,
                cts.Token).ConfigureAwait(false);
            (bool found, _) = await store.TryGetAsync("k")
                .ConfigureAwait(false);

            Assert.That(deleted, Is.True);
            Assert.That(found, Is.False);
            Assert.That(await next.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Kind, Is.EqualTo(KeyValueChangeKind.Delete));
            Assert.That(enumerator.Current.Key, Is.EqualTo("k"));
        }

        /// <summary>
        /// Verifies that compare-and-swap fails when the expected value differs.
        /// </summary>
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

        /// <summary>
        /// Verifies that deleting a Raft store key removes its committed value.
        /// </summary>
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

        /// <summary>
        /// Verifies that a Raft store scan returns only keys matching the requested prefix.
        /// </summary>
        [Test]
        public async Task ScanReturnsMatchingPrefixOnlyAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
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

        /// <summary>
        /// Verifies that prefix watchers observe committed set and delete operations.
        /// </summary>
        [Test]
        public async Task WatchObservesSetAndDeleteForPrefixAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Pre-start the store so the watcher registers synchronously on the
            // first MoveNextAsync, before any mutation is proposed.
            await store.TryGetAsync("warmup", cts.Token).ConfigureAwait(false);

            await using IAsyncEnumerator<KeyValueChange> enumerator =
                store.WatchAsync("a/", cts.Token).GetAsyncEnumerator();

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

        /// <summary>
        /// Verifies that applying a read barrier does not emit a change notification.
        /// </summary>
        [Test]
        public async Task ReadBarrierDoesNotPublishWatchEventAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            await store.TryGetAsync("warmup", cts.Token).ConfigureAwait(false);
            await using IAsyncEnumerator<KeyValueChange> enumerator =
                store.WatchAsync(string.Empty, cts.Token).GetAsyncEnumerator();
            ValueTask<bool> next = enumerator.MoveNextAsync();

            await store.TryGetAsync("missing", cts.Token).ConfigureAwait(false);

            Assert.That(next.IsCompleted, Is.False);

            await store.SetAsync(
                "key",
                ByteString.From(new byte[] { 1 }),
                cts.Token).ConfigureAwait(false);
            Assert.That(await next.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Key, Is.EqualTo("key"));
        }

        /// <summary>
        /// Verifies that exactly one concurrent compare-and-swap wins for the same expected value.
        /// </summary>
        [Test]
        public async Task ConcurrentCompareAndSwapHasExactlyOneWinnerAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            const int contenders = 24;

            // Every contender races to create the same key from absent. Because
            // the consensus log is a single total order, exactly one wins.
            IEnumerable<Task<bool>> races = Enumerable.Range(0, contenders).Select(ii =>
                store.CompareAndSwapAsync("leader", default, ByteString.From(new[] { (byte)ii })).AsTask());
            bool[] results = await Task.WhenAll(races).ConfigureAwait(false);

            Assert.That(results.Count(won => won), Is.EqualTo(1), "exactly one compare-and-swap may win");
        }

        /// <summary>
        /// Verifies that replicas converge on the same shared key-value state.
        /// </summary>
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
            await store1.TryGetAsync("warmup", cts.Token).ConfigureAwait(false);
            await store2.TryGetAsync("warmup", cts.Token).ConfigureAwait(false);

            var payload = ByteString.From(new byte[] { 42 });
            await store1.SetAsync("shared", payload, cts.Token).ConfigureAwait(false);

            ByteString observed = await WaitForValueAsync(store2, "shared", cts.Token).ConfigureAwait(false);
            Assert.That(observed.ToArray(), Is.EqualTo(payload.ToArray()));
        }

        /// <summary>
        /// Verifies that a proposal times out when it is never committed.
        /// </summary>
        [Test]
        public void ProposalTimesOutWhenNoCommitOccurs()
        {
            // Regression: a proposal must never hang when there is no leader /
            // quorum to commit it; the commit timeout fails it instead.
            Assert.That(async () =>
            {
                await using var consensus = new NeverCommitsConsensus();
                await using var store = new RaftSharedKeyValueStore(
                    consensus, ownsConsensus: false, commitTimeout: TimeSpan.FromMilliseconds(200));
                await store.SetAsync("k", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            }, Throws.TypeOf<TimeoutException>());
        }

        /// <summary>
        /// Verifies that reading a value waits until its read barrier is applied.
        /// </summary>
        [Test]
        public async Task TryGetWaitsForReadBarrierToApplyAsync()
        {
            await using var consensus = new ControlledApplyConsensus();
            await using var store = new RaftSharedKeyValueStore(consensus);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var value = ByteString.From(new byte[] { 7 });

            Task setTask = store.SetAsync("key", value).AsTask();
            ReadOnlyMemory<byte> setCommand = await consensus.NextProposalAsync(cts.Token).ConfigureAwait(false);
            await consensus.CommitAsync(setCommand).ConfigureAwait(false);
            await setTask.ConfigureAwait(false);

            Task<(bool Found, ByteString Value)> readTask =
                store.TryGetAsync("key").AsTask();
            ReadOnlyMemory<byte> barrier = await consensus.NextProposalAsync(cts.Token).ConfigureAwait(false);

            Assert.That(readTask.IsCompleted, Is.False);

            await consensus.CommitAsync(barrier).ConfigureAwait(false);
            (bool found, ByteString observed) = await readTask.ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(observed.ToArray(), Is.EqualTo(value.ToArray()));
        }

        /// <summary>
        /// Verifies that scanning waits for promotion and application of the read barrier.
        /// </summary>
        [Test]
        public async Task ScanWaitsForPromotionAndReadBarrierToApplyAsync()
        {
            await using var consensus = new ControlledApplyConsensus();
            await using var store = new RaftSharedKeyValueStore(consensus);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            Task setTask = store.SetAsync(
                "prefix/key",
                ByteString.From(new byte[] { 9 })).AsTask();
            ReadOnlyMemory<byte> setCommand = await consensus.NextProposalAsync(cts.Token).ConfigureAwait(false);
            await consensus.CommitAsync(setCommand).ConfigureAwait(false);
            await setTask.ConfigureAwait(false);

            consensus.Demote();
            await using IAsyncEnumerator<KeyValuePair<string, ByteString>> enumerator =
                store.ScanAsync("prefix/").GetAsyncEnumerator();
            Task<bool> moveNextTask = enumerator.MoveNextAsync().AsTask();

            Assert.That(moveNextTask.IsCompleted, Is.False);

            consensus.Promote();
            ReadOnlyMemory<byte> barrier = await consensus.NextProposalAsync(cts.Token).ConfigureAwait(false);

            Assert.That(moveNextTask.IsCompleted, Is.False);

            await consensus.CommitAsync(barrier).ConfigureAwait(false);

            Assert.That(await moveNextTask.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Key, Is.EqualTo("prefix/key"));
        }

        /// <summary>
        /// Verifies that reading a value times out when its read barrier cannot commit.
        /// </summary>
        [Test]
        public void TryGetTimesOutWhenReadBarrierCannotCommit()
        {
            Assert.That(async () =>
            {
                await using var consensus = new NeverCommitsConsensus();
                await using var store = new RaftSharedKeyValueStore(
                    consensus,
                    ownsConsensus: false,
                    commitTimeout: TimeSpan.FromMilliseconds(200));
                await store.TryGetAsync("key").ConfigureAwait(false);
            }, Throws.TypeOf<TimeoutException>());
        }

        /// <summary>
        /// Verifies that consensus cancellation of a read barrier is reported as a timeout.
        /// </summary>
        [Test]
        public void ReadBarrierMapsConsensusCancellationToTimeout()
        {
            Assert.That(async () =>
            {
                await using var consensus =
                    new CancellationAwareNeverCommitsConsensus();
                await using var store = new RaftSharedKeyValueStore(
                    consensus,
                    ownsConsensus: false,
                    commitTimeout: TimeSpan.FromMilliseconds(200));
                await store.TryGetAsync("key").ConfigureAwait(false);
            }, Throws.TypeOf<TimeoutException>());
        }

        /// <summary>
        /// Verifies that store disposal completes pending watch enumeration.
        /// </summary>
        [Test]
        public async Task PendingWatchCompletesOnDisposeAsync()
        {
            var store = new RaftSharedKeyValueStore();
            await using IAsyncEnumerator<KeyValueChange> enumerator =
                store.WatchAsync(string.Empty).GetAsyncEnumerator();
            Task<bool> pending = enumerator.MoveNextAsync().AsTask();

            await store.DisposeAsync().ConfigureAwait(false);

            bool completed = false;
            try
            {
                completed = !await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                completed = true;
            }
            Assert.That(completed, Is.True);
        }

        /// <summary>
        /// Verifies that canceling a scan cancels its pending read barrier.
        /// </summary>
        [Test]
        public void ScanCancellationCancelsPendingReadBarrier()
        {
            Assert.That(async () =>
            {
                await using var consensus = new NeverCommitsConsensus();
                await using var store = new RaftSharedKeyValueStore(
                    consensus,
                    ownsConsensus: false,
                    commitTimeout: Timeout.InfiniteTimeSpan);
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                await foreach (KeyValuePair<string, ByteString> _ in store.ScanAsync(string.Empty, cts.Token))
                {
                }
            }, Throws.InstanceOf<OperationCanceledException>());
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

            public event Action<bool> LeadershipChanged
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

        private sealed class ControlledApplyConsensus : IRaftConsensus
        {
            public ControlledApplyConsensus()
            {
                m_canPropose.TrySetResult(true);
            }

            public bool IsLeader => m_isLeader;

            public event Action<bool> LeadershipChanged
            {
                add { }
                remove { }
            }

            public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public async ValueTask ProposeAsync(
                ReadOnlyMemory<byte> command,
                CancellationToken ct = default)
            {
                await m_canPropose.Task.ConfigureAwait(false);
                await m_proposed.Writer.WriteAsync(command.ToArray(), ct).ConfigureAwait(false);
            }

            public ValueTask CampaignAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask<ReadOnlyMemory<byte>> NextProposalAsync(
                CancellationToken ct = default)
            {
                return m_proposed.Reader.ReadAsync(ct);
            }

            public ValueTask CommitAsync(
                ReadOnlyMemory<byte> command,
                CancellationToken ct = default)
            {
                return m_committed.Writer.WriteAsync(command, ct);
            }

            public void Demote()
            {
                m_isLeader = false;
                m_canPropose = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void Promote()
            {
                m_isLeader = true;
                m_canPropose.TrySetResult(true);
            }

            public ValueTask DisposeAsync()
            {
                m_proposed.Writer.TryComplete();
                m_committed.Writer.TryComplete();
                return default;
            }

            private readonly Channel<ReadOnlyMemory<byte>> m_proposed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

            private readonly Channel<ReadOnlyMemory<byte>> m_committed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

            private TaskCompletionSource<bool> m_canPropose =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool m_isLeader = true;
        }

        private sealed class CancellationAwareNeverCommitsConsensus :
            IRaftConsensus
        {
            public bool IsLeader => true;

            public event Action<bool> LeadershipChanged
            {
                add { }
                remove { }
            }

            public ChannelReader<ReadOnlyMemory<byte>> Committed =>
                m_committed.Reader;

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public async ValueTask ProposeAsync(
                ReadOnlyMemory<byte> command,
                CancellationToken ct = default)
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
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
