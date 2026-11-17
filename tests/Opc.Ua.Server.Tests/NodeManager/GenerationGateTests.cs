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
        /// A raw binding created before or after publication retains its issuer while its emissions enter
        /// the published facade's gate.
        /// </summary>
        /// <param name="bindBeforePublication">
        /// Whether the raw resource is bound before the facade is published.
        /// </param>
        [TestCase(false)]
        [TestCase(true)]
        public void ForwardedResourceEmissionsUsePublishedGate(bool bindBeforePublication)
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
                    NodeManagerGenerationReference? originalReference = monitored?.Generation;
                    NodeManagerGeneration? originalGeneration = null;
                    try
                    {
                        if (monitored is not null)
                        {
                            Assert.That(
                                monitored.Generation.TryGetGeneration(out originalGeneration),
                                Is.True);
                            bool admitted = MasterNodeManager.TryCaptureItemEmission(
                                server.Object, resource.Object, monitored.Generation, out var standalone);
                            try
                            {
                                Assert.That(admitted, Is.True);
                                Assert.That(standalone?.Generation, Is.SameAs(originalGeneration));
                                Assert.That(standalone?.IssuerGeneration, Is.SameAs(originalGeneration));
                            }
                            finally
                            {
                                standalone?.Dispose();
                            }
                        }

                        routing.Add(facade.Object, [1]);
                        NodeManagerGeneration published = routing.Revision.GetGeneration(facade.Object);
                        if (bindBeforePublication)
                        {
                            Assert.That(published.ForwardedSourceGeneration, Is.SameAs(originalGeneration));
                        }
                        monitored ??= new MonitoredNode2(resource.Object, server.Object, root);
                        NodeManagerGenerationReference reference = monitored.Generation;
                        Assert.That(reference.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);

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
                                Assert.That(bound!.NodeManager, Is.SameAs(resource.Object));
                                Assert.That(bound, Is.Not.SameAs(published));
                                Assert.That(bound.Gate, Is.Not.SameAs(published.Gate));
                                if (bindBeforePublication)
                                {
                                    Assert.That(reference, Is.SameAs(originalReference));
                                    Assert.That(bound, Is.SameAs(originalGeneration));
                                }
                                Assert.That(eventAccepted, Is.True);
                                Assert.That(notification?.Generation, Is.SameAs(published));
                                Assert.That(notification?.IssuerGeneration, Is.SameAs(bound));
                                Assert.That(itemAccepted, Is.True);
                                Assert.That(item?.Generation, Is.SameAs(published));
                                Assert.That(item?.IssuerGeneration, Is.SameAs(bound));
                                Assert.That(unboundAccepted, Is.True);
                                Assert.That(unbound?.Generation, Is.SameAs(published));
                                Assert.That(unbound?.IssuerGeneration, Is.SameAs(bound));
                                Assert.That(published.ForwardedSourceGeneration, Is.SameAs(bound));
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
        /// Private raw and facade bindings retain their identities through publication and retirement.
        /// </summary>
        /// <param name="facadeFirst">Whether the facade is bound before the raw resource.</param>
        /// <param name="replace">Whether publication replaces an existing registration.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task PrivatePreboundSourcesRouteOnlyTheirExactPublishedFacadeAsync(bool facadeFirst, bool replace)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                server.SetupGet(value => value.MessageContext).Returns(
                    new ServiceMessageContext(server.Object.Telemetry, server.Object.Factory)
                    {
                        NamespaceUris = server.Object.NamespaceUris,
                        ServerUris = server.Object.ServerUris
                    });
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    var previous = new Mock<IAsyncNodeManager>();
                    previous.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                    if (replace)
                    {
                        routing.Add(previous.Object, [1]);
                    }
                    var raw = new Mock<IAsyncNodeManager>();
                    var synchronous = new SyncNodeManagerAdapter(raw.Object);
                    raw.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                    raw.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                    var facade = new Mock<IAsyncNodeManager>();
                    facade.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                    facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                    var hiddenAlias = new Mock<IAsyncNodeManager>();
                    hiddenAlias.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                    NodeManagerGeneration? rawSource = null;
                    NodeManagerGeneration? facadeSource = null;
                    NodeManagerGeneration? hiddenSource = null;
                    IServerInternal? staging = null;
                    var factory = new Mock<IAsyncNodeManagerFactory>();
                    factory.Setup(value => value.CreateAsync(
                            It.IsAny<IServerInternal>(),
                            It.IsAny<ApplicationConfiguration>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                        {
                            staging = candidate;
                            if (facadeFirst)
                            {
                                facadeSource = NodeManagerGenerationRegistry.GetOrCreate(facade.Object, candidate);
                                rawSource = NodeManagerGenerationRegistry.GetOrCreate(raw.Object, candidate);
                            }
                            else
                            {
                                rawSource = NodeManagerGenerationRegistry.GetOrCreate(raw.Object, candidate);
                                facadeSource = NodeManagerGenerationRegistry.GetOrCreate(facade.Object, candidate);
                            }
                            hiddenSource = NodeManagerGenerationRegistry.GetOrCreate(hiddenAlias.Object, candidate);
                            return new ValueTask<IAsyncNodeManager>(facade.Object);
                        });
                    var preparation = new ChangePreparer(server.Object);
                    IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                        factory.Object, null!, replace, _ => false, CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        Assert.That(rawSource, Is.Not.Null);
                        Assert.That(facadeSource, Is.Not.Null);
                        Assert.That(hiddenSource, Is.Not.Null);
                        Assert.That(staging, Is.Not.Null);
                        Assert.That(MasterNodeManager.TryCaptureItemEmission(
                            staging!, raw.Object, rawSource!.Reference, out var beforeRaw), Is.False);
                        beforeRaw?.Dispose();
                        Assert.That(MasterNodeManager.TryCaptureItemEmission(
                            staging!, facade.Object, facadeSource!.Reference, out var beforeFacade), Is.False);
                        beforeFacade?.Dispose();

                        if (replace)
                        {
                            routing.Replace(previous.Object, facade.Object, [1]);
                        }
                        else
                        {
                            routing.Add(facade.Object, [1]);
                        }
                        NodeManagerGeneration published = routing.Revision.GetGeneration(facade.Object);
                        Assert.That(published.Gate, Is.SameAs(rawSource.Gate));
                        Assert.That(published.Gate, Is.SameAs(facadeSource.Gate));
                        Assert.That(published.Gate, Is.SameAs(hiddenSource!.Gate));
                        if (replace)
                        {
                            Assert.That(published, Is.Not.SameAs(facadeSource));
                            Assert.That(published.PrivateFacadeGeneration, Is.SameAs(facadeSource));
                        }

                        bool rawAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            staging!, raw.Object, out var rawAdmission, rawSource.Reference);
                        using MasterNodeManager.SourceEmissionOrigin? origin = rawAdmission?.TransferToOrigin();
                        bool facadeAccepted = MasterNodeManager.TryCaptureItemEmission(
                            staging!, facade.Object, facadeSource.Reference, out var facadeAdmission);
                        bool hiddenAccepted = MasterNodeManager.TryCaptureItemEmission(
                            staging!, hiddenAlias.Object, hiddenSource.Reference, out var hiddenAdmission);
                        try
                        {
                            Assert.Multiple(() =>
                            {
                                Assert.That(rawAccepted, Is.True);
                                Assert.That(rawAdmission?.Generation, Is.SameAs(published));
                                Assert.That(rawAdmission?.IssuerGeneration, Is.SameAs(rawSource));
                                Assert.That(facadeAccepted, Is.True);
                                Assert.That(facadeAdmission?.Generation, Is.SameAs(published));
                                Assert.That(facadeAdmission?.IssuerGeneration, Is.SameAs(facadeSource));
                                Assert.That(hiddenAccepted, Is.False);
                                Assert.That(hiddenAdmission, Is.Null);
                            });
                        }
                        finally
                        {
                            facadeAdmission?.Dispose();
                            hiddenAdmission?.Dispose();
                        }

                        Assert.That(origin, Is.Not.Null);
                        ISystemContext context = origin!.CreateContext(staging!.DefaultSystemContext);
                        routing.Remove(facade.Object);
                        bool freshAccepted = MasterNodeManager.TryCaptureItemEmission(
                            staging, raw.Object, rawSource.Reference, out var freshAdmission);
                        bool childAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            staging, facade.Object, out var child, facadeSource.Reference, context: context);
                        try
                        {
                            Assert.Multiple(() =>
                            {
                                Assert.That(freshAccepted, Is.False);
                                Assert.That(childAccepted, Is.True);
                                Assert.That(child?.Generation, Is.SameAs(published));
                                Assert.That(child?.IssuerGeneration, Is.SameAs(rawSource));
                                Assert.That(published.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                            });
                        }
                        finally
                        {
                            freshAdmission?.Dispose();
                            child?.Dispose();
                        }
                    }
                    finally
                    {
                        await ChangePreparer.DisposeAsync(created).ConfigureAwait(false);
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
        /// Concurrent raw binding and facade publication preserve the issuer and route through the facade.
        /// </summary>
        [Test]
        public async Task RawBindingRacingForwardedPublicationPreservesIssuerAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    for (int iteration = 0; iteration < 24; iteration++)
                    {
                        var resource = new Mock<IAsyncNodeManager>();
                        resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                        resource.SetupGet(value => value.SyncNodeManager)
                            .Returns(new SyncNodeManagerAdapter(resource.Object));
                        var facade = new Mock<IAsyncNodeManager>();
                        facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                        facade.SetupGet(value => value.SyncNodeManager)
                            .Returns(new SyncNodeManagerAdapter(resource.Object));
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
                        await Task.WhenAll(binding, publication).WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);

                        NodeManagerGeneration bound = await binding.ConfigureAwait(false);
                        NodeManagerGeneration published = await publication.ConfigureAwait(false);
                        bool admitted = MasterNodeManager.TryCaptureItemEmission(
                            server.Object, resource.Object, bound.Reference, out var emission);
                        try
                        {
                            Assert.That(bound.NodeManager, Is.SameAs(resource.Object));
                            Assert.That(bound, Is.Not.SameAs(published));
                            Assert.That(bound.Gate, Is.Not.SameAs(published.Gate));
                            Assert.That(admitted, Is.True, $"Iteration {iteration}.");
                            Assert.That(emission?.Generation, Is.SameAs(published));
                            Assert.That(emission?.IssuerGeneration, Is.SameAs(bound));

                            routing.Remove(facade.Object);
                            Task drain = published.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                            Assert.That(drain.IsCompleted, Is.False);
                            bool admittedAfterRemoval = MasterNodeManager.TryCaptureItemEmission(
                                server.Object, resource.Object, bound.Reference, out var rejected);
                            rejected?.Dispose();
                            Assert.That(admittedAfterRemoval, Is.False);
                            emission?.Dispose();
                            emission = null;
                            await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        }
                        finally
                        {
                            emission?.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A notification queued before publication delays facade retirement through removal or replacement.
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
                    await using var monitored = new MonitoredNode2(resource.Object, server.Object, root);
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
                        Assert.That(bound!.NodeManager, Is.SameAs(resource.Object));
                        Assert.That(bound, Is.Not.SameAs(published));
                        Assert.That(bound.Gate, Is.Not.SameAs(published.Gate));

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

                        await published.Gate.WaitForDrainAsync().WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                        drain = published.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
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
        /// A hidden custom facade prevents its raw emitter from using a live legacy generation.
        /// </summary>
        [Test]
        public void HiddenCustomFacadeDoesNotAdmitBoundEmitterThroughLegacyFallback()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var source = new Mock<IAsyncNodeManager>();
                source.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                source.SetupGet(manager => manager.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(source.Object));
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(source.Object);

                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(manager => manager.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(manager => manager.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(source.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    routing.Add(facade.Object, [], visible: false);

                    bool accepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, source.Object, out var admission, generation.Reference);

                    try
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(accepted, Is.False);
                            Assert.That(admission, Is.Null);
                            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                        });
                    }
                    finally
                    {
                        admission?.Dispose();
                    }
                }
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
        /// An unbound adapter alias cannot select among distinct visible owners of one resource.
        /// </summary>
        /// <remarks>
        /// The routing table constructs concurrent owners directly; lifecycle registration is not exercised.
        /// </remarks>
        /// <param name="rawFirst">Whether the raw owner is published before the borrowed facade.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnboundAdapterAliasRejectsDistinctBorrowedOwnersAsync(bool rawFirst)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                var synchronous = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                var facade = new AsyncNodeManagerAdapter(synchronous);
                var alias = new AsyncNodeManagerAdapter(synchronous);
                var lease = new Mock<INodeManagerBorrowedResourceLease>();
                lease.SetupGet(value => value.NodeManager).Returns(facade);
                lease.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                lease.Setup(value => value.RetireAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                var factory = new Mock<INodeManagerBorrowedResourceFactory>();
                factory.Setup(value => value.CreateBorrowedAsync(
                        It.IsAny<INodeManagerBorrowedResourcePreparation>(),
                        It.IsAny<ApplicationConfiguration>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((INodeManagerBorrowedResourcePreparation context, ApplicationConfiguration _,
                        CancellationToken _) =>
                    {
                        context.DeclareCandidate(lease.Object);
                        return default;
                    });
                var preparation = new ChangePreparer(server.Object);
                IAsyncNodeManager candidate = await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
                BorrowedNodeManagerLeaseState candidateLease = ChangePreparer.GetBorrowedLease(candidate)!;
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    bool facadeRouted = false;
                    bool published = false;
                    try
                    {
                        if (rawFirst)
                        {
                            routing.Add(resource.Object, [1]);
                        }
                        routing.Add(candidate, [1]);
                        facadeRouted = true;
                        candidateLease.MarkPublished();
                        published = true;
                        if (!rawFirst)
                        {
                            routing.Add(resource.Object, [1]);
                        }

                        NodeManagerGeneration borrowed = routing.Revision.GetGeneration(candidate);
                        Assert.Multiple(() =>
                        {
                            Assert.That(NodeManagerGenerationRegistry.GetIdentity(alias), Is.SameAs(resource.Object));
                            Assert.That(NodeManagerGenerationRegistry.GetIdentity(candidate), Is.SameAs(candidate));
                            Assert.That(routing.Revision.GetGeneration(resource.Object), Is.SameAs(raw));
                            Assert.That(borrowed, Is.Not.SameAs(raw));
                            Assert.That(borrowed.Gate, Is.Not.SameAs(raw.Gate));
                            Assert.That(borrowed.ForwardedSourceGeneration, Is.SameAs(raw));
                            Assert.That(borrowed.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                            Assert.That(raw.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                        });

                        bool ambiguousAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, alias, out var ambiguous);
                        try
                        {
                            Assert.That(ambiguousAccepted, Is.False);
                            Assert.That(ambiguous, Is.Null);
                        }
                        finally
                        {
                            ambiguous?.Dispose();
                        }

                        routing.Remove(candidate);
                        facadeRouted = false;
                        bool rawAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, alias, out var admission);
                        try
                        {
                            Assert.That(rawAccepted, Is.True);
                            Assert.That(admission?.Generation, Is.SameAs(raw));
                            Assert.That(admission?.IssuerGeneration, Is.SameAs(raw));
                        }
                        finally
                        {
                            admission?.Dispose();
                        }
                    }
                    finally
                    {
                        if (facadeRouted)
                        {
                            routing.Remove(candidate);
                        }
                        if (published)
                        {
                            await candidateLease.RetireAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            await candidateLease.RollbackAsync().ConfigureAwait(false);
                        }
                    }
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
        /// A view with no current pins remains reachable until it closes.
        /// </summary>
        [Test]
        public async Task OpenAddressSpaceEpochDoesNotCompleteDrainBeforeCloseAsync()
        {
            var epoch = new AddressSpaceEpoch();
            Task drained = epoch.WaitForDrainAsync();
            Assert.That(drained.IsCompleted, Is.False);
            Assert.That(epoch.TryPin(out int slot), Is.True);
            try
            {
                epoch.Close();
                Assert.That(epoch.TryPin(out int _), Is.False);
                Assert.That(drained.IsCompleted, Is.False);
            }
            finally
            {
                epoch.Exit(slot);
            }
            await drained.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(epoch.IsDrained, Is.True);
        }

        /// <summary>
        /// A retired generation waits for an open routing view even before its first pin.
        /// </summary>
        [Test]
        public async Task RetiredGenerationWaitsForOpenRoutingViewAsync()
        {
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            NodeManagerRoutingTable.RoutingSnapshot view = CreateView(manager);
            generation.Gate.Retire();
            try
            {
                Task drained = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                Assert.That(drained.IsCompleted, Is.False);
                Assert.That(view.TryPin(out int pin), Is.True);
                try
                {
                    view.Epoch.Close();
                    Assert.That(drained.IsCompleted, Is.False);
                }
                finally
                {
                    view.Epoch.Exit(pin);
                }
                await drained.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(generation.IsQuiescent, Is.True);
                Assert.Throws<InvalidOperationException>(
                    () => generation.RegisterViewEpoch(new AddressSpaceEpoch()));
                Assert.Throws<InvalidOperationException>(
                    () => generation.RegisterSourceEpoch(new AddressSpaceEpoch()));
            }
            finally
            {
                view.Epoch.Close();
            }
        }

        /// <summary>
        /// Abandoned proposals release only their own registrations, including while a waiter is active.
        /// </summary>
        [Test]
        public async Task AbandonedSourceEpochReleasesPendingQuiescenceAsync()
        {
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(manager);
            var source = new AddressSpaceEpoch();
            generation.RegisterSourceEpoch(source);
            generation.RegisterSourceEpoch(source);
            generation.Gate.Abort();
            Task drained = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
            Assert.That(drained.IsCompleted, Is.False);

            generation.UnregisterSourceEpoch(source);
            Assert.That(generation.IsQuiescent, Is.False);
            generation.UnregisterSourceEpoch(source);

            await drained.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(generation.IsQuiescent, Is.True);
            Assert.That(source.IsDrained, Is.False);
            Assert.Throws<InvalidOperationException>(() => generation.RegisterSourceEpoch(source));
        }

        /// <summary>
        /// A view that cannot register every generation closes its partially registered epoch.
        /// </summary>
        [Test]
        public async Task FailedRoutingViewClosesPartiallyRegisteredEpochAsync()
        {
            IAsyncNodeManager firstManager = new Mock<IAsyncNodeManager>().Object;
            IAsyncNodeManager retiredManager = new Mock<IAsyncNodeManager>().Object;
            NodeManagerGeneration first = NodeManagerGenerationRegistry.GetOrCreate(firstManager);
            NodeManagerGeneration retired = NodeManagerGenerationRegistry.GetOrCreate(retiredManager);
            retired.Gate.Retire();

            Assert.Throws<InvalidOperationException>(() => new NodeManagerRoutingTable.RoutingSnapshot(
                [firstManager, retiredManager],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                []));
            first.Gate.Retire();
            await first.WaitForQuiescenceAsync(CancellationToken.None).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(first.IsQuiescent, Is.True);
        }

        /// <summary>
        /// Child work retains a closed routing view after its parent releases the original pin.
        /// </summary>
        [Test]
        public async Task ClosedAddressSpaceEpochRetainsForkedPinUntilChildCompletesAsync()
        {
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int slot), Is.True);
            using var parent = new AddressSpaceEpoch.Lease(epoch, slot);
            epoch.Close();
            Assert.That(epoch.TryPin(out int _), Is.False);
            Assert.That(parent.TryFork(out AddressSpaceEpoch? childEpoch, out int childSlot), Is.True);
            Assert.That(childEpoch, Is.SameAs(epoch));

            Task drain = epoch.WaitForDrainAsync();
            parent.Dispose();
            Assert.That(drain.IsCompleted, Is.False);
            Assert.That(parent.TryFork(out _, out _), Is.False);
            childEpoch!.Exit(childSlot);
            await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(epoch.IsDrained, Is.True);
        }

        /// <summary>
        /// A facade retains source work admitted before intervening routing publications.
        /// </summary>
        [Test]
        public async Task FacadeRetirementWaitsForEarlierSourceViewAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var admission, raw.Reference), Is.True);
                    try
                    {
                        var unrelated = new Mock<IAsyncNodeManager>();
                        unrelated.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                        routing.Add(unrelated.Object, [2]);
                        routing.Remove(unrelated.Object);
                        var facade = new Mock<IAsyncNodeManager>();
                        facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                        facade.SetupGet(value => value.SyncNodeManager)
                            .Returns(new SyncNodeManagerAdapter(resource.Object));
                        routing.Add(facade.Object, [1]);
                        NodeManagerGeneration published = routing.Revision.GetGeneration(facade.Object);
                        routing.Remove(facade.Object);

                        await published.Gate.WaitForDrainAsync().WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                        Assert.That(published.Gate.ActiveCount, Is.Zero);
                        Task drain = published.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                        Assert.That(drain.IsCompleted, Is.False);
                        admission?.Dispose();
                        admission = null;
                        await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    finally
                    {
                        admission?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// A source admitted after route reservation remains owned by the incoming facade.
        /// </summary>
        [Test]
        public async Task FacadeRetirementWaitsForSourceAdmittedAfterReservationAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                    var candidate = new PreparedNodeManager(facade.Object, [], generation);
                    using NodeManagerRoutingTable.PreparedRoutes routes = routing.PrepareBatch(
                        [candidate],
                        [],
                        _ => [1],
                        new TypeTable(new NamespaceTable()),
                        (EncodeableFactory)EncodeableFactory.Create());
                    routes.Reserve();
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var admission, raw.Reference), Is.True);
                    try
                    {
                        routes.Publish();
                        candidate.Publish();
                        routes.Dispose();
                        routing.Remove(facade.Object);

                        await generation.Gate.WaitForDrainAsync().WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                        Task drain = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                        Assert.That(drain.IsCompleted, Is.False);
                        admission?.Dispose();
                        admission = null;
                        await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    finally
                    {
                        admission?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// An abandoned routing reservation does not retain source work for an unpublished candidate.
        /// </summary>
        [Test]
        public async Task AbandonedRoutingReservationReleasesSourceEpochAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var admission, raw.Reference), Is.True);
                    NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                    var candidate = new PreparedNodeManager(facade.Object, [], generation);
                    using NodeManagerRoutingTable.PreparedRoutes routes = routing.PrepareBatch(
                        [candidate], [], _ => [1],
                        new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());
                    try
                    {
                        routes.Reserve();
                        routes.Dispose();
                        routes.Dispose();
                        Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                        generation.Gate.Abort();

                        await generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        Assert.That(generation.IsQuiescent, Is.True);
                        Assert.That(raw.Gate.ActiveCount, Is.GreaterThan(0));
                        Assert.That(admission.HasValue, Is.True);
                    }
                    finally
                    {
                        admission?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Discarding an older routing proposal preserves a candidate reserved by its successor.
        /// </summary>
        [Test]
        public async Task AbandonedProposalDoesNotAbortReservedCandidateAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var admission, raw.Reference), Is.True);
                    NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                    var candidate = new PreparedNodeManager(facade.Object, [], generation);
                    using NodeManagerRoutingTable.PreparedRoutes stale = routing.PrepareBatch(
                        [candidate], [], _ => [1],
                        new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());
                    using NodeManagerRoutingTable.PreparedRoutes current = routing.PrepareBatch(
                        [candidate], [], _ => [1],
                        new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());
                    try
                    {
                        current.Reserve();
                        stale.Dispose();
                        Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                        current.Publish();
                        candidate.Publish();
                        current.Dispose();
                        routing.Remove(facade.Object);

                        await generation.Gate.WaitForDrainAsync().WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                        Task drain = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                        Assert.That(drain.IsCompleted, Is.False);
                        admission?.Dispose();
                        admission = null;
                        await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                    finally
                    {
                        admission?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// An admission for another resource on the same server does not delay retirement.
        /// </summary>
        [Test]
        public async Task FacadeRetirementDoesNotWaitForAnotherResourceSourceAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var otherResource = new Mock<IAsyncNodeManager>();
                otherResource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                NodeManagerGeneration other = NodeManagerGenerationRegistry.GetOrCreate(otherResource.Object);
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, otherResource.Object, out var admission, other.Reference), Is.True);
                    try
                    {
                        routing.Add(facade.Object, [1]);
                        NodeManagerGeneration generation = routing.Revision.GetGeneration(facade.Object);
                        routing.Remove(facade.Object);

                        await generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        Assert.That(other.Gate.ActiveCount, Is.GreaterThan(0));
                        Assert.That(admission.HasValue, Is.True);
                    }
                    finally
                    {
                        admission?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// A stale routing view cannot register source work after publication.
        /// </summary>
        [Test]
        public void StaleRoutingViewCannotPinResourceSourceEpoch()
        {
            var routing = new NodeManagerRoutingTable();
            NodeManagerRoutingTable.RoutingSnapshot previous = routing.Revision;
            IAsyncNodeManager resource = Mock.Of<IAsyncNodeManager>();
            IAsyncNodeManager added = Mock.Of<IAsyncNodeManager>();
            routing.Add(added, [1]);

            NodeManagerRoutingTable.SourceEpochAdmission admission =
                routing.TryPinSourceEpoch(previous, resource, true, out AddressSpaceEpoch? epoch, out int slot);
            Assert.Multiple(() =>
            {
                Assert.That(admission, Is.EqualTo(NodeManagerRoutingTable.SourceEpochAdmission.StaleView));
                Assert.That(epoch, Is.Null);
                Assert.That(slot, Is.EqualTo(-1));
            });
        }

        /// <summary>
        /// A raw source is admitted only through an unambiguous owner before and after facade retirement.
        /// </summary>
        /// <remarks>
        /// The routing table constructs concurrent owners directly; lifecycle registration is not exercised.
        /// </remarks>
        /// <param name="rawRouted">Whether the raw owner remains visible in routing.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredFacadeCannotReviveUnroutedRawSourceAsync(bool rawRouted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                resource.Setup(value => value.ValidateEventRolePermissionsAsync(
                        It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerRoutingTable routing = CreateRoutedMaster(server, out MasterNodeManager master);
                using (master)
                {
                    if (rawRouted)
                    {
                        routing.Add(resource.Object, [1]);
                    }
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var before, raw.Reference), Is.True);
                    before?.Dispose();

                    var root = new BaseObjectState(null) { NodeId = new NodeId(44, 1) };
                    await using var monitored = new MonitoredNode2(resource.Object, server.Object, root);
                    var item = new Mock<IEventMonitoredItem>();
                    item.SetupGet(value => value.Id).Returns(1);
                    monitored.Add(item.Object);
                    Assert.That(monitored.Generation.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                    Assert.That(bound, Is.SameAs(raw));
                    await AssertEventDeliveryAsync(Mock.Of<IFilterTarget>(), expected: true).ConfigureAwait(false);

                    routing.Add(facade.Object, [1]);
                    NodeManagerGeneration published = routing.Revision.GetGeneration(facade.Object);
                    bool concurrentAdmitted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var concurrent, raw.Reference);
                    try
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(concurrentAdmitted, Is.EqualTo(!rawRouted));
                            Assert.That(concurrent?.Generation, Is.SameAs(rawRouted ? null : published));
                            Assert.That(concurrent?.IssuerGeneration, Is.SameAs(rawRouted ? null : raw));
                        });
                    }
                    finally
                    {
                        concurrent?.Dispose();
                    }
                    await AssertEventDeliveryAsync(Mock.Of<IFilterTarget>(), expected: !rawRouted)
                        .ConfigureAwait(false);
                    routing.Remove(facade.Object);

                    bool admitted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out var after, raw.Reference);
                    try
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(admitted, Is.EqualTo(rawRouted));
                            Assert.That(after?.Generation, Is.SameAs(rawRouted ? raw : null));
                            Assert.That(raw.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                        });
                    }
                    finally
                    {
                        after?.Dispose();
                    }
                    await AssertEventDeliveryAsync(Mock.Of<IFilterTarget>(), expected: rawRouted)
                        .ConfigureAwait(false);

                    async Task AssertEventDeliveryAsync(IFilterTarget target, bool expected)
                    {
                        await monitored.OnReportEventAsync(
                            server.Object.DefaultSystemContext, root, target, timeout.Token).ConfigureAwait(false);
                        await monitored.WaitForPendingNotificationsAsync(timeout.Token).ConfigureAwait(false);
                        item.Verify(value => value.QueueEvent(target), expected ? Times.Once() : Times.Never());
                    }
                }
            }
        }

        /// <summary>
        /// A source admission owned by another server does not delay a facade's retirement.
        /// </summary>
        [Test]
        public async Task FacadeRetirementDoesNotWaitForAnotherServersSourceViewAsync()
        {
            Mock<IServerInternal> firstServer =
                DeterministicServerMock.Create(out MonitoredItemQueueFactory firstQueues);
            Mock<IServerInternal> secondServer =
                DeterministicServerMock.Create(out MonitoredItemQueueFactory secondQueues);
            using (firstQueues)
            using (secondQueues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                resource.SetupGet(value => value.SyncNodeManager)
                    .Returns(new SyncNodeManagerAdapter(resource.Object));
                NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
                NodeManagerRoutingTable firstRouting =
                    CreateRoutedMaster(firstServer, out MasterNodeManager firstMaster);
                _ = CreateRoutedMaster(secondServer, out MasterNodeManager secondMaster);
                using (firstMaster)
                using (secondMaster)
                {
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        secondServer.Object, resource.Object, out var otherAdmission, raw.Reference), Is.True);
                    try
                    {
                        var facade = new Mock<IAsyncNodeManager>();
                        facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                        facade.SetupGet(value => value.SyncNodeManager)
                            .Returns(new SyncNodeManagerAdapter(resource.Object));
                        firstRouting.Add(facade.Object, [1]);
                        NodeManagerGeneration published = firstRouting.Revision.GetGeneration(facade.Object);
                        firstRouting.Remove(facade.Object);

                        await published.WaitForQuiescenceAsync(CancellationToken.None).AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        Assert.That(raw.Gate.ActiveCount, Is.GreaterThan(0));
                        Assert.That(otherAdmission.HasValue, Is.True);
                    }
                    finally
                    {
                        otherAdmission?.Dispose();
                    }
                }
            }
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
