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

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Distributed
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

        [Test]
        public async Task WiresSynchronizerAndSeedsOptedInNodeManagerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
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
            });

            var nodeManager = new Mock<INodeManager>();
            nodeManager.As<ILocalAddressSpaceSource>()
                .Setup(s => s.CreateLocalAddressSpace())
                .Returns(addressSpace);

            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.NodeManagers).Returns(new[] { nodeManager.Object });

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);

            using var kv = new InMemorySharedKeyValueStore();
            var election = new StaticLeaderElection(true);
            var task = new DistributedAddressSpaceStartupTask(kv, election);

            await task.OnServerStartedAsync(server.Object);

            // The writer must have seeded the node into the shared store, and
            // registered a default store in the task-owned registry.
            var verifyStore = new InMemoryNodeStateStore(kv, messageContext);
            IStoredNode? stored = await verifyStore.TryGetNodeAsync(nodeId);

            Assert.That(stored, Is.Not.Null, "writer should have seeded the opted-in node manager's address space");
            Assert.That(task.NodeStateStoreRegistry, Is.Not.Null);
            Assert.That(task.NodeStateStoreRegistry!.Resolve(nodeId), Is.Not.Null, "a default node state store should be registered");

            await task.DisposeAsync();
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
