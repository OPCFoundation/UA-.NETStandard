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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.Tests.RuntimeNodeSet;

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
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)]).ConfigureAwait(false))
            {
                Assert.That(registry.Stores, Does.Not.Contain(store));
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    registration = result.Registrations[0];
                    Assert.That(registry.Stores.Contains(store), Is.EqualTo(register));
                }
            }
            if (registration is not null)
            {
                await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(registration)]).ConfigureAwait(false);
                NodeManagerBatchResult result = await removal.CommitAsync(_ => default).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                await result.Retirement.ConfigureAwait(false);
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
            NodeManagerRegistration current = await lifecycle.AddAsync(CreateFactory(originalStore), ct)
                .ConfigureAwait(false);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(current.NodeManager);
            Assert.That(generation.Gate.TryEnter(out int stripe), Is.True);
            NodeManagerBatchResult? publication = null;
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
                await using (IPreparedNodeManagerBatch replacement = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(current, CreateFactory(replacementStore))], ct)
                    .ConfigureAwait(false))
                {
                    Assert.That(registry.GetStoreForCategory(category), Is.SameAs(originalStore));
                    if (publish)
                    {
                        publication = await replacement.CommitAsync(_ => default, ct).ConfigureAwait(false);
                        current = replacement.Registrations[0];
                        Assert.That(NodeManagerGenerationRegistry.GetOrCreate(current.NodeManager).Slot,
                            Is.SameAs(generation.Slot));
                        Assert.That(generation.Slot.Id, Is.EqualTo(current.Id));
                        Assert.That(NodeManagerGenerationRegistry.TryGet(managers[1], out NodeManagerGeneration? next),
                            Is.True);
                        Assert.That(next, Is.SameAs(generation.Slot.Current));
                        Assert.That(publication.CleanupFailure, Is.Null);
                        Assert.That(publication.Retirement.IsCompleted, Is.False);
                        Assert.That(registry.GetStoreForCategory(category), Is.SameAs(replacementStore));
                    }
                }
                generation.Gate.Exit(stripe);
                stripe = -1;
                if (publication is not null)
                {
                    await publication.Retirement.WaitAsync(ct).ConfigureAwait(false);
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
                    await publication.Retirement.WaitAsync(cleanup.Token).ConfigureAwait(false);
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
                            }) { CallBase = true };
                        manager.As<INodeManagerReloadParticipant>().Setup(owner => owner.PrepareReloadAsync(
                            It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                            .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
                        managers.Add(manager.Object);
                        if (adapted)
                        {
                            var adapter = new Mock<AsyncNodeManagerAdapter>(manager.Object.SyncNodeManager)
                            {
                                CallBase = true
                            };
                            adapter.As<INodeManagerReloadParticipant>().Setup(owner => owner.PrepareReloadAsync(
                                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
                            return new ValueTask<IAsyncNodeManager>(adapter.Object);
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
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))]).ConfigureAwait(false))
            {
                Assert.That(lifecycle.Revision, Is.EqualTo(revision));
                Assert.That(m_server.CurrentInstance.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            int namespaceIndex;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], null).ConfigureAwait(false))
            {
                IAsyncNodeManager manager = RequireLifecycleValue(candidate);
                Assert.That(manager.NamespaceUris, Does.Not.Contain(discoveredUri));
                namespaceIndex = ChangePreparer.GetServer(manager)!.NamespaceUris.GetIndex(discoveredUri);
                Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(namespaceCount));
                Assert.That(host.RoutingRevision, Is.SameAs(initial));
                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            int discoveredIndex;
            int removedIndex;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], null).ConfigureAwait(false))
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
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await Assert.ThatAsync(() => lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object), NodeManagerBatchChange.Add(failingFactory.Object)],
                null).AsTask(), Throws.TypeOf<IOException>()).ConfigureAwait(false);
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
        /// A factory cannot restore a retired owner while an admitted operation still prevents its reclamation.
        /// </summary>
        /// <param name="synchronous">Whether a fresh adapter returns the same synchronous resource identity.</param>
        /// <param name="roundtrip">Whether a fresh adapter wraps the retiring asynchronous resource's sync facade.</param>
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
                        var adapter = new Mock<AsyncNodeManagerAdapter>(
                            new ReferenceSynchronousNodeManager(server, configuration)) { CallBase = true };
                        adapter.As<INodeManagerReloadParticipant>().Setup(value => value.PrepareReloadAsync(
                            It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                            .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
                        return new ValueTask<IAsyncNodeManager>(adapter.Object);
                    }
                    tracked = new TrackingLifecycleNodeManager(
                        server, configuration, m_logger, kModelNamespaceUri, kFirstRegistrationValue);
                    return new ValueTask<IAsyncNodeManager>(tracked);
                });
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration original = await lifecycle.AddAsync(factory.Object, timeout.Token)
                .ConfigureAwait(false);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(original.NodeManager);
            ChangePreparer.StagingServer? originalClaim = ChangePreparer.GetServer(original.NodeManager);
            Assert.That(originalClaim, Is.Not.Null);
            Assert.That(generation.Gate.TryEnter(out int stripe), Is.True);
            NodeManagerRegistration current = original;
            NodeManagerBatchResult? publication = null;
            try
            {
                await using (IPreparedNodeManagerBatch replacement = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(original, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                    .ConfigureAwait(false))
                {
                    publication = await replacement.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                    current = replacement.Registrations[0];
                }
                Assert.That(publication.CleanupFailure, Is.Null);
                Assert.That(publication.Retirement.IsCompleted, Is.False);
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

                await Assert.ThatAsync(() => lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(current, reuseFactory.Object)], timeout.Token).AsTask(),
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
                Assert.That(publication.Retirement.IsCompleted, Is.False);
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
                    await publication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
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
                var lifecycle = (INodeManagerBatchLifecycle)second.NodeManagerLifecycle;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateBatchComplexTypeOptions(false)))], null).ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
            NodeManagerRegistration current = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateBatchComplexTypeOptions(false), null).ConfigureAwait(false);
            m_server.LoadComplexTypes = false;
            ExpandedNodeId alias = new(kBatchAliasId, RuntimeNodeSetTestServer.NamespaceUri);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(current,
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(true)))],
                null)
                .ConfigureAwait(false))
            {
                Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.False);
                await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await Assert.ThatAsync(() => lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(
                        new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false))),
                    NodeManagerBatchChange.Add(failed.Object)
                ], null).AsTask(), Throws.TypeOf<IOException>()).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))],
                null)
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))],
                null)
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
            }).AsTask(), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? type), Is.True);
            AssertLifecycleValue(type);
            ExpandedNodeId alias = new(8306, "urn:opcfoundation.org:Tests:ConcurrentFactory");
            IEncodeableFactoryBuilder writer = factory.Builder.AddEncodeableType(alias, type);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))],
                null,
                timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
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
                NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
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
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(false)))],
                null)
                .ConfigureAwait(false))
            {
                preparedStructure = factory.TryGetEncodeableType(structure, out _);
                preparedEncoding = factory.TryGetEncodeableType(encoding, out _);
                preparedEnumeration = factory.TryGetEnumeratedType(enumeration, out _);
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
            NodeManagerRegistration current = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateBatchComplexTypeOptions(false), null).ConfigureAwait(false);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            ExpandedNodeId structure = new(RuntimeNodeSetTestServer.TestPointDataType,
                RuntimeNodeSetTestServer.NamespaceUri);
            ExpandedNodeId alias = new(kBatchAliasId, RuntimeNodeSetTestServer.NamespaceUri);
            Assert.That(factory.TryGetEncodeableType(structure, out IEncodeableType? original), Is.True);
            Assert.That(factory.TryGetEncodeableType(alias, out _), Is.False);
            bool visibleWhilePrepared;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(current,
                    new RuntimeNodeSetNodeManagerFactory(CreateBatchComplexTypeOptions(true)))],
                null)
                .ConfigureAwait(false))
            {
                visibleWhilePrepared = factory.TryGetEncodeableType(alias, out _);
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
