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
        public async Task LeadershipPromotionSwitchesReaderToWriterAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            await using var election = new MutableLeaderElection();
            var space = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, space, election);
            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);
            synchronizer.Start();

            BaseDataVariableState standbyNode = NewVariable("standby", 1.0);
            await space.AddOrUpdateNodeAsync(standbyNode).ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(synchronizer.IsWriter, Is.False);
            Assert.That(
                await store.TryGetNodeAsync(standbyNode.NodeId).ConfigureAwait(false),
                Is.Null,
                "a standby must not write to the shared store");

            election.Set(true);
            BaseDataVariableState promotedNode = NewVariable("promoted", 2.0);
            await space.AddOrUpdateNodeAsync(promotedNode).ConfigureAwait(false);

            await AssertEventuallyAsync(
                async () => await store.TryGetNodeAsync(promotedNode.NodeId).ConfigureAwait(false) != null,
                "the promoted replica should switch to writer mode").ConfigureAwait(false);
            Assert.That(synchronizer.IsWriter, Is.True);
        }

        [Test]
        public async Task LeadershipDemotionStopsWriterFromWritingAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            await using var election = new MutableLeaderElection();
            election.Set(true);

            var space = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, space, election);
            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);
            synchronizer.Start();

            election.Set(false);
            BaseDataVariableState demotedNode = NewVariable("demoted", 3.0);
            await space.AddOrUpdateNodeAsync(demotedNode).ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(synchronizer.IsWriter, Is.False);
            Assert.That(
                await store.TryGetNodeAsync(demotedNode.NodeId).ConfigureAwait(false),
                Is.Null,
                "a demoted replica must stop writing to the shared store");
        }

        [Test]
        public async Task DemotionDuringInFlightWriteDoesNotAbortItAsync()
        {
            // This synchronizer never tears down/recreates its outbound channel or
            // drain task on a role transition (only Start()/DisposeAsync() do), so a
            // write already in flight when OnLeadershipChanged(false) fires is never
            // cancelled — it runs to completion against the real store exactly as if
            // demotion had not occurred. Only writes still queued behind it are
            // skipped (correctly, since the replica no longer owns the writer role).
            using var kv = new InMemorySharedKeyValueStore();
            using var innerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var stallingStore = new StallingNodeStateStore(innerStore);
            await using var election = new MutableLeaderElection();
            election.Set(true);

            var space = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(stallingStore, space, election);
            BaseDataVariableState node = NewVariable("in-flight", 1.0);
            await space.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);
            synchronizer.Start();

            // Arm only after the seed above, so the seed write is never the one
            // that stalls.
            stallingStore.Arm();
            node.Value = new Variant(2.0);
            node.ClearChangeMasks(m_systemContext, false);

            await AwaitWithTimeoutAsync(stallingStore.FirstWriteStarted).ConfigureAwait(false);

            // Demote while the write above is stalled mid-flight inside the store.
            election.Set(false);
            stallingStore.ReleaseFirstWrite();

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, DataValue value) = await innerStore
                        .TryReadValueAsync(node.NodeId)
                        .ConfigureAwait(false);
                    return found && Equals(value.WrappedValue, new Variant(2.0));
                },
                "the write already in flight when demotion occurred must still complete").ConfigureAwait(false);
        }

        [Test]
        public async Task RemovedNodesAreDetachedFromTrackingAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, writerSpace, () => true);
            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);
            synchronizer.Start();

            BaseDataVariableState node = NewVariable("leak", 1.0);
            await writerSpace.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await AssertEventuallyAsync(
                async () => await store.TryGetNodeAsync(node.NodeId).ConfigureAwait(false) != null,
                "writer should persist the added node").ConfigureAwait(false);

            await writerSpace.RemoveNodeAsync(node.NodeId).ConfigureAwait(false);
            await AssertEventuallyAsync(
                async () => await store.TryGetNodeAsync(node.NodeId).ConfigureAwait(false) == null,
                "writer should delete the removed node").ConfigureAwait(false);

            Assert.That(synchronizer.TrackedNodeCount, Is.Zero);

            node.Value = new Variant(9.0);
            node.ClearChangeMasks(m_systemContext, false);
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(
                await store.TryGetNodeAsync(node.NodeId).ConfigureAwait(false),
                Is.Null,
                "changing a removed node must not reinsert it into the store");
        }

        [Test]
        public async Task WriterPropagatesReferenceAddAndRemoveToReaderAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            using var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            BaseDataVariableState source = NewVariable("Source", 1.0);
            BaseDataVariableState target = NewVariable("Target", 2.0);
            await writerSpace.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            await writerSpace.AddOrUpdateNodeAsync(target).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true);
            await using var reader = new AddressSpaceSynchronizer(readerStore, readerSpace, () => false);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            writer.Start();
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            reader.Start();

            ExpandedNodeId targetId = new(target.NodeId);
            Task<bool> referenceAdded = WaitForInboundAsync(
                reader,
                c => c.Kind == NodeStateChangeKind.Upsert && c.NodeId == source.NodeId);
            source.AddReference(ReferenceTypeIds.Organizes, false, targetId);
            source.ClearChangeMasks(m_systemContext, false);
            await AwaitWithTimeoutAsync(referenceAdded).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(source.NodeId, out NodeState? mirroredSource), Is.True);
            Assert.That(
                mirroredSource!.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                Is.True,
                "a reference added between existing nodes should replicate to the reader");

            Task<bool> referenceRemoved = WaitForInboundAsync(
                reader,
                c => c.Kind == NodeStateChangeKind.Upsert && c.NodeId == source.NodeId);
            source.RemoveReference(ReferenceTypeIds.Organizes, false, targetId);
            source.ClearChangeMasks(m_systemContext, false);
            await AwaitWithTimeoutAsync(referenceRemoved).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(source.NodeId, out mirroredSource), Is.True);
            Assert.That(
                mirroredSource!.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                Is.False,
                "a reference removed on the writer should be removed on the reader");
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

        private static async Task AssertEventuallyAsync(Func<Task<bool>> condition, string message)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition().ConfigureAwait(false))
                {
                    return;
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            Assert.Fail(message);
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

        private sealed class MutableLeaderElection : ILeaderElection
        {
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
        /// of an in-flight store write. Stalling is armed explicitly so the
        /// seed write made by <c>SeedOrHydrateAsync</c> is never the one that
        /// stalls.
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

                // Block here — deterministically inside an in-flight store
                // write — until the test releases the gate or the caller
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
