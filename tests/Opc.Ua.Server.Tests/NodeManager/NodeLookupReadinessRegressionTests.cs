/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
                    (object? explicitHandle, IAsyncNodeManager? explicitOwner) =
                        await routing.GetManagerHandleAsync(context, nodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    (object? liveHandle, IAsyncNodeManager? liveOwner) =
                        await master.GetManagerHandleAsync(nodeId).ConfigureAwait(false);
                    bool materialized = await master.IsNodeMaterializedAsync(nodeId).ConfigureAwait(false);
                    Assert.That(explicitHandle, initializer ? Is.SameAs(handle) : Is.Null);
                    Assert.That(explicitOwner, initializer ? Is.SameAs(manager.Object) : Is.Null);
                    Assert.That(liveHandle, initializer ? Is.SameAs(handle) : Is.Null);
                    Assert.That(liveOwner, initializer ? Is.SameAs(manager.Object) : Is.Null);
                    Assert.That(materialized, Is.EqualTo(initializer));
                    Assert.That(context.RoutingSnapshot, Is.SameAs(captured));
                    manager.Verify(value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()),
                        Times.Exactly(initializer ? 3 : 0));
                }

                // Readiness capability is revocable, even though the context still owns the same routing view.
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
                    Times.Exactly(initializer ? 5 : 2));
            }
        }
    }
}
