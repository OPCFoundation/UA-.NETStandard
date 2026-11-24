// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Quickstarts.ReferenceServer;
using WotConModel = global::Opc.Ua.WotCon;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Live-server integration tests for <see cref="LifecycleWotViewProjectionHost"/>.
    /// A real <see cref="ReferenceServer"/> is started per test and the stable WoT
    /// registry NodeManager is attached, so the projection-view NodeManager the host
    /// creates coexists with the registry manager on the WoT-Con namespace exactly as
    /// it does in production. The host is then driven directly with hand-built plans
    /// whose members are real Nodes owned by the core NodeManager, and the resulting
    /// address space is inspected over the server-side Browse and Read call chain to
    /// prove the projection materializes as a browsable <c>View</c> that organizes the
    /// planned Nodes, creates no affordance Node, and tears down cleanly.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Category("WotCon")]
    [Category("Server")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotViewProjectionLiveTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(WotViewProjectionLiveTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            await WotTestPki.SeedAsync(m_pkiRoot).ConfigureAwait(false);
            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                .ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;

            // Host the stable registry NodeManager so the WoT-Con namespace is registered
            // and the projection-view manager the host creates shares it with the registry
            // manager, mirroring the production topology.
            var options = new WotRegistryServerOptions
            {
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                }
            };
            m_registry = new WotRegistryService();
            var projectionHost = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, projectionHost, documentConverter: new FakeWotDocumentConverter());
            var factory = new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator);
            await m_server.NodeManagerLifecycle
                .ApplyAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            NodeId registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, server.NamespaceUris);
            m_wotConNamespaceIndex = registryNodeId.NamespaceIndex;

            m_viewHost = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_viewHost?.Dispose();
            await CloseActiveSessionAsync().ConfigureAwait(false);

            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            m_coordinator?.Dispose();
            m_registry?.Dispose();
            m_server?.Dispose();

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// A projected View is created as a real <c>View</c> Node and is discoverable
        /// by browsing the standard Views folder.
        /// </summary>
        [Test]
        public async Task AppliedViewIsBrowsableFromTheViewsFolderAsAView()
        {
            NodeId viewNodeId = ViewNodeId("browsable");
            WotViewProjectionRequest request = Request(
                viewNodeId, Plan(1u, Members(Ua.VariableIds.Server_ServerStatus_CurrentTime)));

            _ = await m_viewHost.ApplyAsync(request).ConfigureAwait(false);

            List<ReferenceDescription> views = await BrowseAsync(
                Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);
            ReferenceDescription? view = views.SingleOrDefault(r => TargetOf(r) == viewNodeId);

            Assert.That(view, Is.Not.Null,
                "The projected View must be organized by the standard Views folder.");
            Assert.That(view!.NodeClass, Is.EqualTo(NodeClass.View),
                "The projection must be materialized as a View NodeClass, not an ObjectType.");
        }

        /// <summary>
        /// The View <c>Organizes</c> exactly the planned member Nodes and nothing else.
        /// </summary>
        /// <summary>
        /// <i>WoT Connectivity</i> §6.7 requires <c>HasWoTProjection</c> from the
        /// stored projection document resource to the View it materialized, so a
        /// client can navigate between them. The View owns the inverse edge.
        /// </summary>
        [Test]
        public async Task AppliedViewCarriesHasWoTProjectionBackToItsResource()
        {
            NodeId viewNodeId = ViewNodeId("projection-ref");
            // The resource Node must actually exist: a reference to an unknown
            // target is dropped when the View is imported. In the server the
            // registry NodeManager owns that Node; here the Server Object stands
            // in for it.
            var request = new WotViewProjectionRequest(
                "closure",
                "resource-xid",
                Ua.ObjectIds.Server,
                viewNodeId,
                Plan(1u, Members(Ua.VariableIds.Server_ServerStatus_CurrentTime)));

            _ = await m_viewHost.ApplyAsync(request).ConfigureAwait(false);

            NodeId hasWoTProjection = ExpandedNodeId.ToNodeId(
                ReferenceTypeIds.HasWoTProjection, m_server.CurrentInstance.NamespaceUris);
            List<ReferenceDescription> inverse = await BrowseAsync(
                viewNodeId, hasWoTProjection, BrowseDirection.Inverse).ConfigureAwait(false);

            Assert.That(
                inverse.Any(r => TargetOf(r) == request.ResourceNodeId),
                Is.True,
                "The View must carry HasWoTProjection back to its document resource.");
        }

        [Test]
        public async Task AppliedViewOrganizesExactlyThePlannedNodeIds()
        {
            NodeId viewNodeId = ViewNodeId("organizes");
            WotViewProjectionRequest request = Request(
                viewNodeId,
                Plan(2u, Members(Ua.ObjectIds.Server, Ua.ObjectIds.ObjectsFolder)));

            _ = await m_viewHost.ApplyAsync(request).ConfigureAwait(false);

            List<ReferenceDescription> organized = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);

            Assert.That(
                organized.Select(TargetOf),
                Is.EquivalentTo(new[] { Ua.ObjectIds.Server, Ua.ObjectIds.ObjectsFolder }),
                "The View must organize exactly the planned Nodes.");
        }

        /// <summary>
        /// A planned member whose NodeId no NodeManager owns is dropped rather
        /// than organized, and the handle names it. Organizing it would leave the
        /// View advertising a membership a client can never browse, which is how
        /// a projection over a document that materialized no Node came to report
        /// a full membership while every reference dangled.
        /// </summary>
        [Test]
        public async Task AppliedViewDropsAndReportsAMemberThatIsNotInTheAddressSpace()
        {
            NodeId viewNodeId = ViewNodeId("dangling");
            var missing = new NodeId("Pump1.Operational.Measurements.DifferentialPressure", 1);
            WotViewProjectionRequest request = Request(
                viewNodeId, Plan(4u, Members(Ua.ObjectIds.Server, missing)));

            WotViewProjectionHandle handle = await m_viewHost.ApplyAsync(request)
                .ConfigureAwait(false);

            List<ReferenceDescription> organized = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);

            Assert.That(
                organized.Select(TargetOf),
                Is.EquivalentTo(new[] { Ua.ObjectIds.Server }),
                "Only the member that exists is reachable by Browse. This holds whether " +
                "or not the dangling reference was added, because Browse drops a " +
                "reference whose target no NodeManager owns - which is exactly why the " +
                "membership has to be reported rather than inferred from the address space.");
            Assert.That(handle.Omissions.Count, Is.EqualTo(1),
                "The member that does not exist must be reported, not silently dropped.");
            Assert.That(handle.Omissions[0], Does.Contain(missing.ToString()));
            Assert.That(handle.Message, Does.Contain(missing.ToString()),
                "The omission must reach the resource's load-result Message.");
        }

        /// <summary>
        /// Materializing a projection creates no affordance Node: the only child of the
        /// View is the standard <c>ViewVersion</c> Property, and it has no Variable or
        /// Method components.
        /// </summary>
        [Test]
        public async Task AppliedViewCreatesNoAffordanceNode()
        {
            NodeId viewNodeId = ViewNodeId("no-affordance");
            WotViewProjectionRequest request = Request(
                viewNodeId, Plan(3u, Members(Ua.ObjectIds.Server)));

            _ = await m_viewHost.ApplyAsync(request).ConfigureAwait(false);

            List<ReferenceDescription> components = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.HasComponent, BrowseDirection.Forward)
                .ConfigureAwait(false);
            List<ReferenceDescription> properties = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.HasProperty, BrowseDirection.Forward)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(components, Is.Empty,
                    "A projection View must not create affordance components.");
                Assert.That(properties, Has.Count.EqualTo(1),
                    "The View must expose only the ViewVersion Property.");
                Assert.That(properties[0].BrowseName.Name, Is.EqualTo("ViewVersion"));
            });
        }

        /// <summary>
        /// Nested organizational groups become organizational Objects; only the
        /// outermost materialization is a <c>View</c>.
        /// </summary>
        [Test]
        public async Task NestedGroupsBecomeObjectsAndOnlyTheOutermostIsAView()
        {
            NodeId viewNodeId = ViewNodeId("groups");
            var inner = new WotOrganizationalGroup(
                "Inner", Members(Ua.ObjectIds.ObjectsFolder), NoGroups());
            var outer = new WotOrganizationalGroup(
                "Outer", Members(Ua.ObjectIds.Server), Groups(inner));
            WotViewProjectionRequest request = Request(
                viewNodeId,
                Plan(4u, Members(Ua.VariableIds.Server_ServerStatus_CurrentTime), Groups(outer)));

            WotViewProjectionHandle handle = await m_viewHost.ApplyAsync(request)
                .ConfigureAwait(false);

            List<ReferenceDescription> viewOrganizes = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);
            ReferenceDescription? outerGroup = viewOrganizes
                .SingleOrDefault(r => r.BrowseName.Name == "Outer");
            Assert.That(outerGroup, Is.Not.Null, "The View must organize the outer group.");
            Assert.That(outerGroup!.NodeClass, Is.EqualTo(NodeClass.Object),
                "A nested group must be an organizational Object, not a View.");

            List<ReferenceDescription> outerOrganizes = await BrowseAsync(
                TargetOf(outerGroup), Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);
            ReferenceDescription? innerGroup = outerOrganizes
                .SingleOrDefault(r => r.BrowseName.Name == "Inner");

            Assert.Multiple(() =>
            {
                Assert.That(innerGroup, Is.Not.Null, "The outer group must organize the inner group.");
                Assert.That(innerGroup!.NodeClass, Is.EqualTo(NodeClass.Object),
                    "A deeply nested group must also be an Object.");
                Assert.That(
                    outerOrganizes.Select(TargetOf),
                    Has.Member(Ua.ObjectIds.Server),
                    "The outer group must organize its own planned member.");
                Assert.That(handle.MaterializedNodeCount, Is.EqualTo(3),
                    "The View plus the two nested Objects are the three materialized Nodes.");
            });
        }

        /// <summary>
        /// The standard <c>ViewVersion</c> Property is set from the plan and updates
        /// when a refreshed plan with a changed membership is applied under the same
        /// View NodeId.
        /// </summary>
        [Test]
        public async Task ViewVersionIsSetFromThePlanAndChangesWithMembership()
        {
            NodeId viewNodeId = ViewNodeId("version");
            _ = await m_viewHost
                .ApplyAsync(Request(viewNodeId, Plan(101u, Members(Ua.ObjectIds.Server))))
                .ConfigureAwait(false);
            uint firstVersion = await ReadViewVersionAsync(viewNodeId).ConfigureAwait(false);

            _ = await m_viewHost
                .ApplyAsync(Request(
                    viewNodeId,
                    Plan(202u, Members(Ua.ObjectIds.Server, Ua.ObjectIds.ObjectsFolder))))
                .ConfigureAwait(false);
            uint secondVersion = await ReadViewVersionAsync(viewNodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(firstVersion, Is.EqualTo(101u),
                    "The ViewVersion Property must carry the plan's version.");
                Assert.That(secondVersion, Is.EqualTo(202u),
                    "Re-applying a changed plan must update the ViewVersion Property.");
            });
        }

        /// <summary>
        /// Removing the View tears down the View and its organizational Objects while
        /// leaving the organized Nodes untouched.
        /// </summary>
        [Test]
        public async Task RemoveAsyncRemovesTheViewAndObjectsButLeavesOrganizedNodesIntact()
        {
            NodeId viewNodeId = ViewNodeId("remove");
            var group = new WotOrganizationalGroup(
                "Group", Members(Ua.ObjectIds.Server), NoGroups());
            WotViewProjectionHandle handle = await m_viewHost
                .ApplyAsync(Request(
                    viewNodeId,
                    Plan(5u, Members(Ua.ObjectIds.ObjectsFolder), Groups(group))))
                .ConfigureAwait(false);

            List<ReferenceDescription> organized = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);
            NodeId groupNodeId = TargetOf(organized.Single(r => r.BrowseName.Name == "Group"));

            await m_viewHost.RemoveAsync(handle).ConfigureAwait(false);

            DataValue viewRead = await ReadAsync(viewNodeId).ConfigureAwait(false);
            DataValue groupRead = await ReadAsync(groupNodeId).ConfigureAwait(false);
            DataValue memberRead = await ReadAsync(Ua.ObjectIds.Server).ConfigureAwait(false);
            List<ReferenceDescription> views = await BrowseAsync(
                Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsBad(viewRead.StatusCode), Is.True,
                    "The View Node must be gone after removal.");
                Assert.That(StatusCode.IsBad(groupRead.StatusCode), Is.True,
                    "The organizational Object must be gone after removal.");
                Assert.That(StatusCode.IsGood(memberRead.StatusCode), Is.True,
                    "An organized Node must survive removal of the View.");
                Assert.That(views.Select(TargetOf), Has.No.Member(viewNodeId),
                    "The Views folder must no longer organize the removed View.");
            });
        }

        /// <summary>
        /// A stale handle from a superseded generation does not remove the current
        /// View, so the coordinator's apply-new-then-remove-old refresh keeps the View.
        /// </summary>
        [Test]
        public async Task RemovingAStaleHandleDoesNotRemoveTheSupersedingView()
        {
            NodeId viewNodeId = ViewNodeId("supersede");
            WotViewProjectionHandle first = await m_viewHost
                .ApplyAsync(Request(viewNodeId, Plan(11u, Members(Ua.ObjectIds.Server))))
                .ConfigureAwait(false);
            WotViewProjectionHandle second = await m_viewHost
                .ApplyAsync(Request(viewNodeId, Plan(22u, Members(Ua.ObjectIds.ObjectsFolder))))
                .ConfigureAwait(false);

            await m_viewHost.RemoveAsync(first).ConfigureAwait(false);

            DataValue afterStaleRemove = await ReadAsync(viewNodeId).ConfigureAwait(false);
            uint version = await ReadViewVersionAsync(viewNodeId).ConfigureAwait(false);
            List<ReferenceDescription> organized = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Forward)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(afterStaleRemove.StatusCode), Is.True,
                    "Removing a superseded handle must not remove the current View.");
                Assert.That(version, Is.EqualTo(22u),
                    "The current View must reflect the superseding generation.");
                Assert.That(organized.Select(TargetOf),
                    Is.EquivalentTo(new[] { Ua.ObjectIds.ObjectsFolder }),
                    "The current View must organize the superseding membership.");
            });

            await m_viewHost.RemoveAsync(second).ConfigureAwait(false);
            DataValue afterCurrentRemove = await ReadAsync(viewNodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsBad(afterCurrentRemove.StatusCode), Is.True,
                "Removing the current handle must remove the View.");
        }

        /// <summary>
        /// Applying a <c>null</c> request throws, and removing a <c>null</c> handle is a
        /// no-op.
        /// </summary>
        [Test]
        public void ApplyRejectsNullRequestAndRemoveIgnoresNullHandle()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    async () => await m_viewHost.ApplyAsync(null!).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    async () => await m_viewHost.RemoveAsync(null!).ConfigureAwait(false),
                    Throws.Nothing);
            });
        }

        /// <summary>
        /// Recovery clears the live warning without republishing the manager or losing the original failure.
        /// </summary>
        [Test]
        public async Task RecoveredInitializationRefreshesWarningsWithoutReplacingTheViewOwner()
        {
            var failure = new InvalidOperationException("initial View reconciliation failure");
            int publications = 0;
            LifecycleResult? committed = null;
            Mock<INodeManagerLifecycle> lifecycle = ObserveInitialization(
                () =>
                {
                    if (++publications == 1)
                    {
                        throw failure;
                    }
                },
                result => committed = result);
            m_viewHost.Dispose();
            m_viewHost = new LifecycleWotViewProjectionHost(lifecycle.Object);
            NodeId viewNodeId = ViewNodeId("reconciliation");
            WotViewProjectionHandle first = await m_viewHost.ApplyAsync(
                Request(viewNodeId, Plan(1u, Members(Ua.ObjectIds.Server)))).ConfigureAwait(false);
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            NodeManagerRegistration owner = registrations[^1];
            long revision = m_server.NodeManagerLifecycle.Revision;
            Assert.That(first.Message, Does.Contain(failure.Message));
            Assert.That(committed, Is.Not.Null);
            Assert.That(committed!.Issues.Count, Is.EqualTo(1));
            Assert.That(committed.Issues[0].Stage, Is.EqualTo(NodeManagerReconciliationStage.ApplicationState));
            Exception? originalFailure = committed.CleanupFailure;
            Assert.That(committed.Issues[0].InitialFailure, Is.SameAs(failure));
            Assert.That(m_viewHost.InitializationResult, Is.SameAs(committed));

            await m_viewHost.InitializationResult!.RetryReconciliationAsync().ConfigureAwait(false);
            WotViewProjectionHandle recovered = await m_viewHost.ApplyAsync(
                Request(viewNodeId, Plan(2u, Members(Ua.ObjectIds.ObjectsFolder)))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(recovered.Omissions, Is.Empty, "A recovered issue must not remain a live warning.");
                Assert.That(committed.Issues, Is.Empty);
                Assert.That(committed.CleanupFailure, Is.SameAs(originalFailure));
                Assert.That(m_server.NodeManagerLifecycle.Registrations[registrations.Count - 1], Is.SameAs(owner));
                Assert.That(m_server.NodeManagerLifecycle.Revision, Is.EqualTo(revision));
                Assert.That(publications, Is.EqualTo(2), "Only the failed reconciliation callback is retried.");
            });
            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            await m_viewHost.RemoveAsync(first).ConfigureAwait(false);
            Assert.That(await ReadViewVersionAsync(viewNodeId).ConfigureAwait(false), Is.EqualTo(2u));
            await m_viewHost.RemoveAsync(recovered).ConfigureAwait(false);
            DataValue removed = await ReadAsync(viewNodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsBad(removed.StatusCode), Is.True);
        }

        /// <summary>
        /// Host retries preserve initial failure provenance, update pending warnings and never repeat initialization.
        /// </summary>
        [Test]
        public async Task HostRetryRetainsFailedAttemptsAndClearsOnlyRecoveredWarnings()
        {
            var initial = new InvalidOperationException("initial initialization failure");
            var subsequent = new InvalidOperationException("subsequent reconciliation failure");
            Exception? failure = initial;
            int publications = 0;
            int decisions = 0;
            int disposals = 0;
            Mock<INodeManagerLifecycle> lifecycle = ObserveInitialization(
                () =>
                {
                    publications++;
                    if (failure is not null)
                    {
                        throw failure;
                    }
                },
                _ => { },
                () => decisions++,
                () => disposals++);
            m_viewHost.Dispose();
            m_viewHost = new LifecycleWotViewProjectionHost(lifecycle.Object);

            await m_viewHost.RetryReconciliationAsync().ConfigureAwait(false);
            Assert.That(m_viewHost.InitializationResult, Is.Null);
            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>()), Times.Never);
            NodeId viewNodeId = ViewNodeId("retry");
            WotViewProjectionHandle first = await m_viewHost.ApplyAsync(
                Request(viewNodeId, Plan(1u, Members(Ua.ObjectIds.Server)))).ConfigureAwait(false);
            LifecycleResult result = m_viewHost.InitializationResult!;
            Exception? originalFailure = result.CleanupFailure;
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            using (var canceled = new CancellationTokenSource())
            {
                await canceled.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await m_viewHost.RetryReconciliationAsync(canceled.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            Assert.That(publications, Is.EqualTo(1), "Cancellation must not admit another attempt.");
            Assert.That(first.Message, Does.Contain(initial.Message));

            failure = subsequent;
            await m_viewHost.RetryReconciliationAsync().ConfigureAwait(false);
            WotViewProjectionHandle pending = await m_viewHost.ApplyAsync(
                Request(viewNodeId, Plan(2u, Members(Ua.ObjectIds.Server)))).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Issues.Count, Is.EqualTo(1));
                Assert.That(result.Issues[0].InitialFailure, Is.SameAs(initial));
                Assert.That(result.Issues[0].CurrentFailure, Is.SameAs(subsequent));
                Assert.That(result.Issues[0].IsRetryable, Is.True);
                Assert.That(pending.Message, Does.Contain(subsequent.Message).And.Not.Contain(initial.Message));
            });

            failure = null;
            await m_viewHost.RetryReconciliationAsync().ConfigureAwait(false);
            await m_viewHost.RetryReconciliationAsync().ConfigureAwait(false);
            WotViewProjectionHandle recovered = await m_viewHost.ApplyAsync(
                Request(viewNodeId, Plan(3u, Members(Ua.ObjectIds.ObjectsFolder)))).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Omissions, Is.Empty);
                Assert.That(result.Issues, Is.Empty);
                Assert.That(result.CleanupFailure, Is.SameAs(originalFailure));
                Assert.That(m_viewHost.InitializationResult, Is.SameAs(result));
                Assert.That(m_server.NodeManagerLifecycle.Revision, Is.EqualTo(result.Revision));
                Assert.That(m_server.NodeManagerLifecycle.Registrations.ToArray(), Is.EqualTo(registrations.ToArray()));
                Assert.That(publications, Is.EqualTo(3));
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(disposals, Is.EqualTo(1), "The prepared owner must be disposed before retry.");
            });
            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            m_viewHost.Dispose();
            await Assert.ThatAsync(
                async () => await m_viewHost.RetryReconciliationAsync().ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
        }

        /// <summary>
        /// Concurrent host retries share native work and cancellation releases only the canceled caller.
        /// </summary>
        [Test]
        public async Task ConcurrentHostRetriesJoinWithoutBlockingApplyOrCancelingRecovery()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var canceled = new CancellationTokenSource();
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            Mock<INodeManagerLifecycle> lifecycle = ObserveInitialization(
                () =>
                {
                    if (Interlocked.Increment(ref attempts) == 1)
                    {
                        throw new InvalidOperationException("pending shared reconciliation");
                    }
                    entered.TrySetResult(true);
                    if (!release.Wait(TimeSpan.FromSeconds(30)))
                    {
                        throw new TimeoutException("The test did not release reconciliation.");
                    }
                },
                _ => { });
            m_viewHost.Dispose();
            m_viewHost = new LifecycleWotViewProjectionHost(lifecycle.Object);
            NodeId viewNodeId = ViewNodeId("concurrent-retry");
            WotViewProjectionRequest request = Request(viewNodeId, Plan(1u, Members(Ua.ObjectIds.Server)));
            _ = await m_viewHost.ApplyAsync(request, timeout.Token).ConfigureAwait(false);
            LifecycleResult result = m_viewHost.InitializationResult!;
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            Task first = m_viewHost.RetryReconciliationAsync(canceled.Token).AsTask();
            Task? second = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                second = m_viewHost.RetryReconciliationAsync(timeout.Token).AsTask();
                await canceled.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(async () => await first.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                WotViewProjectionHandle pending = await m_viewHost.ApplyAsync(request, timeout.Token)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(second.IsCompleted, Is.False);
                    Assert.That(attempts, Is.EqualTo(2), "Concurrent callers must join the same native attempt.");
                    Assert.That(pending.Message, Does.Contain("pending shared reconciliation"));
                    Assert.That(m_server.NodeManagerLifecycle.Registrations.ToArray(),
                        Is.EqualTo(registrations.ToArray()));
                    Assert.That(m_server.NodeManagerLifecycle.Revision, Is.EqualTo(result.Revision));
                });
            }
            finally
            {
                release.Set();
                try
                {
                    await first.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (canceled.IsCancellationRequested)
                {
                }
                if (second is not null)
                {
                    await second.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            WotViewProjectionHandle recovered = await m_viewHost.ApplyAsync(request, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(recovered.Omissions, Is.Empty);
            Assert.That(result.Issues, Is.Empty);
            Assert.That(attempts, Is.EqualTo(2));
            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An obsolete reconciliation callback remains an explicit unresolved warning, not a false recovery.
        /// </summary>
        [Test]
        public async Task SupersededInitializationRemainsUnresolvedWithoutReinitializingTheManager()
        {
            var failure = new InvalidOperationException("superseded initialization failure");
            int publications = 0;
            Mock<INodeManagerLifecycle> lifecycle = ObserveInitialization(
                () =>
                {
                    publications++;
                    throw failure;
                },
                _ => { });
            m_viewHost.Dispose();
            m_viewHost = new LifecycleWotViewProjectionHost(lifecycle.Object);
            NodeId viewNodeId = ViewNodeId("superseded-reconciliation");
            WotViewProjectionRequest request = Request(viewNodeId, Plan(1u, Members(Ua.ObjectIds.Server)));
            _ = await m_viewHost.ApplyAsync(request).ConfigureAwait(false);
            LifecycleResult result = m_viewHost.InitializationResult!;
            Exception? originalFailure = result.CleanupFailure;
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            var factory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            factory.SetupGet(value => value.NamespacesUris).Returns(["urn:opcfoundation:test:unrelated-view-owner"]);
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration _, CancellationToken _) =>
                    new ValueTask<IAsyncNodeManager>(new UnrelatedNodeManager(server)));
            await using (IPreparedChanges unrelated =
                await m_server.NodeManagerLifecycle.PrepareAsync([LifecycleChange.Add(factory.Object)])
                    .ConfigureAwait(false))
            {
                LifecycleResult added = await unrelated.CommitAsync().ConfigureAwait(false);
                Assert.That(added.CleanupFailure, Is.Null);
            }
            long revision = m_server.NodeManagerLifecycle.Revision;

            await m_viewHost.RetryReconciliationAsync().ConfigureAwait(false);
            WotViewProjectionHandle unresolved = await m_viewHost.ApplyAsync(request).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Issues.Count, Is.EqualTo(1));
                Assert.That(result.Issues[0].IsSuperseded, Is.True);
                Assert.That(result.Issues[0].IsRetryable, Is.False);
                Assert.That(result.Issues[0].InitialFailure, Is.SameAs(failure));
                Assert.That(result.CleanupFailure, Is.SameAs(originalFailure));
                Assert.That(unresolved.Message, Does.Contain("unresolved: ").And.Contain("superseded"));
                Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(registrations.Count + 1));
                Assert.That(m_server.NodeManagerLifecycle.Registrations[registrations.Count - 1],
                    Is.SameAs(registrations[^1]));
                Assert.That(m_server.NodeManagerLifecycle.Revision, Is.EqualTo(revision));
                Assert.That(publications, Is.EqualTo(1), "A superseded callback must never replay.");
            });
            lifecycle.Verify(value => value.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Injects a retryable failure into the native application-state callback without replacing its result.
        /// </summary>
        private Mock<INodeManagerLifecycle> ObserveInitialization(
            Action publishAttempt,
            Action<LifecycleResult> committed,
            Action? decideAttempt = null,
            Action? disposed = null)
        {
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.SetupGet(value => value.IsShuttingDown)
                .Returns(() => m_server.NodeManagerLifecycle.IsShuttingDown);
            lifecycle.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<LifecycleChange> changes, CancellationToken token) =>
                {
                    IPreparedChanges prepared = await m_server.NodeManagerLifecycle.PrepareAsync(changes, token)
                        .ConfigureAwait(false);
                    var observed = new Mock<IPreparedChanges>(MockBehavior.Strict);
                    observed.Setup(value => value.CommitAsync(
                        It.IsAny<Func<CancellationToken, ValueTask>?>(), It.IsAny<Action>(),
                        It.IsAny<CancellationToken>()))
                        .Returns(async (Func<CancellationToken, ValueTask>? decide, Action publish,
                            CancellationToken cancellation) =>
                        {
                            LifecycleResult result = await prepared.CommitAsync(
                                async decisionToken =>
                                {
                                    decideAttempt?.Invoke();
                                    if (decide is not null)
                                    {
                                        await decide(decisionToken).ConfigureAwait(false);
                                    }
                                },
                                () =>
                                {
                                    publish();
                                    publishAttempt();
                                },
                                cancellation).ConfigureAwait(false);
                            committed(result);
                            return result;
                        });
                    observed.Setup(value => value.DisposeAsync()).Returns(async () =>
                    {
                        await prepared.DisposeAsync().ConfigureAwait(false);
                        disposed?.Invoke();
                    });
                    return observed.Object;
                });
            return lifecycle;
        }

        private async Task<uint> ReadViewVersionAsync(NodeId viewNodeId)
        {
            List<ReferenceDescription> properties = await BrowseAsync(
                viewNodeId, Ua.ReferenceTypeIds.HasProperty, BrowseDirection.Forward)
                .ConfigureAwait(false);
            ReferenceDescription property = properties.Single(r => r.BrowseName.Name == "ViewVersion");
            DataValue value = await ReadAsync(TargetOf(property), Attributes.Value).ConfigureAwait(false);
            return value.GetValue<uint>(0u);
        }

        private async Task<DataValue> ReadAsync(NodeId nodeId, uint attributeId = Attributes.NodeClass)
        {
            ArrayOf<ReadValueId> readIds =
                [new ReadValueId { NodeId = nodeId, AttributeId = attributeId }];
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse response = await m_server.ReadAsync(
                m_secureChannelContext, requestHeader, 0,
                TimestampsToReturn.Neither, readIds, RequestLifetime.None).ConfigureAwait(false);
            return response.Results[0];
        }

        private async Task<List<ReferenceDescription>> BrowseAsync(
            NodeId nodeId, NodeId referenceTypeId, BrowseDirection direction)
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var template = new BrowseDescription
            {
                BrowseDirection = direction,
                ReferenceTypeId = referenceTypeId,
                IncludeSubtypes = false,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            ArrayOf<BrowseDescription> nodesToBrowse =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId([nodeId], template);
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            BrowseResponse response = await services
                .BrowseAsync(requestHeader, new ViewDescription(), 0, nodesToBrowse)
                .ConfigureAwait(false);
            var references = new List<ReferenceDescription>();
            foreach (ReferenceDescription reference in response.Results[0].References)
            {
                references.Add(reference);
            }
            return references;
        }

        private async Task CloseActiveSessionAsync()
        {
            if (m_requestHeader is null)
            {
                return;
            }
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            await m_server
                .CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                .ConfigureAwait(false);
            m_requestHeader = null!;
            m_secureChannelContext = null!;
        }

        private NodeId ViewNodeId(string name)
        {
            return new NodeId("wot/projection/view/" + name, m_wotConNamespaceIndex);
        }

        private WotViewProjectionRequest Request(NodeId viewNodeId, WotViewProjectionPlan plan)
        {
            var resourceNodeId = new NodeId("wot/projection/resource", m_wotConNamespaceIndex);
            return new WotViewProjectionRequest(
                "closure", "resource-xid", resourceNodeId, viewNodeId, plan);
        }

        private static WotViewProjectionPlan Plan(uint viewVersion, ArrayOf<NodeId> organizedNodeIds)
        {
            return Plan(viewVersion, organizedNodeIds, NoGroups());
        }

        private static WotViewProjectionPlan Plan(
            uint viewVersion,
            ArrayOf<NodeId> organizedNodeIds,
            ArrayOf<WotOrganizationalGroup> groups)
        {
            return new WotViewProjectionPlan(
                "http://example.com/scenario/Test",
                WotDocumentKind.ThingDescription,
                organizedNodeIds,
                groups,
                viewVersion,
                []);
        }

        private static ArrayOf<NodeId> Members(params NodeId[] members)
        {
            return members.ToArrayOf();
        }

        private static ArrayOf<WotOrganizationalGroup> Groups(params WotOrganizationalGroup[] groups)
        {
            return groups.ToArrayOf();
        }

        private static ArrayOf<WotOrganizationalGroup> NoGroups()
        {
            return ArrayOf<WotOrganizationalGroup>.Empty;
        }

        private NodeId TargetOf(ReferenceDescription reference)
        {
            return ExpandedNodeId.ToNodeId(reference.NodeId, m_server.CurrentInstance.NamespaceUris);
        }

        private string m_pkiRoot = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private ReferenceServer m_server = null!;
        private RequestHeader m_requestHeader = null!;
        private SecureChannelContext m_secureChannelContext = null!;
        private WotRegistryService m_registry = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private LifecycleWotViewProjectionHost m_viewHost = null!;
        private ushort m_wotConNamespaceIndex;

        /// <summary>
        /// Publishes an unrelated generation without changing the immediate View owner.
        /// </summary>
        /// <param name="server">The native server that owns the generation.</param>
        private sealed class UnrelatedNodeManager(IServerInternal server)
            : AsyncCustomNodeManager(server, "urn:opcfoundation:test:unrelated-view-owner")
        {
        }
    }
}
