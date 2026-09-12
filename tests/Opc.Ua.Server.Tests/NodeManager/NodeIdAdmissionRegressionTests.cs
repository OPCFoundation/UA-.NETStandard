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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManagement")]
    public sealed class NodeIdAdmissionRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CollidingDerivedNodeIdDoesNotReplaceExistingNodeAsync(bool customAllocator)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (var context = CreateContext())
            {
                AddNodesItem first = manager.CreateItem("First", ReferenceTypeIds.Organizes);
                (ServiceResult initial, NodeId id) = await manager.AddNodeAsync(context, first).ConfigureAwait(false);
                Assert.That(initial.StatusCode, Is.EqualTo(StatusCodes.Good));
                NodeState original = manager.GetNode(id);
                if (customAllocator)
                {
                    manager.ForcedId = id;
                }
                AddNodesItem second = manager.CreateItem(
                    customAllocator ? "Second" : "First", ReferenceTypeIds.HasComponent);
                (ServiceResult rejected, NodeId rejectedId) =
                    await manager.AddNodeAsync(context, second).ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(rejectedId.IsNull, Is.True);
                Assert.That(manager.GetNode(id), Is.SameAs(original));
                Assert.That(original.BrowseName, Is.EqualTo(first.BrowseName));
                Assert.That(original.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder), Is.True);
                Assert.That(original.ReferenceExists(
                    ReferenceTypeIds.HasComponent, true, ObjectIds.ObjectsFolder), Is.False);
                Mock.Get(server.Object.NodeManager).Verify(
                    value => value.AddReferencesAsync(
                        ObjectIds.ObjectsFolder, It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()),
                    Times.Once);
            }
        }

        [Test]
        public async Task ConcurrentNodeIdAdmissionHasOnlyOneWinnerAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object) { PauseNextRegistration = true })
            using (var context = CreateContext())
            {
                manager.ForcedId = new NodeId(101, manager.NamespaceIndexes[0]);
                Task<(ServiceResult result, NodeId addedNodeId)> first = manager.AddNodeAsync(
                    context, manager.CreateItem("First", ReferenceTypeIds.Organizes)).AsTask();
                (ServiceResult Result, NodeId Id) firstOutcome;
                try
                {
                    await manager.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    (ServiceResult winnerStatus, NodeId winnerId) = await manager.AddNodeAsync(
                        context, manager.CreateItem("Second", ReferenceTypeIds.HasComponent)).ConfigureAwait(false);
                    Assert.That(winnerStatus.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(winnerId, Is.EqualTo(manager.ForcedId));
                }
                finally
                {
                    manager.Release.TrySetResult(true);
                    firstOutcome = await first.ConfigureAwait(false);
                }
                Assert.That(firstOutcome.Result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(firstOutcome.Id.IsNull, Is.True);
                Assert.That(manager.GetNode(manager.ForcedId).BrowseName.Name, Is.EqualTo("Second"));
                Mock.Get(server.Object.NodeManager).Verify(
                    value => value.AddReferencesAsync(
                        ObjectIds.ObjectsFolder, It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()),
                    Times.Once);
            }
        }

        [Test]
        public async Task RuntimeReplacementRemainsAvailableOutsideAddNodesAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (var context = CreateContext())
            {
                (ServiceResult status, NodeId id) = await manager.AddNodeAsync(
                    context, manager.CreateItem("First", ReferenceTypeIds.Organizes)).ConfigureAwait(false);
                Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.Good));
                var replacement = new BaseObjectState(null)
                {
                    NodeId = id,
                    BrowseName = new QualifiedName("Replacement", manager.NamespaceIndexes[0])
                };
                await manager.RegisterAsync(replacement).ConfigureAwait(false);
                Assert.That(manager.GetNode(id), Is.SameAs(replacement));
            }
        }

        private static OperationContext CreateContext()
        {
            return new OperationContext(new RequestHeader(), null, RequestType.AddNodes, RequestLifetime.None);
        }

        private sealed class AdmissionHooks : AsyncCustomNodeManager
        {
            public AdmissionHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:node-admission")
            {
            }

            public override bool AllowNodeManagement => true;
            public NodeId ForcedId { get; set; }
            public bool PauseNextRegistration { get; set; }
            public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public AddNodesItem CreateItem(string name, NodeId referenceType)
            {
                return new AddNodesItem
                {
                    ParentNodeId = ObjectIds.ObjectsFolder,
                    ReferenceTypeId = referenceType,
                    BrowseName = new QualifiedName(name, NamespaceIndexes[0]),
                    NodeClass = NodeClass.Object
                };
            }

            public NodeState GetNode(NodeId id)
            {
                return PredefinedNodes[id];
            }

            public ValueTask RegisterAsync(NodeState node)
            {
                return AddPredefinedNodeAsync(SystemContext, node);
            }

            protected override NodeId AllocateNodeIdForAddNodes(
                ServerSystemContext context,
                BaseInstanceState instance,
                NodeId parentNodeId)
            {
                return ForcedId.IsNull
                    ? base.AllocateNodeIdForAddNodes(context, instance, parentNodeId)
                    : ForcedId;
            }

            protected override async ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
                ISystemContext context,
                NodeState predefinedNode,
                CancellationToken cancellationToken = default)
            {
                if (PauseNextRegistration)
                {
                    PauseNextRegistration = false;
                    Entered.TrySetResult(true);
                    await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                return predefinedNode;
            }
        }
    }
}
