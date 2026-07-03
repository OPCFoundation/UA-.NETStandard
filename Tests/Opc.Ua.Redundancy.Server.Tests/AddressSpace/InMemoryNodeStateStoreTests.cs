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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// IDE0230: the byte[] fixtures here are arbitrary binary payloads (e.g. forged/rogue
//   records), not text; rewriting them as UTF-8 (u8) string literals would misrepresent
//   the intent. Suppressed file-level for the suite.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="InMemoryNodeStateStore"/>, including a real
    /// <c>NodeState</c> binary round-trip through the store.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryNodeStateStoreTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext;
        private SystemContext m_systemContext;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:distributed");
            m_messageContext = messageContext;
            m_systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task UpsertTryGetAndDeleteNodeAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("var1", NamespaceIndex);
            var payload = ByteString.From(new byte[] { 1, 2, 3, 4 });

            await store.UpsertNodeAsync(new StoredNode(nodeId, payload)).ConfigureAwait(false);
            IStoredNode? loaded = await store.TryGetNodeAsync(nodeId).ConfigureAwait(false);
            bool deleted = await store.DeleteNodeAsync(nodeId).ConfigureAwait(false);
            IStoredNode? afterDelete = await store.TryGetNodeAsync(nodeId).ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.NodeId, Is.EqualTo(nodeId));
            Assert.That(loaded.Payload.ToArray(), Is.EqualTo(payload.ToArray()));
            Assert.That(deleted, Is.True);
            Assert.That(afterDelete, Is.Null);
        }

        [Test]
        public async Task EnumerateReturnsAllStoredNodesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            var b = new NodeId("b", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(a, ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);
            await store.UpsertNodeAsync(new StoredNode(b, ByteString.From(new byte[] { 2 }))).ConfigureAwait(false);

            var ids = new List<NodeId>();
            await foreach (IStoredNode node in store.EnumerateAsync())
            {
                ids.Add(node.NodeId);
            }

            Assert.That(ids, Is.EquivalentTo([a, b]));
        }

        [Test]
        public async Task WriteAndReadValueRoundTripsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("v", NamespaceIndex);
            var original = new DataValue(new Variant(42.0), StatusCodes.Good, DateTimeUtc.Now);

            await store.WriteValueAsync(nodeId, original).ConfigureAwait(false);
            (bool found, DataValue read) = await store.TryReadValueAsync(nodeId).ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(read.WrappedValue, Is.EqualTo(original.WrappedValue));
            Assert.That(read.StatusCode, Is.EqualTo(original.StatusCode));
            Assert.That(read.SourceTimestamp, Is.EqualTo(original.SourceTimestamp));
        }

        [Test]
        public async Task TryReadValueMissingReturnsFalseAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);

            (bool found, DataValue read) = await store.TryReadValueAsync(new NodeId("nope", NamespaceIndex)).ConfigureAwait(false);

            Assert.That(found, Is.False);
            Assert.That(read.IsNull, Is.True);
        }

        [Test]
        public async Task EnumerateValuesReturnsAllStoredValuesInOnePassAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            var b = new NodeId("b", NamespaceIndex);
            await store.WriteValueAsync(a, new DataValue(new Variant(1.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);
            await store.WriteValueAsync(b, new DataValue(new Variant(2.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            var seen = new Dictionary<NodeId, DataValue>();
            await foreach ((NodeId nodeId, DataValue value) in store.EnumerateValuesAsync())
            {
                seen[nodeId] = value;
            }

            Assert.That(seen.Keys, Is.EquivalentTo([a, b]));
            Assert.That(seen[a].WrappedValue, Is.EqualTo(new Variant(1.0)));
            Assert.That(seen[b].WrappedValue, Is.EqualTo(new Variant(2.0)));
        }

        [Test]
        public async Task EnumerateValuesRoundTripsThroughRecordProtectorAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(i + 1);
            }
            using var protector = new AesCbcHmacRecordProtector(key);
            using var store = new InMemoryNodeStateStore(kv, m_messageContext, protector);
            var nodeId = new NodeId("protected", NamespaceIndex);
            await store.WriteValueAsync(nodeId, new DataValue(new Variant(7.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            var seen = new Dictionary<NodeId, DataValue>();
            await foreach ((NodeId id, DataValue value) in store.EnumerateValuesAsync())
            {
                seen[id] = value;
            }

            Assert.That(seen.Keys, Is.EquivalentTo([nodeId]));
            Assert.That(seen[nodeId].WrappedValue, Is.EqualTo(new Variant(7.0)));
        }

        [Test]
        public async Task EnumerateValuesOnEmptyStoreYieldsNothingAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);

            int count = 0;
            await foreach ((NodeId _, DataValue _) in store.EnumerateValuesAsync())
            {
                count++;
            }

            Assert.That(count, Is.Zero);
        }

        [Test]
        public async Task WriteAndReadSnapshotRoundTripsEntriesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            var b = new NodeId("b", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(a, ByteString.From(new byte[] { 1, 2 }))).ConfigureAwait(false);
            await store.UpsertNodeAsync(new StoredNode(b, ByteString.From(new byte[] { 3 }))).ConfigureAwait(false);
            await store.WriteValueAsync(a, new DataValue(new Variant(5.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            await store.WriteSnapshotAsync().ConfigureAwait(false);
            NodeStateSnapshot? snapshot = await store.TryReadSnapshotAsync().ConfigureAwait(false);

            Assert.That(snapshot, Is.Not.Null);
            var upserts = new HashSet<NodeId>();
            var values = new Dictionary<NodeId, DataValue>();
            await foreach (NodeStateChange entry in snapshot!.Entries)
            {
                if (entry.Kind == NodeStateChangeKind.Upsert)
                {
                    upserts.Add(entry.NodeId);
                }
                else if (entry.Kind == NodeStateChangeKind.Value)
                {
                    values[entry.NodeId] = entry.Value;
                }
            }

            Assert.That(upserts, Is.EquivalentTo([a, b]));
            Assert.That(values.Keys, Is.EquivalentTo([a]));
            Assert.That(values[a].WrappedValue, Is.EqualTo(new Variant(5.0)));
            Assert.That(snapshot.Sequence, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public async Task TryReadSnapshotReturnsNullWhenNonePublishedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);

            NodeStateSnapshot? snapshot = await store.TryReadSnapshotAsync().ConfigureAwait(false);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public async Task DeltaLogReplaysOnlyChangesAfterSequenceAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(a, ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);
            ulong afterUpsert = store.CurrentSequence;
            await store.WriteValueAsync(a, new DataValue(new Variant(9.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            var replayed = new List<NodeStateChange>();
            await foreach (NodeStateChange change in store.ReadDeltaLogAsync(afterUpsert))
            {
                replayed.Add(change);
            }

            Assert.That(replayed, Has.Count.EqualTo(1));
            Assert.That(replayed[0].Kind, Is.EqualTo(NodeStateChangeKind.Value));
            Assert.That(replayed[0].NodeId, Is.EqualTo(a));
            Assert.That(replayed[0].Sequence, Is.GreaterThan(afterUpsert));
        }

        [Test]
        public async Task WriteSnapshotTrimsDeltaLogAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(a, ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);
            await store.WriteValueAsync(a, new DataValue(new Variant(2.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);

            await store.WriteSnapshotAsync().ConfigureAwait(false);

            var replayed = new List<NodeStateChange>();
            await foreach (NodeStateChange change in store.ReadDeltaLogAsync(0))
            {
                replayed.Add(change);
            }

            Assert.That(replayed, Is.Empty);
        }

        [Test]
        public void ObserveSequenceRaisesHighWaterMarkOnly()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);

            store.ObserveSequence(50);
            Assert.That(store.CurrentSequence, Is.EqualTo(50));
            store.ObserveSequence(10);
            Assert.That(store.CurrentSequence, Is.EqualTo(50));
        }

        [Test]
        public async Task SubscribeObservesTopologyAndValueChangesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            using var cts = new CancellationTokenSource();
            var nodeId = new NodeId("watched", NamespaceIndex);

            await using IAsyncEnumerator<NodeStateChange> changes =
                store.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();

            ValueTask<bool> upsert = changes.MoveNextAsync();
            await store.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(new byte[] { 7 }))).ConfigureAwait(false);
            Assert.That(await upsert.ConfigureAwait(false), Is.True);
            Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Upsert));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));
            Assert.That(changes.Current.Node, Is.Not.Null);

            ValueTask<bool> valueChange = changes.MoveNextAsync();
            await store.WriteValueAsync(nodeId, new DataValue(new Variant(1.0), StatusCodes.Good, DateTimeUtc.Now)).ConfigureAwait(false);
            Assert.That(await valueChange.ConfigureAwait(false), Is.True);
            Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Value));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));

            ValueTask<bool> delete = changes.MoveNextAsync();
            await store.DeleteNodeAsync(nodeId).ConfigureAwait(false);
            Assert.That(await delete.ConfigureAwait(false), Is.True);
            Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Delete));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));

            cts.Cancel();
        }

        [Test]
        public async Task SubscribePollsWhenStoreHasNoWatchAsync()
        {
            // A CRDT store has no change-feed (WatchAsync throws NotSupported),
            // so the standby subscription must fall back to scan-polling instead
            // of silently stopping.
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            using var store = new InMemoryNodeStateStore(
                crdt, m_messageContext, null, TimeSpan.FromMilliseconds(50));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var nodeId = new NodeId("polled", NamespaceIndex);

            await using IAsyncEnumerator<NodeStateChange> changes =
                store.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();

            // Start the subscription so the baseline scan runs, then upsert a
            // node; a subsequent poll observes it as an Upsert.
            ValueTask<bool> upsert = changes.MoveNextAsync();
            await Task.Delay(120, cts.Token).ConfigureAwait(false);
            await store.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(new byte[] { 9 })), cts.Token).ConfigureAwait(false);

            Assert.That(await upsert.ConfigureAwait(false), Is.True);
            Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Upsert));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));

            cts.Cancel();
        }

        [Test]
        public async Task NodeStateBinaryRoundTripThroughStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("sensor", NamespaceIndex);

            var original = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Sensor", NamespaceIndex),
                DisplayName = new LocalizedText("Sensor"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(3.14)
            };

            using var stream = new MemoryStream();
            original.SaveAsBinary(m_systemContext, stream);
            await store.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(stream.ToArray()))).ConfigureAwait(false);

            IStoredNode? stored = await store.TryGetNodeAsync(nodeId).ConfigureAwait(false);
            Assert.That(stored, Is.Not.Null);

            var restored = new BaseDataVariableState(null);
            using var loadStream = new MemoryStream(stored!.Payload.ToArray());
            restored.LoadAsBinary(m_systemContext, loadStream);

            Assert.That(restored.BrowseName, Is.EqualTo(original.BrowseName));
            Assert.That(restored.Value, Is.EqualTo(original.Value));
        }

        [Test]
        public async Task ProtectedNodeAndValueRoundTripAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(11));
            using var store = new InMemoryNodeStateStore(kv, m_messageContext, protector);
            var nodeId = new NodeId("secret", NamespaceIndex);
            var payload = ByteString.From(new byte[] { 9, 8, 7, 6 });

            await store.UpsertNodeAsync(new StoredNode(nodeId, payload)).ConfigureAwait(false);
            await store.WriteValueAsync(nodeId, new DataValue(new Variant(99.0), StatusCodes.Good)).ConfigureAwait(false);

            IStoredNode? node = await store.TryGetNodeAsync(nodeId).ConfigureAwait(false);
            (bool found, DataValue value) = await store.TryReadValueAsync(nodeId).ConfigureAwait(false);

            // The bytes persisted in the backend must not be the plaintext.
            (bool rawFound, ByteString raw) = await kv.TryGetAsync("n/" + nodeId).ConfigureAwait(false);
            Assert.That(rawFound, Is.True);
            Assert.That(raw.ToArray(), Is.Not.EqualTo(payload.ToArray()));

            Assert.That(node, Is.Not.Null);
            Assert.That(node!.Payload.ToArray(), Is.EqualTo(payload.ToArray()));
            Assert.That(found, Is.True);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(99.0)));
        }

        [Test]
        public async Task TamperedProtectedNodeIsRejectedFailClosedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(12));
            using var store = new InMemoryNodeStateStore(kv, m_messageContext, protector);
            var nodeId = new NodeId("tampered", NamespaceIndex);

            await store.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(new byte[] { 1, 2, 3 }))).ConfigureAwait(false);

            // A compromised store / rogue replica forges the persisted record.
            await kv.SetAsync("n/" + nodeId, ByteString.From(new byte[] { 66, 66, 66, 66, 66 })).ConfigureAwait(false);

            IStoredNode? node = await store.TryGetNodeAsync(nodeId).ConfigureAwait(false);

            Assert.That(node, Is.Null);
        }

        [Test]
        public async Task NodeProtectedUnderDifferentKeyIsRejectedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerProtector = new AesCbcHmacRecordProtector(MakeKey(13));
            using var readerProtector = new AesCbcHmacRecordProtector(MakeKey(14));
            var writer = new InMemoryNodeStateStore(kv, m_messageContext, writerProtector);
            var reader = new InMemoryNodeStateStore(kv, m_messageContext, readerProtector);
            var nodeId = new NodeId("crosskey", NamespaceIndex);

            await writer.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(new byte[] { 5, 5 }))).ConfigureAwait(false);

            IStoredNode? node = await reader.TryGetNodeAsync(nodeId).ConfigureAwait(false);

            Assert.That(node, Is.Null);
        }

        [Test]
        public void UpsertNodeRejectsNullArgument()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);

            Assert.That(
                async () => await store.UpsertNodeAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task DeltaLogReplaysUpsertAndDeleteChangesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(a, ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);
            bool deleted = await store.DeleteNodeAsync(a).ConfigureAwait(false);

            var replayed = new List<NodeStateChange>();
            await foreach (NodeStateChange change in store.ReadDeltaLogAsync(0))
            {
                replayed.Add(change);
            }

            Assert.That(deleted, Is.True);
            Assert.That(replayed, Has.Count.EqualTo(2));
            Assert.That(replayed[0].Kind, Is.EqualTo(NodeStateChangeKind.Upsert));
            Assert.That(replayed[0].NodeId, Is.EqualTo(a));
            Assert.That(replayed[0].Node, Is.Not.Null);
            Assert.That(replayed[1].Kind, Is.EqualTo(NodeStateChangeKind.Delete));
            Assert.That(replayed[1].NodeId, Is.EqualTo(a));
        }

        [Test]
        public async Task WriteSnapshotOnEmptyStoreProducesEmptySnapshotAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);

            await store.WriteSnapshotAsync().ConfigureAwait(false);
            NodeStateSnapshot? snapshot = await store.TryReadSnapshotAsync().ConfigureAwait(false);

            Assert.That(snapshot, Is.Not.Null);
            int count = 0;
            await foreach (NodeStateChange _ in snapshot!.Entries)
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        [Test]
        public async Task RepeatedSnapshotsCollectSupersededGenerationAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(a, ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);

            await store.WriteSnapshotAsync().ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);

            int generations = await CountSnapshotGenerationsAsync(kv).ConfigureAwait(false);
            NodeStateSnapshot? snapshot = await store.TryReadSnapshotAsync().ConfigureAwait(false);

            // The predecessor-of-predecessor generation is garbage-collected once
            // the third manifest is published, leaving the live and one retained.
            Assert.That(generations, Is.EqualTo(2));
            Assert.That(snapshot, Is.Not.Null);
        }

        [Test]
        public async Task EnumerateSkipsMalformedNodeKeysAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var valid = new NodeId("good", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(valid, ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);

            // A bare-prefix key and an unparseable NodeId suffix must both be skipped.
            var record = ByteString.From(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 1 });
            await kv.SetAsync("n/", record).ConfigureAwait(false);
            await kv.SetAsync("n/i=abc", record).ConfigureAwait(false);

            var ids = new List<NodeId>();
            await foreach (IStoredNode node in store.EnumerateAsync())
            {
                ids.Add(node.NodeId);
            }

            Assert.That(ids, Is.EquivalentTo([valid]));
        }

        [Test]
        public async Task TryGetNodeReturnsNullForTruncatedRecordAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("short", NamespaceIndex);

            // A record shorter than the 8-byte sequence header cannot be split.
            await kv.SetAsync("n/" + nodeId, ByteString.From(new byte[] { 1, 2, 3 })).ConfigureAwait(false);

            IStoredNode? node = await store.TryGetNodeAsync(nodeId).ConfigureAwait(false);

            Assert.That(node, Is.Null);
        }

        private static async Task<int> CountSnapshotGenerationsAsync(InMemorySharedKeyValueStore kv)
        {
            var generations = new HashSet<string>(StringComparer.Ordinal);
            await foreach (KeyValuePair<string, ByteString> entry in kv.ScanAsync("snap/"))
            {
                string remainder = entry.Key["snap/".Length..];
                string[] parts = remainder.Split('/');
                if (parts.Length > 1 && parts[0].Length > 0)
                {
                    generations.Add(parts[0]);
                }
            }
            return generations.Count;
        }

        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(seed + i);
            }
            return key;
        }
    }
}
