// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Test doubles implement or inspect the legacy IAsyncNodeManager.SyncNodeManager view.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed partial class NodeManagerRoutingTableTests
    {
        private static readonly int[] AddNamespaceIndexes = [2, 2, 3];
        private static readonly int[] ReplaceNamespaceIndexes = [3, 4];
        private static readonly int[] InitialNamespaceIndexes = [2, 3];
        private static readonly int[] ConcurrentNamespaceIndexes = [9];
        private static readonly int[] BatchNamespaceIndexes = [2];

        /// <summary>
        /// A failed routing publication preserves the serving source epoch and legacy source admission.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedRoutingPublicationPreservesSourceAdmissionAsync(bool prepared)
        {
            var historian = new Mock<IHistorianProviderRegistry>();
            Mock<IHistorianOwnedProviderRegistry> ownedHistorian =
                historian.As<IHistorianOwnedProviderRegistry>();
            var server = new Mock<IServerInternal>();
            server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry)
                .Returns(historian.Object);
            var namespaces = new NamespaceTable();
            var serverUris = new StringTable();
            var telemetry = new Mock<ITelemetryContext>().Object;
            var encodeableFactory = (EncodeableFactory)EncodeableFactory.Create();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(serverUris);
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(encodeableFactory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MessageContext)
                .Returns(new ServiceMessageContext(telemetry, encodeableFactory)
                {
                    NamespaceUris = namespaces,
                    ServerUris = serverUris
                });
            server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(server.Object));

            var configurationManager = new Mock<IConfigurationNodeManager>();
            var coreManager = new Mock<ICoreNodeManager>();
            var mainFactory = new Mock<IMainNodeManagerFactory>();
            mainFactory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(configurationManager.Object);
            mainFactory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(coreManager.Object);
            server.SetupGet(value => value.MainNodeManagerFactory).Returns(mainFactory.Object);
            server.SetupGet(value => value.CoreNodeManager).Returns(coreManager.Object);
            var table = new NodeManagerRoutingTable();
            await using var master = new MasterNodeManager(
                server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                table);
            server.SetupGet(value => value.NodeManager).Returns(master);

            var manager = new Mock<IAsyncNodeManager>();
            _ = manager.As<INodeManagerReadinessNotification>();
            manager.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(manager.Object));
            IAsyncNodeManager candidate = await new ChangePreparer(server.Object).CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            var resource = new Mock<IAsyncNodeManager>();
            resource.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
            resource.SetupGet(value => value.SyncNodeManager)
                .Returns(new SyncNodeManagerAdapter(resource.Object));
            NodeManagerGeneration raw = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);
            var failure = new InvalidOperationException("Historian retirement failed.");
            bool failRetirement = true;
            bool registered = false;
            NodeManagerRoutingTable.PreparedRoutes? routes = null;
            MasterNodeManager.SourceEmissionAdmission? beforeEmission = null;
            MasterNodeManager.SourceEmissionAdmission? afterEmission = null;
            AddressSpaceEpoch? beforeFailure = null;
            AddressSpaceEpoch? afterFailure = null;
            int beforeSlot = -1;
            int afterSlot = -1;
            ownedHistorian.Setup(value => value.RemoveOwnedRegistrations(It.IsAny<object>()))
                .Callback<object>(_ =>
                {
                    if (failRetirement)
                    {
                        throw failure;
                    }
                });
            try
            {
                ChangePreparer.StagingServer staging = ChangePreparer.GetServer(candidate)!;
                ((IHistorianRegistryProvider)staging).HistorianRegistry.RegisterDefault(
                    new Mock<IHistorianProvider>().Object);
                ChangePreparer.Bind(candidate);
                Assert.That(ChangePreparer.PublishHistorianRegistrations(candidate), Is.True);
                table.Add(candidate, BatchNamespaceIndexes);
                registered = true;
                NodeManagerGeneration generation = table.GetGeneration(candidate);
                NodeManagerRoutingTable.RoutingSnapshot serving = table.Revision;
                Assert.That(
                    serving.TryPin(out beforeSlot),
                    Is.True);
                beforeFailure = serving.Epoch;
                Assert.That(
                    MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out beforeEmission, raw.Reference),
                    Is.True);

                if (prepared)
                {
                    var facade = new Mock<IAsyncNodeManager>();
                    facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                    facade.SetupGet(value => value.SyncNodeManager)
                        .Returns(new SyncNodeManagerAdapter(resource.Object));
                    NodeManagerGeneration facadeGeneration =
                        NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                    var preparedFacade = new PreparedNodeManager(facade.Object, [], facadeGeneration);
                    routes = table.PrepareBatch(
                        [preparedFacade], [candidate], _ => BatchNamespaceIndexes,
                        new TypeTable(new NamespaceTable()), encodeableFactory);
                    routes.Reserve();
                }

                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () =>
                    {
                        if (routes is null)
                        {
                            table.Remove(candidate);
                        }
                        else
                        {
                            routes.Publish();
                        }
                    });
                routes?.Dispose();
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(failure));
                    Assert.That(table.Revision, Is.SameAs(serving));
                    Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                });
                Assert.That(
                    table.Revision.TryPin(out afterSlot),
                    Is.True);
                afterFailure = table.Revision.Epoch;
                bool admittedAfterFailure = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, resource.Object, out afterEmission, raw.Reference);
                Assert.Multiple(() =>
                {
                    Assert.That(afterFailure, Is.SameAs(beforeFailure));
                    Assert.That(admittedAfterFailure, Is.True,
                        "A failed facade publication must not disable the serving legacy source.");
                });

                beforeFailure!.Exit(beforeSlot);
                beforeSlot = -1;
                afterFailure!.Exit(afterSlot);
                afterSlot = -1;
                beforeEmission?.Dispose();
                beforeEmission = null;
                afterEmission?.Dispose();
                afterEmission = null;
                failRetirement = false;
                table.Remove(candidate);
                registered = false;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await generation.WaitForQuiescenceAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                failRetirement = false;
                routes?.Dispose();
                beforeEmission?.Dispose();
                afterEmission?.Dispose();
                if (beforeSlot >= 0)
                {
                    beforeFailure?.Exit(beforeSlot);
                }
                if (afterSlot >= 0)
                {
                    afterFailure?.Exit(afterSlot);
                }
                if (registered)
                {
                    table.Remove(candidate);
                }
                await ChangePreparer.DisposeAsync(candidate).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A failed batch retirement preserves every generation and historian registration in the serving view.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedBatchRoutingPublicationPreservesEarlierServingGenerationAsync(
            bool preferredRemovedFirst)
        {
            var namespaces = new NamespaceTable();
            using var servingHistorian = new HistorianProviderRegistry(namespaces);
            IHistorianOwnedProviderRegistry ownedServingHistorian = servingHistorian;
            var historian = new Mock<IHistorianProviderRegistry>();
            Mock<IHistorianOwnedProviderRegistry> ownedHistorian =
                historian.As<IHistorianOwnedProviderRegistry>();
            historian.SetupGet(value => value.Providers).Returns(() => servingHistorian.Providers);
            ownedHistorian.Setup(value => value.OwnsProvider(It.IsAny<IHistorianProvider>()))
                .Returns<IHistorianProvider>(ownedServingHistorian.OwnsProvider);
            ownedHistorian.Setup(value => value.RetainStagedProviderOwnership(
                    It.IsAny<object>(), It.IsAny<IHistorianProvider>()))
                .Callback<object, IHistorianProvider>(ownedServingHistorian.RetainStagedProviderOwnership);
            ownedHistorian.Setup(value => value.ReleaseStagedProviderOwnership(
                    It.IsAny<object>(), It.IsAny<IHistorianProvider>()))
                .Returns<object, IHistorianProvider>(ownedServingHistorian.ReleaseStagedProviderOwnership);
            var servingOwners = new List<object>();
            ownedHistorian.Setup(value => value.ReplaceOwnedRegistrations(
                    It.IsAny<object>(), It.IsAny<HistorianProviderRegistration[]>()))
                .Callback<object, HistorianProviderRegistration[]>((owner, registrations) =>
                {
                    ownedServingHistorian.ReplaceOwnedRegistrations(owner, registrations);
                    servingOwners.RemoveAll(existing => ReferenceEquals(existing, owner));
                    if (registrations.Length != 0)
                    {
                        servingOwners.Add(owner);
                    }
                });
                var failure = new InvalidOperationException("The second historian retirement failed.");
                var probeNodeId = new NodeId("failed-batch-historian", 0);
                bool failRetirement = true;
                int retirementCalls = 0;
            ownedHistorian.Setup(value => value.RemoveOwnedRegistrations(It.IsAny<object>()))
                .Callback<object>(owner =>
                {
                    if (failRetirement && ++retirementCalls == 2)
                    {
                        throw failure;
                    }
                    ownedServingHistorian.RemoveOwnedRegistrations(owner);
                    servingOwners.RemoveAll(existing => ReferenceEquals(existing, owner));
                });

            var server = new Mock<IServerInternal>();
            server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry)
                .Returns(historian.Object);
            var serverUris = new StringTable();
            var telemetry = new Mock<ITelemetryContext>().Object;
            var encodeableFactory = (EncodeableFactory)EncodeableFactory.Create();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(serverUris);
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(encodeableFactory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MessageContext)
                .Returns(new ServiceMessageContext(telemetry, encodeableFactory)
                {
                    NamespaceUris = namespaces,
                    ServerUris = serverUris
                });
            server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(server.Object));

            var table = new NodeManagerRoutingTable();
            var staged = new List<IAsyncNodeManager>();
            var registered = new List<IAsyncNodeManager>();
            var providers = new List<IHistorianProvider>();
            NodeManagerRoutingTable.PreparedRoutes? routes = null;
            try
            {
                for (int index = 0; index < 2; index++)
                {
                    var manager = new Mock<IAsyncNodeManager>();
                    _ = manager.As<INodeManagerReadinessNotification>();
                    manager.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                    var factory = new Mock<IAsyncNodeManagerFactory>();
                    factory.Setup(value => value.CreateAsync(
                            It.IsAny<IServerInternal>(),
                            It.IsAny<ApplicationConfiguration>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(new ValueTask<IAsyncNodeManager>(manager.Object));
                    IAsyncNodeManager candidate = await new ChangePreparer(server.Object).CreateNodeManagerAsync(
                        factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
                    staged.Add(candidate);
                    IHistorianProvider provider = new Mock<IHistorianProvider>().Object;
                    providers.Add(provider);
                    ChangePreparer.StagingServer staging = ChangePreparer.GetServer(candidate)!;
                    ((IHistorianRegistryProvider)staging).HistorianRegistry.RegisterDefault(provider);
                    ChangePreparer.Bind(candidate);
                }

                int firstPublication = preferredRemovedFirst ? 1 : 0;
                int secondPublication = preferredRemovedFirst ? 0 : 1;
                Assert.That(ChangePreparer.PublishHistorianRegistrations(staged[firstPublication]), Is.True);
                Assert.That(ChangePreparer.PublishHistorianRegistrations(staged[secondPublication]), Is.True);
                foreach (IAsyncNodeManager candidate in staged)
                {
                    table.Add(candidate, BatchNamespaceIndexes);
                    registered.Add(candidate);
                }

                Assert.That(servingOwners, Has.Count.EqualTo(2));
                IHistorianProvider expectedProvider = providers[secondPublication];
                Assert.That(servingHistorian.Resolve(probeNodeId), Is.SameAs(expectedProvider));
                object[] initialOwners = [.. servingOwners];
                NodeManagerRoutingTable.RoutingSnapshot serving = table.Revision;
                NodeManagerGeneration[] generations = [.. registered.Select(table.GetGeneration)];
                routes = table.PrepareBatch(
                    [], [.. registered], _ => BatchNamespaceIndexes,
                    new TypeTable(new NamespaceTable()), encodeableFactory);
                routes.Reserve();

                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () => routes.Publish());
                routes.Dispose();
                var admitted = new bool[generations.Length];
                for (int index = 0; index < generations.Length; index++)
                {
                    admitted[index] = generations[index].Gate.TryEnter(out int slot);
                    if (admitted[index])
                    {
                        generations[index].Gate.Exit(slot);
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(failure));
                    Assert.That(table.Revision, Is.SameAs(serving));
                    Assert.That(servingOwners, Has.Count.EqualTo(2));
                    foreach (object owner in initialOwners)
                    {
                        Assert.That(servingOwners.Exists(current => ReferenceEquals(current, owner)), Is.True);
                    }
                    Assert.That(servingHistorian.Resolve(probeNodeId), Is.SameAs(expectedProvider));
                    Assert.That(generations.Select(generation => generation.Gate.State),
                        Is.All.EqualTo(NodeManagerGenerationState.Live));
                    foreach (NodeManagerGeneration generation in generations)
                    {
                        Assert.That(generation.Slot.Current, Is.SameAs(generation));
                    }
                    Assert.That(admitted, Is.All.True);
                });
            }
            finally
            {
                failRetirement = false;
                routes?.Dispose();
                foreach (IAsyncNodeManager manager in registered)
                {
                    table.Remove(manager);
                }
                foreach (IAsyncNodeManager manager in staged)
                {
                    await ChangePreparer.DisposeAsync(manager).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Replacing one manager preserves an equal peer and the original namespace dispatch order.
        /// </summary>
        [Test]
        public void BatchReplacementPreservesEqualPeerAndRouteOrder()
        {
            var table = new NodeManagerRoutingTable();
            var original = new EqualNodeManager();
            var peer = new EqualNodeManager();
            var replacement = new EqualNodeManager();
            table.Add(original, BatchNamespaceIndexes);
            table.Add(peer, BatchNamespaceIndexes);
            NodeManagerGeneration originalGeneration = table.GetGeneration(original);
            NodeManagerGeneration peerGeneration = table.GetGeneration(peer);
            NodeManagerGeneration replacementGeneration =
                NodeManagerGenerationRegistry.CreateReplacement(originalGeneration, replacement);
            var candidate = new PreparedNodeManager(replacement, [], replacementGeneration, original);
            using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [candidate], [], _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());

            routes.Reserve();
            routes.Publish();

            NodeManagerRoutingTable.RoutingSnapshot published = table.Revision;
            Assert.That(published.NodeManagers, Has.Length.EqualTo(2));
            Assert.That(published.NodeManagers[0], Is.SameAs(replacement));
            Assert.That(published.NodeManagers[1], Is.SameAs(peer));
            Assert.That(published.NamespaceManagers[2], Has.Count.EqualTo(2));
            Assert.That(published.NamespaceManagers[2][0], Is.SameAs(replacement));
            Assert.That(published.NamespaceManagers[2][1], Is.SameAs(peer));
            Assert.That(originalGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(peerGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(replacementGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// A manager that only compares equal to a registered instance cannot retire it.
        /// </summary>
        [Test]
        public void BatchRejectsUnregisteredEqualManager()
        {
            var table = new NodeManagerRoutingTable();
            var registered = new EqualNodeManager();
            var unrelated = new EqualNodeManager();
            table.Add(registered, BatchNamespaceIndexes);
            NodeManagerRoutingTable.RoutingSnapshot serving = table.Revision;
            NodeManagerGeneration generation = table.GetGeneration(registered);

            Assert.Throws<InvalidOperationException>(() => table.PrepareBatch(
                [], [unrelated], _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create()));

            Assert.That(table.Revision, Is.SameAs(serving));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// Retiring one manager does not withdraw a distinct equal manager.
        /// </summary>
        [Test]
        public void BatchRetirementPreservesEqualPeer()
        {
            var table = new NodeManagerRoutingTable();
            var retiring = new EqualNodeManager();
            var peer = new EqualNodeManager();
            table.Add(retiring, BatchNamespaceIndexes);
            table.Add(peer, BatchNamespaceIndexes);
            NodeManagerGeneration peerGeneration = table.GetGeneration(peer);
            using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [], [retiring], _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());

            routes.Reserve();
            routes.Publish();

            NodeManagerRoutingTable.RoutingSnapshot published = table.Revision;
            Assert.That(published.NodeManagers, Has.Length.EqualTo(1));
            Assert.That(published.NodeManagers[0], Is.SameAs(peer));
            Assert.That(published.NamespaceManagers[2], Has.Count.EqualTo(1));
            Assert.That(published.NamespaceManagers[2][0], Is.SameAs(peer));
            Assert.That(peerGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
        }

        /// <summary>
        /// Prepared routing preserves captured requests and reveals only the candidates assigned to the unit.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PreparedBatchRevealsOnlyItsCandidatesInThePublishedImage(bool replace)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager original = CreateManager();
            if (replace)
            {
                table.Add(original, BatchNamespaceIndexes);
            }
            IAsyncNodeManager candidate = CreateManager();
            IAsyncNodeManager unrelated = CreateManager();
            table.RegisterNamespace(2, candidate, visible: false);
            table.RegisterNamespace(3, unrelated, visible: false);
            NodeManagerGeneration generation = replace
                ? NodeManagerGenerationRegistry.CreateReplacement(table.GetGeneration(original), candidate)
                : NodeManagerGenerationRegistry.CreateAddition(candidate);
            var prepared = new PreparedNodeManager(candidate, [], generation, replace ? original : null);
            using NodeManagerRoutingTable.PreparedRoutes routes = table.PrepareBatch(
                [prepared], [], _ => BatchNamespaceIndexes,
                new TypeTable(new NamespaceTable()), (EncodeableFactory)EncodeableFactory.Create());
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            using (context.CaptureRouting(table))
            {
                routes.Reserve();
                routes.Publish();
                Assert.That(table.GetDispatchView(context.RoutingSnapshot).NodeManagers, Does.Not.Contain(candidate),
                    "An in-flight request must retain the unpublished routing image.");
                Assert.That(table, Does.Contain(candidate), "Contextless work must use live routes.");
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(table.IsVisible(candidate), Is.True);
                Assert.That(table.IsVisible(unrelated), Is.False);
                Assert.That(table.NamespaceManagers.ContainsKey(3), Is.False);
                Assert.That(table.Revision.HiddenNodeManagers, Is.EquivalentTo(new[] { unrelated }));
                AssertSingleManagerRoute(table.NamespaceManagers, 2, candidate);
                if (replace)
                {
                    Assert.That(table.IsVisible(original), Is.False);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitOperationRoutingSurvivesAsyncPublicationAsync(bool completeAsynchronously)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager lifecycleManager = CreateManager();

            async ValueTask CaptureAndPublishAsync()
            {
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                context.PinRouting(table);
                NodeManagerRoutingTable.RoutingSnapshot captured = context.RoutingSnapshot!;
                table.Add(lifecycleManager, AddNamespaceIndexes);
                if (completeAsynchronously)
                {
                    await Task.Yield();
                }
                Assert.That(context.RoutingSnapshot, Is.SameAs(captured),
                    "The operation must retain its explicit routes.");
                Assert.That(table.Any(manager => ReferenceEquals(manager, lifecycleManager)), Is.True,
                    "A metadata image must never select dispatch routes.");
            }

            await CaptureAndPublishAsync().ConfigureAwait(false);

            Assert.That(table.Any(manager => ReferenceEquals(manager, lifecycleManager)), Is.True);
        }

        /// <summary>
        /// Independent work never inherits a request's explicitly selected dispatch view.
        /// </summary>
        [Test]
        public async Task IndependentBackgroundWorkUsesLiveRoutesDespiteInheritedRequestMetadataAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager added = CreateManager();
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            using OperationContext.RoutingScope requestRouting = context.CaptureRouting(table);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<(IAsyncNodeManager[] Routes, NodeManagerRoutingTable.RoutingSnapshot Metadata)> background =
                Task.Run(async () =>
                {
                    started.SetResult(true);
                    await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    using var independent = new OperationContext(
                        new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                    return (table.GetDispatchView(independent.RoutingSnapshot).NodeManagers, table.Revision);
                }, timeout.Token);
            try
            {
                await started.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                table.Add(added, AddNamespaceIndexes);
            }
            finally
            {
                published.TrySetResult(true);
            }
            var result = await background.ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Metadata, Is.SameAs(table.Revision));
                Assert.That(result.Routes, Does.Contain(added),
                    "The inherited bridge must not select background dispatch routes.");
                Assert.That(table.GetDispatchView(context.RoutingSnapshot).NodeManagers, Does.Not.Contain(added),
                    "Only the original request explicitly holds its earlier routing view.");
            }
        }

        /// <summary>
        /// Nested service scopes retain a request's original view, which disposal releases.
        /// </summary>
        [Test]
        public void RequestPinSurvivesNestedScopesAndIsReleasedWithTheContext()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            NodeManagerRoutingTable.RoutingSnapshot original = context.RoutingSnapshot!;
            table.Add(CreateManager(), AddNamespaceIndexes);
            context.PinRouting(table);
            Assert.That(context.RoutingSnapshot, Is.SameAs(original), "A later dispatch must not repin the request.");
            using (context.CaptureRouting(table))
            {
                Assert.That(context.RoutingSnapshot, Is.SameAs(original));
                using (context.CaptureRouting(table))
                {
                    Assert.That(context.RoutingSnapshot, Is.SameAs(original));
                }
                Assert.That(context.RoutingSnapshot, Is.SameAs(original));
            }
            Assert.That(context.RoutingSnapshot, Is.SameAs(original));
            context.Dispose();
            Assert.That(context.RoutingSnapshot, Is.Null);
        }

        /// <summary>
        /// Concurrent first captures join one cohort and release its single epoch pin exactly once.
        /// </summary>
        [Test]
        public async Task ConcurrentBorrowedScopesShareOnePinAndPermitFreshReuseAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int count = 0;
            var snapshots = new ConcurrentBag<NodeManagerRoutingTable.RoutingSnapshot>();
            Task[] calls = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            {
                await start.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                using OperationContext.RoutingScope scope = context.CaptureRouting(table);
                NodeManagerRoutingTable.RoutingSnapshot captured = context.RoutingSnapshot!;
                snapshots.Add(captured);
                if (Interlocked.Increment(ref count) == 16)
                {
                    entered.SetResult(true);
                }
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(context.RoutingSnapshot, Is.SameAs(captured));
                scope.Dispose();
                scope.Dispose();
            }, timeout.Token)).ToArray();
            NodeManagerRoutingTable.RoutingSnapshot original = table.Revision;
            try
            {
                start.SetResult(true);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(snapshots, Has.Count.EqualTo(16));
                Assert.That(snapshots.All(snapshot => ReferenceEquals(snapshot, original)), Is.True);
                Assert.That(original.Epoch.ActiveCount, Is.EqualTo(1));
                table.Add(CreateManager(), AddNamespaceIndexes);
                Assert.That(original.Epoch.IsDrained, Is.False);
            }
            finally
            {
                start.TrySetResult(true);
                release.TrySetResult(true);
                await Task.WhenAll(calls).ConfigureAwait(false);
            }
            Assert.That(original.Epoch.ActiveCount, Is.Zero);
            Assert.That(original.Epoch.IsDrained, Is.True);
            Assert.That(context.RoutingSnapshot, Is.Null);
            using (context.CaptureRouting(table))
            {
                Assert.That(context.RoutingSnapshot, Is.SameAs(table.Revision));
                Assert.That(table.Revision.Epoch.ActiveCount, Is.EqualTo(1));
            }
            Assert.That(context.RoutingSnapshot, Is.Null);
            Assert.That(table.Revision.Epoch.ActiveCount, Is.Zero);
            context.PinRouting(table);
            Assert.That(context.RoutingSnapshot, Is.SameAs(table.Revision));
            Assert.That(table.Revision.Epoch.ActiveCount, Is.EqualTo(1));
            context.Dispose();
            Assert.That(table.Revision.Epoch.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Exception and cancellation unwinding release temporary ownership without disposing the context.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BorrowedRoutingScopesReleaseTheirPinOnFailureAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task work = FailAsync();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(table.Revision.Epoch.ActiveCount, Is.EqualTo(1));
                if (cancel)
                {
                    cancellation.Cancel();
                    await Assert.ThatAsync(async () => await work.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else
                {
                    finish.SetResult(true);
                    await Assert.ThatAsync(async () => await work.ConfigureAwait(false),
                        Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                }
            }
            finally
            {
                cancellation.Cancel();
                finish.TrySetResult(true);
            }
            Assert.That(context.RoutingSnapshot, Is.Null);
            Assert.That(context.HasRoutingPin, Is.False);
            Assert.That(table.Revision.Epoch.ActiveCount, Is.Zero);
            using (context.CaptureRouting(table))
            {
                Assert.That(context.RoutingSnapshot, Is.SameAs(table.Revision));
            }
            Assert.That(table.Revision.Epoch.ActiveCount, Is.Zero);

            async Task FailAsync()
            {
                using OperationContext.RoutingScope scope = context.CaptureRouting(table);
                using OperationContext.RoutingScope nested = context.CaptureRouting(table);
                entered.SetResult(true);
                await finish.Task.WaitAsync(cancellation.Token).ConfigureAwait(false);
                throw new InvalidOperationException("Borrowed service failed.");
            }
        }

        /// <summary>
        /// Ordinary request-owned dispatch allocates no cohort or per-service ownership token.
        /// </summary>
        /// <remarks>
        /// The runtime can allocate a few bytes on this thread once, for example when on-stack
        /// replacement moves a Tier0 loop to optimized code, which made a single exact-zero round
        /// flaky (64 bytes locally, 3264 bytes on macOS CI). The measured loop therefore runs in a
        /// separate method that is warmed up first, and the test measures several rounds: every
        /// round must stay below one byte per call, which any per-call allocation exceeds, and at
        /// least one round must allocate nothing at all, so an allocation on a rare path still fails.
        /// </remarks>
        [Test]
#if NET8_0_OR_GREATER
        // Exclude tiered loop instrumentation from the allocation measurement.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        public void RequestOwnedRoutingCaptureAddsNoManagedAllocations()
        {
            const int iterations = 10000;
            const int rounds = 5;
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            for (int ii = 0; ii < 3; ii++)
            {
                MeasureRequestOwnedCaptures(context, table, iterations, out _);
            }
            long[] allocated = new long[rounds];
            long elapsed = 0;
            for (int ii = 0; ii < rounds; ii++)
            {
                allocated[ii] = MeasureRequestOwnedCaptures(context, table, iterations, out long roundElapsed);
                elapsed += roundElapsed;
            }
            string perRound = string.Join(", ", allocated);
            Assert.That(allocated, Has.All.LessThan(iterations), $"Bytes per round: {perRound}");
            Assert.That(allocated, Has.Some.Zero, $"Bytes per round: {perRound}");
            Assert.That(table.Revision.Epoch.ActiveCount, Is.EqualTo(1));
            TestContext.Out.WriteLine(
                $"Request-owned capture microcheck: {rounds} x {iterations} scopes, bytes per round [{perRound}], " +
                $"{elapsed} stopwatch ticks ({Stopwatch.Frequency} ticks/s); not a load benchmark.");
        }

        /// <summary>
        /// Captures and disposes request-owned routing scopes and returns the bytes this thread allocated.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasureRequestOwnedCaptures(
            OperationContext context,
            NodeManagerRoutingTable table,
            int iterations,
            out long elapsed)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int ii = 0; ii < iterations; ii++)
            {
                context.CaptureRouting(table).Dispose();
            }
            elapsed = Stopwatch.GetTimestamp() - started;
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>
        /// First borrowed acquisition racing an explicit pin or disposal cannot leak or repin a terminal context.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BorrowedRoutingAcquisitionRacesExplicitOwnershipSafelyAsync(bool dispose)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            for (int ii = 0; ii < 32; ii++)
            {
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task capture = Task.Run(async () =>
                {
                    await start.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    try
                    {
                        using OperationContext.RoutingScope scope = context.CaptureRouting(table);
                    }
                    catch (ObjectDisposedException) when (dispose)
                    {
                    }
                }, timeout.Token);
                Task owner = Task.Run(async () =>
                {
                    await start.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    if (dispose)
                    {
                        context.Dispose();
                    }
                    else
                    {
                        context.PinRouting(table);
                    }
                }, timeout.Token);
                start.SetResult(true);
                await Task.WhenAll(capture, owner).WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(table.Revision.Epoch.ActiveCount, Is.EqualTo(dispose ? 0 : 1));
                context.Dispose();
                Assert.That(context.RoutingSnapshot, Is.Null);
                Assert.That(table.Revision.Epoch.ActiveCount, Is.Zero);
                Assert.That(() => context.CaptureRouting(table), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(() => context.PinRouting(table), Throws.TypeOf<ObjectDisposedException>());
            }
        }

        /// <summary>
        /// Unwinding a nested request scope after disposal cannot restore the disposed context's old snapshot.
        /// </summary>
        [Test]
        public void NestedRequestRoutingDoesNotRestoreDisposedContext()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            using var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            context.PinRouting(table);
            NodeManagerRoutingTable.RoutingSnapshot original = context.RoutingSnapshot!;
            table.Add(CreateManager(), AddNamespaceIndexes);
            using (context.CaptureRouting(table))
            {
                context.Dispose();
                Assert.That(context.RoutingSnapshot, Is.Null);
            }
            Assert.That(context.RoutingSnapshot, Is.Null);
            Assert.That(original.Epoch.ActiveCount, Is.Zero);
            Assert.That(() => context.CaptureRouting(table), Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// A permanently installed cohort never reuses a state accepted by stale null-cohort bootstrap paths.
        /// A construction-bound checkpoint forces stale null-cohort decisions across drain and upgrade.
        /// </summary>
        [Test]
        public void BorrowedRoutingCohortNeverReentersOrdinaryBootstrapStates()
        {
            AssertStaleBootstrapCannotBypassBorrowedOwnership(upgradeBeforeDisposal: false);
            AssertStaleBootstrapCannotBypassBorrowedOwnership(upgradeBeforeDisposal: true);
        }

        /// <summary>
        /// Forces an ordinary path to observe null before a cohort installs and drains or upgrades.
        /// </summary>
        private static void AssertStaleBootstrapCannotBypassBorrowedOwnership(bool upgradeBeforeDisposal)
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            NodeManagerRoutingTable.RoutingSnapshot snapshot = table.Revision;
            bool installCohort = true;
            int checkpoints = 0;
            OperationContext? context = null;
            context = new OperationContext(new RequestHeader(), () =>
            {
                checkpoints++;
                if (!installCohort)
                {
                    return;
                }
                installCohort = false;
                using OperationContext.RoutingScope scope = context!.CaptureRouting(table);
                if (upgradeBeforeDisposal)
                {
                    context.PinRouting(table);
                }
            }, RequestLifetime.None);
            using (context)
            {
                if (upgradeBeforeDisposal)
                {
                    context.Dispose();
                    Assert.That(snapshot.Epoch.ActiveCount, Is.Zero);
                    Assert.That(() => context.CaptureRouting(table), Throws.TypeOf<ObjectDisposedException>(),
                        "A stale ordinary release must revoke the installed cohort, not bypass it.");
                }
                else
                {
                    context.PinRouting(table);
                    using (context.CaptureRouting(table))
                    {
                        Assert.That(context.RoutingSnapshot, Is.SameAs(snapshot));
                        Assert.That(snapshot.Epoch.ActiveCount, Is.EqualTo(1),
                            "A stale ordinary pin must not acquire outside the installed cohort.");
                    }
                    Assert.That(snapshot.Epoch.ActiveCount, Is.EqualTo(1));
                }
                Assert.That(checkpoints, Is.EqualTo(2),
                    "Only the stale outer selection and the nested first borrow may select bootstrap.");
            }
            Assert.That(snapshot.Epoch.ActiveCount, Is.Zero);
        }

        [Test]
        public void AddPublishesManagerAndNamespaceRoutesWithoutMutatingCapturedSnapshots()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager lifecycleManager = CreateManager();
            using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                namespaceRouteSnapshot = table.NamespaceManagers;

            table.Add(lifecycleManager, AddNamespaceIndexes);

            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> currentRoutes =
                table.NamespaceManagers;
            Assert.That(table, Has.Count.EqualTo(3));
            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
            Assert.That(table[2], Is.SameAs(lifecycleManager));
            Assert.That(currentRoutes, Is.Not.SameAs(namespaceRouteSnapshot));
            Assert.That(currentRoutes, Has.Count.EqualTo(2));
            Assert.That(currentRoutes.Keys, Is.EquivalentTo(InitialNamespaceIndexes));
            AssertSingleManagerRoute(currentRoutes, 2, lifecycleManager);
            AssertSingleManagerRoute(currentRoutes, 3, lifecycleManager);

            AssertManagerSnapshot(
                managerSnapshot,
                firstPermanent,
                secondPermanent);
            Assert.That(namespaceRouteSnapshot, Is.Empty);
        }

        [Test]
        public void ReplacePublishesReplacementRoutesWithoutMutatingCapturedSnapshots()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager original = CreateManager();
            IAsyncNodeManager replacement = CreateManager();
            table.Add(original, InitialNamespaceIndexes);
            using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                namespaceRouteSnapshot = table.NamespaceManagers;
            IReadOnlyList<IAsyncNodeManager> namespaceTwoSnapshot =
                namespaceRouteSnapshot[2];
            IReadOnlyList<IAsyncNodeManager> namespaceThreeSnapshot =
                namespaceRouteSnapshot[3];

            table.Replace(original, replacement, ReplaceNamespaceIndexes);

            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> currentRoutes =
                table.NamespaceManagers;
            Assert.That(table, Has.Count.EqualTo(3));
            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
            Assert.That(table[2], Is.SameAs(replacement));
            Assert.That(table[2], Is.Not.SameAs(original));
            Assert.That(currentRoutes, Is.Not.SameAs(namespaceRouteSnapshot));
            Assert.That(currentRoutes, Has.Count.EqualTo(2));
            Assert.That(currentRoutes.Keys, Is.EquivalentTo(ReplaceNamespaceIndexes));
            Assert.That(currentRoutes.ContainsKey(2), Is.False);
            AssertSingleManagerRoute(currentRoutes, 3, replacement);
            AssertSingleManagerRoute(currentRoutes, 4, replacement);
            Assert.That(currentRoutes[3][0], Is.Not.SameAs(original));
            Assert.That(currentRoutes[4][0], Is.Not.SameAs(original));

            AssertManagerSnapshot(
                managerSnapshot,
                firstPermanent,
                secondPermanent,
                original);
            Assert.That(namespaceRouteSnapshot, Has.Count.EqualTo(2));
            Assert.That(namespaceRouteSnapshot.Keys, Is.EquivalentTo(InitialNamespaceIndexes));
            Assert.That(namespaceRouteSnapshot[2], Is.SameAs(namespaceTwoSnapshot));
            Assert.That(namespaceRouteSnapshot[3], Is.SameAs(namespaceThreeSnapshot));
            AssertSingleManagerRoute(namespaceRouteSnapshot, 2, original);
            AssertSingleManagerRoute(namespaceRouteSnapshot, 3, original);
        }

        [Test]
        public void RestoreReplacementRecoversDroppedRoutePositions()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager original = CreateManager();
            IAsyncNodeManager peer = CreateManager();
            IAsyncNodeManager replacement = CreateManager();
            table.Add(original, InitialNamespaceIndexes);
            table.Add(peer, InitialNamespaceIndexes);

            NodeManagerRoutingTable.NodeManagerRoutingPosition position =
                table.Replace(
                    original,
                    replacement,
                    ReplaceNamespaceIndexes);
            table.RestoreReplacement(
                replacement,
                original,
                position,
                visible: true);

            Assert.That(table[2], Is.SameAs(original));
            Assert.That(table[3], Is.SameAs(peer));
            Assert.That(table.NamespaceManagers.Keys, Is.EquivalentTo(InitialNamespaceIndexes));
            Assert.That(table.NamespaceManagers[2][0], Is.SameAs(original));
            Assert.That(table.NamespaceManagers[2][1], Is.SameAs(peer));
            Assert.That(table.NamespaceManagers[3][0], Is.SameAs(original));
            Assert.That(table.NamespaceManagers[3][1], Is.SameAs(peer));
        }

        [Test]
        public void RemoveUnpublishesManagerWithoutMutatingCapturedSnapshots()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager lifecycleManager = CreateManager();
            table.Add(lifecycleManager, InitialNamespaceIndexes);
            using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>>
                namespaceRouteSnapshot = table.NamespaceManagers;
            IReadOnlyList<IAsyncNodeManager> namespaceTwoSnapshot =
                namespaceRouteSnapshot[2];
            IReadOnlyList<IAsyncNodeManager> namespaceThreeSnapshot =
                namespaceRouteSnapshot[3];

            table.Remove(lifecycleManager);

            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> currentRoutes =
                table.NamespaceManagers;
            Assert.That(table, Has.Count.EqualTo(2));
            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
            Assert.That(currentRoutes, Is.Not.SameAs(namespaceRouteSnapshot));
            Assert.That(currentRoutes, Is.Empty);

            AssertManagerSnapshot(
                managerSnapshot,
                firstPermanent,
                secondPermanent,
                lifecycleManager);
            Assert.That(namespaceRouteSnapshot, Has.Count.EqualTo(2));
            Assert.That(namespaceRouteSnapshot.Keys, Is.EquivalentTo(InitialNamespaceIndexes));
            Assert.That(namespaceRouteSnapshot[2], Is.SameAs(namespaceTwoSnapshot));
            Assert.That(namespaceRouteSnapshot[3], Is.SameAs(namespaceThreeSnapshot));
            AssertSingleManagerRoute(namespaceRouteSnapshot, 2, lifecycleManager);
            AssertSingleManagerRoute(namespaceRouteSnapshot, 3, lifecycleManager);
        }

        [Test]
        public void RestoreReturnsManagerToCapturedGlobalAndNamespacePositions()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager earlier = CreateManager();
            IAsyncNodeManager first = CreateManager();
            IAsyncNodeManager second = CreateManager();
            table.Add(earlier, InitialNamespaceIndexes);
            table.Add(first, InitialNamespaceIndexes);
            table.Add(second, InitialNamespaceIndexes);
            NodeManagerGeneration generation = table.GetGeneration(first);

            NodeManagerRoutingTable.NodeManagerRoutingPosition position =
                table.RemoveAndCapturePosition(first);
            table.Remove(earlier);
            table.Restore(first, position);

            Assert.That(table[2], Is.SameAs(first));
            Assert.That(table[3], Is.SameAs(second));
            Assert.That(table.NamespaceManagers[2][0], Is.SameAs(first));
            Assert.That(table.NamespaceManagers[2][1], Is.SameAs(second));
            Assert.That(table.NamespaceManagers[3][0], Is.SameAs(first));
            Assert.That(table.NamespaceManagers[3][1], Is.SameAs(second));
            Assert.That(table.GetGeneration(first), Is.SameAs(generation));
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(generation.Slot.Current, Is.SameAs(generation));
        }

        [Test]
        public void RoutingMutationsMatchAdaptersBySynchronousManagerIdentity()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            INodeManager syncManager = new Mock<INodeManager>().Object;
            IAsyncNodeManager registeredAdapter =
                new AsyncNodeManagerAdapter(syncManager);
            IAsyncNodeManager lookupAdapter =
                new AsyncNodeManagerAdapter(syncManager);
            table.Add(registeredAdapter, [2]);

            table.RegisterNamespace(3, lookupAdapter);

            Assert.That(table.NamespaceManagers[3], Has.Count.EqualTo(1));
            Assert.That(
                table.NamespaceManagers[3][0],
                Is.SameAs(registeredAdapter));

            table.Remove(lookupAdapter);

            Assert.That(table, Has.Count.EqualTo(2));
            Assert.That(table.NamespaceManagers, Is.Empty);
        }

        /// <summary>
        /// Opaque facades over one synchronous resource keep independent namespace and registration owners.
        /// </summary>
        [Test]
        public void OpaqueSynchronousFacadesKeepIndependentNamespaceAndRegistrationOwners()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            INodeManager resource = Mock.Of<INodeManager>();
            IAsyncNodeManager registered = CreateManagerWithSync(resource);
            IAsyncNodeManager other = CreateManagerWithSync(resource);
            table.Add(registered, BatchNamespaceIndexes);
            table.Add(other, BatchNamespaceIndexes);
            NodeManagerGeneration registeredGeneration = table.GetGeneration(registered);
            NodeManagerGeneration otherGeneration = table.GetGeneration(other);
            table.RegisterNamespace(3, other);
            AssertSingleManagerRoute(table.NamespaceManagers, 3, other);

            table.RemoveNamespaceManager(other);
            AssertSingleManagerRoute(table.NamespaceManagers, 2, registered);
            Assert.That(table.NamespaceManagers.ContainsKey(3), Is.False);
            Assert.That(table.Contains(other), Is.True);
            Assert.That(otherGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));

            table.Remove(other);
            Assert.That(table.Contains(other), Is.False);
            Assert.That(table.Contains(registered), Is.True);
            AssertSingleManagerRoute(table.NamespaceManagers, 2, registered);
            Assert.That(registeredGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(otherGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
        }

        /// <summary>
        /// While one writer replaces a single lifecycle-managed slot with fresh exact generations
        /// (~200 times) and several readers loop concurrently, every
        /// individually captured manager-list snapshot (via <see cref="NodeManagerRoutingTable.GetEnumerator"/>)
        /// must remain structurally complete - three managers, the first two always the
        /// same permanent instances, and the third always exactly one of the known lifecycle
        /// generations, never anything else - and every individually captured
        /// <see cref="NodeManagerRoutingTable.NamespaceManagers"/> snapshot must show exactly
        /// one non-null route to the same namespace index, pointing at exactly one valid
        /// lifecycle generation. The manager list and the namespace-route dictionary are
        /// each read via a single, separate, atomic snapshot access; a legitimate concurrent
        /// write can therefore be interleaved between the two reads, so the two are
        /// deliberately never cross-correlated against each other.
        /// </summary>
        [Test]
        public async Task ConcurrentReadersObserveOnlyCompleteRoutingSnapshotsAsync()
        {
            await RunConcurrentSnapshotReadersAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// A writer failure before either startup notification cancels every waiting reader and preserves the cause.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public async Task ConcurrentSnapshotWriterFailureReleasesEveryParticipantAsync(int failedWrite)
        {
            var failure = new InvalidOperationException("The coordinated writer failed.");
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunConcurrentSnapshotReadersAsync(beforeWrite: write =>
                {
                    if (write == failedWrite)
                    {
                        throw failure;
                    }
                })).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(failure));
        }

        /// <summary>
        /// A reader failure before readiness or during a snapshot round releases the writer and its peer readers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentSnapshotReaderFailureReleasesEveryParticipantAsync(bool duringRound)
        {
            var failure = new InvalidOperationException("The coordinated reader failed.");
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunConcurrentSnapshotReadersAsync(beforeRead: (reader, iteration) =>
                {
                    if (reader == 0 && iteration == (duringRound ? 0 : -1))
                    {
                        throw failure;
                    }
                })).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(failure));
        }

        [Test]
        public void AddInitialThrowsOnNullNodeManager()
        {
            var table = new NodeManagerRoutingTable();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.AddInitial(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void InitializePublishesNamespaceRoutesFromDictionary()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();

            ArgumentNullException nullEx = Assert.Throws<ArgumentNullException>(
                () => table.Initialize(null!))!;
            Assert.That(nullEx.ParamName, Is.EqualTo("namespaceManagers"));

            table.Initialize(
                new Dictionary<int, List<IAsyncNodeManager>> { [5] = [manager] });

            AssertSingleManagerRoute(table.NamespaceManagers, 5, manager);
        }

        [Test]
        public void AddThrowsOnNullNodeManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.Add(null!, AddNamespaceIndexes))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void AddThrowsOnNullNamespaceIndexes()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.Add(CreateManager(), null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("namespaceIndexes"));
        }

        [Test]
        public void AddThrowsWhenNodeManagerAlreadyRegistered()
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager firstPermanent, out _);

            Assert.Throws<InvalidOperationException>(
                () => table.Add(firstPermanent, AddNamespaceIndexes));
        }

        [Test]
        public void AddWithVisibleFalseHidesManagerFromReadOnlyList()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager hiddenManager = CreateManager();

            table.Add(hiddenManager, AddNamespaceIndexes, visible: false);

            Assert.That(table, Has.Count.EqualTo(2));
            Assert.That((IReadOnlyCollection<IAsyncNodeManager>)table, Has.Count.EqualTo(2));
            Assert.That(table, Is.Not.Contains(hiddenManager));
            Assert.That(table.IsVisible(hiddenManager), Is.False);
            using IEnumerator<IAsyncNodeManager> visibleSnapshot = table.GetEnumerator();
            AssertManagerSnapshot(visibleSnapshot, firstPermanent, secondPermanent);
        }

        [Test]
        public void ReplaceThrowsOnNullArguments()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager original = CreateManager();
            table.Add(original, InitialNamespaceIndexes);

            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => table.Replace(null!, CreateManager(), ReplaceNamespaceIndexes))!.ParamName,
                Is.EqualTo("current"));
            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => table.Replace(original, null!, ReplaceNamespaceIndexes))!.ParamName,
                Is.EqualTo("replacement"));
            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => table.Replace(original, CreateManager(), null!))!.ParamName,
                Is.EqualTo("replacementNamespaceIndexes"));
        }

        [Test]
        public void ReplaceInitialManagerPreservesItsRoutingPosition()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstInitial,
                out IAsyncNodeManager secondInitial);
            table.RegisterNamespace(2, firstInitial);
            IAsyncNodeManager replacement = CreateManager();

            var position = table.Replace(firstInitial, replacement, ReplaceNamespaceIndexes);

            Assert.That(position.ManagerPosition.Index, Is.Zero);
            Assert.That(table.Revision.NodeManagers, Is.EqualTo([replacement, secondInitial]));
            Assert.That(table, Does.Not.Contain(firstInitial));
            foreach (int namespaceIndex in ReplaceNamespaceIndexes)
            {
                AssertSingleManagerRoute(table.NamespaceManagers, namespaceIndex, replacement);
            }
        }

        [Test]
        public void ReplaceThrowsWhenReplacementAlreadyRegistered()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            IAsyncNodeManager original = CreateManager();
            IAsyncNodeManager other = CreateManager();
            table.Add(original, InitialNamespaceIndexes);
            table.Add(other, ReplaceNamespaceIndexes);

            Assert.Throws<InvalidOperationException>(
                () => table.Replace(original, other, ReplaceNamespaceIndexes));
        }

        [Test]
        public void RemoveThrowsOnNullNodeManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.Remove(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void RemoveInitialManagerRemovesItsRoutes()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstInitial,
                out IAsyncNodeManager secondInitial);
            table.RegisterNamespace(2, firstInitial);

            table.Remove(firstInitial);

            Assert.That(table.Revision.NodeManagers, Is.EqualTo([secondInitial]));
            Assert.That(table.NamespaceManagers.ContainsKey(2), Is.False);
        }

        [Test]
        public void RegisterNamespaceIsIdempotentForTheSameManager()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();

            table.RegisterNamespace(7, manager);
            table.RegisterNamespace(7, manager);

            AssertSingleManagerRoute(table.NamespaceManagers, 7, manager);
        }

        [Test]
        public void RegisterNamespaceAppendsToExistingRouteForDifferentManager()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager first = CreateManager();
            IAsyncNodeManager second = CreateManager();

            table.RegisterNamespace(7, first);
            table.RegisterNamespace(7, second);

            Assert.That(table.NamespaceManagers[7], Has.Count.EqualTo(2));
            Assert.That(table.NamespaceManagers[7], Does.Contain(first));
            Assert.That(table.NamespaceManagers[7], Does.Contain(second));
        }

        [Test]
        public void RegisterNamespaceWithVisibleFalseHidesManager()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();
            table.AddInitial(manager);

            table.RegisterNamespace(7, manager, visible: false);

            Assert.That(table.IsVisible(manager), Is.False);
        }

        [Test]
        public void UnregisterNamespaceReturnsFalseWhenNamespaceNotFound()
        {
            var table = new NodeManagerRoutingTable();

            Assert.That(table.UnregisterNamespace(1, CreateManager(), null), Is.False);
        }

        [Test]
        public void UnregisterNamespaceReturnsFalseWhenManagerNotInRoute()
        {
            var table = new NodeManagerRoutingTable();
            table.RegisterNamespace(7, CreateManager());

            Assert.That(table.UnregisterNamespace(7, CreateManager(), null), Is.False);
        }

        [Test]
        public void UnregisterNamespaceRemovesRouteEntryWhenLastManagerRemoved()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager manager = CreateManager();
            table.RegisterNamespace(7, manager);

            bool removed = table.UnregisterNamespace(7, manager, null);

            Assert.That(removed, Is.True);
            Assert.That(table.NamespaceManagers.ContainsKey(7), Is.False);
        }

        [Test]
        public void UnregisterNamespaceKeepsRouteWhenOtherManagersRemain()
        {
            var table = new NodeManagerRoutingTable();
            IAsyncNodeManager first = CreateManager();
            IAsyncNodeManager second = CreateManager();
            table.RegisterNamespace(7, first);
            table.RegisterNamespace(7, second);

            bool removed = table.UnregisterNamespace(7, first, null);

            Assert.That(removed, Is.True);
            AssertSingleManagerRoute(table.NamespaceManagers, 7, second);
        }

        [Test]
        public void UnregisterNamespaceMatchesBySyncNodeManagerWhenAsyncNodeManagerIsNull()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager syncManager = new Mock<INodeManager>().Object;
            IAsyncNodeManager manager = CreateManagerWithSync(syncManager);
            table.RegisterNamespace(7, manager);

            bool removed = table.UnregisterNamespace(7, null, syncManager);

            Assert.That(removed, Is.True);
            Assert.That(table.NamespaceManagers.ContainsKey(7), Is.False);
        }

        [Test]
        public void RemoveNamespaceManagerThrowsOnNullNodeManager()
        {
            var table = new NodeManagerRoutingTable();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.RemoveNamespaceManager(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void RemoveNamespaceManagerRemovesAcrossAllNamespacesAndMatchesBySyncNodeManager()
        {
            var table = new NodeManagerRoutingTable();
            INodeManager syncManager = new Mock<INodeManager>().Object;
            IAsyncNodeManager registeredManager = new AsyncNodeManagerAdapter(syncManager);
            IAsyncNodeManager lookupManager = new AsyncNodeManagerAdapter(syncManager);
            IAsyncNodeManager unrelated = CreateManager();
            table.RegisterNamespace(2, registeredManager);
            table.RegisterNamespace(3, registeredManager);
            table.RegisterNamespace(3, unrelated);

            table.RemoveNamespaceManager(lookupManager);

            Assert.That(table.NamespaceManagers.ContainsKey(2), Is.False);
            AssertSingleManagerRoute(table.NamespaceManagers, 3, unrelated);
        }

        [Test]
        public void IsVisibleReturnsFalseForUnregisteredManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            Assert.That(table.IsVisible(CreateManager()), Is.False);
        }

        [Test]
        public void IsVisibleReturnsTrueForRegisteredVisibleManager()
        {
            NodeManagerRoutingTable table = CreateTable(out IAsyncNodeManager firstPermanent, out _);

            Assert.That(table.IsVisible(firstPermanent), Is.True);
        }

        [Test]
        public void SetVisibleThrowsOnNullNodeManager()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => table.SetVisible(null!, false))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void SetVisibleThrowsWhenManagerNotRegistered()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);

            Assert.Throws<InvalidOperationException>(
                () => table.SetVisible(CreateManager(), false));
        }

        [Test]
        public void SetVisibleTogglesVisibilityAffectingEnumerationAndIsVisible()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);

            table.SetVisible(firstPermanent, false);

            Assert.That(table.IsVisible(firstPermanent), Is.False);
            using (IEnumerator<IAsyncNodeManager> hiddenSnapshot = table.GetEnumerator())
            {
                AssertManagerSnapshot(hiddenSnapshot, secondPermanent);
            }

            table.SetVisible(firstPermanent, true);

            Assert.That(table.IsVisible(firstPermanent), Is.True);
            using IEnumerator<IAsyncNodeManager> restoredSnapshot = table.GetEnumerator();
            AssertManagerSnapshot(restoredSnapshot, firstPermanent, secondPermanent);
        }

        [Test]
        public void ClearResetsAllState()
        {
            NodeManagerRoutingTable table = CreateTable(out _, out _);
            table.Add(CreateManager(), InitialNamespaceIndexes);

            table.Clear();

            Assert.That(table, Has.Count.EqualTo(0));
            Assert.That(table.NamespaceManagers, Is.Empty);
        }

        [Test]
        public void IndexerReturnsManagerAtPosition()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);

            Assert.That(table[0], Is.SameAs(firstPermanent));
            Assert.That(table[1], Is.SameAs(secondPermanent));
        }

        [Test]
        public void ExplicitEnumerableGetEnumeratorReturnsSameManagersAsGenericEnumerator()
        {
            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);

            var managers = new List<IAsyncNodeManager>();
            System.Collections.IEnumerable untyped = table;
            System.Collections.IEnumerator enumerator = untyped.GetEnumerator();
            while (enumerator.MoveNext())
            {
                managers.Add((IAsyncNodeManager)enumerator.Current!);
            }

            Assert.That(managers, Is.EqualTo([firstPermanent, secondPermanent]));
        }

        /// <summary>
        /// Joins one bounded writer and every reader, canceling all synchronization waits on the first actor failure.
        /// </summary>
        private static async Task RunConcurrentSnapshotReadersAsync(
            Action<int>? beforeWrite = null,
            Action<int, int>? beforeRead = null)
        {
            const int ReaderIterations = 100;
            const int ReaderCount = 4;
            const int ReplacementCount = (ReaderIterations * 2) + 2;

            NodeManagerRoutingTable table = CreateTable(
                out IAsyncNodeManager firstPermanent,
                out IAsyncNodeManager secondPermanent);
            IAsyncNodeManager[] generations =
                [.. Enumerable.Range(0, ReplacementCount + 1).Select(_ => CreateManager())];
            var expectedManagers = new HashSet<IAsyncNodeManager>(generations, RefEqualityComparer.Default);
            table.Add(generations[0], ConcurrentNamespaceIndexes);

            var managerSnapshotFailures = new ConcurrentBag<string>();
            var namespaceSnapshotFailures = new ConcurrentBag<string>();
            using var startBarrier = new Barrier(ReaderCount + 1);
            using var roundBarrier = new Barrier(ReaderCount + 1);
            using var writerStarted = new ManualResetEventSlim();
            using var readersReadyForConcurrentWrite = new CountdownEvent(ReaderCount);
            using var concurrentWriteCompleted = new ManualResetEventSlim();
            using var stopWorkers = new CancellationTokenSource();
            CancellationToken stop = stopWorkers.Token;
            int writerIterations = 0;
            ExceptionDispatchInfo? firstFailure = null;

            Task writerTask = Task.Factory.StartNew(() =>
            {
                int currentIndex = 0;
                try
                {
                    startBarrier.SignalAndWait(stop);
                    ReplaceCurrent();
                    writerStarted.Set();
                    readersReadyForConcurrentWrite.Wait(stop);
                    ReplaceCurrent();
                    concurrentWriteCompleted.Set();

                    for (int iteration = 0; iteration < ReaderIterations; iteration++)
                    {
                        roundBarrier.SignalAndWait(stop);
                        ReplaceCurrent();
                        Thread.Yield();
                        ReplaceCurrent();
                        roundBarrier.SignalAndWait(stop);
                    }
                }
                catch (Exception failure)
                {
                    Interlocked.CompareExchange(ref firstFailure, ExceptionDispatchInfo.Capture(failure), null);
                    stopWorkers.Cancel();
                    throw;
                }
                finally
                {
                    writerStarted.Set();
                    concurrentWriteCompleted.Set();
                }

                void ReplaceCurrent()
                {
                    int nextIndex = currentIndex + 1;
                    beforeWrite?.Invoke(nextIndex);
                    table.Replace(generations[currentIndex], generations[nextIndex], ConcurrentNamespaceIndexes);
                    currentIndex = nextIndex;
                    Interlocked.Increment(ref writerIterations);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Task[] readerTasks = [.. Enumerable.Range(0, ReaderCount).Select(reader => Task.Factory.StartNew(() =>
            {
                try
                {
                    startBarrier.SignalAndWait(stop);
                    writerStarted.Wait(stop);
                    beforeRead?.Invoke(reader, -1);
                    readersReadyForConcurrentWrite.Signal();
                    concurrentWriteCompleted.Wait(stop);

                    for (int iteration = 0; iteration < ReaderIterations; iteration++)
                    {
                        roundBarrier.SignalAndWait(stop);
                        beforeRead?.Invoke(reader, iteration);
                        (NodeManagerRoutingTable.RoutingSnapshot pinnedSnapshot, AddressSpaceEpoch pinnedEpoch,
                            int pinnedSlot) = table.PinCurrent();
                        try
                        {
                            IAsyncNodeManager[] pinnedManagers = pinnedSnapshot.NodeManagers;
                            if (pinnedManagers.Length != 3 ||
                                !ReferenceEquals(pinnedManagers[0], firstPermanent) ||
                                !ReferenceEquals(pinnedManagers[1], secondPermanent) ||
                                !expectedManagers.Contains(pinnedManagers[2]) ||
                                pinnedSnapshot.Generations.Length != 3 ||
                                !ReferenceEquals(pinnedSnapshot.Generations[2].NodeManager, pinnedManagers[2]))
                            {
                                managerSnapshotFailures.Add(
                                    $"iteration {iteration}: pinned manager count {pinnedManagers.Length}");
                            }
                        }
                        finally
                        {
                            pinnedEpoch.Exit(pinnedSlot);
                        }

                        using IEnumerator<IAsyncNodeManager> managerSnapshot = table.GetEnumerator();
                        var managers = new List<IAsyncNodeManager>();
                        while (managerSnapshot.MoveNext())
                        {
                            managers.Add(managerSnapshot.Current);
                        }

                        if (managers.Count != 3 ||
                            !ReferenceEquals(managers[0], firstPermanent) ||
                            !ReferenceEquals(managers[1], secondPermanent) ||
                            !expectedManagers.Contains(managers[2]))
                        {
                            managerSnapshotFailures.Add(
                                $"iteration {iteration}: manager count {managers.Count}");
                        }

                        IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> namespaceRoutes =
                            table.NamespaceManagers;
                        int namespaceIndex = ConcurrentNamespaceIndexes[0];
                        bool hasRoute = namespaceRoutes.TryGetValue(
                            namespaceIndex,
                            out IReadOnlyList<IAsyncNodeManager>? route);
                        if (namespaceRoutes.Count != 1 ||
                            !hasRoute ||
                            route!.Count != 1 ||
                            !expectedManagers.Contains(route[0]))
                        {
                            namespaceSnapshotFailures.Add(
                                $"iteration {iteration}: hasRoute {hasRoute}, " +
                                $"count {(hasRoute ? route!.Count : -1)}");
                        }

                        Thread.Yield();
                        roundBarrier.SignalAndWait(stop);
                    }
                }
                catch (Exception failure)
                {
                    Interlocked.CompareExchange(ref firstFailure, ExceptionDispatchInfo.Capture(failure), null);
                    stopWorkers.Cancel();
                    throw;
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))];

            var completion = Task.WhenAll([writerTask, .. readerTasks]);
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch
            {
                Volatile.Read(ref firstFailure)?.Throw();
                throw;
            }
            finally
            {
                writerStarted.Set();
                concurrentWriteCompleted.Set();
            }

            Assert.That(writerIterations, Is.EqualTo(ReplacementCount));
            Assert.That(managerSnapshotFailures, Is.Empty);
            Assert.That(namespaceSnapshotFailures, Is.Empty);
        }

        private static NodeManagerRoutingTable CreateTable(
            out IAsyncNodeManager firstPermanent,
            out IAsyncNodeManager secondPermanent)
        {
            firstPermanent = CreateManager();
            secondPermanent = CreateManager();
            var table = new NodeManagerRoutingTable();
            table.AddInitial(firstPermanent);
            table.AddInitial(secondPermanent);
            return table;
        }

        private static IAsyncNodeManager CreateManager()
        {
            return new Mock<IAsyncNodeManager>().Object;
        }

        private static IAsyncNodeManager CreateManagerWithSync(INodeManager syncNodeManager)
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.Setup(m => m.SyncNodeManager).Returns(syncNodeManager);
            return manager.Object;
        }

        private static void AssertManagerSnapshot(
            IEnumerator<IAsyncNodeManager> snapshot,
            params IAsyncNodeManager[] expected)
        {
            foreach (IAsyncNodeManager expectedManager in expected)
            {
                Assert.That(snapshot.MoveNext(), Is.True);
                Assert.That(snapshot.Current, Is.SameAs(expectedManager));
            }
            Assert.That(snapshot.MoveNext(), Is.False);
        }

        private static void AssertSingleManagerRoute(
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routes,
            int namespaceIndex,
            IAsyncNodeManager expected)
        {
            Assert.That(routes.ContainsKey(namespaceIndex), Is.True);
            Assert.That(routes[namespaceIndex], Has.Count.EqualTo(1));
            Assert.That(routes[namespaceIndex][0], Is.SameAs(expected));
        }

        private sealed class EqualNodeManager() : AsyncNodeManagerAdapter(new Mock<INodeManager>().Object)
        {
            public override bool Equals(object? obj)
            {
                return obj is EqualNodeManager;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
