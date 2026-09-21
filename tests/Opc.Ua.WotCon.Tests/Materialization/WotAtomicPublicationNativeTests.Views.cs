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
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
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

        private async Task<HandoffProbe> ConfigureStockViewsAsync(LifecycleWotViewProjectionHost views)
        {
            HandoffProbe probe = ObserveHandoff(views, new StockViewSourceConverter());
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator), callerContext: null)
                .ConfigureAwait(false);
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kStockViewNamespace);
            return probe;
        }

        private async Task<WotResource> UpsertStockSourceAsync(bool changed)
        {
            string other = changed
                ? ",\"Other\":{\"type\":\"integer\",\"forms\":[{\"href\":\"https://example.test/other\"}]}"
                : string.Empty;
            string content = $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                  "id":"urn:stock:source","title":"Source","@type":"uav:object",
                  "uav:id":"nsu=urn:c2:prepared-source;s=Source",
                  "properties":{
                    "Reading":{"type":"integer","forms":[{"href":"https://example.test/reading"}]}{{{other}}}
                  }
                }
                """;
            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions, ResourceId = "stock-source",
                VersionId = changed ? "v2" : "v1", Content = ByteString.From(Encoding.UTF8.GetBytes(content))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            return result.Resource!;
        }

        private async Task<WotResource> AddStockViewAsync(string id, bool parent)
        {
            string link = parent
                ? ",\"links\":[{\"rel\":\"ua:Organizes\",\"uav:refName\":\"Group\",\"href\":\"urn:stock:child\"}]"
                : string.Empty;
            string content = $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                  "@type":["uav:projection"],"uav:projectionKind":"ThingDescription",
                  "id":"urn:stock:{{{id}}}","title":"{{{id}}}","uav:scenario":"urn:stock:scenario",
                  "uav:id":"nsu=urn:c2:prepared-views;s={{{id}}}",
                  "securityDefinitions":{"none":{"scheme":"nosec"}},"security":"none",
                  "uav:projects":[{"uav:sourceName":"source","href":"urn:stock:source","type":"application/td+json",
                    "uav:routing":"source","uav:selectAll":{{{(parent ? "false" : "true")}}}}]{{{link}}}
                }
                """;
            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions, ResourceId = id, VersionId = "v1",
                Format = "WoT-Projection/1.2",
                ContentType =
                    "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"",
                Content = ByteString.From(Encoding.UTF8.GetBytes(content))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            return result.Resource!;
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

        private async Task<List<ReferenceDescription>> BrowseStockAsync(NodeId node, NodeId type)
        {
            BrowseResponse response = await m_session.BrowseAsync(null, new ViewDescription(), 0,
                [new BrowseDescription
                {
                    NodeId = node, ReferenceTypeId = type, BrowseDirection = BrowseDirection.Forward,
                    IncludeSubtypes = false, ResultMask = (uint)BrowseResultMask.All
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
                bool changed = document.RootElement.GetProperty("properties").TryGetProperty("Other", out _);
                string other = changed ? Variable("Other", 7) : string.Empty;
                string xml = $$"""
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                      <NamespaceUris><Uri>{{kStockSourceNamespace}}</Uri></NamespaceUris>
                      <Models><Model ModelUri="{{kStockSourceNamespace}}" Version="1.0.0"
                        PublicationDate="2026-01-01T00:00:00Z" /></Models>
                      <UAObject NodeId="ns=1;s=Source" BrowseName="1:Source">
                        <DisplayName>Source</DisplayName><References>
                          <Reference ReferenceType="i=40">i=58</Reference>
                        </References>
                      </UAObject>
                      {{Variable("Reading", changed ? 84 : 42)}}{{other}}
                    </UANodeSet>
                    """;
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                return await new ValueTask<WotConversionOutput>(new WotConversionOutput(
                    UANodeSet.Read(stream)!, [], new ExpandedNodeId("Source", kStockSourceNamespace)))
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
