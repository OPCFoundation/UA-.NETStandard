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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public Task StartupRestoresTheCommittedSourceAndViewWithoutRepublishing(bool pendingSelection)
        {
            return VerifyStartupRecoveryAsync(pendingSelection, false);
        }

        [Test]
        public Task StartupRestoresTheCommittedBytesAfterAnActiveVersionIsOverwritten()
        {
            return VerifyStartupRecoveryAsync(false, true);
        }

        [TestCase("missing-input")]
        [TestCase("invalid-graph")]
        [TestCase("foreign-server")]
        [TestCase("absent-graph")]
        [TestCase("empty-graph")]
        [TestCase("count-zero")]
        [TestCase("count-extra")]
        [TestCase("wrong-root")]
        public async Task StartupRejectsUnverifiableCommittedEvidenceWithoutNewPublication(string invalid)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-invalid-restart")).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            WotRegistrySnapshot damaged;
            if (invalid == "missing-input")
            {
                WotResourceGroup group = before.FindGroup(source.GroupId)!;
                WotResource active = before.FindResourceByXid(source.Xid)!;
                damaged = before.WithGroup(group.WithResources(
                    group.Resources.SetItem(source.ResourceId, active.WithCommittedVersion(null)), group.Epoch),
                    before.Generation + 1);
            }
            else if (invalid is "count-zero" or "count-extra" or "wrong-root")
            {
                WotResource projection = before.FindResource(WotRegistryGroups.ThingDescriptions, "child")!;
                WotResourceGroup group = before.FindGroup(projection.GroupId)!;
                WotResource changed = invalid == "wrong-root"
                    ? projection.With(rootNodeId: new NodeId("WrongView", projection.RootNodeId.NamespaceIndex))
                    : projection.With(materializedNodeCount: invalid == "count-zero" ? 0 : 99);
                damaged = before.WithGroup(group.WithResources(
                    group.Resources.SetItem(projection.ResourceId, changed), group.Epoch), before.Generation + 1);
            }
            else if (invalid is "absent-graph" or "empty-graph")
            {
                damaged = new WotRegistrySnapshot(before.Generation + 1, before.Groups, before.Labels,
                    invalid == "absent-graph" ? default : ByteString.Empty, before.RefreshGeneration);
            }
            else
            {
                JsonNode graph = invalid == "invalid-graph"
                    ? new JsonObject()
                    : JsonNode.Parse(before.CanonicalViewGraphState.Span)!;
                if (invalid == "foreign-server")
                {
                    graph["logicalServerUri"] = "urn:not-this-logical-server";
                }
                damaged = before.WithPublicationState(before.Generation + 1, before.RefreshGeneration,
                    ByteString.From(Encoding.UTF8.GetBytes(graph.ToJsonString())));
            }
            await m_store.CommitAsync(damaged).ConfigureAwait(false);
            await using PreparedWotTestRuntime restarted = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            restarted.Namespaces.GetIndexOrAppend(kStockViewNamespace);
            using var store = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            using var registry = new WotRegistryService(store);
            using var restoredViews = new LifecycleWotViewProjectionHost(restarted.Lifecycle);
            using var coordinator = new WotMaterializationCoordinator(
                registry, restarted.Host, documentConverter: new StockViewSourceConverter(),
                viewProjectionHost: restoredViews);
            var events = new List<WotMaterializationEventArgs>();
            coordinator.Event += (_, change) => events.Add(change);
            int owners = restarted.Lifecycle.Registrations.Count;

            await Assert.ThatAsync(async () => await restarted.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, registry, coordinator),
                callerContext: null).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(restarted.Lifecycle.Registrations.Count, Is.EqualTo(owners + 1),
                "Only the already-committed parent registry remains; recovery candidates must be aborted.");
            Assert.That(coordinator.Generation, Is.Zero);
            Assert.That(coordinator.CommittedPublication.Views.IsEmpty, Is.True);
            Assert.That(events, Is.Empty);
            WotRegistrySnapshot durable = await store.LoadAsync().ConfigureAwait(false);
            Assert.That(durable.Generation, Is.EqualTo(damaged.Generation));
            Assert.That(durable.RefreshGeneration, Is.EqualTo(damaged.RefreshGeneration));
            Assert.That(durable.CanonicalViewGraphState, Is.EqualTo(damaged.CanonicalViewGraphState));
        }

        [Test]
        public async Task RecoverySelectsRetainedActiveInputsDespiteNewDesiredEnableMetadata()
        {
            WotResource resource = await AddAsync("pending-disabled").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-disable")).ConfigureAwait(false);
            await m_registry.SetEnabledAsync(resource.GroupId, resource.ResourceId, false).ConfigureAwait(false);
            WotResource disabled = m_registry.Current.FindResourceByXid(resource.Xid)!;
            Assert.That(disabled.Enabled, Is.False);
            Assert.That(disabled.ActiveVersionId, Is.EqualTo("v1"));
            using WotMaterializationSnapshot captured = await WotDependencyGraph.CapturePublicationAsync(
                m_registry,
                [new WoTResourceSelectorDataType
                {
                    Kind = resource.Kind, GroupId = resource.GroupId, ResourceId = resource.ResourceId, VersionId = "v1"
                }], false, 64, [], CancellationToken.None, committedInputs: true).ConfigureAwait(false);

            Assert.That(captured.Closures.Count, Is.EqualTo(1));
            WotResource restored = captured.Closures[0].ActivationMembers[0];
            Assert.That(restored.Enabled, Is.True);
            Assert.That(restored.DefaultVersion!.Digest, Is.EqualTo(disabled.ActiveVersion!.Digest));
            Assert.That(m_registry.Current.FindResourceByXid(resource.Xid), Is.SameAs(disabled));
        }

        [Test]
        public async Task StartupRecoversAnOrdinarySourceWithoutACanonicalGraph()
        {
            WotResource resource = await AddAsync("ordinary-recovery").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("ordinary-before-restart")).ConfigureAwait(false);
            WotRegistrySnapshot expected = m_registry.Current;
            Assert.That(expected.CanonicalViewGraphState.IsNull, Is.True);
            await using PreparedWotTestRuntime restarted = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            restarted.Namespaces.GetIndexOrAppend("urn:c1:ordinary-padding");
            using var store = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            using var registry = new WotRegistryService(store);
            using var coordinator = new WotMaterializationCoordinator(
                registry, restarted.Host, documentConverter: m_converter);
            var events = new List<WotMaterializationEventArgs>();
            coordinator.Event += (_, change) => events.Add(change);

            await restarted.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, registry, coordinator),
                callerContext: null).ConfigureAwait(false);

            Assert.That(coordinator.Generation, Is.EqualTo(1u));
            Assert.That(registry.Current.Generation, Is.EqualTo(expected.Generation));
            Assert.That(registry.Current.CanonicalViewGraphState.IsNull, Is.True);
            Assert.That(coordinator.CommittedPublication.Views.IsEmpty, Is.True);
            Assert.That(events, Is.Empty);
            NodeId root = ExpandedNodeId.ToNodeId(
                new ExpandedNodeId(5000u, ModelUri(resource)), restarted.Namespaces);
            Assert.That(registry.Current.FindResourceByXid(resource.Xid)!.RootNodeId, Is.EqualTo(root));
            using ISession session = await m_client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{restarted.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = root, AttributeId = Attributes.NodeClass }],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
                Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Object));
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StartupRestoresAnExplicitEmptyCommittedGeneration()
        {
            IWotPreparedRegistryPublication publication = await m_registry.PreparePublicationAsync(
                m_registry.Current, [], 5, ByteString.Empty).ConfigureAwait(false);
            await using (publication.ConfigureAwait(false))
            {
                await publication.DecideAsync(CancellationToken.None).ConfigureAwait(false);
                publication.Publish();
            }
            WotRegistrySnapshot expected = m_registry.Current;
            await using PreparedWotTestRuntime restarted = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            using var store = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            using var registry = new WotRegistryService(store);
            using var coordinator = new WotMaterializationCoordinator(registry, restarted.Host);

            await restarted.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, registry, coordinator),
                callerContext: null).ConfigureAwait(false);

            Assert.That(coordinator.Generation, Is.EqualTo(5u));
            Assert.That(coordinator.CommittedPublication.RefreshGeneration, Is.EqualTo(5u));
            Assert.That(registry.Current.Generation, Is.EqualTo(expected.Generation));
            Assert.That(registry.Current.CanonicalViewGraphState, Is.EqualTo(ByteString.Empty));
            Assert.That(coordinator.CommittedPublication.Views.IsEmpty, Is.True);
            Assert.That(await coordinator.RecoverAsync().ConfigureAwait(false), Is.True);
            Assert.That((await store.LoadAsync().ConfigureAwait(false)).Generation, Is.EqualTo(expected.Generation));
        }

        private async Task VerifyStartupRecoveryAsync(bool pendingSelection, bool overwriteActive)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            WotRefreshResult initial = await m_coordinator.RefreshAsync(HandoffRequest("before-restart"))
                .ConfigureAwait(false);
            Assert.That(initial.NewGeneration, Is.EqualTo(1u));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            if (pendingSelection)
            {
                await UpsertStockSourceAsync(true).ConfigureAwait(false);
            }
            else if (overwriteActive)
            {
                await UpsertStockSourceAsync(true, versionId: "v1").ConfigureAwait(false);
            }
            WotRegistrySnapshot committed = m_registry.Current;
            Assert.That(committed.FindResourceByXid(source.Xid)!.ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(committed.FindResourceByXid(source.Xid)!.DefaultVersionId,
                Is.EqualTo(pendingSelection ? "v2" : "v1"));
            WotCanonicalViewState expectedGraph = RestoreStockGraph(committed.CanonicalViewGraphState, false);
            ByteString expectedDigest = expectedGraph.Views.ToList()
                .Single(view => view.ResourceXid == child.Xid).MembershipDigest;

            await using PreparedWotTestRuntime restarted = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            restarted.Namespaces.GetIndexOrAppend("urn:c1:restart-padding");
            restarted.Namespaces.GetIndexOrAppend(kStockViewNamespace);
            using var restoredStore = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            using var restoredRegistry = new WotRegistryService(restoredStore);
            using var restoredViews = new LifecycleWotViewProjectionHost(restarted.Lifecycle);
            using var restoredCoordinator = new WotMaterializationCoordinator(
                restoredRegistry, restarted.Host, documentConverter: new StockViewSourceConverter(),
                viewProjectionHost: restoredViews);
            var events = new List<WotMaterializationEventArgs>();
            restoredCoordinator.Event += (_, change) => events.Add(change);

            await restarted.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, restoredRegistry, restoredCoordinator),
                callerContext: null).ConfigureAwait(false);

            Assert.That(restoredCoordinator.Generation, Is.EqualTo(1u));
            Assert.That(restoredRegistry.Current.Generation, Is.EqualTo(committed.Generation));
            Assert.That(restoredRegistry.Current.RefreshGeneration, Is.EqualTo(committed.RefreshGeneration));
            Assert.That(restoredRegistry.Current.CanonicalViewGraphState, Is.EqualTo(committed.CanonicalViewGraphState));
            Assert.That(restoredRegistry.Current.FindResourceByXid(source.Xid)!.ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(restoredRegistry.Current.FindResourceByXid(source.Xid)!.DefaultVersionId,
                Is.EqualTo(pendingSelection ? "v2" : "v1"));
            Assert.That(restoredCoordinator.CommittedPublication.Views.Count, Is.EqualTo(1));
            Assert.That(events, Is.Empty, "Rehydration is not a new materialization decision or repeated intent.");
            ArrayOf<Opc.Ua.Server.NodeManagerRegistration> owners = restarted.Lifecycle.Registrations;
            WotRegistrySnapshot live = restoredRegistry.Current;
            Assert.That(await restoredCoordinator.RecoverAsync().ConfigureAwait(false), Is.True);
            Assert.That(restoredRegistry.Current, Is.SameAs(live));
            Assert.That(restarted.Lifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(events, Is.Empty);
            WotRegistrySnapshot durable = await restoredStore.LoadAsync().ConfigureAwait(false);
            Assert.That(durable.Generation, Is.EqualTo(committed.Generation));
            Assert.That(durable.RefreshGeneration, Is.EqualTo(committed.RefreshGeneration));
            Assert.That(durable.CanonicalViewGraphState, Is.EqualTo(committed.CanonicalViewGraphState));

            using ISession session = await m_client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{restarted.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                var reading = new NodeId("Source/Reading",
                    (ushort)session.NamespaceUris.GetIndex(kStockSourceNamespace));
                DataValue value = await session.ReadValueAsync(reading).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out int actual), Is.True);
                Assert.That(actual, Is.EqualTo(42), "Recovery must not activate the pending v2 selection.");
                var view = new NodeId("child", (ushort)session.NamespaceUris.GetIndex(kStockViewNamespace));
                Assert.That(restoredRegistry.Current.FindResourceByXid(source.Xid)!.RootNodeId,
                    Is.EqualTo(new NodeId("Source", reading.NamespaceIndex)));
                Assert.That(restoredRegistry.Current.FindResourceByXid(child.Xid)!.RootNodeId, Is.EqualTo(view));
                BrowseResponse browsed = await session.BrowseAsync(null,
                    new ViewDescription { ViewId = view, ViewVersion = 1 }, 0,
                    [new BrowseDescription
                    {
                        NodeId = view,
                        ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                        BrowseDirection = BrowseDirection.Forward,
                        ResultMask = (uint)BrowseResultMask.All
                    }], CancellationToken.None).ConfigureAwait(false);
                Assert.That(browsed.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(browsed.Results[0].References.ToList().Select(reference => reference.NodeId),
                    Is.EqualTo(new[] { new ExpandedNodeId(reading) }));
                WotCanonicalViewState restored = WotCanonicalViewState.Parse(
                    restoredCoordinator.CommittedPublication.RegistrySnapshot.CanonicalViewGraphState);
                Assert.That(restored.Views.ToList().Single().MembershipDigest, Is.EqualTo(expectedDigest));
                WotRefreshResult next = await restoredCoordinator.RefreshAsync(new WotRefreshRequest
                {
                    RequestId = "after-recovery",
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
                }).ConfigureAwait(false);
                Assert.That(next.NewGeneration, Is.EqualTo(pendingSelection || overwriteActive ? 2u : 1u));
                DataValue refreshed = await session.ReadValueAsync(reading).ConfigureAwait(false);
                Assert.That(refreshed.WrappedValue.TryGetValue(out int after), Is.True);
                Assert.That(after, Is.EqualTo(pendingSelection || overwriteActive ? 84 : 42));
                if (!pendingSelection && !overwriteActive)
                {
                    Assert.That(next.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
                    Assert.That(restarted.Lifecycle.Registrations, Is.EqualTo(owners));
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
