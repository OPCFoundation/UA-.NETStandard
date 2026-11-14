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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies striped generation admission and address-space epoch lifetimes.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class GenerationGateTests
    {
        /// <summary>
        /// Independent execution contexts cannot inherit a notification's source generation.
        /// </summary>
        [Test]
        public void MasterDoesNotStoreAmbientSourceGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                IAsyncNodeManager source = Mock.Of<IAsyncNodeManager>();
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [source]);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(source);
                NodeManagerGeneration core = NodeManagerGenerationRegistry.GetOrCreate(master.CoreNodeManager!);
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, source, out MasterNodeManager.SourceEmissionAdmission? admission), Is.True);
                using MasterNodeManager.SourceEmissionOrigin? origin = admission?.TransferToOrigin();
                ISystemContext context = origin!.CreateContext(server.Object.DefaultSystemContext);
                generation.Gate.Retire();

                ExecutionContext.Run(ExecutionContext.Capture()!, _ =>
                {
                    bool explicitSource = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, master.CoreNodeManager,
                        out MasterNodeManager.SourceEmissionAdmission? explicitAdmission,
                        isGlobalServerEventSink: true, context: context);
                    bool implicitSource = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, master.CoreNodeManager,
                        out MasterNodeManager.SourceEmissionAdmission? implicitAdmission,
                        isGlobalServerEventSink: true);
                    try
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(explicitSource, Is.True);
                            Assert.That(explicitAdmission?.Generation, Is.SameAs(generation));
                            Assert.That(implicitSource, Is.True);
                            Assert.That(implicitAdmission?.Generation, Is.SameAs(core));
                            Assert.That(generation.Gate.ActiveCount, Is.EqualTo(2));
                            Assert.That(core.Gate.ActiveCount, Is.EqualTo(1));
                        });
                    }
                    finally
                    {
                        explicitAdmission?.Dispose();
                        implicitAdmission?.Dispose();
                    }
                }, null);

                Assert.That(generation.Gate.ActiveCount, Is.EqualTo(1));
                Assert.That(core.Gate.ActiveCount, Is.Zero);
            }
        }

        /// <summary>
        /// Bound raw sources use a single visible adapter, adapter aliases of the same resource share
        /// its routed generation, and unrelated generations are rejected.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        public void BoundRawSourceRequiresOneVisibleAdapter(int facadeCount, bool expected)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var source = new Mock<IAsyncNodeManager>();
                source.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                var facade = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(source.Object));
                var otherFacade = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(source.Object));
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(facade);
                var managers = new List<IAsyncNodeManager>();
                if (facadeCount > 0)
                {
                    managers.Add(facade);
                }
                if (facadeCount > 1)
                {
                    managers.Add(otherFacade);
                }
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [.. managers]);

                bool itemAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, source.Object, generation.Reference, out var item);
                bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, source.Object, out var notification, generation.Reference);
                bool otherFacadeAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, otherFacade, out var otherFacadeAdmission, generation.Reference);
                NodeManagerGeneration unrelated = NodeManagerGenerationRegistry.GetOrCreate(
                    Mock.Of<IAsyncNodeManager>());
                bool unrelatedAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, source.Object, out var other, unrelated.Reference);
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(itemAccepted, Is.EqualTo(expected));
                        Assert.That(eventAccepted, Is.EqualTo(expected));
                        Assert.That(item?.Generation, Is.SameAs(expected ? generation : null));
                        Assert.That(notification?.Generation, Is.SameAs(expected ? generation : null));
                        Assert.That(otherFacadeAccepted, Is.EqualTo(facadeCount > 0));
                        Assert.That(
                            otherFacadeAdmission?.Generation,
                            Is.SameAs(facadeCount > 0 ? generation : null));
                        Assert.That(unrelatedAccepted, Is.False);
                    });
                }
                finally
                {
                    item?.Dispose();
                    notification?.Dispose();
                    otherFacadeAdmission?.Dispose();
                    other?.Dispose();
                }
            }
        }

        /// <summary>
        /// A live bound owner outside the routing table remains eligible to emit.
        /// </summary>
        [Test]
        public void UnroutedExactBoundOwnerCanEmit()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var source = new Mock<IAsyncNodeManager>();
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(source.Object);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, []);

                bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, source.Object, out var notification, generation.Reference);
                bool itemAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, source.Object, generation.Reference, out var item);
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(eventAccepted, Is.True);
                        Assert.That(notification?.Generation, Is.SameAs(generation));
                        Assert.That(itemAccepted, Is.True);
                        Assert.That(item?.Generation, Is.SameAs(generation));
                    });
                }
                finally
                {
                    notification?.Dispose();
                    item?.Dispose();
                }
            }
        }

        /// <summary>
        /// A raw monitored binding created on either side of publication uses the published facade's gate.
        /// </summary>
        /// <param name="bindBeforePublication">
        /// Whether the raw resource is bound before the facade is published.
        /// </param>
        [TestCase(false)]
        [TestCase(true)]
        public void ForwardedResourceBindingUsesPublishedGate(bool bindBeforePublication)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                var unpublished = new Mock<IAsyncNodeManager>();
                unpublished.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    var root = new BaseObjectState(null) { NodeId = new NodeId(42, 1) };
                    MonitoredNode2? monitored = bindBeforePublication
                        ? new MonitoredNode2(resource.Object, server.Object, root)
                        : null;
                    try
                    {
                        if (monitored is not null)
                        {
                            bool admitted = MasterNodeManager.TryCaptureItemEmission(
                                server.Object, resource.Object, monitored.Generation, out var standalone);
                            standalone?.Dispose();
                            Assert.That(admitted, Is.True);
                        }

                        routing.Add(facade.Object, [1]);
                        NodeManagerGeneration published = routing.Revision.GetGeneration(facade.Object);
                        monitored ??= new MonitoredNode2(resource.Object, server.Object, root);
                        Assert.That(monitored.Generation.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);

                        bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, resource.Object, out var notification, monitored.Generation);
                        bool itemAccepted = MasterNodeManager.TryCaptureItemEmission(
                            server.Object, resource.Object, monitored.Generation, out var item);
                        bool unboundAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, resource.Object, out var unbound);
                        bool otherAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, unpublished.Object, out var other, published.Reference);
                        MasterNodeManager.NotificationDispatchLease[] dispatches =
                            master.GetAllEventNotificationDispatches([facade.Object]);
                        try
                        {
                            Assert.Multiple(() =>
                            {
                                Assert.That(bound!.Gate, Is.SameAs(published.Gate));
                                Assert.That(eventAccepted, Is.True);
                                Assert.That(notification?.Generation.Gate, Is.SameAs(published.Gate));
                                Assert.That(itemAccepted, Is.True);
                                Assert.That(item?.Generation.Gate, Is.SameAs(published.Gate));
                                Assert.That(unboundAccepted, Is.True);
                                Assert.That(unbound?.Generation.Gate, Is.SameAs(published.Gate));
                                Assert.That(otherAccepted, Is.False);
                                Assert.That(other.HasValue, Is.False);
                                Assert.That(dispatches.Select(dispatch => dispatch.NodeManager),
                                    Is.EqualTo(new[] { facade.Object }));
                            });
                        }
                        finally
                        {
                            notification?.Dispose();
                            item?.Dispose();
                            unbound?.Dispose();
                            other?.Dispose();
                            MasterNodeManager.DisposeNotificationDispatches(dispatches);
                        }
                    }
                    finally
                    {
                        monitored?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// A raw resource requires exactly one visible forwarding facade for a new emission.
        /// </summary>
        /// <param name="hidden">Whether the first facade is hidden.</param>
        /// <param name="ambiguous">Whether a second visible facade has the same resource.</param>
        /// <param name="expected">Whether the raw source is admitted.</param>
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void ForwardedResourceRequiresOneVisibleFacade(bool hidden, bool ambiguous, bool expected)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                var first = new Mock<IAsyncNodeManager>();
                first.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                first.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                var second = new Mock<IAsyncNodeManager>();
                second.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                second.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerGeneration bound = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    routing.Add(first.Object, [1], visible: !hidden);
                    if (ambiguous)
                    {
                        routing.Add(second.Object, [1]);
                    }
                    bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var notification, bound.Reference);
                    bool unboundAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var unbound);
                    try
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(eventAccepted, Is.EqualTo(expected));
                            Assert.That(unboundAccepted, Is.EqualTo(expected));
                            if (expected)
                            {
                                NodeManagerGeneration published = routing.Revision.GetGeneration(first.Object);
                                Assert.That(notification?.Generation.Gate, Is.SameAs(published.Gate));
                                Assert.That(unbound?.Generation.Gate, Is.SameAs(published.Gate));
                            }
                            else
                            {
                                Assert.That(notification.HasValue, Is.False);
                                Assert.That(unbound.HasValue, Is.False);
                            }
                        });
                    }
                    finally
                    {
                        notification?.Dispose();
                        unbound?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Concurrent raw binding and facade reservation converge on one resource lifetime.
        /// </summary>
        [Test]
        public async Task RawBindingRacingForwardedPublicationSharesOneGateAsync()
        {
            for (int iteration = 0; iteration < 24; iteration++)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                var routing = new NodeManagerRoutingTable();
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<NodeManagerGeneration> binding = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    return NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                });
                Task<NodeManagerGeneration> publication = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    routing.Add(facade.Object, [1]);
                    return routing.Revision.GetGeneration(facade.Object);
                });
                start.SetResult(true);
                await Task.WhenAll(binding, publication).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                NodeManagerGeneration bound = await binding.ConfigureAwait(false);
                NodeManagerGeneration published = await publication.ConfigureAwait(false);
                Assert.That(bound.Gate, Is.SameAs(published.Gate), $"Iteration {iteration}.");
            }
        }

        /// <summary>
        /// A notification queued before publication retains the published gate through removal or replacement.
        /// </summary>
        /// <param name="replace">Whether a new owner replaces the facade instead of removing it.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task QueuedForwardedEventDelaysRetirementAsync(bool replace)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                resource.Setup(value => value.ValidateEventRolePermissionsAsync(
                        It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Returns(async (IEventMonitoredItem _, IFilterTarget _, CancellationToken ct) =>
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        return ServiceResult.Good;
                    });
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    var root = new BaseObjectState(null) { NodeId = new NodeId(43, 1) };
                    using var monitored = new MonitoredNode2(resource.Object, server.Object, root);
                    var item = new Mock<IEventMonitoredItem>();
                    item.SetupGet(value => value.Id).Returns(1);
                    monitored.Add(item.Object);
                    IFilterTarget before = Mock.Of<IFilterTarget>();
                    IFilterTarget after = Mock.Of<IFilterTarget>();
                    Task? drain = null;
                    try
                    {
                        await monitored.OnReportEventAsync(server.Object.DefaultSystemContext, root, before)
                            .ConfigureAwait(false);
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        routing.Add(facade.Object, [1]);
                        NodeManagerGeneration published = routing.Revision.GetGeneration(facade.Object);
                        Assert.That(monitored.Generation.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                        Assert.That(bound!.Gate, Is.SameAs(published.Gate));

                        if (replace)
                        {
                            var replacementResource = new Mock<IAsyncNodeManager>();
                            replacementResource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                            replacementResource.SetupGet(value => value.SyncNodeManager)
                                .Returns(new SyncNodeManagerAdapter(replacementResource.Object));
                            var replacement = new Mock<IAsyncNodeManager>();
                            replacement.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                            replacement.SetupGet(value => value.SyncNodeManager)
                                .Returns(new SyncNodeManagerAdapter(replacementResource.Object));
                            routing.Replace(facade.Object, replacement.Object, [1]);
                            NodeManagerGeneration current = routing.Revision.GetGeneration(replacement.Object);
                            Assert.That(current.Gate, Is.Not.SameAs(published.Gate));
                            bool currentAccepted = MasterNodeManager.TryCaptureSourceEmission(
                                server.Object, replacementResource.Object, out var currentAdmission);
                            try
                            {
                                Assert.That(currentAccepted, Is.True);
                                Assert.That(currentAdmission?.Generation.Gate, Is.SameAs(current.Gate));
                            }
                            finally
                            {
                                currentAdmission?.Dispose();
                            }
                        }
                        else
                        {
                            routing.Remove(facade.Object);
                        }

                        drain = published.Gate.WaitForDrainAsync();
                        Assert.That(drain.IsCompleted, Is.False);
                        bool accepted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, resource.Object, out var rejected, monitored.Generation);
                        rejected?.Dispose();
                        Assert.That(accepted, Is.False);
                        await monitored.OnReportEventAsync(server.Object.DefaultSystemContext, root, after)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        release.TrySetResult(true);
                    }
                    await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Assert.That(drain, Is.Not.Null);
                    await drain!.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    item.Verify(value => value.QueueEvent(before), Times.Once);
                    item.Verify(value => value.QueueEvent(after), Times.Never);
                }
            }
        }

        /// <summary>
        /// A hidden adapter authorizes neither itself nor its raw source, even while its generation is live.
        /// </summary>
        [Test]
        public void HiddenAdapterDoesNotAdmitBoundRawSource()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var source = new Mock<IAsyncNodeManager>();
                source.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                var facade = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(source.Object));
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(facade);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                var routing = new NodeManagerRoutingTable();
                using var master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    routing);
                server.SetupGet(value => value.NodeManager).Returns(master);
                routing.Add(facade, [], visible: false);

                bool accepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, source.Object, out var admission, generation.Reference);
                admission?.Dispose();
                bool ownerAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, facade, out var ownerAdmission, generation.Reference);
                ownerAdmission?.Dispose();
                bool ownerItemAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, facade, generation.Reference, out var ownerItem);
                ownerItem?.Dispose();
                Assert.Multiple(() =>
                {
                    Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                    Assert.That(accepted, Is.False);
                    Assert.That(ownerAccepted, Is.False);
                    Assert.That(ownerItemAccepted, Is.False);
                });
            }
        }

        /// <summary>
        /// A hidden stack adapter cannot borrow a visible sibling's route, while an unregistered alias uses
        /// the first visible stack adapter and the raw resource still needs a unique visible facade.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StackAdapterAliasesFollowVisibleRoutes(bool secondVisible)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var source = new Mock<IAsyncNodeManager>();
                source.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                var facade = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(source.Object));
                var second = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(source.Object));
                var alias = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(source.Object));
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(facade);
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    routing.Add(facade, []);
                    routing.Add(second, [], visible: secondVisible);

                    bool secondAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, second, out var secondAdmission, generation.Reference);
                    secondAdmission?.Dispose();
                    bool aliasAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, alias, out var aliasAdmission, generation.Reference);
                    NodeManagerGeneration? aliasGeneration = aliasAdmission?.Generation;
                    aliasAdmission?.Dispose();
                    bool rawAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, source.Object, out var rawAdmission, generation.Reference);
                    rawAdmission?.Dispose();
                    Assert.Multiple(() =>
                    {
                        Assert.That(secondAccepted, Is.EqualTo(secondVisible));
                        Assert.That(aliasAccepted, Is.True);
                        Assert.That(aliasGeneration, Is.SameAs(generation));
                        Assert.That(rawAccepted, Is.EqualTo(!secondVisible));
                    });
                }
            }
        }

        /// <summary>
        /// Explicit stack adapters over the same synchronous manager share the published generation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitSynchronousAdapterAliasUsesPublishedGeneration(bool cachedOwner)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var synchronous = new Mock<INodeManager>();
                synchronous.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                IAsyncNodeManager published = cachedOwner
                    ? synchronous.Object.ToAsyncNodeManager()
                    : new AsyncNodeManagerAdapter(synchronous.Object);
                var alias = new AsyncNodeManagerAdapter(synchronous.Object);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(published);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [published]);

                bool publishedAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, published, out var publishedAdmission, generation.Reference);
                publishedAdmission?.Dispose();
                bool aliasAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, alias, out var aliasAdmission, generation.Reference);
                NodeManagerGeneration? eventGeneration = aliasAdmission?.Generation;
                aliasAdmission?.Dispose();
                bool aliasItemAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, alias, generation.Reference, out var aliasItem);
                NodeManagerGeneration? itemGeneration = aliasItem?.Generation;
                aliasItem?.Dispose();
                Assert.Multiple(() =>
                {
                    Assert.That(
                        NodeManagerGenerationRegistry.GetIdentity(alias),
                        Is.SameAs(NodeManagerGenerationRegistry.GetIdentity(published)));
                    Assert.That(publishedAccepted, Is.True);
                    Assert.That(aliasAccepted, Is.True);
                    Assert.That(eventGeneration, Is.SameAs(generation));
                    Assert.That(aliasItemAccepted, Is.True);
                    Assert.That(itemGeneration, Is.SameAs(generation));
                });
            }
        }

        /// <summary>
        /// An unrelated async facade cannot borrow a generation by exposing its synchronous manager.
        /// </summary>
        [Test]
        public void UnpublishedCustomFacadeDoesNotBorrowPublishedGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var synchronous = new Mock<INodeManager>();
                synchronous.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                var published = new AsyncNodeManagerAdapter(synchronous.Object);
                var unpublished = new Mock<IAsyncNodeManager>();
                unpublished.SetupGet(manager => manager.SyncNodeManager).Returns(synchronous.Object);
                unpublished.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(published);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [published]);

                bool publishedAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, published, out var publishedAdmission, generation.Reference);
                publishedAdmission?.Dispose();
                bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, unpublished.Object, out var notification, generation.Reference);
                bool itemAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, unpublished.Object, generation.Reference, out var item);
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(unpublished.Object.SyncNodeManager, Is.SameAs(published.SyncNodeManager));
                        Assert.That(publishedAccepted, Is.True);
                        Assert.That(eventAccepted, Is.False);
                        Assert.That(notification, Is.Null);
                        Assert.That(itemAccepted, Is.False);
                        Assert.That(item, Is.Null);
                    });
                }
                finally
                {
                    notification?.Dispose();
                    item?.Dispose();
                }
            }
        }

        /// <summary>
        /// An admitted raw source may complete its own event fan-out after its generation retires.
        /// </summary>
        [Test]
        public void AdaptedRawSourceFanoutRetainsItsAdmittedGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var source = new Mock<IAsyncNodeManager>();
                source.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                var facade = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(source.Object));
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(facade);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [facade]);
                Assert.That(MasterNodeManager.TryCaptureItemEmission(
                    server.Object, source.Object, generation.Reference, out var parent), Is.True);
                using MasterNodeManager.SourceEmissionOrigin? origin = parent?.TransferToOrigin();
                ISystemContext context = origin!.CreateContext(server.Object.DefaultSystemContext);
                generation.Gate.Retire();

                bool accepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, source.Object, out var child, generation.Reference, context: context);
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(accepted, Is.True);
                        Assert.That(child?.Generation, Is.SameAs(generation));
                        Assert.That(generation.Gate.ActiveCount, Is.EqualTo(2));
                    });
                }
                finally
                {
                    child?.Dispose();
                }
            }
        }

        /// <summary>
        /// An admitted source can fan out through an explicit synchronous adapter alias after retirement.
        /// </summary>
        [Test]
        public void ExplicitAdapterAliasFanoutRequiresTheBoundGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var synchronous = new Mock<INodeManager>();
                synchronous.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                var published = new AsyncNodeManagerAdapter(synchronous.Object);
                var alias = new AsyncNodeManagerAdapter(synchronous.Object);
                var unrelatedFacade = new Mock<IAsyncNodeManager>();
                unrelatedFacade.SetupGet(manager => manager.SyncNodeManager).Returns(synchronous.Object);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(published);
                NodeManagerGeneration unrelated = NodeManagerGenerationRegistry.GetOrCreate(
                    Mock.Of<IAsyncNodeManager>());
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [published]);
                Assert.That(MasterNodeManager.TryCaptureItemEmission(
                    server.Object, published, generation.Reference, out var parent), Is.True);
                using MasterNodeManager.SourceEmissionOrigin? origin = parent?.TransferToOrigin();
                ISystemContext context = origin!.CreateContext(server.Object.DefaultSystemContext);
                generation.Gate.Retire();

                bool aliasAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, alias, out var child, generation.Reference, context: context);
                bool wrongGenerationAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, alias, out var wrongGeneration, unrelated.Reference, context: context);
                bool unrelatedFacadeAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, unrelatedFacade.Object, out var other, generation.Reference, context: context);
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(aliasAccepted, Is.True);
                        Assert.That(child?.Generation, Is.SameAs(generation));
                        Assert.That(wrongGenerationAccepted, Is.False);
                        Assert.That(wrongGeneration, Is.Null);
                        Assert.That(unrelatedFacadeAccepted, Is.False);
                        Assert.That(other, Is.Null);
                        Assert.That(generation.Gate.ActiveCount, Is.EqualTo(2));
                    });
                }
                finally
                {
                    child?.Dispose();
                    wrongGeneration?.Dispose();
                    other?.Dispose();
                }
            }
        }

        /// <summary>
        /// Prepared generations refuse entry, while retirement closes a live gate and drains prior work.
        /// </summary>
        [Test]
        public async Task RetiringGenerationRefusesNewAdmissionsAndWaitsForExistingLeaseAsync()
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Prepared);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? _), Is.False);

            gate.Activate();
            Assert.That(gate.TryEnter(out GenerationGate.Lease? lease), Is.True);
            Assert.That(gate.ActiveCount, Is.EqualTo(1));

            gate.Retire();
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(gate.TryEnter(out GenerationGate.Lease? _), Is.False);
            Task drained = gate.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);

            lease!.Dispose();
            await drained.ConfigureAwait(false);

            Assert.That(gate.ActiveCount, Is.Zero);
            gate.DisposeGeneration();
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
            Assert.That(() => gate.Activate(), Throws.InvalidOperationException);

            var aborted = new GenerationGate(NodeManagerGenerationState.Prepared);
            aborted.Abort();
            Assert.That(aborted.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(() => aborted.Activate(), Throws.InvalidOperationException);
        }

        /// <summary>
        /// Only an active parent can extend admitted work, and each child independently delays retirement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ActiveParentForkKeepsGenerationAliveAfterParentExitAsync(bool retireBeforeFork)
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Live);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
            if (retireBeforeFork)
            {
                gate.Retire();
            }
            Assert.That(parent!.TryFork(out int child), Is.True);
            Assert.That(gate.ActiveCount, Is.EqualTo(2));
            parent.Dispose();
            parent.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(parent.TryFork(out int rejected), Is.False);
                Assert.That(rejected, Is.EqualTo(-1));
                Assert.That(gate.ActiveCount, Is.EqualTo(1));
            });
            gate.Retire();
            Task drained = gate.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);
            gate.Exit(child);
            await drained.ConfigureAwait(false);
            Assert.That(gate.ActiveCount, Is.Zero);
            gate.DisposeGeneration();
            Assert.That(parent.TryFork(out int _), Is.False);
            Assert.That(gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Racing parent release either refuses a child or counts it before the generation can drain.
        /// </summary>
        [Test]
        public async Task ForkRacingParentReleaseNeverEscapesGenerationDrainAsync()
        {
            for (int iteration = 0; iteration < 200; iteration++)
            {
                var gate = new GenerationGate(NodeManagerGenerationState.Live);
                Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
                gate.Retire();
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<int> fork = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    return parent!.TryFork(out int child) ? child : -1;
                });
                Task release = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    parent!.Dispose();
                });
                start.SetResult(true);
                await Task.WhenAll(fork, release).ConfigureAwait(false);
                int slot = await fork.ConfigureAwait(false);
                bool entered = slot >= 0;
                Assert.That(gate.ActiveCount, Is.EqualTo(entered ? 1 : 0));
                Task drained = gate.WaitForDrainAsync();
                Assert.That(drained.IsCompleted, Is.EqualTo(!entered));
                if (entered)
                {
                    gate.Exit(slot);
                }
                await drained.ConfigureAwait(false);
                Assert.That(gate.ActiveCount, Is.Zero);
                gate.DisposeGeneration();
            }
        }

        /// <summary>
        /// A drain scan cannot miss work transferred from a late parent stripe to an earlier child stripe.
        /// </summary>
        [Test]
        public async Task ForkRacingDrainScanCannotCompleteWhileChildRemainsActiveAsync()
        {
            using var barrier = new Barrier(2);
            using var cancellation = new CancellationTokenSource();
            GenerationGate.Lease? parent = null;
            int child = -1;
            Task fork = Task.Factory.StartNew(() =>
            {
                try
                {
                    while (true)
                    {
                        barrier.SignalAndWait(cancellation.Token);
                        parent!.TryFork(out child);
                        parent.Dispose();
                        barrier.SignalAndWait(cancellation.Token);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            try
            {
                for (int iteration = 0; iteration < 10000; iteration++)
                {
                    var counter = new StripedCounter();
                    var gate = new GenerationGate(NodeManagerGenerationState.Live, counter);

                    // Model admission on the last real processor stripe, before closing the gate.
                    int parentSlot = counter.LastSlot;
                    counter.Enter(parentSlot);
                    parent = new GenerationGate.Lease(gate, parentSlot);
                    gate.Retire();
                    Assert.That(barrier.SignalAndWait(TimeSpan.FromSeconds(10), cancellation.Token), Is.True);
                    Task drained = gate.WaitForDrainAsync();
                    Assert.That(barrier.SignalAndWait(TimeSpan.FromSeconds(10), cancellation.Token), Is.True);
                    try
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(child, Is.GreaterThanOrEqualTo(0));
                            Assert.That(gate.ActiveCount, Is.EqualTo(1));
                            Assert.That(drained.IsCompleted, Is.False,
                                $"Drain completed during parent-to-child handoff at iteration {iteration}.");
                        });
                    }
                    finally
                    {
                        if (child >= 0)
                        {
                            gate.Exit(child);
                        }
                        parent.Dispose();
                    }
                    await drained.ConfigureAwait(false);
                    gate.DisposeGeneration();
                }
            }
            finally
            {
                cancellation.Cancel();
                await fork.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A refused late entrant cannot invalidate a completed closed-gate drain.
        /// </summary>
        [Test]
        public async Task ConfirmedDrainAllowsDisposalWhileLateEntryIsBeingRefusedAsync()
        {
            var counter = new StripedCounter();
            var gate = new GenerationGate(NodeManagerGenerationState.Live, counter);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
            gate.Retire();
            Task drained = gate.WaitForDrainAsync();
            parent!.Dispose();
            await drained.ConfigureAwait(false);

            // Reproduce the counter state of a fork paused between its increment and parent recheck.
            int pending = counter.Enter();
            try
            {
                Assert.That(gate.DisposeGeneration, Throws.Nothing);
            }
            finally
            {
                counter.Exit(pending);
            }
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
            Assert.That(gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Disposal still refuses accepted work, including after a retired gate has been restored.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GenerationDisposalRequiresAcceptedWorkToDrain(bool restore)
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Live);
            if (restore)
            {
                gate.Retire();
                gate.Restore();
            }
            Assert.That(gate.TryEnter(out GenerationGate.Lease? parent), Is.True);
            gate.Retire();
            try
            {
                Assert.That(gate.DisposeGeneration, Throws.InvalidOperationException);
                Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            }
            finally
            {
                parent!.Dispose();
            }
            gate.DisposeGeneration();
            Assert.That(gate.State, Is.EqualTo(NodeManagerGenerationState.Disposed));
        }

        /// <summary>
        /// Closing an address-space epoch rejects new request pins and waits for the admitted pin.
        /// </summary>
        [Test]
        public async Task ClosedAddressSpaceEpochRefusesNewPinsAndWaitsForExistingPinAsync()
        {
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int slot), Is.True);
            epoch.Close();

            Assert.That(epoch.TryPin(out int _), Is.False);
            Assert.That(epoch.IsDrained, Is.False);
            Task drained = epoch.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);

            epoch.Exit(slot);
            await drained.ConfigureAwait(false);

            Assert.That(epoch.IsDrained, Is.True);
        }

        /// <summary>
        /// Concurrent request pins racing view closure are either rejected or included in the drain.
        /// </summary>
        [Test]
        public async Task RequestAdmissionsRacingViewCloseRemainCountedUntilReleasedAsync()
        {
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int initial), Is.True);
            using var start = new ManualResetEventSlim();
            using var ready = new CountdownEvent(9);
            var pins = new ConcurrentBag<int>();
            Task[] requestPins = new Task[8];
            for (int ii = 0; ii < requestPins.Length; ii++)
            {
                requestPins[ii] = Task.Factory.StartNew(
                    () =>
                    {
                        ready.Signal();
                        start.Wait();
                        if (epoch.TryPin(out int pin))
                        {
                            pins.Add(pin);
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            Task close = Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    epoch.Close();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            ready.Wait();
            start.Set();
            await Task.WhenAll(requestPins.Append(close)).ConfigureAwait(false);
            Assert.That(epoch.TryPin(out int _), Is.False);
            Task drained = epoch.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);
            epoch.Exit(initial);
            Assert.That(drained.IsCompleted, Is.EqualTo(pins.IsEmpty));
            foreach (int pin in pins)
            {
                epoch.Exit(pin);
            }
            await drained.ConfigureAwait(false);

            Assert.That(epoch.IsDrained, Is.True);
        }

        /// <summary>
        /// Concurrent admission racing retirement cannot escape the drain count.
        /// </summary>
        [Test]
        public async Task AdmissionsRacingGenerationRetirementRemainCountedUntilReleasedAsync()
        {
            var gate = new GenerationGate(NodeManagerGenerationState.Live);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? initial), Is.True);
            using var start = new ManualResetEventSlim();
            using var ready = new CountdownEvent(9);
            var leases = new ConcurrentBag<GenerationGate.Lease>();
            Task[] entrants = new Task[8];
            for (int ii = 0; ii < entrants.Length; ii++)
            {
                entrants[ii] = Task.Factory.StartNew(
                    () =>
                    {
                        ready.Signal();
                        start.Wait();
                        if (gate.TryEnter(out GenerationGate.Lease? lease))
                        {
                            leases.Add(lease!);
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            Task retire = Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    gate.Retire();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            ready.Wait();
            start.Set();
            await Task.WhenAll(entrants.Append(retire)).ConfigureAwait(false);
            Assert.That(gate.TryEnter(out GenerationGate.Lease? late), Is.False);
            late?.Dispose();
            Task drained = gate.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);
            initial!.Dispose();
            Assert.That(drained.IsCompleted, Is.EqualTo(leases.IsEmpty));
            foreach (GenerationGate.Lease lease in leases)
            {
                lease.Dispose();
            }
            await drained.ConfigureAwait(false);

            Assert.That(gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Failed and canceled service callbacks release the operation's view pin in cleanup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OperationContextReleasesViewPinAfterExceptionOrCancellationAsync(bool cancel)
        {
            var table = new NodeManagerRoutingTable();
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            AddressSpaceEpoch epoch = context.RoutingSnapshot!.Epoch;

            async Task ExecuteFailedServiceAsync()
            {
                try
                {
                    await Task.Yield();
                    if (cancel)
                    {
                        using var canceled = new CancellationTokenSource();
                        canceled.Cancel();
                        await Task.Delay(Timeout.Infinite, canceled.Token).ConfigureAwait(false);
                    }
                    throw new InvalidOperationException("Controlled service failure.");
                }
                finally
                {
                    context.Dispose();
                }
            }

            Exception? failure = null;
            try
            {
                await ExecuteFailedServiceAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(
                failure,
                Is.TypeOf(cancel ? typeof(TaskCanceledException) : typeof(InvalidOperationException)));
            Assert.That(context.HasRoutingPin, Is.False);
            Assert.That(epoch.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Concurrent admission and context disposal never leave a request pin behind.
        /// </summary>
        [Test]
        public async Task ConcurrentRoutingPinAndContextDisposalReleaseTheViewAsync()
        {
            const int requestCount = 32;
            var table = new NodeManagerRoutingTable();
            var contexts = new OperationContext[requestCount];
            using var start = new ManualResetEventSlim();
            Task[] operations = new Task[requestCount * 2];
            for (int ii = 0; ii < requestCount; ii++)
            {
                var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                contexts[ii] = context;
                operations[ii * 2] = Task.Run(() =>
                {
                    start.Wait();
                    try
                    {
                        context.PinRouting(table);
                    }
                    catch (ObjectDisposedException)
                    {
                        Assert.That(context.HasRoutingPin, Is.False);
                    }
                });
                operations[ii * 2 + 1] = Task.Run(() =>
                {
                    start.Wait();
                    context.Dispose();
                });
            }

            start.Set();
            await Task.WhenAll(operations).ConfigureAwait(false);

            Assert.That(table.Revision.Epoch.ActiveCount, Is.Zero);
            foreach (OperationContext context in contexts)
            {
                Assert.That(context.HasRoutingPin, Is.False);
                Assert.That(context.RoutingSnapshot, Is.Null);
            }
        }

        /// <summary>
        /// A retired view drains independently of requests pinned to its replacement view.
        /// </summary>
        [Test]
        public async Task RetiredViewDrainIgnoresNewerViewRequestPinsAsync()
        {
            IAsyncNodeManager firstManager = new Mock<IAsyncNodeManager>().Object;
            IAsyncNodeManager secondManager = new Mock<IAsyncNodeManager>().Object;
            NodeManagerGeneration first = NodeManagerGenerationRegistry.GetOrCreate(firstManager);
            NodeManagerGeneration second = NodeManagerGenerationRegistry.GetOrCreate(secondManager);
            var firstView = CreateView(firstManager);
            var secondView = CreateView(secondManager);

            Assert.That(firstView.TryPin(out int pin), Is.True);
            Assert.That(secondView.TryPin(out int currentPin), Is.True);
            firstView.Epoch.Close();
            first.Gate.Retire();

            Task firstDrained = first.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
            Assert.That(firstDrained.IsCompleted, Is.False);

            firstView.Epoch.Exit(pin);
            await firstDrained.ConfigureAwait(false);
            Assert.That(secondView.Epoch.ActiveCount, Is.EqualTo(1),
                "A request pinned to the newer view must not delay the retired generation.");

            secondView.Epoch.Close();
            second.Gate.Retire();
            Task secondDrained = second.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
            Assert.That(secondDrained.IsCompleted, Is.False);
            secondView.Epoch.Exit(currentPin);
            await secondDrained.ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a master node manager over an empty routing table that a test populates directly.
        /// </summary>
        private static NodeManagerRoutingTable CreateRoutedMaster(
            Mock<IServerInternal> server,
            out MasterNodeManager master)
        {
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(server.Object.CoreNodeManager);
            server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
            var routing = new NodeManagerRoutingTable();
            master = new MasterNodeManager(
                server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                routing);
            MasterNodeManager created = master;
            server.SetupGet(value => value.NodeManager).Returns(created);
            return routing;
        }

        private static NodeManagerRoutingTable.RoutingSnapshot CreateView(IAsyncNodeManager manager)
        {
            return new NodeManagerRoutingTable.RoutingSnapshot(
                [manager],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                []);
        }
    }
}
