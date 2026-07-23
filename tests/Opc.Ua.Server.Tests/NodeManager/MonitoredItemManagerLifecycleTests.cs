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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("NodeManager")]
    [Category("MonitoredItemLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class MonitoredItemManagerLifecycleTests
    {
        public enum ManagerPath
        {
            AsyncMonitoredNode,
            AsyncSamplingGroup,
            SyncAdapterMonitoredNode,
            SyncAdapterSamplingGroup
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task DetachAndRestorePreserveItemIdentityAndOwnershipAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("Lifecycle", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("Lifecycle", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 42
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);

                Mock<IAsyncNodeManager> originalNodeManager = new();
                object originalHandle = new object();
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    originalNodeManager.Object,
                    originalHandle,
                    node.NodeId);
                var itemLifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, itemLifecycle);
                uint id = item.Id;

                ServiceResult attachResult = await owner.Lifecycle
                    .AttachMonitoredItemAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> attached = await owner.Lifecycle
                    .GetMonitoredItemsSnapshotAsync([node.NodeId])
                    .ConfigureAwait(false);
                object attachedHandle = item.ManagerHandle;
                bool ownedAfterAttach = owner.Owns(item.NodeManager);

                ServiceResult detachResult = await owner.Lifecycle
                    .DetachMonitoredItemAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> detached = await owner.Lifecycle
                    .GetMonitoredItemsSnapshotAsync()
                    .ConfigureAwait(false);
                bool wasDetached = itemLifecycle.IsDetached;
                IAsyncNodeManager detachedOwner = item.NodeManager;
                object detachedHandle = item.ManagerHandle;

                Assert.DoesNotThrow(() =>
                    item.QueueValue(
                        new DataValue(new Variant(84), StatusCodes.Good),
                        ServiceResult.Good));

                ServiceResult restoreResult = await owner.Lifecycle
                    .RestoreMonitoredItemAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> restored = await owner.Lifecycle
                    .GetMonitoredItemsSnapshotAsync([node.NodeId])
                    .ConfigureAwait(false);
                object restoredHandle = item.ManagerHandle;
                bool wasAttachedAfterRestore = !itemLifecycle.IsDetached;
                bool ownedAfterRestore = owner.Owns(item.NodeManager);
                await owner.Lifecycle.DetachMonitoredItemAsync(item).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(attachResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(attached, Has.Count.EqualTo(1));
                    Assert.That(attached[0], Is.SameAs(item));
                    Assert.That(item.Id, Is.EqualTo(id));
                    Assert.That(attachedHandle, Is.TypeOf<NodeHandle>());
                    Assert.That(attachedHandle, Is.Not.SameAs(originalHandle));
                    Assert.That(ownedAfterAttach, Is.True);
                    Assert.That(detachResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(detached, Is.Empty);
                    Assert.That(wasDetached, Is.True);
                    Assert.That(
                        detachedOwner,
                        Is.SameAs(DetachedMonitoredItemOwnership.GetOwner(server.Object)));
                    Assert.That(owner.Owns(detachedOwner), Is.False);
                    Assert.That(detachedHandle, Is.SameAs(DetachedMonitoredItemOwnership.Handle));
                    Assert.That(detachedHandle, Is.Not.TypeOf<NodeHandle>());
                    Assert.That(restoreResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(restored, Has.Count.EqualTo(1));
                    Assert.That(restored[0], Is.SameAs(item));
                    Assert.That(item.Id, Is.EqualTo(id));
                    Assert.That(wasAttachedAfterRestore, Is.True);
                    Assert.That(ownedAfterRestore, Is.True);
                    Assert.That(restoredHandle, Is.TypeOf<NodeHandle>());
                    Assert.That(restoredHandle, Is.Not.SameAs(attachedHandle));
                });
            }
        }

        [Test]
        public void SamplingGroupDetachStopsAndAttachRestartsMonitoring()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var samplingGroups = new TrackingSamplingGroupManager(
                    server.Object,
                    nodeManager.Object);
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object,
                    server.Object,
                    samplingGroups);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("SamplingLifecycle", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                var handle = new NodeHandle(node.NodeId, node);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    node.NodeId);
                var itemLifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, itemLifecycle);
                var lifecycle = (IMonitoredItemManagerLifecycle)manager;
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(
                    new OperationContext(item));

                (ServiceResult firstAttach, bool firstChanged) = lifecycle.AttachMonitoredItem(
                    context,
                    handle,
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });
                (ServiceResult detach, bool detachChanged) = lifecycle.DetachMonitoredItem(
                    context,
                    item,
                    static (_, _) => { });
                (ServiceResult secondAttach, bool secondChanged) = lifecycle.AttachMonitoredItem(
                    context,
                    new NodeHandle(node.NodeId, node),
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });

                Assert.Multiple(() =>
                {
                    Assert.That(firstAttach.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(firstChanged, Is.True);
                    Assert.That(detach.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(detachChanged, Is.True);
                    Assert.That(secondAttach.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(secondChanged, Is.True);
                    Assert.That(samplingGroups.StartCount, Is.EqualTo(2));
                    Assert.That(samplingGroups.StopCount, Is.EqualTo(1));
                    Assert.That(samplingGroups.ApplyCount, Is.EqualTo(3));
                    Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                    Assert.That(itemLifecycle.IsDetached, Is.False);
                });

                lifecycle.DetachMonitoredItem(context, item, static (_, _) => { });
            }
        }

        [Test]
        public void MonitoredNodeRebindMovesCallbacksToReplacementNode()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var originalManager = new Mock<IAsyncNodeManager>();
                var replacementManager = new Mock<IAsyncNodeManager>();
                var nodeId = new NodeId("RebindCallbacks", 1);
                var originalNode = new BaseDataVariableState(null) { NodeId = nodeId };
                var replacementNode = new BaseDataVariableState(null) { NodeId = nodeId };
                var item = new Mock<IDataChangeMonitoredItem2>();
                item.SetupGet(value => value.Id).Returns(1);
                using var monitoredNode = new MonitoredNode2(
                    originalManager.Object,
                    server.Object,
                    originalNode);
                monitoredNode.Add(item.Object);

                ServiceResult result = monitoredNode.Rebind(
                    replacementManager.Object,
                    replacementNode);

                Assert.Multiple(() =>
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(originalNode.OnStateChangedAsync, Is.Null);
                    Assert.That(replacementNode.OnStateChangedAsync, Is.Not.Null);
                    Assert.That(monitoredNode.Node, Is.SameAs(replacementNode));
                    Assert.That(monitoredNode.NodeManager, Is.SameAs(replacementManager.Object));
                });

                monitoredNode.Remove(item.Object);
            }
        }

        [Test]
        public async Task MonitoredNodeDeletedMaskDoesNotClaimManagerDetachmentAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("DeletedMask", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    node.NodeId);
                using var monitoredNode = new MonitoredNode2(
                    nodeManager.Object,
                    server.Object,
                    node);
                monitoredNode.Add((IDataChangeMonitoredItem2)item);

                await monitoredNode.OnMonitoredNodeChangedAsync(
                    server.Object.DefaultSystemContext,
                    node,
                    NodeStateChangeMasks.Deleted).ConfigureAwait(false);

                var lifecycle = (IMonitoredItemLifecycle)item;
                Assert.Multiple(() =>
                {
                    Assert.That(lifecycle.IsDeleted, Is.True);
                    Assert.That(lifecycle.IsDetached, Is.False);
                });
                monitoredNode.Remove((IDataChangeMonitoredItem2)item);
            }
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task DeletedFallbackItemStillDetachesFromOwningManagerAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("DeletedFallback", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("DeletedFallback", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);

                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    new object(),
                    node.NodeId);
                var lifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, lifecycle);
                Assert.That(
                    (await owner.Lifecycle.AttachMonitoredItemAsync(item).ConfigureAwait(false))
                        .StatusCode,
                    Is.EqualTo(StatusCodes.Good));

                lifecycle.MarkNodeDeleted();
                ServiceResult detachResult = await owner.Lifecycle
                    .DetachMonitoredItemAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> remaining = await owner.Lifecycle
                    .GetMonitoredItemsSnapshotAsync()
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(detachResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(lifecycle.IsDeleted, Is.True);
                    Assert.That(lifecycle.IsDetached, Is.True);
                    Assert.That(remaining, Is.Empty);
                });
            }
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task EventAttachCallbackFailureRestoresDetachedStateAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseObjectState(null)
                {
                    NodeId = new NodeId("EventAttachFailure", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("EventAttachFailure", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);

                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    new object(),
                    node.NodeId);
                var lifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, lifecycle);
                bool throwOnAttach = true;
                owner.SetEventSubscriptionCallback(unsubscribe =>
                {
                    if (!unsubscribe && throwOnAttach)
                    {
                        throwOnAttach = false;
                        throw new InvalidOperationException("Injected attach callback failure.");
                    }
                });

                Assert.That(
                    async () => await owner.Lifecycle
                        .AttachMonitoredItemAsync(item)
                        .ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>());
                IReadOnlyList<IMonitoredItem> remaining = await owner.Lifecycle
                    .GetMonitoredItemsSnapshotAsync()
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(lifecycle.IsDetached, Is.True);
                    Assert.That(remaining, Is.Empty);
                    Assert.That(node.AreEventsMonitored, Is.False);
                    Assert.That(
                        item.NodeManager,
                        Is.SameAs(DetachedMonitoredItemOwnership.GetOwner(server.Object)));
                    Assert.That(
                        item.ManagerHandle,
                        Is.SameAs(DetachedMonitoredItemOwnership.Handle));
                });
            }
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task EventDetachCallbackFailureRestoresManagerOwnershipAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseObjectState(null)
                {
                    NodeId = new NodeId("EventDetachFailure", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("EventDetachFailure", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);

                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    new object(),
                    node.NodeId);
                var lifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, lifecycle);
                Assert.That(
                    (await owner.Lifecycle.AttachMonitoredItemAsync(item).ConfigureAwait(false))
                        .StatusCode,
                    Is.EqualTo(StatusCodes.Good));

                bool throwOnDetach = true;
                owner.SetEventSubscriptionCallback(unsubscribe =>
                {
                    if (unsubscribe && throwOnDetach)
                    {
                        throwOnDetach = false;
                        throw new InvalidOperationException("Injected detach callback failure.");
                    }
                });
                Assert.That(
                    async () => await owner.Lifecycle
                        .DetachMonitoredItemAsync(item)
                        .ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>());
                IReadOnlyList<IMonitoredItem> remaining = await owner.Lifecycle
                    .GetMonitoredItemsSnapshotAsync()
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(lifecycle.IsDetached, Is.False);
                    Assert.That(remaining, Has.Count.EqualTo(1));
                    Assert.That(remaining[0], Is.SameAs(item));
                    Assert.That(node.AreEventsMonitored, Is.True);
                });

                owner.SetEventSubscriptionCallback(static _ => { });
                await owner.Lifecycle.DetachMonitoredItemAsync(item).ConfigureAwait(false);
            }
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task NodeRemovalFailureAfterAddressSpaceRemovalKeepsItemDeletedAsync(
            ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("RemovalFailure", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("RemovalFailure", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);

                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    new object(),
                    node.NodeId);
                var lifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, lifecycle);
                Assert.That(
                    (await owner.Lifecycle.AttachMonitoredItemAsync(item).ConfigureAwait(false))
                        .StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                owner.SetNodeRemovedCallback(static _ =>
                    throw new InvalidOperationException("Injected removal callback failure."));

                Assert.That(
                    async () => await owner.DeleteNodeAsync(node.NodeId).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.EqualTo("Injected removal callback failure."));
                IReadOnlyList<IMonitoredItem> remaining = await owner.Lifecycle
                    .GetMonitoredItemsSnapshotAsync()
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(owner.ContainsNode(node.NodeId), Is.False);
                    Assert.That(lifecycle.IsDeleted, Is.True);
                    Assert.That(lifecycle.IsDetached, Is.True);
                    Assert.That(remaining, Is.Empty);
                });
            }
        }

        [Test]
        public async Task DurableStartupFallbackCanBeDeletedAndLaterRecoveredAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                bool isRunning = false;
                server.SetupGet(value => value.IsRunning).Returns(() => isRunning);
                var durableQueueFactory = new Mock<IMonitoredItemQueueFactory>();
                durableQueueFactory.SetupGet(value => value.SupportsDurableQueues).Returns(true);
                durableQueueFactory
                    .Setup(value => value.CreateDataChangeQueue(
                        It.IsAny<bool>(),
                        It.IsAny<uint>()))
                    .Returns<bool, uint>((_, id) =>
                        queueFactory.CreateDataChangeQueue(false, id));
                server.SetupGet(value => value.MonitoredItemQueueFactory)
                    .Returns(durableQueueFactory.Object);

                var configurationManager = new Mock<IConfigurationNodeManager>();
                var coreManager = new Mock<ICoreNodeManager>();
                var mainFactory = new Mock<IMainNodeManagerFactory>();
                mainFactory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(configurationManager.Object);
                mainFactory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(coreManager.Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(mainFactory.Object);

                ApplicationConfiguration configuration = CreateConfiguration();
                var recoverableManager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    configuration,
                    useSamplingGroups: false,
                    new Mock<ILogger>().Object,
                    DeterministicServerMock.TestNamespaceUri);
                using var master = new MasterNodeManager(
                    server.Object,
                    configuration,
                    null,
                    [recoverableManager],
                    null);
                server.SetupGet(value => value.NodeManager).Returns(master);

                var recoverNodeId = new NodeId("DurableRecover", recoverableManager.NamespaceIndex);
                var deleteNodeId = new NodeId("DurableDelete", recoverableManager.NamespaceIndex);
                var storedItems = new List<IStoredMonitoredItem>
                {
                    CreateStoredMonitoredItem(11, recoverNodeId),
                    CreateStoredMonitoredItem(12, deleteNodeId)
                };
                var restoredItems = new List<IMonitoredItem> { null, null };

                await master.RestoreMonitoredItemsAsync(
                    storedItems,
                    restoredItems,
                    new Mock<IUserIdentity>().Object,
                    CancellationToken.None).ConfigureAwait(false);
                using IMonitoredItem recoverableItem = restoredItems[0];
                using IMonitoredItem deletableItem = restoredItems[1];
                var recoverableLifecycle = (IMonitoredItemLifecycle)recoverableItem;
                var deletableLifecycle = (IMonitoredItemLifecycle)deletableItem;
                Assert.Multiple(() =>
                {
                    Assert.That(recoverableLifecycle.IsDetached, Is.True);
                    Assert.That(recoverableLifecycle.IsDeleted, Is.True);
                    Assert.That(
                        recoverableItem.NodeManager,
                        Is.SameAs(coreManager.Object));
                    Assert.That(
                        recoverableItem.ManagerHandle,
                        Is.SameAs(DetachedMonitoredItemOwnership.Handle));
                });

                var deleteErrors = new List<ServiceResult> { ServiceResult.Good };
                await master.DeleteMonitoredItemsAsync(
                    new OperationContext(deletableItem),
                    subscriptionId: 1,
                    [deletableItem],
                    deleteErrors,
                    CancellationToken.None).ConfigureAwait(false);

                var node = new BaseDataVariableState(null)
                {
                    NodeId = recoverNodeId,
                    BrowseName = new QualifiedName("DurableRecover", recoverableManager.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 42
                };
                node.CreateAsPredefinedNode(recoverableManager.SystemContext);
                await recoverableManager.AddPredefinedNodePublicAsync(
                    recoverableManager.SystemContext,
                    node).ConfigureAwait(false);
                isRunning = true;
                await master.RecoverMonitoredItemsAsync(
                    recoverableManager,
                    [recoverableItem],
                    CancellationToken.None).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(deletableLifecycle.IsDetached, Is.True);
                    Assert.That(deletableLifecycle.IsDeleted, Is.True);
                    Assert.That(deleteErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(recoverableLifecycle.IsDetached, Is.False);
                    Assert.That(recoverableLifecycle.IsDeleted, Is.False);
                    Assert.That(recoverableItem.NodeManager, Is.SameAs(recoverableManager));
                    Assert.That(
                        recoverableManager.MonitoredItems[recoverableItem.Id],
                        Is.SameAs(recoverableItem));
                });

                await ((INodeManagerMonitoredItemLifecycle)recoverableManager)
                    .DetachMonitoredItemAsync(recoverableItem)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RollbackDoesNotRestoreItemRemovedFromItsSubscriptionAsync()
        {
            bool subscriptionOwnsItem = true;
            var item = new Mock<IMonitoredItem>();
            var current = new TestNodeManagerLifecycle();

            var server = new Mock<IServerInternal>();
            var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                server.Object,
                current,
                replacement: null,
                compatibleItems: [],
                deletedItems: [item.Object],
                isOwnedBySubscription: _ => subscriptionOwnsItem);

            await transition.DetachCurrentAsync(CancellationToken.None).ConfigureAwait(false);
            subscriptionOwnsItem = false;
            await transition.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(current.RestoreCount, Is.Zero);
        }

        [Test]
        public async Task ReloadTransitionAttachesOnlyAfterTheCommitBoundaryAsync()
        {
            bool committed = false;
            var item = new Mock<IMonitoredItem>();
            var current = new TestNodeManagerLifecycle();
            var replacement = new TestNodeManagerLifecycle
            {
                AttachCallback = _ =>
                {
                    Assert.That(committed, Is.True);
                    return ServiceResult.Good;
                }
            };
            var server = new Mock<IServerInternal>();
            var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                server.Object,
                current,
                replacement,
                compatibleItems: [item.Object],
                deletedItems: [],
                isOwnedBySubscription: static _ => true);

            await transition.DetachCurrentAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(replacement.AttachCount, Is.Zero);

            committed = true;
            List<Exception> failures = await transition
                .AttachCompatibleAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(failures, Is.Empty);
            Assert.That(replacement.AttachCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PostCommitAttachFailureLeavesItemDetachedAndDeletedAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (MonitoredItem item = CreateMonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                new NodeId("PostCommitFailure", 1)))
            {
                var current = new TestNodeManagerLifecycle();
                var replacement = new TestNodeManagerLifecycle
                {
                    AttachCallback = _ =>
                        throw new InvalidOperationException("Injected attach failure.")
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    server.Object,
                    current,
                    replacement,
                    compatibleItems: [item],
                    deletedItems: [],
                    isOwnedBySubscription: static _ => true);

                await transition.DetachCurrentAsync(CancellationToken.None).ConfigureAwait(false);
                List<Exception> failures = await transition
                    .AttachCompatibleAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                transition.MarkDeletedItems();
                var itemLifecycle = (IMonitoredItemLifecycle)item;

                Assert.Multiple(() =>
                {
                    Assert.That(failures, Has.Count.EqualTo(1));
                    Assert.That(failures[0].Message, Is.EqualTo("Injected attach failure."));
                    Assert.That(itemLifecycle.IsDetached, Is.True);
                    Assert.That(itemLifecycle.IsDeleted, Is.True);
                });
            }
        }

        [Test]
        public async Task TransitionFailureVariantsReportEveryRollbackFailureAsync()
        {
            var firstItem = new Mock<IMonitoredItem>();
            var secondItem = new Mock<IMonitoredItem>();
            var current = new TestNodeManagerLifecycle
            {
                DetachCallback = item => ReferenceEquals(item, firstItem.Object)
                    ? new ServiceResult(StatusCodes.BadInvalidState)
                    : ServiceResult.Good
            };
            var server = new Mock<IServerInternal>();
            var failedDetachTransition = new NodeManagerLifecycle.MonitoredItemTransition(
                server.Object,
                current,
                replacement: null,
                compatibleItems: [firstItem.Object],
                deletedItems: [],
                isOwnedBySubscription: static _ => true);

            ServiceResultException detachException = Assert.ThrowsAsync<ServiceResultException>(
                async () => await failedDetachTransition
                    .DetachCurrentAsync(CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(detachException.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            current.DetachCallback = static _ => ServiceResult.Good;
            current.RestoreCallback = item => ReferenceEquals(item, firstItem.Object)
                ? new ServiceResult(StatusCodes.BadUnexpectedError)
                : throw new InvalidOperationException("Injected restore failure.");
            var replacement = new TestNodeManagerLifecycle
            {
                AttachCallback = static _ => ServiceResult.Good,
                DetachCallback = item => ReferenceEquals(item, firstItem.Object)
                    ? new ServiceResult(StatusCodes.BadInvalidState)
                    : throw new InvalidOperationException("Injected replacement detach failure.")
            };
            var rollbackTransition = new NodeManagerLifecycle.MonitoredItemTransition(
                server.Object,
                current,
                replacement,
                compatibleItems: [firstItem.Object, secondItem.Object],
                deletedItems: [],
                isOwnedBySubscription: static _ => true);

            await rollbackTransition.DetachCurrentAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                await rollbackTransition
                    .AttachCompatibleAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                Is.Empty);

            AggregateException rollbackException = Assert.ThrowsAsync<AggregateException>(
                async () => await rollbackTransition
                    .RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(rollbackException.InnerExceptions, Has.Count.EqualTo(4));
                Assert.That(
                    rollbackException.InnerExceptions,
                    Has.Exactly(2).TypeOf<ServiceResultException>());
                Assert.That(
                    rollbackException.InnerExceptions,
                    Has.Exactly(2).TypeOf<InvalidOperationException>());
            });
        }

        [TestCase(false, 0)]
        [TestCase(true, 1)]
        public async Task TransitionAttachFailureClassifiesExpectedStatusAsync(
            bool unexpected,
            int expectedFailureCount)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (MonitoredItem item = CreateMonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                new NodeId("AttachClassification", 1)))
            {
                var current = new TestNodeManagerLifecycle();
                var replacement = new TestNodeManagerLifecycle
                {
                    AttachCallback = _ => new ServiceResult(
                        unexpected
                            ? StatusCodes.BadUnexpectedError
                            : StatusCodes.BadNodeIdUnknown)
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    server.Object,
                    current,
                    replacement,
                    compatibleItems: [item],
                    deletedItems: [],
                    isOwnedBySubscription: static _ => true);

                await transition.DetachCurrentAsync(CancellationToken.None).ConfigureAwait(false);
                List<Exception> failures = await transition
                    .AttachCompatibleAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                transition.MarkDeletedItems();

                Assert.Multiple(() =>
                {
                    Assert.That(failures, Has.Count.EqualTo(expectedFailureCount));
                    Assert.That(((IMonitoredItemLifecycle)item).IsDetached, Is.True);
                    Assert.That(((IMonitoredItemLifecycle)item).IsDeleted, Is.True);
                });
            }
        }

        [Test]
        public async Task TransitionWithoutSubscriptionOwnershipSkipsRecoveryAsync()
        {
            var item = new Mock<IMonitoredItem>();
            var subscriptionManager = new Mock<ISubscriptionManager>();
            subscriptionManager.Setup(manager => manager.GetSubscriptions()).Returns([]);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.SubscriptionManager)
                .Returns(subscriptionManager.Object);
            var current = new TestNodeManagerLifecycle();
            var replacement = new TestNodeManagerLifecycle();
            var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                server.Object,
                current,
                replacement,
                compatibleItems: [item.Object],
                deletedItems: []);

            await transition.DetachCurrentAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                await transition
                    .AttachCompatibleAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                Is.Empty);
            await transition.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(replacement.AttachCount, Is.Zero);
                Assert.That(current.RestoreCount, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManagerLifecycleHandlesIdempotenceAndConflictingOwnership(bool useSamplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var samplingGroups = useSamplingGroups
                    ? new TrackingSamplingGroupManager(server.Object, nodeManager.Object)
                    : null;
                using IMonitoredItemManager manager = useSamplingGroups
                    ? new SamplingGroupMonitoredItemManager(
                        nodeManager.Object,
                        server.Object,
                        samplingGroups)
                    : new MonitoredNodeMonitoredItemManager(nodeManager.Object, server.Object);
                var lifecycle = (IMonitoredItemManagerLifecycle)manager;
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("Idempotent", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                var handle = new NodeHandle(node.NodeId, node);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    node.NodeId);
                using MonitoredItem conflictingItem = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    node.NodeId);
                var itemLifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, itemLifecycle);
                DetachedMonitoredItemOwnership.Detach(
                    server.Object,
                    (IMonitoredItemLifecycle)conflictingItem);
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(
                    new OperationContext(item));

                (ServiceResult firstAttach, bool firstChanged) = lifecycle.AttachMonitoredItem(
                    context,
                    handle,
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });
                (ServiceResult repeatedAttach, bool repeatedChanged) = lifecycle.AttachMonitoredItem(
                    context,
                    handle,
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });
                (ServiceResult conflictingAttach, bool conflictingChanged) =
                    lifecycle.AttachMonitoredItem(
                        context,
                        new NodeHandle(node.NodeId, node),
                        conflictingItem,
                        static (_, _, nodeToCache) => nodeToCache,
                        static (_, _) => { });

                DetachedMonitoredItemOwnership.Detach(server.Object, itemLifecycle);
                (ServiceResult detachedExistingAttach, bool detachedExistingChanged) =
                    lifecycle.AttachMonitoredItem(
                        context,
                        handle,
                        item,
                        static (_, _, nodeToCache) => nodeToCache,
                        static (_, _) => { });
                itemLifecycle.Rebind(nodeManager.Object, handle);

                (ServiceResult detach, bool detachChanged) = lifecycle.DetachMonitoredItem(
                    context,
                    item,
                    static (_, _) => { });
                (ServiceResult repeatedDetach, bool repeatedDetachChanged) =
                    lifecycle.DetachMonitoredItem(
                        context,
                        item,
                        static (_, _) => { });

                Assert.Multiple(() =>
                {
                    Assert.That(firstAttach.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(firstChanged, Is.True);
                    Assert.That(repeatedAttach.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(repeatedChanged, Is.False);
                    Assert.That(
                        conflictingAttach.StatusCode,
                        Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                    Assert.That(conflictingChanged, Is.False);
                    Assert.That(
                        detachedExistingAttach.StatusCode,
                        Is.EqualTo(StatusCodes.BadInvalidState));
                    Assert.That(detachedExistingChanged, Is.False);
                    Assert.That(detach.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(detachChanged, Is.True);
                    Assert.That(repeatedDetach.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(repeatedDetachChanged, Is.False);
                });
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManagerLifecycleRejectsUnsupportedItemsAndEmptySnapshots(bool useSamplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var samplingGroups = useSamplingGroups
                    ? new TrackingSamplingGroupManager(server.Object, nodeManager.Object)
                    : null;
                using IMonitoredItemManager manager = useSamplingGroups
                    ? new SamplingGroupMonitoredItemManager(
                        nodeManager.Object,
                        server.Object,
                        samplingGroups)
                    : new MonitoredNodeMonitoredItemManager(nodeManager.Object, server.Object);
                var lifecycle = (IMonitoredItemManagerLifecycle)manager;
                var unsupported = new Mock<ISampledDataChangeMonitoredItem>();
                unsupported.SetupGet(value => value.Id).Returns(55);
                unsupported.SetupGet(value => value.NodeId).Returns(new NodeId("Unsupported", 1));
                var node = new BaseDataVariableState(null)
                {
                    NodeId = unsupported.Object.NodeId
                };

                (ServiceResult detach, bool detachChanged) = lifecycle.DetachMonitoredItem(
                    server.Object.DefaultSystemContext,
                    unsupported.Object,
                    static (_, _) => { });
                (ServiceResult attach, bool attachChanged) = lifecycle.AttachMonitoredItem(
                    server.Object.DefaultSystemContext,
                    new NodeHandle(node.NodeId, node),
                    unsupported.Object,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });

                Assert.Multiple(() =>
                {
                    Assert.That(detach.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                    Assert.That(detachChanged, Is.False);
                    Assert.That(attach.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                    Assert.That(attachChanged, Is.False);
                    Assert.That(lifecycle.GetMonitoredItemsSnapshot([]), Is.Empty);
                });
            }
        }

        [Test]
        public void MonitoredNodeAttachCacheConflictCleansPartialOwnership()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var manager = new MonitoredNodeMonitoredItemManager(
                    nodeManager.Object,
                    server.Object);
                var lifecycle = (IMonitoredItemManagerLifecycle)manager;
                var cachedNode = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("CacheConflict", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                using MonitoredItem existingItem = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    cachedNode.NodeId,
                    id: 2);
                DetachedMonitoredItemOwnership.Detach(
                    server.Object,
                    (IMonitoredItemLifecycle)existingItem);
                var existingHandle = new NodeHandle(cachedNode.NodeId, cachedNode);
                Assert.That(
                    lifecycle.AttachMonitoredItem(
                        server.Object.DefaultSystemContext,
                        existingHandle,
                        existingItem,
                        static (_, _, nodeToCache) => nodeToCache,
                        static (_, _) => { }).Result.StatusCode,
                    Is.EqualTo(StatusCodes.Good));

                var freshNode = new BaseDataVariableState(null)
                {
                    NodeId = cachedNode.NodeId,
                    DataType = DataTypeIds.Int32,
                    Value = 2
                };
                using MonitoredItem conflictingItem = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    freshNode.NodeId,
                    id: 3);
                DetachedMonitoredItemOwnership.Detach(
                    server.Object,
                    (IMonitoredItemLifecycle)conflictingItem);
                var conflictingHandle = new NodeHandle(freshNode.NodeId, freshNode);
                int removeCacheCount = 0;

                (ServiceResult result, bool changed) = lifecycle.AttachMonitoredItem(
                    server.Object.DefaultSystemContext,
                    conflictingHandle,
                    conflictingItem,
                    (_, _, _) => cachedNode,
                    (_, _) => removeCacheCount++);

                Assert.Multiple(() =>
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                    Assert.That(changed, Is.False);
                    Assert.That(removeCacheCount, Is.EqualTo(1));
                    Assert.That(manager.MonitoredItems.ContainsKey(conflictingItem.Id), Is.False);
                    Assert.That(
                        lifecycle.GetMonitoredItemsSnapshot(null),
                        Is.EqualTo(new[] { existingItem }));
                });

                lifecycle.DetachMonitoredItem(
                    server.Object.DefaultSystemContext,
                    existingItem,
                    static (_, _) => { });
            }
        }

        [Test]
        public void SamplingGroupAttachApplyFailureStopsMonitoringAndRestoresDetachedOwnership()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var samplingGroups = new TrackingSamplingGroupManager(
                    server.Object,
                    nodeManager.Object)
                {
                    FailNextApply = true
                };
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object,
                    server.Object,
                    samplingGroups);
                var lifecycle = (IMonitoredItemManagerLifecycle)manager;
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("ApplyFailure", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    node.NodeId);
                var itemLifecycle = (IMonitoredItemLifecycle)item;
                DetachedMonitoredItemOwnership.Detach(server.Object, itemLifecycle);

                Assert.That(
                    () => lifecycle.AttachMonitoredItem(
                        server.Object.DefaultSystemContext,
                        new NodeHandle(node.NodeId, node),
                        item,
                        static (_, _, nodeToCache) => nodeToCache,
                        static (_, _) => { }),
                    Throws.InvalidOperationException.With.Message.EqualTo(
                        "Injected apply failure."));

                Assert.Multiple(() =>
                {
                    Assert.That(samplingGroups.StartCount, Is.EqualTo(1));
                    Assert.That(samplingGroups.StopCount, Is.EqualTo(1));
                    Assert.That(samplingGroups.ApplyCount, Is.EqualTo(2));
                    Assert.That(manager.MonitoredItems, Is.Empty);
                    Assert.That(itemLifecycle.IsDetached, Is.True);
                    Assert.That(
                        item.NodeManager,
                        Is.SameAs(DetachedMonitoredItemOwnership.GetOwner(server.Object)));
                });
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoreMonitoredItemMarksFoundNodeAsAttached(bool useSamplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using MonitoredItem source = CreateMonitoredItem(
                    server.Object,
                    nodeManager.Object,
                    new object(),
                    new NodeId("Restored", 1));
                ((IMonitoredItemLifecycle)source).MarkNodeDeleted();
                IStoredMonitoredItem stored = source.ToStorableMonitoredItem();
                var node = new BaseDataVariableState(null)
                {
                    NodeId = stored.NodeId,
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                var handle = new NodeHandle(node.NodeId, node);
                IMonitoredItemManager manager;
                TrackingSamplingGroupManager samplingGroups = null;
                if (useSamplingGroups)
                {
                    samplingGroups = new TrackingSamplingGroupManager(
                        server.Object,
                        nodeManager.Object);
                    manager = new SamplingGroupMonitoredItemManager(
                        nodeManager.Object,
                        server.Object,
                        samplingGroups);
                }
                else
                {
                    manager = new MonitoredNodeMonitoredItemManager(
                        nodeManager.Object,
                        server.Object);
                }

                using (manager)
                {
                    bool restored = manager.RestoreMonitoredItem(
                        server.Object,
                        nodeManager.Object,
                        server.Object.DefaultSystemContext,
                        handle,
                        stored,
                        new Mock<IUserIdentity>().Object,
                        static (_, _, nodeToCache) => nodeToCache,
                        out ISampledDataChangeMonitoredItem restoredItem);
                    using ISampledDataChangeMonitoredItem item = restoredItem;

                    Assert.Multiple(() =>
                    {
                        Assert.That(restored, Is.True);
                        Assert.That(((IMonitoredItemLifecycle)item).IsDetached, Is.False);
                        Assert.That(item.NodeManager, Is.SameAs(nodeManager.Object));
                        Assert.That(item.ManagerHandle, Is.SameAs(handle));
                        Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                    });

                    ((IMonitoredItemManagerLifecycle)manager).DetachMonitoredItem(
                        server.Object.DefaultSystemContext,
                        item,
                        static (_, _) => { });
                }

                samplingGroups?.Dispose();
            }
        }

        private static ManagerOwner CreateManager(IServerInternal server, ManagerPath path)
        {
            ApplicationConfiguration configuration = CreateConfiguration();
            var logger = new Mock<ILogger>();
            bool sampling = path is ManagerPath.AsyncSamplingGroup or
                ManagerPath.SyncAdapterSamplingGroup;
            if (path is ManagerPath.AsyncMonitoredNode or ManagerPath.AsyncSamplingGroup)
            {
                var manager = new TestableAsyncCustomNodeManager(
                    server,
                    configuration,
                    sampling,
                    logger.Object,
                    DeterministicServerMock.TestNamespaceUri);
                return new ManagerOwner(manager);
            }

            var syncManager = new TestableCustomNodeManager2(
                server,
                configuration,
                sampling,
                logger.Object,
                DeterministicServerMock.TestNamespaceUri);
            return new ManagerOwner(syncManager);
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200,
                    AvailableSamplingRates = []
                }
            };
        }

        private static StoredMonitoredItem CreateStoredMonitoredItem(uint id, NodeId nodeId)
        {
            return new StoredMonitoredItem
            {
                SubscriptionId = 1,
                Id = id,
                TypeMask = MonitoredItemTypeMask.DataChange,
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                DiagnosticsMasks = DiagnosticsMasks.None,
                TimestampsToReturn = TimestampsToReturn.Both,
                ClientHandle = id,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = 1000,
                QueueSize = 1,
                DiscardOldest = true,
                SourceSamplingInterval = 1000,
                IsDurable = true,
                LastValue = new DataValue(new Variant(1), StatusCodes.Good),
                LastError = ServiceResult.Good
            };
        }

        private static MonitoredItem CreateMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            NodeId nodeId,
            uint id = 2)
        {
            var identity = new Mock<IUserIdentity>();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Identity).Returns(identity.Object);
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Session).Returns(session.Object);
            subscription.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);

            return new MonitoredItem(
                server,
                nodeManager,
                managerHandle,
                subscriptionId: 1,
                id,
                itemToMonitor: new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode: MonitoringMode.Reporting,
                clientHandle: 3,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 1000,
                queueSize: 10,
                discardOldest: true,
                sourceSamplingInterval: 1000)
            {
                SubscriptionCallback = subscription.Object
            };
        }

        private static MonitoredItem CreateEventMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            NodeId nodeId)
        {
            var identity = new Mock<IUserIdentity>();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Identity).Returns(identity.Object);
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Session).Returns(session.Object);
            subscription.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            var filter = new EventFilter();

            return new MonitoredItem(
                server,
                nodeManager,
                managerHandle,
                subscriptionId: 1,
                id: 2,
                itemToMonitor: new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.EventNotifier
                },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode: MonitoringMode.Reporting,
                clientHandle: 3,
                originalFilter: filter,
                filterToUse: filter,
                range: null,
                samplingInterval: 0,
                queueSize: 10,
                discardOldest: true,
                sourceSamplingInterval: 0)
            {
                SubscriptionCallback = subscription.Object
            };
        }

        private sealed class TestNodeManagerLifecycle : INodeManagerMonitoredItemLifecycle
        {
            public Func<IMonitoredItem, ServiceResult> AttachCallback { get; set; } =
                static _ => ServiceResult.Good;

            public Func<IMonitoredItem, ServiceResult> DetachCallback { get; set; } =
                static _ => ServiceResult.Good;

            public Func<IMonitoredItem, ServiceResult> RestoreCallback { get; set; } =
                static _ => ServiceResult.Good;

            public int AttachCount { get; private set; }

            public int RestoreCount { get; private set; }

            public ValueTask<IReadOnlyList<IMonitoredItem>> GetMonitoredItemsSnapshotAsync(
                IReadOnlyCollection<NodeId> nodeIds = null,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IReadOnlyList<IMonitoredItem>>([]);
            }

            public ValueTask<ServiceResult> ValidateMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> DetachMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(DetachCallback(monitoredItem));
            }

            public ValueTask<ServiceResult> AttachMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                AttachCount++;
                return new ValueTask<ServiceResult>(AttachCallback(monitoredItem));
            }

            public ValueTask<ServiceResult> RestoreMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                RestoreCount++;
                return new ValueTask<ServiceResult>(RestoreCallback(monitoredItem));
            }
        }

        private sealed class ManagerOwner : IDisposable
        {
            private readonly TestableAsyncCustomNodeManager m_asyncManager;
            private readonly TestableCustomNodeManager2 m_syncManager;

            public ManagerOwner(TestableAsyncCustomNodeManager manager)
            {
                m_asyncManager = manager;
                Lifecycle = manager;
                NamespaceIndex = manager.NamespaceIndex;
                SystemContext = manager.SystemContext;
            }

            public ManagerOwner(TestableCustomNodeManager2 manager)
            {
                m_syncManager = manager;
                IAsyncNodeManager adapter = manager.ToAsyncNodeManager();
                Lifecycle = (INodeManagerMonitoredItemLifecycle)adapter;
                NamespaceIndex = manager.NamespaceIndex;
                SystemContext = manager.SystemContext;
            }

            public INodeManagerMonitoredItemLifecycle Lifecycle { get; }

            public ushort NamespaceIndex { get; }

            public ServerSystemContext SystemContext { get; }

            public ValueTask AddNodeAsync(NodeState node)
            {
                if (m_asyncManager != null)
                {
                    return m_asyncManager.AddPredefinedNodePublicAsync(SystemContext, node);
                }

                m_syncManager!.AddPredefinedNodePublic(SystemContext, node);
                return default;
            }

            public bool Owns(IAsyncNodeManager nodeManager)
            {
                if (m_asyncManager != null)
                {
                    return ReferenceEquals(nodeManager, m_asyncManager);
                }

                return nodeManager is AsyncNodeManagerAdapter adapter &&
                    ReferenceEquals(adapter.SyncNodeManager, m_syncManager);
            }

            public void SetEventSubscriptionCallback(Action<bool> callback)
            {
                if (m_asyncManager != null)
                {
                    m_asyncManager.EventSubscriptionCallback = (unsubscribe, _) =>
                    {
                        callback(unsubscribe);
                        return default;
                    };
                    return;
                }

                m_syncManager!.EventSubscriptionCallback = callback;
            }

            public void SetNodeRemovedCallback(Action<NodeState> callback)
            {
                if (m_asyncManager != null)
                {
                    m_asyncManager.NodeRemovedCallback = (node, _) =>
                    {
                        callback(node);
                        return default;
                    };
                    return;
                }

                m_syncManager!.NodeRemovedCallback = callback;
            }

            public ValueTask<bool> DeleteNodeAsync(NodeId nodeId)
            {
                if (m_asyncManager != null)
                {
                    return m_asyncManager.DeleteNodeAsync(SystemContext, nodeId);
                }

                return new ValueTask<bool>(m_syncManager!.DeleteNode(SystemContext, nodeId));
            }

            public bool ContainsNode(NodeId nodeId)
            {
                return m_asyncManager?.PredefinedNodes.ContainsKey(nodeId) ??
                    m_syncManager!.PredefinedNodes.ContainsKey(nodeId);
            }

            public void Dispose()
            {
                m_asyncManager?.Dispose();
                m_syncManager?.Dispose();
            }
        }

        private sealed class TrackingSamplingGroupManager : SamplingGroupManager
        {
            public TrackingSamplingGroupManager(
                IServerInternal server,
                IAsyncNodeManager nodeManager)
                : base(server, nodeManager, 100, 200, [])
            {
            }

            public int StartCount { get; private set; }

            public int StopCount { get; private set; }

            public int ApplyCount { get; private set; }

            public bool FailNextApply { get; set; }

            public override void StartMonitoring(
                OperationContext context,
                ISampledDataChangeMonitoredItem monitoredItem,
                IUserIdentity savedOwnerIdentity = null)
            {
                StartCount++;
            }

            public override void StopMonitoring(ISampledDataChangeMonitoredItem monitoredItem)
            {
                StopCount++;
            }

            public override void ApplyChanges()
            {
                ApplyCount++;
                if (FailNextApply)
                {
                    FailNextApply = false;
                    throw new InvalidOperationException("Injected apply failure.");
                }
            }
        }
    }
}
