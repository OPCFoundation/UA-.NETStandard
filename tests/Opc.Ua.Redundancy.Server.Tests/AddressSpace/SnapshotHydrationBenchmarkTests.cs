/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Compares the snapshot fast-path hydration against the streamed fallback
    /// for a larger address space. Asserts both paths materialize the same graph
    /// and logs the timings; there is no timing assertion, so the test is
    /// CI-deterministic.
    /// </summary>
    /// <remarks>
    /// Over the in-memory backing store the two paths are comparable because a
    /// scan has no per-entry round-trip cost. The snapshot path's advantage is on
    /// a networked backend (CRDT / Raft), where it reads a handful of large chunks
    /// plus a bounded delta log instead of transferring 2 * N individual key/value
    /// entries, and applies the topology in one bulk pass. This test is the
    /// correctness-at-scale gate and the harness for measuring on real backends.
    /// </remarks>
    [TestFixture]
    [Category("Distributed")]
    [Category("Benchmark")]
    [Parallelizable(ParallelScope.All)]
    public class SnapshotHydrationBenchmarkTests
    {
        private const ushort NamespaceIndex = 1;
        private const int NodeCount = 5000;
        private IServiceMessageContext m_messageContext = null!;
        private SystemContext m_systemContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:bench");
            m_messageContext = messageContext;
            m_systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task SnapshotHydrationMatchesStreamedAndReportsTimingsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var writerSpace = new DictionaryAddressSpace(m_systemContext);
            for (int i = 0; i < NodeCount; i++)
            {
                await writerSpace.AddOrUpdateNodeAsync(NewVariable(i)).ConfigureAwait(false);
            }

            await using (var writer = new AddressSpaceSynchronizer(writerStore, writerSpace, () => true))
            {
                await writer.SeedOrHydrateAsync().ConfigureAwait(false);
            }

            // Snapshot fast path.
            using var snapshotStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var snapshotSpace = new DictionaryAddressSpace(m_systemContext);
            var snapshotTimer = Stopwatch.StartNew();
            await using (var reader = new AddressSpaceSynchronizer(snapshotStore, snapshotSpace, () => false))
            {
                await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            }
            snapshotTimer.Stop();

            // Streamed fallback: the same backing data behind a view that hides the
            // snapshot capability, so hydration must stream every node and value.
            using var innerStore = new InMemoryNodeStateStore(kv, m_messageContext);
            var streamedStore = new NonSnapshotStoreView(innerStore);
            var streamedSpace = new DictionaryAddressSpace(m_systemContext);
            var streamedTimer = Stopwatch.StartNew();
            await using (var reader = new AddressSpaceSynchronizer(streamedStore, streamedSpace, () => false))
            {
                await reader.SeedOrHydrateAsync().ConfigureAwait(false);
            }
            streamedTimer.Stop();

            int snapshotNodes = CountNodes(snapshotSpace);
            int streamedNodes = CountNodes(streamedSpace);

            TestContext.Progress.WriteLine(
                $"Hydration of {NodeCount} nodes: snapshot={snapshotTimer.ElapsedMilliseconds} ms, " +
                $"streamed={streamedTimer.ElapsedMilliseconds} ms");

            Assert.That(snapshotNodes, Is.EqualTo(NodeCount), "snapshot hydration materialized every node");
            Assert.That(streamedNodes, Is.EqualTo(NodeCount), "streamed hydration materialized every node");
        }

        private static int CountNodes(DictionaryAddressSpace space)
        {
            int count = 0;
            foreach (NodeState _ in space.Nodes)
            {
                count++;
            }
            return count;
        }

        private BaseDataVariableState NewVariable(int index)
        {
            string id = "N" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId(id, NamespaceIndex),
                BrowseName = new QualifiedName(id, NamespaceIndex),
                DisplayName = new LocalizedText(id),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant((double)index)
            };
        }

        /// <summary>
        /// An <see cref="INodeStateStore"/> that delegates to an inner store but
        /// does not expose <see cref="INodeStateSnapshotStore"/>, forcing the
        /// streamed hydration fallback.
        /// </summary>
        private sealed class NonSnapshotStoreView : INodeStateStore
        {
            public NonSnapshotStoreView(INodeStateStore inner)
            {
                m_inner = inner;
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
                return m_inner.WriteValueAsync(nodeId, in value, ct);
            }

            public ValueTask<(bool Found, DataValue Value)> TryReadValueAsync(
                NodeId nodeId,
                CancellationToken ct = default)
            {
                return m_inner.TryReadValueAsync(nodeId, ct);
            }

            public IAsyncEnumerable<(NodeId NodeId, DataValue Value)> EnumerateValuesAsync(
                CancellationToken ct = default)
            {
                return m_inner.EnumerateValuesAsync(ct);
            }

            public IAsyncEnumerable<NodeStateChange> SubscribeChangesAsync(CancellationToken ct = default)
            {
                return m_inner.SubscribeChangesAsync(ct);
            }

            private readonly INodeStateStore m_inner;
        }
    }
}
