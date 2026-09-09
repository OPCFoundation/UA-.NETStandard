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

using System.Threading.Tasks;
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
        /// Verifies that startup wires address-space synchronization and seeds opted-in node managers.
        /// </summary>
        [Test]
        public async Task WiresSynchronizerAndSeedsOptedInNodeManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:wire");
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

            var nodeManager = new Mock<INodeManager>();
            nodeManager.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(addressSpace);

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers).Returns([nodeManager.Object]);

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

            // The writer must have seeded the node into the shared store, and
            // registered a default store in the task-owned registry.
            var verifyStore = new InMemoryNodeStateStore(kv, messageContext);
            IStoredNode? stored = await verifyStore.TryGetNodeAsync(nodeId).ConfigureAwait(false);

            Assert.That(stored, Is.Not.Null, "writer should have seeded the opted-in node manager's address space");
            Assert.That(task.NodeStateStoreRegistry, Is.Not.Null);
            Assert.That(task.NodeStateStoreRegistry!.Resolve(nodeId), Is.Not.Null, "a default node state store should be registered");

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
        public void ConstructorThrowsOnNullArguments()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var election = new StaticLeaderElection(true);

            Assert.That(() => new DistributedAddressSpaceStartupTask(null!, election), Throws.ArgumentNullException);
            Assert.That(() => new DistributedAddressSpaceStartupTask(kv, null!), Throws.ArgumentNullException);
        }
    }
}
