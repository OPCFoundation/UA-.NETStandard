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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
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

        /// <summary>
        /// A lost item belongs only to its stable slot, never an artificial Core binding.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnresolvedItemDropsManagerBindingWithoutCoreParking(bool hasCore)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                if (hasCore)
                {
                    server.SetupGet(value => value.CoreNodeManager).Returns(new Mock<ICoreNodeManager>().Object);
                }
                else
                {
                    server.SetupGet(value => value.CoreNodeManager).Returns(() => null!);
                }
                var manager = new Mock<IAsyncNodeManager>();
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, manager.Object, new NodeHandle(), new NodeId("UnresolvedOwnership", 1));
                var subscription = new Mock<ISubscription>();
                item.SubscriptionCallback = subscription.Object;
                ((IMonitoredItemGenerationBinding)item).BindSubscription(subscription.Object);
                NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
                Assert.That(original.MonitoredItems.ContainsKey(item), Is.True);

                ((IMonitoredItemGenerationBinding)item).ReleaseSource();
                ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);

                Assert.Multiple(() =>
                {
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(item.ManagerHandle, Is.Null);
                    Assert.That(item.Generation, Is.Null);
                    Assert.That(original.MonitoredItems.ContainsKey(item), Is.False);
                    Assert.That(original.Slot.UnresolvedItems.ContainsKey(item), Is.True);
                });
                server.VerifyGet(value => value.CoreNodeManager, Times.Never);
            }
        }

        /// <summary>
        /// Unresolved modification uses server limits and retained metadata without a concrete Core manager.
        /// </summary>
        [TestCase("none", false)]
        [TestCase("none", true)]
        [TestCase("absolute", false)]
        [TestCase("percent", false)]
        [TestCase("missing-range", false)]
        [TestCase("invalid-type", false)]
        [TestCase("no-metadata", false)]
        [TestCase("none-no-metadata", false)]
        [TestCase("aggregate", false)]
        [TestCase("unsupported-aggregate", false)]
        [TestCase("aggregate-no-metadata", false)]
        [TestCase("cancelled", false)]
        public async Task UnresolvedModificationUsesServerPolicyWithoutConcreteCoreAsync(string filter, bool durable)
        {
            var clock = new FakeTimeProvider();
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues, clock);
            using (queues)
            using (var aggregates = new AggregateManager(server.Object))
            {
                server.SetupGet(value => value.AggregateManager).Returns(aggregates);
                if (filter == "aggregate")
                {
                    server.SetupGet(value => value.DiagnosticsNodeManager)
                        .Returns(new Mock<IDiagnosticsNodeManager>().Object);
                    var calculator = new Mock<IAggregateCalculator>();
                    await aggregates.RegisterFactoryAsync(
                        ObjectIds.AggregateFunction_Average, "Average",
                        (_, _, _, _, _, _, _) => calculator.Object).ConfigureAwait(false);
                }
                if (durable)
                {
                    var durableQueues = new Mock<IMonitoredItemQueueFactory>();
                    durableQueues.SetupGet(value => value.SupportsDurableQueues).Returns(true);
                    durableQueues.Setup(value => value.CreateDataChangeQueue(It.IsAny<bool>(), It.IsAny<uint>()))
                        .Returns<bool, uint>((_, id) => queues.CreateDataChangeQueue(false, id));
                    server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(durableQueues.Object);
                }
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration
                    {
                        ServerConfiguration = new ServerConfiguration
                        {
                            MinSupportedSamplingInterval = 125,
                            MaxNotificationQueueSize = 4,
                            MaxDurableNotificationQueueSize = 8
                        }
                    },
                    null,
                    Array.Empty<IAsyncNodeManager>(),
                    null);
                server.SetupGet(value => value.NodeManager).Returns(master);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("LostSource", 1),
                    DataType = filter == "invalid-type" ? DataTypeIds.String : DataTypeIds.Number,
                    MinimumSamplingInterval = 500
                };
                if (filter == "percent")
                {
                    node.AddChild(new PropertyState(node)
                    {
                        BrowseName = QualifiedName.From(BrowseNames.EURange),
                        Value = new Variant(new ExtensionObject(new Range { Low = 0, High = 100 }))
                    });
                }
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    filter.EndsWith("no-metadata", StringComparison.Ordinal)
                        ? new NodeHandle() : new NodeHandle(node.NodeId, node),
                    node.NodeId,
                    durable: durable);
                ((IMonitoredItemGenerationBinding)item).ReleaseSource();
                ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);
                var request = new MonitoredItemModifyRequest
                {
                    MonitoredItemId = item.Id,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 43,
                        SamplingInterval = 0,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = filter is "none" or "none-no-metadata" ? ExtensionObject.Null :
                            filter.Contains("aggregate", StringComparison.Ordinal) ? new ExtensionObject(
                                new AggregateFilter
                                {
                                    AggregateType = ObjectIds.AggregateFunction_Average,
                                    ProcessingInterval = 1,
                                    AggregateConfiguration = new AggregateConfiguration
                                    {
                                        UseServerCapabilitiesDefaults = true
                                    }
                                }) :
                            new ExtensionObject(new DataChangeFilter
                            {
                                DeadbandType = filter is "percent" or "missing-range"
                                    ? (uint)DeadbandType.Percent : (uint)DeadbandType.Absolute,
                                DeadbandValue = 10
                            })
                    }
                };
                var errors = new List<ServiceResult> { ServiceResult.Good };
                var results = new List<MonitoringFilterResult> { null! };

                using var cancellation = new CancellationTokenSource();
                if (filter == "cancelled")
                {
                    cancellation.Cancel();
                    await Assert.ThrowsAsync<OperationCanceledException>(
                        async () => await ModifyAsync().ConfigureAwait(false));
                    Assert.That(item.ClientHandle, Is.EqualTo(3U));
                    Assert.That(item.NodeManager, Is.Null);
                    server.VerifyGet(value => value.CoreNodeManager, Times.Never);
                    return;
                }
                await ModifyAsync().ConfigureAwait(false);

                ValueTask ModifyAsync() => master.ModifyMonitoredItemsAsync(
                    new OperationContext(new RequestHeader(), null, RequestType.ModifyMonitoredItems,
                        RequestLifetime.None),
                    TimestampsToReturn.Both,
                    [item],
                    [request],
                    errors, results, cancellation.Token);

                StatusCode expected = filter switch
                {
                    "missing-range" => StatusCodes.BadDeadbandFilterInvalid,
                    "invalid-type" => StatusCodes.BadFilterNotAllowed,
                    "no-metadata" => StatusCodes.BadMonitoredItemFilterUnsupported,
                    "aggregate-no-metadata" => StatusCodes.BadMonitoredItemFilterUnsupported,
                    "unsupported-aggregate" => StatusCodes.BadAggregateNotSupported,
                    _ => StatusCodes.Good
                };
                Assert.Multiple(() =>
                {
                    Assert.That(errors[0]?.StatusCode ?? StatusCodes.Good, Is.EqualTo(expected));
                    Assert.That(request.Processed, Is.True);
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(item.ManagerHandle, Is.Null);
                    Assert.That(item.ClientHandle, StatusCode.IsGood(expected) ? Is.EqualTo(43U) : Is.Not.EqualTo(43U));
                    if (StatusCode.IsGood(expected))
                    {
                        Assert.That(item.SamplingInterval, Is.EqualTo(filter == "none-no-metadata" ? 125 : 500));
                        Assert.That(item.QueueSize, Is.EqualTo(durable ? 8U : 4U));
                    }
                    if (filter == "aggregate")
                    {
                        Assert.That(results[0], Is.InstanceOf<AggregateFilterResult>());
                        var revised = (AggregateFilterResult)results[0];
                        Assert.That(revised.RevisedProcessingInterval, Is.EqualTo(1000));
                        Assert.That(revised.RevisedStartTime, Is.LessThanOrEqualTo(
                            (DateTimeUtc)clock.GetUtcNow().UtcDateTime));
                        Assert.That(revised.RevisedAggregateConfiguration.UseServerCapabilitiesDefaults, Is.False);
                        Assert.That(revised.RevisedAggregateConfiguration.PercentDataGood, Is.EqualTo(100));
                    }
                });
                server.VerifyGet(value => value.CoreNodeManager, Times.Never);
            }
        }

        /// <summary>
        /// A stale unresolved modify cannot change an already adopted or disposed item.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UnresolvedModificationRejectsAdoptedOrDisposedItem(bool disposed)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (MonitoredItem item = CreateMonitoredItem(
                server.Object, new Mock<IAsyncNodeManager>().Object, new NodeHandle(), new NodeId("State", 1)))
            {
                if (disposed)
                {
                    item.Dispose();
                }
                (ServiceResult? error, _) = item.ModifyUnresolvedAttributes(
                    new OperationContext(new RequestHeader(), null, RequestType.ModifyMonitoredItems,
                        RequestLifetime.None),
                    TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest
                    {
                        RequestedParameters = new MonitoringParameters { ClientHandle = 43 }
                    },
                    new ServerConfiguration());
                Assert.That(error!.StatusCode, Is.EqualTo(
                    disposed ? StatusCodes.BadMonitoredItemIdInvalid : StatusCodes.BadInvalidState));
                Assert.That(item.ClientHandle, Is.EqualTo(3U));
            }
        }

        /// <summary>
        /// Awaited legacy-manager edits recover only unresolved items in the same serving slot.
        /// </summary>
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task LegacyRuntimeAdditionRecoversSameSlotItemAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            using (MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                server, [(IAsyncNodeManager)owner.Lifecycle]))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("LegacyRecovery", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("LegacyRecovery", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 42
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), node.NodeId);
                var state = (IMonitoredItemGenerationBinding)item;
                state.ReleaseSource();
                ServiceResult attached = await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false);
                Assert.That(attached.StatusCode, Is.EqualTo(StatusCodes.Good));
                var subscription = new Mock<ISubscription>();
                subscription.SetupGet(value => value.EffectiveIdentity).Returns(Mock.Of<IUserIdentity>());
                item.SubscriptionCallback = subscription.Object;
                ((IMonitoredItemGenerationBinding)item).BindSubscription(subscription.Object);
                uint id = item.Id;
                uint clientHandle = item.ClientHandle;
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                    (IAsyncNodeManager)owner.Lifecycle);
                NodeManagerSlot slot = generation.Slot;
                using MonitoredItem unrelated = CreateMonitoredItem(
                    server.Object, (IAsyncNodeManager)owner.Lifecycle, new NodeHandle(),
                    new NodeId("NotInThisAddition", owner.NamespaceIndex), 3);
                unrelated.SubscriptionCallback = subscription.Object;
                ((IMonitoredItemGenerationBinding)unrelated).BindSubscription(subscription.Object);
                var unrelatedState = (IMonitoredItemGenerationBinding)unrelated;
                unrelatedState.ReleaseSource();

                await owner.DeleteNodeAsync(node.NodeId).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(state.IsSourceUnavailable, Is.True);
                    Assert.That(state.IsDeleted, Is.True);
                    Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.True);
                });

                await owner.AddNodeAsync(node).ConfigureAwait(false);

                IReadOnlyList<IMonitoredItem> owned = owner.CaptureMonitoredItems();
                Assert.Multiple(() =>
                {
                    Assert.That(state.IsSourceUnavailable, Is.False);
                    Assert.That(state.IsDeleted, Is.False);
                    Assert.That(item.Id, Is.EqualTo(id));
                    Assert.That(item.ClientHandle, Is.EqualTo(clientHandle));
                    Assert.That(owner.Owns(item.NodeManager), Is.True);
                    Assert.That(item.Generation!.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                    Assert.That(bound, Is.SameAs(generation));
                    Assert.That(slot.UnresolvedItems.ContainsKey(item), Is.False);
                    Assert.That(owned, Has.Count.EqualTo(1));
                    Assert.That(owned[0], Is.SameAs(item));
                    Assert.That(unrelatedState.IsSourceUnavailable, Is.True);
                    Assert.That(unrelatedState.IsDeleted, Is.False,
                        "Adding another subtree must not retry or queue a lost-node status for this item.");
                    Assert.That(slot.UnresolvedItems.ContainsKey(unrelated), Is.True);
                });
            }
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
                object originalHandle = new();
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    originalNodeManager.Object,
                    originalHandle,
                    node.NodeId);
                var itemLifecycle = (IMonitoredItemGenerationBinding)item;
                itemLifecycle.ReleaseSource();
                uint id = item.Id;

                ServiceResult attachResult = await owner.Lifecycle
                    .AdoptSourceAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> attached = owner.CaptureMonitoredItems();
                object? attachedHandle = item.ManagerHandle;
                bool ownedAfterAttach = owner.Owns(item.NodeManager);

                ServiceResult detachResult = await owner.Lifecycle
                    .ReleaseSourceAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> detached = owner.CaptureMonitoredItems();
                bool wasDetached = itemLifecycle.IsSourceUnavailable;
                IAsyncNodeManager? detachedOwner = item.NodeManager;
                object? detachedHandle = item.ManagerHandle;

                Assert.DoesNotThrow(() =>
                    item.QueueValue(
                        new DataValue(new Variant(84), StatusCodes.Good),
                        ServiceResult.Good));

                ServiceResult restoreResult = await owner.Lifecycle
                    .AdoptSourceAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> restored = owner.CaptureMonitoredItems();
                object? restoredHandle = item.ManagerHandle;
                bool wasAttachedAfterRestore = !itemLifecycle.IsSourceUnavailable;
                bool ownedAfterRestore = owner.Owns(item.NodeManager);
                await owner.Lifecycle.ReleaseSourceAsync(item).ConfigureAwait(false);

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
                        Is.Null);
                    Assert.That(owner.Owns(detachedOwner), Is.False);
                    Assert.That(detachedHandle, Is.Null);
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
                var itemLifecycle = (IMonitoredItemGenerationBinding)item;
                itemLifecycle.ReleaseSource();
                var lifecycle = (IMonitoredItemSourceManager)manager;
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(
                    new OperationContext(item));

                (ServiceResult firstAttach, bool firstChanged) = lifecycle.AdoptSource(
                    context,
                    handle,
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });
                (ServiceResult detach, bool detachChanged) = lifecycle.ReleaseSource(
                    context,
                    item,
                    static (_, _) => { });
                (ServiceResult secondAttach, bool secondChanged) = lifecycle.AdoptSource(
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
                    Assert.That(itemLifecycle.IsSourceUnavailable, Is.False);
                });

                lifecycle.ReleaseSource(context, item, static (_, _) => { });
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

                var lifecycle = (IMonitoredItemGenerationBinding)item;
                Assert.Multiple(() =>
                {
                    Assert.That(lifecycle.IsDeleted, Is.True);
                    Assert.That(lifecycle.IsSourceUnavailable, Is.False);
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
                var lifecycle = (IMonitoredItemGenerationBinding)item;
                lifecycle.ReleaseSource();
                Assert.That(
                    (await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false))
                        .StatusCode,
                    Is.EqualTo(StatusCodes.Good));

                ((IMonitoredItemGenerationBinding)item).Invalidate(terminal: false);
                ServiceResult detachResult = await owner.Lifecycle
                    .ReleaseSourceAsync(item)
                    .ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> remaining = owner.CaptureMonitoredItems();

                Assert.Multiple(() =>
                {
                    Assert.That(detachResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(lifecycle.IsDeleted, Is.True);
                    Assert.That(lifecycle.IsSourceUnavailable, Is.True);
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
                var lifecycle = (IMonitoredItemGenerationBinding)item;
                lifecycle.ReleaseSource();
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
                        .AdoptSourceAsync(item)
                        .ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>());
                IReadOnlyList<IMonitoredItem> remaining = owner.CaptureMonitoredItems();

                Assert.Multiple(() =>
                {
                    Assert.That(lifecycle.IsSourceUnavailable, Is.True);
                    Assert.That(remaining, Is.Empty);
                    Assert.That(node.AreEventsMonitored, Is.False);
                    Assert.That(
                        item.NodeManager,
                        Is.Null);
                    Assert.That(
                        item.ManagerHandle,
                        Is.Null);
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
                var lifecycle = (IMonitoredItemGenerationBinding)item;
                lifecycle.ReleaseSource();
                Assert.That(
                    (await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false))
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
                        .ReleaseSourceAsync(item)
                        .ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>());
                IReadOnlyList<IMonitoredItem> remaining = owner.CaptureMonitoredItems();

                Assert.Multiple(() =>
                {
                    Assert.That(lifecycle.IsSourceUnavailable, Is.False);
                    Assert.That(remaining, Has.Count.EqualTo(1));
                    Assert.That(remaining[0], Is.SameAs(item));
                    Assert.That(node.AreEventsMonitored, Is.True);
                });

                owner.SetEventSubscriptionCallback(static _ => { });
                await owner.Lifecycle.ReleaseSourceAsync(item).ConfigureAwait(false);
            }
        }

        [TestCase(ManagerPath.AsyncMonitoredNode, false, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, false, true)]
        [TestCase(ManagerPath.AsyncMonitoredNode, true, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, true, true)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false, true)]
        [TestCase(ManagerPath.AsyncSamplingGroup, true, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, true, true)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode, false, false)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode, false, true)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode, true, false)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode, true, true)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup, false, false)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup, false, true)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup, true, false)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup, true, true)]
        public async Task FailedEventSourceReleaseRetriesProviderCleanupWhenNativeOwnershipCannotBeRestoredAsync(
            ManagerPath path,
            bool removeBeforeRetry,
            bool failFirstRetry)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseObjectState(null)
                {
                    NodeId = new NodeId("EventSourceCleanup", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("EventSourceCleanup", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    new object(),
                    node.NodeId);
                ((IMonitoredItemGenerationBinding)item).ReleaseSource();
                Assert.That(
                    (await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    ((IMonitoredItemGenerationBinding)item).CaptureSource().Generation!
                        .TryGetGeneration(out NodeManagerGeneration? generation),
                    Is.True);

                var failure = new InvalidOperationException("Injected provider source-release failure.");
                var retryFailure = new InvalidOperationException("Injected source-release retry failure.");
                int unsubscribeCount = 0;
                var conflictingItem = new Mock<IMonitoredItem>();
                owner.SetEventSubscriptionCallback(unsubscribe =>
                {
                    if (unsubscribe && ++unsubscribeCount == 1)
                    {
                        Assert.That(
                            owner.TryAddNativeItem(item.Id, conflictingItem.Object),
                            Is.True);
                        throw failure;
                    }
                    if (unsubscribe && failFirstRetry && unsubscribeCount == 2)
                    {
                        throw retryFailure;
                    }
                });

                InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await owner.Lifecycle.ReleaseSourceAsync(item).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(observed, Is.SameAs(failure));
                    Assert.That(unsubscribeCount, Is.EqualTo(1));
                    Assert.That(node.AreEventsMonitored, Is.False);
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(item.ManagerHandle, Is.Null);
                });
                Assert.That(owner.TryRemoveNativeItem(item.Id), Is.True);
                if (removeBeforeRetry)
                {
                    item.Dispose();
                }
                if (failFirstRetry)
                {
                    InvalidOperationException? retriedFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await generation!.CompleteSourceReleasesAsync(null, CancellationToken.None)
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(retriedFailure, Is.SameAs(retryFailure));
                    Assert.That(unsubscribeCount, Is.EqualTo(2));
                }

                if (removeBeforeRetry)
                {
                    await generation!.CompleteSourceReleasesAsync(null, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    ServiceResult retried = await owner.Lifecycle.ReleaseSourceAsync(item).ConfigureAwait(false);
                    Assert.That(retried.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                await generation!.CompleteSourceReleasesAsync(null, CancellationToken.None).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(unsubscribeCount, Is.EqualTo(failFirstRetry ? 3 : 2));
                    Assert.That(owner.CaptureMonitoredItems(), Is.Empty);
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(item.ManagerHandle, Is.Null);
                    Assert.That(node.AreEventsMonitored, Is.False);
                });
            }
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task FailedEventAdoptionRetainsProviderCleanupForGenerationReclamationAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseObjectState(null)
                {
                    NodeId = new NodeId("FailedTargetProvider", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("FailedTargetProvider", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new object(), node.NodeId);
                ((IMonitoredItemGenerationBinding)item).ReleaseSource();
                var adoptionFailure = new InvalidOperationException("Injected target-provider failure.");
                var cleanupFailure = new InvalidOperationException("Injected target-provider cleanup failure.");
                int unsubscribes = 0;
                owner.SetEventSubscriptionCallback(unsubscribe =>
                {
                    if (!unsubscribe)
                    {
                        throw adoptionFailure;
                    }
                    if (++unsubscribes == 1)
                    {
                        throw cleanupFailure;
                    }
                });
                AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(
                    async () => await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false))
                    .ConfigureAwait(false);
                NodeManagerGeneration generation =
                    NodeManagerGenerationRegistry.GetOrCreate((IAsyncNodeManager)owner.Lifecycle);
                Assert.Multiple(() =>
                {
                    Assert.That(observed!.InnerExceptions, Has.Count.EqualTo(2));
                    Assert.That(observed.InnerExceptions[0], Is.SameAs(adoptionFailure));
                    Assert.That(observed.InnerExceptions[1], Is.SameAs(cleanupFailure));
                    Assert.That(owner.CaptureMonitoredItems(), Is.Empty);
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(item.ManagerHandle, Is.Null);
                    Assert.That(node.AreEventsMonitored, Is.False);
                    Assert.That(generation.HasPendingSourceReleases, Is.True);
                });
                item.Dispose();
                await generation.CompleteSourceReleasesAsync(null, CancellationToken.None).ConfigureAwait(false);
                await generation.CompleteSourceReleasesAsync(null, CancellationToken.None).ConfigureAwait(false);
                Assert.That(unsubscribes, Is.EqualTo(2));
                Assert.That(generation.HasPendingSourceReleases, Is.False);
            }
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public Task EventAttachmentReadinessDoesNotBlockUnrelatedMonitoredItemServiceAsync(ManagerPath path)
        {
            return AssertEventReadinessDoesNotBlockDataChangeCreationAsync(path, compensateDetach: false);
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public Task FailedEventDetachCompensationDoesNotBlockUnrelatedMonitoredItemServiceAsync(ManagerPath path)
        {
            return AssertEventReadinessDoesNotBlockDataChangeCreationAsync(path, compensateDetach: true);
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public Task FailedEventNodeDeletionCompensationDoesNotBlockUnrelatedMonitoredItemServiceAsync(ManagerPath path)
        {
            return AssertEventReadinessDoesNotBlockDataChangeCreationAsync(
                path, compensateDetach: true, deleteNode: true);
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public Task FailedEventAttachmentDoesNotRollBackReattachedItemAsync(ManagerPath path)
        {
            return AssertStaleEventTransitionPreservesReplacementAsync(path, failAttachment: true);
        }

        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public Task CompletedEventDetachDoesNotParkReattachedItemAsync(ManagerPath path)
        {
            return AssertStaleEventTransitionPreservesReplacementAsync(path, failAttachment: false);
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
                var lifecycle = (IMonitoredItemGenerationBinding)item;
                lifecycle.ReleaseSource();
                Assert.That(
                    (await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false))
                        .StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                owner.SetNodeRemovedCallback(static _ =>
                    throw new InvalidOperationException("Injected removal callback failure."));

                Assert.That(
                    async () => await owner.DeleteNodeAsync(node.NodeId).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.EqualTo("Injected removal callback failure."));
                IReadOnlyList<IMonitoredItem> remaining = owner.CaptureMonitoredItems();

                Assert.Multiple(() =>
                {
                    Assert.That(owner.ContainsNode(node.NodeId), Is.False);
                    Assert.That(lifecycle.IsDeleted, Is.True);
                    Assert.That(lifecycle.IsSourceUnavailable, Is.True);
                    Assert.That(remaining, Is.Empty);
                });
            }
        }

        /// <summary>
        /// A partially deleted subtree preserves monitoring for surviving descendants.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode, false)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, true)]
        [TestCase(ManagerPath.AsyncSamplingGroup, true)]
        public async Task PartialNodeDeletionRestoresSurvivingMonitoredItemsAsync(ManagerPath path, bool service)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var root = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("PartialRemoval", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("PartialRemoval", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                root.CreateAsPredefinedNode(owner.SystemContext);
                var child = new BaseDataVariableState(root)
                {
                    NodeId = new NodeId("SurvivingChild", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("SurvivingChild", owner.NamespaceIndex),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    DataType = DataTypeIds.Int32,
                    Value = 2
                };
                child.CreateAsPredefinedNode(owner.SystemContext);
                root.AddChild(child);
                await owner.AddNodeAsync(root).ConfigureAwait(false);
                using MonitoredItem rootItem = CreateMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), root.NodeId, 2);
                using MonitoredItem childItem = CreateMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), child.NodeId, 3);
                var rootLifecycle = (IMonitoredItemGenerationBinding)rootItem;
                var childLifecycle = (IMonitoredItemGenerationBinding)childItem;
                rootLifecycle.ReleaseSource();
                childLifecycle.ReleaseSource();
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(rootItem).ConfigureAwait(false)), Is.True);
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(childItem).ConfigureAwait(false)), Is.True);
                var failure = new InvalidOperationException("Injected partial removal failure.");
                owner.SetNodeRemovedCallback(removed =>
                {
                    if (ReferenceEquals(removed, root))
                    {
                        throw failure;
                    }
                });
                await Assert.ThatAsync(async () =>
                {
                    if (service)
                    {
                        using var context = new OperationContext(rootItem);
                        await owner.AsyncManager.DeleteNodeAsync(
                            context, new DeleteNodesItem { NodeId = root.NodeId }).ConfigureAwait(false);
                    }
                    else
                    {
                        await owner.DeleteNodeAsync(root.NodeId).ConfigureAwait(false);
                    }
                }, Throws.TypeOf<InvalidOperationException>().And.SameAs(failure)).ConfigureAwait(false);

                IReadOnlyList<IMonitoredItem> remaining = owner.CaptureMonitoredItems();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(owner.ContainsNode(root.NodeId), Is.False);
                    Assert.That(owner.ContainsNode(child.NodeId), Is.True);
                    Assert.That(rootLifecycle.IsDeleted, Is.True);
                    Assert.That(rootLifecycle.IsSourceUnavailable, Is.True);
                    Assert.That(childLifecycle.IsDeleted, Is.False);
                    Assert.That(childLifecycle.IsSourceUnavailable, Is.False);
                    Assert.That(owner.Owns(childItem.NodeManager), Is.True);
                    Assert.That(remaining, Has.Count.EqualTo(1));
                    Assert.That(remaining[0], Is.SameAs(childItem));
                }
            }
        }

        /// <summary>
        /// Restoration failures do not hide the deletion failure or abandon other surviving items.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task PartialDeletionPreservesFailuresAndRestoresOtherItemsAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("FailedRoot", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("FailedRoot", owner.NamespaceIndex)
                };
                root.CreateAsPredefinedNode(owner.SystemContext);
                var value = new BaseDataVariableState(root)
                {
                    NodeId = new NodeId("SurvivingValue", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("SurvivingValue", owner.NamespaceIndex),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                value.CreateAsPredefinedNode(owner.SystemContext);
                var events = new BaseObjectState(root)
                {
                    NodeId = new NodeId("SurvivingEvents", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("SurvivingEvents", owner.NamespaceIndex),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                events.CreateAsPredefinedNode(owner.SystemContext);
                root.AddChild(value);
                root.AddChild(events);
                await owner.AddNodeAsync(root).ConfigureAwait(false);
                using MonitoredItem valueItem = CreateMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), value.NodeId, 2);
                using MonitoredItem eventItem = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), events.NodeId, 3);
                var valueLifecycle = (IMonitoredItemGenerationBinding)valueItem;
                var eventLifecycle = (IMonitoredItemGenerationBinding)eventItem;
                valueLifecycle.ReleaseSource();
                eventLifecycle.ReleaseSource();
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(valueItem).ConfigureAwait(false)), Is.True);
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(eventItem).ConfigureAwait(false)), Is.True);
                var deletionFailure = new InvalidOperationException("Injected deletion failure.");
                var restorationFailure = new InvalidOperationException("Injected restoration failure.");
                bool restoring = false;
                owner.SetEventSubscriptionCallback(unsubscribe =>
                {
                    if (restoring && !unsubscribe)
                    {
                        throw restorationFailure;
                    }
                });
                owner.SetNodeRemovedCallback(removed =>
                {
                    if (ReferenceEquals(removed, root))
                    {
                        restoring = true;
                        throw deletionFailure;
                    }
                });
                AggregateException? observed = null;
                try
                {
                    await owner.DeleteNodeAsync(root.NodeId).ConfigureAwait(false);
                }
                catch (AggregateException failure)
                {
                    observed = failure;
                }
                owner.SetEventSubscriptionCallback(static _ => { });
                Assert.That(observed, Is.Not.Null);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(observed!.Flatten().InnerExceptions, Does.Contain(deletionFailure));
                    Assert.That(observed.Flatten().InnerExceptions, Does.Contain(restorationFailure));
                    Assert.That(valueLifecycle.IsSourceUnavailable, Is.False);
                    Assert.That(valueLifecycle.IsDeleted, Is.False);
                    Assert.That(owner.Owns(valueItem.NodeManager), Is.True);
                    Assert.That(eventLifecycle.IsDeleted, Is.False);
                }
            }
        }

        /// <summary>
        /// A timed-out restoration does not consume the cleanup budget of the next surviving item.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public async Task TimedOutRestorationDoesNotCancelFollowingItemsAsync(ManagerPath path)
        {
            var clock = new FakeTimeProvider();
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory, clock);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("TimedOutRestoration", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("TimedOutRestoration", owner.NamespaceIndex)
                };
                root.CreateAsPredefinedNode(owner.SystemContext);
                var first = new BaseObjectState(root)
                {
                    NodeId = new NodeId("FirstSurvivor", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("FirstSurvivor", owner.NamespaceIndex),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                var second = new BaseObjectState(root)
                {
                    NodeId = new NodeId("SecondSurvivor", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("SecondSurvivor", owner.NamespaceIndex),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                first.CreateAsPredefinedNode(owner.SystemContext);
                second.CreateAsPredefinedNode(owner.SystemContext);
                root.AddChild(first);
                root.AddChild(second);
                await owner.AddNodeAsync(root).ConfigureAwait(false);
                using MonitoredItem firstItem = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), first.NodeId, 2);
                using MonitoredItem secondItem = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), second.NodeId, 3);
                var firstLifecycle = (IMonitoredItemGenerationBinding)firstItem;
                var secondLifecycle = (IMonitoredItemGenerationBinding)secondItem;
                firstLifecycle.ReleaseSource();
                secondLifecycle.ReleaseSource();
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(firstItem).ConfigureAwait(false)), Is.True);
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(secondItem).ConfigureAwait(false)), Is.True);

                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int restorationCalls = 0;
                owner.AsyncManager.EventSubscriptionCallback = async (unsubscribe, ct) =>
                {
                    if (!unsubscribe && Interlocked.Increment(ref restorationCalls) == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                };
                var deletionFailure = new InvalidOperationException("Injected deletion failure.");
                owner.SetNodeRemovedCallback(removed =>
                {
                    if (ReferenceEquals(removed, root))
                    {
                        throw deletionFailure;
                    }
                });
                Task<bool> deletion = owner.DeleteNodeAsync(root.NodeId).AsTask();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
                    Assert.That(deletion.IsCompleted, Is.False);
                    clock.Advance(TimeSpan.FromTicks(1));
                    await Assert.ThatAsync(
                        () => deletion.WaitAsync(TimeSpan.FromSeconds(10)),
                        Throws.TypeOf<AggregateException>().With.InnerException.SameAs(deletionFailure))
                        .ConfigureAwait(false);

                    IReadOnlyList<IMonitoredItem> remaining = owner.CaptureMonitoredItems();
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(restorationCalls, Is.EqualTo(2));
                        Assert.That(remaining, Has.Count.EqualTo(1));
                        Assert.That(
                            firstLifecycle.IsSourceUnavailable, Is.Not.EqualTo(secondLifecycle.IsSourceUnavailable));
                        Assert.That(firstLifecycle.IsDeleted, Is.False);
                        Assert.That(secondLifecycle.IsDeleted, Is.False);
                    }
                }
                finally
                {
                    owner.AsyncManager.EventSubscriptionCallback = static (_, _) => default;
                    release.TrySetResult(true);
                    try
                    {
                        await deletion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    catch (AggregateException failure) when (
                        failure.Flatten().InnerExceptions.Contains(deletionFailure))
                    {
                    }
                }
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
                var restoredItems = new List<IMonitoredItem> { null!, null! };

                await master.RestoreMonitoredItemsAsync(
                    storedItems,
                    restoredItems,
                    new Mock<IUserIdentity>().Object,
                    CancellationToken.None).ConfigureAwait(false);
                using IMonitoredItem recoverableItem = restoredItems[0];
                using IMonitoredItem deletableItem = restoredItems[1];
                var recoverableLifecycle = (IMonitoredItemGenerationBinding)recoverableItem;
                var deletableLifecycle = (IMonitoredItemGenerationBinding)deletableItem;
                Assert.Multiple(() =>
                {
                    Assert.That(recoverableLifecycle.IsSourceUnavailable, Is.True);
                    Assert.That(recoverableLifecycle.IsDeleted, Is.True);
                    Assert.That(
                        recoverableItem.NodeManager,
                        Is.Null);
                    Assert.That(
                        recoverableItem.ManagerHandle,
                        Is.Null);
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
                await master.ResolveMonitoredItemSourcesAsync(
                    recoverableManager,
                    [recoverableItem],
                    CancellationToken.None).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(deletableLifecycle.IsSourceUnavailable, Is.True);
                    Assert.That(deletableLifecycle.IsDeleted, Is.True);
                    Assert.That(deleteErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(recoverableLifecycle.IsSourceUnavailable, Is.False);
                    Assert.That(recoverableLifecycle.IsDeleted, Is.False);
                    Assert.That(recoverableItem.NodeManager, Is.SameAs(recoverableManager));
                    Assert.That(
                        recoverableManager.MonitoredItems[recoverableItem.Id],
                        Is.SameAs(recoverableItem));
                });

                await ((INodeManagerMonitoredItemSource)recoverableManager)
                    .ReleaseSourceAsync(recoverableItem)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// One item completes handoff before another item is released from the serving source.
        /// </summary>
        [Test]
        public async Task CompatibleItemsCompleteHandoffBeforeNextItemIsReleasedAsync()
        {
            IMonitoredItem first = new Mock<IMonitoredItem>().Object;
            IMonitoredItem second = new Mock<IMonitoredItem>().Object;
            var operations = new List<string>();
            var current = new TestNodeManagerLifecycle
            {
                DetachCallback = item =>
                {
                    operations.Add(ReferenceEquals(item, first) ? "release-first" : "release-second");
                    return ServiceResult.Good;
                }
            };
            var replacement = new TestNodeManagerLifecycle
            {
                AttachCallback = item =>
                {
                    operations.Add(ReferenceEquals(item, first) ? "adopt-first" : "adopt-second");
                    return ServiceResult.Good;
                }
            };
            var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                current, replacement, [first, second], [], static _ => true);

            Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);
            Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);

            Assert.That(string.Join(", ", operations),
                Is.EqualTo("release-first, adopt-first, release-second, adopt-second"));
        }

        [Test]
        public async Task AdoptionSkipsItemRemovedFromItsSubscriptionDuringReleaseAsync()
        {
            bool subscriptionOwnsItem = true;
            var item = new Mock<IMonitoredItem>();
            var current = new TestNodeManagerLifecycle
            {
                DetachCallback = _ =>
                {
                    subscriptionOwnsItem = false;
                    return ServiceResult.Good;
                }
            };
            var replacement = new TestNodeManagerLifecycle();

            var server = new Mock<IServerInternal>();
            var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                current,
                replacement,
                compatibleItems: [item.Object],
                deletedItems: [],
                isOwnedBySubscription: _ => subscriptionOwnsItem);

            Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(current.DetachCount, Is.EqualTo(1));
                Assert.That(current.AttachCount, Is.Zero);
                Assert.That(replacement.AttachCount, Is.Zero);
            });
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
                current,
                replacement,
                compatibleItems: [item.Object],
                deletedItems: [],
                isOwnedBySubscription: static _ => true);

            Assert.Multiple(() =>
            {
                Assert.That(current.DetachCount, Is.Zero);
                Assert.That(replacement.AttachCount, Is.Zero);
            });

            committed = true;
            List<Exception> failures = await transition
                .AdoptAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(failures, Is.Empty);
            Assert.That(replacement.AttachCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A failed older handoff must not clear or invalidate a source installed during its callback.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FailedHandoffPreservesNewerSourceBindingAsync(bool throwFailure, bool releaseNewSource)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (MonitoredItem item = CreateMonitoredItem(
                server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), new NodeId("NewerSource", 1)))
            {
                var binding = (IMonitoredItemGenerationBinding)item;
                var newerOwner = Mock.Of<IAsyncNodeManager>();
                var newerHandle = new NodeHandle();
                var failure = new InvalidOperationException(
                    "The older handoff failed after another source was installed.");
                var current = new TestNodeManagerLifecycle
                {
                    DetachCallback = _ =>
                    {
                        Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);
                        return ServiceResult.Good;
                    }
                };
                var replacement = new TestNodeManagerLifecycle
                {
                    AttachCallback = monitoredItem =>
                    {
                        Assert.That(((IMonitoredItemGenerationBinding)monitoredItem).TryBindSource(
                            binding.SourceRevision, newerOwner, newerHandle, out _), Is.True);
                        if (releaseNewSource)
                        {
                            Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);
                        }
                        if (throwFailure)
                        {
                            throw failure;
                        }
                        return StatusCodes.BadNodeIdUnknown;
                    }
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, replacement, [item], [], static _ => true);

                List<Exception> failures = await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false);
                long newerRevision = binding.SourceRevision;
                transition.MarkDeletedItems();
                transition.MarkDeletedItems();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(failures, Has.Count.EqualTo(throwFailure ? 1 : 0));
                    if (throwFailure)
                    {
                        Assert.That(failures[0], Is.SameAs(failure));
                    }
                    Assert.That(item.NodeManager, Is.SameAs(releaseNewSource ? null : newerOwner));
                    Assert.That(item.ManagerHandle, Is.SameAs(releaseNewSource ? null : newerHandle));
                    Assert.That(binding.SourceRevision, Is.EqualTo(newerRevision));
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDeleted, Is.False);
                    Assert.That(
                        ((IMonitoredItemGenerationBinding)item).IsSourceUnavailable, Is.EqualTo(releaseNewSource));
                    Assert.That(binding.IsTerminal, Is.False);
                    Assert.That(current.DetachCount, Is.EqualTo(1));
                    Assert.That(replacement.AttachCount, Is.EqualTo(1));
                }
            }
        }

        /// <summary>
        /// Subscription removal cannot clear ownership that the native endpoint has not released.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RemovedHandoffRetainsSourceUntilNativeCleanupCompletesAsync(bool failedAdoption)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (MonitoredItem item = CreateMonitoredItem(
                server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), new NodeId("RemovedSource", 1)))
            {
                var binding = (IMonitoredItemGenerationBinding)item;
                var subscription = Mock.Of<ISubscription>();
                item.SubscriptionCallback = subscription;
                binding.BindSubscription(subscription);
                var target = Mock.Of<IAsyncNodeManager>();
                var handle = new NodeHandle();
                var cleanupFailure = new InvalidOperationException("Native cleanup must remain retryable.");
                var current = new TestNodeManagerLifecycle();
                var replacement = new TestNodeManagerLifecycle
                {
                    AttachCallback = monitoredItem =>
                    {
                        Assert.That(monitoredItem, Is.SameAs(item));
                        Assert.That(binding.TryBindSource(binding.SourceRevision, target, handle, out _), Is.True);
                        binding.UnbindSubscription(subscription);
                        if (failedAdoption)
                        {
                            throw new ServiceResultException(StatusCodes.BadMonitoredItemIdInvalid);
                        }
                        return ServiceResult.Good;
                    },
                    DetachCallback = _ => throw cleanupFailure
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, replacement, [item], [], static _ => true);

                List<Exception> failures = await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false);
                transition.MarkDeletedItems();
                Assert.Multiple(() =>
                {
                    Assert.That(failures, Has.Count.EqualTo(failedAdoption ? 0 : 1));
                    if (!failedAdoption)
                    {
                        Assert.That(failures[0], Is.SameAs(cleanupFailure));
                    }
                    Assert.That(binding.IsRemoved, Is.True);
                    Assert.That(item.NodeManager, Is.SameAs(target));
                    Assert.That(item.ManagerHandle, Is.SameAs(handle));
                    Assert.That(item.Generation, Is.Not.Null);
                    Assert.That(binding.IsDeleted, Is.False);
                    Assert.That(replacement.DetachCount, Is.EqualTo(failedAdoption ? 0 : 1));
                });
            }
        }

        /// <summary>
        /// A failed post-native adoption hook releases its registration and retains failed provider cleanup.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode, false, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, true, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, true, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, false, true)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false, true)]
        public async Task FailedAttachedHookReleasesNativeSourceAndRetainsCleanupAsync(
            ManagerPath path, bool failCleanup, bool cancelAttachment)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("AttachedHook", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), node.NodeId);
                var binding = (IMonitoredItemGenerationBinding)item;
                binding.ReleaseSource();
                using var cancellation = new CancellationTokenSource();
                Exception failure = cancelAttachment
                    ? new OperationCanceledException(cancellation.Token)
                    : new InvalidOperationException("Post-native source adoption failed.");
                var cleanupFailure = new InvalidOperationException("Provider cleanup failed.");
                owner.AsyncManager.MonitoredItemAttachedCallback = _ =>
                {
                    if (cancelAttachment)
                    {
                        cancellation.Cancel();
                    }
                    throw failure;
                };
                owner.AsyncManager.MonitoredItemDetachedCallback = ct =>
                {
                    Assert.That(ct.CanBeCanceled, Is.False);
                    if (failCleanup)
                    {
                        throw cleanupFailure;
                    }
                    return default;
                };
                Exception actual = (await Assert.CatchAsync<Exception>(async () =>
                    await owner.Lifecycle.AdoptSourceAsync(item, cancellationToken: cancellation.Token)
                        .ConfigureAwait(false)))!;
                if (failCleanup)
                {
                    Assert.That(actual, Is.TypeOf<AggregateException>());
                    Assert.That(((AggregateException)actual).InnerExceptions, Is.EqualTo(
                        new[] { failure, cleanupFailure }));
                }
                else
                {
                    Assert.That(actual, Is.SameAs(failure));
                }
                Assert.That(owner.CaptureMonitoredItems(), Is.Empty);
                Assert.That(item.NodeManager, Is.Null);
                Assert.That(item.ManagerHandle, Is.Null);
                Assert.That(item.Generation, Is.Null);
                Assert.That(owner.AsyncManager.DetachedNotificationCount, Is.EqualTo(1));
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner.AsyncManager);
                Assert.That(generation.HasPendingSourceReleases, Is.EqualTo(failCleanup));

                owner.AsyncManager.MonitoredItemDetachedCallback = null;
                await generation.CompleteSourceReleasesAsync(item, CancellationToken.None).ConfigureAwait(false);
                Assert.That(generation.HasPendingSourceReleases, Is.False);
                Assert.That(owner.AsyncManager.DetachedNotificationCount, Is.EqualTo(failCleanup ? 2 : 1));
                owner.AsyncManager.MonitoredItemAttachedCallback = null;
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false)), Is.True);
                Assert.That(owner.CaptureMonitoredItems(), Is.EqualTo(new[] { item }));
                Assert.That(binding.IsDeleted, Is.False);
                Assert.That(binding.IsSourceUnavailable, Is.False);
            }
        }

        /// <summary>
        /// Idempotent adoption neither repeats provider notification nor releases the existing source.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public async Task UnchangedAdoptionDoesNotNotifyOrReleaseExistingNativeSourceAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("UnchangedAdoption", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = await CreateIndependentDataChangeItemAsync(owner.AsyncManager, node.NodeId)
                    .ConfigureAwait(false);
                MonitoredItemSourceSnapshot source = ((IMonitoredItemGenerationBinding)item).CaptureSource();
                int callbacks = 0;
                owner.AsyncManager.MonitoredItemAttachedCallback = _ =>
                {
                    callbacks++;
                    throw new InvalidOperationException("An unchanged adoption must not notify providers.");
                };
                ServiceResult result = await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(callbacks, Is.Zero);
                Assert.That(owner.AsyncManager.DetachedNotificationCount, Is.Zero);
                Assert.That(owner.CaptureMonitoredItems(), Is.EqualTo(new[] { item }));
                Assert.That(item.ManagerHandle, Is.SameAs(source.Handle));
                Assert.That(item.Generation, Is.SameAs(source.Generation));
                Assert.That(((IMonitoredItemGenerationBinding)item).SourceRevision, Is.EqualTo(source.Revision));
            }
        }

        /// <summary>
        /// An older attached hook cannot roll back a source that a nested operation already replaced.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        public async Task FailedAttachedHookCannotReleaseNewerIncarnationAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("ReplacedAttachedHook", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), node.NodeId);
                var binding = (IMonitoredItemGenerationBinding)item;
                binding.ReleaseSource();
                var failure = new InvalidOperationException("Older post-native callback failed.");
                long newerRevision = 0;
                owner.AsyncManager.MonitoredItemAttachedCallback = async ct =>
                {
                    owner.AsyncManager.MonitoredItemAttachedCallback = null;
                    Assert.That(ServiceResult.IsGood(
                        await owner.Lifecycle.ReleaseSourceAsync(item, ct).ConfigureAwait(false)), Is.True);
                    Assert.That(ServiceResult.IsGood(
                        await owner.Lifecycle.AdoptSourceAsync(item, cancellationToken: ct)
                            .ConfigureAwait(false)), Is.True);
                    newerRevision = binding.SourceRevision;
                    throw failure;
                };
                Exception actual = (await Assert.CatchAsync<Exception>(async () =>
                    await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false)))!;
                Assert.That(actual, Is.SameAs(failure));
                Assert.That(owner.CaptureMonitoredItems(), Is.EqualTo(new[] { item }));
                Assert.That(owner.Owns(item.NodeManager), Is.True);
                Assert.That(item.ManagerHandle, Is.Not.Null);
                Assert.That(binding.SourceRevision, Is.EqualTo(newerRevision));
                Assert.That(binding.IsDeleted, Is.False);
                Assert.That(owner.AsyncManager.DetachedNotificationCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// A failed native target that rolls back its own binding must still publish the lost-node status.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task FailedNativeHandoffReportsLostSourceAfterTargetRollbackAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseObjectState(null)
                {
                    NodeId = new NodeId("FailedHandoffTarget", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("FailedHandoffTarget", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                node.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), node.NodeId);
                var binding = (IMonitoredItemGenerationBinding)item;
                var failure = new InvalidOperationException("The native target subscription failed.");
                owner.SetEventSubscriptionCallback(unsubscribe =>
                {
                    if (!unsubscribe)
                    {
                        throw failure;
                    }
                });
                var current = new TestNodeManagerLifecycle
                {
                    DetachCallback = _ =>
                    {
                        Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);
                        return ServiceResult.Good;
                    }
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, owner.Lifecycle, [item], [], static _ => true);

                List<Exception> failures = await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false);
                transition.MarkDeletedItems();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(failures, Has.Count.EqualTo(1));
                    Assert.That(failures[0], Is.SameAs(failure));
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(item.ManagerHandle, Is.Null);
                    Assert.That(owner.CaptureMonitoredItems(), Is.Empty);
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDeleted, Is.True);
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsSourceUnavailable, Is.True);
                }
            }
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
                    current,
                    replacement,
                    compatibleItems: [item],
                    deletedItems: [],
                    isOwnedBySubscription: static _ => true);

                List<Exception> failures = await transition
                    .AdoptAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                transition.MarkDeletedItems();
                var itemLifecycle = (IMonitoredItemGenerationBinding)item;

                Assert.Multiple(() =>
                {
                    Assert.That(failures, Has.Count.EqualTo(1));
                    Assert.That(failures[0].Message, Is.EqualTo("Injected attach failure."));
                    Assert.That(itemLifecycle.IsSourceUnavailable, Is.True);
                    Assert.That(itemLifecycle.IsDeleted, Is.True);
                });
            }
        }

        /// <summary>
        /// Slot-local recovery reports a rolled-back source as lost and drains failed provider cleanup before retry.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, true)]
        [TestCase(ManagerPath.AsyncSamplingGroup, true)]
        public async Task FailedSlotRecoveryAfterNativeCallbackRemainsRetryableAsync(ManagerPath path, bool failCleanup)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            using (MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                server, [owner.AsyncManager]))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("FailedNativeSlotRecovery", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, owner.AsyncManager, new NodeHandle(), node.NodeId);
                var binding = (IMonitoredItemGenerationBinding)item;
                binding.ReleaseSource();
                var failure = new InvalidOperationException("Slot recovery provider failed after native adoption.");
                var cleanupFailure = new InvalidOperationException("Slot recovery provider release failed.");
                owner.AsyncManager.MonitoredItemAttachedCallback = _ => throw failure;
                if (failCleanup)
                {
                    owner.AsyncManager.MonitoredItemDetachedCallback = _ => throw cleanupFailure;
                }
                Exception observed = (await Assert.CatchAsync<Exception>(async () =>
                    await master.ResolveMonitoredItemSourcesAsync(owner.AsyncManager, [item]).ConfigureAwait(false)))!;
                Assert.That(observed, failCleanup ? Is.TypeOf<AggregateException>() : Is.SameAs(failure));
                Assert.That(owner.CaptureMonitoredItems(), Is.Empty);
                Assert.That(binding.IsDeleted, Is.True);
                Assert.That(binding.IsSourceUnavailable, Is.True);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner.AsyncManager);
                Assert.That(generation.HasPendingSourceReleases, Is.EqualTo(failCleanup));
                owner.AsyncManager.MonitoredItemAttachedCallback = null;
                owner.AsyncManager.MonitoredItemDetachedCallback = null;

                await master.ResolveMonitoredItemSourcesAsync(owner.AsyncManager, [item]).ConfigureAwait(false);
                Assert.That(generation.HasPendingSourceReleases, Is.False);
                Assert.That(owner.AsyncManager.DetachedNotificationCount, Is.EqualTo(failCleanup ? 2 : 1));
                Assert.That(owner.CaptureMonitoredItems(), Is.EqualTo(new[] { item }));
                Assert.That(owner.Owns(item.NodeManager), Is.True);
                Assert.That(binding.IsDeleted, Is.False);
                Assert.That(binding.IsSourceUnavailable, Is.False);
            }
        }

        /// <summary>
        /// Slot-local recovery must preserve a newer source installed before an older adoption reports failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedSlotRecoveryPreservesNewerSourceBindingAsync(bool throwFailure)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            var target = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerMonitoredItemSource> lifecycle = target.As<INodeManagerMonitoredItemSource>();
            using (queueFactory)
            using (MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [target.Object]))
            using (MonitoredItem item = CreateMonitoredItem(
                server.Object, target.Object, new NodeHandle(), new NodeId("NewerRecoverySource", 1)))
            {
                var binding = (IMonitoredItemGenerationBinding)item;
                Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);
                var newerOwner = Mock.Of<IAsyncNodeManager>();
                var newerHandle = new NodeHandle();
                var failure = new InvalidOperationException("An older slot recovery failed after a newer binding.");
                lifecycle.Setup(value => value.AdoptSourceAsync(
                        item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()))
                    .Returns((IMonitoredItem monitoredItem, MonitoredItemAdoption? adoption, CancellationToken _) =>
                    {
                        Assert.That(((IMonitoredItemGenerationBinding)monitoredItem).TryBindSource(
                            binding.SourceRevision, newerOwner, newerHandle, out long boundRevision), Is.True);
                        Assert.That(boundRevision, Is.EqualTo(binding.SourceRevision));
                        if (throwFailure)
                        {
                            throw failure;
                        }
                        return new ValueTask<ServiceResult>(StatusCodes.BadNodeIdUnknown);
                    });

                if (throwFailure)
                {
                    InvalidOperationException observed = (await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await master.ResolveMonitoredItemSourcesAsync(target.Object, [item])
                            .ConfigureAwait(false)))!;
                    Assert.That(observed, Is.SameAs(failure));
                }
                else
                {
                    await master.ResolveMonitoredItemSourcesAsync(target.Object, [item]).ConfigureAwait(false);
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(item.NodeManager, Is.SameAs(newerOwner));
                    Assert.That(item.ManagerHandle, Is.SameAs(newerHandle));
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDeleted, Is.False);
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsSourceUnavailable, Is.False);
                    Assert.That(binding.IsTerminal, Is.False);
                }
                lifecycle.Verify(value => value.AdoptSourceAsync(
                    item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Test]
        public async Task AdoptionResumesAfterReleaseFailureWithoutRestoringCommittedItemsAsync()
        {
            bool rejectSecond = true;
            var firstItem = new Mock<IMonitoredItem>();
            var secondItem = new Mock<IMonitoredItem>();
            var current = new TestNodeManagerLifecycle
            {
                DetachCallback = item => rejectSecond && ReferenceEquals(item, secondItem.Object)
                    ? new ServiceResult(StatusCodes.BadInvalidState)
                    : ServiceResult.Good
            };
            var server = new Mock<IServerInternal>();
            var replacement = new TestNodeManagerLifecycle();
            var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                current,
                replacement,
                compatibleItems: [firstItem.Object, secondItem.Object],
                deletedItems: [],
                isOwnedBySubscription: static _ => true);

            ServiceResultException detachException = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await transition
                    .AdoptAsync(CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(detachException.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(current.DetachCount, Is.EqualTo(2));
                Assert.That(current.AttachCount, Is.Zero);
                Assert.That(replacement.AttachCount, Is.EqualTo(1));
            });

            rejectSecond = false;
            Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);
            Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(current.DetachCount, Is.EqualTo(3));
                Assert.That(current.AttachCount, Is.Zero);
                Assert.That(replacement.AttachCount, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Invalidating a failed transition cannot transfer items whose source release never completed.
        /// </summary>
        [Test]
        public async Task ReleaseFailureDoesNotInvalidateItemsStillOwnedByTheSourceAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var owner = new Mock<IAsyncNodeManager>();
                using MonitoredItem first = CreateMonitoredItem(
                    server.Object, owner.Object, new object(), new NodeId("Released", 1), 2);
                using MonitoredItem second = CreateMonitoredItem(
                    server.Object, owner.Object, new object(), new NodeId("StillOwned", 1), 3);
                var current = new TestNodeManagerLifecycle
                {
                    DetachCallback = item => ReferenceEquals(item, second)
                        ? new ServiceResult(StatusCodes.BadInvalidState)
                        : ServiceResult.Good
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, null, [], [first, second], static _ => true);

                await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false));
                transition.MarkDeletedItems();

                Assert.Multiple(() =>
                {
                    Assert.That(((IMonitoredItemGenerationBinding)first).IsDeleted, Is.True);
                    Assert.That(((IMonitoredItemGenerationBinding)second).IsDeleted, Is.False);
                    Assert.That(second.NodeManager, Is.SameAs(owner.Object));
                });
            }
        }

        /// <summary>
        /// A failed handoff preserves source ownership so the next retirement attempt can retry it.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RetirementHandoffFailurePreservesSourceOwnershipForRetryAsync(bool migrate, bool throws)
        {
            var server = new Mock<IServerInternal>();
            var current = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerMonitoredItemSource> source = current.As<INodeManagerMonitoredItemSource>();
            var replacement = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerMonitoredItemSource> target = replacement.As<INodeManagerMonitoredItemSource>();
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(value => value.NodeId).Returns(new NodeId("RetainedFailure", 1));
            item.As<IMonitoredItemGenerationBinding>();
            var subscription = new Mock<ISubscription>();
            subscription.As<ISubscriptionMonitoredItemLifecycle>()
                .Setup(value => value.ContainsMonitoredItem(item.Object)).Returns(true);
            item.SetupGet(value => value.SubscriptionCallback).Returns(subscription.Object);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(current.Object);
            generation.MonitoredItems.TryAdd(item.Object, subscription.Object);
            source.Setup(value => value.ReleaseSourceAsync(
                item.Object, It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns(() => throws
                    ? throw new InvalidOperationException("Injected retirement handoff failure.")
                    : new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadInvalidState)));
            target.Setup(value => value.ValidateSourceAsync(item.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Exception failure = await Assert.CatchAsync<Exception>(async () =>
                {
                    if (migrate)
                    {
                        await NodeManagerLifecycle.MigrateLateMonitoredItemsAsync(
                            server.Object, current.Object, replacement.Object).ConfigureAwait(false);
                    }
                    else
                    {
                        await NodeManagerLifecycle.DetachActiveMonitoredItemsAsync(
                            server.Object, current.Object).ConfigureAwait(false);
                    }
                });
                Assert.Multiple(() =>
                {
                    Assert.That(failure, Is.TypeOf(
                        throws ? typeof(InvalidOperationException) : typeof(ServiceResultException)));
                    Assert.That(generation.MonitoredItems.ContainsKey(item.Object), Is.True);
                });
                source.Verify(value => value.ReleaseSourceAsync(
                    item.Object, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Exactly(attempt));
            }
            target.Verify(value => value.AdoptSourceAsync(
                It.IsAny<IMonitoredItem>(), It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Late handoff retries retain provider ownership after native membership has been released.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LateHandoffResumesPendingProviderCleanupWithoutNativeMembershipAsync(bool migrate)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var current = new Mock<IAsyncNodeManager>();
                Mock<INodeManagerMonitoredItemSource> source = current.As<INodeManagerMonitoredItemSource>();
                var replacement = new Mock<IAsyncNodeManager>();
                Mock<INodeManagerMonitoredItemSource> target =
                    replacement.As<INodeManagerMonitoredItemSource>();
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, current.Object, new NodeHandle(), new NodeId("LatePendingSource", 1));
                var binding = (IMonitoredItemGenerationBinding)item;
                var subscription = new Mock<ISubscription>();
                subscription.As<ISubscriptionMonitoredItemLifecycle>()
                    .Setup(value => value.ContainsMonitoredItem(item)).Returns(true);
                item.SubscriptionCallback = subscription.Object;
                binding.BindSubscription(subscription.Object);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(current.Object);
                int sourceCalls = 0;
                int cleanupCalls = 0;
                var failure = new InvalidOperationException("Injected late source-provider failure.");
                source.Setup(value => value.ReleaseSourceAsync(item, It.IsAny<long>(), It.IsAny<CancellationToken>()))
                    .Returns(async (IMonitoredItem _, long sourceRevision, CancellationToken ct) =>
                    {
                        if (++sourceCalls == 1)
                        {
                            long revision = binding.SourceRevision;
                            Assert.That(binding.TryClearSource(revision), Is.True);
                            generation.RetainSourceRelease(item, revision, _ =>
                            {
                                cleanupCalls++;
                                return default;
                            });
                            throw failure;
                        }
                        await generation.CompleteSourceReleasesAsync(item, ct).ConfigureAwait(false);
                        return ServiceResult.Good;
                    });
                target.Setup(value => value.ValidateSourceAsync(item, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(ServiceResult.Good);
                target.Setup(value => value.AdoptSourceAsync(
                    item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Assert.That(binding.TryBindSource(
                            binding.SourceRevision, replacement.Object, new NodeHandle(), out _), Is.True);
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });
                InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await ExecuteAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(observed, Is.SameAs(failure));
                    Assert.That(generation.MonitoredItems, Is.Empty);
                    Assert.That(NodeManagerLifecycle.HasActiveMonitoredItems(server.Object, current.Object), Is.False);
                    Assert.That(generation.HasPendingSourceReleases, Is.True);
                });

                await ExecuteAsync().ConfigureAwait(false);
                await ExecuteAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(sourceCalls, Is.EqualTo(2));
                    Assert.That(cleanupCalls, Is.EqualTo(1));
                    Assert.That(generation.HasPendingSourceReleases, Is.False);
                    Assert.That(binding.IsTerminal, Is.EqualTo(!migrate));
                    Assert.That(item.Id, Is.EqualTo(2));
                    Assert.That(item.NodeManager, migrate ? Is.SameAs(replacement.Object) : Is.Null);
                });
                target.Verify(value => value.AdoptSourceAsync(
                    item, It.IsAny<MonitoredItemAdoption>(), It.IsAny<CancellationToken>()),
                    migrate ? Times.Once : Times.Never);

                ValueTask ExecuteAsync()
                {
                    return migrate
                        ? NodeManagerLifecycle.MigrateLateMonitoredItemsAsync(
                            server.Object, current.Object, replacement.Object)
                        : NodeManagerLifecycle.DetachActiveMonitoredItemsAsync(server.Object, current.Object);
                }
            }
        }

        /// <summary>
        /// Client disposal is immediate and forbids queue use even while generation adoption is running.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClientDisposalDoesNotWaitForAdoptionOrPermitLaterQueueUseAsync(bool disposeDuringRelease)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var queue = new Mock<IDataChangeMonitoredItemQueue>();
                queue.SetupGet(value => value.QueueSize).Returns(10);
                var queueFactory = new Mock<IMonitoredItemQueueFactory>();
                queueFactory.Setup(value => value.CreateDataChangeQueue(false, It.IsAny<uint>()))
                    .Returns(queue.Object);
                server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queueFactory.Object);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, new Mock<IAsyncNodeManager>().Object, new NodeHandle(), new NodeId("Dispose", 1));
                bool queueDisposedInCallback = false;
                var current = new TestNodeManagerLifecycle
                {
                    DetachCallback = _ =>
                    {
                        if (disposeDuringRelease)
                        {
                            DisposeInCallback();
                        }
                        return ServiceResult.Good;
                    }
                };
                var replacement = new TestNodeManagerLifecycle
                {
                    AttachCallback = _ =>
                    {
                        DisposeInCallback();
                        return ServiceResult.Good;
                    }
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, replacement, [item], [], static _ => true);

                Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);
                Assert.Multiple(() =>
                {
                    Assert.That(queueDisposedInCallback, Is.True);
                    Assert.That(replacement.AttachCount, Is.EqualTo(disposeDuringRelease ? 0 : 1));
                    Assert.That(replacement.DetachCount, Is.EqualTo(disposeDuringRelease ? 0 : 1));
                });
                queue.Verify(value => value.Dispose(), Times.Once);
                queue.Verify(value => value.Enqueue(It.IsAny<DataValue>(), It.IsAny<ServiceResult>()), Times.Never);
                queue.Verify(value => value.OverwriteLastValue(It.IsAny<DataValue>(), It.IsAny<ServiceResult>()),
                    Times.Never);

                void DisposeInCallback()
                {
                    item.Dispose();
                    queueDisposedInCallback = queue.Invocations.Any(
                        value => value.Method.Name == nameof(IDisposable.Dispose));
                    item.QueueValue(new DataValue(new Variant(123)), null, ignoreFilters: true);
                }
            }
        }

        /// <summary>
        /// Client disposal inside native adoption rolls back registrations before clearing the disposed binding.
        /// </summary>
        [Test]
        public async Task DisposedNativeAdoptionRollsBackItsPartialRegistrationAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var nodeManager = new Mock<IAsyncNodeManager>().Object;
                using var manager = new MonitoredNodeMonitoredItemManager(nodeManager, server.Object);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("DisposeDuringNativeAdoption", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, nodeManager, new NodeHandle(node.NodeId, node), node.NodeId);
                var binding = (IMonitoredItemGenerationBinding)item;
                var native = (IMonitoredItemSourceManager)manager;
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(new OperationContext(item));
                int cacheRemovals = 0;
                var current = new TestNodeManagerLifecycle
                {
                    DetachCallback = _ =>
                    {
                        binding.ReleaseSource();
                        return ServiceResult.Good;
                    }
                };
                var replacement = new TestNodeManagerLifecycle
                {
                    AttachCallback = _ => native.AdoptSource(
                        context,
                        new NodeHandle(node.NodeId, node),
                        item,
                        (_, _, value) =>
                        {
                            item.Dispose();
                            return value;
                        },
                        (_, _) => cacheRemovals++).Result
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, replacement, [item], [], static _ => true);

                Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);
                Assert.Multiple(() =>
                {
                    Assert.That(current.DetachCount, Is.EqualTo(1));
                    Assert.That(replacement.AttachCount, Is.EqualTo(1));
                    Assert.That(manager.MonitoredItems, Is.Empty);
                    Assert.That(manager.MonitoredNodes, Is.Empty);
                    Assert.That(cacheRemovals, Is.EqualTo(1));
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(item.ManagerHandle, Is.Null);
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDisposed, Is.True);
                });
            }
        }

        /// <summary>
        /// A provider callback finishing after subscription removal must release the source it registered late.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode, false, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, false, true)]
        [TestCase(ManagerPath.AsyncMonitoredNode, true, false)]
        [TestCase(ManagerPath.AsyncMonitoredNode, true, true)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, false, true)]
        [TestCase(ManagerPath.AsyncSamplingGroup, true, false)]
        [TestCase(ManagerPath.AsyncSamplingGroup, true, true)]
        public async Task RemovedEventAdoptionReleasesLateProviderRegistrationAsync(
            ManagerPath path, bool dispose, bool failCleanup)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var source = new BaseObjectState(null)
                {
                    NodeId = new NodeId("DeletedEventAdoption", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("DeletedEventAdoption", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                source.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(source).ConfigureAwait(false);
                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), source.NodeId);
                var subscription = new Mock<ISubscription>().Object;
                item.SubscriptionCallback = subscription;
                var generationBinding = (IMonitoredItemGenerationBinding)item;
                generationBinding.BindSubscription(subscription);
                var binding = (IMonitoredItemGenerationBinding)item;
                var current = new TestNodeManagerLifecycle
                {
                    DetachCallback = _ =>
                    {
                        binding.ReleaseSource();
                        return ServiceResult.Good;
                    }
                };
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cleanupFailure = new InvalidOperationException("Injected late-registration cleanup failure.");
                bool providerRegistered = false;
                int unsubscribeCalls = 0;
                owner.AsyncManager.EventSubscriptionCallback = async (unsubscribe, ct) =>
                {
                    if (!unsubscribe)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        providerRegistered = true;
                    }
                    else
                    {
                        if (++unsubscribeCalls == 2 && failCleanup)
                        {
                            throw cleanupFailure;
                        }
                        providerRegistered = false;
                    }
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, owner.Lifecycle, [item], [], static _ => true);
                Task<List<Exception>> adoption = transition.AdoptAsync(timeout.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    using var context = new OperationContext(item);
                    ServiceResult deletion = await owner.AsyncManager.SubscribeToEventsAsync(
                        context, item.ManagerHandle!, item.SubscriptionId, item, true, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(deletion.StatusCode, Is.EqualTo(StatusCodes.Good));
                    if (dispose)
                    {
                        item.Dispose();
                    }
                    else
                    {
                        generationBinding.UnbindSubscription(subscription);
                    }
                    release.TrySetResult(true);
                    List<Exception> failures = await adoption.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(unsubscribeCalls, Is.EqualTo(2));
                        Assert.That(providerRegistered, Is.EqualTo(failCleanup));
                        Assert.That(failures, Has.Count.EqualTo(failCleanup ? 1 : 0));
                        Assert.That(generationBinding.IsDisposed, Is.EqualTo(dispose));
                    });
                    if (failCleanup)
                    {
                        Assert.That(failures[0], Is.TypeOf<AggregateException>());
                        Assert.That(((AggregateException)failures[0]).Flatten().InnerExceptions,
                            Does.Contain(cleanupFailure));
                    }
                    Assert.That(owner.CaptureMonitoredItems(), Is.Empty);
                }
                finally
                {
                    release.TrySetResult(true);
                    await adoption.WaitAsync(timeout.Token).ConfigureAwait(false);
                    owner.AsyncManager.EventSubscriptionCallback = static (_, _) => default;
                }
            }
        }

        /// <summary>
        /// Source release failures preserve ownership and leave adoption retryable without an item reservation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReleaseFailurePreservesSourceOwnershipForSuccessfulRetryAsync(bool throws)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (MonitoredItem item = CreateMonitoredItem(
                server.Object, new Mock<IAsyncNodeManager>().Object, new object(), new NodeId("ReleaseFailure", 1)))
            {
                IAsyncNodeManager originalOwner = item.NodeManager!;
                object originalHandle = item.ManagerHandle!;
                var current = new TestNodeManagerLifecycle
                {
                    DetachCallback = _ => throws
                        ? throw new InvalidOperationException("Injected release failure.")
                        : new ServiceResult(StatusCodes.BadInvalidState)
                };
                var replacement = new TestNodeManagerLifecycle();
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current, replacement, [item], [], static _ => true);

                Exception failure = await Assert.CatchAsync<Exception>(
                    async () => await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(failure, Is.TypeOf(
                        throws ? typeof(InvalidOperationException) : typeof(ServiceResultException)));
                    Assert.That(item.NodeManager, Is.SameAs(originalOwner));
                    Assert.That(item.ManagerHandle, Is.SameAs(originalHandle));
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDisposed, Is.False);
                    Assert.That(replacement.AttachCount, Is.Zero);
                });
                current.DetachCallback = _ => ServiceResult.Good;
                Assert.That(await transition.AdoptAsync(CancellationToken.None).ConfigureAwait(false), Is.Empty);
                Assert.That(current.DetachCount, Is.EqualTo(2));
                Assert.That(replacement.AttachCount, Is.EqualTo(1));
            }
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
                    current,
                    replacement,
                    compatibleItems: [item],
                    deletedItems: [],
                    isOwnedBySubscription: static _ => true);

                List<Exception> failures = await transition
                    .AdoptAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                transition.MarkDeletedItems();

                Assert.Multiple(() =>
                {
                    Assert.That(failures, Has.Count.EqualTo(expectedFailureCount));
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsSourceUnavailable, Is.True);
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDeleted, Is.True);
                });
            }
        }

        [Test]
        public async Task TransitionWithoutSubscriptionOwnershipSkipsAdoptionAsync()
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
                current,
                replacement,
                compatibleItems: [item.Object],
                deletedItems: []);

            Assert.That(
                await transition
                    .AdoptAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                Is.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(replacement.AttachCount, Is.Zero);
                Assert.That(current.DetachCount, Is.Zero);
                Assert.That(current.AttachCount, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EventRegistrationRejectsConflictingInstancesWithoutRemovingTheOwner(bool anotherSource)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var manager = new MonitoredNodeMonitoredItemManager(nodeManager.Object, server.Object);
                var source = new BaseObjectState(null)
                {
                    NodeId = new NodeId("EventOwner", 1),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                BaseObjectState target = anotherSource ? new BaseObjectState(null)
                {
                    NodeId = new NodeId("ConflictingSource", 1),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                } : source;
                using MonitoredItem owner = CreateEventMonitoredItem(
                    server.Object, nodeManager.Object, new object(), source.NodeId);
                using MonitoredItem conflicting = CreateEventMonitoredItem(
                    server.Object, nodeManager.Object, new object(), target.NodeId);
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(new OperationContext(owner));
                (MonitoredNode2? registered, ServiceResult first) = manager.SubscribeToEvents(
                    context, source, owner, unsubscribe: false);
                MonitoredNode2 registeredNode = registered ??
                    throw new AssertionException("The first event subscription did not register its monitored node.");

                (_, ServiceResult rejected) = manager.SubscribeToEvents(
                    context, target, conflicting, unsubscribe: false);
                (_, ServiceResult rejectedCleanup) = manager.SubscribeToEvents(
                    context, target, conflicting, unsubscribe: true);

                Assert.Multiple(() =>
                {
                    Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                    Assert.That(rejectedCleanup.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                    Assert.That(manager.MonitoredItems[owner.Id], Is.SameAs(owner));
                    Assert.That(registeredNode.EventMonitoredItems[owner.Id], Is.SameAs(owner));
                });
            }
        }

        [Test]
        public void SharedEventRegistrationRemainsOwnedUntilItsLastRootUnsubscribes()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var manager = new MonitoredNodeMonitoredItemManager(nodeManager.Object, server.Object);
                var first = new BaseObjectState(null)
                {
                    NodeId = new NodeId("FirstRoot", 1),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                var second = new BaseObjectState(null)
                {
                    NodeId = new NodeId("SecondRoot", 1),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object, nodeManager.Object, new object(), ObjectIds.Server);
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(new OperationContext(item));
                Assert.That(manager.SubscribeToEvents(context, first, item, false).Item2.StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.SubscribeToEvents(context, second, item, false).Item2.StatusCode,
                    Is.EqualTo(StatusCodes.Good));

                manager.SubscribeToEvents(context, first, item, true);
                Assert.That(manager.MonitoredItems.TryGetValue(item.Id, out IMonitoredItem? remaining), Is.True);
                Assert.That(remaining, Is.SameAs(item));

                manager.SubscribeToEvents(context, second, item, true);
                Assert.That(manager.MonitoredItems, Is.Empty);
            }
        }

        /// <summary>
        /// Removing one native all-events root preserves the identity and queues used by another root.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ClientEventDeletionPreservesOtherRootSubscriptions(bool useSamplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var owner = Mock.Of<IAsyncNodeManager>();
                using var sampling = new TrackingSamplingGroupManager(server.Object, owner);
                using IMonitoredItemManager manager = useSamplingGroups
                    ? new SamplingGroupMonitoredItemManager(owner, server.Object, sampling)
                    : new MonitoredNodeMonitoredItemManager(owner, server.Object);
                var first = new BaseObjectState(null)
                {
                    NodeId = new NodeId("FirstDeletionRoot", 1),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                var second = new BaseObjectState(null)
                {
                    NodeId = new NodeId("SecondDeletionRoot", 1),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object, owner, new object(), ObjectIds.Server);
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(new OperationContext(item));
                Assert.That(manager.SubscribeToEvents(context, first, item, false).Item2.StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.SubscribeToEvents(context, second, item, false).Item2.StatusCode,
                    Is.EqualTo(StatusCodes.Good));

                Assert.That(manager.DeleteMonitoredItem(context, item, new NodeHandle(first.NodeId, first)),
                    Is.EqualTo(StatusCodes.Good));
                Assert.Multiple(() =>
                {
                    Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                    Assert.That(manager.MonitoredNodes.ContainsKey(first.NodeId), Is.False);
                    Assert.That(manager.MonitoredNodes[second.NodeId].EventMonitoredItems[item.Id], Is.SameAs(item));
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDisposed, Is.False);
                });

                Assert.That(manager.DeleteMonitoredItem(context, item, new NodeHandle(second.NodeId, second)),
                    Is.EqualTo(StatusCodes.Good));
                Assert.Multiple(() =>
                {
                    Assert.That(manager.MonitoredItems, Is.Empty);
                    Assert.That(manager.MonitoredNodes, Is.Empty);
                    Assert.That(sampling.StopCount, Is.Zero);
                });
            }
        }

        /// <summary>
        /// Client deletion shares exact native ownership checks without entering source-release state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ClientDeletionRequiresExactRegisteredItemAndPreservesOtherOwners(bool useSamplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var owner = Mock.Of<IAsyncNodeManager>();
                using var sampling = new TrackingSamplingGroupManager(server.Object, owner);
                using IMonitoredItemManager manager = useSamplingGroups
                    ? new SamplingGroupMonitoredItemManager(owner, server.Object, sampling)
                    : new MonitoredNodeMonitoredItemManager(owner, server.Object);
                var source = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("ExactDeletionOwner", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                var handle = new NodeHandle(source.NodeId, source);
                using MonitoredItem item = CreateMonitoredItem(server.Object, owner, handle, source.NodeId);
                using MonitoredItem conflicting = CreateMonitoredItem(server.Object, owner, handle, source.NodeId);
                var registration = (IMonitoredItemSourceManager)manager;
                ((IMonitoredItemGenerationBinding)item).ReleaseSource();
                Assert.That(registration.AdoptSource(
                    server.Object.DefaultSystemContext, handle, item,
                    static (_, _, node) => node, static (_, _) => { }).Result.StatusCode,
                    Is.EqualTo(StatusCodes.Good));

                Assert.That(manager.DeleteMonitoredItem(server.Object.DefaultSystemContext, conflicting, handle),
                    Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                Assert.Multiple(() =>
                {
                    Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsDisposed, Is.False);
                    Assert.That(((IMonitoredItemGenerationBinding)conflicting).IsDisposed, Is.False);
                    Assert.That(sampling.StopCount, Is.Zero);
                });
                Assert.That(manager.DeleteMonitoredItem(server.Object.DefaultSystemContext, item, handle),
                    Is.EqualTo(StatusCodes.Good));
                manager.ApplyChanges();
                Assert.Multiple(() =>
                {
                    Assert.That(manager.MonitoredItems, Is.Empty);
                    Assert.That(manager.MonitoredNodes, Is.Empty);
                    Assert.That(((IMonitoredItemGenerationBinding)item).IsSourceUnavailable, Is.False,
                        "Client deletion must not enter the generation source-release transition.");
                    Assert.That(sampling.StopCount, Is.EqualTo(useSamplingGroups ? 1 : 0));
                });
            }
        }

        /// <summary>
        /// Native source release and adoption cannot consume an earlier incarnation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NativeSourceOperationsRejectStaleIncarnations(bool useSamplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var owner = Mock.Of<IAsyncNodeManager>();
                using var sampling = new TrackingSamplingGroupManager(server.Object, owner);
                using IMonitoredItemManager manager = useSamplingGroups
                    ? new SamplingGroupMonitoredItemManager(owner, server.Object, sampling)
                    : new MonitoredNodeMonitoredItemManager(owner, server.Object);
                var native = (IMonitoredItemSourceManager)manager;
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("StaleNativeSource", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                var handle = new NodeHandle(node.NodeId, node);
                using MonitoredItem item = CreateMonitoredItem(server.Object, owner, handle, node.NodeId);
                var binding = (IMonitoredItemGenerationBinding)item;
                binding.ReleaseSource();
                Assert.That(native.AdoptSource(server.Object.DefaultSystemContext, handle, item,
                    static (_, _, source) => source, static (_, _) => { }).Changed, Is.True);
                long oldRevision = binding.SourceRevision;
                Assert.That(native.ReleaseSource(server.Object.DefaultSystemContext, item,
                    oldRevision, static (_, _) => { }).Changed, Is.True);
                Assert.That(native.AdoptSource(server.Object.DefaultSystemContext, handle, item,
                    static (_, _, source) => source, static (_, _) => { }).Changed, Is.True);
                long currentRevision = binding.SourceRevision;
                (ServiceResult result, bool changed) = native.ReleaseSource(
                    server.Object.DefaultSystemContext, item, oldRevision, static (_, _) => { });
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(changed, Is.False);
                Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                Assert.That(binding.SourceRevision, Is.EqualTo(currentRevision));
                Assert.That(item.ManagerHandle, Is.SameAs(handle));
                Assert.That(native.ReleaseSource(server.Object.DefaultSystemContext, item,
                    currentRevision, static (_, _) => { }).Changed, Is.True);
                (result, changed) = native.AdoptSource(server.Object.DefaultSystemContext, handle, item,
                    static (_, _, source) => source, static (_, _) => { }, oldRevision);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(changed, Is.False);
                Assert.That(manager.MonitoredItems, Is.Empty);
                Assert.That(binding.IsSourceUnavailable, Is.True);
            }
        }

        /// <summary>
        /// A delayed source release cannot remove a replacement installed in the same generation.
        /// </summary>
        [TestCase(ManagerPath.AsyncMonitoredNode)]
        [TestCase(ManagerPath.AsyncSamplingGroup)]
        [TestCase(ManagerPath.SyncAdapterMonitoredNode)]
        [TestCase(ManagerPath.SyncAdapterSamplingGroup)]
        public async Task BaseManagerSourceReleaseCannotRemoveNewerIncarnationAsync(ManagerPath path)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("StaleBaseSource", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                await owner.AddNodeAsync(node).ConfigureAwait(false);
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), node.NodeId);
                var binding = (IMonitoredItemGenerationBinding)item;
                binding.ReleaseSource();
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false)), Is.True);
                long oldRevision = binding.SourceRevision;
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.ReleaseSourceAsync(item, oldRevision).ConfigureAwait(false)), Is.True);
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.AdoptSourceAsync(item).ConfigureAwait(false)), Is.True);
                MonitoredItemSourceSnapshot current = binding.CaptureSource();
                Assert.That(ServiceResult.IsGood(
                    await owner.Lifecycle.ReleaseSourceAsync(item, oldRevision).ConfigureAwait(false)), Is.True);
                Assert.That(owner.CaptureMonitoredItems(), Is.EqualTo(new[] { item }));
                Assert.That(item.ManagerHandle, Is.SameAs(current.Handle));
                Assert.That(item.Generation, Is.SameAs(current.Generation));
                Assert.That(binding.SourceRevision, Is.EqualTo(current.Revision));
                Assert.That(binding.IsDeleted, Is.False);
            }
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
                using TrackingSamplingGroupManager samplingGroups = (useSamplingGroups
                    ? new TrackingSamplingGroupManager(server.Object, nodeManager.Object)
                    : null)!;
                using IMonitoredItemManager manager = useSamplingGroups
                    ? new SamplingGroupMonitoredItemManager(
                        nodeManager.Object,
                        server.Object,
                        samplingGroups!)
                    : new MonitoredNodeMonitoredItemManager(nodeManager.Object, server.Object);
                var lifecycle = (IMonitoredItemSourceManager)manager;
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
                var itemLifecycle = (IMonitoredItemGenerationBinding)item;
                itemLifecycle.ReleaseSource();
                ((IMonitoredItemGenerationBinding)conflictingItem).ReleaseSource();
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(
                    new OperationContext(item));

                (ServiceResult firstAttach, bool firstChanged) = lifecycle.AdoptSource(
                    context,
                    handle,
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });
                (ServiceResult repeatedAttach, bool repeatedChanged) = lifecycle.AdoptSource(
                    context,
                    handle,
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });
                (ServiceResult conflictingAttach, bool conflictingChanged) =
                    lifecycle.AdoptSource(
                        context,
                        new NodeHandle(node.NodeId, node),
                        conflictingItem,
                        static (_, _, nodeToCache) => nodeToCache,
                        static (_, _) => { });

                itemLifecycle.ReleaseSource();
                (ServiceResult detachedExistingAttach, bool detachedExistingChanged) =
                    lifecycle.AdoptSource(
                        context,
                        handle,
                        item,
                        static (_, _, nodeToCache) => nodeToCache,
                        static (_, _) => { });
                itemLifecycle.BindSource(nodeManager.Object, handle);

                (ServiceResult detach, bool detachChanged) = lifecycle.ReleaseSource(
                    context,
                    item,
                    static (_, _) => { });
                (ServiceResult repeatedDetach, bool repeatedDetachChanged) =
                    lifecycle.ReleaseSource(
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
                using TrackingSamplingGroupManager samplingGroups = (useSamplingGroups
                    ? new TrackingSamplingGroupManager(server.Object, nodeManager.Object)
                    : null)!;
                using IMonitoredItemManager manager = useSamplingGroups
                    ? new SamplingGroupMonitoredItemManager(
                        nodeManager.Object,
                        server.Object,
                        samplingGroups!)
                    : new MonitoredNodeMonitoredItemManager(nodeManager.Object, server.Object);
                var lifecycle = (IMonitoredItemSourceManager)manager;
                var unsupported = new Mock<ISampledDataChangeMonitoredItem>();
                unsupported.SetupGet(value => value.Id).Returns(55);
                unsupported.SetupGet(value => value.NodeId).Returns(new NodeId("Unsupported", 1));
                var node = new BaseDataVariableState(null)
                {
                    NodeId = unsupported.Object.NodeId
                };

                (ServiceResult detach, bool detachChanged) = lifecycle.ReleaseSource(
                    server.Object.DefaultSystemContext,
                    unsupported.Object,
                    0,
                    static (_, _) => { });
                (ServiceResult attach, bool attachChanged) = lifecycle.AdoptSource(
                    server.Object.DefaultSystemContext,
                    new NodeHandle(node.NodeId, node),
                    unsupported.Object,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { },
                    0);

                Assert.Multiple(() =>
                {
                    Assert.That(detach.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                    Assert.That(detachChanged, Is.False);
                    Assert.That(attach.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                    Assert.That(attachChanged, Is.False);
                    Assert.That(manager.CaptureMonitoredItems([]), Is.Empty);
                });
            }
        }

        /// <summary>
        /// Membership capture needs only the ordinary manager registry and never retains a live collection.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void MembershipCaptureFiltersAndPreservesExactNativeItems(int selection)
        {
            var first = new Mock<IMonitoredItem>();
            first.SetupGet(value => value.NodeId).Returns(new NodeId("FirstCapturedItem", 1));
            var second = new Mock<IMonitoredItem>();
            second.SetupGet(value => value.NodeId).Returns(new NodeId("SecondCapturedItem", 1));
            var membership = new ConcurrentDictionary<uint, IMonitoredItem>();
            membership[1] = first.Object;
            membership[2] = second.Object;
            var manager = new Mock<IMonitoredItemManager>(MockBehavior.Strict);
            manager.SetupGet(value => value.MonitoredItems).Returns(membership);
            IReadOnlyCollection<NodeId>? nodeIds = selection switch
            {
                0 => null,
                1 => [],
                2 => [first.Object.NodeId, first.Object.NodeId, new NodeId("MissingCapturedItem", 1)],
                _ => [new NodeId("MissingCapturedItem", 1)]
            };
            IReadOnlyList<IMonitoredItem> expected = selection switch
            {
                0 => [first.Object, second.Object],
                2 => [first.Object],
                _ => []
            };

            IReadOnlyList<IMonitoredItem> snapshot = manager.Object.CaptureMonitoredItems(nodeIds);
            manager.VerifyGet(value => value.MonitoredItems, selection == 1 ? Times.Never() : Times.Once());
            membership.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot, Is.EquivalentTo(expected));
                Assert.That(manager.Object.CaptureMonitoredItems(null), Is.Empty);
            });
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
                var lifecycle = (IMonitoredItemSourceManager)manager;
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
                ((IMonitoredItemGenerationBinding)existingItem).ReleaseSource();
                var existingHandle = new NodeHandle(cachedNode.NodeId, cachedNode);
                Assert.That(
                    lifecycle.AdoptSource(
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
                ((IMonitoredItemGenerationBinding)conflictingItem).ReleaseSource();
                var conflictingHandle = new NodeHandle(freshNode.NodeId, freshNode);
                int removeCacheCount = 0;

                (ServiceResult result, bool changed) = lifecycle.AdoptSource(
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
                        manager.CaptureMonitoredItems(null),
                        Is.EqualTo([existingItem]));
                });

                lifecycle.ReleaseSource(
                    server.Object.DefaultSystemContext,
                    existingItem,
                    static (_, _) => { });
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SamplingGroupAttachApplyFailureStopsMonitoringAndRestoresDetachedOwnership(bool failCleanup)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var samplingGroups = new TrackingSamplingGroupManager(
                    server.Object,
                    nodeManager.Object);
                var failure = new InvalidOperationException("Injected apply failure.");
                var cleanupFailure = new InvalidOperationException("Injected cleanup apply failure.");
                samplingGroups.ApplyFailures.Enqueue(failure);
                if (failCleanup)
                {
                    samplingGroups.ApplyFailures.Enqueue(cleanupFailure);
                }
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object,
                    server.Object,
                    samplingGroups);
                var lifecycle = (IMonitoredItemSourceManager)manager;
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
                var itemLifecycle = (IMonitoredItemGenerationBinding)item;
                itemLifecycle.ReleaseSource();

                void Attach()
                {
                    lifecycle.AdoptSource(
                        server.Object.DefaultSystemContext,
                        new NodeHandle(node.NodeId, node),
                        item,
                        static (_, _, nodeToCache) => nodeToCache,
                        static (_, _) => { });
                }
                if (failCleanup)
                {
                    AggregateException error = Assert.Throws<AggregateException>(Attach);
                    Assert.That(error.InnerExceptions, Is.EqualTo(new Exception[] { failure, cleanupFailure }));
                }
                else
                {
                    InvalidOperationException error = Assert.Throws<InvalidOperationException>(Attach);
                    Assert.That(error, Is.SameAs(failure));
                }

                Assert.Multiple(() =>
                {
                    Assert.That(samplingGroups.StartCount, Is.EqualTo(1));
                    Assert.That(samplingGroups.StopCount, Is.EqualTo(1));
                    Assert.That(samplingGroups.ApplyCount, Is.EqualTo(2));
                    Assert.That(manager.MonitoredItems, Is.Empty);
                    Assert.That(itemLifecycle.IsSourceUnavailable, Is.True);
                    Assert.That(item.ManagerHandle, Is.Null);
                    Assert.That(
                        item.NodeManager,
                        Is.Null);
                });

                (ServiceResult result, bool changed) = lifecycle.AdoptSource(
                    server.Object.DefaultSystemContext,
                    new NodeHandle(node.NodeId, node),
                    item,
                    static (_, _, nodeToCache) => nodeToCache,
                    static (_, _) => { });
                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(result), Is.True);
                    Assert.That(changed, Is.True);
                    Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                    Assert.That(item.NodeManager, Is.SameAs(nodeManager.Object));
                    Assert.That(itemLifecycle.IsSourceUnavailable, Is.False);
                    Assert.That(samplingGroups.StartCount, Is.EqualTo(2));
                    Assert.That(samplingGroups.ApplyCount, Is.EqualTo(3));
                });
                lifecycle.ReleaseSource(
                    server.Object.DefaultSystemContext,
                    item,
                    static (_, _) => { });
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void SamplingGroupSourceReleasePreservesOwnershipUntilSamplingChangesCommit(
            bool eventItem,
            bool failRestoration)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using var samplingGroups = new TrackingSamplingGroupManager(server.Object, nodeManager.Object);
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object, server.Object, samplingGroups);
                NodeState node = eventItem
                    ? new BaseObjectState(null)
                    {
                        NodeId = new NodeId("ReleaseCommit", 1),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    }
                    : new BaseDataVariableState(null)
                    {
                        NodeId = new NodeId("ReleaseCommit", 1),
                        DataType = DataTypeIds.Int32,
                        Value = 1
                    };
                using MonitoredItem item = eventItem
                    ? CreateEventMonitoredItem(server.Object, nodeManager.Object, new object(), node.NodeId)
                    : CreateMonitoredItem(server.Object, nodeManager.Object, new object(), node.NodeId);
                var state = (IMonitoredItemGenerationBinding)item;
                state.ReleaseSource();
                var lifecycle = (IMonitoredItemSourceManager)manager;
                var handle = new NodeHandle(node.NodeId, node);
                var failure = new InvalidOperationException("Injected release flush failure.");
                if (eventItem)
                {
                    samplingGroups.ApplyFailures.Enqueue(failure);
                }
                Assert.That(lifecycle.AdoptSource(
                    server.Object.DefaultSystemContext, handle, item,
                    static (_, _, nodeToCache) => nodeToCache, static (_, _) => { }).Result.StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                var subscription = new Mock<ISubscription>();
                item.SubscriptionCallback = subscription.Object;
                ((IMonitoredItemGenerationBinding)item).BindSubscription(subscription.Object);
                Assert.That(item.Generation!.TryGetGeneration(out NodeManagerGeneration? generation), Is.True);
                int applyCount = samplingGroups.ApplyCount;
                var restorationFailure = new InvalidOperationException("Injected restoration flush failure.");
                if (eventItem)
                {
                    Assert.That(applyCount, Is.Zero);
                }
                else
                {
                    samplingGroups.ApplyFailures.Enqueue(failure);
                }
                if (failRestoration)
                {
                    samplingGroups.ApplyFailures.Enqueue(restorationFailure);
                }

                if (!eventItem)
                {
                    if (failRestoration)
                    {
                        AggregateException aggregate = Assert.Throws<AggregateException>(() =>
                            lifecycle.ReleaseSource(
                                server.Object.DefaultSystemContext, item, static (_, _) => { }));
                        Assert.That(aggregate.InnerExceptions,
                            Is.EqualTo(new Exception[] { failure, restorationFailure }));
                    }
                    else
                    {
                        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                            lifecycle.ReleaseSource(
                                server.Object.DefaultSystemContext, item, static (_, _) => { }));
                        Assert.That(error, Is.SameAs(failure));
                    }
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                        Assert.That(state.IsSourceUnavailable, Is.False);
                        Assert.That(item.ManagerHandle, Is.SameAs(handle));
                        Assert.That(generation!.MonitoredItems.ContainsKey(item), Is.True);
                        Assert.That(samplingGroups.StartCount, Is.EqualTo(2));
                    }
                }

                (ServiceResult result, bool changed) = lifecycle.ReleaseSource(
                    server.Object.DefaultSystemContext, item, static (_, _) => { });
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(changed, Is.True);
                    Assert.That(manager.MonitoredItems, Is.Empty);
                    Assert.That(manager.MonitoredNodes, Is.Empty);
                    Assert.That(state.IsSourceUnavailable, Is.True);
                    Assert.That(samplingGroups.ApplyCount, Is.EqualTo(applyCount + (eventItem ? 0 : 3)));
                    Assert.That(samplingGroups.ApplyFailures, Has.Count.EqualTo(eventItem ? 1 : 0));
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RealSamplingResumesAfterReleaseFlushFailureAsync(
            bool failAfterApply,
            bool failRestoration)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var releaseRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager.Setup(value => value.ReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<double>(), It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<IList<DataValue>>(), It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                    .Returns<OperationContext, double, ArrayOf<ReadValueId>, IList<DataValue>,
                        IList<ServiceResult>, CancellationToken>((_, _, reads, values, errors, ct) =>
                            new ValueTask(ReadAsync(reads, values, errors, ct)));
                nodeManager.Setup(value => value.ValidateRolePermissionsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<NodeId>(), It.IsAny<PermissionType>(),
                    It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                using var samplingGroups = new TrackingSamplingGroupManager(
                    server.Object, nodeManager.Object, forwardToBase: true);
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object, server.Object, samplingGroups);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("RealReleaseCommit", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                using MonitoredItem item = CreateMonitoredItem(
                    server.Object, nodeManager.Object, new object(), node.NodeId);
                ISession session = item.Session;
                IUserIdentity identity = item.EffectiveIdentity;
                var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var subscription = new Mock<ISubscription>();
                subscription.SetupGet(value => value.Session).Returns(session);
                subscription.SetupGet(value => value.EffectiveIdentity).Returns(identity);
                subscription.As<IMonitoredItemReadySink>()
                    .Setup(value => value.OnMonitoredItemReady(item))
                    .Callback(() => queued.TrySetResult(true));
                item.SubscriptionCallback = subscription.Object;
                var state = (IMonitoredItemGenerationBinding)item;
                state.ReleaseSource();
                samplingGroups.MarkInitialValueQueued(item);
                var lifecycle = (IMonitoredItemSourceManager)manager;
                Assert.That(lifecycle.AdoptSource(
                    server.Object.DefaultSystemContext, new NodeHandle(node.NodeId, node), item,
                    static (_, _, nodeToCache) => nodeToCache, static (_, _) => { }).Result.StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                ((IMonitoredItemGenerationBinding)item).BindSubscription(subscription.Object);
                var failure = new InvalidOperationException("Injected real sampling flush failure.");
                var restorationFailure = new InvalidOperationException("Injected real sampling restoration failure.");
                samplingGroups.ApplyFailures.Enqueue(failure);
                if (failRestoration)
                {
                    samplingGroups.ApplyFailures.Enqueue(restorationFailure);
                }
                samplingGroups.FailAfterApply = failAfterApply;

                if (failRestoration)
                {
                    AggregateException aggregate = Assert.Throws<AggregateException>(() =>
                        lifecycle.ReleaseSource(
                            server.Object.DefaultSystemContext, item, static (_, _) => { }));
                    Assert.That(aggregate.InnerExceptions,
                        Is.EqualTo(new Exception[] { failure, restorationFailure }));
                }
                else
                {
                    InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                        lifecycle.ReleaseSource(
                            server.Object.DefaultSystemContext, item, static (_, _) => { }));
                    Assert.That(error, Is.SameAs(failure));
                }
                Assert.That(state.IsSourceUnavailable, Is.False);
                item.SetSamplingInterval(2000);
                using (var modification = new OperationContext(item))
                {
                    samplingGroups.ModifyMonitoring(modification, item);
                    samplingGroups.ApplyChanges();
                }
                releaseRead.TrySetResult(true);
                await queued.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Publish, RequestLifetime.None);
                var notifications = new Queue<MonitoredItemNotification>();
                item.SetupResendDataTrigger();
                item.Publish(context, notifications, new Queue<DiagnosticInfo>(), 100, server.Object.Telemetry
                    .CreateLogger<MonitoredItemManagerLifecycleTests>());
                Assert.That(notifications, Has.Count.EqualTo(1));
                DataValue value = notifications.Dequeue().Value;
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out int actual), Is.True);
                Assert.That(actual, Is.EqualTo(73));

                (ServiceResult result, bool changed) = lifecycle.ReleaseSource(
                    server.Object.DefaultSystemContext, item, static (_, _) => { });
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(changed, Is.True);
                Assert.That(manager.MonitoredItems, Is.Empty);

                async Task ReadAsync(
                    ArrayOf<ReadValueId> reads,
                    IList<DataValue> values,
                    IList<ServiceResult> errors,
                    CancellationToken ct)
                {
                    Assert.That(reads, Has.Count.EqualTo(1));
                    await releaseRead.Task.WaitAsync(ct).ConfigureAwait(false);
                    values[0] = new DataValue(new Variant(73), StatusCodes.Good);
                    errors[0] = ServiceResult.Good;
                }
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
                var binding = (IMonitoredItemGenerationBinding)source;
                binding.Invalidate(terminal: false);
                Assert.That(binding.TryClearSource(binding.SourceRevision), Is.True);
                IStoredMonitoredItem stored = source.ToStorableMonitoredItem();
                var node = new BaseDataVariableState(null)
                {
                    NodeId = stored.NodeId,
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                var handle = new NodeHandle(node.NodeId, node);
                IMonitoredItemManager manager;
                TrackingSamplingGroupManager? samplingGroups = null;
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
                        Assert.That(((IMonitoredItemGenerationBinding)item).IsSourceUnavailable, Is.False);
                        Assert.That(item.NodeManager, Is.SameAs(nodeManager.Object));
                        Assert.That(item.ManagerHandle, Is.SameAs(handle));
                        Assert.That(manager.MonitoredItems[item.Id], Is.SameAs(item));
                    });

                    ((IMonitoredItemSourceManager)manager).ReleaseSource(
                        server.Object.DefaultSystemContext,
                        item,
                        static (_, _) => { });
                }

                samplingGroups?.Dispose();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CustomAndRestoredItemsAcquireIndependentComponentReferences(bool restore)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var owner = new Mock<IAsyncNodeManager>();
                using var manager = new MonitoredNodeMonitoredItemManager(owner.Object, server.Object);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("SharedComponent", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                int references = 0;
                NodeState AddReference(ISystemContext context, NodeHandle handle, NodeState value)
                {
                    references++;
                    return value;
                }
                void RemoveReference(ISystemContext context, NodeHandle handle) => references--;

                var items = new List<ISampledDataChangeMonitoredItem>();
                var ids = new MonitoredItemIdFactory();
                for (uint id = 1; id <= 2; id++)
                {
                    var handle = new NodeHandle(node.NodeId, node);
                    if (restore)
                    {
                        StoredMonitoredItem stored = CreateStoredMonitoredItem(id, node.NodeId);
                        stored.IsDurable = false;
                        Assert.That(manager.RestoreMonitoredItem(
                            server.Object, owner.Object, server.Object.DefaultSystemContext,
                            handle, stored, new Mock<IUserIdentity>().Object, AddReference,
                            out ISampledDataChangeMonitoredItem item), Is.True);
                        items.Add(item);
                    }
                    else
                    {
                        items.Add(((ICustomMonitoredItemManager)manager).CreateCustomMonitoredItem(
                            server.Object,
                            owner.Object,
                            server.Object.DefaultSystemContext,
                            handle,
                            1,
                            1000,
                            DiagnosticsMasks.None,
                            TimestampsToReturn.Both,
                            new MonitoredItemCreateRequest
                            {
                                ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                                MonitoringMode = MonitoringMode.Reporting,
                                RequestedParameters = new MonitoringParameters { ClientHandle = 3, QueueSize = 10 }
                            },
                            null!,
                            null!,
                            1000,
                            10,
                            false,
                            ids,
                            AddReference,
                            RemoveReference,
                            context => CreateMonitoredItem(
                                context.Server, context.NodeManager, context.Handle,
                                context.Handle.NodeId, context.MonitoredItemId),
                            initialValueQueued: false));
                    }
                }

                try
                {
                    Assert.That(references, Is.EqualTo(2));
                    var lifecycle = (IMonitoredItemSourceManager)manager;
                    Assert.That(ServiceResult.IsGood(lifecycle.ReleaseSource(
                        server.Object.DefaultSystemContext, items[0], RemoveReference).Result), Is.True);
                    Assert.That(references, Is.EqualTo(1));
                    Assert.That(
                        manager.MonitoredNodes[node.NodeId].DataChangeMonitoredItems.ContainsKey(items[1].Id), Is.True);
                    Assert.That(ServiceResult.IsGood(lifecycle.ReleaseSource(
                        server.Object.DefaultSystemContext, items[1], RemoveReference).Result), Is.True);
                    Assert.That(references, Is.Zero);
                    Assert.That(manager.MonitoredNodes, Is.Empty);
                }
                finally
                {
                    foreach (ISampledDataChangeMonitoredItem item in items)
                    {
                        item.Dispose();
                    }
                }
            }
        }

        private static async Task AssertStaleEventTransitionPreservesReplacementAsync(
            ManagerPath path,
            bool failAttachment)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var source = new BaseObjectState(null)
                {
                    NodeId = new NodeId("ReattachedEventSource", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("ReattachedEventSource", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                source.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(source).ConfigureAwait(false);
                using MonitoredItem item = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), source.NodeId);
                var lifecycle = (IMonitoredItemGenerationBinding)item;
                lifecycle.ReleaseSource();
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var failure = new InvalidOperationException("Original attachment readiness failed.");
                int subscribeCalls = 0;
                int unsubscribeCalls = 0;
                owner.AsyncManager.EventSubscriptionCallback = async (unsubscribe, ct) =>
                {
                    int call = unsubscribe
                        ? Interlocked.Increment(ref unsubscribeCalls)
                        : Interlocked.Increment(ref subscribeCalls);
                    if (call == 1 && unsubscribe != failAttachment)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        if (failAttachment)
                        {
                            throw failure;
                        }
                    }
                };
                var transitions = new List<Task<ServiceResult>>();
                try
                {
                    if (!failAttachment)
                    {
                        Task<ServiceResult> initial = owner.Lifecycle.AdoptSourceAsync(item).AsTask();
                        transitions.Add(initial);
                        ServiceResult result = await initial.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    }

                    object? originalHandle = item.ManagerHandle;
                    Task<ServiceResult> original = failAttachment
                        ? owner.Lifecycle.AdoptSourceAsync(item).AsTask()
                        : owner.Lifecycle.ReleaseSourceAsync(item).AsTask();
                    transitions.Add(original);
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (failAttachment)
                    {
                        originalHandle = item.ManagerHandle;
                    }
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(original.IsCompleted, Is.False);
                        Assert.That(originalHandle, Is.TypeOf<NodeHandle>());
                        Assert.That(source.AreEventsMonitored, Is.EqualTo(failAttachment));
                        Assert.That(Volatile.Read(ref subscribeCalls), Is.EqualTo(1));
                        Assert.That(Volatile.Read(ref unsubscribeCalls), Is.EqualTo(failAttachment ? 0 : 1));
                    }
                    if (!failAttachment)
                    {
                        MonitoredItemSourceSnapshot released =
                            ((IMonitoredItemGenerationBinding)item).CaptureSource();
                        using (Assert.EnterMultipleScope())
                        {
                            Assert.That(item.ManagerHandle, Is.Null);
                            Assert.That(item.NodeManager, Is.Null);
                            Assert.That(released.Generation, Is.Null);
                            Assert.That(released.Handle, Is.Null);
                            Assert.That(owner.CaptureMonitoredItems(), Is.Empty);
                        }
                    }
                    if (failAttachment)
                    {
                        Task<ServiceResult> detach = owner.Lifecycle.ReleaseSourceAsync(item).AsTask();
                        transitions.Add(detach);
                        ServiceResult result = await detach.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        using (Assert.EnterMultipleScope())
                        {
                            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                            Assert.That(lifecycle.IsSourceUnavailable, Is.True);
                            Assert.That(source.AreEventsMonitored, Is.False);
                        }
                    }

                    Task<ServiceResult> replacement = owner.Lifecycle.AdoptSourceAsync(item).AsTask();
                    transitions.Add(replacement);
                    ServiceResult attached = await replacement.WaitAsync(TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);
                    object? replacementHandle = item.ManagerHandle;
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(attached.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(replacementHandle, Is.TypeOf<NodeHandle>());
                        Assert.That(replacementHandle, Is.Not.SameAs(originalHandle));
                        Assert.That(item.NodeManager, Is.SameAs(owner.AsyncManager));
                        Assert.That(lifecycle.IsSourceUnavailable, Is.False);
                        Assert.That(source.AreEventsMonitored, Is.True);
                        Assert.That(original.IsCompleted, Is.False);
                        Assert.That(release.Task.IsCompleted, Is.False);
                        Assert.That(owner.AsyncManager.DetachedNotificationCount,
                            Is.EqualTo(failAttachment ? 1 : 0));
                    }

                    release.TrySetResult(true);
                    if (failAttachment)
                    {
                        InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(
                            async () => await original.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)))!;
                        Assert.That(error, Is.SameAs(failure));
                    }
                    else
                    {
                        ServiceResult detached = await original.WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                        Assert.That(detached.StatusCode, Is.EqualTo(StatusCodes.Good));
                    }
                    IReadOnlyList<IMonitoredItem> snapshot = owner.CaptureMonitoredItems();
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(snapshot, Has.Count.EqualTo(1));
                        Assert.That(snapshot[0], Is.SameAs(item));
                        Assert.That(item.NodeManager, Is.SameAs(owner.AsyncManager));
                        Assert.That(item.ManagerHandle, Is.SameAs(replacementHandle));
                        Assert.That(lifecycle.IsSourceUnavailable, Is.False);
                        Assert.That(lifecycle.IsDeleted, Is.False);
                        Assert.That(source.AreEventsMonitored, Is.True);
                        Assert.That(Volatile.Read(ref subscribeCalls), Is.EqualTo(2));
                        Assert.That(Volatile.Read(ref unsubscribeCalls), Is.EqualTo(1));
                        Assert.That(owner.AsyncManager.DetachedNotificationCount,
                            Is.EqualTo(failAttachment ? 1 : 0));
                    }

                    Task<ServiceResult> finalDetach = owner.Lifecycle.ReleaseSourceAsync(item).AsTask();
                    transitions.Add(finalDetach);
                    ServiceResult finalResult = await finalDetach.WaitAsync(TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);
                    snapshot = owner.CaptureMonitoredItems();
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(finalResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(snapshot, Is.Empty);
                        Assert.That(lifecycle.IsSourceUnavailable, Is.True);
                        Assert.That(item.NodeManager, Is.Null);
                        Assert.That(item.ManagerHandle, Is.Null);
                        Assert.That(source.AreEventsMonitored, Is.False);
                        Assert.That(Volatile.Read(ref subscribeCalls), Is.EqualTo(2));
                        Assert.That(Volatile.Read(ref unsubscribeCalls), Is.EqualTo(2));
                        Assert.That(owner.AsyncManager.DetachedNotificationCount,
                            Is.EqualTo(failAttachment ? 2 : 1));
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                    foreach (Task<ServiceResult> transition in transitions)
                    {
                        try
                        {
                            await transition.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        }
                        catch (InvalidOperationException exception) when (
                            failAttachment && ReferenceEquals(exception, failure))
                        {
                        }
                    }
                    owner.AsyncManager.EventSubscriptionCallback = static (_, _) => default;
                    await owner.Lifecycle.ReleaseSourceAsync(item)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }
        }

        private static async Task AssertEventReadinessDoesNotBlockDataChangeCreationAsync(
            ManagerPath path,
            bool compensateDetach,
            bool deleteNode = false)
        {
            var clock = new FakeTimeProvider();
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory, clock);
            using (queueFactory)
            using (ManagerOwner owner = CreateManager(server.Object, path))
            {
                var source = new BaseObjectState(null)
                {
                    NodeId = new NodeId("HeldEventSource", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("HeldEventSource", owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                var value = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("IndependentValue", owner.NamespaceIndex),
                    BrowseName = new QualifiedName("IndependentValue", owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = 42
                };
                source.CreateAsPredefinedNode(owner.SystemContext);
                value.CreateAsPredefinedNode(owner.SystemContext);
                await owner.AddNodeAsync(source).ConfigureAwait(false);
                await owner.AddNodeAsync(value).ConfigureAwait(false);
                using MonitoredItem eventItem = CreateEventMonitoredItem(
                    server.Object, Mock.Of<IAsyncNodeManager>(), new NodeHandle(), source.NodeId);
                var lifecycle = (IMonitoredItemGenerationBinding)eventItem;
                lifecycle.ReleaseSource();
                if (compensateDetach)
                {
                    ServiceResult attached = await owner.Lifecycle.AdoptSourceAsync(eventItem)
                        .ConfigureAwait(false);
                    Assert.That(attached.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var failure = new InvalidOperationException("Injected unsubscribe failure.");
                using var transitionCancellation = new CancellationTokenSource();
                CancellationToken requestToken = default;
                CancellationToken compensationToken = default;
                owner.AsyncManager.EventSubscriptionCallback = async (unsubscribe, ct) =>
                {
                    if (unsubscribe)
                    {
                        requestToken = ct;
                        throw failure;
                    }
                    compensationToken = ct;
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                };
                Task<ServiceResult>? lifecycleTransition = null;
                Task transition;
                if (deleteNode)
                {
                    transition = owner.AsyncManager.DeleteNodeAsync(
                        owner.SystemContext, source.NodeId, transitionCancellation.Token).AsTask();
                }
                else
                {
                    lifecycleTransition = compensateDetach
                        ? owner.Lifecycle.ReleaseSourceAsync(eventItem, transitionCancellation.Token).AsTask()
                        : owner.Lifecycle.AdoptSourceAsync(
                            eventItem, cancellationToken: transitionCancellation.Token).AsTask();
                    transition = lifecycleTransition;
                }
                Task<MonitoredItem>? creation = null;
                MonitoredItem? created = null;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (compensateDetach)
                    {
                        transitionCancellation.Cancel();
                        clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
                        using (Assert.EnterMultipleScope())
                        {
                            Assert.That(requestToken, Is.EqualTo(transitionCancellation.Token));
                            Assert.That(requestToken.IsCancellationRequested, Is.True);
                            Assert.That(compensationToken.CanBeCanceled, Is.True);
                            Assert.That(compensationToken, Is.Not.EqualTo(requestToken));
                            Assert.That(compensationToken.IsCancellationRequested, Is.False,
                                "Compensation must survive request cancellation until its own deadline.");
                        }
                    }
                    creation = CreateIndependentDataChangeItemAsync(owner.AsyncManager, value.NodeId);
                    created = await creation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(release.Task.IsCompleted, Is.False);
                        Assert.That(transition.IsCompleted, Is.False,
                            "The unrelated service must finish before event readiness is released.");
                        Assert.That(created.NodeId, Is.EqualTo(value.NodeId));
                        Assert.That(created.AttributeId, Is.EqualTo(Attributes.Value));
                        Assert.That(created.Id, Is.Not.EqualTo(eventItem.Id));
                        Assert.That(owner.Owns(created.NodeManager), Is.True);
                        Assert.That(((IMonitoredItemGenerationBinding)created).IsSourceUnavailable, Is.False);
                    }
                    release.TrySetResult(true);
                    if (compensateDetach)
                    {
                        InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(
                            async () => await transition.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)))!;
                        Assert.That(error, Is.SameAs(failure));
                        clock.Advance(TimeSpan.FromTicks(1));
                        Assert.That(compensationToken.IsCancellationRequested, Is.False,
                            "Completed compensation must retire its deadline timer.");
                    }
                    else
                    {
                        ServiceResult result = await lifecycleTransition!.WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    }
                    IReadOnlyList<IMonitoredItem> snapshot = owner.CaptureMonitoredItems();
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(snapshot, Is.EquivalentTo(new IMonitoredItem[] { eventItem, created }));
                        Assert.That(lifecycle.IsSourceUnavailable, Is.False);
                        Assert.That(owner.Owns(eventItem.NodeManager), Is.True);
                        Assert.That(source.AreEventsMonitored, Is.True);
                    }
                    if (deleteNode)
                    {
                        Assert.That(owner.ContainsNode(source.NodeId), Is.True);
                        Assert.That(lifecycle.IsDeleted, Is.False);
                        owner.AsyncManager.EventSubscriptionCallback = static (_, _) => default;
                        bool deleted = await owner.DeleteNodeAsync(source.NodeId).ConfigureAwait(false);
                        snapshot = owner.CaptureMonitoredItems();
                        using (Assert.EnterMultipleScope())
                        {
                            Assert.That(deleted, Is.True);
                            Assert.That(snapshot, Is.EqualTo(new IMonitoredItem[] { created }));
                            Assert.That(owner.ContainsNode(source.NodeId), Is.False);
                            Assert.That(lifecycle.IsDeleted, Is.True);
                            Assert.That(lifecycle.IsSourceUnavailable, Is.True);
                            Assert.That(source.AreEventsMonitored, Is.False);
                        }
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                    try
                    {
                        await transition.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure))
                    {
                    }
                    owner.AsyncManager.EventSubscriptionCallback = static (_, _) => default;
                    if (creation != null)
                    {
                        created ??= await creation.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    if (created != null)
                    {
                        await owner.Lifecycle.ReleaseSourceAsync(created).ConfigureAwait(false);
                        created.Dispose();
                    }
                    await owner.Lifecycle.ReleaseSourceAsync(eventItem).ConfigureAwait(false);
                }
            }
        }

        private static async Task<MonitoredItem> CreateIndependentDataChangeItemAsync(
            TestableAsyncCustomNodeManager manager,
            NodeId nodeId)
        {
            var request = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 17,
                    SamplingInterval = 1000,
                    QueueSize = 10,
                    DiscardOldest = true
                }
            };
            var identity = new Mock<IUserIdentity>();
            var session = new Mock<ISession>();
            session.SetupGet(item => item.Identity).Returns(identity.Object);
            session.SetupGet(item => item.EffectiveIdentity).Returns(identity.Object);
            session.SetupGet(item => item.PreferredLocales).Returns([]);
            using var context = new OperationContext(
                new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
            var errors = new ServiceResult[1];
            var filterErrors = new MonitoringFilterResult[1];
            var items = new IMonitoredItem[1];
            await manager.CreateMonitoredItemsAsync(
                context, 1, 1000, TimestampsToReturn.Both, [request], errors, filterErrors, items,
                false, new MonitoredItemIdFactory()).ConfigureAwait(false);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(items[0], Is.TypeOf<MonitoredItem>());
            return (MonitoredItem)items[0];
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

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task AttachSkipsMonitoredItemDisposedBeforeTheHandOverAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                Mock<IAsyncNodeManager> originalNodeManager = new();
                MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    originalNodeManager.Object,
                    new object(),
                    new NodeId("Disposed", 1));
                var current = new TestNodeManagerLifecycle();
                var replacement = new TestNodeManagerLifecycle();
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current,
                    replacement,
                    compatibleItems: [item],
                    deletedItems: [],
                    isOwnedBySubscription: static _ => true);

                // The Client deleted the item while the reload was running.
                item.Dispose();

                List<Exception> failures = await transition
                    .AdoptAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(failures, Is.Empty);
                    Assert.That(replacement.AttachCount, Is.Zero);
                });
            }
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [Category("NodeManagerLifecycle")]
        public async Task AttachUndoesHandOverWhenItemIsDisposedWhileAttachingAsync(
            int cleanupFailure,
            int expectedFailureCount)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                Mock<IAsyncNodeManager> originalNodeManager = new();
                MonitoredItem item = CreateMonitoredItem(
                    server.Object,
                    originalNodeManager.Object,
                    new object(),
                    new NodeId("Racing", 1));
                var current = new TestNodeManagerLifecycle();
                var replacement = new TestNodeManagerLifecycle
                {
                    // The delete lands after the ownership check and before the attach returns.
                    AttachCallback = attaching =>
                    {
                        ((MonitoredItem)attaching).Dispose();
                        return ServiceResult.Good;
                    },
                    DetachCallback = _ => cleanupFailure switch
                    {
                        1 => new ServiceResult(StatusCodes.BadUnexpectedError),
                        2 => throw new InvalidOperationException("Injected cleanup failure."),
                        _ => ServiceResult.Good
                    }
                };
                var transition = new NodeManagerLifecycle.MonitoredItemTransition(
                    current,
                    replacement,
                    compatibleItems: [item],
                    deletedItems: [],
                    isOwnedBySubscription: static _ => true);

                List<Exception> failures = await transition
                    .AdoptAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(failures, Has.Count.EqualTo(expectedFailureCount));
                    Assert.That(replacement.AttachCount, Is.EqualTo(1));

                    // The replacement must not be left sampling a disposed item.
                    Assert.That(replacement.DetachCount, Is.EqualTo(1));
                    Assert.That(item.ManagerHandle, Is.Null);
                });
                if (cleanupFailure == 1)
                {
                    Assert.That(((ServiceResultException)failures[0]).StatusCode,
                        Is.EqualTo(StatusCodes.BadUnexpectedError));
                }
                else if (cleanupFailure == 2)
                {
                    Assert.That(failures[0].Message, Is.EqualTo("Injected cleanup failure."));
                }
            }
        }

        private static MonitoredItem CreateMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            object managerHandle,
            NodeId nodeId,
            uint id = 2,
            bool durable = false)
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
                sourceSamplingInterval: 1000,
                createDurable: durable)
            {
                SubscriptionCallback = subscription.Object
            };
        }

        private static MonitoredItem CreateEventMonitoredItem(
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
            var filter = new EventFilter();

            return new MonitoredItem(
                server,
                nodeManager,
                managerHandle,
                subscriptionId: 1,
                id,
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

        private sealed class TestNodeManagerLifecycle : INodeManagerMonitoredItemSource
        {
            public Func<IMonitoredItem, ServiceResult> AttachCallback { get; set; } =
                static _ => ServiceResult.Good;

            public Func<IMonitoredItem, ServiceResult> DetachCallback { get; set; } =
                static _ => ServiceResult.Good;

            public int AttachCount { get; private set; }

            public int DetachCount { get; private set; }

            /// <summary>
            /// Reports that this test lifecycle has no monitored-node notification consumer.
            /// </summary>
            public Func<CancellationToken, ValueTask>? GetNotificationDrain(NodeId nodeId)
            {
                return null;
            }

            public ValueTask<ServiceResult> ValidateSourceAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> ReleaseSourceAsync(
                IMonitoredItem monitoredItem,
                long sourceRevision,
                CancellationToken cancellationToken = default)
            {
                DetachCount++;
                var binding = monitoredItem as IMonitoredItemGenerationBinding;
                ServiceResult result = DetachCallback(monitoredItem);
                if (ServiceResult.IsGood(result))
                {
                    binding?.TryClearSource(sourceRevision);
                }
                return new ValueTask<ServiceResult>(result);
            }

            public ValueTask<ServiceResult> AdoptSourceAsync(
                IMonitoredItem monitoredItem,
                MonitoredItemAdoption adoption,
                CancellationToken cancellationToken = default)
            {
                AttachCount++;
                return new ValueTask<ServiceResult>(AttachCallback(monitoredItem));
            }
        }

        private sealed class ManagerOwner : IDisposable
        {
            private readonly TestableAsyncCustomNodeManager m_asyncManager = null!;
            private readonly TestableCustomNodeManager2 m_syncManager = null!;

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
                Lifecycle = (INodeManagerMonitoredItemSource)adapter;
                NamespaceIndex = manager.NamespaceIndex;
                SystemContext = manager.SystemContext;
            }

            public INodeManagerMonitoredItemSource Lifecycle { get; }

            public TestableAsyncCustomNodeManager AsyncManager => m_asyncManager ??
                throw new InvalidOperationException("This scenario requires an async node manager.");

            public ushort NamespaceIndex { get; }

            public ServerSystemContext SystemContext { get; }

            /// <summary>
            /// Captures native membership after the tested mutation has completed.
            /// </summary>
            public IReadOnlyList<IMonitoredItem> CaptureMonitoredItems()
            {
                return m_asyncManager is not null
                    ? [.. m_asyncManager.MonitoredItems.Values]
                    : [.. m_syncManager!.MonitoredItems.Values];
            }

            /// <summary>
            /// Injects a conflicting native entry while a provider callback is outside the manager gate.
            /// </summary>
            public bool TryAddNativeItem(uint id, IMonitoredItem item)
            {
                return m_asyncManager is not null
                    ? m_asyncManager.MonitoredItems.TryAdd(id, item)
                    : m_syncManager.MonitoredItems.TryAdd(id, item);
            }

            /// <summary>
            /// Removes the exact test-owned conflicting entry.
            /// </summary>
            public bool TryRemoveNativeItem(uint id)
            {
                return m_asyncManager is not null
                    ? m_asyncManager.MonitoredItems.TryRemove(id, out _)
                    : m_syncManager.MonitoredItems.TryRemove(id, out _);
            }

            public ValueTask AddNodeAsync(NodeState node)
            {
                if (m_asyncManager != null)
                {
                    return m_asyncManager.AddPredefinedNodePublicAsync(SystemContext, node);
                }

                return ((ILocalAddressSpaceSource)m_syncManager!).CreateLocalAddressSpace()
                    .AddOrUpdateNodeAsync(node);
            }

            public bool Owns(IAsyncNodeManager? nodeManager)
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
                IAsyncNodeManager nodeManager,
                bool forwardToBase = false)
                : base(server, nodeManager, 100, 200, [])
            {
                m_forwardToBase = forwardToBase;
            }

            public int StartCount { get; private set; }

            public int StopCount { get; private set; }

            public int ApplyCount { get; private set; }

            public bool FailNextApply { get; set; }

            /// <summary>
            /// Gets ordered failures injected into sampling commits.
            /// </summary>
            public Queue<Exception> ApplyFailures { get; } = [];

            /// <summary>
            /// Gets or sets whether an injected failure follows real sampling publication.
            /// </summary>
            public bool FailAfterApply { get; set; }

            public override void StartMonitoring(
                OperationContext context,
                ISampledDataChangeMonitoredItem monitoredItem,
                IUserIdentity? savedOwnerIdentity = null)
            {
                StartCount++;
                if (m_forwardToBase)
                {
                    base.StartMonitoring(context, monitoredItem, savedOwnerIdentity);
                }
            }

            public override void StopMonitoring(ISampledDataChangeMonitoredItem monitoredItem)
            {
                StopCount++;
                if (m_forwardToBase)
                {
                    base.StopMonitoring(monitoredItem);
                }
            }

            public override void ApplyChanges()
            {
                ApplyCount++;
                if (m_forwardToBase && (ApplyFailures.Count == 0 || FailAfterApply))
                {
                    base.ApplyChanges();
                }
                if (ApplyFailures.Count > 0)
                {
                    throw ApplyFailures.Dequeue();
                }
                if (FailNextApply)
                {
                    FailNextApply = false;
                    throw new InvalidOperationException("Injected apply failure.");
                }
            }

            /// <summary>Whether real sampling runs below the failure injection points.</summary>
            private readonly bool m_forwardToBase;
        }
    }
}
