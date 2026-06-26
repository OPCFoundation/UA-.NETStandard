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

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Distributed
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
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
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
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("var1", NamespaceIndex);
            ByteString payload = ByteString.From(new byte[] { 1, 2, 3, 4 });

            await store.UpsertNodeAsync(new StoredNode(nodeId, payload));
            IStoredNode? loaded = await store.TryGetNodeAsync(nodeId);
            bool deleted = await store.DeleteNodeAsync(nodeId);
            IStoredNode? afterDelete = await store.TryGetNodeAsync(nodeId);

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
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var a = new NodeId("a", NamespaceIndex);
            var b = new NodeId("b", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(a, ByteString.From(new byte[] { 1 })));
            await store.UpsertNodeAsync(new StoredNode(b, ByteString.From(new byte[] { 2 })));

            var ids = new System.Collections.Generic.List<NodeId>();
            await foreach (IStoredNode node in store.EnumerateAsync())
            {
                ids.Add(node.NodeId);
            }

            Assert.That(ids, Is.EquivalentTo(new[] { a, b }));
        }

        [Test]
        public async Task WriteAndReadValueRoundTripsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("v", NamespaceIndex);
            var original = new DataValue(new Variant(42.0), StatusCodes.Good, DateTimeUtc.Now);

            await store.WriteValueAsync(nodeId, original);
            (bool found, DataValue read) = await store.TryReadValueAsync(nodeId);

            Assert.That(found, Is.True);
            Assert.That(read.WrappedValue, Is.EqualTo(original.WrappedValue));
            Assert.That(read.StatusCode, Is.EqualTo(original.StatusCode));
            Assert.That(read.SourceTimestamp, Is.EqualTo(original.SourceTimestamp));
        }

        [Test]
        public async Task TryReadValueMissingReturnsFalseAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new InMemoryNodeStateStore(kv, m_messageContext);

            (bool found, DataValue read) = await store.TryReadValueAsync(new NodeId("nope", NamespaceIndex));

            Assert.That(found, Is.False);
            Assert.That(read.IsNull, Is.True);
        }

        [Test]
        public async Task SubscribeObservesTopologyAndValueChangesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
            using var cts = new CancellationTokenSource();
            var nodeId = new NodeId("watched", NamespaceIndex);

            await using System.Collections.Generic.IAsyncEnumerator<NodeStateChange> changes =
                store.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();

            ValueTask<bool> upsert = changes.MoveNextAsync();
            await store.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(new byte[] { 7 })));
            Assert.That(await upsert, Is.True);
            Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Upsert));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));
            Assert.That(changes.Current.Node, Is.Not.Null);

            ValueTask<bool> valueChange = changes.MoveNextAsync();
            await store.WriteValueAsync(nodeId, new DataValue(new Variant(1.0), StatusCodes.Good, DateTimeUtc.Now));
            Assert.That(await valueChange, Is.True);
            Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Value));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));

            ValueTask<bool> delete = changes.MoveNextAsync();
            await store.DeleteNodeAsync(nodeId);
            Assert.That(await delete, Is.True);
            Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Delete));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));

            cts.Cancel();
        }

        [Test]
        public async Task NodeStateBinaryRoundTripThroughStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
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
            await store.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(stream.ToArray())));

            IStoredNode? stored = await store.TryGetNodeAsync(nodeId);
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
            var store = new InMemoryNodeStateStore(kv, m_messageContext, protector);
            var nodeId = new NodeId("secret", NamespaceIndex);
            ByteString payload = ByteString.From(new byte[] { 9, 8, 7, 6 });

            await store.UpsertNodeAsync(new StoredNode(nodeId, payload));
            await store.WriteValueAsync(nodeId, new DataValue(new Variant(99.0), StatusCodes.Good));

            IStoredNode? node = await store.TryGetNodeAsync(nodeId);
            (bool found, DataValue value) = await store.TryReadValueAsync(nodeId);

            // The bytes persisted in the backend must not be the plaintext.
            (bool rawFound, ByteString raw) = await kv.TryGetAsync("n/" + nodeId);
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
            var store = new InMemoryNodeStateStore(kv, m_messageContext, protector);
            var nodeId = new NodeId("tampered", NamespaceIndex);

            await store.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(new byte[] { 1, 2, 3 })));

            // A compromised store / rogue replica forges the persisted record.
            await kv.SetAsync("n/" + nodeId, ByteString.From(new byte[] { 66, 66, 66, 66, 66 }));

            IStoredNode? node = await store.TryGetNodeAsync(nodeId);

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

            await writer.UpsertNodeAsync(new StoredNode(nodeId, ByteString.From(new byte[] { 5, 5 })));

            IStoredNode? node = await reader.TryGetNodeAsync(nodeId);

            Assert.That(node, Is.Null);
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
