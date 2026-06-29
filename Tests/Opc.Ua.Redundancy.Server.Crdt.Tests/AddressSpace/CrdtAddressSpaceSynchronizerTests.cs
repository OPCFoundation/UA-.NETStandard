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
using System.Collections.Generic;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Active/active convergence tests for <see cref="CrdtAddressSpaceSynchronizer"/>
    /// running two replicas over a deterministic in-memory gossip network.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class CrdtAddressSpaceSynchronizerTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;
        private SystemContext m_systemContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
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
            await using var fixture = await TwoReplicaFixture.CreateAsync(this);

            BaseDataVariableState node = NewVariable("added", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node);

            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node added on A should replicate to B");
        }

        [Test]
        public async Task ReplicatesValueUpdateAsync()
        {
            await using var fixture = await TwoReplicaFixture.CreateAsync(this);

            BaseDataVariableState node = NewVariable("value", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node should replicate before the value update");

            node.Value = new Variant(42.0);
            node.ClearChangeMasks(m_systemContext, false);

            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out NodeState? remote) &&
                      remote is BaseVariableState variable &&
                      variable.Value.Equals(new Variant(42.0)),
                "value updated on A should replicate to B");
        }

        [Test]
        public async Task ReplicatesRemovalAsync()
        {
            await using var fixture = await TwoReplicaFixture.CreateAsync(this);

            BaseDataVariableState node = NewVariable("removed", 1.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(node);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "node should replicate before removal");

            await fixture.SpaceA.RemoveNodeAsync(node.NodeId);

            await AssertEventuallyAsync(
                () => !fixture.SpaceB.TryGetNode(node.NodeId, out _),
                "removal on A should replicate to B");
        }

        [Test]
        public async Task MultiWriterConvergesBothDirectionsAsync()
        {
            await using var fixture = await TwoReplicaFixture.CreateAsync(this);

            BaseDataVariableState onA = NewVariable("from-a", 1.0);
            BaseDataVariableState onB = NewVariable("from-b", 2.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(onA);
            await fixture.SpaceB.AddOrUpdateNodeAsync(onB);

            await AssertEventuallyAsync(
                () => fixture.SpaceA.TryGetNode(onB.NodeId, out _) &&
                      fixture.SpaceB.TryGetNode(onA.NodeId, out _),
                "each replica's write should converge on the other (no leader)");
        }

        [Test]
        public async Task ConcurrentValueWritesConvergeAsync()
        {
            await using var fixture = await TwoReplicaFixture.CreateAsync(this);

            BaseDataVariableState seed = NewVariable("contended", 0.0);
            await fixture.SpaceA.AddOrUpdateNodeAsync(seed);
            await AssertEventuallyAsync(
                () => fixture.SpaceB.TryGetNode(seed.NodeId, out _),
                "node should replicate before concurrent writes");

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
                      a is BaseVariableState av && b is BaseVariableState bv &&
                      av.Value.Equals(bv.Value),
                "concurrent value writes must converge to the same value on both replicas");
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
                await Task.Delay(25);
            }
            Assert.Fail(message);
        }

        private sealed class TwoReplicaFixture : IAsyncDisposable
        {
            public DictionaryAddressSpace SpaceA { get; private set; } = null!;
            public DictionaryAddressSpace SpaceB { get; private set; } = null!;

            public static async Task<TwoReplicaFixture> CreateAsync(CrdtAddressSpaceSynchronizerTests test)
            {
                var fixture = new TwoReplicaFixture();
                fixture.m_network = new InMemoryNetwork();
                fixture.SpaceA = new DictionaryAddressSpace(test.m_systemContext);
                fixture.SpaceB = new DictionaryAddressSpace(test.m_systemContext);

                fixture.m_syncA = new CrdtAddressSpaceSynchronizer(
                    fixture.SpaceA, test.m_messageContext, ReplicaId.FromUInt64(1),
                    fixture.m_network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
                fixture.m_syncB = new CrdtAddressSpaceSynchronizer(
                    fixture.SpaceB, test.m_messageContext, ReplicaId.FromUInt64(2),
                    fixture.m_network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);

                // Start both transports first so neither misses the other's
                // broadcasts, then begin capturing/applying changes.
                await fixture.m_syncA.SeedOrHydrateAsync();
                await fixture.m_syncB.SeedOrHydrateAsync();
                fixture.m_syncA.Start();
                fixture.m_syncB.Start();
                return fixture;
            }

            public async ValueTask DisposeAsync()
            {
                if (m_syncA != null)
                {
                    await m_syncA.DisposeAsync();
                }
                if (m_syncB != null)
                {
                    await m_syncB.DisposeAsync();
                }
                if (m_network != null)
                {
                    await m_network.DisposeAsync();
                }
            }

            private InMemoryNetwork? m_network;
            private CrdtAddressSpaceSynchronizer? m_syncA;
            private CrdtAddressSpaceSynchronizer? m_syncB;
        }
    }
}