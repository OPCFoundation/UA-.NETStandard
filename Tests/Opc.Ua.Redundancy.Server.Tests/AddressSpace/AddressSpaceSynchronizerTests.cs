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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration tests for <see cref="AddressSpaceSynchronizer"/> running
    /// two replicas (writer + reader) over one shared store.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class AddressSpaceSynchronizerTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;
        private SystemContext m_systemContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:sync");
            m_messageContext = messageContext;
            m_systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task WriterSeedsEmptyStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("seed", 1.0)).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            IStoredNode? stored = await store.TryGetNodeAsync(new NodeId("seed", NamespaceIndex)).ConfigureAwait(false);
            (bool found, _) = await store.TryReadValueAsync(new NodeId("seed", NamespaceIndex)).ConfigureAwait(false);
            Assert.That(stored, Is.Not.Null);
            Assert.That(found, Is.True);
        }

        [Test]
        public async Task ReaderDoesNotSeedEmptyStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await readerSpace.AddOrUpdateNodeAsync(NewVariable("local", 1.0)).ConfigureAwait(false);

            await using var reader = new AddressSpaceSynchronizer(store, readerSpace, () => false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            IStoredNode? stored = await store.TryGetNodeAsync(new NodeId("local", NamespaceIndex)).ConfigureAwait(false);
            Assert.That(reader.IsWriter, Is.False);
            Assert.That(stored, Is.Null, "a reader must never write to the shared store");
        }

        [Test]
        public async Task WriterReplicatesTopologyAndValueToReaderAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            using var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);

            BaseDataVariableState nodeX = NewVariable("X", 1.0);
            await writerSpace.AddOrUpdateNodeAsync(nodeX).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true);
            await using var reader = new AddressSpaceSynchronizer(readerStore, readerSpace, () => false);

            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            writer.Start();

            await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out _), Is.True, "reader hydrated X from the store");
            reader.Start();

            // Value change on the writer propagates to the reader.
            Task<bool> valueApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Value && c.NodeId == nodeX.NodeId);
            nodeX.Value = new Variant(42.0);
            nodeX.ClearChangeMasks(m_systemContext, false);
            await AwaitWithTimeoutAsync(valueApplied).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out NodeState? rx), Is.True);
            Assert.That(((BaseDataVariableState)rx!).Value, Is.EqualTo(new Variant(42.0)));

            // Adding a node on the writer propagates to the reader.
            BaseDataVariableState nodeY = NewVariable("Y", 7.0);
            Task<bool> addApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Upsert && c.NodeId == nodeY.NodeId);
            await writerSpace.AddOrUpdateNodeAsync(nodeY).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(addApplied).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeY.NodeId, out _), Is.True, "reader received added node Y");

            // Removing a node on the writer propagates to the reader.
            Task<bool> deleteApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Delete && c.NodeId == nodeX.NodeId);
            await writerSpace.RemoveNodeAsync(nodeX.NodeId).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(deleteApplied).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out _), Is.False, "reader removed node X");
        }

        [Test]
        public async Task SnapshotIsPublishedAfterWriterSeedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("X", 1.0)).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            NodeStateSnapshot? snapshot = await store.TryReadSnapshotAsync().ConfigureAwait(false);
            Assert.That(snapshot, Is.Not.Null);
            bool sawX = false;
            await foreach (NodeStateChange entry in snapshot!.Entries)
            {
                if (entry.Kind == NodeStateChangeKind.Upsert && entry.NodeId == new NodeId("X", NamespaceIndex))
                {
                    sawX = true;
                }
            }
            Assert.That(sawX, Is.True, "the published snapshot contains the seeded node");
        }

        [Test]
        public async Task ReaderHydratesSnapshotThenDeltaLogValueAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var x = new NodeId("X", NamespaceIndex);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("X", 1.0)).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            // A post-snapshot value change lands only in the delta log.
            await writerStore.WriteValueAsync(x, new DataValue(new Variant(99.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            using var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var reader = new AddressSpaceSynchronizer(readerStore, readerSpace, () => false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(x, out NodeState? node), Is.True, "reader hydrated the snapshot node");
            Assert.That(
                ((BaseDataVariableState)node!).Value,
                Is.EqualTo(new Variant(99.0)),
                "reader applied the post-snapshot delta-log value on top of the snapshot");
        }

        [Test]
        public async Task ReaderFallsBackToStreamedHydrationWithoutSnapshotAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var x = new NodeId("X", NamespaceIndex);

            // Write directly, without publishing a snapshot, so hydration must use
            // the streamed EnumerateAsync/EnumerateValuesAsync fallback path.
            await store.UpsertNodeAsync(
                new StoredNode(x, NodeStateSerializer.Serialize(m_systemContext, NewVariable("X", 5.0)))).ConfigureAwait(false);
            await store.WriteValueAsync(x, new DataValue(new Variant(5.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);
            Assert.That(await store.TryReadSnapshotAsync().ConfigureAwait(false), Is.Null, "no snapshot was published");

            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var reader = new AddressSpaceSynchronizer(store, readerSpace, () => false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(x, out _), Is.True, "reader hydrated via the streamed fallback");
        }

        [Test]
        public async Task ReaderObservesWriterSequenceForPromotionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("X", 1.0)).ConfigureAwait(false);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("Y", 2.0)).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            using var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var reader = new AddressSpaceSynchronizer(readerStore, readerSpace, () => false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(readerStore.CurrentSequence, Is.GreaterThan(0));
            Assert.That(
                readerStore.CurrentSequence,
                Is.EqualTo(writerStore.CurrentSequence),
                "a promoted reader continues assigning sequences from the writer's high-water mark");
        }

        [Test]
        public async Task DeletedMaskChangeIsIgnoredButValueStillReplicatesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            using var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);

            BaseDataVariableState nodeX = NewVariable("X", 1.0);
            await writerSpace.AddOrUpdateNodeAsync(nodeX).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true);
            await using var reader = new AddressSpaceSynchronizer(readerStore, readerSpace, () => false);

            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            writer.Start();
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            reader.Start();

            // A StateChanged carrying the Deleted mask must be ignored by the
            // writer (deletes are driven by NodeRemoved, not StateChanged).
            nodeX.UpdateChangeMasks(NodeStateChangeMasks.Deleted);
            nodeX.ClearChangeMasks(m_systemContext, false);

            // A subsequent value change proves the pipeline survived the ignored
            // delete mask and still replicates to the reader.
            Task<bool> valueApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Value && c.NodeId == nodeX.NodeId);
            nodeX.Value = new Variant(55.0);
            nodeX.ClearChangeMasks(m_systemContext, false);
            await AwaitWithTimeoutAsync(valueApplied).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out NodeState? rx), Is.True);
            Assert.That(((BaseDataVariableState)rx!).Value, Is.EqualTo(new Variant(55.0)));
        }

        [Test]
        public async Task StartIsIdempotentAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            writer.Start();

            Assert.That(() => writer.Start(), Throws.Nothing);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            writer.Start();

            await writer.DisposeAsync().ConfigureAwait(false);

            // The second disposal must be a no-op; the await-using scope exit
            // disposes a third time, also without effect.
            Assert.That(
                async () => await writer.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public async Task PromotedReaderMirrorsLocalChangeToStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var testStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var testSpace = new DictionaryAddressSpace(m_systemContext);
            var xId = new NodeId("X", NamespaceIndex);

            // Seed the store so the standby has something to hydrate.
            await testStore.UpsertNodeAsync(
                new StoredNode(xId, NodeStateSerializer.Serialize(m_systemContext, NewVariable("X", 1.0)))).ConfigureAwait(false);
            await testStore.WriteValueAsync(
                xId, new DataValue(new Variant(1.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            // The replica under test starts as a standby (reader) driven by a
            // leader election, hydrates X, and initially never writes.
            await using var election = new MutableLeaderElection(false);
            await using var replica = new AddressSpaceSynchronizer(testStore, testSpace, election);
            await replica.SeedOrHydrateAsync().ConfigureAwait(false);
            Assert.That(testSpace.TryGetNode(xId, out _), Is.True, "standby hydrated X from the store");
            replica.Start();
            Assert.That(replica.WriterRoleActive, Is.False, "a standby must not write");

            // Promote the standby and wait for the writer role to activate.
            Task<bool> promoted = WaitForRoleAsync(replica, writer: true);
            election.Set(true);
            await AwaitWithTimeoutAsync(promoted).ConfigureAwait(false);
            Assert.That(replica.WriterRoleActive, Is.True, "a promoted standby must attach outbound handlers");

            // A local change on the promoted replica must now mirror to the store.
            Assert.That(testSpace.TryGetNode(xId, out NodeState? node), Is.True);
            var variable = (BaseDataVariableState)node!;
            variable.Value = new Variant(77.0);
            variable.ClearChangeMasks(m_systemContext, false);

            await WaitForStoreValueAsync(kv, xId, new Variant(77.0)).ConfigureAwait(false);
        }

        [Test]
        public async Task DemotedWriterStopsWritingAndFollowsNewWriterAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var testStore = new InMemoryNodeStateStore(kv, m_messageContext);
            using var externalStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var testSpace = new DictionaryAddressSpace(m_systemContext);
            var xId = new NodeId("X", NamespaceIndex);
            await testSpace.AddOrUpdateNodeAsync(NewVariable("X", 1.0)).ConfigureAwait(false);

            // The replica under test starts as the active writer and seeds the store.
            await using var election = new MutableLeaderElection(true);
            await using var replica = new AddressSpaceSynchronizer(testStore, testSpace, election);
            await replica.SeedOrHydrateAsync().ConfigureAwait(false);
            replica.Start();
            Assert.That(replica.WriterRoleActive, Is.True);

            // Demote it and wait for the reader role to activate.
            Task<bool> demoted = WaitForRoleAsync(replica, writer: false);
            election.Set(false);
            await AwaitWithTimeoutAsync(demoted).ConfigureAwait(false);
            Assert.That(
                replica.WriterRoleActive,
                Is.False,
                "a demoted writer must detach its outbound handlers");

            // A new external leader writes X; the demoted replica must apply the
            // change, proving it now follows the store change-feed as a reader.
            Task<bool> applied = WaitForInboundAsync(
                replica, c => c.Kind == NodeStateChangeKind.Value && c.NodeId == xId);
            await externalStore.WriteValueAsync(
                xId, new DataValue(new Variant(555.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(applied).ConfigureAwait(false);

            Assert.That(testSpace.TryGetNode(xId, out NodeState? node), Is.True);
            Assert.That(((BaseDataVariableState)node!).Value, Is.EqualTo(new Variant(555.0)));
        }

        [Test]
        public async Task DemotionDrainsInFlightAndBufferedWritesWithoutCancellingThemAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var innerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var stallingStore = new StallingNodeStateStore(innerStore);
            var testSpace = new DictionaryAddressSpace(m_systemContext);
            BaseDataVariableState nodeX = NewVariable("X", 0.0);
            await testSpace.AddOrUpdateNodeAsync(nodeX).ConfigureAwait(false);

            // The replica under test starts as the active writer. Seeding
            // happens before the stall is armed, so the seed write for the
            // initial value is never the one that stalls.
            await using var election = new MutableLeaderElection(true);
            await using var replica = new AddressSpaceSynchronizer(stallingStore, testSpace, election);
            await replica.SeedOrHydrateAsync().ConfigureAwait(false);
            stallingStore.Arm();
            replica.Start();
            Assert.That(replica.WriterRoleActive, Is.True);

            // Enqueue several rapid local value changes; the first store write
            // they produce is made to stall deterministically (see
            // StallingNodeStateStore), so the remaining four are guaranteed to
            // still be buffered in the outbound channel while that first write
            // is in flight.
            const int changeCount = 5;
            for (int i = 1; i <= changeCount; i++)
            {
                nodeX.Value = new Variant((double)i);
                nodeX.ClearChangeMasks(m_systemContext, false);
            }

            // Wait until the drain has actually started (and stalled on) that
            // first write before demoting, so the demotion race window lands
            // exactly on an in-flight store call plus buffered items behind it.
            await AwaitWithTimeoutAsync(stallingStore.FirstWriteStarted).ConfigureAwait(false);

            Task<bool> demoted = WaitForRoleAsync(replica, writer: false);
            election.Set(false);

            // Give the role worker time to reach StopWriterAsync/Cancel() while
            // the first write is still stalled - this is the exact window in
            // which a premature cancellation would abort the drain and discard
            // every buffered write behind the stalled one. Only after this do we
            // let the stalled write proceed.
            await Task.Delay(100).ConfigureAwait(false);
            stallingStore.ReleaseFirstWrite();

            await AwaitWithTimeoutAsync(demoted).ConfigureAwait(false);

            // Every buffered write - including the ones queued behind the
            // in-flight one - must have reached the store; a demotion must
            // never silently discard queued writes.
            await WaitForStoreValueAsync(kv, nodeX.NodeId, new Variant((double)changeCount)).ConfigureAwait(false);
        }

        private BaseDataVariableState NewVariable(string id, double value)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId(id, NamespaceIndex),
                BrowseName = new QualifiedName(id, NamespaceIndex),
                DisplayName = new LocalizedText(id),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(value)
            };
        }

        private static Task<bool> WaitForInboundAsync(
            AddressSpaceSynchronizer synchronizer,
            Func<NodeStateChange, bool> predicate)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(NodeStateChange change)
            {
                if (predicate(change))
                {
                    synchronizer.InboundApplied -= Handler;
                    tcs.TrySetResult(true);
                }
            }

            synchronizer.InboundApplied += Handler;
            return tcs.Task;
        }

        private static async Task AwaitWithTimeoutAsync(Task task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task), "replication did not complete within the timeout");
            await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Awaits <paramref name="task"/>, observing <paramref name="ct"/>
        /// without relying on any BCL API newer than net48 (this test project
        /// multi-targets down to net48).
        /// </summary>
        private static async Task WaitWithCancellationAsync(Task task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
            {
                Task completed = await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false);
                if (completed == cancelled.Task)
                {
                    ct.ThrowIfCancellationRequested();
                }
            }

            await task.ConfigureAwait(false);
        }

        private static Task<bool> WaitForRoleAsync(AddressSpaceSynchronizer synchronizer, bool writer)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(bool isWriter)
            {
                if (isWriter == writer)
                {
                    synchronizer.RoleActivated -= Handler;
                    tcs.TrySetResult(true);
                }
            }

            synchronizer.RoleActivated += Handler;
            return tcs.Task;
        }

        private async Task WaitForStoreValueAsync(
            InMemorySharedKeyValueStore kv,
            NodeId nodeId,
            Variant expected)
        {
            using var verify = new InMemoryNodeStateStore(kv, m_messageContext);
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                (bool found, DataValue value) = await verify.TryReadValueAsync(nodeId).ConfigureAwait(false);
                if (found && Equals(value.WrappedValue, expected))
                {
                    return;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }

            Assert.Fail("the promoted replica did not mirror the local change to the shared store");
        }

        private sealed class MutableLeaderElection : ILeaderElection
        {
            public MutableLeaderElection(bool isLeader)
            {
                m_isLeader = isLeader ? 1 : 0;
            }

            public bool IsLeader => Volatile.Read(ref m_isLeader) != 0;

            public event Action<bool>? LeadershipChanged;

            public void Set(bool isLeader)
            {
                Volatile.Write(ref m_isLeader, isLeader ? 1 : 0);
                LeadershipChanged?.Invoke(isLeader);
            }

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(IsLeader);
            }

            public void Start()
            {
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            private int m_isLeader;
        }

        /// <summary>
        /// An <see cref="INodeStateStore"/> decorator whose first
        /// <see cref="WriteValueAsync"/> call after <see cref="Arm"/> stalls
        /// (deterministically, without relying on timing) until the test
        /// releases it, so a test can reliably place a demotion in the middle
        /// of an in-flight store write with more writes already buffered
        /// behind it. Stalling is armed explicitly so the seed write made by
        /// <c>SeedOrHydrateAsync</c> is never the one that stalls.
        /// </summary>
        private sealed class StallingNodeStateStore : INodeStateStore
        {
            public StallingNodeStateStore(INodeStateStore inner)
            {
                m_inner = inner;
            }

            public Task FirstWriteStarted => m_firstWriteStarted.Task;

            /// <summary>
            /// Arms the stall: the next <see cref="WriteValueAsync"/> call
            /// after this point (and only that one) will stall.
            /// </summary>
            public void Arm()
            {
                Volatile.Write(ref m_armed, 1);
            }

            public void ReleaseFirstWrite()
            {
                m_releaseGate.TrySetResult(true);
            }

            public ValueTask UpsertNodeAsync(IStoredNode node, CancellationToken ct = default)
            {
                return m_inner.UpsertNodeAsync(node, ct);
            }

            public ValueTask<bool> DeleteNodeAsync(NodeId nodeId, CancellationToken ct = default)
            {
                return m_inner.DeleteNodeAsync(nodeId, ct);
            }

            public ValueTask<IStoredNode?> TryGetNodeAsync(NodeId nodeId, CancellationToken ct = default)
            {
                return m_inner.TryGetNodeAsync(nodeId, ct);
            }

            public IAsyncEnumerable<IStoredNode> EnumerateAsync(CancellationToken ct = default)
            {
                return m_inner.EnumerateAsync(ct);
            }

            public ValueTask WriteValueAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
            {
                // Copy out of the `in` parameter before crossing an await
                // boundary (in-parameters cannot be used in async methods).
                DataValue copy = value;
                if (Volatile.Read(ref m_armed) != 0 && Interlocked.Exchange(ref m_stalledOnce, 1) == 0)
                {
                    return StallThenWriteAsync(nodeId, copy, ct);
                }
                return m_inner.WriteValueAsync(nodeId, copy, ct);
            }

            public ValueTask<(bool Found, DataValue Value)> TryReadValueAsync(NodeId nodeId, CancellationToken ct = default)
            {
                return m_inner.TryReadValueAsync(nodeId, ct);
            }

            public IAsyncEnumerable<(NodeId NodeId, DataValue Value)> EnumerateValuesAsync(CancellationToken ct = default)
            {
                return m_inner.EnumerateValuesAsync(ct);
            }

            public IAsyncEnumerable<NodeStateChange> SubscribeChangesAsync(CancellationToken ct = default)
            {
                return m_inner.SubscribeChangesAsync(ct);
            }

            private async ValueTask StallThenWriteAsync(NodeId nodeId, DataValue value, CancellationToken ct)
            {
                m_firstWriteStarted.TrySetResult(true);

                // Block here - deterministically inside an in-flight store
                // write - until the test releases the gate or the caller
                // cancels, reproducing the exact race window a graceful
                // demotion must not lose buffered writes across.
                await WaitWithCancellationAsync(m_releaseGate.Task, ct).ConfigureAwait(false);
                await m_inner.WriteValueAsync(nodeId, value, ct).ConfigureAwait(false);
            }

            private readonly INodeStateStore m_inner;
            private readonly TaskCompletionSource<bool> m_firstWriteStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_releaseGate =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_armed;
            private int m_stalledOnce;
        }
    }
}
