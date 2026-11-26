// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Direct readiness checks distinguish lookup permission from retaining a service routing view.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class NodeLookupReadinessRegressionTests
    {
        /// <summary>
        /// Readiness-scoped lookup may see an initializer's nodes without changing the request's pinned routes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitAndContextFreeLookupHonorReadinessWithoutPromotingLiveExistenceAsync(
            bool initializer)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var manager = new Mock<IAsyncNodeManager>();
                var nodeId = new NodeId("PendingLookup", 1);
                var handle = new NodeHandle { NodeId = nodeId };
                manager.SetupGet(value => value.NamespaceUris).Returns([DeterministicServerMock.TestNamespaceUri]);
                manager.Setup(value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(handle);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(manager.Object);
                generation.ReadinessPending = true;
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [manager.Object]);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                var routing = (IRequestRouting)master;
                routing.PinRequest(context);
                NodeManagerRoutingTable.RoutingSnapshot captured = context.RoutingSnapshot!;
                manager.Invocations.Clear();
                using (NodeManagerReadinessScope? readiness = initializer
                    ? new NodeManagerReadinessScope(server.Object, generation)
                    : null)
                {
                    var unbound = await routing.GetManagerHandleAsync(context, nodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(unbound.handle, Is.Null);
                    Assert.That(unbound.nodeManager, Is.Null);
                    context.Readiness = readiness;
                    Task<(object? handle, IAsyncNodeManager? nodeManager)> lookup;
                    using (ExecutionContext.SuppressFlow())
                    {
                        lookup = Task.Run(async () => await routing
                            .GetManagerHandleAsync(context, nodeId, CancellationToken.None)
                            .ConfigureAwait(false));
                    }
                    (object? explicitHandle, IAsyncNodeManager? explicitOwner) =
                        await lookup.ConfigureAwait(false);
                    (object? liveHandle, IAsyncNodeManager? liveOwner) =
                        await master.GetManagerHandleAsync(nodeId).ConfigureAwait(false);
                    bool materialized = await master.IsNodeMaterializedAsync(nodeId).ConfigureAwait(false);
                    Assert.That(explicitHandle, initializer ? Is.SameAs(handle) : Is.Null);
                    Assert.That(explicitOwner, initializer ? Is.SameAs(manager.Object) : Is.Null);
                    Assert.That(liveHandle, Is.Null);
                    Assert.That(liveOwner, Is.Null);
                    Assert.That(materialized, Is.False);
                    Assert.That(context.RoutingSnapshot, Is.SameAs(captured));
                    manager.Verify(value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()),
                        Times.Exactly(initializer ? 1 : 0));
                }

                // Readiness capability is revocable, even though the context still owns the same routing view.
                var expired = await routing.GetManagerHandleAsync(context, nodeId, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(expired.handle, Is.Null);
                Assert.That(expired.nodeManager, Is.Null);
                Assert.That(await master.IsNodeMaterializedAsync(nodeId).ConfigureAwait(false), Is.False);
                generation.ReadinessPending = false;
                Assert.That(await master.IsNodeMaterializedAsync(nodeId).ConfigureAwait(false), Is.True);
                Assert.That(await master.IsNodeMaterializedAsync(new NodeId("UnknownLookup", 1))
                    .ConfigureAwait(false), Is.False);
                Assert.That(context.RoutingSnapshot, Is.SameAs(captured));
                (object? readyHandle, IAsyncNodeManager? readyOwner) =
                    await routing.GetManagerHandleAsync(context, nodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                Assert.That(readyHandle, Is.SameAs(handle));
                Assert.That(readyOwner, Is.SameAs(manager.Object));
                manager.Verify(value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()),
                    Times.Exactly(initializer ? 3 : 2));
            }
        }
    }
}
