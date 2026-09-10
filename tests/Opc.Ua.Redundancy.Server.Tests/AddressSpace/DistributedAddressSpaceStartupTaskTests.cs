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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
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
    /// Tests for <see cref="DistributedAddressSpaceStartupTask"/>: it wires a
    /// synchronizer to every opted-in node manager and (as writer) seeds the
    /// node manager's address space into the shared store.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class DistributedAddressSpaceStartupTaskTests
    {
        private const ushort NamespaceIndex = 1;

        /// <summary>
        /// Verifies that startup seeds only the namespaces owned by synchronous and asynchronous node managers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WiresSynchronizerAndSeedsOptedInNodeManagerAsync(
            bool useAsyncNodeManager)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:wire");
            ushort foreignNamespaceIndex = messageContext.NamespaceUris.GetIndexOrAppend("urn:test:foreign");
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };

            // A node manager that opts in and exposes a one-node address space.
            var addressSpace = new DictionaryAddressSpace(systemContext);
            var nodeId = new NodeId("seeded", NamespaceIndex);
            await addressSpace.AddOrUpdateNodeAsync(new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Seeded", NamespaceIndex),
                DisplayName = new LocalizedText("Seeded"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(1.0)
            }).ConfigureAwait(false);
            var standardNodeId = new NodeId(22u);
            await addressSpace.AddOrUpdateNodeAsync(new BaseObjectState(null)
            {
                NodeId = standardNodeId,
                BrowseName = new QualifiedName("Standard"),
                DisplayName = new LocalizedText("Standard")
            }).ConfigureAwait(false);
            var foreignNodeId = new NodeId("foreign", foreignNamespaceIndex);
            await addressSpace.AddOrUpdateNodeAsync(new BaseObjectState(null)
            {
                NodeId = foreignNodeId,
                BrowseName = new QualifiedName("Foreign", foreignNamespaceIndex),
                DisplayName = new LocalizedText("Foreign")
            }).ConfigureAwait(false);

            ILocalAddressSpaceSource source;
            if (useAsyncNodeManager)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager.SetupGet(m => m.NamespaceUris).Returns(["urn:test:wire"]);
                nodeManager.As<ILocalAddressSpaceSource>()
                    .Setup(s => s.CreateLocalAddressSpace())
                    .Returns(addressSpace);
                source = nodeManager.As<ILocalAddressSpaceSource>().Object;
            }
            else
            {
                var nodeManager = new Mock<INodeManager>();
                nodeManager.SetupGet(m => m.NamespaceUris).Returns(["urn:test:wire"]);
                nodeManager.As<ILocalAddressSpaceSource>()
                    .Setup(s => s.CreateLocalAddressSpace())
                    .Returns(addressSpace);
                source = nodeManager.As<ILocalAddressSpaceSource>().Object;
            }

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers).Returns([]);

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.Setup(s => s.FindNodeManagers<ILocalAddressSpaceSource>())
                .Returns([source]);

            using var kv = new InMemorySharedKeyValueStore();
            var election = new StaticLeaderElection(true);
            var task = new DistributedAddressSpaceStartupTask(kv, election);

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            // The writer must have seeded the node into the shared store, and
            // registered a default store in the task-owned registry.
            var verifyStore = new InMemoryNodeStateStore(kv, messageContext);
            IStoredNode? stored = await verifyStore.TryGetNodeAsync(nodeId).ConfigureAwait(false);
            IStoredNode? standard = await verifyStore.TryGetNodeAsync(standardNodeId).ConfigureAwait(false);
            IStoredNode? foreign = await verifyStore.TryGetNodeAsync(foreignNodeId).ConfigureAwait(false);

            Assert.That(stored, Is.Not.Null, "writer should have seeded the opted-in node manager's address space");
            Assert.That(standard, Is.Null, "replica-local standard nodes must not be distributed");
            Assert.That(foreign, Is.Null, "a node manager must not distribute nodes owned by another namespace");
            Assert.That(task.NodeStateStoreRegistry, Is.Not.Null);
            Assert.That(task.NodeStateStoreRegistry!.Resolve(nodeId), Is.Not.Null, "a default node state store should be registered");

            await task.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task UsesExplicitOwnershipForCustomPartitionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            ushort ownedNamespaceIndex =
                messageContext.NamespaceUris.GetIndexOrAppend("urn:test:owned");
            ushort foreignNamespaceIndex =
                messageContext.NamespaceUris.GetIndexOrAppend("urn:test:foreign");
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
            var addressSpace = new DictionaryAddressSpace(systemContext);
            var ownedNodeId = new NodeId("owned", ownedNamespaceIndex);
            var foreignNodeId = new NodeId("foreign", foreignNamespaceIndex);
            await addressSpace.AddOrUpdateNodeAsync(new BaseObjectState(null)
            {
                NodeId = ownedNodeId,
                BrowseName = new QualifiedName("Owned", ownedNamespaceIndex),
                DisplayName = new LocalizedText("Owned")
            }).ConfigureAwait(false);
            await addressSpace.AddOrUpdateNodeAsync(new BaseObjectState(null)
            {
                NodeId = foreignNodeId,
                BrowseName = new QualifiedName("Foreign", foreignNamespaceIndex),
                DisplayName = new LocalizedText("Foreign")
            }).ConfigureAwait(false);

            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.SetupGet(m => m.NamespaceUris).Returns((IEnumerable<string>)null!);
            nodeManager.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(addressSpace);
            nodeManager.As<ILocalAddressSpaceOwnership>()
                .SetupGet(o => o.PartitionId)
                .Returns("custom-partition");
            nodeManager.As<ILocalAddressSpaceOwnership>()
                .Setup(o => o.OwnsNode(It.IsAny<NodeId>()))
                .Returns<NodeId>(nodeId => nodeId == ownedNodeId);

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers).Returns([]);
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.Setup(s => s.FindNodeManagers<ILocalAddressSpaceSource>())
                .Returns([nodeManager.As<ILocalAddressSpaceSource>().Object]);

            using var kv = new InMemorySharedKeyValueStore();
            var election = new StaticLeaderElection(true);
            var task = new DistributedAddressSpaceStartupTask(kv, election);

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            var verifyStore = new InMemoryNodeStateStore(kv, messageContext);
            Assert.That(
                await verifyStore.TryGetNodeAsync(ownedNodeId).ConfigureAwait(false),
                Is.Not.Null);
            Assert.That(
                await verifyStore.TryGetNodeAsync(foreignNodeId).ConfigureAwait(false),
                Is.Null);

            await task.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that startup excludes replica-local built-in address spaces from replication.
        /// </summary>
        [Test]
        public async Task DoesNotReplicateReplicaLocalBuiltInAddressSpacesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
            var addressSpace = new DictionaryAddressSpace(systemContext);
            await addressSpace.AddOrUpdateNodeAsync(new BaseObjectState(null)
            {
                NodeId = ObjectIds.Server,
                BrowseName = new QualifiedName(BrowseNames.Server),
                DisplayName = new LocalizedText("Server")
            }).ConfigureAwait(false);
            var diagnosticsNodeManager = new Mock<IDiagnosticsNodeManager>();
            Mock<ILocalAddressSpaceSource> diagnosticsSource =
                diagnosticsNodeManager.As<ILocalAddressSpaceSource>();
            diagnosticsSource
                .Setup(value => value.CreateLocalAddressSpace())
                .Returns(addressSpace);
            var coreNodeManager = new Mock<ICoreNodeManager>();
            Mock<ILocalAddressSpaceSource> coreSource =
                coreNodeManager.As<ILocalAddressSpaceSource>();
            coreSource
                .Setup(value => value.CreateLocalAddressSpace())
                .Returns(addressSpace);
            var server = new Mock<IServerInternal>();
            server.Setup(value => value.Telemetry).Returns(telemetry);
            server.Setup(value => value.MessageContext).Returns(messageContext);
            server.Setup(value => value.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server
                .Setup(value => value.FindNodeManagers<ILocalAddressSpaceSource>())
                .Returns([diagnosticsSource.Object, coreSource.Object]);
            using var keyValueStore = new InMemorySharedKeyValueStore();
            await using var startup = new DistributedAddressSpaceStartupTask(
                keyValueStore,
                new StaticLeaderElection(true));

            await startup.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            using var store = new InMemoryNodeStateStore(keyValueStore, messageContext);
            Assert.That(
                await store.TryGetNodeAsync(ObjectIds.Server).ConfigureAwait(false),
                Is.Null,
                "Replica-local core, diagnostics, and configuration nodes must never enter the shared address space.");
            diagnosticsSource.Verify(value => value.CreateLocalAddressSpace(), Times.Never);
            coreSource.Verify(value => value.CreateLocalAddressSpace(), Times.Never);
        }

        /// <summary>
        /// Verifies that the distributed address-space startup task rejects null constructor dependencies.
        /// </summary>
        [Test]
        public async Task DuplicateNamespaceClaimsRequireExplicitOwnershipAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:shared");
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
            var firstSpace = new DictionaryAddressSpace(systemContext);
            var secondSpace = new DictionaryAddressSpace(systemContext);
            var firstManager = new Mock<IAsyncNodeManager>();
            firstManager.SetupGet(m => m.NamespaceUris).Returns(["urn:test:shared"]);
            firstManager.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(firstSpace);
            var secondManager = new Mock<IAsyncNodeManager>();
            secondManager.SetupGet(m => m.NamespaceUris).Returns(["urn:test:shared"]);
            secondManager.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(secondSpace);

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers).Returns([]);
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.Setup(s => s.FindNodeManagers<ILocalAddressSpaceSource>())
                .Returns(
                [
                    firstManager.As<ILocalAddressSpaceSource>().Object,
                    secondManager.As<ILocalAddressSpaceSource>().Object
                ]);

            using var kv = new InMemorySharedKeyValueStore();
            var election = new Mock<ILeaderElection>();
            var task = new DistributedAddressSpaceStartupTask(kv, election.Object);
            try
            {
                await Assert.ThatAsync(
                    async () => await task.OnServerStartedAsync(server.Object).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            finally
            {
                await task.DisposeAsync().ConfigureAwait(false);
            }
            election.Verify(
                e => e.TryAcquireOrRenewAsync(It.IsAny<CancellationToken>()),
                Times.Never);
            election.Verify(e => e.Start(), Times.Never);
        }

        [Test]
        public async Task ExplicitOwnershipCannotClaimNamespaceZeroAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
            var addressSpace = new DictionaryAddressSpace(systemContext);
            await addressSpace.AddOrUpdateNodeAsync(new BaseObjectState(null)
            {
                NodeId = new NodeId(22u),
                BrowseName = new QualifiedName("Standard"),
                DisplayName = new LocalizedText("Standard")
            }).ConfigureAwait(false);
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.SetupGet(m => m.NamespaceUris).Returns((IEnumerable<string>)null!);
            nodeManager.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(addressSpace);
            nodeManager.As<ILocalAddressSpaceOwnership>()
                .SetupGet(o => o.PartitionId)
                .Returns("invalid-standard");
            nodeManager.As<ILocalAddressSpaceOwnership>()
                .Setup(o => o.OwnsNode(It.IsAny<NodeId>()))
                .Returns(true);

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers).Returns([]);
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.Setup(s => s.FindNodeManagers<ILocalAddressSpaceSource>())
                .Returns([nodeManager.As<ILocalAddressSpaceSource>().Object]);

            using var kv = new InMemorySharedKeyValueStore();
            var election = new Mock<ILeaderElection>();
            var task = new DistributedAddressSpaceStartupTask(kv, election.Object);
            try
            {
                await Assert.ThatAsync(
                    async () => await task.OnServerStartedAsync(server.Object).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            finally
            {
                await task.DisposeAsync().ConfigureAwait(false);
            }
            election.Verify(
                e => e.TryAcquireOrRenewAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Rejects an unsupported CRDT writer before creating address spaces, acquiring a lease, or writing markers.
        /// </summary>
        [Test]
        public async Task BareCrdtFailsBeforeOwnershipOrElectionAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            var election = new Mock<ILeaderElection>();
            var server = new Mock<IServerInternal>(MockBehavior.Strict);
            await using var startup = new DistributedAddressSpaceStartupTask(crdt, election.Object);

            await Assert.ThatAsync(
                async () => await startup.OnServerStartedAsync(server.Object).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(InMemoryNodeStateStore.SequenceKey))
                .ConfigureAwait(false);

            election.Verify(e => e.TryAcquireOrRenewAsync(It.IsAny<CancellationToken>()), Times.Never);
            election.Verify(e => e.Start(), Times.Never);
            server.VerifyNoOtherCalls();
            Assert.That(startup.NodeStateStoreRegistry, Is.Null);
            int count = 0;
            await foreach (KeyValuePair<string, ByteString> _ in crdt.ScanAsync(string.Empty).ConfigureAwait(false))
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        /// <summary>
        /// Rejects a configured lease routed to CRDT even when the sequence key is correctly coordinated.
        /// </summary>
        [Test]
        public async Task MisroutedLeaseFailsBeforeStartupMutationAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await using var coordinator = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            await using var hybrid = new HybridSharedKeyValueStore(
                crdt, coordinator, [InMemoryNodeStateStore.SequenceKey]);
            var election = new Mock<ILeaderElection>();
            var server = new Mock<IServerInternal>(MockBehavior.Strict);
            await using var startup = new DistributedAddressSpaceStartupTask(
                hybrid, election.Object, protector: null, leaseKey: "custom/leader");

            await Assert.ThatAsync(
                async () => await startup.OnServerStartedAsync(server.Object).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("custom/leader")).ConfigureAwait(false);

            election.Verify(e => e.TryAcquireOrRenewAsync(It.IsAny<CancellationToken>()), Times.Never);
            election.Verify(e => e.Start(), Times.Never);
            server.VerifyNoOtherCalls();
            Assert.That(startup.NodeStateStoreRegistry, Is.Null);
        }

        /// <summary>
        /// Rejects replicated startup before accessing the address space when no fixed identity policy was configured.
        /// </summary>
        [Test]
        public async Task ReplicatedStartupRequiresNodeIdentityConfigurationAsync()
        {
            await using var coordinator = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            var election = new Mock<ILeaderElection>();
            var server = new Mock<IServerInternal>(MockBehavior.Strict);
            await using var startup = new DistributedAddressSpaceStartupTask(coordinator, election.Object);

            await Assert.ThatAsync(
                async () => await startup.OnServerStartedAsync(server.Object).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError)).ConfigureAwait(false);

            election.Verify(value => value.TryAcquireOrRenewAsync(It.IsAny<CancellationToken>()), Times.Never);
            server.VerifyNoOtherCalls();
            Assert.That(startup.NodeStateStoreRegistry, Is.Null);
        }

        /// <summary>
        /// Wires a hosted registry and a directly constructed store to the same strong coordinator and real CRDT bulk.
        /// </summary>
        [Test]
        public async Task HostedRegistryAndDirectStoreShareHybridCoordinationAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:test:hybrid-application");
            context.NamespaceUris.GetIndexOrAppend("urn:test:shared-hybrid");
            var identity = new ReplicaNodeIdFactory("shared-hybrid", ["urn:test:shared-hybrid"]);
            var server = new Mock<IServerInternal>();
            server.As<INodeIdFactoryProvider>().Setup(s => s.NodeIdFactory).Returns(identity);
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(context);
            server.Setup(s => s.NamespaceUris).Returns(context.NamespaceUris);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.Setup(s => s.FindNodeManagers<ILocalAddressSpaceSource>()).Returns([]);
            await using var network = new InMemoryNetwork();
            await using var crdt = new ReplicatedSharedKeyValueStore(
                ReplicaId.New(), network.CreateTransport(), TimeProvider.System, CrdtReaderOptions.Default);
            await using var coordinator = new RaftSharedKeyValueStore(
                DefaultRaftConsensus.CreateSingleNode(), ownsConsensus: true);
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            await identity.InitializeNewStoreAsync(coordinator, protector).ConfigureAwait(false);
            await using var hybrid = new HybridSharedKeyValueStore(crdt, coordinator);
            await using var startup = new DistributedAddressSpaceStartupTask(
                hybrid, new StaticLeaderElection(true), protector);

            await startup.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            var hostedId = new NodeId("hosted", 2);
            var directId = new NodeId("direct", 2);
            Assert.That(startup.NodeStateStoreRegistry, Is.Not.Null);
            INodeStateStore? hosted = startup.NodeStateStoreRegistry!.Resolve(hostedId);
            Assert.That(hosted, Is.Not.Null);
            await hosted!.UpsertNodeAsync(new StoredNode(hostedId, new ByteString(new byte[] { 1 })))
                .ConfigureAwait(false);
            using var direct = new InMemoryNodeStateStore(hybrid, context, protector);
            await direct.UpsertNodeAsync(new StoredNode(directId, new ByteString(new byte[] { 2 })))
                .ConfigureAwait(false);

            var changes = new List<NodeStateChange>();
            await foreach (NodeStateChange change in direct.ReadDeltaLogAsync(0).ConfigureAwait(false))
            {
                changes.Add(change);
            }
            Assert.That(changes, Has.Count.EqualTo(2));
            Assert.That(changes[0].Sequence, Is.EqualTo(1));
            Assert.That(changes[1].Sequence, Is.EqualTo(2));
            (bool hostedStored, _) = await crdt.TryGetAsync("n/" + hostedId).ConfigureAwait(false);
            (bool directStored, _) = await crdt.TryGetAsync("n/" + directId).ConfigureAwait(false);
            (bool misplacedCounter, _) = await crdt.TryGetAsync(InMemoryNodeStateStore.SequenceKey)
                .ConfigureAwait(false);
            Assert.That(hostedStored, Is.True);
            Assert.That(directStored, Is.True);
            Assert.That(misplacedCounter, Is.False);
        }

        [Test]
        public void ConstructorThrowsOnNullArguments()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var election = new StaticLeaderElection(true);

            Assert.That(() => new DistributedAddressSpaceStartupTask(null!, election), Throws.ArgumentNullException);
            Assert.That(() => new DistributedAddressSpaceStartupTask(kv, null!), Throws.ArgumentNullException);
        }
    }
}
