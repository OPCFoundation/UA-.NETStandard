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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
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
    /// Unit tests for <see cref="InMemoryNodeStateStore"/>, including a real
    /// <c>NodeState</c> binary round-trip through the store.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryNodeStateStoreTests
    {
        private const ushort NamespaceIndex = 1;
        private ServiceMessageContext m_messageContext;
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
        public async Task TryReadSnapshotRejectsInvalidGuidLengthsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            using var encoder = new BinaryEncoder(m_messageContext);
            encoder.WriteByteString(null, new ByteString(new byte[15]));
            encoder.WriteInt32(null, 0);
            encoder.WriteUInt64(null, 0);
            encoder.WriteByteString(null, new ByteString(new byte[16]));
            byte[]? manifest = encoder.CloseAndReturnBuffer();
            Assert.That(manifest, Is.Not.Null);
            await kv.SetAsync(
                "snapmeta/manifest",
                new ByteString(manifest!)).ConfigureAwait(false);

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

        /// <summary>
        /// Prevents another store from snapshotting an unfinished write and prevents a delayed
        /// older primary CAS from overwriting a newer completed row.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SnapshotRejectsUnfinishedPublicationAcrossStoreInstancesAsync(bool beforePrimary)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var nodeId = new NodeId("overlap", NamespaceIndex);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISharedKeyValueStore> controlled = CreateStoreMock(kv);
            if (beforePrimary)
            {
                controlled.Setup(s => s.CompareAndSwapAsync(
                        "n/" + nodeId,
                        It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns(WritePrimaryAsync);
            }
            else
            {
                controlled.Setup(s => s.SetAsync(
                        "dlog/00000000000000000001", It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns(WriteDeltaAsync);
            }

            async ValueTask<bool> WritePrimaryAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return await kv.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
            }

            async ValueTask WriteDeltaAsync(string key, ByteString value, CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                await kv.SetAsync(key, value, ct).ConfigureAwait(false);
            }

            using var earlier = new InMemoryNodeStateStore(controlled.Object, m_messageContext);
            using var later = new InMemoryNodeStateStore(kv, m_messageContext);
            using var snapshotWriter = new InMemoryNodeStateStore(kv, m_messageContext);
            Task publication = earlier.UpsertNodeAsync(
                new StoredNode(nodeId, new ByteString(new byte[] { 1 })), cts.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                await later.UpsertNodeAsync(
                    new StoredNode(nodeId, new ByteString(new byte[] { 2 })), cts.Token).ConfigureAwait(false);

                await Assert.ThatAsync(
                    async () => await snapshotWriter.WriteSnapshotAsync(cts.Token).ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains("unfinished")).ConfigureAwait(false);
                Assert.That(await snapshotWriter.TryReadSnapshotAsync(cts.Token).ConfigureAwait(false), Is.Null);
                List<NodeStateChange> retained = await ReadChangesAsync(snapshotWriter.ReadDeltaLogAsync(0, cts.Token))
                    .ConfigureAwait(false);
                Assert.That(retained, Has.Count.EqualTo(1));
                Assert.That(retained[0].Sequence, Is.EqualTo(2));
            }
            finally
            {
                release.TrySetResult(true);
                await publication.ConfigureAwait(false);
            }

            IStoredNode? newest = await snapshotWriter.TryGetNodeAsync(nodeId, cts.Token).ConfigureAwait(false);
            Assert.That(newest, Is.Not.Null);
            Assert.That(newest!.Payload, Is.EqualTo(new ByteString(new byte[] { 2 })));
            await snapshotWriter.WriteSnapshotAsync(cts.Token).ConfigureAwait(false);
            NodeStateSnapshot? snapshot = await snapshotWriter.TryReadSnapshotAsync(cts.Token).ConfigureAwait(false);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.Sequence, Is.EqualTo(2));
            Assert.That(await CountKeysAsync(kv, "dlog/").ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Keeps failed or cancelled reservations in shared state so restart cannot trim later completed deltas.
        /// </summary>
        /// <exception cref="IOException">The test backend deliberately rejects a delta publication.</exception>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnfinishedPublicationSurvivesFailureAndRestartAsync(bool cancel)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISharedKeyValueStore> controlled = CreateStoreMock(kv);
            controlled.Setup(s => s.SetAsync(
                    "dlog/00000000000000000001", It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(FailPublicationAsync);

            async ValueTask FailPublicationAsync(string key, ByteString value, CancellationToken ct)
            {
                entered.TrySetResult(true);
                if (cancel)
                {
                    await blocked.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                throw new IOException($"The delta '{key}' ({value.Length} bytes) could not be published.");
            }

            using (var writer = new InMemoryNodeStateStore(controlled.Object, m_messageContext))
            {
                Task publication = writer.UpsertNodeAsync(new StoredNode(
                    new NodeId("unfinished", NamespaceIndex),
                    new ByteString(new byte[] { 1 })), cts.Token).AsTask();
                await entered.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                if (cancel)
                {
                    cts.Cancel();
                    await Assert.ThatAsync(
                        async () => await publication.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(
                        async () => await publication.ConfigureAwait(false),
                        Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
            }

            using var restarted = new InMemoryNodeStateStore(kv, m_messageContext);
            await restarted.UpsertNodeAsync(new StoredNode(
                new NodeId("completed", NamespaceIndex),
                new ByteString(new byte[] { 2 }))).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await restarted.WriteSnapshotAsync().ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("publication 1 is unfinished"))
                .ConfigureAwait(false);

            List<NodeStateChange> retained = await ReadChangesAsync(restarted.ReadDeltaLogAsync(0))
                .ConfigureAwait(false);
            Assert.That(retained, Has.Count.EqualTo(1));
            Assert.That(retained[0].Sequence, Is.EqualTo(2));
            (bool found, ByteString coordinator) = await kv.TryGetAsync(InMemoryNodeStateStore.SequenceKey)
                .ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(coordinator.Length, Is.EqualTo(2 * sizeof(ulong)));
            Assert.That(BinaryPrimitives.ReadUInt64BigEndian(coordinator.Span), Is.EqualTo(2));
            Assert.That(BinaryPrimitives.ReadUInt64BigEndian(coordinator.Span[sizeof(ulong)..]), Is.EqualTo(1));
        }

        /// <summary>
        /// Revalidates shared coordination after both scans, preserving the previous manifest and all newer deltas.
        /// </summary>
        [Test]
        public async Task SnapshotRejectsConcurrentPublicationDuringScansAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writer = new InMemoryNodeStateStore(kv, m_messageContext);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var nodeId = new NodeId("baseline", NamespaceIndex);
            await writer.UpsertNodeAsync(new StoredNode(nodeId, new ByteString(new byte[] { 1 })))
                .ConfigureAwait(false);
            await writer.WriteSnapshotAsync().ConfigureAwait(false);
            (_, ByteString manifest) = await kv.TryGetAsync("snapmeta/manifest").ConfigureAwait(false);
            await writer.UpsertNodeAsync(new StoredNode(nodeId, new ByteString(new byte[] { 2 })))
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISharedKeyValueStore> controlled = CreateStoreMock(kv);
            controlled.Setup(s => s.ScanAsync("v/", It.IsAny<CancellationToken>()))
                .Returns((string prefix, CancellationToken ct) => ScanValuesAsync(prefix, ct));

            async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanValuesAsync(
                string prefix,
                [EnumeratorCancellation] CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                await foreach (KeyValuePair<string, ByteString> entry in kv.ScanAsync(prefix, ct)
                    .ConfigureAwait(false))
                {
                    yield return entry;
                }
            }

            using var snapshotWriter = new InMemoryNodeStateStore(controlled.Object, m_messageContext);
            Task snapshot = snapshotWriter.WriteSnapshotAsync(cts.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                await writer.UpsertNodeAsync(new StoredNode(
                    new NodeId("concurrent", NamespaceIndex),
                    new ByteString(new byte[] { 3 })), cts.Token).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await Assert.ThatAsync(
                async () => await snapshot.ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);

            (_, ByteString unchanged) = await kv.TryGetAsync("snapmeta/manifest").ConfigureAwait(false);
            Assert.That(unchanged, Is.EqualTo(manifest));
            Assert.That(await CountSnapshotGenerationsAsync(kv).ConfigureAwait(false), Is.EqualTo(1));
            List<NodeStateChange> retained = await ReadChangesAsync(writer.ReadDeltaLogAsync(0)).ConfigureAwait(false);
            Assert.That(retained, Has.Count.EqualTo(2));
            Assert.That(retained[0].Sequence, Is.EqualTo(2));
            Assert.That(retained[1].Sequence, Is.EqualTo(3));
        }

        /// <summary>
        /// Retains chunks when an indeterminate manifest CAS actually committed before its caller observed failure.
        /// </summary>
        /// <exception cref="OperationCanceledException">The backend injects cancellation after manifest commit.</exception>
        /// <exception cref="IOException">The backend injects a lost response after manifest commit.</exception>
        [TestCase(false)]
        [TestCase(true)]
        public async Task IndeterminateManifestPublicationRetainsPossiblyPublishedChunksAsync(bool cancel)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writer = new InMemoryNodeStateStore(kv, m_messageContext);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var nodeId = new NodeId("manifest", NamespaceIndex);
            await writer.UpsertNodeAsync(new StoredNode(nodeId, new ByteString(new byte[] { 1 })))
                .ConfigureAwait(false);
            await writer.WriteSnapshotAsync().ConfigureAwait(false);
            await writer.UpsertNodeAsync(new StoredNode(nodeId, new ByteString(new byte[] { 2 })))
                .ConfigureAwait(false);
            Mock<ISharedKeyValueStore> controlled = CreateStoreMock(kv);
            controlled.Setup(s => s.CompareAndSwapAsync(
                    "snapmeta/manifest",
                    It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(PublishThenFailAsync);

            async ValueTask<bool> PublishThenFailAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct)
            {
                bool published = await kv.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                Assert.That(published, Is.True);
                if (cancel)
                {
                    cts.Cancel();
                    throw new OperationCanceledException(ct);
                }
                throw new IOException("The committed manifest response was lost.");
            }

            using var snapshotWriter = new InMemoryNodeStateStore(controlled.Object, m_messageContext);
            Task publication = snapshotWriter.WriteSnapshotAsync(cts.Token).AsTask();
            if (cancel)
            {
                await Assert.ThatAsync(
                    async () => await publication.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await publication.ConfigureAwait(false),
                    Throws.TypeOf<IOException>()).ConfigureAwait(false);
            }

            NodeStateSnapshot? snapshot = await writer.TryReadSnapshotAsync().ConfigureAwait(false);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.Sequence, Is.EqualTo(2));
            List<NodeStateChange> entries = await ReadChangesAsync(snapshot.Entries).ConfigureAwait(false);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Node!.Payload, Is.EqualTo(new ByteString(new byte[] { 2 })));
            Assert.That(await CountSnapshotGenerationsAsync(kv).ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await CountKeysAsync(kv, "dlog/").ConfigureAwait(false), Is.EqualTo(1));
        }

        /// <summary>
        /// Removes only the losing attempt's chunks when another instance publishes a snapshot first.
        /// </summary>
        [Test]
        public async Task ConcurrentSnapshotLoserCleansOnlyItsOwnGenerationAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var winner = new InMemoryNodeStateStore(kv, m_messageContext);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await winner.UpsertNodeAsync(new StoredNode(
                new NodeId("snapshot", NamespaceIndex),
                new ByteString(new byte[] { 1 }))).ConfigureAwait(false);
            await winner.WriteSnapshotAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISharedKeyValueStore> controlled = CreateStoreMock(kv);
            controlled.Setup(s => s.CompareAndSwapAsync(
                    "snapmeta/manifest",
                    It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(PublishLastAsync);

            async ValueTask<bool> PublishLastAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return await kv.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
            }

            using var loser = new InMemoryNodeStateStore(controlled.Object, m_messageContext);
            Task publication = loser.WriteSnapshotAsync(cts.Token).AsTask();
            ByteString manifest;
            try
            {
                await entered.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                await winner.WriteSnapshotAsync(cts.Token).ConfigureAwait(false);
                (_, manifest) = await kv.TryGetAsync("snapmeta/manifest", cts.Token).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await Assert.ThatAsync(
                async () => await publication.ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("concurrently")).ConfigureAwait(false);

            (_, ByteString unchanged) = await kv.TryGetAsync("snapmeta/manifest").ConfigureAwait(false);
            Assert.That(unchanged, Is.EqualTo(manifest));
            Assert.That(await CountSnapshotGenerationsAsync(kv).ConfigureAwait(false), Is.EqualTo(2));
            NodeStateSnapshot? snapshot = await winner.TryReadSnapshotAsync().ConfigureAwait(false);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(await ReadChangesAsync(snapshot!.Entries).ConfigureAwait(false), Has.Count.EqualTo(1));
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

        /// <summary>
        /// Preserves the unsigned high-water mark across the signed interlocked storage boundary.
        /// </summary>
        [Test]
        public async Task ObserveSequencePreservesUnsignedHighWaterMarkAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            const ulong highWaterMark = (ulong)long.MaxValue + 2;

            store.ObserveSequence(highWaterMark);
            store.ObserveSequence(1);
            await store.UpsertNodeAsync(
                new StoredNode(new NodeId("unsigned", NamespaceIndex), new ByteString(new byte[] { 1 })))
                .ConfigureAwait(false);

            Assert.That(store.CurrentSequence, Is.EqualTo(highWaterMark + 1));
            List<NodeStateChange> deltas = await ReadChangesAsync(store.ReadDeltaLogAsync(0)).ConfigureAwait(false);
            Assert.That(deltas, Has.Count.EqualTo(1));
            Assert.That(deltas[0].Sequence, Is.EqualTo(highWaterMark + 1));
        }

        /// <summary>
        /// Rejects exhausted shared or observed sequence numbers without wrapping or writing a node.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SequenceExhaustionRejectsBeforePrimaryMutationAsync(bool shared)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            if (shared)
            {
                await kv.SetAsync(
                    InMemoryNodeStateStore.SequenceKey,
                    MakeRecord(ulong.MaxValue, ByteString.Empty)).ConfigureAwait(false);
            }
            else
            {
                store.ObserveSequence(ulong.MaxValue);
            }

            await Assert.ThatAsync(
                async () => await store.UpsertNodeAsync(
                    new StoredNode(new NodeId("exhausted", NamespaceIndex), new ByteString(new byte[] { 1 })))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadOutOfRange)).ConfigureAwait(false);

            Assert.That(await CountKeysAsync(kv, "n/").ConfigureAwait(false), Is.Zero);
            Assert.That(await CountKeysAsync(kv, "dlog/").ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Assigns unique shared sequences across concurrent stores and a newly constructed writer.
        /// </summary>
        [Test]
        public async Task SharedCoordinatorAssignsUniqueSequencesAcrossStoreInstancesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var contendersReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int contenders = 0;
            Mock<ISharedKeyValueStore> controlled = CreateStoreMock(kv);
            controlled.Setup(s => s.CompareAndSwapAsync(
                    InMemoryNodeStateStore.SequenceKey,
                    It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(ReserveConcurrentlyAsync);

            async ValueTask<bool> ReserveConcurrentlyAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct)
            {
                int contender = Interlocked.Increment(ref contenders);
                if (contender <= 32)
                {
                    if (contender == 32)
                    {
                        contendersReady.TrySetResult(true);
                    }
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                return await kv.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
            }

            using var first = new InMemoryNodeStateStore(controlled.Object, m_messageContext);
            using var second = new InMemoryNodeStateStore(controlled.Object, m_messageContext);
            var writes = new List<Task>();
            for (int i = 0; i < 32; i++)
            {
                InMemoryNodeStateStore writer = i % 2 == 0 ? first : second;
                writes.Add(writer.UpsertNodeAsync(new StoredNode(
                    new NodeId(i.ToString(CultureInfo.InvariantCulture), NamespaceIndex),
                    new ByteString(new byte[] { 1 })), cts.Token).AsTask());
            }
            try
            {
                await contendersReady.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await Task.WhenAll(writes).ConfigureAwait(false);

            using var restarted = new InMemoryNodeStateStore(kv, m_messageContext);
            await restarted.UpsertNodeAsync(new StoredNode(
                new NodeId("restarted", NamespaceIndex),
                new ByteString(new byte[] { 2 }))).ConfigureAwait(false);
            List<NodeStateChange> deltas = await ReadChangesAsync(restarted.ReadDeltaLogAsync(0))
                .ConfigureAwait(false);

            Assert.That(deltas, Has.Count.EqualTo(33));
            for (int i = 0; i < deltas.Count; i++)
            {
                Assert.That(deltas[i].Sequence, Is.EqualTo((ulong)i + 1));
            }
            Assert.That(restarted.CurrentSequence, Is.EqualTo(33));
        }

        /// <summary>
        /// Leaves the coordinator and payload untouched when cancellation precedes sequence reservation.
        /// </summary>
        [Test]
        public async Task CancelledWriterDoesNotReserveASequenceAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThatAsync(
                async () => await store.UpsertNodeAsync(
                    new StoredNode(new NodeId("cancelled", NamespaceIndex), new ByteString(new byte[] { 1 })),
                    cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(await CountKeysAsync(kv, string.Empty).ConfigureAwait(false), Is.Zero);
            Assert.That(store.CurrentSequence, Is.Zero);
        }

        /// <summary>
        /// Fails a bare CRDT writer before any coordinator, primary, or delta mutation.
        /// </summary>
        [Test]
        public async Task BareCrdtWriterRejectsBeforeAnyMutationAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            using var store = new InMemoryNodeStateStore(crdt, m_messageContext);

            await Assert.ThatAsync(
                async () => await store.UpsertNodeAsync(
                    new StoredNode(new NodeId("unsupported", NamespaceIndex), new ByteString(new byte[] { 1 })))
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(InMemoryNodeStateStore.SequenceKey))
                .ConfigureAwait(false);

            Assert.That(await CountKeysAsync(crdt, string.Empty).ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Does not mistake an undisclosed store's CAS method for a declared linearizable coordinator.
        /// </summary>
        [Test]
        public async Task UndeclaredCoordinatorRejectsBeforeCallingTheBackendAsync()
        {
            var kv = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            using var store = new InMemoryNodeStateStore(kv.Object, m_messageContext);

            await Assert.ThatAsync(
                async () => await store.UpsertNodeAsync(
                    new StoredNode(new NodeId("unsupported", NamespaceIndex), new ByteString(new byte[] { 1 })))
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("declared linearizable"))
                .ConfigureAwait(false);

            kv.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Rejects a private in-memory counter for replicated payloads while retaining ordinary local construction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplicatedPayloadRejectsProcessLocalCoordinatorAsync(bool onlyDeltaReplicated)
        {
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            using var coordinator = new InMemorySharedKeyValueStore();
            ArrayOf<string> prefixes = onlyDeltaReplicated ? ["election/", "n/", "v/"] : default;
            await using var hybrid = new HybridSharedKeyValueStore(crdt, coordinator, prefixes);
            using var store = new InMemoryNodeStateStore(hybrid, m_messageContext);

            await Assert.ThatAsync(
                async () => await store.UpsertNodeAsync(
                    new StoredNode(new NodeId("unsupported", NamespaceIndex), new ByteString(new byte[] { 1 })))
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("process-local")).ConfigureAwait(false);

            Assert.That(await CountKeysAsync(crdt, string.Empty).ConfigureAwait(false), Is.Zero);
            Assert.That(await CountKeysAsync(coordinator, string.Empty).ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Preserves a corrupt coordinator record and rejects publication before mutating primary state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidCoordinatorRejectsBeforePrimaryMutationAsync(bool invalidReservation)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            ByteString record = invalidReservation
                ? MakeRecord(1, MakeRecord(2, ByteString.Empty))
                : new ByteString(new byte[] { 1, 2, 3 });
            await kv.SetAsync(InMemoryNodeStateStore.SequenceKey, record).ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await store.UpsertNodeAsync(
                    new StoredNode(new NodeId("invalid", NamespaceIndex), new ByteString(new byte[] { 1 })))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);

            Assert.That(await CountKeysAsync(kv, "n/").ConfigureAwait(false), Is.Zero);
            (bool found, ByteString unchanged) = await kv.TryGetAsync(InMemoryNodeStateStore.SequenceKey)
                .ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(unchanged, Is.EqualTo(record));
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
            await using var coordinator = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            await using var hybrid = new HybridSharedKeyValueStore(crdt, coordinator);
            var baseline = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISharedKeyValueStore> observed = CreateStoreMock(hybrid);
            observed.Setup(s => s.ScanAsync("dlog/", It.IsAny<CancellationToken>()))
                .Returns((string prefix, CancellationToken ct) => ObserveBaselineAsync(prefix, ct));

            async IAsyncEnumerable<KeyValuePair<string, ByteString>> ObserveBaselineAsync(
                string prefix,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await foreach (KeyValuePair<string, ByteString> entry in hybrid.ScanAsync(prefix, ct)
                    .ConfigureAwait(false))
                {
                    yield return entry;
                }
                baseline.TrySetResult(true);
            }

            using var store = new InMemoryNodeStateStore(
                observed.Object, m_messageContext, null, TimeSpan.FromMilliseconds(50));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var nodeId = new NodeId("polled", NamespaceIndex);

            await using IAsyncEnumerator<NodeStateChange> changes =
                store.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();

            Task<bool> pending = changes.MoveNextAsync().AsTask();
            try
            {
                await baseline.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                await store.UpsertNodeAsync(
                    new StoredNode(nodeId, ByteString.From(new byte[] { 9 })), cts.Token).ConfigureAwait(false);

                Assert.That(await pending.ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Upsert));
                Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));

                pending = changes.MoveNextAsync().AsTask();
                await store.DeleteNodeAsync(nodeId, cts.Token).ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await store.WriteSnapshotAsync(cts.Token).ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains("eventually consistent"))
                    .ConfigureAwait(false);

                Assert.That(await pending.ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Delete));
                Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));
            }
            finally
            {
                cts.Cancel();
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    // Drain an interrupted MoveNext before disposing the async iterator.
                }
            }
        }

        [Test]
        public async Task PreparedPollingSubscriptionEmitsCurrentStateEachTimeAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(),
                network.CreateTransport(),
                TimeProvider.System,
                CrdtReaderOptions.Default);
            await using var coordinator = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            await using var hybrid = new HybridSharedKeyValueStore(crdt, coordinator);
            using var store = new InMemoryNodeStateStore(
                hybrid,
                m_messageContext,
                null,
                TimeSpan.FromMilliseconds(25));
            var nodeId = new NodeId("prepared", NamespaceIndex);
            await store.UpsertNodeAsync(
                new StoredNode(nodeId, ByteString.From(new byte[] { 9 }))).ConfigureAwait(false);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                ((INodeStateSubscriptionPreparer)store).PrepareSubscription();
                await using IAsyncEnumerator<NodeStateChange> changes =
                    store.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();

                Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Kind, Is.EqualTo(NodeStateChangeKind.Upsert));
                Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));
                cts.Cancel();
            }
        }

        /// <summary>
        /// Replays a retained higher-sequence delta when a delayed earlier CRDT write wins the raw row.
        /// </summary>
        [Test]
        public async Task HybridPollingRecoversNewerDeltaAfterDelayedPrimaryWriteAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await using var coordinator = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            await using var hybrid = new HybridSharedKeyValueStore(crdt, coordinator);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var nodeId = new NodeId("delayed", NamespaceIndex);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISharedKeyValueStore> controlled = CreateStoreMock(hybrid);
            controlled.Setup(s => s.SetAsync("n/" + nodeId, It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(DelayPrimaryAsync);

            async ValueTask DelayPrimaryAsync(string key, ByteString value, CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                await hybrid.SetAsync(key, value, ct).ConfigureAwait(false);
            }

            using var earlier = new InMemoryNodeStateStore(controlled.Object, m_messageContext);
            using var later = new InMemoryNodeStateStore(hybrid, m_messageContext);
            Task delayed = earlier.UpsertNodeAsync(
                new StoredNode(nodeId, new ByteString(new byte[] { 1 })), cts.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                await later.UpsertNodeAsync(
                    new StoredNode(nodeId, new ByteString(new byte[] { 2 })), cts.Token).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                await delayed.ConfigureAwait(false);
            }

            IStoredNode? raw = await later.TryGetNodeAsync(nodeId, cts.Token).ConfigureAwait(false);
            Assert.That(raw, Is.Not.Null);
            Assert.That(raw!.Payload, Is.EqualTo(new ByteString(new byte[] { 1 })));
            Assert.That(((INodeStateStoreReadConsistency)later).HasAuthoritativeReads, Is.False);
            Assert.That(((INodeStateStoreReadConsistency)later).SupportsSnapshots, Is.False);
            await Assert.ThatAsync(
                async () => await later.WriteSnapshotAsync(cts.Token).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("must be retained")).ConfigureAwait(false);
            List<NodeStateChange> deltas = await ReadChangesAsync(later.ReadDeltaLogAsync(0, cts.Token))
                .ConfigureAwait(false);
            Assert.That(deltas, Has.Count.EqualTo(2));
            Assert.That(deltas[1].Sequence, Is.EqualTo(2));

            ((INodeStateSubscriptionPreparer)later).PrepareSubscription();
            await using IAsyncEnumerator<NodeStateChange> changes =
                later.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();
            do
            {
                Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            }
            while (changes.Current.Sequence < 2);
            Assert.That(changes.Current.Sequence, Is.EqualTo(2));
            Assert.That(changes.Current.NodeId, Is.EqualTo(nodeId));
            Assert.That(changes.Current.Node!.Payload, Is.EqualTo(new ByteString(new byte[] { 2 })));
            cts.Cancel();

            (bool bulkHasCounter, _) = await crdt.TryGetAsync(InMemoryNodeStateStore.SequenceKey)
                .ConfigureAwait(false);
            Assert.That(bulkHasCounter, Is.False, "the real CRDT backend must not hold or CAS the coordinator key");
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

        /// <summary>
        /// Rejects an unauthenticated sequence coordinator without changing its protected record or stored nodes.
        /// </summary>
        [Test]
        public async Task ProtectedCoordinatorRejectsWriterWithDifferentKeyAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var originalProtector = new AesCbcHmacRecordProtector(MakeKey(23));
            using var otherProtector = new AesCbcHmacRecordProtector(MakeKey(24));
            using var original = new InMemoryNodeStateStore(kv, m_messageContext, originalProtector);
            using var other = new InMemoryNodeStateStore(kv, m_messageContext, otherProtector);
            await original.UpsertNodeAsync(new StoredNode(
                new NodeId("original", NamespaceIndex),
                new ByteString(new byte[] { 1 }))).ConfigureAwait(false);
            (_, ByteString record) = await kv.TryGetAsync(InMemoryNodeStateStore.SequenceKey).ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await other.UpsertNodeAsync(new StoredNode(
                    new NodeId("unauthenticated", NamespaceIndex),
                    new ByteString(new byte[] { 2 }))).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);

            (_, ByteString unchanged) = await kv.TryGetAsync(InMemoryNodeStateStore.SequenceKey).ConfigureAwait(false);
            Assert.That(unchanged, Is.EqualTo(record));
            Assert.That(await CountKeysAsync(kv, "n/").ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await CountKeysAsync(kv, "dlog/").ConfigureAwait(false), Is.EqualTo(1));
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

        /// <summary>
        /// Rejects a truncated record under a valid node key instead of treating
        /// an incomplete, nonempty hydration scan as authoritative.
        /// </summary>
        [Test]
        public async Task EnumerateRejectsTruncatedRecordAlongsideValidNodeAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var valid = new NodeId("good", NamespaceIndex);
            var corrupt = new NodeId("corrupt", NamespaceIndex);
            await store.UpsertNodeAsync(
                new StoredNode(valid, ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);

            var ids = new List<NodeId>();
            await foreach (IStoredNode node in store.EnumerateAsync().ConfigureAwait(false))
            {
                ids.Add(node.NodeId);
            }
            Assert.That(ids, Is.EquivalentTo([valid]));

            await kv.SetAsync(
                "n/" + corrupt,
                ByteString.From(new byte[] { 1, 2, 3 })).ConfigureAwait(false);

            ids.Clear();
            Assert.That(
                async () =>
                {
                    await foreach (IStoredNode node in store.EnumerateAsync().ConfigureAwait(false))
                    {
                        ids.Add(node.NodeId);
                    }
                },
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// Rejects a valid node key's corrupt frame before or after a good record, including failed authentication.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task EnumerateRejectsCorruptionAtEitherScanPositionAsync(bool corruptFirst, bool authenticated)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(17));
            Mock<ISharedKeyValueStore> ordered = CreateStoreMock(kv);
            ordered.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string prefix, CancellationToken ct) => ScanSortedAsync(kv, prefix, ct));
            using var store = new InMemoryNodeStateStore(
                ordered.Object,
                m_messageContext,
                authenticated ? protector : NullRecordProtector.Instance);
            var valid = new NodeId(corruptFirst ? "z-valid" : "a-valid", NamespaceIndex);
            var corrupt = new NodeId(corruptFirst ? "a-corrupt" : "z-corrupt", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(valid, new ByteString(new byte[] { 1 })))
                .ConfigureAwait(false);
            byte[] frame = authenticated
                ? protector.Protect(MakeRecord(2, new ByteString(new byte[] { 2 }))).ToArray()
                : [1, 2, 3];
            if (authenticated)
            {
                frame[^1] ^= 1;
            }
            await kv.SetAsync("n/" + corrupt, new ByteString(frame)).ConfigureAwait(false);
            var ids = new List<NodeId>();

            await Assert.ThatAsync(
                async () =>
                {
                    await foreach (IStoredNode node in store.EnumerateAsync().ConfigureAwait(false))
                    {
                        ids.Add(node.NodeId);
                    }
                },
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);

            Assert.That(ids, Has.Count.EqualTo(corruptFirst ? 0 : 1));
            if (!corruptFirst)
            {
                Assert.That(ids[0], Is.EqualTo(valid));
            }
            Assert.That(await store.TryGetNodeAsync(corrupt).ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Retains the prior manifest, retained generations, and delta log after a mixed corrupt snapshot scan.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SnapshotCorruptionPreservesManifestLogAndOwnedGenerationsAsync(bool corruptValue)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(18));
            Mock<ISharedKeyValueStore> ordered = CreateStoreMock(kv);
            ordered.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string prefix, CancellationToken ct) => ScanSortedAsync(kv, prefix, ct));
            using var store = new InMemoryNodeStateStore(ordered.Object, m_messageContext, protector);
            var valid = new NodeId("a-valid", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(valid, new ByteString(new byte[] { 1 })))
                .ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);
            await store.WriteSnapshotAsync().ConfigureAwait(false);
            (_, ByteString manifest) = await kv.TryGetAsync("snapmeta/manifest").ConfigureAwait(false);
            Assert.That(await CountSnapshotGenerationsAsync(kv).ConfigureAwait(false), Is.EqualTo(2));

            var largePayload = new ByteString(new byte[m_messageContext.MaxByteStringLength]);
            await store.UpsertNodeAsync(new StoredNode(valid, largePayload))
                .ConfigureAwait(false);
            string corruptKey = (corruptValue ? "v/" : "n/") + new NodeId("z-corrupt", NamespaceIndex);
            await kv.SetAsync(
                corruptKey,
                protector.Protect(new ByteString(new byte[] { 1, 2, 3 }))).ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await store.WriteSnapshotAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);

            (_, ByteString unchanged) = await kv.TryGetAsync("snapmeta/manifest").ConfigureAwait(false);
            Assert.That(unchanged, Is.EqualTo(manifest));
            Assert.That(await CountSnapshotGenerationsAsync(kv).ConfigureAwait(false), Is.EqualTo(2));
            List<NodeStateChange> retained = await ReadChangesAsync(store.ReadDeltaLogAsync(0)).ConfigureAwait(false);
            Assert.That(retained, Has.Count.EqualTo(1));
            Assert.That(retained[0].Sequence, Is.EqualTo(2));
            NodeStateSnapshot? previous = await store.TryReadSnapshotAsync().ConfigureAwait(false);
            Assert.That(previous, Is.Not.Null);
            List<NodeStateChange> entries = await ReadChangesAsync(previous!.Entries).ConfigureAwait(false);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Node!.Payload, Is.EqualTo(new ByteString(new byte[] { 1 })));
        }

        /// <summary>
        /// Treats valid topology tombstones as absence rather than corruption during scans and snapshot construction.
        /// </summary>
        [Test]
        public async Task AuthoritativeScansAcceptDeletionTombstonesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var nodeId = new NodeId("deleted", NamespaceIndex);
            await store.UpsertNodeAsync(new StoredNode(nodeId, new ByteString(new byte[] { 1 })))
                .ConfigureAwait(false);
            Assert.That(await store.DeleteNodeAsync(nodeId).ConfigureAwait(false), Is.True);
            int count = 0;
            await foreach (IStoredNode _ in store.EnumerateAsync().ConfigureAwait(false))
            {
                count++;
            }
            Assert.That(count, Is.Zero);

            await store.WriteSnapshotAsync().ConfigureAwait(false);
            NodeStateSnapshot? snapshot = await store.TryReadSnapshotAsync().ConfigureAwait(false);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(await ReadChangesAsync(snapshot!.Entries).ConfigureAwait(false), Is.Empty);
        }

        /// <summary>
        /// Rejects truncated value envelopes, empty values, trailing bytes, and unauthenticated value records.
        /// </summary>
        [TestCase("envelope")]
        [TestCase("empty")]
        [TestCase("trailing")]
        [TestCase("authentication")]
        public async Task EnumerateValuesRejectsInvalidRecordsAsync(string corruption)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(19));
            using var store = new InMemoryNodeStateStore(
                kv,
                m_messageContext,
                corruption == "authentication" ? protector : NullRecordProtector.Instance);
            var valid = new NodeId("valid", NamespaceIndex);
            var corrupt = new NodeId("corrupt", NamespaceIndex);
            await store.WriteValueAsync(valid, new DataValue(new Variant(7))).ConfigureAwait(false);
            ByteString frame = corruption switch
            {
                "empty" => MakeRecord(2, ByteString.Empty),
                "trailing" => MakeRecord(2, new ByteString(new byte[] { 0, 1 })),
                _ => new ByteString(new byte[] { 1, 2, 3 })
            };
            await kv.SetAsync("v/" + corrupt, frame).ConfigureAwait(false);
            if (corruption == "empty")
            {
                (bool found, DataValue value) = await store.TryReadValueAsync(corrupt).ConfigureAwait(false);
                Assert.That(found, Is.True);
                Assert.That(value.IsNull, Is.True, "non-authoritative lookup compatibility is preserved");
            }

            await Assert.ThatAsync(
                async () =>
                {
                    await foreach ((NodeId _, DataValue _) in store.EnumerateValuesAsync().ConfigureAwait(false))
                    {
                    }
                },
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
        }

        /// <summary>
        /// Surfaces malformed delta envelopes, kinds, identifiers, payloads, and
        /// trailing bytes instead of skipping them.
        /// </summary>
        [TestCase("envelope")]
        [TestCase("kind")]
        [TestCase("node")]
        [TestCase("payload")]
        [TestCase("trailing")]
        [TestCase("authentication")]
        public async Task DeltaReplayRejectsInvalidFramesAsync(string corruption)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(20));
            using var store = new InMemoryNodeStateStore(
                kv,
                m_messageContext,
                corruption == "authentication" ? protector : NullRecordProtector.Instance);
            ByteString frame;
            if (corruption is "envelope" or "authentication")
            {
                frame = new ByteString(new byte[] { 1, 2, 3 });
            }
            else
            {
                using var encoder = new BinaryEncoder(m_messageContext);
                encoder.WriteByte(null, corruption == "kind" ? byte.MaxValue : (byte)NodeStateChangeKind.Upsert);
                encoder.WriteNodeId(null, corruption == "node" ? NodeId.Null : new NodeId("delta", NamespaceIndex));
                encoder.WriteByteString(
                    null,
                    corruption == "payload" ? ByteString.Empty : new ByteString(new byte[] { 1 }));
                if (corruption == "trailing")
                {
                    encoder.WriteByte(null, 0);
                }
                frame = new ByteString(encoder.CloseAndReturnBuffer()!);
            }
            await kv.SetAsync("dlog/00000000000000000001", frame).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => ReadChangesAsync(store.ReadDeltaLogAsync(0)),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
        }

        /// <summary>
        /// Does not wrap overflowing delta key suffixes into valid earlier sequence numbers.
        /// </summary>
        [Test]
        public async Task DeltaReplaySkipsOverflowedSequenceKeysAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            await kv.SetAsync("dlog/18446744073709551617", new ByteString(new byte[] { 1, 2, 3 }))
                .ConfigureAwait(false);

            Assert.That(await ReadChangesAsync(store.ReadDeltaLogAsync(0)).ConfigureAwait(false), Is.Empty);
        }

        /// <summary>
        /// Reports corrupt node, value, and delta frames received on a live backend feed.
        /// </summary>
        [TestCase("n/")]
        [TestCase("v/")]
        [TestCase("dlog/")]
        public async Task WatchRejectsCorruptLiveFramesAsync(string prefix)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(21));
            using var store = new InMemoryNodeStateStore(kv, m_messageContext, protector);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using IAsyncEnumerator<NodeStateChange> changes =
                store.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();
            Task<bool> pending = changes.MoveNextAsync().AsTask();
            string key = prefix +
                (prefix == "dlog/"
                    ? "00000000000000000001"
                    : new NodeId("live", NamespaceIndex).ToString());
            await kv.SetAsync(key, new ByteString(new byte[] { 1, 2, 3 }), cts.Token).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => pending,
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
            cts.Cancel();
        }

        /// <summary>
        /// Reports corrupt CRDT rows even when they are already present in the polling baseline.
        /// </summary>
        [TestCase("n/")]
        [TestCase("v/")]
        [TestCase("dlog/")]
        public async Task PollingRejectsCorruptBaselineAsync(string prefix)
        {
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            using var protector = new AesCbcHmacRecordProtector(MakeKey(22));
            using var store = new InMemoryNodeStateStore(crdt, m_messageContext, protector);
            string key = prefix +
                (prefix == "dlog/"
                    ? "00000000000000000001"
                    : new NodeId("baseline", NamespaceIndex).ToString());
            await crdt.SetAsync(key, new ByteString(new byte[] { 1, 2, 3 })).ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using IAsyncEnumerator<NodeStateChange> changes =
                store.SubscribeChangesAsync(cts.Token).GetAsyncEnumerator();

            await Assert.ThatAsync(
                async () => await changes.MoveNextAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
            cts.Cancel();
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

        private static Mock<ISharedKeyValueStore> CreateStoreMock(ISharedKeyValueStore inner)
        {
            var store = new Mock<ISharedKeyValueStore>();
            store.Setup(s => s.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => inner.TryGetAsync(key, ct));
            store.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString value, CancellationToken ct) => inner.SetAsync(key, value, ct));
            store.Setup(s => s.CompareAndSwapAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    inner.CompareAndSwapAsync(key, expected, value, ct));
            store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => inner.DeleteAsync(key, ct));
            store.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string prefix, CancellationToken ct) => inner.ScanAsync(prefix, ct));
            store.Setup(s => s.WatchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string prefix, CancellationToken ct) => inner.WatchAsync(prefix, ct));
            store.As<ISharedKeyValueStoreConsistency>()
                .Setup(s => s.IsLinearizable(It.IsAny<string>()))
                .Returns((string key) => inner is ISharedKeyValueStoreConsistency consistency &&
                    consistency.IsLinearizable(key));
            store.As<ISharedKeyValueStoreConsistency>()
                .Setup(s => s.IsProcessLocal(It.IsAny<string>()))
                .Returns((string key) => inner is not ISharedKeyValueStoreConsistency consistency ||
                    consistency.IsProcessLocal(key));
            return store;
        }

        private static ByteString MakeRecord(ulong sequence, ByteString payload)
        {
            byte[] record = new byte[sizeof(ulong) + payload.Length];
            BinaryPrimitives.WriteUInt64BigEndian(record, sequence);
            payload.Span.CopyTo(record.AsSpan(sizeof(ulong)));
            return new ByteString(record);
        }

        private static async Task<List<NodeStateChange>> ReadChangesAsync(IAsyncEnumerable<NodeStateChange> source)
        {
            var changes = new List<NodeStateChange>();
            await foreach (NodeStateChange change in source.ConfigureAwait(false))
            {
                changes.Add(change);
            }
            return changes;
        }

        private static async Task<int> CountKeysAsync(ISharedKeyValueStore store, string prefix)
        {
            int count = 0;
            await foreach (KeyValuePair<string, ByteString> _ in store.ScanAsync(prefix).ConfigureAwait(false))
            {
                count++;
            }
            return count;
        }

        private static async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanSortedAsync(
            InMemorySharedKeyValueStore store,
            string prefix,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var entries = new List<KeyValuePair<string, ByteString>>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync(prefix, ct).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            entries.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
            foreach (KeyValuePair<string, ByteString> entry in entries)
            {
                yield return entry;
            }
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
