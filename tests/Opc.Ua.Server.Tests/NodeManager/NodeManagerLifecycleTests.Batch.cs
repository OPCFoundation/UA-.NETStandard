// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Test doubles implement or inspect the legacy IAsyncNodeManager.SyncNodeManager view.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Schema;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Publishes manager contributions on add and replacement and withdraws them after retirement.
        /// </summary>
        [Test]
        public async Task ConformanceContributorsFollowAddReplaceAndRetireWithoutRemovingHostProfilesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            ConformanceUnitsManager conformance =
                ((IConformanceUnitsManagerProvider)server).ConformanceUnitsManager!;
            var diagnostics = (DiagnosticsNodeManager)server.DiagnosticsNodeManager;
            BaseVariableState profiles = diagnostics.FindPredefinedNode<BaseVariableState>(
                VariableIds.Server_ServerCapabilities_ServerProfileArray);
            Assert.That(profiles.Value.TryGetValue(out ArrayOf<string> initialProfiles), Is.True);
            var host = new Mock<IConformanceContributor>();
            host.SetupGet(value => value.ConformanceUnits).Returns([new("Lifecycle Host Unit")]);
            host.SetupGet(value => value.ServerProfiles).Returns(["urn:test:conformance:host"]);
            Mock<IAsyncNodeManager> original = CreateLifecycleNodeManager(kModelNamespaceUri + ":Conformance");
            original.As<INodeManagerReloadParticipant>()
                .Setup(value => value.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask<ArrayOf<LocalReference>>));
            Mock<IConformanceContributor> originalContributor = original.As<IConformanceContributor>();
            originalContributor.SetupGet(value => value.ConformanceUnits).Returns([new("Lifecycle Original Unit")]);
            originalContributor.SetupGet(value => value.ServerProfiles).Returns(["urn:test:conformance:original"]);
            Mock<IAsyncNodeManager> replacement = CreateLifecycleNodeManager(kModelNamespaceUri + ":Conformance");
            replacement.As<IConformanceContributor>()
                .SetupGet(value => value.ConformanceUnits).Returns([new("Lifecycle Replacement Unit")]);
            replacement.As<IConformanceContributor>()
                .SetupGet(value => value.ServerProfiles).Returns(["urn:test:conformance:replacement"]);
            conformance.Register(host.Object);
            try
            {
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                LifecycleResult added = await lifecycle.ApplyAsync(
                    [LifecycleChange.Add(CreateConformanceFactory(original))], timeout.Token).ConfigureAwait(false);
                NodeManagerSlot slot = added.Registrations[0].Slot;
                Assert.That(added.Issues.Count, Is.Zero);
                Assert.That(conformance.IsSupported(new QualifiedName("Lifecycle Original Unit")), Is.True);
                conformance.Register(originalContributor.Object);
                Assert.That(conformance.Unregister(originalContributor.Object), Is.True);
                Assert.That(conformance.IsSupported(new QualifiedName("Lifecycle Original Unit")), Is.True);

                LifecycleResult replaced = await lifecycle.ApplyAsync(
                    [LifecycleChange.Replace(slot, CreateConformanceFactory(replacement))], timeout.Token)
                    .ConfigureAwait(false);
                await replaced.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(replaced.Issues.Count, Is.Zero);
                Assert.That(conformance.IsSupported(new QualifiedName("Lifecycle Original Unit")), Is.False);
                Assert.That(conformance.IsSupported(new QualifiedName("Lifecycle Replacement Unit")), Is.True);
                Assert.That(profiles.Value.TryGetValue(out ArrayOf<string> replacementProfiles), Is.True);
                Assert.That(replacementProfiles.ToArray(), Does.Contain("urn:test:conformance:replacement"));
                Assert.That(replacementProfiles.ToArray(), Does.Not.Contain("urn:test:conformance:original"));

                LifecycleResult removed = await lifecycle.ApplyAsync(
                    [LifecycleChange.Remove(slot)], timeout.Token).ConfigureAwait(false);
                await removed.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(removed.Issues.Count, Is.Zero);
                Assert.That(conformance.IsSupported(new QualifiedName("Lifecycle Replacement Unit")), Is.False);
                Assert.That(conformance.IsSupported(new QualifiedName("Lifecycle Host Unit")), Is.True);
                Assert.That(profiles.Value.TryGetValue(out ArrayOf<string> finalProfiles), Is.True);
                Assert.That(finalProfiles.ToArray(), Does.Contain("urn:test:conformance:host"));
                Assert.That(finalProfiles.ToArray(), Does.Not.Contain("urn:test:conformance:replacement"));
                foreach (string profile in initialProfiles)
                {
                    Assert.That(finalProfiles.ToArray(), Does.Contain(profile));
                }
                host.VerifyGet(value => value.ConformanceUnits, Times.AtLeastOnce);
            }
            finally
            {
                conformance.Unregister(host.Object);
                await conformance.PublishAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A failed catalog snapshot must prevent publication and allow a fresh attempt.
        /// </summary>
        [Test]
        public async Task FailedConformanceGetterPreventsAdditionUntilRetriedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var failure = new InvalidOperationException("Conformance snapshot unavailable.");
            int decisions = 0;
            const string modelNamespace = kModelNamespaceUri + ":ConformanceRetry";
            var unit = new QualifiedName("Recovered Unit");
            Mock<IAsyncNodeManager> failingManager = CreateLifecycleNodeManager(modelNamespace);
            failingManager.As<IConformanceContributor>().SetupGet(value => value.ConformanceUnits)
                .Throws(failure);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long originalRevision = lifecycle.Revision;
            ConformanceUnitsManager conformance =
                ((IConformanceUnitsManagerProvider)m_server.CurrentInstance).ConformanceUnitsManager!;
            await using (IPreparedChanges failed = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateConformanceFactory(failingManager))], timeout.Token).ConfigureAwait(false))
            {
                InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await failed.CommitAsync(
                        _ =>
                        {
                            decisions++;
                            return default;
                        },
                        cancellationToken: timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(thrown, Is.SameAs(failure));
                Assert.That(decisions, Is.Zero);
                Assert.That(lifecycle.Revision, Is.EqualTo(originalRevision));
                Assert.That(conformance.IsSupported(unit), Is.False);
            }

            Mock<IAsyncNodeManager> recovered = CreateLifecycleNodeManager(modelNamespace);
            recovered.As<IConformanceContributor>().SetupGet(value => value.ConformanceUnits).Returns([unit]);
            recovered.As<IConformanceContributor>().SetupGet(value => value.ServerProfiles)
                .Returns(["urn:test:conformance:recovered"]);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateConformanceFactory(recovered))], timeout.Token).ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(
                _ =>
                {
                    decisions++;
                    return default;
                },
                cancellationToken: timeout.Token).ConfigureAwait(false);
            Assert.That(result.Registrations.Count, Is.EqualTo(1));
            Assert.That(conformance.IsSupported(unit), Is.True);
            Assert.That(result.Issues.Count, Is.Zero);
            Assert.That(decisions, Is.EqualTo(1));
            Assert.That(result.Revision, Is.EqualTo(originalRevision + 1));
            Assert.That(lifecycle.Revision, Is.EqualTo(result.Revision));
        }

        [Test]
        public async Task RegistrationSlotTracksReplacementAndRemovalAsync()
        {
            TrackingLifecycleNodeManager? original = null;
            TrackingLifecycleNodeManager? replacement = null;
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => original = manager)), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult1.Registrations[0]);
            NodeManagerSlot slot = registration.Slot;
            Assert.That(slot.TryGetCurrent(out NodeManagerSlotSnapshot initial), Is.True);
            Assert.That(initial.NodeManager, Is.SameAs(original));
            Assert.That(initial.Generation, Is.EqualTo(registration.Generation));

            LifecycleResult publicationResult2 = await CommitChangeAsync(
                Capture(
                m_server.NodeManagerLifecycle, registration), LifecycleChange.Replace(
                registration.Slot, CreateTrackingNodeManagementFactory(
                kGeneration2Value, manager => replacement = manager)), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration next = RequireRegistration(publicationResult2.Registrations[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(next.Slot, Is.SameAs(slot));
                Assert.That(slot.Id, Is.EqualTo(registration.Id));
                Assert.That(slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.NodeManager, Is.SameAs(replacement));
                Assert.That(current.Generation, Is.EqualTo(next.Generation));
            }

            await CommitChangeAsync(
                Capture(
                m_server.NodeManagerLifecycle, next), LifecycleChange.Remove(
                next.Slot), CancellationToken.None).ConfigureAwait(false);
            Assert.That(slot.TryGetCurrent(out _), Is.False);
        }

        [Test]
        public async Task RegistrationSlotSnapshotsKeepManagerAndGenerationTogetherAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var knownGenerations = new ConcurrentDictionary<IAsyncNodeManager, long>();
            TrackingLifecycleNodeManager? initial = null;
            LifecycleResult publicationResult3 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager =>
                {
                    initial = manager;
                    knownGenerations[manager] = 1;
                })), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult3.Registrations[0]);
            NodeManagerSlot slot = registration.Slot;
            var inconsistent = new ConcurrentQueue<(long Generation, long Expected)>();

            Task reloads = Task.Run(async () =>
            {
                for (int index = 0; index < 6; index++)
                {
                    long expectedGeneration = registration.Generation + 1;
                    LifecycleResult publicationResult4 = await CommitChangeAsync(
                        Capture(
                        m_server.NodeManagerLifecycle, registration), LifecycleChange.Replace(
                        registration.Slot, CreateTrackingNodeManagementFactory(kGeneration2Value + index,
                            manager => knownGenerations[manager] = expectedGeneration)), timeout.Token)
                        .ConfigureAwait(false);
                    registration = RequireRegistration(publicationResult4.Registrations[0]);
                }
            }, timeout.Token);

            do
            {
                if (slot.TryGetCurrent(out NodeManagerSlotSnapshot snapshot) &&
                    knownGenerations.TryGetValue(snapshot.NodeManager, out long expectedGeneration) &&
                    snapshot.Generation != expectedGeneration)
                {
                    inconsistent.Enqueue((snapshot.Generation, expectedGeneration));
                }
                await Task.Yield();
            }
            while (!reloads.IsCompleted);

            await reloads.ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(initial, Is.Not.Null);
                Assert.That(inconsistent, Is.Empty);
                Assert.That(slot.TryGetCurrent(out NodeManagerSlotSnapshot final), Is.True);
                Assert.That(final.Generation, Is.EqualTo(7));
                Assert.That(knownGenerations.TryGetValue(final.NodeManager, out long expected), Is.True);
                Assert.That(final.Generation, Is.EqualTo(expected));
            }
            await CommitChangeAsync(
                Capture(
                m_server.NodeManagerLifecycle, registration), LifecycleChange.Remove(registration.Slot), timeout.Token)
                .ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchRevealsSelfRegisteredNamespaceOnlyOnCommitAsync(
            bool publish, bool registerDuringFactory)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IAsyncNodeManager? candidate = null;
            RuntimeNodeSetOptions options = CreateOptions(kModelNamespaceUri, kFirstRegistrationValue);
            Action<INodeManagerBuilder>? configure = options.Configure;
            options.Configure = builder =>
            {
                configure?.Invoke(builder);
                if (!registerDuringFactory)
                {
                    m_server.CurrentInstance.NodeManager.RegisterNamespaceManager(
                        kModelNamespaceUri, RequireLifecycleValue(candidate));
                }
            };
            var runtimeFactory = new RuntimeNodeSetNodeManagerFactory(options);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateCandidateAsync(server, configuration, token));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            NodeId valueId;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false))
            {
                valueId = new NodeId(kValueNodeId,
                    (ushort)ChangePreparer.GetServer(RequireLifecycleValue(candidate))!
                        .NamespaceUris.GetIndex(kModelNamespaceUri));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
                ReadResponse hidden = await ReadAsync(valueId).ConfigureAwait(false);
                Assert.That(hidden.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            ReadResponse final = await ReadAsync(valueId).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(final.Results[0].StatusCode,
                    Is.EqualTo(publish ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(publish ? 1 : 0));
                if (publish)
                {
                    Assert.That(final.Results[0].WrappedValue, Is.EqualTo(new Variant(kFirstRegistrationValue)));
                }
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);

            async ValueTask<IAsyncNodeManager> CreateCandidateAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                candidate = await runtimeFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
                if (registerDuringFactory)
                {
                    server.NodeManager.RegisterNamespaceManager(kModelNamespaceUri, candidate);
                }
                return candidate;
            }

            async Task<ReadResponse> ReadAsync(NodeId id)
            {
                return await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = id, AttributeId = Attributes.Value }], timeout.Token)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchPublishesReferencesToSurvivingOwnersAsync(bool publish)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            RequireRegistration(
                (await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(
                kSecondModelNamespaceUri, kSecondRegistrationValue))), timeout.Token).ConfigureAwait(
                false)).Registrations[0]);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kModelNamespaceUri);
            NodeId root = new(kRootNodeId, ns);
            NodeId ordinaryRoot = new(kRootNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
            Assert.That(await ObjectsContainsRootAsync(ordinaryRoot).ConfigureAwait(false), Is.True,
                "The same native Browse predicate must find an ordinarily published root.");
            bool whilePrepared;
            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false))
            {
                whilePrepared = await ObjectsContainsRootAsync(root).ConfigureAwait(false);
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            bool afterDecision = await ObjectsContainsRootAsync(root).ConfigureAwait(false);
            ReadResponse direct = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = root, AttributeId = Attributes.BrowseName }], timeout.Token)
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(direct.Results[0].StatusCode,
                    Is.EqualTo(publish ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                if (publish)
                {
                    Assert.That(direct.Results[0].WrappedValue,
                        Is.EqualTo(new Variant(new QualifiedName(kRootBrowseName, ns))),
                        "The committed candidate is routable even if its surviving owner's reference is missing.");
                }
                Assert.That(whilePrepared, Is.False, "Preparation must not mutate a serving owner's references.");
                Assert.That(afterDecision, Is.EqualTo(publish),
                    "The surviving Objects owner must expose the newly committed manager's root.");
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);

            async Task<bool> ObjectsContainsRootAsync(NodeId target)
            {
                BrowseResponse response = await session.BrowseAsync(null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = ObjectIds.ObjectsFolder,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], timeout.Token).ConfigureAwait(false);
                BrowseResult page = response.Results[0];
                bool found = false;
                while (true)
                {
                    Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                    for (int index = 0; index < page.References.Count; index++)
                    {
                        found |= page.References[index].NodeId == new ExpandedNodeId(target);
                    }
                    if (page.ContinuationPoint.Length == 0)
                    {
                        return found;
                    }
                    BrowseNextResponse next = await session.BrowseNextAsync(
                        null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                    page = next.Results[0];
                }
            }
        }

        /// <summary>
        /// Verifies that concurrent namespace additions invalidate preparation before the durable decision.
        /// </summary>
        [Test]
        public async Task PreparedBatchRejectsChangedNamespacePrefixBeforeDecisionAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))]).ConfigureAwait(false);
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            Assert.That(namespaces.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            ushort concurrent = namespaces.GetIndexOrAppend("urn:opcfoundation.org:Tests:ConcurrentNamespace");
            int decisions = 0;
            await Assert.ThrowsAsync<OptimisticPublicationConflictException>(async () =>
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(namespaces.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            Assert.That(namespaces.GetString(concurrent),
                Is.EqualTo("urn:opcfoundation.org:Tests:ConcurrentNamespace"));
            Assert.That(GetBranchRegistrations(lifecycle).IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies that cached manager contexts and builders follow canonical additive metadata across later commits.
        /// </summary>
        [Test]
        public async Task PublishedManagerCachesObserveLaterMetadataAndDetachBeforeRetirementAsync()
        {
            TrackingLifecycleNodeManager? firstManager = null;
            LifecycleResult publicationResult5 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kFirstRegistrationValue, manager => firstManager = manager)), CancellationToken.None)
                .ConfigureAwait(false);
            NodeManagerRegistration first = RequireRegistration(publicationResult5.Registrations[0]);
            TrackingLifecycleNodeManager firstOwner = RequireLifecycleValue(firstManager);
            var cachedTypes = (TypeTable)firstOwner.SystemContext.TypeTable;
            NamespaceTable cachedNamespaces = firstOwner.SystemContext.NamespaceUris;
            IEncodeableFactory cachedFactory = firstOwner.SystemContext.EncodeableFactory;
            IEncodeableFactoryBuilder cachedBuilder = cachedFactory.Builder;
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            NodeId added = new(8410, ns);
            ExpandedNodeId alias = new(8411, kSecondModelNamespaceUri);
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? activator), Is.True);
            IEncodeableType structure = RequireLifecycleValue(activator);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kSecondRegistrationValue, manager =>
                {
                    manager.Server.TypeTree.AddSubtype(added, Ua.ObjectTypeIds.BaseObjectType);
                    manager.Server.Factory.Builder.AddEncodeableType(alias, structure).Commit();
                }, kSecondModelNamespaceUri))]).ConfigureAwait(false);
            Assert.That(cachedTypes.IsKnown(added), Is.False);
            Assert.That(cachedFactory.TryGetEncodeableType(alias, out _), Is.False);
            await prepared.CommitAsync(_ => default).ConfigureAwait(false);
            Assert.That(cachedTypes.FindSuperType(added), Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
            Assert.That(cachedNamespaces.GetString(ns), Is.EqualTo(kSecondModelNamespaceUri));
            Assert.That(cachedFactory.TryGetEncodeableType(alias, out IEncodeableType? retained), Is.True);
            Assert.That(retained, Is.SameAs(structure));
            ExpandedNodeId cachedAlias = new(8412, kSecondModelNamespaceUri);
            cachedBuilder.AddEncodeableType(cachedAlias, structure).Commit();
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(cachedAlias, out _), Is.True);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, first).ConfigureAwait(false);
            cachedTypes.Remove(added);
            cachedNamespaces.Append("urn:opcfoundation.org:Tests:DetachedCleanup");
            Assert.That(m_server.CurrentInstance.TypeTree.IsKnown(added), Is.True);
            Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(
                "urn:opcfoundation.org:Tests:DetachedCleanup"), Is.EqualTo(-1));
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.True);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);
        }

        [Test]
        public async Task PreparedBatchRejectsChangedTypeInputsBeforeDecisionAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))]).ConfigureAwait(false);
            NodeId unrelated = new(8301,
                m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kModelNamespaceUri));
            m_server.CurrentInstance.TypeTree.AddSubtype(unrelated, Ua.ObjectTypeIds.BaseObjectType);
            int decisions = 0;
            await Assert.ThrowsAsync<OptimisticPublicationConflictException>(async () =>
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(GetBranchRegistrations(lifecycle).IsEmpty, Is.True);
            Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(unrelated),
                Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
        }

        [Test]
        public async Task PreparedBatchDoesNotLoseTypeWritesDuringDecisionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeId unrelated = new(8302, 0);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            bool accepted = false;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    m_server.CurrentInstance.TypeTree.AddSubtype(unrelated, Ua.ObjectTypeIds.BaseObjectType);
                    accepted = true;
                }
                catch (InvalidOperationException)
                {
                    Assert.That(m_server.CurrentInstance.TypeTree.IsKnown(unrelated), Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            LifecycleResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(unrelated),
                Is.EqualTo(accepted ? Ua.ObjectTypeIds.BaseObjectType : NodeId.Null),
                "A concurrent type mutation must either be retained or fail before it changes the serving image.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchTypesRemainPrivateUntilPublicationAsync(bool replace, bool publish)
        {
            const string modelUri = kModelNamespaceUri + ":BatchTypes";
            const uint typeIdentifier = 8100;
            ushort namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(modelUri);
            NodeId typeId = new(typeIdentifier, namespaceIndex);
            NodeId addedTypeId = new(typeIdentifier + 1, namespaceIndex);
            NodeManagerRegistration? previous = null;
            if (replace)
            {
                LifecycleResult publicationResult6 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(
                    new RuntimeNodeSetNodeManagerFactory(
                    Options(ObjectTypeIds.BaseObjectType))), CancellationToken.None).ConfigureAwait(false);
                previous = RequireRegistration(publicationResult6.Registrations[0]);
            }
            NodeId originalParent = replace ? Ua.ObjectTypeIds.BaseObjectType : NodeId.Null;
            Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId), Is.EqualTo(originalParent));
            NodeId proposedParent = replace ? originalParent : Ua.ObjectTypeIds.FolderType;
            var factory = new RuntimeNodeSetNodeManagerFactory(Options(proposedParent, includeChild: true));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeId whilePrepared;
            ILifecycleCapture capture = replace
                ? Capture(lifecycle, RequireLifecycleValue(previous))
                : lifecycle.CaptureChanges();
            await using ILifecycleWriter exactWriter1 = await capture.BeginAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using (IPreparedChanges prepared = await exactWriter1.PrepareAsync([replace
                    ? LifecycleChange.Replace(RequireLifecycleValue(previous).Slot, factory)
                    : LifecycleChange.Add(factory)])
                .ConfigureAwait(false))
            {
                whilePrepared = m_server.CurrentInstance.TypeTree.FindSuperType(typeId);
                Assert.That(m_server.CurrentInstance.TypeTree.IsKnown(addedTypeId), Is.False);
                if (publish)
                {
                    LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    await exactWriter1.DisposeAsync().ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(whilePrepared, Is.EqualTo(originalParent),
                    "Private preparation must not add or replace a serving type relationship.");
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId),
                    Is.EqualTo(publish ? proposedParent : originalParent),
                    "Publication preserves serving definitions and adds compatible declarations.");
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(addedTypeId),
                    Is.EqualTo(publish ? typeId : NodeId.Null));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(replace || publish ? 1 : 0));
                if (replace && !publish)
                {
                    Assert.That(GetBranchRegistration(lifecycle, RequireLifecycleValue(previous).Id),
                        Is.SameAs(previous));
                }
            }

            RuntimeNodeSetOptions Options(NodeId parent, bool includeChild = false)
            {
                string child = includeChild ? $$"""
                      <UAObjectType NodeId="ns=1;i={{typeIdentifier + 1}}" BrowseName="1:BatchChildType">
                        <DisplayName>BatchChildType</DisplayName>
                        <References>
                          <Reference ReferenceType="i=45" IsForward="false">ns=1;i={{typeIdentifier}}</Reference>
                        </References>
                      </UAObjectType>
                    """ : string.Empty;
                string xml = $$"""
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                      <NamespaceUris><Uri>{{modelUri}}</Uri></NamespaceUris>
                      <Models><Model ModelUri="{{modelUri}}" Version="1.0.0" /></Models>
                      <UAObjectType NodeId="ns=1;i={{typeIdentifier}}" BrowseName="1:BatchType">
                        <DisplayName>BatchType</DisplayName>
                        <References>
                          <Reference ReferenceType="i=45" IsForward="false">{{parent}}</Reference>
                        </References>
                      </UAObjectType>
                      {{child}}
                    </UANodeSet>
                    """;
                return new RuntimeNodeSetOptions
                {
                    Sources =
                    [
                        RuntimeNodeSetSource.FromStream("batch-types",
                            _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))), [modelUri])
                    ]
                };
            }
        }

        /// <summary>
        /// Verifies that incompatible metadata aborts before the durable decision and preserves the live owner.
        /// </summary>
        /// <param name="changeFactory">Whether to change an activator rather than a type relationship.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchRejectsIncompatibleMetadataBeforeDecisionAsync(bool changeFactory)
        {
            LifecycleResult publicationResult7 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kFirstRegistrationValue, _ => { })), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration previous = RequireRegistration(publicationResult7.Registrations[0]);
            ushort namespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId typeId = new(8400, namespaceIndex);
            m_server.CurrentInstance.TypeTree.AddSubtype(typeId, Ua.ObjectTypeIds.BaseObjectType);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? serving), Is.True);
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.EUInformation, out IEncodeableType? proposed),
                Is.True);
            AssertLifecycleValue(proposed);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var replacement = LifecycleChange.Replace(
                previous.Slot, CreateTrackingNodeManagementFactory(303, manager =>
                {
                    if (changeFactory)
                    {
                        manager.Server.Factory.Builder.AddEncodeableType(DataTypeIds.Range, proposed).Commit();
                    }
                    else
                    {
                        manager.Server.TypeTree.AddSubtype(typeId, Ua.ObjectTypeIds.FolderType);
                    }
                }));
            int decisions = 0;
            if (changeFactory)
            {
                await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                    [replacement]).ConfigureAwait(false);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    }).ConfigureAwait(false);
                }).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.False);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                        [replacement]).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(GetBranchRegistration(lifecycle, previous.Id), Is.SameAs(previous));
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId),
                    Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
                Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? retained), Is.True);
                Assert.That(retained, Is.SameAs(serving));
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchNativeReadRetainsOneGenerationAcrossDecisionAsync(bool rejectDecision)
        {
            Task exactWriter2Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                TrackingLifecycleNodeManager? firstManager = null;
                TrackingLifecycleNodeManager? secondManager = null;
                LifecycleResult publicationResult8 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(
                    CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, manager => firstManager = manager)), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration first = RequireRegistration(publicationResult8.Registrations[0]);
                LifecycleResult publicationResult9 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(
                    CreateTrackingNodeManagementFactory(kSecondRegistrationValue, manager => secondManager = manager,
                        kSecondModelNamespaceUri)), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration second = RequireRegistration(publicationResult9.Registrations[0]);
                AssertLifecycleValue(firstManager);
                AssertLifecycleValue(secondManager);
                NodeId typeId = new(8303,
                    (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
                NodeId addedTypeId = new(8304, typeId.NamespaceIndex);
                m_server.CurrentInstance.TypeTree.AddSubtype(typeId, ObjectTypeIds.BaseObjectType);
                IEncodeableFactory factory = m_server.CurrentInstance.Factory;
                Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? structure), Is.True);
                AssertLifecycleValue(structure);
                ExpandedNodeId alias = new(8307, "urn:opcfoundation.org:Tests:RetainedFactory");
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                await using ILifecycleWriter exactWriter2 = await Capture(
                    m_server.NodeManagerLifecycle, first, second).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter2.PrepareAsync([
                        LifecycleChange.Replace(first.Slot, CreateTrackingNodeManagementFactory(303, manager =>
                            {
                                manager.Server.TypeTree.AddSubtype(addedTypeId, typeId);
                                manager.Server.Factory.Builder.AddEncodeableType(alias, structure).Commit();
                            })),
                        LifecycleChange.Replace(
                            second.Slot, CreateTrackingNodeManagementFactory(404, _ => { }, kSecondModelNamespaceUri))
                    ], timeout.Token).ConfigureAwait(false);
                await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
                await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                using Client.ISession session = await client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                    .ConfigureAwait(false);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var decided = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                NodeId firstId = new(kValueNodeId,
                    (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
                NodeId secondId = new(kValueNodeId,
                    (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
                NodeId beforePublication = NodeId.Null;
                NodeId afterPublication = NodeId.Null;
                bool factoryBeforePublication = false;
                firstManager.ReadCallbackNodeId = firstId;
                firstManager.ReadCallback = async (context, token) =>
                {
                    beforePublication = m_server.CurrentInstance.TypeTree.FindSuperType(typeId);
                    factoryBeforePublication = factory.TryGetEncodeableType(alias, out _);
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    afterPublication = m_server.CurrentInstance.TypeTree.FindSuperType(typeId);
                };
                Task<ReadResponse> pendingRead = ReadPairAsync();
                Task<LifecycleResult>? pendingCommit = null;
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    pendingCommit = prepared.CommitAsync(
                        _ =>
                        {
                            decided.TrySetResult(true);
                            if (rejectDecision)
                            {
                                throw new IOException("Confirmed noncommit while a native read owns the old image.");
                            }
                            return default;
                        },
                        () => published.TrySetResult(true),
                        timeout.Token).AsTask();
                    exactWriter2Disposal = exactWriter2.DisposeAsync().AsTask();
                    await decided.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    if (!rejectDecision)
                    {
                        await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(pendingRead.IsCompleted, Is.False);
                        Assert.That(firstManager.DeleteAddressSpaceCount, Is.Zero);
                        Assert.That(secondManager.DeleteAddressSpaceCount, Is.Zero);
                        Assert.That(firstManager.DisposeCount, Is.Zero);
                        Assert.That(secondManager.DisposeCount, Is.Zero);
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                }
                ReadResponse oldRead = await pendingRead.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(pendingCommit, Is.Not.Null);
                AssertLifecycleValue(pendingCommit);
                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => pendingCommit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
                else
                {
                    LifecycleResult committed = await pendingCommit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(committed.CleanupFailure, Is.Null);
                    await committed.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                ReadResponse current = await ReadPairAsync().ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(oldRead.Results.Count, Is.EqualTo(2));
                    Assert.That(oldRead.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(oldRead.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(oldRead.Results[0].WrappedValue, Is.EqualTo(new Variant(kFirstRegistrationValue)));
                    Assert.That(oldRead.Results[1].WrappedValue, Is.EqualTo(new Variant(kSecondRegistrationValue)));
                    Assert.That(current.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(current.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(current.Results[0].WrappedValue,
                        Is.EqualTo(new Variant(rejectDecision ? kFirstRegistrationValue : 303)));
                    Assert.That(current.Results[1].WrappedValue,
                        Is.EqualTo(new Variant(rejectDecision ? kSecondRegistrationValue : 404)));
                    Assert.That(prepared.IsCommitted, Is.EqualTo(!rejectDecision));
                    Assert.That(firstManager.DisposeCount, Is.EqualTo(rejectDecision ? 0 : 1));
                    Assert.That(secondManager.DisposeCount, Is.EqualTo(rejectDecision ? 0 : 1));
                    Assert.That(beforePublication, Is.EqualTo(ObjectTypeIds.BaseObjectType));
                    Assert.That(afterPublication, Is.EqualTo(ObjectTypeIds.BaseObjectType),
                        "An additive publication cannot change a serving type's definition.");
                    Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId),
                        Is.EqualTo(ObjectTypeIds.BaseObjectType));
                    Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(addedTypeId),
                        Is.EqualTo(rejectDecision ? NodeId.Null : typeId));
                    Assert.That(factoryBeforePublication, Is.False);
                    Assert.That(factory.TryGetEncodeableType(alias, out _), Is.EqualTo(!rejectDecision));
                }
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);

                async Task<ReadResponse> ReadPairAsync()
                {
                    return await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        [
                            new ReadValueId { NodeId = firstId, AttributeId = Attributes.Value },
                            new ReadValueId { NodeId = secondId, AttributeId = Attributes.Value }
                        ], timeout.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                await exactWriter2Disposal.ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchOwnsItsChangesAcrossAwaitAsync(bool mutateCallerArray)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            LifecycleResult publicationResult10 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kReadinessProbeNamespaceUri, 707))), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration unrelated = RequireRegistration(publicationResult10.Registrations[0]);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var delayedFactory = new Mock<IAsyncNodeManagerFactory>();
            delayedFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateDelayedAsync(server, configuration, token));
            LifecycleChange[] changes =
            [
                LifecycleChange.Add(delayedFactory.Object),
                LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
            ];
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            Task<IPreparedChanges> pending = lifecycle.PrepareAsync(changes, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (mutateCallerArray)
                {
                    changes[1] = LifecycleChange.Remove(unrelated.Slot);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            await using IPreparedChanges prepared = await pending.WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            DataValue second = await ReadValueAsync(new NodeId(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)))
                .ConfigureAwait(false);
            DataValue original = await ReadValueAsync(new NodeId(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kReadinessProbeNamespaceUri)))
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Registrations.Count, Is.EqualTo(2));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(3));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(unrelated));
                Assert.That(result.RetiredCount, Is.Zero);
                Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(second.WrappedValue, Is.EqualTo(new Variant(kSecondRegistrationValue)));
                Assert.That(original.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(original.WrappedValue, Is.EqualTo(new Variant(707)));
            }

            async ValueTask<IAsyncNodeManager> CreateDelayedAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return await originalFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchPostdecisionFailuresStillReconcileAsync(
            bool failPublication, bool failReadiness)
        {
            TrackingLifecycleNodeManager? originalManager = null;
            LifecycleResult publicationResult11 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => originalManager = manager)), CancellationToken.None)
                .ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult11.Registrations[0]);
            var publicationFailure = new IOException("Post-decision publication callback failed.");
            var readinessFailure = new InvalidOperationException("Committed readiness failed.");
            ReadinessLifecycleNodeManager? replacement = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    replacement = new ReadinessLifecycleNodeManager(server, configuration, m_logger, kGeneration2Value)
                    {
                        ReadinessCallback = token =>
                        {
                            Assert.That(token.CanBeCanceled, Is.False);
                            if (failReadiness)
                            {
                                throw readinessFailure;
                            }
                            return default;
                        }
                    };
                    return new ValueTask<IAsyncNodeManager>(replacement);
                });
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter3 = await Capture(
                m_server.NodeManagerLifecycle, original).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using IPreparedChanges prepared = await exactWriter3.PrepareAsync(
                [LifecycleChange.Replace(original.Slot, factory.Object)]).ConfigureAwait(false);
            AssertLifecycleValue(originalManager);
            AssertLifecycleValue(replacement);
            int decisions = 0;
            int publications = 0;
            LifecycleResult? result = null;
            IOException? escaped = null;
            try
            {
                result = await prepared.CommitAsync(
                    _ =>
                    {
                        decisions++;
                        return default;
                    },
                    () =>
                    {
                        publications++;
                        Assert.That(prepared.IsCommitted, Is.True);
                        Assert.That(GetBranchRegistration(lifecycle, original.Id),
                            Is.SameAs(RequireRegistration(prepared.Registrations[0])));
                        if (failPublication)
                        {
                            throw publicationFailure;
                        }
                    }).ConfigureAwait(false);
                await exactWriter3.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException failure)
            {
                escaped = failure;
            }
            Assert.That(result, Is.Not.Null);
            await result!.Retired.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(escaped, Is.Null, "A post-decision callback failure must remain a committed outcome.");
                Assert.That(result, Is.Not.Null);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(publications, Is.EqualTo(1));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(1));
                Assert.That(GetBranchRegistration(lifecycle, original.Id).Generation,
                    Is.EqualTo(original.Generation + 1));
                Assert.That(replacement.ReadinessCount, Is.EqualTo(1));
                Assert.That(replacement.ReadinessCompletedCount, Is.EqualTo(failReadiness ? 0 : 1));
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                if (result is not null)
                {
                    Assert.That(result.Retired.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    if (failPublication || failReadiness)
                    {
                        Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
                        var failures = RequireLifecycleValue(result.CleanupFailure as AggregateException)
                            .Flatten().InnerExceptions;
                        Assert.That(failures, Has.Count.EqualTo((failPublication ? 1 : 0) + (failReadiness ? 1 : 0)));
                        if (failPublication)
                        {
                            Assert.That(failures, Does.Contain(publicationFailure));
                        }
                        if (failReadiness)
                        {
                            Assert.That(failures, Does.Contain(readinessFailure));
                        }
                    }
                    else
                    {
                        Assert.That(result.CleanupFailure, Is.Null);
                    }
                }
            }
            if (failReadiness)
            {
                await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            }
            else
            {
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PreparedBatchDecisionFailureKeepsCurrentGenerationAndAbortsCandidateAsync()
        {
            TrackingLifecycleNodeManager? originalManager = null;
            TrackingLifecycleNodeManager? candidateManager = null;
            LifecycleResult publicationResult12 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => originalManager = manager)), CancellationToken.None)
                .ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult12.Registrations[0]);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter4 = await Capture(
                m_server.NodeManagerLifecycle, original).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using IPreparedChanges prepared = await exactWriter4.PrepareAsync(
                [LifecycleChange.Replace(
                original.Slot, CreateTrackingNodeManagementFactory(
                kGeneration2Value, manager => candidateManager = manager))]).ConfigureAwait(false);
            var decisionFailure = new IOException("Durable decision was rejected.");
            int decisions = 0;
            Exception? escaped = null;

            try
            {
                _ = await prepared.CommitAsync(
                    _ =>
                    {
                        decisions++;
                        return new ValueTask(Task.FromException(decisionFailure));
                    }).ConfigureAwait(false);
                await exactWriter4.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException failure)
            {
                escaped = failure;
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(escaped, Is.SameAs(decisionFailure));
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(GetBranchRegistration(lifecycle, original.Id), Is.SameAs(original));
                Assert.That(originalManager, Is.Not.Null);
                Assert.That(candidateManager, Is.Not.Null);
                Assert.That(candidateManager!.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(candidateManager.DisposeCount, Is.EqualTo(1));
            }

            await prepared.DisposeAsync().ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(GetBranchRegistration(lifecycle, original.Id), Is.SameAs(original));
                Assert.That(candidateManager!.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(candidateManager.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchRejectsChangedRoutingInputsOverNativeTcp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(false, true, telemetry);
            await client.LoadClientConfigurationAsync(m_pkiRoot);
            Assert.That(client.SessionFactory, Is.TypeOf<DefaultSessionFactory>());
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);
            try
            {
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                await using IPreparedChanges prepared = await lifecycle.PrepareAsync([
                        LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                            CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                    ]).ConfigureAwait(false);
                LifecycleResult publicationResult13 = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(
                    new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(
                    kSecondModelNamespaceUri, kSecondRegistrationValue))), CancellationToken.None)
                    .ConfigureAwait(false);
                NodeManagerRegistration intervening = RequireRegistration(publicationResult13.Registrations[0]);
                int decisions = 0;

                await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(async () =>
                    await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    })).ConfigureAwait(false);

                Assert.That(decisions, Is.Zero);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(GetBranchRegistrations().Count, Is.EqualTo(1));
                Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(intervening));
                ArrayOf<ReadValueId> nodes =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(kValueNodeId,
                            (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)),
                        AttributeId = Attributes.Value
                    },
                    new ReadValueId
                    {
                        NodeId = new NodeId(kValueNodeId,
                            (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)),
                        AttributeId = Attributes.Value
                    }
                ];
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, nodes, CancellationToken.None);
                Assert.That(response.Results.Count, Is.EqualTo(2));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[1].WrappedValue.TryGetValue(out int number), Is.True);
                Assert.That(number, Is.EqualTo(202));
            }
            finally
            {
                await session.CloseAsync();
            }
        }

        [Test]
        public async Task PreparedBatchRejectsChangedRoutingInputsBeforeDecision()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ]).ConfigureAwait(false);
            LifecycleResult publicationResult14 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(
                kSecondModelNamespaceUri, kSecondRegistrationValue))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration intervening = RequireRegistration(publicationResult14.Registrations[0]);
            int decisions = 0;

            await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(async () =>
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                })).ConfigureAwait(false);

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(intervening));
            DataValue unpublished = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue retained = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(unpublished.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(retained.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retained.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(202));
        }

        [Test]
        public async Task PreparedBatchDecisionFailureLeavesBothExistingGenerationsActive()
        {
            LifecycleResult publicationResult15 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(
                kModelNamespaceUri, kFirstRegistrationValue))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration first = RequireRegistration(publicationResult15.Registrations[0]);
            LifecycleResult publicationResult16 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(
                kSecondModelNamespaceUri, kSecondRegistrationValue))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration second = RequireRegistration(publicationResult16.Registrations[0]);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter5 = await Capture(
                m_server.NodeManagerLifecycle, first, second).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using IPreparedChanges prepared = await exactWriter5.PrepareAsync([
                    LifecycleChange.Replace(first.Slot, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 303))),
                    LifecycleChange.Replace(second.Slot, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, 404)))
                ]).ConfigureAwait(false);

            await Assert.ThrowsAsync<IOException>(async () =>
            {
                await prepared.CommitAsync(_ => throw new IOException("Confirmed decision noncommit."));
                await exactWriter5.DisposeAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(first));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(second));
            DataValue firstValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue secondValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(firstValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(secondValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(firstValue.WrappedValue.TryGetValue(out int firstNumber), Is.True);
            Assert.That(secondValue.WrappedValue.TryGetValue(out int secondNumber), Is.True);
            Assert.That(firstNumber, Is.EqualTo(101));
            Assert.That(secondNumber, Is.EqualTo(202));

            await prepared.DisposeAsync().ConfigureAwait(false);
            await using ILifecycleWriter exactWriter6 = await Capture(
                m_server.NodeManagerLifecycle, first).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using IPreparedChanges followup = await exactWriter6.PrepareAsync([
                    LifecycleChange.Replace(first.Slot, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 606)))
                ]).ConfigureAwait(false);
            await followup.CommitAsync(_ => default).ConfigureAwait(false);
            await exactWriter6.DisposeAsync().ConfigureAwait(false);
            Assert.That(followup.IsCommitted, Is.True);
            DataValue followupValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(followupValue.WrappedValue.TryGetValue(out int followupNumber), Is.True);
            Assert.That(followupNumber, Is.EqualTo(606));
        }

        [Test]
        public async Task PreparedBatchDecisionCallbackRejectsNestedLifecycleOperationsAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ]).ConfigureAwait(false);
            await using IPreparedChanges nestedCommit = await lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ]).ConfigureAwait(false);

            await prepared.CommitWithDecisionAsync(async (decision, token) =>
            {
                await Task.Run(async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    {
                        await ((INodeManagerCallbackLifecycle)lifecycle).PrepareFromDecisionAsync([
                                LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                            ], decision, timeout.Token).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    {
                        await nestedCommit.CommitInMemoryAsync(decision, timeout.Token).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    {
                        await nestedCommit.DisposeAsync(decision).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(1));
            await nestedCommit.DisposeAsync().ConfigureAwait(false);

            await using IPreparedChanges followup = await lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ]).ConfigureAwait(false);
            await followup.CommitAsync(_ => default).ConfigureAwait(false);
            Assert.That(followup.IsCommitted, Is.True);
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(2));
        }

        [Test]
        public async Task PreparedBatchRejectsStaleRegistrationBeforeDecision()
        {
            LifecycleResult publicationResult17 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(
                kModelNamespaceUri, kFirstRegistrationValue))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration first = RequireRegistration(publicationResult17.Registrations[0]);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter exactWriter7 = await Capture(
                m_server.NodeManagerLifecycle, first).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using IPreparedChanges prepared = await exactWriter7.PrepareAsync([
                    LifecycleChange.Replace(first.Slot, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 303)))
                ]).ConfigureAwait(false);
            LifecycleResult publicationResult18 = await CommitChangeAsync(
                Capture(
                m_server.NodeManagerLifecycle, first), LifecycleChange.Replace(
                first.Slot, new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, 505))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration winner = Registration(publicationResult18.Registrations[0]);
            int decisions = 0;

            await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                });
                await exactWriter7.DisposeAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(winner));
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(505));
        }

        /// <summary>
        /// Equal registration metadata cannot authorize a different prepared generation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BatchRejectsDifferentGenerationWithMatchingRegistrationMetadataAsync(bool replace)
        {
            LifecycleResult publicationResult19 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(
                kModelNamespaceUri, kFirstRegistrationValue))), CancellationToken.None).ConfigureAwait(false);
            NodeManagerRegistration current = RequireRegistration(publicationResult19.Registrations[0]);
            var otherGeneration = new NodeManagerGeneration(
                current.Slot,
                current.Generation,
                current.NodeManager,
                NodeManagerGenerationState.Prepared);
            var stale = new NodeManagerRegistration(otherGeneration, current.NamespaceUris);
            Assert.That(stale.Id, Is.EqualTo(current.Id));
            Assert.That(stale.Generation, Is.EqualTo(current.Generation));
            Assert.That(stale.NodeManager, Is.SameAs(current.NodeManager));
            Assert.That(stale.Identity, Is.Not.SameAs(current.Identity));

            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleChange change = replace
                ? LifecycleChange.Replace(stale.Slot, new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kSecondRegistrationValue)))
                : LifecycleChange.Remove(stale.Slot);
            IPreparedChanges? unexpected = null;
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await using ILifecycleWriter writer = await Capture(lifecycle, stale)
                        .BeginAsync().ConfigureAwait(false);
                    unexpected = await writer.PrepareAsync([change]).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            finally
            {
                if (unexpected is not null)
                {
                    await unexpected.DisposeAsync().ConfigureAwait(false);
                }
            }
            Assert.That(current.Slot.TryGetCurrent(out NodeManagerSlotSnapshot published), Is.True);
            Assert.That(published.Generation, Is.EqualTo(current.Generation));
            Assert.That(published.NodeManager, Is.SameAs(current.NodeManager));

            var copied = new NodeManagerRegistration(current.Identity, current.NamespaceUris);
            Assert.That(copied, Is.Not.SameAs(current));
            await using ILifecycleWriter exactWriter8 = await Capture(
                m_server.NodeManagerLifecycle, copied).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using IPreparedChanges valid = await exactWriter8.PrepareAsync(
                [LifecycleChange.Remove(copied.Slot)]).ConfigureAwait(false);
            Assert.That(valid.IsCommitted, Is.False);
        }

        [Test]
        public async Task PreparedBatchPredecisionCancellationLeavesNoPublishedOwner()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ]).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int decisions = 0;

            await Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, cancellation.Token);
            }).ConfigureAwait(false);

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(GetBranchRegistrations(), Is.Empty);
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task PreparedBatchPostdecisionCancellationKeepsCommittedOwner()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ]).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            LifecycleResult result = await prepared.CommitAsync(_ =>
            {
                cancellation.Cancel();
                return default;
            }, cancellation.Token).ConfigureAwait(false);

            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(result.Registrations.Count, Is.EqualTo(1));
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.ToList(),
                Does.Contain(RequireRegistration(result.Registrations[0])));
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(101));
        }

        [Test]
        public async Task PreparedBatchRemainsPrivateUntilDecision()
        {
            Assert.That(m_server.NodeManagerLifecycle, Is.InstanceOf<INodeManagerLifecycle>());
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync([
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))),
                    LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ]).ConfigureAwait(false);
            Assert.That(GetBranchRegistrations(), Is.Empty);
            int decisions = 0;
            LifecycleResult result = await prepared.CommitAsync(async _ =>
            {
                decisions++;
                DataValue first = await ReadValueAsync(new NodeId(
                    kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
                DataValue second = await ReadValueAsync(new NodeId(
                    kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(GetBranchRegistrations(), Is.Empty);
            }).ConfigureAwait(false);
            Assert.That(decisions, Is.EqualTo(1));
            Assert.That(result.Registrations.Count, Is.EqualTo(2));
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(2));
            DataValue publishedFirst = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue publishedSecond = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(publishedFirst.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(publishedSecond.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(publishedFirst.WrappedValue.TryGetValue(out int firstValue), Is.True);
            Assert.That(publishedSecond.WrappedValue.TryGetValue(out int secondValue), Is.True);
            Assert.That(firstValue, Is.EqualTo(kFirstRegistrationValue));
            Assert.That(secondValue, Is.EqualTo(kSecondRegistrationValue));
        }

        /// <summary>
        /// Publishes an already configured conformance contributor through the actual lifecycle.
        /// </summary>
        private static IAsyncNodeManagerFactory CreateConformanceFactory(Mock<IAsyncNodeManager> manager)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(manager.Object));
            return factory.Object;
        }
    }
}

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises the real lifecycle and routing publisher without the network startup
    /// inherited by the main lifecycle fixture.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerLifecycleBatchConflictTests
    {
        private const string kCandidateNamespace = "urn:test:batch-conflict:candidate";

        [TestCase(false)]
        [TestCase(true)]
        public async Task SameBatchDataTypeConflictsRejectBeforeDecisionAndDisposeCandidatesAsync(
            bool conflictEncoding)
        {
            await using var harness = new BatchConflictHarness();
            TypeTable servingTypes = harness.Data.TypeTree;
            NodeId retainedType = new(9200, 1);
            NodeId retainedEncoding = new(9201, 1);
            NodeId firstType = new(9202, 1);
            NodeId secondType = conflictEncoding ? new NodeId(9203, 1) : firstType;
            NodeId encoding = new(9204, 1);
            servingTypes.AddSubtype(retainedType, DataTypeIds.Structure);
            Assert.That(servingTypes.AddEncoding(retainedType, retainedEncoding), Is.True);
            _ = servingTypes.CaptureSnapshot(out TypeTable source, out long revision);
            NodeManagerRoutingTable.RoutingSnapshot routing =
                ((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision;
            string[] namespaces = harness.Data.NamespaceUris.ToArray()!;
            TypeTable? firstImage = null;
            bool retainedFirstParent = false;
            bool retainedFirstEncoding = false;
            Mock<IAsyncNodeManager> first = harness.CreateManager(kCandidateNamespace);
            Mock<IAsyncNodeManager> second = harness.CreateManager(kCandidateNamespace + ":second");
            IAsyncNodeManagerFactory firstFactory = Factory(first, server =>
            {
                firstImage = server.TypeTree;
                server.NamespaceUris.GetIndexOrAppend(kCandidateNamespace);
                server.TypeTree.Add(CreateDataType(firstType, DataTypeIds.Structure, encoding));
                Assert.That(server.TypeTree.FindSuperType(firstType), Is.EqualTo(DataTypeIds.Structure));
                Assert.That(server.TypeTree.FindDataTypeId(encoding), Is.EqualTo(firstType));
            });
            IAsyncNodeManagerFactory secondFactory = Factory(second, server =>
            {
                Assert.That(server.TypeTree.FindDataTypeId(encoding), Is.EqualTo(firstType),
                    "Both declarations must belong to ONE shared batch image.");
                try
                {
                    server.TypeTree.Add(CreateDataType(secondType,
                        conflictEncoding ? DataTypeIds.Structure : DataTypeIds.BaseDataType, encoding));
                }
                finally
                {
                    // Observe the first declaration before lifecycle abort detaches either manager.
                    retainedFirstParent = firstImage!.FindSuperType(firstType) == DataTypeIds.Structure;
                    retainedFirstEncoding = firstImage.FindDataTypeId(encoding) == firstType;
                }
            });
            int decisions = 0;
            IPreparedChanges? prepared = null;
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    prepared = await harness.Lifecycle.PrepareAsync(
                        [LifecycleChange.Add(firstFactory), LifecycleChange.Add(secondFactory)],
                        CancellationToken.None).ConfigureAwait(false);
                    await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    }).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            finally
            {
                if (prepared is not null)
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(retainedFirstParent, Is.True);
                Assert.That(retainedFirstEncoding, Is.True);
                Assert.That(((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision, Is.SameAs(routing));
                Assert.That(harness.Master.AsyncNodeManagers, Does.Contain(harness.ServingManager.Object));
                Assert.That(harness.Master.AsyncNodeManagers, Does.Not.Contain(first.Object));
                Assert.That(harness.Master.AsyncNodeManagers, Does.Not.Contain(second.Object));
                Assert.That(harness.Lifecycle.Registrations, Is.Empty);
                Assert.That(harness.Data.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
                Assert.That(servingTypes.IsCurrentSnapshot(source, revision), Is.True);
                Assert.That(servingTypes.FindSuperType(retainedType), Is.EqualTo(DataTypeIds.Structure));
                Assert.That(servingTypes.FindDataTypeId(retainedEncoding), Is.EqualTo(retainedType));
                Assert.That(servingTypes.FindDataTypeId(encoding), Is.EqualTo(NodeId.Null));
                Assert.That(servingTypes.IsKnown(firstType), Is.False);
                Assert.That(servingTypes.IsKnown(secondType), Is.False);
                first.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                second.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                harness.ServingManager.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
            }
        }

        [TestCase("node")]
        [TestCase("namespace")]
        [TestCase("default")]
        public async Task SameCandidateDuplicateHistorianKeyPreservesFirstBindingBeforeAbortAsync(string scope)
        {
            await using var harness = new BatchConflictHarness();
            var registry = (Opc.Ua.Server.Historian.HistorianProviderRegistry)harness.Data.HistorianRegistry;
            var servingProvider = new Mock<Opc.Ua.Server.Historian.IHistorianProvider>();
            var firstProvider = new Mock<Opc.Ua.Server.Historian.IHistorianProvider>();
            var secondProvider = new Mock<Opc.Ua.Server.Historian.IHistorianProvider>();
            Mock<Opc.Ua.Server.Historian.IHistorianProviderIdentity> servingIdentity =
                servingProvider.As<Opc.Ua.Server.Historian.IHistorianProviderIdentity>();
            Mock<Opc.Ua.Server.Historian.IHistorianProviderIdentity> firstIdentity =
                firstProvider.As<Opc.Ua.Server.Historian.IHistorianProviderIdentity>();
            Mock<Opc.Ua.Server.Historian.IHistorianProviderIdentity> secondIdentity =
                secondProvider.As<Opc.Ua.Server.Historian.IHistorianProviderIdentity>();
            servingProvider.As<IDisposable>();
            firstProvider.As<IDisposable>();
            secondProvider.As<IDisposable>();
            servingIdentity.SetupGet(value => value.ProviderId).Returns("serving-provider");
            firstIdentity.SetupGet(value => value.ProviderId).Returns("first-candidate-provider");
            secondIdentity.SetupGet(value => value.ProviderId).Returns("second-candidate-provider");
            NodeId node = new("history-value", 1);
            Register(registry, servingProvider.Object);
            ArrayOf<Opc.Ua.Server.Historian.IHistorianProvider> providers = registry.Providers;
            NodeManagerRoutingTable.RoutingSnapshot routing =
                ((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision;
            _ = harness.Data.TypeTree.CaptureSnapshot(out TypeTable source, out long revision);
            string[] namespaces = harness.Data.NamespaceUris.ToArray()!;
            Mock<IAsyncNodeManager> candidate = harness.CreateManager(kCandidateNamespace);
            candidate.As<INodeManagerReadinessNotification>()
                .Setup(value => value.OnServerReadyAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            bool firstStillBound = false;
            bool providerSetUnchanged = false;
            IAsyncNodeManagerFactory factory = Factory(candidate, server =>
            {
                var staged = ((Opc.Ua.Server.Historian.IHistorianRegistryProvider)server).HistorianRegistry;
                Register(staged, firstProvider.Object);
                Assert.That(staged.Resolve(node), Is.SameAs(firstProvider.Object));
                ArrayOf<Opc.Ua.Server.Historian.IHistorianProvider> firstSnapshot = staged.Providers;
                Assert.That(firstSnapshot.ToArray(), Does.Contain(firstProvider.Object));
                Assert.That(firstSnapshot.ToArray(), Does.Not.Contain(secondProvider.Object));
                try
                {
                    Register(staged, secondProvider.Object);
                }
                finally
                {
                    // This must be observed before abort, not inferred from serving fallback afterward.
                    firstStillBound = ReferenceEquals(staged.Resolve(node), firstProvider.Object);
                    providerSetUnchanged = staged.Providers.Count == firstSnapshot.Count &&
                        staged.Providers.Contains(servingProvider.Object) &&
                        staged.Providers.Contains(firstProvider.Object) &&
                        !staged.Providers.Contains(secondProvider.Object);
                }
            });
            int decisions = 0;
            IPreparedChanges? prepared = null;
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    prepared = await harness.Lifecycle.PrepareAsync(
                        [LifecycleChange.Add(factory)], CancellationToken.None).ConfigureAwait(false);
                    await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    }).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            finally
            {
                if (prepared is not null)
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(firstStillBound, Is.True);
                Assert.That(providerSetUnchanged, Is.True);
                Assert.That(registry.Resolve(node), Is.SameAs(servingProvider.Object));
                Assert.That(registry.Providers.ToArray(), Is.EquivalentTo(providers.ToArray()!));
                Assert.That(harness.Data.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
                Assert.That(harness.Data.TypeTree.IsCurrentSnapshot(source, revision), Is.True);
                Assert.That(((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision, Is.SameAs(routing));
                Assert.That(harness.Master.AsyncNodeManagers, Does.Contain(harness.ServingManager.Object));
                Assert.That(harness.Master.AsyncNodeManagers, Does.Not.Contain(candidate.Object));
                Assert.That(harness.Lifecycle.Registrations, Is.Empty);
                candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                harness.ServingManager.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
                firstProvider.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                secondProvider.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
                servingProvider.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
            }

            void Register(
                Opc.Ua.Server.Historian.IHistorianProviderRegistry target,
                Opc.Ua.Server.Historian.IHistorianProvider provider)
            {
                switch (scope)
                {
                    case "node":
                        target.RegisterForNode(node, provider);
                        break;
                    case "namespace":
                        target.RegisterForNamespace(DeterministicServerMock.TestNamespaceUri, provider);
                        break;
                    default:
                        target.RegisterDefault(provider);
                        break;
                }
            }
        }

        private static DataTypeNode CreateDataType(NodeId id, NodeId parent, NodeId encoding)
        {
            var node = new DataTypeNode
            {
                NodeId = id,
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("BatchDataType", id.NamespaceIndex)
            };
            node.ReferenceTable.Add(ReferenceTypeIds.HasSubtype, true, parent);
            node.ReferenceTable.Add(ReferenceTypeIds.HasEncoding, false, encoding);
            return node;
        }

        private static IAsyncNodeManagerFactory Factory(
            Mock<IAsyncNodeManager> manager, Action<IServerInternal> declare)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration _, CancellationToken _) =>
                {
                    manager.Setup(value => value.CreateAddressSpaceAsync(
                            It.IsAny<System.Collections.Generic.IDictionary<NodeId,
                                System.Collections.Generic.IList<IReference>>>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(() =>
                        {
                            declare(server);
                            return default;
                        });
                    return new ValueTask<IAsyncNodeManager>(manager.Object);
                });
            return factory.Object;
        }

        /// <summary>
        /// Constructs the lifecycle and routing publisher without starting server workers.
        /// </summary>
        private sealed class BatchConflictHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates independent serving metadata and construction-bound lifecycle services.
            /// </summary>
            public BatchConflictHarness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var configuration = new ApplicationConfiguration
                {
                    ApplicationUri = "urn:test:batch-conflict:server",
                    ServerConfiguration = new ServerConfiguration()
                };
                var status = new ServerStatusValue(new ServerStatusState(null),
                    new ServerStatusDataType { State = ServerState.Running }, new Lock());
                Data = new ServerInternalData(new ServerProperties(), configuration,
                    new ServiceMessageContext(telemetry, EncodeableFactory.Create()), null, null, status);
                Data.NamespaceUris.GetIndexOrAppend(DeterministicServerMock.TestNamespaceUri);
                Data.TypeTree.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
                Data.TypeTree.AddSubtype(DataTypeIds.Structure, DataTypeIds.BaseDataType);
                var mainFactory = new Mock<IMainNodeManagerFactory>();
                var configurationNodeManager = new Mock<IConfigurationNodeManager>();
                configurationNodeManager.As<IDiagnosticsNodeManager>()
                    .Setup(value => value.FindPredefinedNode<ServerObjectState>(ObjectIds.Server))
                    .Returns(new ServerObjectState(null));
                mainFactory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(configurationNodeManager.Object);
                mainFactory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                Data.SetMainNodeManagerFactory(mainFactory.Object);
                ServingManager = CreateManager(DeterministicServerMock.TestNamespaceUri);
                Master = new MasterNodeManager(Data, configuration, null, [ServingManager.Object], null);
                Data.SetNodeManager(Master);
                var host = new Mock<INodeManagerLifecycleHost>(MockBehavior.Strict);
                host.SetupGet(value => value.CurrentInstance).Returns(Data);
                host.SetupGet(value => value.CurrentConfiguration).Returns(configuration);
                host.Setup(value => value.PrepareComplexTypesAsync(
                        It.IsAny<IServerInternal>(), It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask<IDataTypeDefinitionResolver?>));
                Lifecycle = new NodeManagerLifecycle(host.Object, TimeProvider.System, null);
                Assert.That(Data.IsRunning, Is.True);
                Assert.That(Master.AsyncNodeManagers, Does.Contain(ServingManager.Object));
            }

            public ServerInternalData Data { get; }
            public MasterNodeManager Master { get; }

            /// <summary>
            /// Gets the lifecycle owning this harness's candidates and publication state.
            /// </summary>
            public NodeManagerLifecycle Lifecycle { get; }

            public Mock<IAsyncNodeManager> ServingManager { get; }

            public Mock<IAsyncNodeManager> CreateManager(string namespaceUri)
            {
                var manager = new Mock<IAsyncNodeManager>();
                manager.As<IDisposable>();
                manager.SetupGet(value => value.NamespaceUris).Returns([namespaceUri]);
                manager.SetupGet(value => value.SyncNodeManager).Returns(new Mock<INodeManager>().Object);
                manager.Setup(value => value.CreateAddressSpaceAsync(
                        It.IsAny<System.Collections.Generic.IDictionary<NodeId,
                            System.Collections.Generic.IList<IReference>>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask));
                manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask));
                return manager;
            }

            public async ValueTask DisposeAsync()
            {
                Lifecycle.Dispose();
                await Data.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
