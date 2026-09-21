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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedViewDiUsesTheRegisteredOwnerAndRejectsImmediateOnlyAliases(bool immediateOnly)
        {
            var services = new ServiceCollection();
            services.AddSingleton(m_server.NodeManagerLifecycle);
            InMemoryWotViewProjectionHost? custom = immediateOnly ? new InMemoryWotViewProjectionHost() : null;
            if (custom is not null)
            {
                services.AddSingleton<IWotViewProjectionHost>(custom);
            }
            services.AddOpcUa().AddWotRegistryServer();
            ServiceProvider provider = services.BuildServiceProvider();
            await using (provider.ConfigureAwait(false))
            {
                IWotViewProjectionHost owner = provider.GetRequiredService<IWotViewProjectionHost>();
                if (custom is not null)
                {
                    Assert.That(owner, Is.SameAs(custom));
                    Assert.That(() => provider.GetRequiredService<IWotPreparedViewProjectionHost>(),
                        Throws.InvalidOperationException);
                }
                else
                {
                    IWotPreparedViewProjectionHost typed =
                        provider.GetRequiredService<IWotPreparedViewProjectionHost>();
                    Assert.That(typed, Is.SameAs(owner));
                    Assert.That(typed, Is.TypeOf<LifecycleWotViewProjectionHost>());
                    Assert.That(typed.SupportsPreparedPublication, Is.True);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task StockCanonicalViewIncludesAuthoredNumericSourceMembers(bool alreadyActive)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false, numeric: true).ConfigureAwait(false);
            if (alreadyActive)
            {
                await m_coordinator.RefreshAsync(HandoffRequest("stock-numeric-source")).ConfigureAwait(false);
            }
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(
                HandoffRequest("stock-numeric-view", alreadyActive ? 1u : 0u)).ConfigureAwait(false);

            Assert.That(result.Results.All(row => row.LoadState == WoTLoadStateEnum.Active), Is.True,
                string.Join("; ", result.Results.Select(row => row.Message)));
            ushort index = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kStockSourceNamespace);
            var member = new NodeId(101u, index);
            DataValue reading = await m_session.ReadValueAsync(member).ConfigureAwait(false);
            Assert.That(reading.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reading.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            List<ReferenceDescription> membership = await BrowseStockAsync(
                StockView("child"), Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(membership.Select(reference => (reference.NodeId, reference.NodeClass, reference.BrowseName)),
                Is.EqualTo(new[]
                {
                    (new ExpandedNodeId(member), NodeClass.Variable, new QualifiedName("Reading", index))
                }), "Authored numeric identities belong to the source image without a string-prefix relationship.");
            WotCanonicalViewState graph = WotCanonicalViewState.Restore(
                m_registry.Current.CanonicalViewGraphState,
                new WotCanonicalViewGraphContext(
                    m_server.CurrentInstance.ServerUris.GetString(0)!, Namespaces.WotCon,
                    m_server.CurrentInstance.NamespaceUris, [new(member, NodeClass.Variable)]));
            WotCanonicalViewPublication published = graph.Views.ToList().Single(view => view.ResourceXid == child.Xid);
            Assert.That(published.Membership.ToArray(),
                Is.EqualTo(new[] { new ExpandedNodeId(101u, kStockSourceNamespace) }));
            Assert.That(published.Omissions.IsEmpty, Is.True);
            Assert.That(published.ViewVersion, Is.EqualTo(1u));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task StockPreparedViewsPublishTheCandidateSourceAndDurableGraphTogether(bool warning)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kStockSourceNamespace), Is.EqualTo(-1));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.BeforeDecisionAsync = async _ =>
            {
                entered.TrySetResult(true);
                await resume.Task.ConfigureAwait(false);
            };
            m_committedWarning = warning;
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(HandoffRequest("stock-prepare")).AsTask();
            try
            {
                await AwaitStockDecisionAsync(pending, entered.Task).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_registry.Current.CanonicalViewGraphState.IsNull, Is.True);
                Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That((await ReadNodeClassAsync(StockView("child")).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(await BrowseStockAsync(ResourceId(child), HasProjectionId()).ConfigureAwait(false),
                    Is.Empty);
                Assert.That(await BrowseStockAsync(ResourceId(source), HasProjectionId()).ConfigureAwait(false),
                    Is.Empty);
                Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(1),
                    "Only the source generation, never the canonical View, creates a binding owner.");
                Assert.That(probe.Publications.Single().ViewGraph!.Views.ToList().Single().ResourceXid,
                    Is.EqualTo(child.Xid));
            }
            finally
            {
                resume.TrySetResult(true);
            }
            WotRefreshResult result = await pending.ConfigureAwait(false);
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Results.Select(row => row.Xid), Is.EquivalentTo(new[] { source.Xid, child.Xid }));
            Assert.That(result.Results.All(row => row.LoadState == WoTLoadStateEnum.Active), Is.True);
            Assert.That(result.Results.Single(row => row.Xid == source.Xid).RootNodeId,
                Is.EqualTo(StockNode("Source")));
            Assert.That(result.Results.Single(row => row.Xid == child.Xid).RootNodeId, Is.EqualTo(StockView("child")));
            Assert.That(m_registry.Current.FindResourceByXid(source.Xid)!.RootNodeId, Is.EqualTo(StockNode("Source")));
            Assert.That(m_registry.Current.FindResourceByXid(child.Xid)!.RootNodeId, Is.EqualTo(StockView("child")));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(probe.AcceptedCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeCreatedCount - probe.RuntimeDisposedCount, Is.EqualTo(1));
            if (warning)
            {
                Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
                Assert.That(result.Results.All(row => row.Message!.Contains("durability", StringComparison.Ordinal)),
                    Is.True);
            }
            using var observer = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            WotRegistrySnapshot durable = await observer.LoadAsync().ConfigureAwait(false);
            Assert.That(durable.CanonicalViewGraphState, Is.EqualTo(m_registry.Current.CanonicalViewGraphState));
            Assert.That(durable.RefreshGeneration, Is.EqualTo(1u));
            WotCanonicalViewState graph = RestoreStockGraph(durable.CanonicalViewGraphState, false);
            WotCanonicalViewPublication published = graph.Views.ToList().Single();
            Assert.That(published.ResourceXid, Is.EqualTo(child.Xid));
            Assert.That(published.ViewVersion, Is.EqualTo(1u));
            Assert.That(published.Membership.ToArray(),
                Is.EqualTo(new[] { new ExpandedNodeId("Source/Reading", kStockSourceNamespace) }));
            Assert.That(published.MembershipDigest.Length, Is.EqualTo(32));
            Assert.That(m_coordinator.CommittedPublication.Views[0],
                Is.SameAs(probe.Publications[0].ViewGraph!.Views[0]));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task StockPreparedViewNoncommitKeepsTheExactSourceGraphAndToken(bool storeRejects)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("stock-initial")).ConfigureAwait(false);
            WotCommittedPublicationState committed = m_coordinator.CommittedPublication;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            probe.BeforeDecisionAsync = async _ =>
            {
                entered.TrySetResult(true);
                await resume.Task.ConfigureAwait(false);
            };
            m_failDecision = storeRejects;
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(
                HandoffRequest("stock-noncommit", 1), cancellation.Token).AsTask();
            try
            {
                await AwaitStockDecisionAsync(pending, entered.Task).ConfigureAwait(false);
                Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
                await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
                Assert.That((await BrowseStockAsync(ResourceId(source), HasProjectionId()).ConfigureAwait(false))
                    .Select(reference => reference.NodeId), Is.EqualTo(new[] { new ExpandedNodeId(StockNode("Source")) }));
                Assert.That((await BrowseStockAsync(ResourceId(child), HasProjectionId()).ConfigureAwait(false))
                    .Select(reference => reference.NodeId), Is.EqualTo(new[] { new ExpandedNodeId(StockView("child")) }));
                Assert.That((await ReadNodeClassAsync(StockNode("Source/Other")).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(m_registry.Current.CanonicalViewGraphState,
                    Is.EqualTo(committed.RegistrySnapshot.CanonicalViewGraphState));
                if (!storeRejects)
                {
                    cancellation.Cancel();
                }
            }
            finally
            {
                resume.TrySetResult(true);
            }
            if (storeRejects)
            {
                await Assert.ThatAsync(() => pending,
                    Throws.TypeOf<WotRegistryCommitNotCommittedException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => pending,
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.CommittedPublication, Is.SameAs(committed));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            Assert.That(probe.Publications[1].IsCommitted, Is.False);
            m_failDecision = false;
            probe.BeforeDecisionAsync = null;

            WotRefreshResult retry = await m_coordinator.RefreshAsync(HandoffRequest("stock-retry", 1))
                .ConfigureAwait(false);

            Assert.That(retry.NewGeneration, Is.EqualTo(2u));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(84));
            await AssertStockMembershipAsync("child", 2, ["Reading", "Other"]).ConfigureAwait(false);
            Assert.That(probe.RuntimeCreatedCount - probe.RuntimeDisposedCount, Is.EqualTo(1));
        }

        [Test]
        [Platform("Win")]
        public async Task StockSharedChildPublishesBothAncestorTokensAndNoOpKeepsTheWholeImage()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            WotResource left = await AddStockViewAsync("left", true).ConfigureAwait(false);
            WotResource right = await AddStockViewAsync("right", true).ConfigureAwait(false);
            WotRefreshResult initial = await m_coordinator.RefreshAsync(HandoffRequest("stock-shared"))
                .ConfigureAwait(false);
            Assert.That(initial.Results.All(row => row.LoadState == WoTLoadStateEnum.Active), Is.True,
                string.Join("; ", initial.Results.Select(row => row.Message)));
            var leftBefore = await BrowseStockAsync(StockView("left"), Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            var rightBefore = await BrowseStockAsync(StockView("right"), Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            NodeId leftWrapper = ExpandedNodeId.ToNodeId(
                leftBefore.Single().NodeId, m_server.CurrentInstance.NamespaceUris);
            NodeId rightWrapper = ExpandedNodeId.ToNodeId(
                rightBefore.Single().NodeId, m_server.CurrentInstance.NamespaceUris);
            Assert.That(leftWrapper, Is.Not.EqualTo(rightWrapper));
            Assert.That(leftBefore.Single().NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(leftBefore.Single().TypeDefinition,
                Is.EqualTo(new ExpandedNodeId(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.WoTProjectionGroupType, m_server.CurrentInstance.NamespaceUris))));
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            WotRefreshRequest refresh = HandoffRequest("stock-shared-update", 1);
            refresh.Selection =
            [
                new WoTResourceSelectorDataType
                {
                    Kind = source.Kind, GroupId = source.GroupId, ResourceId = source.ResourceId
                }
            ];

            WotRefreshResult changed = await m_coordinator.RefreshAsync(refresh).ConfigureAwait(false);

            Assert.That(changed.NewGeneration, Is.EqualTo(2u));
            Assert.That(changed.Results.Select(row => row.Xid),
                Is.EquivalentTo(new[] { source.Xid, child.Xid, left.Xid, right.Xid }));
            Assert.That(probe.Publications[^1].ViewGraph!.AffectedResourceXids.ToArray(),
                Is.EquivalentTo(new[] { child.Xid, left.Xid, right.Xid }));
            await AssertStockMembershipAsync("child", 2, ["Reading", "Other"]).ConfigureAwait(false);
            foreach (string parent in new[] { "left", "right" })
            {
                Assert.That(await ReadStockViewVersionAsync(parent).ConfigureAwait(false), Is.EqualTo(2u));
                NodeId wrapper = parent == "left" ? leftWrapper : rightWrapper;
                Assert.That((await BrowseStockAsync(StockView(parent), Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false)).Select(reference => reference.NodeId),
                    Is.EqualTo(new[] { new ExpandedNodeId(wrapper) }));
                Assert.That((await BrowseStockAsync(wrapper, Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false)).Select(reference => reference.NodeId),
                    Is.EquivalentTo(new[]
                    {
                        new ExpandedNodeId(StockNode("Source/Reading")), new ExpandedNodeId(StockNode("Source/Other"))
                    }));
                ReferenceDescription root = (await BrowseStockAsync(wrapper, Ua.ReferenceTypeIds.HasProperty)
                    .ConfigureAwait(false)).Single();
                Assert.That(root.BrowseName, Is.EqualTo(new QualifiedName("ProjectionRoot",
                    (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(Namespaces.WotCon))));
                DataValue value = await m_session.ReadValueAsync(
                    ExpandedNodeId.ToNodeId(root.NodeId, m_server.CurrentInstance.NamespaceUris)).ConfigureAwait(false);
                Assert.That(value.WrappedValue.TryGetValue(out NodeId canonical), Is.True);
                Assert.That(canonical, Is.EqualTo(StockView("child")));
            }
            WotCommittedPublicationState before = m_coordinator.CommittedPublication;
            WotRefreshResult noOp = await m_coordinator.RefreshAsync(HandoffRequest("stock-no-op", 2))
                .ConfigureAwait(false);
            Assert.That(noOp.NewGeneration, Is.EqualTo(2u));
            Assert.That(noOp.Summary.Unchanged, Is.EqualTo(4u));
            Assert.That(probe.DecisionCount, Is.EqualTo(2));
            Assert.That(m_coordinator.CommittedPublication.Views.ToList(), Is.EqualTo(before.Views.ToList()));
            Assert.That(m_registry.Current.CanonicalViewGraphState,
                Is.EqualTo(before.RegistrySnapshot.CanonicalViewGraphState));
        }

        [Test]
        [Platform("Win")]
        public async Task StockPreparedPublicationIncludesOrdinarySourceAndViewNavigation()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest("stock-navigation"))
                .ConfigureAwait(false);
            Assert.That(result.Summary.Failed, Is.Zero);
            List<ReferenceDescription> sourceForward = await BrowseStockAsync(ResourceId(source), HasProjectionId())
                .ConfigureAwait(false);
            List<ReferenceDescription> sourceInverse = await BrowseStockAsync(
                StockNode("Source"), HasProjectionId(), BrowseDirection.Inverse).ConfigureAwait(false);
            List<ReferenceDescription> viewForward = await BrowseStockAsync(ResourceId(child), HasProjectionId())
                .ConfigureAwait(false);
            List<ReferenceDescription> viewInverse = await BrowseStockAsync(
                StockView("child"), HasProjectionId(), BrowseDirection.Inverse).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sourceForward.Select(reference =>
                    (reference.ReferenceTypeId, reference.IsForward, reference.NodeId, reference.NodeClass)),
                    Is.EqualTo(new[]
                    {
                        (HasProjectionId(), true, new ExpandedNodeId(StockNode("Source")), NodeClass.Object)
                    }));
                Assert.That(sourceInverse.Select(reference =>
                    (reference.ReferenceTypeId, reference.IsForward, reference.NodeId, reference.NodeClass)),
                    Is.EqualTo(new[]
                    {
                        (HasProjectionId(), false, new ExpandedNodeId(ResourceId(source)), NodeClass.Object)
                    }));
                Assert.That(viewForward.Select(reference =>
                    (reference.ReferenceTypeId, reference.IsForward, reference.NodeId, reference.NodeClass)),
                    Is.EqualTo(new[]
                    {
                        (HasProjectionId(), true, new ExpandedNodeId(StockView("child")), NodeClass.View)
                    }));
                Assert.That(viewInverse.Select(reference =>
                    (reference.ReferenceTypeId, reference.IsForward, reference.NodeId, reference.NodeClass)),
                    Is.EqualTo(new[]
                    {
                        (HasProjectionId(), false, new ExpandedNodeId(ResourceId(child)), NodeClass.Object)
                    }));
            });
        }

        [Test]
        [Platform("Win")]
        public async Task StockMembershipDigestReadsTheAuthoritativeCommittedGraph()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            List<ReferenceDescription> properties = await BrowseStockAsync(
                ResourceId(child), Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            var name = new QualifiedName("ProjectionMembershipDigest",
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(Namespaces.WotCon));
            Assert.That(properties.Count(reference => reference.BrowseName == name), Is.EqualTo(1),
                string.Join("; ", properties.Select(reference => $"{reference.BrowseName}={reference.NodeId}")));
            ReferenceDescription descriptor = properties.Single(reference => reference.BrowseName == name);
            Assert.That(descriptor.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(descriptor.TypeDefinition, Is.EqualTo(new ExpandedNodeId(Ua.VariableTypeIds.PropertyType)));
            NodeId property = ExpandedNodeId.ToNodeId(descriptor.NodeId, m_server.CurrentInstance.NamespaceUris);
            ReadResponse shape = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = property, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = property, AttributeId = Attributes.ValueRank },
                    new ReadValueId { NodeId = property, AttributeId = Attributes.AccessLevel }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(shape.Results.ToList().All(value => value.StatusCode == StatusCodes.Good), Is.True);
            Assert.That(shape.Results[0].WrappedValue.TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.ByteString));
            Assert.That(shape.Results[1].WrappedValue.TryGetValue(out int rank), Is.True);
            Assert.That(rank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(shape.Results[2].WrappedValue.TryGetValue(out byte access), Is.True);
            Assert.That(access, Is.EqualTo(AccessLevels.CurrentRead));
            List<ReferenceDescription> components = await BrowseStockAsync(
                ResourceId(child), Ua.ReferenceTypeIds.HasComponent).ConfigureAwait(false);
            Assert.That(components.Any(reference => reference.BrowseName.Name == "Versions"), Is.True,
                string.Join("; ", components.Select(reference => $"{reference.BrowseName}={reference.NodeId}")));
            ReferenceDescription folder = components.Single(reference => reference.BrowseName.Name == "Versions");
            List<ReferenceDescription> versions = await BrowseStockAsync(
                ExpandedNodeId.ToNodeId(folder.NodeId, m_server.CurrentInstance.NamespaceUris),
                Ua.ReferenceTypeIds.HierarchicalReferences, includeSubtypes: true).ConfigureAwait(false);
            Assert.That(versions, Has.Count.EqualTo(1),
                string.Join("; ", versions.Select(reference => $"{reference.BrowseName}={reference.NodeId}")));
            ReferenceDescription versionReference = versions.Single();
            NodeId version = ExpandedNodeId.ToNodeId(versionReference.NodeId, m_server.CurrentInstance.NamespaceUris);
            Assert.That((await BrowseStockAsync(version, Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false))
                .Any(reference => reference.BrowseName == name), Is.False);
            ReadResponse initial = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = property, AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);
            DataValue waiting = initial.Results[0];
            Assert.That(waiting.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));

            await m_coordinator.RefreshAsync(HandoffRequest("stock-digest")).ConfigureAwait(false);

            DataValue first = await m_session.ReadValueAsync(property).ConfigureAwait(false);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.WrappedValue.TryGetValue(out ByteString firstDigest), Is.True);
            Assert.That(firstDigest.Length, Is.EqualTo(32));
            Assert.That(firstDigest, Is.EqualTo(RestoreStockGraph(
                m_registry.Current.CanonicalViewGraphState, false).Views.ToList().Single().MembershipDigest));
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("stock-digest-update", 1)).ConfigureAwait(false);
            DataValue second = await m_session.ReadValueAsync(property).ConfigureAwait(false);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(second.WrappedValue.TryGetValue(out ByteString secondDigest), Is.True);
            Assert.That(secondDigest.Length, Is.EqualTo(32));
            Assert.That(secondDigest, Is.Not.EqualTo(firstDigest));
            Assert.That(secondDigest, Is.EqualTo(RestoreStockGraph(
                m_registry.Current.CanonicalViewGraphState, true).Views.ToList().Single().MembershipDigest));
        }

        [Test]
        [Platform("Win")]
        public async Task StockThingModelViewKeepsTypedSourceOwnershipAndCanonicalNavigation()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource model = await UpsertStockSourceAsync(false, thingModel: true).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false, thingModel: true).ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest("stock-tm"))
                .ConfigureAwait(false);

            Assert.That(result.Results.All(row => row.LoadState == WoTLoadStateEnum.Active), Is.True,
                string.Join("; ", result.Results.Select(row => row.Message)));
            Assert.That(result.Results.All(row => row.Kind == WoTDocumentKindEnum.ThingModel), Is.True);
            DataValue sourceClass = await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false);
            Assert.That(sourceClass.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(sourceClass.WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.ObjectType));
            Assert.That((await BrowseStockAsync(ResourceId(model), HasProjectionId()).ConfigureAwait(false))
                .Select(reference => (reference.NodeId, reference.NodeClass)),
                Is.EqualTo(new[] { (new ExpandedNodeId(StockNode("Source")), NodeClass.ObjectType) }));
            Assert.That((await BrowseStockAsync(ResourceId(child), HasProjectionId()).ConfigureAwait(false))
                .Select(reference => (reference.NodeId, reference.NodeClass)),
                Is.EqualTo(new[] { (new ExpandedNodeId(StockView("child")), NodeClass.View) }));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(1));
            Assert.That(m_coordinator.CommittedPublication.ActiveBindingPlans.ToList().Select(plan => plan.ResourceXid),
                Is.EqualTo(new[] { model.Xid }));
        }

        [TestCase(WoTAtomicityEnum.PerResource)]
        [TestCase(WoTAtomicityEnum.PerGroup)]
        [TestCase(WoTAtomicityEnum.PerClosure)]
        [TestCase(WoTAtomicityEnum.PerRegistry)]
        [Platform("Win")]
        public async Task InvalidStockViewCannotPublishItsPreparedSourcePeer(WoTAtomicityEnum atomicity)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kStockSourceNamespace);
            await AddStockViewAsync("child", false, collideWithSource: true).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            WotRefreshRequest request = HandoffRequest("stock-invalid-view");
            request.Options.Atomicity = atomicity;

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(request).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNodeIdExists))
                .ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_registry.Current.CanonicalViewGraphState.IsNull, Is.True);
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(probe.DecisionCount, Is.Zero);
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(1));
        }

        private async Task<HandoffProbe> ConfigureStockViewsAsync(LifecycleWotViewProjectionHost views)
        {
            HandoffProbe probe = ObserveHandoff(views, new StockViewSourceConverter());
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator), callerContext: null)
                .ConfigureAwait(false);
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kStockViewNamespace);
            return probe;
        }

        private async Task<WotResource> UpsertStockSourceAsync(
            bool changed, bool thingModel = false, bool numeric = false)
        {
            string other = changed
                ? ",\"Other\":{\"type\":\"integer\",\"forms\":[{\"href\":\"https://example.test/other\"}]}"
                : string.Empty;
            string role = thingModel ? "tm:ThingModel" : "uav:object";
            string rootId = numeric ? "i=100" : "s=Source";
            string readingId = numeric ? "\"uav:id\":\"nsu=urn:c2:prepared-source;i=101\"," : string.Empty;
            string content = $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/","tm":"https://www.w3.org/2019/wot/tm#"}],
                  "id":"urn:stock:source","title":"Source","@type":"{{{role}}}",
                  "uav:id":"nsu=urn:c2:prepared-source;{{{rootId}}}",
                  "properties":{
                    "Reading":{ {{{readingId}}}"type":"integer","forms":[{"href":"https://example.test/reading"}]}{{{other}}}
                  }
                }
                """;
            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = thingModel ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions,
                Kind = thingModel ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription,
                ResourceId = "stock-source",
                Format = thingModel ? "WoT-TM/1.1" : "WoT-TD/1.1",
                ContentType = thingModel ? "application/tm+json" : "application/td+json",
                VersionId = changed ? "v2" : "v1", Content = ByteString.From(Encoding.UTF8.GetBytes(content))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            return result.Resource!;
        }

        private async Task<WotResource> AddStockViewAsync(
            string id, bool parent, bool collideWithSource = false, bool thingModel = false)
        {
            string link = parent
                ? ",\"links\":[{\"rel\":\"ua:Organizes\",\"uav:refName\":\"Group\",\"href\":\"urn:stock:child\"}]"
                : string.Empty;
            string identity = collideWithSource
                ? "nsu=urn:c2:prepared-source;s=Source"
                : $"nsu={kStockViewNamespace};s={id}";
            string kind = thingModel ? "ThingModel" : "ThingDescription";
            string contentType = thingModel ? "application/tm+json" : "application/td+json";
            string content = $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                  "@type":["uav:projection"],"uav:projectionKind":"{{{kind}}}",
                  "id":"urn:stock:{{{id}}}","title":"{{{id}}}","uav:scenario":"urn:stock:scenario",
                  "uav:id":"{{{identity}}}",
                  "securityDefinitions":{"none":{"scheme":"nosec"}},"security":"none",
                  "uav:projects":[{"uav:sourceName":"source","href":"urn:stock:source","type":"{{{contentType}}}",
                    "uav:routing":"source","uav:selectAll":{{{(parent ? "false" : "true")}}}}]{{{link}}}
                }
                """;
            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = thingModel ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions,
                Kind = thingModel ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription,
                ResourceId = id, VersionId = "v1",
                Format = "WoT-Projection/1.2",
                ContentType =
                    "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"",
                Content = ByteString.From(Encoding.UTF8.GetBytes(content))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            return result.Resource!;
        }

        private ValueTask AwaitStockRegistryProjectionAsync()
        {
            WotRegistryNodeManager manager = m_server.NodeManagerLifecycle.Registrations.ToList()
                .Select(registration => registration.NodeManager).OfType<WotRegistryNodeManager>()
                .Single(candidate => ReferenceEquals(candidate.Registry, m_registry));
            return manager.DispatchProjectionAsync(_ => default, CancellationToken.None);
        }

        private static async Task AwaitStockDecisionAsync(Task<WotRefreshResult> pending, Task entered)
        {
            Task first = await Task.WhenAny(pending, entered).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            if (ReferenceEquals(first, pending))
            {
                WotRefreshResult failed = await pending.ConfigureAwait(false);
                Assert.Fail("The unit did not reach its decision: " +
                    string.Join("; ", failed.Results.Select(row => row.Message)));
            }
        }

        private async Task<int> ReadStockValueAsync(string name)
        {
            DataValue value = await m_session.ReadValueAsync(StockNode("Source/" + name)).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int reading), Is.True);
            return reading;
        }

        private async Task<uint> ReadStockViewVersionAsync(string name)
        {
            ReferenceDescription property = (await BrowseStockAsync(StockView(name), Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false)).Single(reference => reference.BrowseName == new QualifiedName("ViewVersion"));
            DataValue value = await m_session.ReadValueAsync(
                ExpandedNodeId.ToNodeId(property.NodeId, m_server.CurrentInstance.NamespaceUris)).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out uint version), Is.True);
            return version;
        }

        private async Task AssertStockMembershipAsync(string name, uint version, ArrayOf<string> members)
        {
            Assert.That(await ReadStockViewVersionAsync(name).ConfigureAwait(false), Is.EqualTo(version));
            BrowseResponse response = await m_session.BrowseAsync(null,
                new ViewDescription { ViewId = StockView(name), ViewVersion = version }, 0,
                [new BrowseDescription
                {
                    NodeId = StockView(name), ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                    BrowseDirection = BrowseDirection.Forward, IncludeSubtypes = false,
                    ResultMask = (uint)BrowseResultMask.All
                }], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].References.ToArray()!.Select(reference =>
                (reference.ReferenceTypeId, reference.IsForward, reference.NodeId,
                    reference.NodeClass, reference.BrowseName)),
                Is.EquivalentTo(members.ToList().Select(member =>
                    (Ua.ReferenceTypeIds.Organizes, true, new ExpandedNodeId(StockNode("Source/" + member)),
                        NodeClass.Variable, new QualifiedName(member, StockNode("Source").NamespaceIndex)))));
        }

        private async Task<List<ReferenceDescription>> BrowseStockAsync(
            NodeId node, NodeId type, BrowseDirection direction = BrowseDirection.Forward, bool includeSubtypes = false)
        {
            BrowseResponse response = await m_session.BrowseAsync(null, new ViewDescription(), 0,
                [new BrowseDescription
                {
                    NodeId = node, ReferenceTypeId = type, BrowseDirection = direction,
                    IncludeSubtypes = includeSubtypes, ResultMask = (uint)BrowseResultMask.All
                }], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return response.Results[0].References.ToList();
        }

        private WotCanonicalViewState RestoreStockGraph(ByteString payload, bool changed)
        {
            var sources = new List<WotCanonicalViewSource> { new(StockNode("Source/Reading"), NodeClass.Variable) };
            if (changed)
            {
                sources.Add(new WotCanonicalViewSource(StockNode("Source/Other"), NodeClass.Variable));
            }
            return WotCanonicalViewState.Restore(payload, new WotCanonicalViewGraphContext(
                m_server.CurrentInstance.ServerUris.GetString(0)!, Namespaces.WotCon,
                m_server.CurrentInstance.NamespaceUris, sources.ToArrayOf()));
        }

        private NodeId ResourceId(WotResource resource)
        {
            return new NodeId($"WoTRegistry/groups/{resource.GroupId}/resources/{resource.ResourceId}",
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(Namespaces.WotCon));
        }

        private NodeId HasProjectionId()
        {
            return ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, m_server.CurrentInstance.NamespaceUris);
        }

        private NodeId StockNode(string name)
        {
            return new NodeId(name, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kStockSourceNamespace));
        }

        private NodeId StockView(string name)
        {
            return new NodeId(name, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kStockViewNamespace));
        }

        private sealed class StockViewSourceConverter : IWotDocumentConverter
        {
            public async ValueTask<WotConversionOutput> ConvertAsync(
                WotResource resource, ByteString content, WotRegistrySnapshot snapshot,
                IReadOnlyDictionary<string, ByteString> contents, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using JsonDocument document = JsonDocument.Parse(content.Memory);
                bool numeric = document.RootElement.GetProperty("uav:id").GetString() ==
                    "nsu=urn:c2:prepared-source;i=100";
                bool changed = document.RootElement.GetProperty("properties").TryGetProperty("Other", out _);
                string other = changed ? Variable("Other", 7) : string.Empty;
                string root = resource.Kind == WoTDocumentKindEnum.ThingModel
                    ? """
                      <UAObjectType NodeId="ns=1;s=Source" BrowseName="1:Source">
                        <DisplayName>Source</DisplayName><References>
                          <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                        </References>
                      </UAObjectType>
                      """
                    : """
                      <UAObject NodeId="ns=1;s=Source" BrowseName="1:Source">
                        <DisplayName>Source</DisplayName><References>
                          <Reference ReferenceType="i=40">i=58</Reference>
                        </References>
                      </UAObject>
                      """;
                string xml = $$"""
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                      <NamespaceUris><Uri>{{kStockSourceNamespace}}</Uri></NamespaceUris>
                      <Models><Model ModelUri="{{kStockSourceNamespace}}" Version="1.0.0"
                        PublicationDate="2026-01-01T00:00:00Z" /></Models>
                      {{root}}
                      {{Variable("Reading", changed ? 84 : 42)}}{{other}}
                    </UANodeSet>
                    """;
                if (numeric)
                {
                    xml = xml.Replace("ns=1;s=Source/Reading", "ns=1;i=101", StringComparison.Ordinal)
                        .Replace("ns=1;s=Source/Other", "ns=1;i=102", StringComparison.Ordinal)
                        .Replace("ns=1;s=Source", "ns=1;i=100", StringComparison.Ordinal);
                }
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return await new ValueTask<WotConversionOutput>(new WotConversionOutput(
                    UANodeSet.Read(stream)!, [], numeric
                        ? new ExpandedNodeId(100u, kStockSourceNamespace)
                        : new ExpandedNodeId("Source", kStockSourceNamespace)))
                    .ConfigureAwait(false);
            }

            private static string Variable(string name, int value)
            {
                return $$"""
                    <UAVariable NodeId="ns=1;s=Source/{{name}}" BrowseName="1:{{name}}" DataType="i=6"
                      AccessLevel="1" UserAccessLevel="1" ParentNodeId="ns=1;s=Source">
                      <DisplayName>{{name}}</DisplayName><References>
                        <Reference ReferenceType="i=47" IsForward="false">ns=1;s=Source</Reference>
                        <Reference ReferenceType="i=40">i=63</Reference>
                      </References>
                      <Value><Int32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">{{value}}</Int32></Value>
                    </UAVariable>
                    """;
            }
        }

        private const string kStockSourceNamespace = "urn:c2:prepared-source";
        private const string kStockViewNamespace = "urn:c2:prepared-views";
    }
}
