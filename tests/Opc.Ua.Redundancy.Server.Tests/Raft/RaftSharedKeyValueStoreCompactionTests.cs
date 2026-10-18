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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Raft;
using Raft.Configuration;
using Raft.Storage;
using Raft.Storage.File;
using Raft.Transport;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// A <see cref="RaftSharedKeyValueStore"/> over the RaftCs engine bounds the Raft log: it snapshots its state
    /// into the engine, which discards the log behind the snapshot, sends the snapshot to a lagging replica, and
    /// hands it back to the store after a restart, followed by the log after it.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class RaftSharedKeyValueStoreCompactionTests
    {
        [Test]
        public async Task CompactionBoundsTheRaftLogAsync()
        {
            await using var network = new InMemoryNetwork();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var storage = new MemoryStorage(new ConfState(new ulong[] { 1 }));
            var store = new RaftSharedKeyValueStore(
                CreateSingleNode(network, storage), true, TimeSpan.Zero, compactionInterval: 10);
            try
            {
                for (int ii = 0; ii < 35; ii++)
                {
                    await store.SetAsync(Key(ii), Value(ii), cts.Token);
                }

                // The leader's empty entry is index 1, so the 30th command is index 31.
                await WaitUntilAsync(() => storage.FirstIndex() > 31, cts.Token);
                Snapshot? snapshot = storage.LatestSnapshot();
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot!.IsEmpty, Is.False);
                Assert.That(storage.FirstIndex(), Is.EqualTo(snapshot.Metadata.Index + 1));
                Assert.That(store.AppliedIndex, Is.GreaterThanOrEqualTo(36UL));
                for (int ii = 0; ii < 35; ii++)
                {
                    (bool found, ByteString value) = await store.TryGetAsync(Key(ii), cts.Token);
                    Assert.That(found, Is.True);
                    Assert.That(value.ToArray(), Is.EqualTo(Value(ii).ToArray()));
                }
            }
            finally
            {
                await store.DisposeAsync();
            }
        }

        [Test]
        public async Task LaggingReplicaInstallsTheLeaderSnapshotAsync()
        {
            await using var network = new InMemoryNetwork();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            ulong[] ids = [1, 2, 3];
            var storages = new MemoryStorage[ids.Length];
            var nodes = new DefaultRaftConsensus[ids.Length];
            var stores = new RaftSharedKeyValueStore[ids.Length];
            for (int ii = 0; ii < ids.Length; ii++)
            {
                storages[ii] = new MemoryStorage(new ConfState(ids));
                nodes[ii] = DefaultRaftConsensus.CreateCluster(
                    ids[ii],
                    network.CreateNode(ids[ii]),
                    storages[ii],
                    new RaftNodeOptions { TickInterval = TimeSpan.FromMilliseconds(10) },
                    config =>
                    {
                        config.ElectionTick = 10;
                        config.HeartbeatTick = 1;
                        config.PreVote = true;
                        config.RandomizedElectionTimeout = (int)(ids[ii] * 6);
                    },
                    readyTimeout: TimeSpan.FromSeconds(25));
            }

            try
            {
                await Task.WhenAll(Array.ConvertAll(nodes, n => n.StartAsync(cts.Token).AsTask()));
                int leader = await WaitForLeaderAsync(nodes, cts.Token);
                int lagging = leader == ids.Length - 1 ? 0 : ids.Length - 1;
                for (int ii = 0; ii < ids.Length; ii++)
                {
                    // The lagging replica never compacts, so a snapshot in its storage is the leader's.
                    stores[ii] = new RaftSharedKeyValueStore(
                        nodes[ii],
                        false,
                        TimeSpan.FromSeconds(20),
                        compactionInterval: ii == lagging ? 0 : 5);
                }

                // Watch the lagging replica; a change it applies itself proves the watcher is registered.
                var expected = new HashSet<string>();
                for (int ii = 0; ii < 20; ii++)
                {
                    expected.Add(Key(ii));
                }
                var watched = new HashSet<string>();
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task watch = WatchAsync(stores[lagging], watched, expected, ready, cts.Token);
                for (int ii = 0; !ready.Task.IsCompleted; ii++)
                {
                    await stores[lagging].SetAsync("k-ready", Value(ii), cts.Token);
                    await Task.WhenAny(ready.Task, Task.Delay(100, cts.Token));
                }

                // Cut the replica off, commit on the other two, and let them compact behind it.
                network.SetPartition([ids[lagging]]);
                for (int ii = 0; ii < 20; ii++)
                {
                    await stores[leader].SetAsync(Key(ii), Value(ii), cts.Token);
                }
                await WaitUntilAsync(() => storages[leader].FirstIndex() > 20, cts.Token);
                network.Heal();

                for (int ii = 0; ii < 20; ii++)
                {
                    ByteString value = await WaitForValueAsync(stores[lagging], Key(ii), cts.Token);
                    Assert.That(value.ToArray(), Is.EqualTo(Value(ii).ToArray()));
                }
                Snapshot? installed = storages[lagging].LatestSnapshot();
                Assert.That(installed, Is.Not.Null);
                Assert.That(installed!.IsEmpty, Is.False);
                Assert.That(stores[lagging].AppliedIndex, Is.GreaterThan(installed.Metadata.Index));

                // The watcher of the lagging replica sees every key the snapshot brought in.
                await watch;
                Assert.That(watched.IsSupersetOf(expected), Is.True);

                // The installed snapshot carries the command position, so the replicas keep numbering the
                // command stream alike.
                RaftKeyValueSnapshot onLeader = await stores[leader].CreateSnapshotAsync(cts.Token);
                RaftKeyValueSnapshot onLagging = await stores[lagging].CreateSnapshotAsync(cts.Token);
                Assert.That(onLagging.LastIncludedCommand, Is.EqualTo(onLeader.LastIncludedCommand + 1));
            }
            finally
            {
                foreach (RaftSharedKeyValueStore store in stores)
                {
                    if (store != null)
                    {
                        await store.DisposeAsync();
                    }
                }
                foreach (DefaultRaftConsensus node in nodes)
                {
                    await node.DisposeAsync();
                }
            }
        }

        [Test]
        public async Task RestartRestoresTheStateFromTheSnapshotAndTheLogAsync()
        {
            string directory = NewDirectory();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                long positionBeforeRestart;
                await using (var network = new InMemoryNetwork())
                {
                    using var storage = OpenStorage(directory, initialize: true);
                    await using var store = new RaftSharedKeyValueStore(
                        CreateSingleNode(network, storage), true, TimeSpan.Zero, compactionInterval: 5);
                    for (int ii = 0; ii < 12; ii++)
                    {
                        await store.SetAsync(Key(ii), Value(ii), cts.Token);
                    }

                    // The 10th command is index 11: the snapshot covers it, the log holds the rest.
                    await WaitUntilAsync(() => storage.FirstIndex() > 11, cts.Token);
                    positionBeforeRestart = (await store.CreateSnapshotAsync(cts.Token)).LastIncludedCommand;
                }

                await using (var network = new InMemoryNetwork())
                {
                    using var storage = OpenStorage(directory, initialize: false);
                    Snapshot? snapshot = storage.LatestSnapshot();
                    Assert.That(snapshot, Is.Not.Null);
                    Assert.That(snapshot!.IsEmpty, Is.False);
                    Assert.That(storage.FirstIndex(), Is.GreaterThan(11UL));
                    await using var store = new RaftSharedKeyValueStore(
                        CreateSingleNode(network, storage), true, TimeSpan.Zero, compactionInterval: 5);

                    // The engine's snapshot carries its command position: the commands after it continue the
                    // numbering of the store before the restart.
                    RaftKeyValueSnapshot afterRestart = await store.CreateSnapshotAsync(cts.Token);
                    Assert.That(positionBeforeRestart, Is.EqualTo(13L), "twelve sets and the barrier");
                    Assert.That(afterRestart.LastIncludedCommand, Is.EqualTo(positionBeforeRestart + 1));
                    for (int ii = 0; ii < 12; ii++)
                    {
                        (bool found, ByteString value) = await store.TryGetAsync(Key(ii), cts.Token);
                        Assert.That(found, Is.True, Key(ii));
                        Assert.That(value.ToArray(), Is.EqualTo(Value(ii).ToArray()));
                    }
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Test]
        public async Task RestoredSnapshotAndAnOlderEngineSnapshotCombineAsync()
        {
            string directory = NewDirectory();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                RaftKeyValueSnapshot restored;
                await using (var network = new InMemoryNetwork())
                {
                    using var storage = OpenStorage(directory, initialize: true);
                    await using var store = new RaftSharedKeyValueStore(
                        CreateSingleNode(network, storage), true, TimeSpan.Zero, compactionInterval: 10);
                    for (int ii = 0; ii < 10; ii++)
                    {
                        await store.SetAsync(Key(ii), Value(ii), cts.Token);
                    }

                    // The engine snapshot is at the 10th command; "counter" is created and swapped after it.
                    await WaitUntilAsync(() => storage.LatestSnapshot() is { IsEmpty: false }, cts.Token);
                    await store.SetAsync("counter", Value(1), cts.Token);
                    Assert.That(await store.CompareAndSwapAsync("counter", Value(1), Value(2), cts.Token), Is.True);
                    restored = await store.CreateSnapshotAsync(cts.Token);
                }
                Assert.That(restored.LastIncludedCommand, Is.EqualTo(13L));

                await using (var network = new InMemoryNetwork())
                {
                    using var storage = OpenStorage(directory, initialize: false);
                    await using var store = new RaftSharedKeyValueStore(
                        CreateSingleNode(network, storage), true, TimeSpan.Zero, compactionInterval: 10);
                    await store.RestoreSnapshotAsync(restored.Data, RaftSnapshotLogReplay.FromFirstCommand, cts.Token);

                    // The engine delivers its snapshot, older than the restored one, and the log after it: the
                    // store ends at the state of every command, each applied once, and numbers them alike.
                    (bool found, ByteString value) = await store.TryGetAsync("counter", cts.Token);
                    Assert.That(found, Is.True);
                    Assert.That(value.ToArray(), Is.EqualTo(Value(2).ToArray()));
                    Assert.That((await store.CreateSnapshotAsync(cts.Token)).LastIncludedCommand, Is.EqualTo(15L));
                    for (int ii = 0; ii < 10; ii++)
                    {
                        (found, _) = await store.TryGetAsync(Key(ii), cts.Token);
                        Assert.That(found, Is.True, Key(ii));
                    }
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Test]
        public async Task EngineSnapshotNewerThanTheRestoredSnapshotReplacesItAsync()
        {
            var consensus = new ScriptedSnapshotConsensus();
            await using var source = new RaftSharedKeyValueStore(consensus, false, TimeSpan.Zero, compactionInterval: 3);
            await source.SetAsync("a", Value(1));
            RaftKeyValueSnapshot older = await source.CreateSnapshotAsync();
            await source.SetAsync("b", Value(2));
            await consensus.WaitForCompactionAsync();
            (ulong index, byte[] engineSnapshot) = consensus.Compactions[0];

            var target = new ScriptedSnapshotConsensus();
            await using var store = new RaftSharedKeyValueStore(target, false, TimeSpan.Zero, compactionInterval: 0);
            await store.RestoreSnapshotAsync(older.Data, RaftSnapshotLogReplay.AfterSnapshot);
            var changes = new List<KeyValueChange>();
            using var watching = new CancellationTokenSource();
            Task watch = CollectAsync(store, changes, 1, watching.Token);
            await target.DeliverSnapshotAsync(index, engineSnapshot);
            await watch.WaitAsync(TimeSpan.FromSeconds(10));

            (bool found, _) = await store.TryGetAsync("b");
            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(changes[0].Key, Is.EqualTo("b"));
                Assert.That(store.AppliedIndex, Is.GreaterThan(index));
            });
            Assert.That((await store.CreateSnapshotAsync()).LastIncludedCommand, Is.EqualTo(5L));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CompactionThatFailsIsRetriedAtTheNextIntervalAsync(bool cancelledByTheConsensus)
        {
            var consensus = new ScriptedSnapshotConsensus { FailCompactions = 1 };
            if (cancelledByTheConsensus)
            {
                // A cancellation the store did not request.
                consensus.CompactionFailure = () => new OperationCanceledException();
            }
            await using var store = new RaftSharedKeyValueStore(consensus, false, TimeSpan.Zero, compactionInterval: 3);
            for (int ii = 0; ii < 6; ii++)
            {
                await store.SetAsync(Key(ii), Value(ii));
            }
            await consensus.WaitForCompactionAsync();

            // The compaction after the third command failed; the commands kept applying and the next interval compacted.
            Assert.Multiple(() =>
            {
                Assert.That(consensus.FailedCompactions, Is.EqualTo(1));
                Assert.That(consensus.Compactions, Has.Count.EqualTo(1));
                Assert.That(consensus.Compactions[0].Index, Is.EqualTo(6UL));
            });

            var target = new ScriptedSnapshotConsensus();
            await using var restored = new RaftSharedKeyValueStore(target, false, TimeSpan.Zero, compactionInterval: 0);
            await target.DeliverSnapshotAsync(consensus.Compactions[0].Index, consensus.Compactions[0].Snapshot);
            for (int ii = 0; ii < 6; ii++)
            {
                (bool found, ByteString value) = await restored.TryGetAsync(Key(ii));
                Assert.That(found, Is.True, Key(ii));
                Assert.That(value.ToArray(), Is.EqualTo(Value(ii).ToArray()));
            }
        }

        [Test]
        public async Task EngineSnapshotReportsAValueThatChangesBetweenNullAndEmptyAsync()
        {
            var consensus = new ScriptedSnapshotConsensus();
            await using var source = new RaftSharedKeyValueStore(consensus, false, TimeSpan.Zero, compactionInterval: 2);
            await source.SetAsync("toNull", default(ByteString));
            await source.SetAsync("toEmpty", ByteString.From(Array.Empty<byte>()));
            await consensus.WaitForCompactionAsync();
            (ulong index, byte[] engineSnapshot) = consensus.Compactions[0];

            var target = new ScriptedSnapshotConsensus();
            await using var store = new RaftSharedKeyValueStore(target, false, TimeSpan.Zero, compactionInterval: 0);
            await store.SetAsync("toNull", ByteString.From(Array.Empty<byte>()));
            await store.SetAsync("toEmpty", default(ByteString));
            var changes = new List<KeyValueChange>();
            using var watching = new CancellationTokenSource();
            Task watch = CollectAsync(store, changes, 2, watching.Token);
            await target.DeliverSnapshotAsync(index, engineSnapshot);
            await watch.WaitAsync(TimeSpan.FromSeconds(10));

            var byKey = new Dictionary<string, KeyValueChange>();
            foreach (KeyValueChange change in changes)
            {
                byKey[change.Key] = change;
            }
            Assert.Multiple(() =>
            {
                Assert.That(byKey["toNull"].Kind, Is.EqualTo(KeyValueChangeKind.Set));
                Assert.That(byKey["toNull"].Value.IsNull, Is.True);
                Assert.That(byKey["toEmpty"].Kind, Is.EqualTo(KeyValueChangeKind.Set));
                Assert.That(byKey["toEmpty"].Value.IsNull, Is.False);
            });
        }

        [Test]
        public async Task CompactionWaitsUntilTheReplayedLogReachesTheRestoredSnapshotAsync()
        {
            var source = new ScriptedSnapshotConsensus();
            await using var origin = new RaftSharedKeyValueStore(source, false, TimeSpan.Zero, compactionInterval: 0);
            for (int ii = 0; ii < 4; ii++)
            {
                await origin.SetAsync(Key(ii), Value(ii));
            }
            RaftKeyValueSnapshot snapshot = await origin.CreateSnapshotAsync();

            var target = new ScriptedSnapshotConsensus();
            await using var store = new RaftSharedKeyValueStore(target, false, TimeSpan.Zero, compactionInterval: 2);
            await store.RestoreSnapshotAsync(snapshot.Data, RaftSnapshotLogReplay.FromFirstCommand);
            // A watch starts the applier without a proposal of its own.
            using var watching = new CancellationTokenSource();
            Task watch = CollectAsync(store, [], 1, watching.Token);
            for (int ii = 0; ii < 3; ii++)
            {
                await target.DeliverCommandAsync(source.Proposed[ii]);
            }
            using (var reading = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await WaitUntilAsync(() => target.PendingItems == 0, reading.Token);
            }
            await Task.Delay(200);

            // The restored state is the one at the snapshot's last command, not at a replayed earlier one.
            Assert.That(store.AppliedIndex, Is.Zero);
            for (int ii = 3; ii < source.Proposed.Count; ii++)
            {
                await target.DeliverCommandAsync(source.Proposed[ii]);
            }
            (bool found, _) = await store.TryGetAsync(Key(3));
            await target.WaitForCompactionAsync();
            watching.Cancel();
            try
            {
                await watch;
            }
            catch (OperationCanceledException)
            {
                // The watch ends with its cancellation.
            }

            // Only the state at the barrier after the replayed commands is the state at its log index.
            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(target.Compactions, Has.Count.EqualTo(1));
                Assert.That(target.Compactions[0].Index, Is.EqualTo(6UL));
            });
        }

        [Test]
        public async Task EngineSnapshotOfAnUnsupportedFormatStopsTheApplierAsync()
        {
            var consensus = new ScriptedSnapshotConsensus();
            await using var store = new RaftSharedKeyValueStore(consensus, false, TimeSpan.FromSeconds(30), 0);
            consensus.HoldCommands = true;
            Task<bool> pending = store.DeleteAsync("a").AsTask();
            await consensus.WaitForProposalAsync();
            Task watch = CollectAsync(store, [], 1, CancellationToken.None);

            // A snapshot without the engine format marker, here the bare encoding version 1.
            await consensus.DeliverSnapshotAsync(1, [1, 0, 0, 0, 0]);

            FormatException? error = null;
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (FormatException e)
            {
                error = e;
            }

            Assert.That(error?.Message, Does.Contain("unsupported format"));

            // Nothing applies a later request: it is refused with the applier's error instead of waiting.
            InvalidOperationException? refused = null;
            try
            {
                await store.DeleteAsync("b");
            }
            catch (InvalidOperationException e)
            {
                refused = e;
            }

            Assert.That(refused?.InnerException, Is.TypeOf<FormatException>());

            // The change feed ends with the applier's error, and a new watch is refused.
            FormatException? watchError = null;
            try
            {
                await watch.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (FormatException e)
            {
                watchError = e;
            }

            InvalidOperationException? watchRefused = null;
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await foreach (KeyValueChange change in store.WatchAsync(string.Empty, bounded.Token))
                {
                    Assert.Fail("No change follows a stopped applier: " + change.Key);
                }
            }
            catch (InvalidOperationException e)
            {
                watchRefused = e;
            }
            catch (OperationCanceledException)
            {
                // The watch was accepted and waited: watchRefused stays null.
            }

            Assert.Multiple(() =>
            {
                Assert.That(watchError, Is.Not.Null);
                Assert.That(watchRefused?.InnerException, Is.TypeOf<FormatException>());
            });
        }

        [Test]
        public void CompactionNeedsASnapshottingConsensus()
        {
            Assert.That(
                () => new RaftSharedKeyValueStore(new InProcessRaftConsensus(), true, TimeSpan.Zero, 5),
                Throws.ArgumentException);
        }

        [Test]
        public void CompactionIntervalIsNotNegative()
        {
            Assert.That(
                () => new RaftSharedKeyValueStore(new ScriptedSnapshotConsensus(), false, TimeSpan.Zero, -1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        private static DefaultRaftConsensus CreateSingleNode(InMemoryNetwork network, IRaftWritableStorage storage)
        {
            return DefaultRaftConsensus.CreateCluster(
                1,
                network.CreateNode(1),
                storage,
                new RaftNodeOptions { TickInterval = TimeSpan.FromMilliseconds(5) },
                config => config.ElectionTick = 3);
        }

        private static FileRaftStorage OpenStorage(string directory, bool initialize)
        {
            var storage = new FileRaftStorage(new FileRaftStorageOptions(directory) { Fsync = false });
            if (initialize)
            {
                storage.SetConfState(new ConfState(new ulong[] { 1 }));
            }
            return storage;
        }

        private static string NewDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "raft-kv-" + Guid.NewGuid().ToString("N"));
        }

        private static void DeleteDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string Key(int ii)
        {
            return "k" + ii.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static ByteString Value(int ii)
        {
            return ByteString.From(new byte[] { (byte)ii, (byte)(ii + 1) });
        }

        private static async Task CollectAsync(
            RaftSharedKeyValueStore store,
            List<KeyValueChange> changes,
            int count,
            CancellationToken ct)
        {
            await foreach (KeyValueChange change in store.WatchAsync(string.Empty, ct))
            {
                changes.Add(change);
                if (changes.Count >= count)
                {
                    return;
                }
            }
        }

        private static async Task WatchAsync(
            RaftSharedKeyValueStore store,
            HashSet<string> keys,
            HashSet<string> expected,
            TaskCompletionSource<bool> ready,
            CancellationToken ct)
        {
            await foreach (KeyValueChange change in store.WatchAsync("k", ct))
            {
                if (change.Kind == KeyValueChangeKind.Set)
                {
                    keys.Add(change.Key);
                    ready.TrySetResult(true);
                }
                if (keys.IsSupersetOf(expected))
                {
                    return;
                }
            }
        }

        private static async Task<int> WaitForLeaderAsync(DefaultRaftConsensus[] nodes, CancellationToken ct)
        {
            while (true)
            {
                for (int ii = 0; ii < nodes.Length; ii++)
                {
                    if (nodes[ii].IsLeader)
                    {
                        return ii;
                    }
                }
                await Task.Delay(20, ct);
            }
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

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
        {
            while (!condition())
            {
                await Task.Delay(10, ct);
            }
        }

        /// <summary>
        /// A single-replica <see cref="IRaftSnapshotConsensus"/> that commits every proposal at the next log index
        /// (unless <see cref="HoldCommands"/> is set), records compactions, and delivers snapshots on request.
        /// </summary>
        private sealed class ScriptedSnapshotConsensus : IRaftSnapshotConsensus
        {
            public bool IsLeader => true;

            public event Action<bool>? LeadershipChanged
            {
                add { }
                remove { }
            }

            public bool HoldCommands { get; set; }

            public List<(ulong Index, byte[] Snapshot)> Compactions { get; } = [];

            /// <summary>The number of compactions still to fail; each failed one is counted, not recorded.</summary>
            public int FailCompactions { get; set; }

            /// <summary>The exception a failing compaction throws.</summary>
            public Func<Exception> CompactionFailure { get; set; } =
                () => new IOException("The storage could not record the snapshot.");

            /// <summary>The number of items the store has not read yet.</summary>
            public int PendingItems => m_items.Reader.Count;

            public int FailedCompactions { get; private set; }

            public List<byte[]> Proposed { get; } = [];

            public ChannelReader<ReadOnlyMemory<byte>> Committed =>
                throw new NotSupportedException("The store reads CommittedItems.");

            public ChannelReader<RaftCommittedItem> CommittedItems => m_items.Reader;

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public async ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
            {
                lock (m_proposedLock)
                {
                    Proposed.Add(command.ToArray());
                }
                m_proposed.TrySetResult(true);
                if (HoldCommands)
                {
                    return;
                }
                await m_gate.WaitAsync(ct);
                try
                {
                    await m_items.Writer.WriteAsync(new RaftCommittedItem(false, ++m_index, command.ToArray()), ct);
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public ValueTask CampaignAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask CompactAsync(ulong appliedIndex, ReadOnlyMemory<byte> snapshot, CancellationToken ct = default)
            {
                lock (m_compactionsLock)
                {
                    if (FailCompactions > 0)
                    {
                        FailCompactions--;
                        FailedCompactions++;
                        throw CompactionFailure();
                    }
                    Compactions.Add((appliedIndex, snapshot.ToArray()));
                }
                m_compacted.TrySetResult(true);
                return default;
            }

            public async Task DeliverSnapshotAsync(ulong index, byte[] snapshot)
            {
                await m_gate.WaitAsync();
                try
                {
                    m_index = Math.Max(m_index, index);
                    await m_items.Writer.WriteAsync(new RaftCommittedItem(true, index, snapshot));
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public async Task DeliverCommandAsync(byte[] command)
            {
                await m_gate.WaitAsync();
                try
                {
                    await m_items.Writer.WriteAsync(new RaftCommittedItem(false, ++m_index, command));
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public Task<bool> WaitForCompactionAsync()
            {
                return m_compacted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }

            public Task<bool> WaitForProposalAsync()
            {
                return m_proposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }

            public ValueTask DisposeAsync()
            {
                m_items.Writer.TryComplete();
                m_gate.Dispose();
                return default;
            }

            private readonly Channel<RaftCommittedItem> m_items = Channel.CreateUnbounded<RaftCommittedItem>();
            private readonly Lock m_proposedLock = new();
            private readonly Lock m_compactionsLock = new();
            private readonly SemaphoreSlim m_gate = new(1, 1);
            private readonly TaskCompletionSource<bool> m_compacted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_proposed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private ulong m_index;
        }
    }
}
