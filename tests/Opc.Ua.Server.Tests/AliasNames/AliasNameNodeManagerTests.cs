/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for <see cref="AliasNameNodeManager"/> built around a
    /// mocked <see cref="IServerInternal"/> following the pattern used by
    /// <c>AsyncCustomNodeManagerTests</c>.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameNodeManagerTests
    {
        private const string c_namespaceUri = "http://example.org/AliasNames/";

        private Mock<IServerInternal> m_mockServer;
        private NamespaceTable m_namespaceTable;
        private ApplicationConfiguration m_configuration;
        private InMemoryAliasNameStore m_store;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_mockServer.As<IAliasNameStoreRegistryProvider>();
            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(c_namespaceUri);

            var stringTable = new StringTable();
            var typeTable = new TypeTable(m_namespaceTable);

            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();

            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(stringTable);
            m_mockServer.Setup(s => s.TypeTree).Returns(typeTable);
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.Telemetry).Returns(telemetry);
            m_mockServer.Setup(s => s.MessageContext).Returns(new ServiceMessageContext(telemetry));
            m_mockServer.Setup(s => s.DefaultSystemContext)
                .Returns(new ServerSystemContext(m_mockServer.Object));

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            // No additional type-tree subtype seeding is needed because
            // the tests pass NodeId.Null / References as the reference
            // type filter — InMemoryAliasNameStore short-circuits in that
            // case and never invokes ITypeTable.IsTypeOf.

            var categoryId = new NodeId("MyCategory", 1);
            var descriptor = new AliasNameCategoryDescriptor(
                categoryId,
                new QualifiedName("MyCategory", 1),
                AliasNameCapabilities.All);
            m_store = new InMemoryAliasNameStore([descriptor]);
        }

        [TearDown]
        public void TearDown()
        {
            m_store?.Dispose();
        }

        private AliasNameNodeManager CreateManager(
            AliasNameNodeManagerOptions? options = null)
        {
            return new AliasNameNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_store,
                options ??
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false
                });
        }

        /// <summary>
        /// Legacy injected registries retain single-owner registration but reject an unsafe shared claim.
        /// </summary>
        /// <param name="alreadyRegistered">Whether an independent owner already registered the backend.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyRegistryRequiresExclusiveStoreOwnershipAsync(bool alreadyRegistered)
        {
            using var backing = new AliasNameStoreRegistry();
            if (alreadyRegistered)
            {
                backing.Register(m_store);
            }
            var legacy = new Mock<IAliasNameStoreRegistry>();
            legacy.SetupGet(registry => registry.Stores).Returns(() => backing.Stores);
            legacy.Setup(registry => registry.Register(It.IsAny<IAliasNameStore>()))
                .Callback<IAliasNameStore>(backing.Register);
            legacy.Setup(registry => registry.Unregister(It.IsAny<IAliasNameStore>()))
                .Callback<IAliasNameStore>(backing.Unregister);
            m_mockServer.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(provider => provider.AliasNameStoreRegistry).Returns(legacy.Object);
            var manager = CreateManager(new AliasNameNodeManagerOptions
            {
                NamespaceUri = c_namespaceUri,
                RegisterWithServerRegistry = true,
                MaterializeAliasNodes = false,
                RefreshAliasNodesOnChange = false
            });
            try
            {
                if (alreadyRegistered)
                {
                    await Assert.ThatAsync(() => manager.CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>()).AsTask(),
                        Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
                    legacy.Verify(registry => registry.Register(It.IsAny<IAliasNameStore>()), Times.Never);
                }
                else
                {
                    await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    await manager.OnServerReadyAsync().ConfigureAwait(false);
                    await manager.OnServerReadyAsync().ConfigureAwait(false);
                    legacy.Verify(registry => registry.Register(m_store), Times.Once);
                }
                Assert.That(backing.GetStoreForCategory(new NodeId("MyCategory", 1)), Is.SameAs(m_store));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(backing.Stores.Contains(m_store), Is.EqualTo(alreadyRegistered));
            legacy.Verify(registry => registry.Unregister(m_store),
                alreadyRegistered ? Times.Never() : Times.Once());
            if (!alreadyRegistered)
            {
                await using (AliasNameNodeManager restarted = CreateManager(new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    MaterializeAliasNodes = false,
                    RefreshAliasNodesOnChange = false
                }))
                {
                    await restarted.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    await restarted.OnServerReadyAsync().ConfigureAwait(false);
                    Assert.That(backing.GetStoreForCategory(new NodeId("MyCategory", 1)), Is.SameAs(m_store));
                    legacy.Verify(registry => registry.Register(m_store), Times.Exactly(2));
                }
                Assert.That(backing.Stores, Is.Empty);
                legacy.Verify(registry => registry.Unregister(m_store), Times.Exactly(2));
            }
        }

        /// <summary>
        /// Private preparation rejects replacing a legacy claim but allows a first alias manager in an older slot.
        /// </summary>
        /// <param name="replacesAlias">Whether the previous generation owns a registered alias store.</param>
        /// <param name="adapted">Whether the managers are hosted through an asynchronous/synchronous round trip.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task LegacyRegistryValidatesReplacementDuringPrivatePreparationAsync(
            bool replacesAlias, bool adapted)
        {
            using var backing = new AliasNameStoreRegistry();
            using var previousStore = new InMemoryAliasNameStore([]);
            var legacy = new Mock<IAliasNameStoreRegistry>();
            legacy.SetupGet(registry => registry.Stores).Returns(() => backing.Stores);
            legacy.Setup(registry => registry.Register(It.IsAny<IAliasNameStore>()))
                .Callback<IAliasNameStore>(backing.Register);
            legacy.Setup(registry => registry.Unregister(It.IsAny<IAliasNameStore>()))
                .Callback<IAliasNameStore>(backing.Unregister);
            m_mockServer.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(provider => provider.AliasNameStoreRegistry).Returns(legacy.Object);
            using var previousAlias = new AliasNameNodeManager(
                m_mockServer.Object, m_configuration, previousStore,
                new AliasNameNodeManagerOptions { NamespaceUri = c_namespaceUri, RefreshAliasNodesOnChange = false });
            IAsyncNodeManager previous = replacesAlias ? previousAlias : Mock.Of<IAsyncNodeManager>();
            if (replacesAlias && adapted)
            {
                previous = new AsyncNodeManagerAdapter(previousAlias.SyncNodeManager);
            }
            if (replacesAlias)
            {
                await previous.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                await ((INodeManagerReadinessNotification)previous).OnServerReadyAsync().ConfigureAwait(false);
            }
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(previous);
            Assert.That(original.Gate.TryEnter(out int stripe), Is.True);
            var preparation = new ChangePreparer(m_mockServer.Object);
            using ChangePreparer.StagingServer facade = preparation.CreateServer();
            await using var candidate = new AliasNameNodeManager(
                facade, m_configuration, m_store, new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    MaterializeAliasNodes = false,
                    RefreshAliasNodesOnChange = false
                });
            IAsyncNodeManager hostedCandidate = adapted
                ? new AsyncNodeManagerAdapter(candidate.SyncNodeManager)
                : candidate;
            NodeManagerGeneration next = NodeManagerGenerationRegistry.CreateReplacement(original, hostedCandidate);
            facade.Attach(candidate);
            try
            {
                Assert.That(next.Number, Is.EqualTo(2));
                Assert.That(next.Slot, Is.SameAs(original.Slot));
                Assert.That(() => ((IAliasNameStoreRegistryProvider)facade).AliasNameStoreRegistry,
                    Throws.TypeOf<InvalidOperationException>());
                if (replacesAlias)
                {
                    await Assert.ThatAsync(() => hostedCandidate.CreateAddressSpaceAsync(
                        new Dictionary<NodeId, IList<IReference>>()).AsTask(),
                        Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
                    Assert.That(facade.IsStaging, Is.True);
                    Assert.That(original.Slot.Current, Is.SameAs(original));
                    Assert.That(backing.Stores, Is.EqualTo(new[] { previousStore }));
                    legacy.Verify(registry => registry.Register(m_store), Times.Never);
                }
                else
                {
                    await hostedCandidate.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    Assert.That(facade.IsStaging, Is.True);
                    Assert.That(backing.Stores, Is.Empty);
                    facade.Bind();
                    await ((INodeManagerReadinessNotification)hostedCandidate).OnServerReadyAsync()
                        .ConfigureAwait(false);
                    Assert.That(backing.Stores, Is.EqualTo(new[] { m_store }));
                }
            }
            finally
            {
                original.Gate.Exit(stripe);
            }
        }

        /// <summary>
        /// Readiness serializes claim acquisition with a concurrent readiness or disposal callback.
        /// </summary>
        /// <param name="dispose">Whether the competing callback disposes the manager.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadinessSerializesOwnedRegistrationWithCompetingCallbacksAsync(bool dispose)
        {
            using var backing = new AliasNameStoreRegistry();
            using var acquired = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var competing = new ManualResetEventSlim();
            var registry = new Mock<IAliasNameStoreRegistry>();
            var registrations = registry.As<IAliasNameStoreRegistrationProvider>();
            int calls = 0;
            registrations.Setup(provider => provider.AcquireRegistration(
                It.IsAny<IAliasNameStore>(), It.IsAny<Guid>()))
                .Returns((IAliasNameStore store, Guid owner) =>
                {
                    IDisposable claim = backing.AcquireRegistration(store, owner);
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        acquired.Set();
                        if (!release.Wait(TimeSpan.FromSeconds(30)))
                        {
                            throw new TimeoutException("The first registration was not released.");
                        }
                    }
                    return claim;
                });
            m_mockServer.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(provider => provider.AliasNameStoreRegistry).Returns(registry.Object);
            var manager = CreateManager(new AliasNameNodeManagerOptions
            {
                NamespaceUri = c_namespaceUri,
                MaterializeAliasNodes = false,
                RefreshAliasNodesOnChange = false
            });
            Task first = Task.Run(async () => await manager.OnServerReadyAsync().ConfigureAwait(false));
            Task? second = null;
            try
            {
                Assert.That(acquired.Wait(TimeSpan.FromSeconds(30)), Is.True);
                Thread? competingThread = null;
                second = Task.Run(async () =>
                {
                    competingThread = Thread.CurrentThread;
                    competing.Set();
                    if (dispose)
                    {
                        manager.Dispose();
                    }
                    else
                    {
                        await manager.OnServerReadyAsync().ConfigureAwait(false);
                    }
                });
                Assert.That(competing.Wait(TimeSpan.FromSeconds(30)), Is.True);
                // Thread state coordinates the interleaving; claim count and final inventory prove ownership.
                Assert.That(SpinWait.SpinUntil(() => second.IsCompleted ||
                    (competingThread!.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(30)), Is.True,
                    "The started competing callback must reach a completion or blocking boundary.");
                release.Set();
                await Task.WhenAll(first, second).ConfigureAwait(false);
                Assert.That(calls, Is.EqualTo(1));
                if (!dispose)
                {
                    Assert.That(backing.GetStoreForCategory(new NodeId("MyCategory", 1)), Is.SameAs(m_store));
                    manager.Dispose();
                }
                Assert.That(backing.Stores, Is.Empty);
                Assert.That(() => manager.OnServerReadyAsync(), Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                release.Set();
                await first.ConfigureAwait(false);
                if (second is not null)
                {
                    await second.ConfigureAwait(false);
                }
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A provider that reenters disposal cannot leave a claim acquired after the disposal callback.
        /// </summary>
        /// <param name="owned">Whether the provider returns an owned claim instead of a legacy registration.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReentrantProviderDisposalDoesNotRetainRegistrationAsync(bool owned)
        {
            using var backing = new AliasNameStoreRegistry();
            var registry = new Mock<IAliasNameStoreRegistry>();
            AliasNameNodeManager? manager = null;
            registry.SetupGet(provider => provider.Stores).Returns(() => backing.Stores);
            registry.Setup(provider => provider.Register(m_store)).Callback(() =>
            {
                backing.Register(m_store);
                manager!.Dispose();
            });
            registry.Setup(provider => provider.Unregister(m_store)).Callback(() => backing.Unregister(m_store));
            if (owned)
            {
                registry.As<IAliasNameStoreRegistrationProvider>()
                    .Setup(provider => provider.AcquireRegistration(m_store, It.IsAny<Guid>()))
                    .Returns((IAliasNameStore store, Guid owner) =>
                    {
                        IDisposable claim = backing.AcquireRegistration(store, owner);
                        manager!.Dispose();
                        return claim;
                    });
            }
            m_mockServer.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(provider => provider.AliasNameStoreRegistry).Returns(registry.Object);
            manager = CreateManager(new AliasNameNodeManagerOptions
            {
                NamespaceUri = c_namespaceUri,
                MaterializeAliasNodes = false,
                RefreshAliasNodesOnChange = false
            });
            try
            {
                Assert.That(() => manager.OnServerReadyAsync(), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(backing.Stores, Is.Empty);
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Readiness revalidates ownership when another manager claims a category after private validation.
        /// </summary>
        [Test]
        public async Task OwnedRegistrationRevalidatesAfterPrivateCompatibilityCheckAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            m_mockServer.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(provider => provider.AliasNameStoreRegistry).Returns(registry);
            using var independent = new InMemoryAliasNameStore(
            [
                new AliasNameCategoryDescriptor(new NodeId("MyCategory", 1), new QualifiedName("MyCategory", 1))
            ]);
            var manager = CreateManager(new AliasNameNodeManagerOptions
            {
                NamespaceUri = c_namespaceUri,
                MaterializeAliasNodes = false,
                RefreshAliasNodesOnChange = false
            });
            try
            {
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                Assert.That(registry.Stores, Is.Empty);
                using IDisposable claim = registry.AcquireRegistration(independent, Guid.NewGuid());
                await manager.OnServerReadyAsync().ConfigureAwait(false);
                Assert.That(registry.GetStoreForCategory(new NodeId("MyCategory", 1)), Is.SameAs(independent));
                await manager.DisposeAsync().ConfigureAwait(false);
                Assert.That(registry.GetStoreForCategory(new NodeId("MyCategory", 1)), Is.SameAs(independent));
                Assert.That(registry.Stores, Is.EqualTo(new[] { independent }));
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(registry.Stores, Is.Empty);
        }

        [Test]
        public async Task CreateAddressSpaceBuildsCategoryWithAllOptionalChildrenAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            var externalReferences =
                new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(
                    new NodeId("MyCategory", 1));
            Assert.That(category, Is.Not.Null);
            Assert.That(category.FindAlias, Is.Not.Null);
            Assert.That(category.FindAliasVerbose, Is.Not.Null);
            Assert.That(category.AddAliasesToCategory, Is.Not.Null);
            Assert.That(category.DeleteAliasesFromCategory, Is.Not.Null);
            Assert.That(category.LastChange, Is.Not.Null);

            Assert.That(externalReferences.TryGetValue(
                ObjectIds.Aliases, out IList<IReference>? refs), Is.True);
            bool hasOrganizes = false;
            foreach (IReference r in refs!)
            {
                if (r.ReferenceTypeId.Equals(ReferenceTypeIds.Organizes) &&
                    r.TargetId.Equals(category.NodeId))
                {
                    hasOrganizes = true;
                    break;
                }
            }
            Assert.That(hasOrganizes, Is.True,
                "Expected an Organizes external reference from Aliases to category.");
        }

        [Test]
        public async Task OmittingCapabilitiesSkipsOptionalChildrenAsync()
        {
            m_store.Dispose();
            var minimal = new AliasNameCategoryDescriptor(
                new NodeId("Minimal", 1),
                new QualifiedName("Minimal", 1),
                AliasNameCapabilities.None);
            m_store = new InMemoryAliasNameStore([minimal]);

            using AliasNameNodeManager manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(
                    new NodeId("Minimal", 1));
            Assert.That(category, Is.Not.Null);
            Assert.That(category.FindAlias, Is.Not.Null,
                "Mandatory FindAlias must always be present.");
            Assert.That(category.FindAliasVerbose, Is.Null);
            Assert.That(category.AddAliasesToCategory, Is.Null);
            Assert.That(category.DeleteAliasesFromCategory, Is.Null);
            Assert.That(category.LastChange, Is.Null);
        }

        [Test]
        public async Task FindAliasHandlerReturnsStoreAliasesAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            var categoryId = new NodeId("MyCategory", 1);
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("Alpha",
                        new ExpandedNodeId("Tgt", 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);
            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant("%"),
                    new Variant(NodeId.Null)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await category.FindAlias!.CallAsync(
                manager.SystemContext,
                categoryId,
                input,
                argumentErrors,
                output,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.Null.Or.EqualTo(ServiceResult.Good));
            Assert.That(output, Has.Count.EqualTo(1));
            Assert.That(output[0].TryGetStructure(
                out ArrayOf<AliasNameDataType> aliases), Is.True);
            Assert.That(aliases.Count, Is.EqualTo(1));
            Assert.That(aliases[0].AliasName.Name, Is.EqualTo("Alpha"));
        }

        [Test]
        public async Task CreateAddressSpaceMaterializesAliasNodesAsync()
        {
            // Aliases present in the store BEFORE the address space is
            // built become browsable AliasNameType nodes (Part 17 §6.2) —
            // the same materialization DiagnosticsNodeManager applies to
            // the standard well-known categories.
            var categoryId = new NodeId("MyCategory", 1);
            var target = new ExpandedNodeId("Tgt", 1);
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("Alpha", target, null, ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            using AliasNameNodeManager manager = CreateManager();
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

            var aliasNodeId = new NodeId(
                Utils.Format("{0}.{1}", categoryId, "Alpha"), 1);
            AliasNameState aliasNode = manager
                .FindPredefinedNode<AliasNameState>(aliasNodeId);
            Assert.That(aliasNode, Is.Not.Null,
                "The alias must be materialized as a browsable AliasNameType node.");
            Assert.That(aliasNode.BrowseName.Name, Is.EqualTo("Alpha"));
            Assert.That(aliasNode.TypeDefinitionId,
                Is.EqualTo(ObjectTypeIds.AliasNameType));

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);
            Assert.That(
                category.ReferenceExists(
                    ReferenceTypeIds.Organizes, false, aliasNodeId),
                Is.True,
                "The category organizes its alias node.");
            Assert.That(
                aliasNode.ReferenceExists(
                    ReferenceTypeIds.AliasFor, false, new NodeId("Tgt", 1)),
                Is.True,
                "The alias node carries the AliasFor reference to its target.");
            Assert.That(externalReferences.ContainsKey(new NodeId("Tgt", 1)),
                Is.True,
                "The inverse HasAlias reference is queued for the target's manager.");
        }

        [Test]
        public async Task MaterializeAliasNodesCanBeDisabledAsync()
        {
            var categoryId = new NodeId("MyCategory", 1);
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("Alpha",
                        new ExpandedNodeId("Tgt", 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            using AliasNameNodeManager manager = CreateManager(
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false,
                    MaterializeAliasNodes = false
                });
            await manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.That(
                manager.FindPredefinedNode<AliasNameCategoryState>(categoryId),
                Is.Not.Null,
                "The category tree is still built.");
            Assert.That(
                manager.FindPredefinedNode<AliasNameState>(
                    new NodeId(Utils.Format("{0}.{1}", categoryId, "Alpha"), 1)),
                Is.Null,
                "No alias instance nodes are created when the option is off.");
        }

        [Test]
        public async Task LastChangeReflectsStoreUpdatesAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);

            uint initial = category.LastChange!.Value;
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("LC1",
                        new ExpandedNodeId("Tgt", 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(category.LastChange.Value, Is.Not.EqualTo(initial));
        }

        private static readonly string[] s_singleX = ["X"];
        private static readonly ExpandedNodeId[] s_singleT = [new("T", 1)];
        private static readonly string[] s_singleEmpty = [string.Empty];

        [Test]
        public async Task AddAliasesAnonymousIsRejectedWithBadUserAccessDeniedAsync()
        {
            using AliasNameNodeManager manager = CreateManager(
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false,
                    RequireSecurityAdminForMutations = true
                });
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);

            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant(s_singleX.ToArrayOf()),
                    new Variant(s_singleT.ToArrayOf()),
                    new Variant(s_singleEmpty.ToArrayOf()),
                    new Variant(ReferenceTypeIds.AliasFor)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>
            {
                new(System.Array.Empty<StatusCode>().ToArrayOf())
            };

            ServiceResult result = await category.AddAliasesToCategory!.CallAsync(
                manager.SystemContext,
                categoryId,
                input,
                argumentErrors,
                output,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        // --------------------------------------------------------------
        // Security matrix for HasSecureAdminAccess.
        // The auth boundary depends on (a) the channel's SecurityMode
        // and (b) whether the user identity carries the SecurityAdmin
        // role. The matrix below exercises every non-trivial combination
        // and asserts the user-facing service result on AddAliases
        // and DeleteAliases.
        // --------------------------------------------------------------

        private ServerSystemContext BuildSecuritySystemContext(
            MessageSecurityMode securityMode,
            bool grantSecurityAdmin)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var secureChannelContext = new SecureChannelContext(
                "test", endpoint, RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);

            var baseIdentity = new UserIdentity("admin", []);
            IUserIdentity identity = grantSecurityAdmin
                ? new RoleBasedIdentity(
                    baseIdentity,
                    [Role.SecurityAdmin],
                    m_mockServer.Object.NamespaceUris)
                : baseIdentity;

            // The identity must be carried by the OperationContext —
            // SessionSystemContext.UserIdentity prefers
            // OperationContext.UserIdentity when the OperationContext
            // implements ISessionOperationContext (which the server-side
            // OperationContext class does), so setting it on the
            // SystemContext directly is silently ignored.
            var opContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity);

            return new ServerSystemContext(m_mockServer.Object, opContext);
        }

        private async Task<ServiceResult> InvokeAddAliasesAsync(
            AliasNameNodeManager manager,
            AliasNameCategoryState category,
            NodeId categoryId,
            ISystemContext context)
        {
            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant(s_singleX.ToArrayOf()),
                    new Variant(s_singleT.ToArrayOf()),
                    new Variant(s_singleEmpty.ToArrayOf()),
                    new Variant(ReferenceTypeIds.AliasFor)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>
            {
                new(System.Array.Empty<StatusCode>().ToArrayOf())
            };
            return await category.AddAliasesToCategory!.CallAsync(
                context, categoryId, input, argumentErrors, output,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<ServiceResult> InvokeDeleteAliasesAsync(
            AliasNameNodeManager manager,
            AliasNameCategoryState category,
            NodeId categoryId,
            ISystemContext context)
        {
            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant(s_singleX.ToArrayOf()),
                    new Variant(s_singleT.ToArrayOf())
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>
            {
                new(System.Array.Empty<StatusCode>().ToArrayOf())
            };
            return await category.DeleteAliasesFromCategory!.CallAsync(
                context, categoryId, input, argumentErrors, output,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<(AliasNameNodeManager Manager,
                            AliasNameCategoryState Category,
                            NodeId CategoryId)>
            CreateAdminCheckedManagerAsync()
        {
            AliasNameNodeManager manager = CreateManager(
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false,
                    RequireSecurityAdminForMutations = true
                });
            await manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);
            return (manager, category, categoryId);
        }

        [Test]
        public async Task AddAliasesAdminOnSignAndEncryptIsAcceptedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.SignAndEncrypt, grantSecurityAdmin: true);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                // The auth predicate must pass; the call reaches the
                // store and returns Good at the service level.
                Assert.That(result, Is.Null.Or.EqualTo(ServiceResult.Good),
                    "Admin on SignAndEncrypt must satisfy HasSecureAdminAccess.");
            }
        }

        [Test]
        public async Task AddAliasesAdminOnSignOnlyIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.Sign, grantSecurityAdmin: true);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied),
                    "Admin role on Sign-only must NOT be sufficient — admin mutations require SignAndEncrypt.");
            }
        }

        [Test]
        public async Task AddAliasesAdminOnNoneSecurityIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.None, grantSecurityAdmin: true);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task AddAliasesNonAdminOnSignAndEncryptIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.SignAndEncrypt, grantSecurityAdmin: false);
                ServiceResult result = await InvokeAddAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied),
                    "A SignAndEncrypt channel alone is not enough; the user must also have SecurityAdmin.");
            }
        }

        [Test]
        public async Task DeleteAliasesAnonymousIsRejectedWithBadUserAccessDeniedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                // Default manager.SystemContext has no SessionSystemContext shape —
                // mirror the existing Add-side anonymous-rejection test.
                ServiceResult result = await InvokeDeleteAliasesAsync(
                    manager, category, categoryId, manager.SystemContext)
                    .ConfigureAwait(false);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task DeleteAliasesAdminOnSignAndEncryptIsAcceptedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.SignAndEncrypt, grantSecurityAdmin: true);
                ServiceResult result = await InvokeDeleteAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                // The auth predicate passes; the store returns Good
                // at the service level even though the per-row delete
                // would fail with BadNotFound (the test fixture's store
                // is empty) — the service result itself is Good.
                Assert.That(result, Is.Null.Or.EqualTo(ServiceResult.Good),
                    "Admin on SignAndEncrypt must satisfy the Delete-side auth predicate.");
            }
        }

        [Test]
        public async Task DeleteAliasesAdminOnSignOnlyIsRejectedAsync()
        {
            (AliasNameNodeManager manager, AliasNameCategoryState category, NodeId categoryId) =
                await CreateAdminCheckedManagerAsync().ConfigureAwait(false);
            using (manager)
            {
                ServerSystemContext ctx = BuildSecuritySystemContext(
                    MessageSecurityMode.Sign, grantSecurityAdmin: true);
                ServiceResult result = await InvokeDeleteAliasesAsync(
                    manager, category, categoryId, ctx).ConfigureAwait(false);

                Assert.That(result.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }
    }
}
