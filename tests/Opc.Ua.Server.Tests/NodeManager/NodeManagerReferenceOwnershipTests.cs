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
 *
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
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [NonParallelizable]
    public sealed class NodeManagerReferenceOwnershipTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReferenceMutationReportsOwnershipForEachDirection(bool inverse)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaces = new NamespaceTable();
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            using var queues = new MonitoredItemQueueFactory(telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            using var manager = new ReferenceNodeManager(server.Object);
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("Source", manager.NamespaceIndexes[0]),
                BrowseName = new QualifiedName("Source", manager.NamespaceIndexes[0])
            };
            await manager.RegisterAsync(source).ConfigureAwait(false);
            var target = new NodeId("Peer", manager.NamespaceIndexes[0]);
            var item = new AddReferencesItem
            {
                SourceNodeId = source.NodeId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = !inverse,
                TargetNodeId = target
            };
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.AddReferences, RequestLifetime.None);

            ServiceResult first = await manager.AddReferenceAsync(context, item).ConfigureAwait(false);
            ServiceResult duplicate = await manager.AddReferenceAsync(context, item).ConfigureAwait(false);
            item.IsForward = inverse;
            ServiceResult opposite = await manager.AddReferenceAsync(context, item).ConfigureAwait(false);

            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadDuplicateReferenceNotAllowed));
            Assert.That(opposite.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(source.ReferenceExists(item.ReferenceTypeId, inverse, target), Is.True);
            Assert.That(source.ReferenceExists(item.ReferenceTypeId, !inverse, target), Is.True);

            ServiceResult removed = await manager.DeleteReferenceAsync(context, new DeleteReferencesItem
            {
                SourceNodeId = source.NodeId,
                ReferenceTypeId = item.ReferenceTypeId,
                IsForward = !inverse,
                TargetNodeId = target,
                DeleteBidirectional = false
            }).ConfigureAwait(false);

            Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(source.ReferenceExists(item.ReferenceTypeId, inverse, target), Is.False);
            Assert.That(source.ReferenceExists(item.ReferenceTypeId, !inverse, target), Is.True);
        }

        [TestCase(0, TestName = "LifecycleAdapterPreservesAddedReferenceResult")]
        [TestCase(1, TestName = "LifecycleAdapterPreservesExistingReferenceResult")]
        [TestCase(2, TestName = "LifecycleAdapterPreservesUnsupportedReferenceResult")]
        public async Task LifecycleAdapterForwardsReferenceAddition(int outcome)
        {
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            IAsyncNodeManager adapted = manager.Object.ToSyncNodeManager().ToAsyncNodeManager();
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.AddReferences, RequestLifetime.None);
            using var cancellation = new CancellationTokenSource();
            var item = new AddReferencesItem
            {
                SourceNodeId = new NodeId("Peer", 2),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = new NodeId("Source", 3)
            };
            ArrayOf<StatusCode> outcomes =
                [StatusCodes.Good, StatusCodes.BadDuplicateReferenceNotAllowed, StatusCodes.BadNotSupported];
            var expected = new ServiceResult(outcomes[outcome]);
            manager.Setup(value => value.AddReferenceAsync(context, item, cancellation.Token))
                .ReturnsAsync(expected);

            ServiceResult actual = await adapted.AddReferenceAsync(context, item, cancellation.Token)
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
            manager.VerifyAll();
        }

        [TestCase(0, TestName = "LifecycleAdapterPreservesDeletedReferenceResult")]
        [TestCase(1, TestName = "LifecycleAdapterPreservesMissingReferenceResult")]
        [TestCase(2, TestName = "LifecycleAdapterPreservesUnsupportedDeletionResult")]
        public async Task LifecycleAdapterForwardsReferenceDeletion(int outcome)
        {
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            IAsyncNodeManager adapted = manager.Object.ToSyncNodeManager().ToAsyncNodeManager();
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.DeleteReferences, RequestLifetime.None);
            using var cancellation = new CancellationTokenSource();
            var item = new DeleteReferencesItem
            {
                SourceNodeId = new NodeId("Peer", 2),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = new NodeId("Source", 3),
                DeleteBidirectional = false
            };
            ArrayOf<StatusCode> outcomes = [StatusCodes.Good, StatusCodes.BadNoMatch, StatusCodes.BadNotSupported];
            var expected = new ServiceResult(outcomes[outcome]);
            manager.Setup(value => value.DeleteReferenceAsync(context, item, cancellation.Token))
                .ReturnsAsync(expected);

            ServiceResult actual = await adapted.DeleteReferenceAsync(context, item, cancellation.Token)
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
            manager.VerifyAll();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LifecycleAdapterPreservesNodeManagementOptIn(bool allowed)
        {
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            manager.SetupGet(value => value.AllowNodeManagement).Returns(allowed);
            IAsyncNodeManager adapted = manager.Object.ToSyncNodeManager().ToAsyncNodeManager();

            Assert.That(adapted.AllowNodeManagement, Is.EqualTo(allowed));
            manager.VerifyAll();
        }

        private sealed class ReferenceNodeManager : AsyncCustomNodeManager
        {
            public ReferenceNodeManager(IServerInternal server)
                : base(server, server.Telemetry.CreateLogger<ReferenceNodeManager>(),
                    "urn:test:reference-ownership")
            {
            }

            public ValueTask RegisterAsync(NodeState node)
            {
                return AddPredefinedNodeAsync(SystemContext, node);
            }
        }
    }
}
