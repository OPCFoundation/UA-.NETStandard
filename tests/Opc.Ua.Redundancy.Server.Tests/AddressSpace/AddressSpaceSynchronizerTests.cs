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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Moq;
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
        private ServiceMessageContext m_messageContext = null!;
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
        public async Task PublicWriterDoesNotSeedNonEmptyStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            BaseDataVariableState remote = NewVariable("remote", 1.0);
            await store.UpsertNodeAsync(
                new StoredNode(
                    remote.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, remote))).ConfigureAwait(false);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            BaseDataVariableState local = NewVariable("local", 2.0);
            await writerSpace.AddOrUpdateNodeAsync(local).ConfigureAwait(false);
            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);

            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(
                await store.TryGetNodeAsync(local.NodeId).ConfigureAwait(false),
                Is.Null);
            Assert.That(writerSpace.TryGetNode(remote.NodeId, out _), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PublicSynchronizerAppliesPersistedDeleteAsync(
            bool compactAfterDelete)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            BaseDataVariableState deleted = NewVariable("deleted", 1.0);
            await store.UpsertNodeAsync(
                new StoredNode(
                    deleted.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, deleted))).ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);
            await store.DeleteNodeAsync(deleted.NodeId).ConfigureAwait(false);
            if (compactAfterDelete)
            {
                await store.WriteSnapshotAsync().ConfigureAwait(false);
            }

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("deleted", 2.0)).ConfigureAwait(false);
            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);

            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(writerSpace.TryGetNode(deleted.NodeId, out _), Is.False);
            Assert.That(
                await store.TryGetNodeAsync(deleted.NodeId).ConfigureAwait(false),
                Is.Null);
        }

        [Test]
        public async Task PublicSynchronizerDoesNotReseedDeletedGraphAfterCorruptSnapshotAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            BaseDataVariableState deleted = NewVariable("deleted", 1.0);
            await store.UpsertNodeAsync(
                new StoredNode(
                    deleted.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, deleted))).ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);
            await store.DeleteNodeAsync(deleted.NodeId).ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);
            await kv.SetAsync(
                "snapmeta/manifest",
                ByteString.From(new byte[] { 1, 2, 3 })).ConfigureAwait(false);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("deleted", 2.0)).ConfigureAwait(false);
            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);

            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(writerSpace.TryGetNode(deleted.NodeId, out _), Is.False);
            Assert.That(
                await store.TryGetNodeAsync(deleted.NodeId).ConfigureAwait(false),
                Is.Null);
        }

        /// <summary>
        /// Verifies that corruption cannot remove a local root even when another stored record is valid.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MalformedStoredRecordDoesNotDeleteLocalRootAsync(bool includeValidRecord)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("invalid", NamespaceIndex);
            if (includeValidRecord)
            {
                BaseDataVariableState valid = NewVariable("valid", 1.0);
                await store.UpsertNodeAsync(new StoredNode(
                    valid.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, valid))).ConfigureAwait(false);
            }
            await kv.SetAsync(
                "n/" + nodeId,
                ByteString.From(new byte[] { 1, 2, 3 })).ConfigureAwait(false);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            BaseDataVariableState localNode = NewVariable("invalid", 2.0);
            await writerSpace.AddOrUpdateNodeAsync(localNode).ConfigureAwait(false);
            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);

            await Assert.ThatAsync(
                async () => await writer.SeedOrHydrateAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);

            Assert.That(writerSpace.TryGetNode(nodeId, out NodeState? retained), Is.True);
            Assert.That(retained, Is.SameAs(localNode));
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
            DateTime nodeYTimestamp = DateTime.UtcNow.AddMinutes(-2);
            nodeY.Timestamp = nodeYTimestamp;
            Task<bool> addApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Upsert && c.NodeId == nodeY.NodeId);
            Task<bool> addedValueApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Value && c.NodeId == nodeY.NodeId);
            await writerSpace.AddOrUpdateNodeAsync(nodeY).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(addApplied).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(addedValueApplied).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeY.NodeId, out NodeState? mirroredY), Is.True);
            Assert.That(((BaseVariableState)mirroredY!).Timestamp, Is.EqualTo(nodeYTimestamp));

            // Removing a node on the writer propagates to the reader.
            Task<bool> deleteApplied = WaitForInboundAsync(
                reader, c => c.Kind == NodeStateChangeKind.Delete && c.NodeId == nodeX.NodeId);
            await writerSpace.RemoveNodeAsync(nodeX.NodeId).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(deleteApplied).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeX.NodeId, out _), Is.False, "reader removed node X");
        }

        [Test]
        public async Task AddedVariableDescendantReplicatesTimestampAndLaterValuesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            using var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var readerSpace = new RecordingAddressSpace(m_systemContext);
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Root", NamespaceIndex),
                BrowseName = new QualifiedName("Root", NamespaceIndex),
                DisplayName = new LocalizedText("Root")
            };
            await writerSpace.AddOrUpdateNodeAsync(root).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true);
            await using var reader = new AddressSpaceSynchronizer(readerStore, readerSpace, () => false);
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            writer.Start();
            reader.Start();

            DateTime initialTimestamp = DateTime.UtcNow.AddMinutes(-3);
            BaseDataVariableState child = NewVariable("Child", 5.0);
            child.Timestamp = initialTimestamp;
            Task<bool> initialValueApplied = WaitForInboundAsync(
                reader,
                change => change.Kind == NodeStateChangeKind.Value &&
                    change.NodeId == child.NodeId);
            root.AddChild(child);
            root.ClearChangeMasks(m_systemContext, false);
            await AwaitWithTimeoutAsync(initialValueApplied).ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(root.NodeId, out NodeState? mirroredRoot), Is.True);
            var mirroredChild = mirroredRoot!
                .FindChild(m_systemContext, child.BrowseName) as BaseVariableState;
            Assert.That(mirroredChild, Is.Not.Null);
            Assert.That(mirroredChild!.Timestamp, Is.EqualTo(initialTimestamp));

            Task<bool> changedValueApplied = WaitForInboundAsync(
                reader,
                change => change.Kind == NodeStateChangeKind.Value &&
                    change.NodeId == child.NodeId);
            child.Value = new Variant(9.0);
            child.Timestamp = DateTime.UtcNow;
            child.ClearChangeMasks(m_systemContext, false);
            await AwaitWithTimeoutAsync(changedValueApplied).ConfigureAwait(false);

            Assert.That(mirroredChild.Value, Is.EqualTo(new Variant(9.0)));
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
        public async Task AuthoritativeEmptyPartitionRemovesStaleLocalRootAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("deleted", NamespaceIndex);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("deleted", 1.0)).ConfigureAwait(false);

            await using (var writer = new AddressSpaceSynchronizer(
                store,
                writerSpace,
                () => true,
                null,
                node => node.NamespaceIndex == NamespaceIndex,
                "urn:test:sync"))
            {
                await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            }

            await store.DeleteNodeAsync(nodeId).ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);

            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await readerSpace.AddOrUpdateNodeAsync(NewVariable("deleted", 2.0)).ConfigureAwait(false);
            await using var reader = new AddressSpaceSynchronizer(
                store,
                readerSpace,
                () => true,
                null,
                node => node.NamespaceIndex == NamespaceIndex,
                "urn:test:sync");

            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeId, out _), Is.False);
            Assert.That(
                await store.TryGetNodeAsync(nodeId).ConfigureAwait(false),
                Is.Null);
        }

        [Test]
        public async Task IncompletePartitionSeedDoesNotDeleteMissingLocalRootsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            BaseDataVariableState partial = NewVariable("partial", 1.0);
            await store.UpsertNodeAsync(
                new StoredNode(
                    partial.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, partial))).ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("partial", 2.0)).ConfigureAwait(false);
            BaseDataVariableState missing = NewVariable("missing", 3.0);
            await writerSpace.AddOrUpdateNodeAsync(missing).ConfigureAwait(false);
            await using var writer = new AddressSpaceSynchronizer(
                store,
                writerSpace,
                () => true,
                null,
                node => node.NamespaceIndex == NamespaceIndex,
                "urn:test:sync");

            await writer.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(writerSpace.TryGetNode(missing.NodeId, out _), Is.True);
            Assert.That(
                await store.TryGetNodeAsync(missing.NodeId).ConfigureAwait(false),
                Is.Not.Null);
        }

        [Test]
        public async Task WriterRejectsForeignDescendantAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("owned", NamespaceIndex),
                BrowseName = new QualifiedName("Owned", NamespaceIndex),
                DisplayName = new LocalizedText("Owned")
            };
            root.AddChild(new BaseObjectState(root)
            {
                NodeId = new NodeId("foreign", NamespaceIndex + 1),
                BrowseName = new QualifiedName("Foreign", NamespaceIndex + 1),
                DisplayName = new LocalizedText("Foreign")
            });
            await writerSpace.AddOrUpdateNodeAsync(root).ConfigureAwait(false);

            await using var writer = new AddressSpaceSynchronizer(
                store,
                writerSpace,
                () => true,
                null,
                node => node.NamespaceIndex == NamespaceIndex,
                "urn:test:sync");

            await Assert.ThatAsync(
                async () => await writer.SeedOrHydrateAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task ReaderRejectsForeignDescendantBeforeMutatingExistingRootAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var rootId = new NodeId("owned", NamespaceIndex);
            var invalidRoot = new BaseObjectState(null)
            {
                NodeId = rootId,
                BrowseName = new QualifiedName("Owned", NamespaceIndex),
                DisplayName = new LocalizedText("Remote")
            };
            invalidRoot.AddChild(new BaseObjectState(invalidRoot)
            {
                NodeId = new NodeId("foreign", NamespaceIndex + 1),
                BrowseName = new QualifiedName("Foreign", NamespaceIndex + 1),
                DisplayName = new LocalizedText("Foreign")
            });
            await store.UpsertNodeAsync(
                new StoredNode(
                    rootId,
                    NodeStateSerializer.Serialize(m_systemContext, invalidRoot))).ConfigureAwait(false);

            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            var localRoot = new BaseObjectState(null)
            {
                NodeId = rootId,
                BrowseName = new QualifiedName("Owned", NamespaceIndex),
                DisplayName = new LocalizedText("Local")
            };
            await readerSpace.AddOrUpdateNodeAsync(localRoot).ConfigureAwait(false);
            await using var reader = new AddressSpaceSynchronizer(
                store,
                readerSpace,
                () => false,
                null,
                node => node.NamespaceIndex == NamespaceIndex,
                "urn:test:sync");

            await Assert.ThatAsync(
                async () => await reader.SeedOrHydrateAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(localRoot.DisplayName, Is.EqualTo(new LocalizedText("Local")));
            var children = new List<BaseInstanceState>();
            localRoot.GetChildren(m_systemContext, children);
            Assert.That(children, Is.Empty);
        }

        [Test]
        public async Task ReaderRejectsMismatchedPayloadRootBeforeMutationAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var recordNodeId = new NodeId("owned", NamespaceIndex);
            var payloadRoot = new BaseObjectState(null)
            {
                NodeId = new NodeId("foreign-root", NamespaceIndex + 1),
                BrowseName = new QualifiedName("ForeignRoot", NamespaceIndex + 1),
                DisplayName = new LocalizedText("Remote")
            };
            await store.UpsertNodeAsync(
                new StoredNode(
                    recordNodeId,
                    NodeStateSerializer.Serialize(m_systemContext, payloadRoot))).ConfigureAwait(false);

            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            var localRoot = new BaseObjectState(null)
            {
                NodeId = recordNodeId,
                BrowseName = new QualifiedName("Owned", NamespaceIndex),
                DisplayName = new LocalizedText("Local")
            };
            await readerSpace.AddOrUpdateNodeAsync(localRoot).ConfigureAwait(false);
            await using var reader = new AddressSpaceSynchronizer(
                store,
                readerSpace,
                () => false,
                null,
                node => node.NamespaceIndex == NamespaceIndex,
                "urn:test:sync");

            await Assert.ThatAsync(
                async () => await reader.SeedOrHydrateAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(localRoot.DisplayName, Is.EqualTo(new LocalizedText("Local")));
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

        /// <summary>
        /// Verifies that a stale replayed deletion cannot prune a newer root already present in hydration.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StaleDeletionCannotPruneRecreatedRootDuringHydrationAsync(bool useSnapshot)
        {
            BaseDataVariableState recreated = NewVariable("recreated", 3.0);
            var store = new OverlappingHydrationStore(
                new StoredNode(recreated.NodeId, NodeStateSerializer.Serialize(m_systemContext, recreated)),
                useSnapshot);
            var local = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, local, () => false);
            synchronizer.StartBeforeHydration();

            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(local.TryGetNode(recreated.NodeId, out NodeState? hydrated), Is.True,
                "Deletion 2 must not remove the root hydrated at sequence 3.");
            Assert.That(((BaseDataVariableState)hydrated!).Value, Is.EqualTo(new Variant(3.0)));

            Task<bool> duplicateApplied = WaitForInboundAsync(synchronizer, change => change.Sequence == 3);
            store.PublishRecreation();
            await synchronizer.CompleteHydrationAsync().ConfigureAwait(false);
            await AwaitWithTimeoutAsync(duplicateApplied).ConfigureAwait(false);
            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(local.TryGetNode(recreated.NodeId, out NodeState? repeated), Is.True,
                "Already-applied snapshot entries must still represent the root on repeated hydration.");
            Assert.That(repeated, Is.SameAs(hydrated));
        }

        /// <summary>
        /// Applies CRDT updates and tombstones without deriving deletion or initial seeding from missing rows.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task HybridHydrationPreservesUnobservedLocalRootsAndAppliesTombstonesAsync(
            bool routeNodeRecordsStrong)
        {
            await using var network = new InMemoryNetwork();
            await using var bulk = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await using var coordinator = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            ArrayOf<string> prefixes = routeNodeRecordsStrong
                ? ["election/", "n/", "v/", "dlog/"]
                : default;
            await using var hybrid = new HybridSharedKeyValueStore(bulk, coordinator, prefixes);
            using var store = new InMemoryNodeStateStore(hybrid, m_messageContext);
            BaseDataVariableState remote = NewVariable("remote", 2.0);
            BaseDataVariableState deleted = NewVariable("deleted", 3.0);
            await store.UpsertNodeAsync(new StoredNode(
                remote.NodeId, NodeStateSerializer.Serialize(m_systemContext, remote))).ConfigureAwait(false);
            await store.UpsertNodeAsync(new StoredNode(
                deleted.NodeId, NodeStateSerializer.Serialize(m_systemContext, deleted))).ConfigureAwait(false);
            await store.DeleteNodeAsync(deleted.NodeId).ConfigureAwait(false);
            var local = new DictionaryAddressSpace(m_systemContext);
            BaseDataVariableState unobserved = NewVariable("unobserved", 1.0);
            await local.AddOrUpdateNodeAsync(unobserved).ConfigureAwait(false);
            await local.AddOrUpdateNodeAsync(deleted).ConfigureAwait(false);
            await using var synchronizer = new AddressSpaceSynchronizer(store, local, () => true);
            synchronizer.StartBeforeHydration();

            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);
            await synchronizer.CompleteHydrationAsync().ConfigureAwait(false);

            Assert.That(local.TryGetNode(unobserved.NodeId, out NodeState? retained), Is.True);
            Assert.That(retained, Is.SameAs(unobserved));
            Assert.That(local.TryGetNode(remote.NodeId, out _), Is.True);
            Assert.That(local.TryGetNode(deleted.NodeId, out _), Is.False);
            Assert.That(await store.TryGetNodeAsync(unobserved.NodeId).ConfigureAwait(false), Is.Null,
                "Missing CRDT rows must not be treated as evidence of an empty store to reseed.");
            (bool snapshotPublished, _) = await bulk.TryGetAsync("snapmeta/manifest").ConfigureAwait(false);
            Assert.That(snapshotPublished, Is.False);
        }

        [Test]
        public async Task ReaderBuffersChangesBetweenHydrationAndLiveApplyAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            var nodeId = new NodeId("X", NamespaceIndex);
            await writerSpace.AddOrUpdateNodeAsync(NewVariable("X", 1.0)).ConfigureAwait(false);
            await using (var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true))
            {
                await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            }

            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var reader = new AddressSpaceSynchronizer(store, readerSpace, () => false);
            reader.StartBeforeHydration();
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            await store.WriteValueAsync(
                nodeId,
                new DataValue(new Variant(2.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);
            await reader.CompleteHydrationAsync().ConfigureAwait(false);
            Assert.That(reader.TrackedNodeCount, Is.EqualTo(1));

            await AssertEventuallyAsync(
                () => Task.FromResult(
                    readerSpace.TryGetNode(nodeId, out NodeState? node) &&
                    node is BaseDataVariableState variable &&
                    variable.Value == new Variant(2.0)),
                "a change committed after hydration must be observed by the pre-registered feed")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task WriterDoesNotFeedHydrationBackIntoStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            BaseDataVariableState node = NewVariable("X", 1.0);
            await store.UpsertNodeAsync(
                new StoredNode(
                    node.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, node))).ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);
            ulong sequenceBeforeHydration = store.CurrentSequence;

            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var writer = new AddressSpaceSynchronizer(store, writerSpace, () => true);
            writer.StartBeforeHydration();
            await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            await writer.CompleteHydrationAsync().ConfigureAwait(false);

            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(store.CurrentSequence, Is.EqualTo(sequenceBeforeHydration));
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

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReaderFallsBackToStreamedHydrationWhenSnapshotChunkIsUnavailableAsync(
            bool corruptChunk)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("X", NamespaceIndex);
            await writerStore.UpsertNodeAsync(
                new StoredNode(
                    nodeId,
                    NodeStateSerializer.Serialize(
                        m_systemContext,
                        NewVariable("X", 5.0)))).ConfigureAwait(false);
            await writerStore.WriteSnapshotAsync().ConfigureAwait(false);

            string? chunkKey = null;
            await foreach (KeyValuePair<string, ByteString> entry in kv.ScanAsync("snap/"))
            {
                chunkKey = entry.Key;
                break;
            }
            Assert.That(chunkKey, Is.Not.Null);
            if (corruptChunk)
            {
                await kv.SetAsync(
                    chunkKey!,
                    ByteString.From(
                    [
                        byte.MaxValue,
                        byte.MaxValue,
                        byte.MaxValue,
                        byte.MaxValue
                    ])).ConfigureAwait(false);
            }
            else
            {
                await kv.DeleteAsync(chunkKey!).ConfigureAwait(false);
            }

            using var readerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var readerSpace = new DictionaryAddressSpace(m_systemContext);
            await using var reader = new AddressSpaceSynchronizer(
                readerStore,
                readerSpace,
                () => false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeId, out _), Is.True);
            Assert.That(readerStore.CurrentSequence, Is.EqualTo(writerStore.CurrentSequence));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReaderHydrationPreservesExistingConcreteNodeGraphAsync(bool publishSnapshot)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("Typed", NamespaceIndex);

            var remoteRoot = new BaseObjectState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Typed", NamespaceIndex),
                DisplayName = new LocalizedText("Remote")
            };
            var remoteMethod = new MethodState(remoteRoot)
            {
                NodeId = new NodeId("Typed.Method", NamespaceIndex),
                BrowseName = new QualifiedName("RenamedMethod", NamespaceIndex),
                DisplayName = new LocalizedText("Remote Method"),
                UserExecutable = false
            };
            PropertyState<ArrayOf<Argument>> remoteOutputArguments =
                remoteMethod.CreateOrReplaceOutputArguments(
                    m_systemContext,
                    null,
                    assignInstanceNodeIds: false);
            remoteOutputArguments.NodeId =
                new NodeId("Typed.Method.OutputArguments", NamespaceIndex);
            remoteRoot.AddChild(remoteMethod);
            BaseDataVariableState remoteVariable = NewVariable("Callback", 2.0);
            remoteVariable.StatusCode = StatusCodes.Good;
            var remoteVariableType = new BaseDataVariableTypeState
            {
                NodeId = new NodeId("VariableType", NamespaceIndex),
                BrowseName = new QualifiedName("VariableType", NamespaceIndex),
                DisplayName = new LocalizedText("VariableType"),
                Value = new Variant(2.0)
            };
            await store.UpsertNodeAsync(
                new StoredNode(nodeId, NodeStateSerializer.Serialize(m_systemContext, remoteRoot))).ConfigureAwait(false);
            await store.UpsertNodeAsync(
                new StoredNode(
                    remoteVariable.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, remoteVariable))).ConfigureAwait(false);
            await store.UpsertNodeAsync(
                new StoredNode(
                    remoteVariableType.NodeId,
                    NodeStateSerializer.Serialize(m_systemContext, remoteVariableType))).ConfigureAwait(false);
            if (publishSnapshot)
            {
                await store.WriteSnapshotAsync().ConfigureAwait(false);
            }

            var readerSpace = new RecordingAddressSpace(m_systemContext);
            object rootHandle = new();
            var localRoot = new ConcreteObjectState
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Typed", NamespaceIndex),
                DisplayName = new LocalizedText("Local"),
                Handle = rootHandle,
                UserWriteMask = AttributeWriteMask.DisplayName
            };
            var localMethod = new ConcreteMethodState(localRoot)
            {
                NodeId = new NodeId("Typed.Method", NamespaceIndex),
                BrowseName = new QualifiedName("Method", NamespaceIndex),
                DisplayName = new LocalizedText("Local Method"),
                Executable = false
            };
            PropertyState<ArrayOf<Argument>> removedInputArguments =
                localMethod.CreateOrReplaceInputArguments(
                    m_systemContext,
                    null,
                    assignInstanceNodeIds: false);
            removedInputArguments.NodeId = new NodeId("Typed.Method.InputArguments", NamespaceIndex);
            PropertyState<ArrayOf<Argument>> localOutputArguments =
                localMethod.CreateOrReplaceOutputArguments(
                    m_systemContext,
                    null,
                    assignInstanceNodeIds: false);
            object outputArgumentsHandle = new();
            localOutputArguments.Handle = outputArgumentsHandle;
            localRoot.AddChild(localMethod);
            var removedChild = new BaseObjectState(localRoot)
            {
                NodeId = new NodeId("Typed.Removed", NamespaceIndex),
                BrowseName = new QualifiedName("Removed", NamespaceIndex),
                DisplayName = new LocalizedText("Removed")
            };
            localRoot.AddChild(removedChild);
            var removedTarget = new NodeId("RemovedTarget", NamespaceIndex);
            localRoot.AddReference(ReferenceTypeIds.Organizes, false, removedTarget);
            await readerSpace.AddOrUpdateNodeAsync(localRoot).ConfigureAwait(false);
            BaseDataVariableState localVariable = NewVariable("Callback", 1.0);
            object variableHandle = new();
            localVariable.Handle = variableHandle;
            DateTime variableTimestamp = DateTime.UtcNow.AddMinutes(-1);
            localVariable.Timestamp = variableTimestamp;
            NodeValueEventHandler readCallback = ReadValue;
            localVariable.OnReadValue = readCallback;
            PropertyState<ArrayOf<LocalizedText>> removedEnumStrings =
                localVariable.CreateOrReplaceEnumStrings(
                    m_systemContext,
                    null,
                    assignInstanceNodeIds: false);
            removedEnumStrings.NodeId = new NodeId("Callback.EnumStrings", NamespaceIndex);
            await readerSpace.AddOrUpdateNodeAsync(localVariable).ConfigureAwait(false);
            var localVariableType = new BaseDataVariableTypeState
            {
                NodeId = remoteVariableType.NodeId,
                BrowseName = remoteVariableType.BrowseName,
                DisplayName = remoteVariableType.DisplayName,
                Value = new Variant(1.0)
            };
            await readerSpace.AddOrUpdateNodeAsync(localVariableType).ConfigureAwait(false);

            await using var reader = new AddressSpaceSynchronizer(store, readerSpace, () => false);
            await reader.SeedOrHydrateAsync().ConfigureAwait(false);

            Assert.That(readerSpace.TryGetNode(nodeId, out NodeState? hydratedRoot), Is.True);
            Assert.That(hydratedRoot, Is.SameAs(localRoot));
            Assert.That(hydratedRoot!.Handle, Is.SameAs(rootHandle));
            Assert.That(hydratedRoot!.DisplayName, Is.EqualTo(new LocalizedText("Remote")));
            Assert.That(hydratedRoot.UserWriteMask, Is.EqualTo(AttributeWriteMask.None));
            Assert.That(
                hydratedRoot.FindChild(
                    m_systemContext,
                    new QualifiedName("RenamedMethod", NamespaceIndex)),
                Is.SameAs(localMethod));
            Assert.That(
                hydratedRoot.FindChild(
                    m_systemContext,
                    new QualifiedName("Method", NamespaceIndex)),
                Is.Null);
            Assert.That(localMethod.DisplayName, Is.EqualTo(new LocalizedText("Remote Method")));
            Assert.That(localMethod.Executable, Is.True);
            Assert.That(localMethod.UserExecutable, Is.False);
            Assert.That(localMethod.InputArguments, Is.Null);
            Assert.That(localMethod.OutputArguments, Is.SameAs(localOutputArguments));
            Assert.That(localOutputArguments.NodeId, Is.EqualTo(remoteOutputArguments.NodeId));
            Assert.That(localOutputArguments.Handle, Is.SameAs(outputArgumentsHandle));
            Assert.That(
                hydratedRoot.FindChild(
                    m_systemContext,
                    new QualifiedName("Removed", NamespaceIndex)),
                Is.Null);
            Assert.That(
                hydratedRoot.ReferenceExists(ReferenceTypeIds.Organizes, false, removedTarget),
                Is.False);
            Assert.That(readerSpace.RemovedNodeIds, Does.Contain(removedChild.NodeId));
            Assert.That(readerSpace.RemovedNodeIds, Does.Contain(removedInputArguments.NodeId));
            Assert.That(
                readerSpace.TryGetNode(remoteVariable.NodeId, out NodeState? hydratedVariable),
                Is.True);
            Assert.That(hydratedVariable, Is.SameAs(localVariable));
            Assert.That(localVariable.Handle, Is.SameAs(variableHandle));
            Assert.That(localVariable.OnReadValue, Is.EqualTo(readCallback));
            Assert.That(localVariable.Value, Is.EqualTo(new Variant(2.0)));
            Assert.That(localVariable.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(localVariable.Timestamp, Is.EqualTo(variableTimestamp));
            Assert.That(localVariable.EnumStrings, Is.Null);
            Assert.That(readerSpace.RemovedNodeIds, Does.Contain(removedEnumStrings.NodeId));
            Assert.That(
                readerSpace.TryGetNode(remoteVariableType.NodeId, out NodeState? hydratedVariableType),
                Is.True);
            Assert.That(hydratedVariableType, Is.SameAs(localVariableType));
            Assert.That(localVariableType.Value, Is.EqualTo(new Variant(2.0)));
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
            writer.Start();
            reader.Start();

            BaseDataVariableState liveNode = NewVariable("Z", 3.0);
            Task<bool> upsertApplied = WaitForInboundAsync(
                reader,
                change => change.Kind == NodeStateChangeKind.Upsert &&
                    change.NodeId == liveNode.NodeId);
            await writerSpace.AddOrUpdateNodeAsync(liveNode).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(upsertApplied).ConfigureAwait(false);

            Task<bool> deleteApplied = WaitForInboundAsync(
                reader,
                change => change.Kind == NodeStateChangeKind.Delete &&
                    change.NodeId == liveNode.NodeId);
            await writerSpace.RemoveNodeAsync(liveNode.NodeId).ConfigureAwait(false);
            await AwaitWithTimeoutAsync(deleteApplied).ConfigureAwait(false);

            Assert.That(readerStore.CurrentSequence, Is.GreaterThan(0));
            Assert.That(
                readerStore.CurrentSequence,
                Is.EqualTo(writerStore.CurrentSequence),
                "a promoted reader continues assigning sequences from the writer's high-water mark");
        }

        [Test]
        public async Task TopologyAndValueSequencesAreOrderedIndependentlyAsync()
        {
            var store = new ScriptedNodeStateStore();
            var space = new DictionaryAddressSpace(m_systemContext);
            var nodeId = new NodeId("X", NamespaceIndex);
            await using var reader = new AddressSpaceSynchronizer(store, space, () => false);
            reader.Start();

            Task<bool> initialUpsert = WaitForInboundAsync(
                reader,
                change => change.Sequence == 10);
            store.Publish(new NodeStateChange
            {
                Kind = NodeStateChangeKind.Upsert,
                NodeId = nodeId,
                Node = new StoredNode(
                    nodeId,
                    NodeStateSerializer.Serialize(
                        m_systemContext,
                        NewVariable("X", 10.0))),
                Sequence = 10
            });
            await AwaitWithTimeoutAsync(initialUpsert).ConfigureAwait(false);

            Task<bool> newerValue = WaitForInboundAsync(
                reader,
                change => change.Sequence == 20);
            store.Publish(new NodeStateChange
            {
                Kind = NodeStateChangeKind.Value,
                NodeId = nodeId,
                Value = new DataValue(new Variant(20.0), StatusCodes.Good, DateTimeUtc.Now),
                Sequence = 20
            });
            await AwaitWithTimeoutAsync(newerValue).ConfigureAwait(false);

            Task<bool> olderTopology = WaitForInboundAsync(
                reader,
                change => change.Sequence == 15);
            store.Publish(new NodeStateChange
            {
                Kind = NodeStateChangeKind.Upsert,
                NodeId = nodeId,
                Node = new StoredNode(
                    nodeId,
                    NodeStateSerializer.Serialize(
                        m_systemContext,
                        NewVariable("X", 15.0))),
                Sequence = 15
            });
            await AwaitWithTimeoutAsync(olderTopology).ConfigureAwait(false);
            Assert.That(space.TryGetNode(nodeId, out NodeState? current), Is.True);
            Assert.That(((BaseVariableState)current!).Value, Is.EqualTo(new Variant(20.0)));

            Task<bool> staleDelete = WaitForInboundAsync(
                reader,
                change => change.Sequence == 12);
            store.Publish(new NodeStateChange
            {
                Kind = NodeStateChangeKind.Delete,
                NodeId = nodeId,
                Sequence = 12
            });
            await AwaitWithTimeoutAsync(staleDelete).ConfigureAwait(false);
            Assert.That(space.TryGetNode(nodeId, out _), Is.True);

            Task<bool> newerTopologyDelete = WaitForInboundAsync(
                reader,
                change => change.Sequence == 16);
            store.Publish(new NodeStateChange
            {
                Kind = NodeStateChangeKind.Delete,
                NodeId = nodeId,
                Sequence = 16
            });
            await AwaitWithTimeoutAsync(newerTopologyDelete).ConfigureAwait(false);
            Assert.That(space.TryGetNode(nodeId, out _), Is.False);
        }

        /// <summary>
        /// Retains newer pending values without carrying pre-deletion values into a recreated subtree.
        /// </summary>
        [TestCase(false, false, 11UL)]
        [TestCase(true, false, 11UL)]
        [TestCase(true, true, 11UL)]
        [TestCase(true, true, 14UL)]
        public async Task PendingValuesRespectTopologyIncarnationsAsync(
            bool nested,
            bool deleteParent,
            ulong valueSequence)
        {
            var store = new ScriptedNodeStateStore();
            var local = new RecordingAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, local, () => false);
            synchronizer.Start();
            BaseObjectState? parent = nested
                ? new BaseObjectState(null)
                {
                    NodeId = new NodeId("parent", NamespaceIndex),
                    BrowseName = new QualifiedName("Parent", NamespaceIndex)
                }
                : null;
            var variable = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId("late-value", NamespaceIndex),
                BrowseName = new QualifiedName("LateValue", NamespaceIndex),
                Value = new Variant(1.0)
            };
            NodeState root = variable;
            if (parent != null)
            {
                parent.AddChild(variable);
                root = parent;
            }
            var value = new DataValue(new Variant(42.0), StatusCodes.Good, DateTimeUtc.Now);
            Task<bool> valueReceived = WaitForInboundAsync(synchronizer, change => change.Kind == NodeStateChangeKind.Value);
            store.Publish(new NodeStateChange
            {
                Kind = NodeStateChangeKind.Value,
                NodeId = variable.NodeId,
                Value = value,
                Sequence = valueSequence
            });
            await AwaitWithTimeoutAsync(valueReceived).ConfigureAwait(false);
            Assert.That(local.TryGetNode(variable.NodeId, out _), Is.False);
            if (deleteParent)
            {
                Task<bool> deleted = WaitForInboundAsync(synchronizer, change => change.Kind == NodeStateChangeKind.Delete);
                store.Publish(new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Delete,
                    NodeId = root.NodeId,
                    Sequence = 12
                });
                await AwaitWithTimeoutAsync(deleted).ConfigureAwait(false);
            }
            Task<bool> topologyReceived = WaitForInboundAsync(
                synchronizer, change => change.Kind == NodeStateChangeKind.Upsert);
            store.Publish(new NodeStateChange
            {
                Kind = NodeStateChangeKind.Upsert,
                NodeId = root.NodeId,
                Node = new StoredNode(root.NodeId, NodeStateSerializer.Serialize(m_systemContext, root)),
                Sequence = deleteParent ? 13UL : 10UL
            });
            await AwaitWithTimeoutAsync(topologyReceived).ConfigureAwait(false);

            Assert.That(local.TryGetNode(variable.NodeId, out NodeState? created), Is.True);
            bool retainValue = !deleteParent || valueSequence > 12;
            Assert.That(((BaseVariableState)created!).Value,
                Is.EqualTo(retainValue ? value.WrappedValue : new Variant(1.0)));
            if (retainValue)
            {
                Assert.That(((BaseVariableState)created).Timestamp, Is.EqualTo(value.SourceTimestamp));
            }
            else
            {
                Task<bool> staleReplay = WaitForInboundAsync(
                    synchronizer, change => change.Kind == NodeStateChangeKind.Value);
                store.Publish(new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Value,
                    NodeId = variable.NodeId,
                    Value = value,
                    Sequence = valueSequence
                });
                await AwaitWithTimeoutAsync(staleReplay).ConfigureAwait(false);
                Assert.That(((BaseVariableState)created).Value, Is.EqualTo(new Variant(1.0)));
            }
        }

        /// <summary>
        /// Rejects stale topology and values after the largest sequence without wrapping the sequence guard.
        /// </summary>
        [Test]
        public async Task MaximumAppliedSequenceDoesNotWrapReplayGuardAsync()
        {
            var store = new ScriptedNodeStateStore();
            var local = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, local, () => false);
            synchronizer.Start();
            BaseDataVariableState node = NewVariable("maximum", 1.0);
            NodeStateChange[] changes =
            [
                new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Upsert,
                    NodeId = node.NodeId,
                    Node = new StoredNode(node.NodeId, NodeStateSerializer.Serialize(m_systemContext, node)),
                    Sequence = ulong.MaxValue
                },
                new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Value,
                    NodeId = node.NodeId,
                    Value = new DataValue(new Variant(2.0)),
                    Sequence = ulong.MaxValue
                },
                new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Value,
                    NodeId = node.NodeId,
                    Value = new DataValue(new Variant(9.0)),
                    Sequence = 1
                },
                new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Delete,
                    NodeId = node.NodeId,
                    Sequence = 1
                }
            ];
            foreach (NodeStateChange change in changes)
            {
                Task<bool> observed = WaitForInboundAsync(synchronizer, current => ReferenceEquals(current, change));
                store.Publish(change);
                await AwaitWithTimeoutAsync(observed).ConfigureAwait(false);
            }

            Assert.That(local.TryGetNode(node.NodeId, out NodeState? retained), Is.True);
            Assert.That(((BaseDataVariableState)retained!).Value, Is.EqualTo(new Variant(2.0)));
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

        /// <summary>
        /// Verifies descendant handlers are removed even when the real node manager clears the tree first.
        /// </summary>
        [Test]
        public async Task RemovingRealNodeManagerSubtreeDetachesDescendantHandlersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var server = new Mock<IServerInternal>();
            server.Setup(value => value.Telemetry).Returns(telemetry);
            server.Setup(value => value.NamespaceUris).Returns(m_systemContext.NamespaceUris);
            server.Setup(value => value.ServerUris).Returns(m_systemContext.ServerUris);
            server.Setup(value => value.Factory).Returns(m_messageContext.Factory);
            server.Setup(value => value.TypeTree).Returns(new TypeTable(m_systemContext.NamespaceUris));
            var master = new Mock<IMasterNodeManager>();
            master.Setup(value => value.NodeManagers).Returns([]);
            master.Setup(value => value.ConfigurationNodeManager).Returns(Mock.Of<IConfigurationNodeManager>());
            server.Setup(value => value.NodeManager).Returns(master.Object);
            server.Setup(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            server.Setup(value => value.MonitoredItemQueueFactory).Returns(queueFactory);
            using var manager = new LifecycleNodeManager(server.Object);
            ILocalAddressSpace local = ((ILocalAddressSpaceSource)manager).CreateLocalAddressSpace();
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, local, () => true);
            await synchronizer.SeedOrHydrateAsync().ConfigureAwait(false);
            synchronizer.Start();
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("root", NamespaceIndex),
                BrowseName = new QualifiedName("Root", NamespaceIndex),
                DisplayName = new LocalizedText("Root")
            };
            var child = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("child", NamespaceIndex),
                BrowseName = new QualifiedName("Child", NamespaceIndex),
                DisplayName = new LocalizedText("Child"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(1.0)
            };
            root.AddChild(child);
            await local.AddOrUpdateNodeAsync(root).ConfigureAwait(false);
            await AssertEventuallyAsync(
                async () => await store.TryGetNodeAsync(root.NodeId).ConfigureAwait(false) != null,
                "the root is published before removal").ConfigureAwait(false);
            Assert.That(synchronizer.TrackedNodeCount, Is.EqualTo(2));

            Assert.That(await local.RemoveNodeAsync(root.NodeId).ConfigureAwait(false), Is.True);

            Assert.That(synchronizer.TrackedNodeCount, Is.Zero,
                "Removal must use tracked membership after the node manager has cleared parent/child links.");
            child.DisplayName = new LocalizedText("Deleted child must not be republished");
            child.ClearChangeMasks(local.Context, false);
            BaseDataVariableState barrier = NewVariable("barrier", 2.0);
            await local.AddOrUpdateNodeAsync(barrier).ConfigureAwait(false);
            await AssertEventuallyAsync(
                async () => await store.TryGetNodeAsync(barrier.NodeId).ConfigureAwait(false) != null,
                "a subsequent write drains after any stale child callback").ConfigureAwait(false);

            Assert.That(await store.TryGetNodeAsync(root.NodeId).ConfigureAwait(false), Is.Null);
            Assert.That(await store.TryGetNodeAsync(child.NodeId).ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Detaches replaced instances without detaching or duplicating the current subtree's subscriptions.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacedSubtreeIgnoresOldDescendantCallbacksAsync(bool reuseChildId)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var local = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store, local, () => true);
            synchronizer.Start();
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("root", NamespaceIndex),
                BrowseName = new QualifiedName("Root", NamespaceIndex)
            };
            var oldChild = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("old-child", NamespaceIndex),
                BrowseName = new QualifiedName("Child", NamespaceIndex),
                Value = new Variant(1.0)
            };
            root.AddChild(oldChild);
            await local.AddOrUpdateNodeAsync(root).ConfigureAwait(false);
            var replacement = new BaseObjectState(null)
            {
                NodeId = root.NodeId,
                BrowseName = root.BrowseName
            };
            var newChild = new BaseDataVariableState(replacement)
            {
                NodeId = reuseChildId ? oldChild.NodeId : new NodeId("new-child", NamespaceIndex),
                BrowseName = oldChild.BrowseName,
                Value = new Variant(2.0)
            };
            replacement.AddChild(newChild);
            await local.AddOrUpdateNodeAsync(replacement).ConfigureAwait(false);
            await local.AddOrUpdateNodeAsync(replacement).ConfigureAwait(false);

            Assert.That(synchronizer.TrackedNodeCount, Is.EqualTo(2));
            oldChild.DisplayName = new LocalizedText("Stale");
            oldChild.ClearChangeMasks(m_systemContext, false);
            newChild.Value = new Variant(3.0);
            newChild.ClearChangeMasks(m_systemContext, false);
            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, DataValue value) = await store.TryReadValueAsync(newChild.NodeId).ConfigureAwait(false);
                    return found && value.WrappedValue == new Variant(3.0);
                },
                "the replacement child keeps its live callback").ConfigureAwait(false);

            Assert.That(await store.TryGetNodeAsync(oldChild.NodeId).ConfigureAwait(false), Is.Null,
                "a stale descendant must not be republished as a standalone root");
            await local.RemoveNodeAsync(replacement.NodeId).ConfigureAwait(false);
            Assert.That(synchronizer.TrackedNodeCount, Is.Zero);
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

            Assert.That(writer.Start, Throws.Nothing);
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
            await Assert.ThatAsync(
                async () => await writer.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing).ConfigureAwait(false);
        }

        /// <summary>
        /// Waits for snapshot cleanup started reentrantly by the last outbound publication during disposal.
        /// </summary>
        [Test]
        public async Task DisposalAwaitsSnapshotStartedByOutboundWorkerAsync()
        {
            var store = new Mock<INodeStateStore>();
            Mock<INodeStateSnapshotStore> snapshots = store.As<INodeStateSnapshotStore>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var local = new DictionaryAddressSpace(m_systemContext);
            await using var synchronizer = new AddressSpaceSynchronizer(store.Object, local, () => true);
            Task? disposal = null;
            snapshots.Setup(value => value.WriteSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken _) =>
                {
                    disposal = synchronizer.DisposeAsync().AsTask();
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    finished.TrySetResult(true);
                });
            synchronizer.Start();
            BaseDataVariableState node = NewVariable("snapshot", 0.0);
            await local.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            for (int value = 1; value <= 1024; value++)
            {
                node.Value = new Variant((double)value);
                node.ClearChangeMasks(m_systemContext, false);
            }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(disposal, Is.Not.Null);
            try
            {
                await Assert.ThatAsync(
                    () => disposal!.WaitAsync(TimeSpan.FromMilliseconds(200)),
                    Throws.TypeOf<TimeoutException>()).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await disposal!.ConfigureAwait(false);
            }
            Assert.That(synchronizer.TrackedNodeCount, Is.Zero);
        }

        private static BaseDataVariableState NewVariable(string id, double value)
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

        private static ServiceResult ReadValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            return ServiceResult.Good;
        }

        private sealed class LifecycleNodeManager : AsyncCustomNodeManager
        {
            public LifecycleNodeManager(IServerInternal server)
                : base(server, new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration()
                }, "urn:test:sync")
            {
            }
        }

        private sealed class ConcreteObjectState : BaseObjectState
        {
            public ConcreteObjectState()
                : base(null)
            {
            }
        }

        private sealed class ConcreteMethodState : MethodState
        {
            public ConcreteMethodState(NodeState parent)
                : base(parent)
            {
            }
        }

        private sealed class RecordingAddressSpace : ILocalAddressSpace
        {
            public RecordingAddressSpace(ISystemContext context)
            {
                m_inner = new DictionaryAddressSpace(context);
            }

            public ISystemContext Context => m_inner.Context;

            public IEnumerable<NodeState> Nodes => m_inner.Nodes;

            public List<NodeId> RemovedNodeIds { get; } = [];

            public event Action<NodeState>? NodeAdded
            {
                add => m_inner.NodeAdded += value;
                remove => m_inner.NodeAdded -= value;
            }

            public event Action<NodeId>? NodeRemoved
            {
                add => m_inner.NodeRemoved += value;
                remove => m_inner.NodeRemoved -= value;
            }

            public bool TryGetNode(
                NodeId nodeId,
                [NotNullWhen(true)] out NodeState? node)
            {
                return m_inner.TryGetNode(nodeId, out node) ||
                    m_descendants.TryGetValue(nodeId, out node);
            }

            public async ValueTask AddOrUpdateNodeAsync(
                NodeState node,
                CancellationToken cancellationToken = default)
            {
                await m_inner
                    .AddOrUpdateNodeAsync(node, cancellationToken)
                    .ConfigureAwait(false);
                IndexDescendants(node);
            }

            public async ValueTask AddOrUpdateRangeAsync(
                IEnumerable<NodeState> nodes,
                CancellationToken cancellationToken = default)
            {
                var buffered = new List<NodeState>(nodes);
                await m_inner
                    .AddOrUpdateRangeAsync(buffered, cancellationToken)
                    .ConfigureAwait(false);
                foreach (NodeState node in buffered)
                {
                    IndexDescendants(node);
                }
            }

            public async ValueTask<bool> RemoveNodeAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                RemovedNodeIds.Add(nodeId);
                if (m_descendants.TryRemove(nodeId, out _))
                {
                    return true;
                }
                return await m_inner
                    .RemoveNodeAsync(nodeId, cancellationToken)
                    .ConfigureAwait(false);
            }

            private void IndexDescendants(NodeState node)
            {
                var children = new List<BaseInstanceState>();
                node.GetChildren(Context, children);
                foreach (BaseInstanceState child in children)
                {
                    if (!child.NodeId.IsNull)
                    {
                        m_descendants[child.NodeId] = child;
                    }
                    IndexDescendants(child);
                }
            }

            private readonly DictionaryAddressSpace m_inner;
            private readonly NodeIdDictionary<NodeState> m_descendants = [];
        }

        private sealed class OverlappingHydrationStore : INodeStateStore, INodeStateSnapshotStore, ISequencedNodeStateStore
        {
            public OverlappingHydrationStore(IStoredNode recreated, bool useSnapshot)
            {
                m_recreated = recreated;
                m_useSnapshot = useSnapshot;
            }

            public ulong CurrentSequence { get; private set; }

            public void ObserveSequence(ulong sequence)
            {
                CurrentSequence = Math.Max(CurrentSequence, sequence);
            }

            public void PublishRecreation()
            {
                m_changes.Writer.TryWrite(new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Upsert,
                    NodeId = m_recreated.NodeId,
                    Node = m_recreated,
                    Sequence = 3
                });
            }

            public ValueTask UpsertNodeAsync(IStoredNode node, CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask<bool> DeleteNodeAsync(NodeId nodeId, CancellationToken ct = default)
            {
                return new ValueTask<bool>(false);
            }

            public ValueTask<IStoredNode?> TryGetNodeAsync(NodeId nodeId, CancellationToken ct = default)
            {
                return new ValueTask<IStoredNode?>(m_recreated);
            }

            public async IAsyncEnumerable<IStoredNode> EnumerateAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield return m_recreated;
            }

            public async IAsyncEnumerable<(IStoredNode Node, ulong Sequence)> EnumerateNodesWithSequenceAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield return (m_recreated, 3);
            }

            public ValueTask WriteValueAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask<(bool Found, DataValue Value)> TryReadValueAsync(
                NodeId nodeId,
                CancellationToken ct = default)
            {
                return new ValueTask<(bool, DataValue)>((false, DataValue.Null));
            }

            public async IAsyncEnumerable<(NodeId NodeId, DataValue Value)> EnumerateValuesAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public async IAsyncEnumerable<(NodeId NodeId, DataValue Value, ulong Sequence)> EnumerateValuesWithSequenceAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public IAsyncEnumerable<NodeStateChange> SubscribeChangesAsync(CancellationToken ct = default)
            {
                return m_changes.Reader.ReadAllAsync(ct);
            }

            public ValueTask WriteSnapshotAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask<NodeStateSnapshot?> TryReadSnapshotAsync(CancellationToken ct = default)
            {
                return new ValueTask<NodeStateSnapshot?>(
                    m_useSnapshot ? new NodeStateSnapshot(1, ReadSnapshotEntriesAsync(ct)) : null);
            }

            public async IAsyncEnumerable<NodeStateChange> ReadDeltaLogAsync(
                ulong fromSequenceExclusive,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                if (fromSequenceExclusive < 2)
                {
                    yield return new NodeStateChange
                    {
                        Kind = NodeStateChangeKind.Delete,
                        NodeId = m_recreated.NodeId,
                        Sequence = 2
                    };
                }
            }

            private async IAsyncEnumerable<NodeStateChange> ReadSnapshotEntriesAsync(
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield return new NodeStateChange
                {
                    Kind = NodeStateChangeKind.Upsert,
                    NodeId = m_recreated.NodeId,
                    Node = m_recreated,
                    Sequence = 3
                };
            }

            private readonly IStoredNode m_recreated;
            private readonly bool m_useSnapshot;
            private readonly Channel<NodeStateChange> m_changes = Channel.CreateUnbounded<NodeStateChange>();
        }

        private sealed class ScriptedNodeStateStore : INodeStateStore
        {
            public void Publish(NodeStateChange change)
            {
                m_changes.Writer.TryWrite(change);
            }

            public ValueTask UpsertNodeAsync(
                IStoredNode node,
                CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask<bool> DeleteNodeAsync(
                NodeId nodeId,
                CancellationToken ct = default)
            {
                return new ValueTask<bool>(false);
            }

            public ValueTask<IStoredNode?> TryGetNodeAsync(
                NodeId nodeId,
                CancellationToken ct = default)
            {
                return new ValueTask<IStoredNode?>((IStoredNode?)null);
            }

            public async IAsyncEnumerable<IStoredNode> EnumerateAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public ValueTask WriteValueAsync(
                NodeId nodeId,
                in DataValue value,
                CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask<(bool Found, DataValue Value)> TryReadValueAsync(
                NodeId nodeId,
                CancellationToken ct = default)
            {
                return new ValueTask<(bool, DataValue)>((false, DataValue.Null));
            }

            public async IAsyncEnumerable<(NodeId NodeId, DataValue Value)>
                EnumerateValuesAsync(
                    [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public IAsyncEnumerable<NodeStateChange> SubscribeChangesAsync(
                CancellationToken ct = default)
            {
                return m_changes.Reader.ReadAllAsync(ct);
            }

            private readonly Channel<NodeStateChange> m_changes =
                Channel.CreateUnbounded<NodeStateChange>();
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
