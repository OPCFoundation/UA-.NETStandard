// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Covers the deprecated synchronous node manager API on purpose.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.Tests.RuntimeNodeSet;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Keeps alias-store registration private until readiness and removes it after retirement.
        /// </summary>
        /// <param name="publish">Whether the candidate is published or abandoned.</param>
        /// <param name="register">Whether the manager registers its store with the server.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedAliasRegistrationFollowsPublicationAsync(bool publish, bool register)
        {
            using var store = new InMemoryAliasNameStore([]);
            IAliasNameStoreRegistry registry =
                ((IAliasNameStoreRegistryProvider)m_server.CurrentInstance).AliasNameStoreRegistry;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(owner => owner.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal staged, ApplicationConfiguration configuration, CancellationToken _) =>
                    new ValueTask<IAsyncNodeManager>(new AliasNameNodeManager(staged, configuration, store,
                        new AliasNameNodeManagerOptions
                        {
                            NamespaceUri = kModelNamespaceUri + ":alias",
                            RegisterWithServerRegistry = register,
                            MaterializeAliasNodes = false,
                            RefreshAliasNodesOnChange = false
                        })));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration? registration = null;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)]).ConfigureAwait(false))
            {
                Assert.That(registry.Stores, Does.Not.Contain(store));
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    registration = Registration(result.Registrations[0]);
                    Assert.That(registry.Stores.Contains(store), Is.EqualTo(register));
                }
            }
            if (registration is not null)
            {
                await using ILifecycleWriter exactWriter1 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(
                    CancellationToken.None).ConfigureAwait(false);
                await using IPreparedChanges removal = await exactWriter1.PrepareAsync(
                    [LifecycleChange.Remove(registration.Slot)]).ConfigureAwait(false);
                LifecycleResult result = await removal.CommitAsync(_ => default).ConfigureAwait(false);
                await exactWriter1.DisposeAsync().ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                await result.Retired.ConfigureAwait(false);
            }
            Assert.That(registry.Stores, Does.Not.Contain(store));
        }

        /// <summary>
        /// A replacement retains its alias-store ownership after the prior generation physically retires.
        /// </summary>
        /// <param name="sharedStore">Whether both generations use the same backend instance.</param>
        /// <param name="publish">Whether the replacement is committed or abandoned.</param>
        /// <param name="adapted">Whether both managers are hosted through their synchronous adapters.</param>
        [TestCase(true, true, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(false, false, false)]
        [TestCase(true, true, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(false, false, true)]
        public async Task AliasReplacementOwnsRegistrationAcrossPhysicalRetirementAsync(
            bool sharedStore, bool publish, bool adapted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            string uri = kModelNamespaceUri + ":alias-replacement";
            ushort namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(uri);
            var category = new NodeId("ReplacementCategory", namespaceIndex);
            var descriptor = new AliasNameCategoryDescriptor(
                category, new QualifiedName("ReplacementCategory", namespaceIndex));
            using var originalStore = new InMemoryAliasNameStore([descriptor]);
            using var distinctStore = new InMemoryAliasNameStore([descriptor]);
            InMemoryAliasNameStore replacementStore = sharedStore ? originalStore : distinctStore;
            IAliasNameStoreRegistry registry =
                ((IAliasNameStoreRegistryProvider)m_server.CurrentInstance).AliasNameStoreRegistry;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            IAliasNameStore[] originalStores = registry.Stores.ToArray();
            var managers = new List<AliasNameNodeManager>();
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(CreateFactory(originalStore)), ct).ConfigureAwait(false);
            NodeManagerRegistration current = RequireRegistration(publicationResult1.Registrations[0]);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(current.NodeManager);
            Assert.That(generation.Gate.TryEnter(out int stripe), Is.True);
            LifecycleResult? publication = null;
            try
            {
                Assert.That(NodeManagerGenerationRegistry.TryGet(managers[0], out NodeManagerGeneration? original),
                    Is.True);
                Assert.That(original, Is.SameAs(generation));
                ChangePreparer.StagingServer? claim = ChangePreparer.GetServer(current.NodeManager);
                Assert.That(claim, Is.Not.Null);
                Assert.That(ChangePreparer.GetServer(managers[0]), Is.SameAs(claim));
                Assert.That(ChangePreparer.GetServer(new AsyncNodeManagerAdapter(managers[0].SyncNodeManager)),
                    Is.SameAs(claim));
                Assert.That(registry.GetStoreForCategory(category), Is.SameAs(originalStore));
                await using ILifecycleWriter exactWriter2 = await Capture(
                    m_server.NodeManagerLifecycle, current).BeginAsync(ct).ConfigureAwait(false);
                await using (IPreparedChanges replacement = await exactWriter2.PrepareAsync(
                    [LifecycleChange.Replace(current.Slot, CreateFactory(replacementStore))], ct)
                    .ConfigureAwait(false))
                {
                    Assert.That(registry.GetStoreForCategory(category), Is.SameAs(originalStore));
                    if (publish)
                    {
                        publication = await replacement.CommitAsync(_ => default, ct).ConfigureAwait(false);
                        await exactWriter2.DisposeAsync().ConfigureAwait(false);
                        current = Registration(replacement.Registrations[0]);
                        Assert.That(NodeManagerGenerationRegistry.GetOrCreate(current.NodeManager).Slot,
                            Is.SameAs(generation.Slot));
                        Assert.That(generation.Slot.Id, Is.EqualTo(current.Id));
                        Assert.That(NodeManagerGenerationRegistry.TryGet(managers[1], out NodeManagerGeneration? next),
                            Is.True);
                        Assert.That(next, Is.SameAs(generation.Slot.Current));
                        Assert.That(publication.CleanupFailure, Is.Null);
                        Assert.That(publication.Retired.IsCompleted, Is.False);
                        Assert.That(registry.GetStoreForCategory(category), Is.SameAs(replacementStore));
                    }
                }
                generation.Gate.Exit(stripe);
                stripe = -1;
                if (publication is not null)
                {
                    await publication.Retired.WaitAsync(ct).ConfigureAwait(false);
                    Mock.Get(managers[0]).Verify(manager => manager.DisposeAsync(), Times.Once);
                }
                Assert.That(registry.GetStoreForCategory(category),
                    Is.SameAs(publish ? replacementStore : originalStore));
                Assert.That(registry.Stores, Is.EquivalentTo(
                    originalStores.Append(publish ? replacementStore : originalStore)));
            }
            finally
            {
                if (stripe >= 0)
                {
                    generation.Gate.Exit(stripe);
                }
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (publication is not null)
                {
                    await publication.Retired.WaitAsync(cleanup.Token).ConfigureAwait(false);
                }
                await RemoveAndRetireAsync(lifecycle, current, cleanup.Token).ConfigureAwait(false);
            }
            Assert.That(registry.GetStoreForCategory(category), Is.Null);
            Assert.That(registry.Stores, Is.EquivalentTo(originalStores));
            foreach (AliasNameNodeManager manager in managers)
            {
                Mock.Get(manager).Verify(owner => owner.DisposeAsync(), Times.Once);
            }

            IAsyncNodeManagerFactory CreateFactory(InMemoryAliasNameStore store)
            {
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(owner => owner.CreateAsync(It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .Returns((IServerInternal staged, ApplicationConfiguration configuration, CancellationToken _) =>
                    {
                        var manager = new Mock<AliasNameNodeManager>(
                            staged, configuration, store, new AliasNameNodeManagerOptions
                            {
                                NamespaceUri = uri,
                                MaterializeAliasNodes = false,
                                RefreshAliasNodesOnChange = false
                            })
                        { CallBase = true };
                        manager.As<INodeManagerReloadParticipant>().Setup(owner => owner.PrepareReloadAsync(
                            It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                            .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
                        managers.Add(manager.Object);
                        if (adapted)
                        {
                            return new ValueTask<IAsyncNodeManager>(
                                new AsyncNodeManagerAdapter(manager.Object.SyncNodeManager));
                        }
                        return new ValueTask<IAsyncNodeManager>(manager.Object);
                    });
                return factory.Object;
            }
        }

        /// <summary>
        /// Hosts the alias manager through an initial factory without accessing registries during construction.
        /// </summary>
        /// <param name="register">Whether readiness registers the store with the server.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InitialAliasFactoryRegistersAtReadinessAsync(bool register)
        {
            using var store = new InMemoryAliasNameStore([]);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(owner => owner.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal staged, ApplicationConfiguration configuration, CancellationToken _) =>
                    new ValueTask<IAsyncNodeManager>(new AliasNameNodeManager(staged, configuration, store,
                        new AliasNameNodeManagerOptions
                        {
                            RegisterWithServerRegistry = register,
                            MaterializeAliasNodes = false,
                            RefreshAliasNodesOnChange = false
                        })));
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
            {
                var server = new LifecycleTestServer(telemetry);
                server.AddNodeManager(factory.Object);
                return server;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                LifecycleTestServer server = await fixture.StartAsync(Path.Combine(m_pkiRoot, "initial-alias"))
                    .ConfigureAwait(false);
                IAliasNameStoreRegistry registry =
                    ((IAliasNameStoreRegistryProvider)server.CurrentInstance).AliasNameStoreRegistry;
                Assert.That(registry.Stores.Contains(store), Is.EqualTo(register));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A private candidate changes serving routes only at publication, never during preparation or abandonment.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedCandidateKeepsNamespaceRoutesPrivateUntilPublicationAsync(bool publish)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long revision = lifecycle.Revision;
            int namespaceCount = m_server.CurrentInstance.NamespaceUris.Count;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))]).ConfigureAwait(false))
            {
                Assert.That(lifecycle.Revision, Is.EqualTo(revision));
                Assert.That(m_server.CurrentInstance.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Revision, Is.EqualTo(revision + 1));
                }
            }
            Assert.That(lifecycle.Revision, Is.EqualTo(revision + (publish ? 1 : 0)));
            if (publish)
            {
                await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
            }
            else
            {
                Assert.That(m_server.CurrentInstance.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            }
        }

        /// <summary>
        /// Constructor-discovered namespace registrations remain private and are retained at publication.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedCandidateKeepsConstructorDiscoveredNamespacesPrivateAsync(bool publish)
        {
            const string discoveredUri = kModelNamespaceUri + ":ConstructorDiscovered";
            IServerInternal server = m_server.CurrentInstance;
            var host = (IDynamicNodeManagerBatchHost)server.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot initial = host.RoutingRevision;
            int namespaceCount = server.NamespaceUris.Count;
            var runtimeFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            IAsyncNodeManager? candidate = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(async (IServerInternal staged, ApplicationConfiguration configuration,
                    CancellationToken token) =>
                {
                    candidate = await runtimeFactory.CreateAsync(staged, configuration, token).ConfigureAwait(false);
                    staged.NodeManager.RegisterNamespaceManager(discoveredUri, candidate);
                    Assert.That(host.RoutingRevision, Is.SameAs(initial));
                    Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                    return candidate;
                });
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int namespaceIndex;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)]).ConfigureAwait(false))
            {
                IAsyncNodeManager manager = RequireLifecycleValue(candidate);
                Assert.That(manager.NamespaceUris, Does.Not.Contain(discoveredUri));
                namespaceIndex = ChangePreparer.GetServer(manager)!.NamespaceUris.GetIndex(discoveredUri);
                Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(namespaceCount));
                Assert.That(host.RoutingRevision, Is.SameAs(initial));
                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            if (publish)
            {
                Assert.That(host.RoutingRevision.Revision, Is.EqualTo(initial.Revision + 1));
                Assert.That(host.RoutingRevision.NamespaceManagers[namespaceIndex],
                    Does.Contain(RequireLifecycleValue(candidate)));
                await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
            }
            else
            {
                Assert.That(host.RoutingRevision, Is.SameAs(initial));
                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            }
        }

        /// <summary>
        /// Address-space preparation publishes retained discovered namespaces but not locally unregistered routes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedCandidatePublishesOnlyRetainedDiscoveredNamespacesAsync(bool publish)
        {
            const string discoveredUri = kModelNamespaceUri + ":Discovered";
            const string removedUri = kModelNamespaceUri + ":Removed";
            IServerInternal server = m_server.CurrentInstance;
            var host = (IDynamicNodeManagerBatchHost)server.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot initial = host.RoutingRevision;
            int namespaceCount = server.NamespaceUris.Count;
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(kModelNamespaceUri);
            manager.Setup(value => value.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    server.NodeManager.RegisterNamespaceManager(discoveredUri, manager.Object);
                    server.NodeManager.RegisterNamespaceManager(discoveredUri, manager.Object);
                    server.NodeManager.RegisterNamespaceManager(removedUri, manager.Object);
                    Assert.That(server.NodeManager.UnregisterNamespaceManager(
                        removedUri, manager.Object.SyncNodeManager!), Is.True);
                    Assert.That(server.NodeManager.UnregisterNamespaceManager(removedUri, manager.Object), Is.False);
                })
                .Returns(default(ValueTask));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(manager.Object));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int discoveredIndex;
            int removedIndex;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)]).ConfigureAwait(false))
            {
                NamespaceTable staged = ChangePreparer.GetServer(manager.Object)!.NamespaceUris;
                discoveredIndex = staged.GetIndex(discoveredUri);
                removedIndex = staged.GetIndex(removedUri);
                Assert.That(discoveredIndex, Is.GreaterThanOrEqualTo(namespaceCount));
                Assert.That(removedIndex, Is.GreaterThan(discoveredIndex));
                Assert.That(host.RoutingRevision, Is.SameAs(initial));
                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            NodeManagerRoutingTable.RoutingSnapshot final = host.RoutingRevision;
            if (publish)
            {
                Assert.That(final.Revision, Is.EqualTo(initial.Revision + 1));
                Assert.That(server.NamespaceUris.GetIndex(discoveredUri), Is.EqualTo(discoveredIndex));
                Assert.That(final.NamespaceManagers[discoveredIndex], Is.EqualTo(new[] { manager.Object }));
                Assert.That(final.NamespaceManagers.ContainsKey(removedIndex), Is.False);
            }
            else
            {
                Assert.That(final, Is.SameAs(initial));
                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            }
        }

        /// <summary>
        /// Creation and later-candidate failures clean up private namespace registrations without publishing routes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedCandidatePreparationKeepsServingRoutesUnchangedAsync(bool failDuringCreation)
        {
            const string discoveredUri = kModelNamespaceUri + ":FailedCandidate";
            IServerInternal server = m_server.CurrentInstance;
            var host = (IDynamicNodeManagerBatchHost)server.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot initial = host.RoutingRevision;
            int namespaceCount = server.NamespaceUris.Count;
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(kModelNamespaceUri);
            manager.Setup(value => value.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    server.NodeManager.RegisterNamespaceManager(discoveredUri, manager.Object);
                    if (failDuringCreation)
                    {
                        throw new IOException("Candidate address-space creation failed.");
                    }
                })
                .Returns(default(ValueTask));
            manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Callback(() => Assert.That(
                    server.NodeManager.UnregisterNamespaceManager(discoveredUri, manager.Object), Is.True))
                .Returns(default(ValueTask));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(manager.Object));
            var failingFactory = new Mock<IAsyncNodeManagerFactory>();
            failingFactory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Later factory failed."));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await Assert.ThatAsync(
                () => lifecycle.PrepareAsync(
                [LifecycleChange.Add(
                factory.Object), LifecycleChange.Add(
                failingFactory.Object)]).AsTask(), Throws.TypeOf<IOException>()).ConfigureAwait(false);
            Assert.That(host.RoutingRevision, Is.SameAs(initial));
            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            Assert.That(GetBranchRegistrations(lifecycle).IsEmpty, Is.True);
            manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Concurrent factories returning one manager cannot both claim its staging metadata.
        /// </summary>
        /// <param name="adapted">Whether each factory call returns a fresh round-trip facade.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentCandidateClaimsHaveExactlyOneOwnerAsync(bool adapted)
        {
            var first = new ChangePreparer(m_server.CurrentInstance);
            var second = new ChangePreparer(m_server.CurrentInstance);
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(kModelNamespaceUri);
            manager.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(manager.Object));
            IAsyncNodeManager resource = manager.Object;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IAsyncNodeManager>(adapted
                    ? new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(resource))
                    : resource));
            using var claims = new Barrier(2);
            bool[] results = await Task.WhenAll(
                Task.Run(() => ClaimAsync(first)),
                Task.Run(() => ClaimAsync(second))).ConfigureAwait(false);
            Assert.That(results.Count(success => success), Is.EqualTo(1));
            ChangePreparer.StagingServer owner = ChangePreparer.GetServer(resource)!;
            Assert.That(owner, Is.Not.Null);
            owner.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            Assert.That(first.NamespaceUris.GetIndex(kSecondModelNamespaceUri) >= 0, Is.EqualTo(results[0]));
            Assert.That(second.NamespaceUris.GetIndex(kSecondModelNamespaceUri) >= 0, Is.EqualTo(results[1]));

            async Task<bool> ClaimAsync(ChangePreparer preparation)
            {
                try
                {
                    IAsyncNodeManager claimed = await preparation.CreateNodeManagerAsync(
                        factory.Object, m_server.CurrentConfiguration, false, _ =>
                        {
                            if (!claims.SignalAndWait(TimeSpan.FromSeconds(30)))
                            {
                                throw new TimeoutException("Concurrent candidate claims did not rendezvous.");
                            }
                            return false;
                        }, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(NodeManagerGenerationRegistry.GetIdentity(claimed), Is.SameAs(resource));
                    Assert.That(ChangePreparer.GetServer(claimed),
                        Is.SameAs(ChangePreparer.GetServer(resource)));
                    return true;
                }
                catch (NodeManagerAlreadyRegisteredException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// A raw binding created by a private factory follows its facade through publication or abort.
        /// </summary>
        /// <param name="publish">Whether the candidate is published instead of abandoned.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PrivateForwardedRawBindingFollowsCandidateLifetimeAsync(bool publish)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            const string uri = kModelNamespaceUri + ":PrivateForwardedBinding";
            Mock<IAsyncNodeManager> facade = CreateLifecycleNodeManager(uri);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            TestableAsyncCustomNodeManager? resource = null;
            MonitoredNode2? monitored = null;
            IServerInternal? staged = null;
            bool admittedDuringFactory = true;
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((
                    IServerInternal candidateServer,
                    ApplicationConfiguration configuration,
                    CancellationToken _) =>
                {
                    staged = candidateServer;
                    var owner = new TestableAsyncCustomNodeManager(
                        candidateServer, configuration, false, NullLogger.Instance, uri);
                    resource = owner;
                    facade.SetupGet(value => value.SyncNodeManager).Returns(owner.SyncNodeManager);
                    var root = new BaseObjectState(null)
                    {
                        NodeId = new NodeId("private-forwarded-root", owner.NamespaceIndex)
                    };
                    monitored = new MonitoredNode2(owner, candidateServer, root);
                    admittedDuringFactory = MasterNodeManager.TryCaptureItemEmission(
                        candidateServer, owner, monitored.Generation, out var admission);
                    admission?.Dispose();
                    return new ValueTask<IAsyncNodeManager>(facade.Object);
                });
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration? registration = null;
            try
            {
                await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false))
                {
                    MonitoredNode2 source = RequireLifecycleValue(monitored);
                    TestableAsyncCustomNodeManager owner = RequireLifecycleValue(resource);
                    IServerInternal candidateServer = RequireLifecycleValue(staged);
                    NodeManagerGeneration candidate = NodeManagerGenerationRegistry.GetOrCreate(facade.Object);
                    Assert.That(RequireLifecycleValue(source.Generation)
                        .TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(admittedDuringFactory, Is.False);
                        Assert.That(bound!.NodeManager, Is.SameAs(owner));
                        Assert.That(bound, Is.Not.SameAs(candidate));
                        Assert.That(candidate.NodeManager, Is.SameAs(facade.Object));
                        Assert.That(bound!.Gate, Is.SameAs(candidate.Gate));
                        Assert.That(candidate.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                    });
                    bool beforePublication = MasterNodeManager.TryCaptureItemEmission(
                        candidateServer, owner, source.Generation, out var before);
                    before?.Dispose();
                    Assert.That(beforePublication, Is.False);

                    if (publish)
                    {
                        LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                            .ConfigureAwait(false);
                        Assert.That(result.CleanupFailure, Is.Null);
                        registration = RequireRegistration(result.Registrations[0]);
                        Assert.That(candidate.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                        bool afterPublication = MasterNodeManager.TryCaptureItemEmission(
                            candidateServer, owner, source.Generation, out var after);
                        try
                        {
                            Assert.That(afterPublication, Is.True);
                            Assert.That(after?.Generation, Is.SameAs(candidate));
                            Assert.That(after?.Generation.Gate, Is.SameAs(candidate.Gate));
                        }
                        finally
                        {
                            after?.Dispose();
                        }

                        bool eventAdmitted = MasterNodeManager.TryCaptureSourceEmission(
                            candidateServer, owner, out var eventAdmission, source.Generation);
                        using MasterNodeManager.SourceEmissionOrigin? origin = eventAdmission?.TransferToOrigin();
                        Assert.That(eventAdmitted, Is.True);
                        Assert.That(origin, Is.Not.Null);
                        bool childAdmitted = MasterNodeManager.TryCaptureSourceEmission(
                            candidateServer, owner, out var child, source.Generation,
                            context: origin!.CreateContext(owner.SystemContext));
                        using (child)
                        {
                            Assert.That(childAdmitted, Is.True);
                            Assert.That(child?.Generation, Is.SameAs(candidate));
                        }

                        // Another facade can identify the same raw manager without owning this route.
                        Mock<IAsyncNodeManager> otherFacade = CreateLifecycleNodeManager(uri);
                        otherFacade.SetupGet(value => value.SyncNodeManager).Returns(owner.SyncNodeManager);
                        var otherGeneration = new NodeManagerGeneration(
                            new NodeManagerSlot(), 1, otherFacade.Object, NodeManagerGenerationState.Prepared);
                        NodeManagerGeneration otherSource = otherGeneration.CreateForwardedSourceView(owner);
                        bool wrongFacadeAdmitted = MasterNodeManager.TryCaptureSourceEmission(
                            candidateServer, owner, out var wrongFacade, otherSource.Reference,
                            context: origin!.CreateContext(owner.SystemContext));
                        wrongFacade?.Dispose();
                        Assert.That(wrongFacadeAdmitted, Is.False);
                        GC.KeepAlive(otherSource);
                        GC.KeepAlive(otherGeneration);
                    }
                }

                if (registration is not null)
                {
                    await RemoveAndRetireAsync(lifecycle, registration, timeout.Token).ConfigureAwait(false);
                    registration = null;
                }

                MonitoredNode2 detachedSource = RequireLifecycleValue(monitored);
                bool admittedAfterCleanup = MasterNodeManager.TryCaptureItemEmission(
                    RequireLifecycleValue(staged), RequireLifecycleValue(resource),
                    detachedSource.Generation, out var afterCleanup);
                afterCleanup?.Dispose();
                Assert.That(admittedAfterCleanup, Is.False);
                if (!publish)
                {
                    Assert.That(RequireLifecycleValue(detachedSource.Generation)
                        .TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                    Assert.That(bound!.Gate.TryEnter(out int _), Is.False);
                }
                facade.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                try
                {
                    if (registration is not null)
                    {
                        await RemoveAndRetireAsync(lifecycle, registration, timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (monitored is not null)
                        {
                            await monitored.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (resource is not null)
                        {
                            await resource.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A factory cannot restore a retired owner while an admitted operation still prevents its reclamation.
        /// </summary>
        /// <param name="synchronous">Whether a fresh adapter returns the same synchronous resource identity.</param>
        /// <param name="roundtrip">
        /// Whether a fresh adapter wraps the retiring asynchronous resource's sync facade.
        /// </param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task FactoryCannotRestoreRetiringGenerationBeforeItsAdmissionDrainsAsync(
            bool synchronous, bool roundtrip)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            TrackingLifecycleNodeManager? tracked = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    if (synchronous)
                    {
                        return new ValueTask<IAsyncNodeManager>(new AsyncNodeManagerAdapter(
                            new ReferenceSynchronousNodeManager(server, configuration)));
                    }
                    tracked = new TrackingLifecycleNodeManager(
                        server, configuration, m_logger, kModelNamespaceUri, kFirstRegistrationValue);
                    return new ValueTask<IAsyncNodeManager>(tracked);
                });
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult publicationResult2 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(factory.Object), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult2.Registrations[0]);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(original.NodeManager);
            ChangePreparer.StagingServer? originalClaim = ChangePreparer.GetServer(original.NodeManager);
            Assert.That(originalClaim, Is.Not.Null);
            Assert.That(generation.Gate.TryEnter(out int stripe), Is.True);
            NodeManagerRegistration current = original;
            LifecycleResult? publication = null;
            try
            {
                await using ILifecycleWriter exactWriter3 = await Capture(
                    m_server.NodeManagerLifecycle, original).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using (IPreparedChanges replacement = await exactWriter3.PrepareAsync(
                    [LifecycleChange.Replace(original.Slot, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                    .ConfigureAwait(false))
                {
                    publication = await replacement.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                    await exactWriter3.DisposeAsync().ConfigureAwait(false);
                    current = Registration(replacement.Registrations[0]);
                }
                Assert.That(publication.CleanupFailure, Is.Null);
                Assert.That(publication.Retired.IsCompleted, Is.False);
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Task drain = generation.Gate.WaitForDrainAsync();
                Assert.That(drain.IsCompleted, Is.False);
                NodeManagerGeneration replacementGeneration =
                    NodeManagerGenerationRegistry.GetOrCreate(current.NodeManager);
                Assert.That(replacementGeneration.Slot, Is.SameAs(generation.Slot));
                var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
                NodeManagerRoutingTable.RoutingSnapshot view = host.RoutingRevision;
                long revision = lifecycle.Revision;
                IAsyncNodeManager reused = synchronous || roundtrip
                    ? new AsyncNodeManagerAdapter(RequireLifecycleValue(original.NodeManager.SyncNodeManager))
                    : original.NodeManager;
                var reuseFactory = new Mock<IAsyncNodeManagerFactory>();
                reuseFactory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(reused);

                await Assert.ThatAsync(async () =>
                {
                    await using ILifecycleWriter writer = await LifecycleInspection.Capture(
                        lifecycle, current).BeginAsync(timeout.Token).ConfigureAwait(false);
                    await using IPreparedChanges prepared = await writer.PrepareAsync(
                        [LifecycleChange.Replace(
                        current.Slot, reuseFactory.Object)], timeout.Token).ConfigureAwait(false);
                },
                    Throws.TypeOf<NodeManagerAlreadyRegisteredException>()).ConfigureAwait(false);

                reuseFactory.Verify(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                    Times.Once);
                Assert.That(NodeManagerGenerationRegistry.TryGet(reused, out NodeManagerGeneration? retained), Is.True);
                Assert.That(retained, Is.SameAs(generation));
                Assert.That(ChangePreparer.GetServer(reused), Is.SameAs(originalClaim));
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Assert.That(generation.Gate.WaitForDrainAsync(), Is.SameAs(drain));
                Assert.That(drain.IsCompleted, Is.False);
                Assert.That(publication.Retired.IsCompleted, Is.False);
                Assert.That(host.RoutingRevision, Is.SameAs(view));
                Assert.That(lifecycle.Revision, Is.EqualTo(revision));
                Assert.That(lifecycle.Registrations.Find(value => value.Id == current.Id), Is.SameAs(current));
                Assert.That(replacementGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                if (tracked is not null)
                {
                    Assert.That(tracked.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(tracked.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                generation.Gate.Exit(stripe);
                if (publication is not null)
                {
                    await publication.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await RemoveAndRetireAsync(lifecycle, current, timeout.Token).ConfigureAwait(false);
            }
            Assert.That(generation.Gate.ActiveCount, Is.Zero);
            Assert.That(generation.Gate.WaitForDrainAsync().IsCompleted, Is.True);
            if (tracked is not null)
            {
                Assert.That(tracked.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(tracked.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Rejects unsupported factories before creating or owning initial candidates.
        /// </summary>
        [Test]
        public async Task InitialPublicationRejectsUnsupportedFactoryBeforeCandidateEffectsAsync()
        {
            var candidate = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            LifecycleTestServer? rejectedServer = null;
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
            {
                rejectedServer = new LifecycleTestServer(telemetry)
                {
                    PrivateEncodeableFactory = new ForeignBatchFactory(EncodeableFactory.Create())
                };
                rejectedServer.AddNodeManager(candidate.Object);
                return rejectedServer;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.StartAsync(Path.Combine(m_pkiRoot, "foreign-factory")).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
                Assert.That(failure.InnerException, Is.TypeOf<NotSupportedException>());
                Assert.That(failure.InnerException!.Message,
                    Is.EqualTo("The server factory does not support prepared publication."));
                Assert.That(rejectedServer, Is.Not.Null);
                Assert.That(((NodeManagerLifecycle)rejectedServer!.NodeManagerLifecycle).HasPendingShutdownCleanup,
                    Is.False, "An unsupported factory must be rejected before system generations become owned.");
                candidate.VerifyNoOtherCalls();
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PreparedBatchFactoryOwnershipEndsWithServerLifetimeAsync()
        {
            IEncodeableFactory factory = EncodeableFactory.Create();
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
                new LifecycleTestServer(telemetry) { PrivateEncodeableFactory = factory })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            string pki = Path.Combine(m_pkiRoot, "factory-lifetime");
            ExpandedNodeId alias = new(8308, "urn:opcfoundation.org:Tests:FactoryLifetime");
            try
            {
                LifecycleTestServer first = await fixture.StartAsync(pki).ConfigureAwait(false);
                Assert.That(first.CurrentInstance.Factory.TryGetEncodeableType(
                    DataTypeIds.Range, out IEncodeableType? type), Is.True);
                AssertLifecycleValue(type);
                first.CurrentInstance.Factory.Builder.AddEncodeableType(alias, type).Commit();
                await fixture.StopAsync().ConfigureAwait(false);
                Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType? retained), Is.True,
                    "A supplied private factory must retain committed registrations when its server stops.");
                Assert.That(retained, Is.SameAs(type));
                LifecycleTestServer second = await fixture.StartAsync(pki).ConfigureAwait(false);
                Assert.That(second.CurrentInstance.Factory, Is.SameAs(factory));
                Assert.That(second.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.True);
                INodeManagerLifecycle lifecycle = second.NodeManagerLifecycle;
                await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateBatchComplexTypeOptions(false)))]).ConfigureAwait(false);
                LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PreparedBatchAliasIsPrivateWhenComplexTypeLoadingIsDisabledAsync()
        {
            LifecycleResult publicationResult3 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateBatchComplexTypeOptions(false))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration current = RequireRegistration(publicationResult3.Registrations[0]);
            m_server.LoadComplexTypes = false;
            ExpandedNodeId alias = new(kBatchAliasId, RuntimeNodeSetTestServer.NamespaceUri);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter4 = await Capture(
                m_server.NodeManagerLifecycle, current).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using (IPreparedChanges prepared = await exactWriter4.PrepareAsync(
                [LifecycleChange.Replace(
                current.Slot, new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(true)))])
                .ConfigureAwait(false))
            {
                Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.False);
                await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                await exactWriter4.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.True);
        }

        [Test]
        public async Task PreparedBatchFailureDoesNotRetainFactoryRegistrationsAsync()
        {
            var failed = new Mock<IAsyncNodeManagerFactory>();
            failed.Setup(factory => factory.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Later candidate preparation failed."));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await Assert.ThatAsync(() => lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false))),
                    LifecycleChange.Add(failed.Object)
                ]).AsTask(), Throws.TypeOf<IOException>()).ConfigureAwait(false);
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(
                new ExpandedNodeId(RuntimeNodeSetTestServer.TestPointDataType, RuntimeNodeSetTestServer.NamespaceUri),
                out _), Is.False);
            Assert.That(GetBranchRegistrations(lifecycle).IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchSchemaResolverChangesOnlyOnPublicationAsync(bool publish)
        {
            const string markerNamespace = "urn:opcfoundation.org:Tests:FactoryResolverMarker";
            ExpandedNodeId marker = new(1, markerNamespace);
            var original = new DataTypeDefinitionRegistry();
            original.Add(new UaTypeDescription(marker, new QualifiedName("Marker"),
                new EnumDefinition { Fields = [] }, markerNamespace));
            var holder = new ServerDataTypeDefinitionResolver();
            holder.SetResolver(original);
            m_server.ComplexTypeResolverHolder = holder;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))])
                .ConfigureAwait(false))
            {
                Assert.That(holder.TryResolve(marker, out _), Is.True);
                Assert.That(holder.TryResolve(structure, out _), Is.False);
                if (publish)
                {
                    await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                }
            }
            Assert.That(holder.TryResolve(marker, out _), Is.EqualTo(!publish));
            Assert.That(holder.TryResolve(structure, out _), Is.EqualTo(publish));
        }

        [Test]
        public async Task PreparedBatchRejectsChangedFactoryBeforeDecisionAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))])
                .ConfigureAwait(false);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? type), Is.True);
            AssertLifecycleValue(type);
            ExpandedNodeId alias = new(8305, "urn:opcfoundation.org:Tests:ConcurrentFactory");
            factory.Builder.AddEncodeableType(alias, type).Commit();
            int decisions = 0;
            await Assert.ThatAsync(() => prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }).AsTask(), Throws.TypeOf<OptimisticPublicationConflictException>()).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType? retained), Is.True);
            Assert.That(retained, Is.SameAs(type));
            Assert.That(prepared.IsCommitted, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchDoesNotLoseFactoryWritesDuringDecisionAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? type), Is.True);
            AssertLifecycleValue(type);
            ExpandedNodeId alias = new(8306, "urn:opcfoundation.org:Tests:ConcurrentFactory");
            IEncodeableFactoryBuilder writer = factory.Builder.AddEncodeableType(alias, type);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateBatchComplexTypeOptions(false)))], timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (rejectDecision)
                {
                    throw new IOException("Confirmed noncommit.");
                }
            }, timeout.Token).AsTask();
            bool accepted = false;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    writer.Commit();
                    accepted = true;
                }
                catch (InvalidOperationException)
                {
                    Assert.That(factory.TryGetEncodeableType(alias, out _), Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (rejectDecision)
            {
                await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                writer.Commit();
                accepted = true;
            }
            else
            {
                LifecycleResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
            }
            Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType? retained), Is.EqualTo(accepted),
                "A factory write must survive publication or be rejected before effects.");
            if (accepted)
            {
                Assert.That(retained, Is.SameAs(type));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchFactoryTypesRemainPrivateUntilPublicationAsync(bool publish)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            ExpandedNodeId encoding = new(RuntimeNodeSetTestServer.TestPointBinaryEncoding,
                RuntimeNodeSetTestServer.NamespaceUri);
            ExpandedNodeId enumeration = new(RuntimeNodeSetTestServer.TestColorDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            Assert.That(factory.TryGetEncodeableType(structure, out _), Is.False);
            Assert.That(factory.TryGetEnumeratedType(enumeration, out _), Is.False);
            bool preparedStructure;
            bool preparedEncoding;
            bool preparedEnumeration;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))])
                .ConfigureAwait(false))
            {
                preparedStructure = factory.TryGetEncodeableType(structure, out _);
                preparedEncoding = factory.TryGetEncodeableType(encoding, out _);
                preparedEnumeration = factory.TryGetEnumeratedType(enumeration, out _);
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(preparedStructure, Is.False, "A prepared structure must not enter the serving factory.");
                Assert.That(preparedEncoding, Is.False, "A prepared encoding must not enter the serving factory.");
                Assert.That(preparedEnumeration, Is.False, "A prepared enum must not enter the serving factory.");
                Assert.That(factory.TryGetEncodeableType(structure, out IEncodeableType? structureType),
                    Is.EqualTo(publish));
                Assert.That(factory.TryGetEncodeableType(encoding, out IEncodeableType? encodingType),
                    Is.EqualTo(publish));
                Assert.That(factory.TryGetEnumeratedType(enumeration, out _), Is.EqualTo(publish));
                if (publish)
                {
                    Assert.That(encodingType, Is.SameAs(structureType));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchEncodingAliasRemainsPrivateUntilPublicationAsync(bool publish)
        {
            LifecycleResult publicationResult4 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateBatchComplexTypeOptions(false))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration current = RequireRegistration(publicationResult4.Registrations[0]);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            ExpandedNodeId alias = new(kBatchAliasId, RuntimeNodeSetTestServer.NamespaceUri);
            Assert.That(factory.TryGetEncodeableType(structure, out IEncodeableType? original), Is.True);
            Assert.That(factory.TryGetEncodeableType(alias, out _), Is.False);
            bool visibleWhilePrepared;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter5 = await Capture(
                m_server.NodeManagerLifecycle, current).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using (IPreparedChanges prepared = await exactWriter5.PrepareAsync(
                [LifecycleChange.Replace(
                current.Slot, new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(true)))])
                .ConfigureAwait(false))
            {
                visibleWhilePrepared = factory.TryGetEncodeableType(alias, out _);
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    await exactWriter5.DisposeAsync().ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(visibleWhilePrepared, Is.False, "An alias must stay private until its owner is published.");
                Assert.That(factory.TryGetEncodeableType(alias, out IEncodeableType? resolved), Is.EqualTo(publish));
                Assert.That(factory.TryGetEncodeableType(structure, out IEncodeableType? retained), Is.True);
                Assert.That(retained, Is.SameAs(original));
                if (publish)
                {
                    Assert.That(resolved, Is.SameAs(original));
                }
            }
        }

        private static RuntimeNodeSetOptions CreateBatchComplexTypeOptions(bool includeAlias)
        {
            using Stream source = RuntimeNodeSetTestServer.OpenTestStream();
            XDocument document = XDocument.Load(source);
            XElement root = RequireLifecycleValue(document.Root);
            XNamespace ns = root.Name.Namespace;
            if (includeAlias)
            {
                XElement structure = root.Elements(ns + "UADataType").Single(element =>
                    RequireLifecycleValue(element.Attribute("NodeId")).Value ==
                        $"ns=1;i={RuntimeNodeSetTestServer.TestPointDataType}");
                RequireLifecycleValue(structure.Element(ns + "References")).Add(new XElement(ns + "Reference",
                    new XAttribute("ReferenceType", "HasEncoding"), $"ns=1;i={kBatchAliasId}"));
                root.Add(new XElement(ns + "UAObject",
                    new XAttribute("NodeId", $"ns=1;i={kBatchAliasId}"),
                    new XAttribute("BrowseName", "Default XML"),
                    new XElement(ns + "DisplayName", "Default XML"),
                    new XElement(ns + "References",
                        new XElement(ns + "Reference", new XAttribute("ReferenceType", "HasEncoding"),
                            new XAttribute("IsForward", "false"),
                            $"ns=1;i={RuntimeNodeSetTestServer.TestPointDataType}"),
                        new XElement(ns + "Reference", new XAttribute("ReferenceType", "HasTypeDefinition"), "i=76"))));
            }
            string xml = document.ToString();
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream("batch-complex-types",
                        _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [RuntimeNodeSetTestServer.NamespaceUri])
                ],
                DefaultNamespaceUri = RuntimeNodeSetTestServer.NamespaceUri
            };
        }

        /// <summary>
        /// Supplies the synchronous fixture's real reload capability through the transparent stack adapter.
        /// </summary>
        private sealed partial class ReferenceSynchronousNodeManager : INodeManagerReloadParticipant
        {
            /// <inheritdoc/>
            public ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
                IAsyncNodeManager replacement,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<LocalReference>>([]);
            }
        }

        private sealed class ForeignBatchFactory(IEncodeableFactory factory) : IEncodeableFactory
        {
            public IEnumerable<ExpandedNodeId> KnownTypeIds => factory.KnownTypeIds;
            public IEncodeableFactoryBuilder Builder => factory.Builder;

            public bool TryGetEncodeableType(ExpandedNodeId typeId, [NotNullWhen(true)] out IEncodeableType? type)
            {
                return factory.TryGetEncodeableType(typeId, out type);
            }

            public bool TryGetEnumeratedType(ExpandedNodeId typeId, [NotNullWhen(true)] out IEnumeratedType? type)
            {
                return factory.TryGetEnumeratedType(typeId, out type);
            }

            public bool TryGetType(XmlQualifiedName xmlName, [NotNullWhen(true)] out IType? type)
            {
                return factory.TryGetType(xmlName, out type);
            }
        }

        private const uint kBatchAliasId = 15012;
    }
}
