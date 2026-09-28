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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises how the materialization coordinator wires a projection document
    /// (WoT Binding Section 12, WoT Connectivity Section 7.13) into the
    /// address-space pipeline: it is stored and refreshed as an ordinary
    /// resource, its <c>uav:projects</c> sources are registry dependencies, it
    /// is deferred past its sources and materialized as a View through the view
    /// host rather than as affordance Nodes, and a cyclic projection graph is
    /// rejected at dependency resolution.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionViewCoordinatorTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_runtime = await PreparedWotTestRuntime.StartAsync().ConfigureAwait(false);
            bool initialized = false;
            try
            {
                m_registry = await m_runtime.CreateRegistryAsync(
                    compatibilityMode: WotProjectionCompatibilityMode.DraftProjection11).ConfigureAwait(false);
                m_host = new FakeWotProjectionHost();
                m_converter = new ViewSourceConverter();
                m_viewHost = new LifecycleWotViewProjectionHost(m_runtime.Lifecycle);
                m_coordinator = new WotMaterializationCoordinator(
                    m_registry, m_runtime.Observe(m_host.RecordCommitted),
                    converterOptions: new WotNodeSetConverterOptions
                    {
                        ProjectionCompatibilityMode = WotProjectionCompatibilityMode.DraftProjection11
                    },
                    documentConverter: m_converter,
                    viewProjectionHost: m_viewHost)
                {
                    ServerNamespaceUris = m_runtime.Namespaces
                };
                NodeManagerRegistration registration = await m_runtime.Lifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(
                        new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                    callerContext: null).ConfigureAwait(false);
                m_registryManager = (WotRegistryNodeManager)registration.NodeManager;
                initialized = true;
            }
            finally
            {
                if (!initialized)
                {
                    await TearDownAsync().ConfigureAwait(false);
                }
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                m_coordinator?.Dispose();
            }
            finally
            {
                m_coordinator = null!;
                try
                {
                    m_viewHost?.Dispose();
                }
                finally
                {
                    m_viewHost = null!;
                    try
                    {
                        if (m_runtime is not null)
                        {
                            await m_runtime.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        m_runtime = null!;
                    }
                }
            }
        }

        [Test]
        public async Task ProjectionDocumentMaterializesAViewAndCreatesNoAffordanceSource()
        {
            await RegisterTd("src-1", TestMaterialization.Td("urn:src-1")).ConfigureAwait(false);
            await RegisterTd("view-1",
                Projection("urn:view:1", "http://example.com/scenario/One", "urn:src-1")).ConfigureAwait(false);

            WotRefreshResult result = await RefreshAsync().ConfigureAwait(false);

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "The projection and its source share one runtime closure.");
            HostOperation add = m_host.Operations.Single(o => o.Op == "add");
            Assert.That(add.SourceNames, Has.Count.EqualTo(1),
                "The projection document must not be projected as an affordance source; " +
                "only its one real source is materialized as Nodes.");

            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.EqualTo(1),
                "The projection document must be materialized as a View.");
            WotViewProjectionHandle view = m_coordinator.CommittedPublication.Views[0];
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-1");
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(projection.RootNodeId, Is.EqualTo(view.ViewNodeId),
                "RootNodeId must be the View Node's NodeId.");
        }

        [Test]
        public async Task ProjectionViewRootNodeIdIsDistinctFromTheResourceNode()
        {
            await RegisterTd("src-2", TestMaterialization.Td("urn:src-2")).ConfigureAwait(false);
            await RegisterTd("view-2",
                Projection("urn:view:2", "http://example.com/scenario/Two", "urn:src-2")).ConfigureAwait(false);

            await RefreshAsync().ConfigureAwait(false);

            WotCanonicalViewPublication view = ViewFor("view-2");
            Assert.That(view.ResourceNodeId.IsNull, Is.False);
            Assert.That(view.ViewNodeId.IsNull, Is.False);
            Assert.That(view.ViewNodeId, Is.Not.EqualTo(view.ResourceNodeId));
            NodeId viewId = ExpandedNodeId.ToNodeId(view.ViewNodeId, m_runtime.Namespaces);
            NodeId resourceId = ExpandedNodeId.ToNodeId(view.ResourceNodeId, m_runtime.Namespaces);
            Assert.That(viewId.IdentifierAsString,
                Does.EndWith("/View"));
            Assert.That(viewId.IdentifierAsString,
                Does.StartWith(resourceId.IdentifierAsString));
        }

        /// <summary>
        /// Replacing a source closure retains one committed canonical View
        /// for its Resource instead of retiring the replacement with the old owner.
        /// </summary>
        [Test]
        public async Task RefreshingAProjectionLeavesExactlyOneAppliedView()
        {
            await RegisterTd("src-r", TestMaterialization.Td("urn:src-r")).ConfigureAwait(false);
            await RegisterTd("view-r",
                Projection("urn:view:r", "http://example.com/scenario/R", "urn:src-r")).ConfigureAwait(false);

            await RefreshAsync().ConfigureAwait(false);
            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.EqualTo(1),
                "The first refresh must apply the View.");
            WotViewProjectionHandle initial = m_coordinator.CommittedPublication.Views[0];

            await RegisterTd("src-r", TestMaterialization.Td("urn:src-r", "Changed")).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);

            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.EqualTo(1),
                "Re-materializing must leave the replacement View applied, not remove it.");
            WotViewProjectionHandle replacement = m_coordinator.CommittedPublication.Views[0];
            Assert.That(replacement.ResourceXid, Is.EqualTo(initial.ResourceXid));
            Assert.That(replacement.ViewNodeId, Is.EqualTo(initial.ViewNodeId));
            Assert.That(ViewFor("view-r").Active, Is.True);
        }

        [Test]
        public async Task ProjectionMaterializedNodeCountCoversOnlyTheView()
        {
            await RegisterTd("src-3", TestMaterialization.Td("urn:src-3")).ConfigureAwait(false);
            await RegisterTd("view-3",
                Projection("urn:view:3", "http://example.com/scenario/Three", "urn:src-3")).ConfigureAwait(false);

            WotRefreshResult result = await RefreshAsync().ConfigureAwait(false);

            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-3");
            // A projection with no organizing links materializes exactly one Node:
            // the View. The organized source Nodes are never counted.
            Assert.That(projection.MaterializedNodeCount, Is.EqualTo(1u));
        }

        [Test]
        public async Task OutOfAddressSpaceSelectionIsOmittedAndReportedButStaysActive()
        {
            await RegisterTd("src-4", TestMaterialization.Td("urn:src-4")).ConfigureAwait(false);
            await RegisterTd("view-4",
                Projection("urn:view:4", "http://example.com/scenario/Four", "urn:src-4")).ConfigureAwait(false);

            WotRefreshResult result = await RefreshAsync().ConfigureAwait(false);

            // The fake source Nodes carry no portable uav:id and a non-string
            // root, so the selected affordance cannot be located in this address
            // space and is omitted; the load nonetheless reaches Active.
            WotCanonicalViewPublication view = ViewFor("view-4");
            int omittedCount = view.Omissions.Count;
            Assert.That(omittedCount, Is.EqualTo(1),
                "The unlocatable selection must be recorded as an omission.");
            Assert.That(view.Membership.Count, Is.Zero);
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-4");
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning),
                "A View that selected a member but organized none must not report plain Success.");
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active),
                "Omission is not a failure; the resource still reaches Active.");
            Assert.That(projection.Message, Does.Contain("Affordance 'value'"));
            Assert.That(projection.Message, Does.Contain("urn:src-4"));
            Assert.That(projection.Message, Does.Contain("omitted"),
                "The omission must be reported in the load-result Message.");
        }

        [Test]
        public async Task ProjectionViewWithSomeSelectionsOmittedReportsWarning()
        {
            await RegisterMappedTd("src-partial-a", "urn:src-partial-a", "PartialSourceA", "valueA")
                .ConfigureAwait(false);
            await RegisterTd("src-partial-b", Td("urn:src-partial-b", "valueB")).ConfigureAwait(false);
            await RegisterTd("view-partial",
                Projection(
                    "urn:view:partial",
                    "http://example.com/scenario/Partial",
                    "urn:src-partial-a",
                    "urn:src-partial-b")).ConfigureAwait(false);

            WotRefreshResult result = await RefreshAsync().ConfigureAwait(false);

            WotCanonicalViewPublication view = ViewFor("view-partial");
            Assert.That(view.Membership.Count, Is.EqualTo(1));
            Assert.That(view.Membership[0], Is.EqualTo(new ExpandedNodeId(
                "PartialSourceA/valueA", $"urn:wot:{WotRegistryGroups.ThingDescriptions}/src-partial-a")));
            Assert.That(view.Omissions.Count, Is.EqualTo(1));
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-partial");
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(projection.Message, Does.Contain("Affordance 'valueB'"));
            Assert.That(projection.Message, Does.Contain("omitted"));
            Assert.That(projection.Message, Does.Contain("urn:src-partial-b"));
        }

        [Test]
        public async Task ProjectionViewWithAllSelectionsMaterializedReportsSuccess()
        {
            await RegisterMappedTd("src-clean", "urn:src-clean", "CleanSource", "value").ConfigureAwait(false);
            await RegisterTd("view-clean",
                Projection(
                    "urn:view:clean",
                    "http://example.com/scenario/Clean",
                    "urn:src-clean")).ConfigureAwait(false);

            WotRefreshResult result = await RefreshAsync().ConfigureAwait(false);

            WotCanonicalViewPublication view = ViewFor("view-clean");
            Assert.That(view.Membership.Count, Is.EqualTo(1));
            Assert.That(view.Membership[0], Is.EqualTo(new ExpandedNodeId(
                "CleanSource/value", $"urn:wot:{WotRegistryGroups.ThingDescriptions}/src-clean")));
            Assert.That(view.Omissions.Count, Is.Zero);
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-clean");
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(projection.Message, Does.Contain("organizing 1 Node(s)"));
            Assert.That(projection.Message, Does.Not.Contain("omitted"));
        }

        [Test]
        public async Task GeneratedFallbackOutsideTheCapturedSourceIsOmitted()
        {
            await RegisterMappedTd(
                "src-fallback", "urn:src-fallback", "FallbackSource", "value", explicitAffordanceId: false)
                .ConfigureAwait(false);
            await RegisterTd("view-fallback",
                Projection("urn:view:fallback", "urn:scenario:fallback", "urn:src-fallback")).ConfigureAwait(false);

            WotRefreshResult result = await RefreshAsync().ConfigureAwait(false);

            WotCanonicalViewPublication view = ViewFor("view-fallback");
            WoTResourceLoadResultDataType row = result.Results.Single(value => value.ResourceId == "view-fallback");
            Assert.That(view.Active, Is.True);
            Assert.That(view.Membership.Count, Is.Zero);
            Assert.That(view.Omissions.Count, Is.EqualTo(1));
            Assert.That(row.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(row.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(row.RootNodeId, Is.EqualTo(m_coordinator.CommittedPublication.Views[0].ViewNodeId));
            Assert.That(m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "src-fallback")!.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
        }

        [Test]
        public async Task CyclicProjectionGraphIsRejectedAtDependencyResolution()
        {
            await RegisterTd("cyc-a",
                Projection("urn:view:cyc-a", "http://example.com/scenario/A", "urn:view:cyc-b")).ConfigureAwait(false);
            await RegisterTd("cyc-b",
                Projection("urn:view:cyc-b", "http://example.com/scenario/B", "urn:view:cyc-a")).ConfigureAwait(false);

            WotRefreshResult result = await RefreshAsync().ConfigureAwait(false);

            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.Zero,
                "A cyclic projection graph must not materialize any View.");
            Assert.That(m_host.AddCount, Is.Zero);
            WoTResourceLoadResultDataType[] cyclic = [.. result.Results.Where(r => r.ResourceId is "cyc-a" or "cyc-b")];
            Assert.That(cyclic, Has.Length.EqualTo(2));
            Assert.That(
                cyclic.All(r => r.Outcome == WoTOutcomeEnum.Failed &&
                    r.Phase == WoTPhaseEnum.DependencyResolution),
                Is.True,
                "A cyclic projection graph is rejected at Phase = DependencyResolution.");
        }

        [Test]
        public async Task ProjectionViewIsRemovedWhenTheProjectionResourceIsDeleted()
        {
            await RegisterTd("src-5", TestMaterialization.Td("urn:src-5")).ConfigureAwait(false);
            await RegisterTd("view-5",
                Projection("urn:view:5", "http://example.com/scenario/Five", "urn:src-5")).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.EqualTo(1));

            await m_coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "view-5",
                Policy = WoTDeletePolicyEnum.Reject
            }).ConfigureAwait(false);

            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.Zero,
                "Deleting the Resource must remove the materialized View.");
            Assert.That(m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "view-5"), Is.Null);
            Assert.That(m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "src-5")!.LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
        }

        [Test]
        public async Task RetiredNestedProjectionIsNotReactivatedAsADependency()
        {
            await RegisterTd("src-retire", TestMaterialization.Td("urn:src-retire")).ConfigureAwait(false);
            await RegisterTd(
                "view-inner",
                Projection(
                    "urn:view:inner-retire",
                    "http://example.com/scenario/InnerRetire",
                    "urn:src-retire")).ConfigureAwait(false);
            await RegisterTd(
                "view-outer",
                Projection(
                    "urn:view:outer-retire",
                    "http://example.com/scenario/OuterRetire",
                    "urn:view:inner-retire")).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.EqualTo(2));

            WotDeleteOutcome outcome = await m_coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "view-inner",
                Policy = WoTDeletePolicyEnum.Retire
            }).ConfigureAwait(false);

            WotResource inner = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "view-inner")!;
            WotResource outer = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "view-outer")!;
            WoTResourceLoadResultDataType retired =
                outcome.Results.Single(result => result.ResourceId == "view-inner");
            Assert.Multiple(() =>
            {
                Assert.That(outcome.Delete.Retired, Is.True);
                Assert.That(outcome.Summary.Retired, Is.EqualTo(1));
                Assert.That(inner.Enabled, Is.False);
                Assert.That(inner.LoadState, Is.EqualTo(WoTLoadStateEnum.Retired));
                Assert.That(inner.ActiveVersionId, Is.Null);
                Assert.That(inner.RootNodeId.IsNull, Is.True);
                Assert.That(inner.MaterializedNodeCount, Is.Zero);
                Assert.That(outer.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.EqualTo(1));
                Assert.That(
                    m_coordinator.CommittedPublication.Views[0].ResourceXid,
                    Does.EndWith("/view-outer"));
                Assert.That(retired.LoadState, Is.EqualTo(WoTLoadStateEnum.Retired));
                Assert.That(retired.Generation, Is.EqualTo(outcome.Generation));
                Assert.That(retired.RootNodeId.IsNull, Is.True);
                Assert.That(retired.MaterializedNodeCount, Is.Zero);
                Assert.That(ViewFor("view-inner").Active, Is.False);
            });

            WotRefreshResult repeated = await RefreshAsync().ConfigureAwait(false);
            Assert.That(repeated.NewGeneration, Is.EqualTo(outcome.Generation));
            Assert.That(repeated.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            Assert.That(m_registry.Current.FindResourceByXid(inner.Xid)!.LoadState,
                Is.EqualTo(WoTLoadStateEnum.Retired));
            Assert.That(m_coordinator.CommittedPublication.Views.Count, Is.EqualTo(1));
        }

        private async Task<WotRegistryMutationResult> RegisterTd(string resourceId, byte[] content)
        {
            using var document = WotDocument.Parse(content);
            bool projection = WotProjection.IsProjection(document);
            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Format = projection ? WotProjection.Format : "WoT-TD/1.1",
                ContentType = projection ? WotProjection.ContentType : "application/td+json",
                Content = ByteString.From(content)
            }).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
            await m_registryManager.DispatchProjectionAsync(_ => default, CancellationToken.None).ConfigureAwait(false);
            return result;
        }

        private async Task RegisterMappedTd(
            string resourceId, string id, string rootIdentifier, string propertyName, bool explicitAffordanceId = true)
        {
            string modelUri = $"urn:wot:{WotRegistryGroups.ThingDescriptions}/{resourceId}";
            string affordanceId = explicitAffordanceId
                ? $"\"uav:id\":\"nsu={modelUri};s={rootIdentifier}/{propertyName}\","
                : string.Empty;
            m_converter.UseMappedSource(resourceId);
            await RegisterTd(resourceId, Encoding.UTF8.GetBytes($$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                  "@type":"uav:object","id":"{{id}}","title":"{{rootIdentifier}}",
                  "uav:id":"nsu={{modelUri}};s={{rootIdentifier}}",
                  "properties":{
                    "{{propertyName}}":{
                      {{affordanceId}}
                      "type":"number","forms":[{"href":"https://example.test/reading"}]
                    }
                  }
                }
                """)).ConfigureAwait(false);
        }

        private ValueTask<WotRefreshResult> RefreshAsync()
        {
            return m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            });
        }

        private WotCanonicalViewPublication ViewFor(string resourceId)
        {
            WotResource resource = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, resourceId)!;
            return WotCanonicalViewState.Parse(m_registry.Current.CanonicalViewGraphState)
                .Views.ToList().Single(view => view.ResourceXid == resource.Xid);
        }

        private static byte[] Projection(string id, string scenario, params string[] projectHrefs)
        {
            var builder = new StringBuilder();
            builder.Append("{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",")
                .Append("{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\",")
                .Append("\"tm\":\"https://www.w3.org/2019/wot/tm#\"}],")
                .Append("\"@type\":[\"Thing\",\"uav:projection\"],")
                .Append("\"id\":\"").Append(id).Append("\",")
                .Append("\"title\":\"").Append(id).Append("\",")
                .Append("\"uav:scenario\":\"").Append(scenario).Append("\",")
                .Append("\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}},")
                .Append("\"security\":\"nosec_sc\",")
                .Append("\"uav:projects\":[");
            for (int i = 0; i < projectHrefs.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"uav:sourceName\":\"s").Append(i).Append("\",")
                    .Append("\"href\":\"").Append(projectHrefs[i]).Append("\",")
                    .Append("\"type\":\"application/td+json\",")
                    .Append("\"uav:routing\":\"source\",")
                    .Append("\"uav:selectAll\":true}");
            }
            builder.Append("]}");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static byte[] Td(string id, string propertyName)
        {
            var builder = new StringBuilder();
            builder.Append("{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",")
                .Append("\"@type\":\"uav:object\",")
                .Append("\"id\":\"").Append(id).Append("\",")
                .Append("\"title\":\"").Append(id).Append("\",")
                .Append("\"properties\":{\"").Append(propertyName)
                .Append("\":{\"type\":\"number\",\"forms\":[{\"href\":\"x\"}]}}}");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private sealed class ViewSourceConverter : IWotDocumentConverter
        {
            public void UseMappedSource(string resourceId)
            {
                m_mapped.Add(resourceId);
            }

            public ValueTask<WotConversionOutput> ConvertAsync(
                WotResource resource,
                ByteString content,
                WotRegistrySnapshot snapshot,
                IReadOnlyDictionary<string, ByteString> contents,
                CancellationToken cancellationToken)
            {
                return (m_mapped.Contains(resource.ResourceId) ? m_real : m_unmapped).ConvertAsync(
                    resource, content, snapshot, contents, cancellationToken);
            }

            private readonly HashSet<string> m_mapped = new(StringComparer.Ordinal);
            private readonly IWotDocumentConverter m_real = new WotNodeSetDocumentConverter();
            private readonly IWotDocumentConverter m_unmapped = new FakeWotDocumentConverter();
        }

        private PreparedWotTestRuntime m_runtime = null!;
        private WotRegistryService m_registry = null!;
        private WotRegistryNodeManager m_registryManager = null!;
        private FakeWotProjectionHost m_host = null!;
        private ViewSourceConverter m_converter = null!;
        private LifecycleWotViewProjectionHost m_viewHost = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
    }
}
