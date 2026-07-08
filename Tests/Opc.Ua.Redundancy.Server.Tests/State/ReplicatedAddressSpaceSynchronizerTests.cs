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

// CA2007: tests run without a SynchronizationContext.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Active/active convergence tests for <see cref="ReplicatedAddressSpaceSynchronizer"/>
    /// running two replicas over a deterministic in-memory gossip network.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ReplicatedAddressSpaceSynchronizerTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;
        private SystemContext m_systemContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:crdt");
            m_messageContext = messageContext;
            m_systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task ReplicatesAddedNodeToPeerAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState node = NewVariable("added", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node).ConfigureAwait(false);

            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node added on A should replicate to B").ConfigureAwait(false);
        }

        [Test]
        public async Task ReplicatesValueUpdateAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState node = NewVariable("value", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node should replicate before the value update").ConfigureAwait(false);

            node.Value = new Variant(42.0);
            node.ClearChangeMasks(m_systemContext, false);

            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out NodeState? remote) &&
                    remote is BaseVariableState variable &&
                    variable.Value.Equals(new Variant(42.0)),
                "value updated on A should replicate to B").ConfigureAwait(false);
        }

        [Test]
        public async Task ReplicatesRemovalAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState node = NewVariable("removed", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node should replicate before removal").ConfigureAwait(false);

            await fixture.SpaceA.RemoveNodeAsync(node.NodeId).ConfigureAwait(false);

            await AssertEventuallyAsync(
                () => !fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "removal on A should replicate to B").ConfigureAwait(false);
        }

        [Test]
        public async Task MultiWriterConvergesBothDirectionsAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState onA = NewVariable("from-a", 1.0);
            BaseDataVariableState onB = NewVariable("from-b", 2.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(onA).ConfigureAwait(false);
            await fixture.SpaceB.AddOrUpdateNodeAsync(onB).ConfigureAwait(false);

            await AssertEventuallyAsync(
                () => fixture.SpaceA.TryGetNode(onB.NodeId, out _) &&
                    fixture.SpaceB.TryGetNode(onA.NodeId, out _),
                "each replica's write should converge on the other (no leader)").ConfigureAwait(false);
        }

        [Test]
        public async Task ConcurrentValueWritesConvergeAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState seed = NewVariable("contended", 0.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(seed).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(seed.NodeId, out _),
                "node should replicate before concurrent writes").ConfigureAwait(false);

            // Concurrent writes on both replicas; last-writer-wins must converge
            // both sides to the same value.
            seed.Value = new Variant(11.0);
            seed.ClearChangeMasks(m_systemContext, false);
            if (fixture.SpaceB.TryGetNode(seed.NodeId, out NodeState? onB) && onB is BaseVariableState bVar)
            {
                bVar.Value = new Variant(22.0);
                bVar.ClearChangeMasks(m_systemContext, false);
            }

            await AssertEventuallyAsync(
                () => fixture.SpaceA.TryGetNode(seed.NodeId, out NodeState? a) &&
                    fixture.SpaceB.TryGetNode(seed.NodeId, out NodeState? b) &&
                    a is BaseVariableState av &&
                    b is BaseVariableState bv &&
                    av.Value.Equals(bv.Value),
                "concurrent value writes must converge to the same value on both replicas").ConfigureAwait(false);
        }

        [Test]
        public async Task RemovedNodesAreDetachedFromTrackingAsync()
        {
            await using var network = new InMemoryNetwork();
            var space = new DictionaryAddressSpace(m_systemContext);
            await using var sync = new ReplicatedAddressSpaceSynchronizer(
                space,
                m_messageContext,
                ReplicaId.FromUInt64(1),
                network.CreateTransport(),
                TimeProvider.System,
                CrdtReaderOptions.Default);
            await sync.SeedOrHydrateAsync().ConfigureAwait(false);
            sync.Start();

            BaseDataVariableState node = NewVariable("tracked", 1.0);
            await space.AddOrUpdateNodeAsync(node).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => sync.TrackedNodeCount == 1,
                "the local node should be tracked while it exists").ConfigureAwait(false);

            await space.RemoveNodeAsync(node.NodeId).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => sync.TrackedNodeCount == 0,
                "removing a node should detach its StateChanged subscription").ConfigureAwait(false);

            node.Value = new Variant(9.0);
            node.ClearChangeMasks(m_systemContext, false);
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(sync.TrackedNodeCount, Is.Zero);
        }

        [Test]
        public async Task ReplicatesReferenceAddAndRemoveAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState source = NewVariable("source", 1.0);
            BaseDataVariableState target = NewVariable("target", 2.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            await fixture.SpaceA.AddOrUpdateNodeAsync(target).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out _) &&
                    fixture.SpaceB.TryGetNode(target.NodeId, out _),
                "nodes should replicate before reference changes").ConfigureAwait(false);

            ExpandedNodeId targetId = new(target.NodeId);
            source.AddReference(ReferenceTypeIds.Organizes, false, targetId);
            source.ClearChangeMasks(m_systemContext, false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out NodeState? mirrored) &&
                    mirrored!.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                "a reference added on A should replicate to B").ConfigureAwait(false);

            source.RemoveReference(ReferenceTypeIds.Organizes, false, targetId);
            source.ClearChangeMasks(m_systemContext, false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out NodeState? mirrored) &&
                    !mirrored!.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                "a reference removed on A should be removed on B").ConfigureAwait(false);
        }

        [Test]
        public async Task ReconcileMaterializedValuesCorrectsStaleReplicaAndRebroadcastsAsync()
        {
            await using TwoReplicaFixture fixture = await TwoReplicaFixture.CreateAsync(this).ConfigureAwait(false);

            BaseDataVariableState source = NewVariable("reconcile", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(source).ConfigureAwait(false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out _),
                "the seed node should replicate before reconciliation").ConfigureAwait(false);

            source.Value = new Variant(42.0);
            source.ClearChangeMasks(m_systemContext, false);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out NodeState? remote) &&
                    remote is BaseVariableState variable &&
                    variable.Value.Equals(new Variant(42.0)),
                "the authoritative value should replicate before making B stale").ConfigureAwait(false);

            Assert.That(fixture.SpaceB.TryGetNode(source.NodeId, out NodeState? staleNode), Is.True);
            var staleVariable = (BaseVariableState)staleNode!;
            staleVariable.Value = new Variant(11.0);
            staleVariable.StatusCode = StatusCodes.Good;
            staleVariable.Timestamp = DateTimeUtc.Now;

            var rebroadcasted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler()
            {
                fixture.SyncA.InboundApplied -= Handler;
                rebroadcasted.TrySetResult(true);
            }

            fixture.SyncA.InboundApplied += Handler;
            await fixture.SyncA.SeedOrHydrateAsync().ConfigureAwait(false);

            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(source.NodeId, out NodeState? remote) &&
                    remote is BaseVariableState variable &&
                    variable.Value.Equals(new Variant(42.0)),
                "a zero-diff merge should reconcile the stale materialized value on B").ConfigureAwait(false);
            await AwaitWithTimeoutAsync(rebroadcasted.Task).ConfigureAwait(false);
        }

        [Test]
        public void ConstructorRejectsNullAddressSpace()
        {
            Assert.That(
                () => new ReplicatedAddressSpaceSynchronizer(
                    null!, m_messageContext, ReplicaId.FromUInt64(1), null!,
                    TimeProvider.System, CrdtReaderOptions.Default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("addressSpace"));
        }

        [Test]
        public void ConstructorRejectsNullMessageContext()
        {
            var space = new DictionaryAddressSpace(m_systemContext);
            Assert.That(
                () => new ReplicatedAddressSpaceSynchronizer(
                    space, null!, ReplicaId.FromUInt64(1), null!,
                    TimeProvider.System, CrdtReaderOptions.Default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("messageContext"));
        }

        [Test]
        public void ConstructorRejectsNullTransport()
        {
            var space = new DictionaryAddressSpace(m_systemContext);
            Assert.That(
                () => new ReplicatedAddressSpaceSynchronizer(
                    space, m_messageContext, ReplicaId.FromUInt64(1), null!,
                    TimeProvider.System, CrdtReaderOptions.Default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("transport"));
        }

        [Test]
        public async Task StartIsIdempotentAsync()
        {
            await using var network = new InMemoryNetwork();
            var space = new DictionaryAddressSpace(m_systemContext);
            await using var sync = new ReplicatedAddressSpaceSynchronizer(
                space, m_messageContext, ReplicaId.FromUInt64(1),
                network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await sync.SeedOrHydrateAsync().ConfigureAwait(false);

            sync.Start();

            Assert.That(() => sync.Start(), Throws.Nothing);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            await using var network = new InMemoryNetwork();
            var space = new DictionaryAddressSpace(m_systemContext);
            await using var sync = new ReplicatedAddressSpaceSynchronizer(
                space, m_messageContext, ReplicaId.FromUInt64(1),
                network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await sync.SeedOrHydrateAsync().ConfigureAwait(false);
            sync.Start();

            await sync.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await sync.DisposeAsync().ConfigureAwait(false),
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

        private static async Task AssertEventuallyAsync(Func<bool> condition, string message)
        {
            // CRDT convergence over the in-memory gossip network is normally sub-second,
            // but the background capture/broadcast loops can be starved of CPU on a heavily
            // loaded CI runner (the full test matrix runs dozens of jobs per runner). Allow a
            // generous deadline so the assertion measures convergence correctness, not runner load.
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.Fail(message);
        }

        private static async Task AwaitWithTimeoutAsync(Task task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task), "replication did not complete within the timeout");
            await task.ConfigureAwait(false);
        }

        private sealed class TwoReplicaFixture : IAsyncDisposable
        {
            public DictionaryAddressSpace SpaceA { get; private set; } = null!;
            public DictionaryAddressSpace SpaceB { get; private set; } = null!;
            public ReplicatedAddressSpaceSynchronizer SyncA => m_syncA!;
            public ReplicatedAddressSpaceSynchronizer SyncB => m_syncB!;

            public static async Task<TwoReplicaFixture> CreateAsync(ReplicatedAddressSpaceSynchronizerTests test)
            {
                var fixture = new TwoReplicaFixture
                {
                    m_network = new InMemoryNetwork(),
                    SpaceA = new DictionaryAddressSpace(test.m_systemContext),
                    SpaceB = new DictionaryAddressSpace(test.m_systemContext)
                };

                fixture.m_syncA = new ReplicatedAddressSpaceSynchronizer(
                    fixture.SpaceA, test.m_messageContext, ReplicaId.FromUInt64(1),
                    fixture.m_network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
                fixture.m_syncB = new ReplicatedAddressSpaceSynchronizer(
                    fixture.SpaceB, test.m_messageContext, ReplicaId.FromUInt64(2),
                    fixture.m_network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);

                // Start both transports first so neither misses the other's
                // broadcasts, then begin capturing/applying changes.
                await fixture.m_syncA.SeedOrHydrateAsync().ConfigureAwait(false);
                await fixture.m_syncB.SeedOrHydrateAsync().ConfigureAwait(false);
                fixture.m_syncA.Start();
                fixture.m_syncB.Start();
                return fixture;
            }

            public async ValueTask DisposeAsync()
            {
                if (m_syncA != null)
                {
                    await m_syncA.DisposeAsync().ConfigureAwait(false);
                }
                if (m_syncB != null)
                {
                    await m_syncB.DisposeAsync().ConfigureAwait(false);
                }
                if (m_network != null)
                {
                    await m_network.DisposeAsync().ConfigureAwait(false);
                }
            }

            private InMemoryNetwork? m_network;
            private ReplicatedAddressSpaceSynchronizer? m_syncA;
            private ReplicatedAddressSpaceSynchronizer? m_syncB;
        }
    }
}
