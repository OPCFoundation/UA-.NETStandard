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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// The state of a <see cref="RaftSharedKeyValueStore"/> can be captured in
    /// a snapshot at an exact position of the committed command stream and a
    /// replica can start from it, so an application can bound the Raft log:
    /// snapshot, persist, compact the log, and on restart restore the snapshot
    /// before the remaining log is applied.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class RaftSharedKeyValueStoreSnapshotTests
    {
        [Test]
        public async Task SnapshotRestoresTheStateIntoANewStoreAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            await source.SetAsync("a", ByteString.From(new byte[] { 1 }));
            await source.SetAsync("b", ByteString.From(new byte[] { 2, 3 }));
            await source.SetAsync("c", ByteString.From(new byte[] { 4 }));
            await source.DeleteAsync("c");
            await source.SetAsync("empty", ByteString.From(Array.Empty<byte>()));
            await source.SetAsync("null", default);

            RaftKeyValueSnapshot snapshot = await source.CreateSnapshotAsync();

            await using var restored = new RaftSharedKeyValueStore();
            await restored.RestoreSnapshotAsync(snapshot.Data);

            Assert.That(await ReadAllAsync(restored), Is.EquivalentTo(await ReadAllAsync(source)));
            (bool found, _) = await restored.TryGetAsync("c");
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task SnapshotRecordsThePositionOfItsBarrierAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            await source.SetAsync("a", ByteString.From(new byte[] { 1 }));
            await source.SetAsync("b", ByteString.From(new byte[] { 2 }));
            await source.SetAsync("c", ByteString.From(new byte[] { 3 }));

            RaftKeyValueSnapshot first = await source.CreateSnapshotAsync();
            await source.SetAsync("d", ByteString.From(new byte[] { 4 }));
            RaftKeyValueSnapshot second = await source.CreateSnapshotAsync();

            Assert.Multiple(() =>
            {
                Assert.That(first.LastIncludedCommand, Is.EqualTo(4), "three sets and the barrier");
                Assert.That(second.LastIncludedCommand, Is.EqualTo(6), "one more set and the second barrier");
            });
        }

        [Test]
        public async Task SnapshotExcludesCommandsCommittedAfterItsBarrierAsync()
        {
            var consensus = new ScriptedConsensus(autoCommit: false);
            await using var store = new RaftSharedKeyValueStore(consensus, ownsConsensus: true);
            ValueTask early = store.SetAsync("early", ByteString.From(new byte[] { 1 }));
            await consensus.CommitAsync(1);
            await early;

            // The barrier and a later command are committed together, so the
            // applier applies the later command right after the barrier.
            ValueTask<RaftKeyValueSnapshot> snapshotTask = store.CreateSnapshotAsync();
            ValueTask late = store.SetAsync("late", ByteString.From(new byte[] { 2 }));
            await consensus.CommitAsync(2);
            RaftKeyValueSnapshot snapshot = await snapshotTask;
            await late;

            await using var restored = new RaftSharedKeyValueStore();
            await restored.RestoreSnapshotAsync(snapshot.Data);
            (bool earlyFound, _) = await restored.TryGetAsync("early");
            (bool lateFound, _) = await restored.TryGetAsync("late");

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.LastIncludedCommand, Is.EqualTo(2));
                Assert.That(earlyFound, Is.True);
                Assert.That(lateFound, Is.False, "a command committed after the barrier is not in the snapshot");
            });
        }

        [Test]
        public async Task CancelledSnapshotDoesNotHangAndTheStoreGoesOnAsync()
        {
            var consensus = new ScriptedConsensus(autoCommit: false);
            await using var store = new RaftSharedKeyValueStore(consensus, ownsConsensus: true);
            using var cancellation = new CancellationTokenSource();

            Task<RaftKeyValueSnapshot> snapshot = store.CreateSnapshotAsync(cancellation.Token).AsTask();
            await cancellation.CancelAsync();
            await consensus.CommitAsync(1);

            Assert.CatchAsync<OperationCanceledException>(
                async () => await snapshot.WaitAsync(TimeSpan.FromSeconds(10)));
            ValueTask<RaftKeyValueSnapshot> next = store.CreateSnapshotAsync();
            await consensus.CommitAsync(1);
            Assert.That((await next).LastIncludedCommand, Is.EqualTo(2));
        }

        [Test]
        public async Task SnapshotCancelledWhileItsBarrierIsAppliedCompletesAsync()
        {
            var consensus = new ScriptedConsensus(autoCommit: true);
            await using var store = new RaftSharedKeyValueStore(consensus, ownsConsensus: true);
            await store.SetAsync("a", ByteString.From(new byte[] { 1 }));

            // The barrier is committed and the token cancelled at once, so the
            // applier and the cancellation race for the proposal. Either side
            // may win, but the snapshot must always complete one way or the
            // other.
            for (int ii = 0; ii < 200; ii++)
            {
                using var cancellation = new CancellationTokenSource();
                consensus.AfterPropose = cancellation.Cancel;
                Task<RaftKeyValueSnapshot> snapshot = store.CreateSnapshotAsync(cancellation.Token).AsTask();
                Task finished = await Task.WhenAny(snapshot, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(finished, Is.SameAs(snapshot), "iteration " + ii + " did not complete");
                Assert.That(snapshot.Status, Is.EqualTo(TaskStatus.RanToCompletion).Or.EqualTo(TaskStatus.Canceled));
            }
            consensus.AfterPropose = null;
        }

        [Test]
        public async Task LogAppliedAfterTheSnapshotBuildsOnItAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            await source.SetAsync("counter", ByteString.From(new byte[] { 1 }));
            RaftKeyValueSnapshot snapshot = await source.CreateSnapshotAsync();

            await using var restored = new RaftSharedKeyValueStore();
            await restored.RestoreSnapshotAsync(snapshot.Data, RaftSnapshotLogReplay.AfterSnapshot);
            bool swapped = await restored.CompareAndSwapAsync(
                "counter",
                ByteString.From(new byte[] { 1 }),
                ByteString.From(new byte[] { 2 }));
            RaftKeyValueSnapshot next = await restored.CreateSnapshotAsync();

            (bool found, ByteString value) = await restored.TryGetAsync("counter");
            Assert.Multiple(() =>
            {
                Assert.That(swapped, Is.True);
                Assert.That(found, Is.True);
                Assert.That(value.ToArray(), Is.EqualTo(new byte[] { 2 }));
                Assert.That(
                    next.LastIncludedCommand,
                    Is.EqualTo(snapshot.LastIncludedCommand + 2),
                    "positions continue after the snapshot");
            });
        }

        [Test]
        public async Task ReplayedLogIsNotAppliedTwiceAsync()
        {
            var recording = new ScriptedConsensus(autoCommit: true);
            RaftKeyValueSnapshot snapshot;
            List<string> expected;
            List<ReadOnlyMemory<byte>> log;
            await using (var source = new RaftSharedKeyValueStore(recording, ownsConsensus: true))
            {
                // Applied once, this sequence leaves 2; applied again on top of
                // 2, the second CAS matches and leaves 3.
                await source.CompareAndSwapAsync("counter", default, ByteString.From(new byte[] { 1 }));
                await source.CompareAndSwapAsync(
                    "counter", ByteString.From(new byte[] { 2 }), ByteString.From(new byte[] { 3 }));
                await source.CompareAndSwapAsync(
                    "counter", ByteString.From(new byte[] { 1 }), ByteString.From(new byte[] { 2 }));
                snapshot = await source.CreateSnapshotAsync();
                await source.SetAsync("after", ByteString.From(new byte[] { 4 }));
                expected = await ReadAllAsync(source);
                log = recording.Log;
            }

            // A replica that restarts from its persisted, uncompacted log gets
            // every command again. The restored snapshot already holds the ones
            // up to its barrier; applying them again would replay the CAS chain.
            var replaying = new ScriptedConsensus(autoCommit: true, replay: log);
            await using var restored = new RaftSharedKeyValueStore(replaying, ownsConsensus: true);
            await restored.RestoreSnapshotAsync(snapshot.Data, RaftSnapshotLogReplay.FromFirstCommand);

            Assert.That(await ReadAllAsync(restored), Is.EquivalentTo(expected));
        }

        [Test]
        public async Task RestoreAfterTheStoreStartedIsRejectedAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            await source.SetAsync("a", ByteString.From(new byte[] { 1 }));
            RaftKeyValueSnapshot snapshot = await source.CreateSnapshotAsync();

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await source.RestoreSnapshotAsync(snapshot.Data));
        }

        [Test]
        public async Task RestoreWhileTheStoreIsStartingIsRejectedAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            RaftKeyValueSnapshot snapshot = await source.CreateSnapshotAsync();
            var consensus = new ScriptedConsensus(autoCommit: true, holdStart: true);
            await using var store = new RaftSharedKeyValueStore(consensus, ownsConsensus: true);

            // The first operation starts the store; its start is held open.
            ValueTask set = store.SetAsync("applied", ByteString.From(new byte[] { 1 }));
            await consensus.StartCalled;
            Task restore = store.RestoreSnapshotAsync(snapshot.Data).AsTask();
            consensus.CompleteStart();
            await set;

            Assert.ThrowsAsync<InvalidOperationException>(async () => await restore);
            (bool found, _) = await store.TryGetAsync("applied");
            Assert.That(found, Is.True, "the restore did not erase what the started store applied");
        }

        [Test]
        public async Task RestoreRejectsAnUndefinedReplayAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            RaftKeyValueSnapshot snapshot = await source.CreateSnapshotAsync();
            await using var store = new RaftSharedKeyValueStore();

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await store.RestoreSnapshotAsync(snapshot.Data, (RaftSnapshotLogReplay)7));
        }

        [TestCase("version", TestName = "CorruptSnapshotWithUnknownVersionIsRejected")]
        [TestCase("empty", TestName = "CorruptSnapshotThatIsEmptyIsRejected")]
        [TestCase("truncated", TestName = "CorruptSnapshotThatIsTruncatedIsRejected")]
        [TestCase("position", TestName = "CorruptSnapshotWithoutAPositionIsRejected")]
        [TestCase("utf8", TestName = "CorruptSnapshotWithInvalidUtf8KeyIsRejected")]
        [TestCase("boolean", TestName = "CorruptSnapshotWithNonCanonicalValueMarkerIsRejected")]
        [TestCase("nullPayload", TestName = "CorruptSnapshotWithPayloadOnNullValueIsRejected")]
        [TestCase("duplicate", TestName = "CorruptSnapshotWithDuplicateKeyIsRejected")]
        [TestCase("trailing", TestName = "CorruptSnapshotWithTrailingDataIsRejected")]
        public async Task CorruptSnapshotIsRejectedAsync(string corruption)
        {
            byte[] data = corruption switch
            {
                "version" => Encode(version: 0xFF, position: 1, ("a", 1, [1])),
                "empty" => [],
                "truncated" => Truncate(Encode(version: 1, position: 1, ("a", 1, [1]))),
                "position" => Encode(version: 1, position: 0, ("a", 1, [1])),
                "utf8" => Encode(version: 1, position: 1, keyBytes: [0xC3, 0x28], kind: 1, value: [1]),
                "boolean" => Encode(version: 1, position: 1, ("a", 2, [1])),
                "nullPayload" => Encode(version: 1, position: 1, ("a", 0, [1])),
                "duplicate" => Encode(version: 1, position: 1, ("a", 1, [1]), ("a", 1, [2])),
                "trailing" => [.. Encode(version: 1, position: 1, ("a", 1, [1])), 0],
                _ => throw new ArgumentOutOfRangeException(nameof(corruption))
            };
            await using var store = new RaftSharedKeyValueStore();

            Assert.ThrowsAsync<FormatException>(
                async () => await store.RestoreSnapshotAsync(ByteString.From(data)));
        }

        [Test]
        public async Task CanonicalSnapshotIsAcceptedAsync()
        {
            byte[] data = Encode(version: 1, position: 3, ("a", 1, [1]), ("b", 0, []));
            await using var store = new RaftSharedKeyValueStore();

            await store.RestoreSnapshotAsync(ByteString.From(data));
            (bool found, ByteString value) = await store.TryGetAsync("a");

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(new byte[] { 1 }));
        }

        private static byte[] Encode(byte version, long position, params (string Key, byte Kind, byte[] Value)[] entries)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(version);
                writer.Write(position);
                writer.Write(entries.Length);
                foreach ((string key, byte kind, byte[] value) in entries)
                {
                    byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                    writer.Write(keyBytes.Length);
                    writer.Write(keyBytes);
                    writer.Write(kind);
                    writer.Write(value.Length);
                    writer.Write(value);
                }
            }
            return stream.ToArray();
        }

        private static byte[] Encode(byte version, long position, byte[] keyBytes, byte kind, byte[] value)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(version);
                writer.Write(position);
                writer.Write(1);
                writer.Write(keyBytes.Length);
                writer.Write(keyBytes);
                writer.Write(kind);
                writer.Write(value.Length);
                writer.Write(value);
            }
            return stream.ToArray();
        }

        private static byte[] Truncate(byte[] data)
        {
            return data.AsSpan(0, data.Length - 1).ToArray();
        }

        private static async Task<List<string>> ReadAllAsync(RaftSharedKeyValueStore store)
        {
            var entries = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync(string.Empty))
            {
                entries.Add(entry.Key + "=" + (entry.Value.IsNull
                    ? "null"
                    : Convert.ToBase64String(entry.Value.ToArray() ?? [])));
            }
            return entries;
        }

        /// <summary>
        /// A single-node consensus whose commits, start and log are under the
        /// control of the test.
        /// </summary>
        private sealed class ScriptedConsensus : IRaftConsensus
        {
            public ScriptedConsensus(
                bool autoCommit,
                IEnumerable<ReadOnlyMemory<byte>>? replay = null,
                bool holdStart = false)
            {
                m_autoCommit = autoCommit;
                if (replay != null)
                {
                    foreach (ReadOnlyMemory<byte> command in replay)
                    {
                        m_committed.Writer.TryWrite(command);
                    }
                }
                if (!holdStart)
                {
                    m_start.TrySetResult(true);
                }
            }

            public event Action<bool>? LeadershipChanged
            {
                add { }
                remove { }
            }

            public bool IsLeader => true;

            public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

            public List<ReadOnlyMemory<byte>> Log
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_log];
                    }
                }
            }

            public Task StartCalled => m_startCalled.Task;

            public Action? AfterPropose { get; set; }

            public async ValueTask StartAsync(CancellationToken ct = default)
            {
                m_startCalled.TrySetResult(true);
                await m_start.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            public void CompleteStart()
            {
                m_start.TrySetResult(true);
            }

            public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
            {
                byte[] copy = command.ToArray();
                lock (m_lock)
                {
                    m_log.Add(copy);
                }
                if (m_autoCommit)
                {
                    m_committed.Writer.TryWrite(copy);
                }
                else
                {
                    m_proposed.Writer.TryWrite(copy);
                }
                AfterPropose?.Invoke();
                return default;
            }

            public async ValueTask CommitAsync(int count)
            {
                var batch = new List<ReadOnlyMemory<byte>>(count);
                for (int ii = 0; ii < count; ii++)
                {
                    batch.Add(await m_proposed.Reader.ReadAsync().ConfigureAwait(false));
                }
                foreach (ReadOnlyMemory<byte> command in batch)
                {
                    m_committed.Writer.TryWrite(command);
                }
            }

            public ValueTask CampaignAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask DisposeAsync()
            {
                m_committed.Writer.TryComplete();
                m_proposed.Writer.TryComplete();
                return default;
            }

            private readonly bool m_autoCommit;
            private readonly Lock m_lock = new();
            private readonly List<ReadOnlyMemory<byte>> m_log = [];
            private readonly Channel<ReadOnlyMemory<byte>> m_committed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            private readonly Channel<ReadOnlyMemory<byte>> m_proposed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
            private readonly TaskCompletionSource<bool> m_start =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_startCalled =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}