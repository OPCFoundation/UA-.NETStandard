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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises the host's recovery selector with real restored subscription items.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class OwnerlessDurableRecoveryTests
    {
        /// <summary>
        /// A manager discovered after restoration recovers an item without replacing its queue or subscription.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OwnerlessRestoredItemRecoversThroughHostWithoutLosingQueueAsync(bool filterNode)
        {
            Mock<IServerInternal> server = CreateServer(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using MonitoredItem item = Restore(server.Object, queues);
                ISubscription owner = Claim(item);
                var binding = (IMonitoredItemGenerationBinding)item;
                long revision = binding.SourceRevision;
                Assert.That(item.NodeManager, Is.Null);
                Assert.That(binding.IsSourceUnavailable, Is.True);
                Assert.That(item.IsDurable, Is.True);
                Assert.That(item.ItemsInQueue, Is.EqualTo(1));

                Mock<IAsyncNodeManager> manager = CreateManager(item.NodeId, out var source);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [manager.Object]);
                IReadOnlyCollection<NodeId>? nodes = filterNode ? new[] { item.NodeId } : null;

                await ((IDynamicNodeManagerHost)master)
                    .ResolveUnresolvedSourcesAsync(manager.Object, nodes, CancellationToken.None)
                    .ConfigureAwait(false);
                await ((IDynamicNodeManagerHost)master)
                    .ResolveUnresolvedSourcesAsync(manager.Object, nodes, CancellationToken.None)
                    .ConfigureAwait(false);

                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
                Assert.That(item.NodeManager, Is.SameAs(manager.Object));
                Assert.That(binding.IsSourceUnavailable, Is.False);
                Assert.That(binding.SourceRevision, Is.EqualTo(revision + 1));
                Assert.That(item.SubscriptionCallback, Is.SameAs(owner));
                Assert.That(item.ToStorableMonitoredItem().IsDurable, Is.True);
                Assert.That(generation.MonitoredItems.TryGetValue(item, out ISubscription? registered), Is.True);
                Assert.That(registered, Is.SameAs(owner));
                Assert.That(item.ItemsInQueue, Is.EqualTo(1));
                source.Verify(value => value.AdoptSourceAsync(
                    item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()), Times.Once);

                using var context = new OperationContext(item);
                var notifications = new Queue<MonitoredItemNotification>();
                item.Publish(
                    context, notifications, new Queue<DiagnosticInfo>(), 10,
                    server.Object.Telemetry.CreateLogger<OwnerlessDurableRecoveryTests>());
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(notifications.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(notifications.Peek().Value.WrappedValue.TryGetValue(out int restoredValue), Is.True);
                Assert.That(restoredValue, Is.EqualTo(7));
            }
        }

        /// <summary>
        /// Empty or unrelated node notifications do not consume the later matching recovery.
        /// </summary>
        [Test]
        public async Task OwnerlessRecoveryHonorsNodeFilterBeforeMatchingNotificationAsync()
        {
            Mock<IServerInternal> server = CreateServer(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using MonitoredItem item = Restore(server.Object, queues);
                Claim(item);
                var binding = (IMonitoredItemGenerationBinding)item;
                long revision = binding.SourceRevision;
                Mock<IAsyncNodeManager> manager = CreateManager(item.NodeId, out var source);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [manager.Object]);
                var host = (IDynamicNodeManagerHost)master;

                await host.ResolveUnresolvedSourcesAsync(manager.Object, [], CancellationToken.None)
                    .ConfigureAwait(false);
                await host.ResolveUnresolvedSourcesAsync(
                    manager.Object, new[] { new NodeId("Unrelated", 1) }, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(item.NodeManager, Is.Null);
                Assert.That(binding.SourceRevision, Is.EqualTo(revision));
                source.Verify(value => value.AdoptSourceAsync(
                    It.IsAny<IMonitoredItem>(), It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()),
                    Times.Never);

                await host.ResolveUnresolvedSourcesAsync(
                    manager.Object, new[] { item.NodeId }, CancellationToken.None).ConfigureAwait(false);

                Assert.That(item.NodeManager, Is.SameAs(manager.Object));
                source.Verify(value => value.AdoptSourceAsync(
                    item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Recovery cannot reclaim a private, removed, disposed or terminal restored item.
        /// </summary>
        [TestCase("private")]
        [TestCase("removed")]
        [TestCase("disposed")]
        [TestCase("terminal")]
        public async Task OwnerlessRecoveryRejectsItemsWithoutRecoverableSubscriptionOwnershipAsync(string state)
        {
            Mock<IServerInternal> server = CreateServer(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using MonitoredItem item = Restore(server.Object, queues);
                var binding = (IMonitoredItemGenerationBinding)item;
                if (state != "private")
                {
                    ISubscription owner = Claim(item);
                    switch (state)
                    {
                        case "removed":
                            binding.UnbindSubscription(owner);
                            break;
                        case "disposed":
                            item.Dispose();
                            break;
                        case "terminal":
                            binding.Invalidate(terminal: true);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(state));
                    }
                }
                long revision = binding.SourceRevision;
                Mock<IAsyncNodeManager> manager = CreateManager(item.NodeId, out var source);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [manager.Object]);

                await ((IDynamicNodeManagerHost)master)
                    .ResolveUnresolvedSourcesAsync(manager.Object, null, CancellationToken.None).ConfigureAwait(false);

                Assert.That(item.NodeManager, Is.Null);
                Assert.That(binding.SourceRevision, Is.EqualTo(revision));
                source.Verify(value => value.AdoptSourceAsync(
                    It.IsAny<IMonitoredItem>(), It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        /// <summary>
        /// Equal NodeIds in separate server instances cannot share ownerless recovery candidates.
        /// </summary>
        [Test]
        public async Task OwnerlessRecoveryDoesNotCrossServerBoundaryAsync()
        {
            Mock<IServerInternal> first = CreateServer(out MonitoredItemQueueFactory firstQueues);
            Mock<IServerInternal> second = CreateServer(out MonitoredItemQueueFactory secondQueues);
            using (firstQueues)
            using (secondQueues)
            {
                using MonitoredItem item = Restore(first.Object, firstQueues);
                Claim(item);
                Mock<IAsyncNodeManager> foreign = CreateManager(item.NodeId, out var foreignSource);
                using MasterNodeManager foreignMaster = DeterministicServerMock.CreateMasterNodeManager(
                    second, [foreign.Object]);

                await ((IDynamicNodeManagerHost)foreignMaster)
                    .ResolveUnresolvedSourcesAsync(foreign.Object, null, CancellationToken.None).ConfigureAwait(false);

                Assert.That(item.NodeManager, Is.Null);
                foreignSource.Verify(value => value.AdoptSourceAsync(
                    It.IsAny<IMonitoredItem>(), It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()),
                    Times.Never);
                Mock<IAsyncNodeManager> local = CreateManager(item.NodeId, out var localSource);
                using MasterNodeManager localMaster = DeterministicServerMock.CreateMasterNodeManager(
                    first, [local.Object]);

                await ((IDynamicNodeManagerHost)localMaster)
                    .ResolveUnresolvedSourcesAsync(local.Object, null, CancellationToken.None).ConfigureAwait(false);

                Assert.That(item.NodeManager, Is.SameAs(local.Object));
                localSource.Verify(value => value.AdoptSourceAsync(
                    item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// A manager cannot use ownerless recovery to steal an unresolved item owned by another real slot.
        /// </summary>
        [Test]
        public async Task OwnerlessRecoveryDoesNotStealAnotherManagersStableSlotAsync()
        {
            Mock<IServerInternal> server = CreateServer(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var original = new Mock<IAsyncNodeManager>();
                original.SetupGet(value => value.NamespaceUris).Returns([DeterministicServerMock.TestNamespaceUri]);
                StoredMonitoredItem stored = CreateStored(queues);
                using var item = new MonitoredItem(server.Object, original.Object, new object(), stored);
                ISubscription owner = Claim(item);
                NodeManagerSlot slot = NodeManagerGenerationRegistry.GetOrCreate(original.Object).Slot;
                Mock<IAsyncNodeManager> replacement = CreateManager(item.NodeId, out var source);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [original.Object, replacement.Object]);
                Assert.That(slot.UnresolvedItems.TryGetValue(item, out ISubscription? registered), Is.True);
                Assert.That(registered, Is.SameAs(owner));

                await ((IDynamicNodeManagerHost)master)
                    .ResolveUnresolvedSourcesAsync(replacement.Object, null, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(item.NodeManager, Is.Null);
                Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);
                source.Verify(value => value.AdoptSourceAsync(
                    It.IsAny<IMonitoredItem>(), It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        /// <summary>
        /// Creates a server with a real queue factory and a borrowed subscription store.
        /// </summary>
        private static Mock<IServerInternal> CreateServer(out MonitoredItemQueueFactory queues)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out queues);
            server.SetupGet(value => value.SubscriptionStore).Returns(Mock.Of<ISubscriptionStore>());
            return server;
        }

        /// <summary>
        /// A failed stale recovery cannot invalidate or detach the source installed by a competing manager.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompetingOwnerlessRecoveryPreservesWinningSourceAfterStaleFailureAsync(bool throwFailure)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Mock<IServerInternal> server = CreateServer(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using MonitoredItem item = Restore(server.Object, queues);
                ISubscription owner = Claim(item);
                var binding = (IMonitoredItemGenerationBinding)item;
                long originalRevision = binding.SourceRevision;
                Mock<IAsyncNodeManager> stale = CreateManager(item.NodeId, out var staleSource);
                Mock<IAsyncNodeManager> winner = CreateManager(item.NodeId, out var winningSource);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [stale.Object, winner.Object]);
                var host = (IDynamicNodeManagerHost)master;
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var failure = new InvalidOperationException("The stale native source adoption failed.");
                var staleHandle = new NodeHandle(item.NodeId, new BaseDataVariableState(null)
                {
                    NodeId = item.NodeId,
                    DataType = DataTypeIds.Int32,
                    Value = 7
                });
                long capturedRevision = -1;
                bool staleBound = false;
                staleSource.Setup(value => value.AdoptSourceAsync(
                        item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()))
                    .Returns(async (IMonitoredItem _, MonitoredItemAdoption adoption, CancellationToken token) =>
                    {
                        capturedRevision = adoption.SourceRevision;
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        staleBound = binding.TryBindSource(
                            adoption.SourceRevision, stale.Object, staleHandle, out long revision);
                        if (staleBound)
                        {
                            adoption.SourceRevision = revision;
                        }
                        if (throwFailure)
                        {
                            throw failure;
                        }
                        return new ServiceResult(StatusCodes.BadNodeIdUnknown);
                    });
                Task? recovery = null;
                bool recoveryObserved = false;
                try
                {
                    Task activeRecovery = host.ResolveUnresolvedSourcesAsync(
                        stale.Object, null, timeout.Token).AsTask();
                    recovery = activeRecovery;
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await host.ResolveUnresolvedSourcesAsync(winner.Object, null, timeout.Token).ConfigureAwait(false);
                    long winningRevision = binding.SourceRevision;
                    Assert.That(item.NodeManager, Is.SameAs(winner.Object));
                    Assert.That(winningRevision, Is.EqualTo(originalRevision + 1));

                    release.TrySetResult(true);
                    if (throwFailure)
                    {
                        InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                            () => activeRecovery.WaitAsync(timeout.Token)).ConfigureAwait(false);
                        recoveryObserved = true;
                        Assert.That(observed, Is.SameAs(failure));
                    }
                    else
                    {
                        await activeRecovery.WaitAsync(timeout.Token).ConfigureAwait(false);
                        recoveryObserved = true;
                    }

                    Assert.That(capturedRevision, Is.EqualTo(originalRevision));
                    Assert.That(staleBound, Is.False);
                    Assert.That(item.NodeManager, Is.SameAs(winner.Object));
                    Assert.That(binding.SourceRevision, Is.EqualTo(winningRevision));
                    Assert.That(binding.IsSourceUnavailable, Is.False);
                    Assert.That(binding.IsDeleted, Is.False);
                    Assert.That(binding.IsTerminal, Is.False);
                    Assert.That(item.SubscriptionCallback, Is.SameAs(owner));
                    Assert.That(item.ItemsInQueue, Is.EqualTo(1));
                    NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(winner.Object);
                    Assert.That(generation.MonitoredItems.TryGetValue(item, out ISubscription? enrolled), Is.True);
                    Assert.That(enrolled, Is.SameAs(owner));
                    staleSource.Verify(value => value.ReleaseSourceAsync(
                        item, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
                    winningSource.Verify(value => value.ReleaseSourceAsync(
                        item, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
                }
                finally
                {
                    release.TrySetResult(true);
                    if (recovery is not null && !recoveryObserved)
                    {
                        try
                        {
                            await recovery.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        }
                        catch (InvalidOperationException observed) when (ReferenceEquals(observed, failure))
                        {
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Restores an item before any source manager is registered.
        /// </summary>
        private static MonitoredItem Restore(IServerInternal server, MonitoredItemQueueFactory queues)
        {
            return new MonitoredItem(server, CreateStored(queues));
        }

        /// <summary>
        /// Creates persisted settings with a real, prehydrated data-change queue.
        /// </summary>
        private static StoredMonitoredItem CreateStored(MonitoredItemQueueFactory queues)
        {
            IDataChangeMonitoredItemQueue queue = queues.CreateDataChangeQueue(false, 41);
            queue.ResetQueue(10, false);
            queue.Enqueue(new DataValue(Variant.From(7), StatusCodes.Good), ServiceResult.Good);
            return new StoredMonitoredItem
            {
                Id = 41,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId("RestoredOwnerless", 1),
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(Variant.From(7), StatusCodes.Good),
                IsDurable = true,
                IsDetached = true,
                RestoredDataChangeQueue = queue
            };
        }

        /// <summary>
        /// Enrolls the real item in subscription ownership without manufacturing a source binding.
        /// </summary>
        private static ISubscription Claim(MonitoredItem item)
        {
            var subscription = new Mock<ISubscription>().Object;
            item.SubscriptionCallback = subscription;
            ((IMonitoredItemGenerationBinding)item).BindSubscription(subscription);
            return subscription;
        }

        /// <summary>
        /// Supplies a native adoption seam that uses the item's real source-revision guard.
        /// </summary>
        private static Mock<IAsyncNodeManager> CreateManager(
            NodeId nodeId,
            out Mock<INodeManagerMonitoredItemSource> source)
        {
            var manager = new Mock<IAsyncNodeManager>();
            source = manager.As<INodeManagerMonitoredItemSource>();
            manager.SetupGet(value => value.NamespaceUris).Returns([DeterministicServerMock.TestNamespaceUri]);
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                DataType = DataTypeIds.Int32,
                Value = 7
            };
            var handle = new NodeHandle(nodeId, node);
            manager.Setup(value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(handle);
            source.Setup(value => value.ValidateSourceAsync(
                    It.IsAny<IMonitoredItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            source.Setup(value => value.AdoptSourceAsync(
                    It.IsAny<IMonitoredItem>(), It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()))
                .Returns((IMonitoredItem item, MonitoredItemAdoption adoption, CancellationToken _) =>
                {
                    Assert.That(item.NodeId, Is.EqualTo(nodeId));
                    var binding = (IMonitoredItemGenerationBinding)item;
                    Assert.That(binding.TryBindSource(
                        adoption.SourceRevision, manager.Object, handle, out long revision), Is.True);
                    adoption.SourceRevision = revision;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            return manager;
        }
    }
}
