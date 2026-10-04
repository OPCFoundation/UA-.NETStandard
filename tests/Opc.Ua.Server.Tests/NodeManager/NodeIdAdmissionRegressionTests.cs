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
    /// <summary>
    /// Verifies exclusive AddNodes identifier admission while preserving intentional runtime node replacement.
    /// </summary>
    [TestFixture]
    [Category("NodeManagement")]
    public sealed class NodeIdAdmissionRegressionTests
    {
        [TestCase("service")]
        [TestCase("exception")]
        [TestCase("cancelled")]
        public async Task FailedAddNodeReparksRecoveredMonitoredItemAndLaterRecoversAsync(string failureKind)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (OperationContext context = CreateContext())
            using (var cancellation = new CancellationTokenSource())
            {
                var nodeId = new NodeId("RecoveredChild", manager.NamespaceIndexes[0]);
                var identity = new Mock<IUserIdentity>();
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Identity).Returns(identity.Object);
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                session.SetupGet(value => value.PreferredLocales).Returns([]);
                var subscription = new Mock<ISubscription>();
                subscription.SetupGet(value => value.Session).Returns(session.Object);
                subscription.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                using var monitoredItem = new MonitoredItem(
                    server.Object, manager, new NodeHandle(), 1, 2,
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DisplayName },
                    DiagnosticsMasks.None, TimestampsToReturn.Both, MonitoringMode.Reporting, 3,
                    null, null, null, 0, 1, true, 0)
                {
                    SubscriptionCallback = subscription.Object
                };
                var detachable = (IDetachableMonitoredItem)monitoredItem;
                var lifecycle = (INodeManagerMonitoredItemLifecycle)manager;
                detachable.Detach(server.Object);
                detachable.QueueNodeIdUnknown();
                var master = new Mock<IMasterNodeManager>();
                Mock<IDynamicNodeManagerHost> recovery = master.As<IDynamicNodeManagerHost>();
                master.SetupGet(value => value.CoreNodeManager).Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.NodeManager).Returns(master.Object);
                recovery.Setup(value => value.RecoverDetachedMonitoredItemsAsync(
                        It.IsAny<IAsyncNodeManager>(), It.IsAny<IReadOnlyCollection<NodeId>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (IAsyncNodeManager owner, IReadOnlyCollection<NodeId> ids, CancellationToken ct) =>
                    {
                        Assert.That(owner, Is.SameAs(manager));
                        Assert.That(ids, Does.Contain(nodeId));
                        ServiceResult attached = await lifecycle.AttachMonitoredItemAsync(monitoredItem, ct)
                            .ConfigureAwait(false);
                        Assert.That(attached.StatusCode, Is.EqualTo(StatusCodes.Good));
                    });
                bool fail = true;
                var injectedFailure = new InvalidOperationException("Remote parent publication failed.");
                master.Setup(value => value.AddReferencesAsync(
                        It.IsAny<NodeId>(), It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Assert.That(manager.GetNode(nodeId).BrowseName.Name, Is.EqualTo("RecoveredChild"));
                        if (!fail)
                        {
                            return default;
                        }
                        if (failureKind == "cancelled")
                        {
                            cancellation.Cancel();
                            throw new OperationCanceledException(cancellation.Token);
                        }
                        throw failureKind == "service"
                            ? new ServiceResultException(StatusCodes.BadResourceUnavailable)
                            : injectedFailure;
                    });
                AddNodesItem request = manager.CreateItem("RecoveredChild", ReferenceTypeIds.HasComponent);
                request.RequestedNewNodeId = nodeId;

                if (failureKind == "service")
                {
                    (ServiceResult failure, NodeId failedId) = await manager.AddNodeAsync(
                        context, request, cancellation.Token).ConfigureAwait(false);
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                    Assert.That(failedId.IsNull, Is.True);
                }
                else if (failureKind == "cancelled")
                {
                    OperationCanceledException failure = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                        await manager.AddNodeAsync(context, request, cancellation.Token).ConfigureAwait(false))!;
                    Assert.That(failure.CancellationToken, Is.EqualTo(cancellation.Token));
                }
                else
                {
                    InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await manager.AddNodeAsync(context, request, cancellation.Token).ConfigureAwait(false))!;
                    Assert.That(failure, Is.SameAs(injectedFailure));
                }

                IReadOnlyList<IMonitoredItem> snapshot = await lifecycle.GetMonitoredItemsSnapshotAsync()
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(manager.NodeCount, Is.Zero);
                    Assert.That(snapshot, Is.Empty);
                    Assert.That(detachable.IsDeleted, Is.True);
                    Assert.That(detachable.IsDetached, Is.True);
                    Assert.That(monitoredItem.NodeManager, Is.SameAs(MonitoredItem.GetDetachedOwner(server.Object)));
                    Assert.That(monitoredItem.ManagerHandle, Is.SameAs(MonitoredItem.DetachedHandle));
                });
                using var publishContext = new OperationContext(monitoredItem);
                var notifications = new Queue<MonitoredItemNotification>();
                monitoredItem.Publish(publishContext, notifications, new Queue<DiagnosticInfo>(), 10,
                    NullLogger.Instance);
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(notifications.Dequeue().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                fail = false;
                (ServiceResult retried, NodeId retriedId) = await manager.AddNodeAsync(context, request)
                    .ConfigureAwait(false);
                Assert.That(retried.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(retriedId, Is.EqualTo(nodeId));
                Assert.That(detachable.IsDeleted, Is.False);
                Assert.That(detachable.IsDetached, Is.False);
                Assert.That(monitoredItem.NodeManager, Is.SameAs(manager));
                snapshot = await lifecycle.GetMonitoredItemsSnapshotAsync().ConfigureAwait(false);
                Assert.That(snapshot, Is.EqualTo(new IMonitoredItem[] { monitoredItem }));
                monitoredItem.Publish(publishContext, notifications, new Queue<DiagnosticInfo>(), 10,
                    NullLogger.Instance);
                Assert.That(notifications, Has.Count.EqualTo(1));
                DataValue recovered = notifications.Dequeue().Value;
                Assert.That(recovered.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(recovered.WrappedValue, Is.EqualTo(new Variant(new LocalizedText("RecoveredChild"))));
            }
        }

        [TestCase("recovery", true, "service")]
        [TestCase("recovery", true, "exception")]
        [TestCase("recovery", true, "cancelled")]
        [TestCase("recovery", false, "service")]
        [TestCase("recovery", false, "exception")]
        [TestCase("recovery", false, "cancelled")]
        [TestCase("references", false, "service")]
        [TestCase("references", false, "exception")]
        [TestCase("references", false, "cancelled")]
        public async Task FailedAddNodeRemovesIndexedNodeAndAllowsRetry(
            string stage,
            bool localParent,
            string failureKind)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (OperationContext context = CreateContext())
            using (var cancellation = new CancellationTokenSource())
            {
                ushort ns = manager.NamespaceIndexes[0];
                var parent = new BaseObjectState(null)
                {
                    NodeId = localParent ? new NodeId("Parent", ns) : ObjectIds.ObjectsFolder,
                    BrowseName = new QualifiedName("Parent", ns)
                };
                if (localParent)
                {
                    await manager.RegisterAsync(parent).ConfigureAwait(false);
                }
                var master = new Mock<IMasterNodeManager>();
                Mock<IDynamicNodeManagerHost> recovery = master.As<IDynamicNodeManagerHost>();
                server.Setup(value => value.NodeManager).Returns(master.Object);
                bool fail = true;
                Exception Failure()
                {
                    if (failureKind == "cancelled")
                    {
                        cancellation.Cancel();
                        return new OperationCanceledException(cancellation.Token);
                    }
                    return failureKind == "service"
                        ? new ServiceResultException(StatusCodes.BadResourceUnavailable)
                        : new InvalidOperationException("Registration failed after indexing.");
                }
                recovery.Setup(value => value.RecoverDetachedMonitoredItemsAsync(
                        It.IsAny<IAsyncNodeManager>(),
                        It.IsAny<IReadOnlyCollection<NodeId>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() => fail && stage == "recovery" ? throw Failure() : default);
                master.Setup(value => value.AddReferencesAsync(
                        It.IsAny<NodeId>(), It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()))
                    .Returns(() => fail && stage == "references" ? throw Failure() : default);
                AddNodesItem item = manager.CreateItem("Child", ReferenceTypeIds.HasComponent);
                item.ParentNodeId = parent.NodeId;
                item.RequestedNewNodeId = new NodeId("Child", ns);

                if (failureKind == "service")
                {
                    (ServiceResult result, NodeId addedId) = await manager.AddNodeAsync(
                        context, item, cancellation.Token).ConfigureAwait(false);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                    Assert.That(addedId.IsNull, Is.True);
                }
                else if (failureKind == "cancelled")
                {
                    Assert.ThrowsAsync<OperationCanceledException>(async () =>
                        await manager.AddNodeAsync(context, item, cancellation.Token).ConfigureAwait(false));
                }
                else
                {
                    Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await manager.AddNodeAsync(context, item, cancellation.Token).ConfigureAwait(false));
                }

                Assert.That(manager.NodeCount, Is.EqualTo(localParent ? 1 : 0));
                Assert.That(parent.FindChildWithQualifiedName(manager.SystemContext, item.BrowseName), Is.Null);
                fail = false;
                (ServiceResult retry, NodeId retryId) = await manager.AddNodeAsync(context, item)
                    .ConfigureAwait(false);
                Assert.That(retry.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(retryId, Is.EqualTo(new NodeId("Child", ns)));
                Assert.That(manager.GetNode(retryId).BrowseName, Is.EqualTo(item.BrowseName));
            }
        }

        /// <summary>
        /// Verifies automatic allocation chooses another identifier without replacing the original
        /// node or its references.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CollidingDerivedNodeIdDoesNotReplaceExistingNodeAsync(bool customAllocator)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (OperationContext context = CreateContext())
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
                (ServiceResult added, NodeId addedId) =
                    await manager.AddNodeAsync(context, second).ConfigureAwait(false);
                Assert.That(added.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(addedId.IsNull, Is.False);
                Assert.That(addedId, Is.Not.EqualTo(id));
                Assert.That(addedId.NamespaceIndex, Is.EqualTo(id.NamespaceIndex));
                Assert.That(manager.GetNode(id), Is.SameAs(original));
                Assert.That(original.BrowseName, Is.EqualTo(first.BrowseName));
                Assert.That(original.ReferenceExists(
                    ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder), Is.True);
                Assert.That(original.ReferenceExists(
                    ReferenceTypeIds.HasComponent, true, ObjectIds.ObjectsFolder), Is.False);
                Assert.That(manager.GetNode(addedId).BrowseName, Is.EqualTo(second.BrowseName));
                Assert.That(manager.GetNode(addedId).ReferenceExists(
                    ReferenceTypeIds.HasComponent, true, ObjectIds.ObjectsFolder), Is.True);
                Mock.Get(server.Object.NodeManager).Verify(
                    value => value.AddReferencesAsync(
                        ObjectIds.ObjectsFolder, It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
            }
        }

        /// <summary>
        /// Verifies automatic requests reserve distinct identifiers while explicit requests cannot share a reservation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentNodeIdAdmissionPreservesAutomaticAndExplicitIdentityAsync(bool explicitId)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object) { PauseNextRegistration = true })
            using (OperationContext context = CreateContext())
            {
                manager.ForcedId = new NodeId(101, manager.NamespaceIndexes[0]);
                AddNodesItem firstItem = manager.CreateItem("First", ReferenceTypeIds.Organizes);
                AddNodesItem secondItem = manager.CreateItem("Second", ReferenceTypeIds.HasComponent);
                if (explicitId)
                {
                    firstItem.RequestedNewNodeId = manager.ForcedId;
                    secondItem.RequestedNewNodeId = manager.ForcedId;
                }
                Task<(ServiceResult result, NodeId addedNodeId)> first = manager.AddNodeAsync(
                    context, firstItem).AsTask();
                (ServiceResult Result, NodeId Id) firstOutcome;
                (ServiceResult Result, NodeId Id) secondOutcome;
                try
                {
                    await manager.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    secondOutcome = await manager.AddNodeAsync(context, secondItem).ConfigureAwait(false);
                    Assert.That(secondOutcome.Result.StatusCode,
                        Is.EqualTo(explicitId ? StatusCodes.BadNodeIdExists : StatusCodes.Good));
                    Assert.That(secondOutcome.Id.IsNull, Is.EqualTo(explicitId));
                    if (!explicitId)
                    {
                        Assert.That(secondOutcome.Id, Is.Not.EqualTo(manager.ForcedId));
                        Assert.That(manager.GetNode(secondOutcome.Id).BrowseName.Name, Is.EqualTo("Second"));
                    }
                }
                finally
                {
                    manager.Release.TrySetResult(true);
                    firstOutcome = await first.ConfigureAwait(false);
                }
                Assert.That(firstOutcome.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(firstOutcome.Id, Is.EqualTo(manager.ForcedId));
                Assert.That(manager.GetNode(firstOutcome.Id).BrowseName.Name, Is.EqualTo("First"));
                Mock.Get(server.Object.NodeManager).Verify(
                    value => value.AddReferencesAsync(
                        ObjectIds.ObjectsFolder, It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()),
                    Times.Exactly(explicitId ? 1 : 2));
            }
        }

        /// <summary>
        /// Verifies fallback allocation skips already registered counter candidates without replacing any node.
        /// </summary>
        [Test]
        public async Task AutomaticNodeIdAllocationSkipsOccupiedCounterCandidatesAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (OperationContext context = CreateContext())
            {
                ushort ns = manager.NamespaceIndexes[0];
                NodeId[] occupied = [new NodeId(101, ns), new NodeId(102, ns), new NodeId(103, ns)];
                var originals = new List<NodeState>();
                foreach (NodeId nodeId in occupied)
                {
                    var node = new BaseObjectState(null)
                    {
                        NodeId = nodeId,
                        BrowseName = new QualifiedName(nodeId.ToString(), ns)
                    };
                    await manager.RegisterAsync(node).ConfigureAwait(false);
                    originals.Add(node);
                }
                var available = new NodeId(104, ns);
                IRebasableNodeIdFactory originalFactory = manager.NodeIdFactory;
                var factory = new Mock<IRebasableNodeIdFactory>(MockBehavior.Strict);
                factory.SetupGet(value => value.DefaultNamespaceIndex).Returns(ns);
                factory.Setup(value => value.WithCollisionDetection(It.IsAny<bool>())).Returns(factory.Object);
                factory.Setup(value => value.New(It.IsAny<ISystemContext>(), It.IsAny<NodeState>()))
                    .Returns((ISystemContext systemContext, NodeState node) =>
                        originalFactory.New(systemContext, node));
                factory.SetupSequence(value => value.NextCounterNodeId())
                    .Returns(occupied[0])
                    .Returns(occupied[1])
                    .Returns(occupied[2])
                    .Returns(available);
                manager.NodeIdFactory = factory.Object;
                manager.ForcedId = occupied[0];

                (ServiceResult result, NodeId addedId) = await manager.AddNodeAsync(
                    context, manager.CreateItem("New", ReferenceTypeIds.Organizes)).ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(addedId, Is.EqualTo(available));
                Assert.That(manager.GetNode(addedId).BrowseName.Name, Is.EqualTo("New"));
                for (int i = 0; i < occupied.Length; i++)
                {
                    Assert.That(manager.GetNode(occupied[i]), Is.SameAs(originals[i]));
                }
                factory.Verify(value => value.NextCounterNodeId(), Times.Exactly(4));
            }
        }

        /// <summary>
        /// Verifies invalid or non-progressing counter providers fail explicitly without mutating registered nodes.
        /// </summary>
        [TestCase("null")]
        [TestCase("foreign")]
        [TestCase("repeated")]
        public async Task AutomaticNodeIdAllocationRejectsInvalidCounterOutputAsync(string output)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (OperationContext context = CreateContext())
            {
                ushort ns = manager.NamespaceIndexes[0];
                var occupied = new NodeId(101, ns);
                var original = new BaseObjectState(null)
                {
                    NodeId = occupied,
                    BrowseName = new QualifiedName("Original", ns)
                };
                await manager.RegisterAsync(original).ConfigureAwait(false);
                var factory = new Mock<IRebasableNodeIdFactory>(MockBehavior.Strict);
                factory.SetupGet(value => value.DefaultNamespaceIndex).Returns(ns);
                factory.Setup(value => value.WithCollisionDetection(It.IsAny<bool>())).Returns(factory.Object);
                factory.Setup(value => value.NextCounterNodeId()).Returns(output switch
                {
                    "null" => NodeId.Null,
                    "foreign" => new NodeId(102, (ushort)(ns + 1)),
                    _ => occupied
                });
                manager.NodeIdFactory = factory.Object;
                manager.ForcedId = occupied;
                AddNodesItem item = manager.CreateItem("New", ReferenceTypeIds.Organizes);

                if (output == "repeated")
                {
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await manager.AddNodeAsync(context, item).ConfigureAwait(false))!;
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                }
                else
                {
                    (ServiceResult result, NodeId addedId) =
                        await manager.AddNodeAsync(context, item).ConfigureAwait(false);
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdRejected));
                    Assert.That(addedId.IsNull, Is.True);
                }
                Assert.That(manager.NodeCount, Is.EqualTo(1));
                Assert.That(manager.GetNode(occupied), Is.SameAs(original));
                factory.Verify(value => value.NextCounterNodeId(), Times.Exactly(output == "repeated" ? 2 : 1));
                Mock.Get(server.Object.NodeManager).Verify(
                    value => value.AddReferencesAsync(
                        ObjectIds.ObjectsFolder, It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        /// <summary>
        /// Verifies interrupted registration releases its ID and parent linkage before a retry.
        /// </summary>
        [TestCase(false, "canceled", true)]
        [TestCase(true, "canceled", true)]
        [TestCase(false, "service", true)]
        [TestCase(true, "service", true)]
        [TestCase(false, "exception", true)]
        [TestCase(true, "exception", true)]
        [TestCase(false, "canceled", false)]
        [TestCase(true, "canceled", false)]
        [TestCase(false, "service", false)]
        [TestCase(true, "service", false)]
        [TestCase(false, "exception", false)]
        [TestCase(true, "exception", false)]
        public async Task InterruptedNodeIdReservationCanBeReusedAsync(
            bool automatic,
            string outcome,
            bool localParent)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (OperationContext context = CreateContext())
            using (var cancellation = new CancellationTokenSource())
            {
                ushort ns = manager.NamespaceIndexes[0];
                var parent = new BaseObjectState(null)
                {
                    NodeId = localParent ? new NodeId(100, ns) : ObjectIds.ObjectsFolder,
                    BrowseName = new QualifiedName("Parent", ns)
                };
                if (localParent)
                {
                    await manager.RegisterAsync(parent).ConfigureAwait(false);
                }
                manager.PauseNextRegistration = true;
                manager.ForcedId = new NodeId(101, ns);
                AddNodesItem item = manager.CreateItem("Child", ReferenceTypeIds.HasComponent);
                item.ParentNodeId = parent.NodeId;
                if (!automatic)
                {
                    item.RequestedNewNodeId = manager.ForcedId;
                }
                Task<(ServiceResult result, NodeId addedNodeId)> first =
                    manager.AddNodeAsync(context, item, cancellation.Token).AsTask();
                var failure = new InvalidOperationException("Controlled node registration failure.");
                try
                {
                    await manager.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (outcome == "canceled")
                    {
                        cancellation.Cancel();
                        Task completed = await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(5)))
                            .ConfigureAwait(false);
                        Assert.That(completed, Is.SameAs(first));
                        OperationCanceledException error = null;
                        try
                        {
                            await first.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException ex)
                        {
                            error = ex;
                        }
                        Assert.That(error, Is.Not.Null);
                        Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
                    }
                    else
                    {
                        manager.RegistrationFailure = outcome == "service"
                            ? new ServiceResultException(StatusCodes.BadResourceUnavailable)
                            : failure;
                        manager.Release.TrySetResult(true);
                        if (outcome == "service")
                        {
                            (ServiceResult result, NodeId addedId) =
                                await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                            Assert.That(addedId.IsNull, Is.True);
                        }
                        else
                        {
                            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                                await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
                            Assert.That(error, Is.SameAs(failure));
                        }
                    }

                    Assert.That(manager.NodeCount, Is.EqualTo(localParent ? 1 : 0));
                    var children = new List<BaseInstanceState>();
                    if (localParent)
                    {
                        Assert.That(parent.FindChild(manager.SystemContext, item.BrowseName), Is.Null);
                        parent.GetChildren(manager.SystemContext, children);
                        Assert.That(children, Is.Empty);
                    }
                    Mock.Get(server.Object.NodeManager).Verify(
                        value => value.AddReferencesAsync(
                            parent.NodeId, It.IsAny<IList<IReference>>(), It.IsAny<CancellationToken>()),
                        Times.Never);
                    manager.RegistrationFailure = null;
                    item.RequestedNewNodeId = manager.ForcedId;
                    (ServiceResult retried, NodeId retriedId) =
                        await manager.AddNodeAsync(context, item).ConfigureAwait(false);

                    Assert.That(retried.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(retriedId, Is.EqualTo(manager.ForcedId));
                    Assert.That(manager.NodeCount, Is.EqualTo(localParent ? 2 : 1));
                    if (localParent)
                    {
                        Assert.That(parent.FindChild(manager.SystemContext, item.BrowseName),
                            Is.SameAs(manager.GetNode(retriedId)));
                        parent.GetChildren(manager.SystemContext, children);
                        Assert.That(children, Has.Count.EqualTo(1));
                        Assert.That(children[0], Is.SameAs(manager.GetNode(retriedId)));
                    }
                    else
                    {
                        Assert.That(manager.GetNode(retriedId).ReferenceExists(
                            item.ReferenceTypeId, true, parent.NodeId), Is.True);
                    }
                    Mock.Get(server.Object.NodeManager).Verify(
                        value => value.AddReferencesAsync(
                            parent.NodeId,
                            It.Is<IList<IReference>>(references =>
                                references.Count == 1 &&
                                references[0].ReferenceTypeId == item.ReferenceTypeId &&
                                !references[0].IsInverse &&
                                references[0].TargetId == (ExpandedNodeId)retriedId),
                            It.IsAny<CancellationToken>()),
                        localParent ? Times.Never() : Times.Once());
                }
                finally
                {
                    manager.Release.TrySetResult(true);
                    Task completed = await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(5)))
                        .ConfigureAwait(false);
                    Assert.That(completed, Is.SameAs(first));
                    try
                    {
                        await first.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        // The original cancellation has already been asserted.
                    }
                    catch (InvalidOperationException ex) when (ReferenceEquals(ex, failure))
                    {
                        // The injected registration exception has already been asserted.
                    }
                }
            }
        }

        /// <summary>
        /// Verifies that direct predefined-node registration can still replace a node outside AddNodes admission.
        /// </summary>
        [Test]
        public async Task RuntimeReplacementRemainsAvailableOutsideAddNodesAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AdmissionHooks(server.Object))
            using (OperationContext context = CreateContext())
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

        /// <summary>
        /// Creates the AddNodes operation context used for identifier admission.
        /// </summary>
        private static OperationContext CreateContext()
        {
            return new OperationContext(new RequestHeader(), null, RequestType.AddNodes, RequestLifetime.None);
        }

        /// <summary>
        /// Exposes node-management admission with controllable identifier allocation and registration suspension.
        /// </summary>
        private sealed class AdmissionHooks : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates a node manager that permits AddNodes requests in an isolated test namespace.
            /// </summary>
            public AdmissionHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:node-admission")
            {
            }

            /// <summary>
            /// Gets whether AddNodes operations are permitted for this admission test manager.
            /// </summary>
            public override bool AllowNodeManagement => true;

            /// <summary>
            /// Gets or sets a forced allocation result, or a null identifier to use normal derivation.
            /// </summary>
            public NodeId ForcedId { get; set; }

            /// <summary>
            /// Gets or sets whether the next predefined-node registration pauses before admission completes.
            /// </summary>
            public bool PauseNextRegistration { get; set; }

            /// <summary>
            /// Gets or sets a failure thrown after the controlled behavior-registration barrier.
            /// </summary>
            public Exception RegistrationFailure { get; set; }

            /// <summary>
            /// Gets the number of nodes whose registration has completed.
            /// </summary>
            public int NodeCount => PredefinedNodes.Count;

            /// <summary>
            /// Gets the signal raised when the selected registration reaches its pause.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the gate that releases the paused registration.
            /// </summary>
            public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Creates an object-add request under Objects with the requested browse name and reference type.
            /// </summary>
            public AddNodesItem CreateItem(string name, NodeId referenceType)
            {
                return new AddNodesItem
                {
                    ParentNodeId = ObjectIds.ObjectsFolder,
                    ReferenceTypeId = referenceType,
                    BrowseName = new QualifiedName(name, NamespaceIndexes[0]),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                };
            }

            /// <summary>
            /// Gets the currently registered node to identify which admission attempt won.
            /// </summary>
            public NodeState GetNode(NodeId id)
            {
                return PredefinedNodes[id];
            }

            /// <summary>
            /// Registers a predefined node directly to exercise intentional runtime replacement.
            /// </summary>
            public ValueTask RegisterAsync(NodeState node)
            {
                return AddPredefinedNodeAsync(SystemContext, node);
            }

            /// <summary>
            /// Returns a forced collision identifier when configured, otherwise uses normal identifier allocation.
            /// </summary>
            protected override NodeId AllocateNodeIdForAddNodes(
                ServerSystemContext context,
                BaseInstanceState instance,
                NodeId parentNodeId)
            {
                return ForcedId.IsNull
                    ? base.AllocateNodeIdForAddNodes(context, instance, parentNodeId)
                    : ForcedId;
            }

            /// <summary>
            /// Pauses one registration at the behavior hook so another AddNodes request can race admission.
            /// </summary>
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
                if (RegistrationFailure is { } failure)
                {
                    throw failure;
                }
                return predefinedNode;
            }
        }
    }
}
