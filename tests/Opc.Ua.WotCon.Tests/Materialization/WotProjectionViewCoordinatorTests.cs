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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
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
        private WotRegistryService m_registry = null!;
        private FakeWotProjectionHost m_host = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private InMemoryWotViewProjectionHost m_viewHost = null!;
        private WotMaterializationCoordinator m_coordinator = null!;

        [SetUp]
        public void SetUp()
        {
            m_registry = new WotRegistryService();
            m_host = new FakeWotProjectionHost();
            m_converter = new FakeWotDocumentConverter();
            m_viewHost = new InMemoryWotViewProjectionHost();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, m_host,
                documentConverter: m_converter,
                viewProjectionHost: m_viewHost);
        }

        [TearDown]
        public void TearDown()
        {
            m_coordinator.Dispose();
            m_registry.Dispose();
        }

        [Test]
        public async Task ProjectionDocumentMaterializesAViewAndCreatesNoAffordanceSource()
        {
            await RegisterTd("src-1", TestMaterialization.Td("urn:src-1"));
            await RegisterTd("view-1",
                Projection("urn:view:1", "http://example.com/scenario/One", "urn:src-1"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "The projection and its source share one runtime closure.");
            HostOperation add = m_host.Operations.Single(o => o.Op == "add");
            Assert.That(add.SourceNames, Has.Count.EqualTo(1),
                "The projection document must not be projected as an affordance source; " +
                "only its one real source is materialized as Nodes.");

            Assert.That(m_viewHost.Applied, Has.Count.EqualTo(1),
                "The projection document must be materialized as a View.");
            WotViewProjectionRequest request = m_viewHost.Applied.Single();
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-1");
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(projection.RootNodeId, Is.EqualTo(request.ViewNodeId),
                "RootNodeId must be the View Node's NodeId.");
        }

        [Test]
        public async Task ProjectionViewRootNodeIdIsDistinctFromTheResourceNode()
        {
            await RegisterTd("src-2", TestMaterialization.Td("urn:src-2"));
            await RegisterTd("view-2",
                Projection("urn:view:2", "http://example.com/scenario/Two", "urn:src-2"));

            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WotViewProjectionRequest request = m_viewHost.Applied.Single();
            // HasWoTProjection runs from the resource Node to the View Node; the
            // two endpoints must be distinct and the View id carries the /View
            // suffix so a client can navigate WoTProjectionOf back.
            Assert.That(request.ResourceNodeId.IsNull, Is.False);
            Assert.That(request.ViewNodeId.IsNull, Is.False);
            Assert.That(request.ViewNodeId, Is.Not.EqualTo(request.ResourceNodeId));
            Assert.That(request.ViewNodeId.IdentifierAsString,
                Does.EndWith("/View"));
            Assert.That(request.ViewNodeId.IdentifierAsString,
                Does.StartWith(request.ResourceNodeId.IdentifierAsString));
        }

        /// <summary>
        /// A refresh applies the replacement View before retiring the handle it
        /// supersedes, and both carry the same resource Xid. A host that removed
        /// by Xid alone would delete the View that had just been applied, so the
        /// default in-memory host must stay consistent across a re-materialization.
        /// </summary>
        [Test]
        public async Task RefreshingAProjectionLeavesExactlyOneAppliedView()
        {
            await RegisterTd("src-r", TestMaterialization.Td("urn:src-r"));
            await RegisterTd("view-r",
                Projection("urn:view:r", "http://example.com/scenario/R", "urn:src-r"));

            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_viewHost.Applied, Has.Count.EqualTo(1),
                "The first refresh must apply the View.");

            await RegisterTd("src-r", TestMaterialization.Td("urn:src-r", "Changed"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_viewHost.Applied, Has.Count.EqualTo(1),
                "Re-materializing must leave the replacement View applied, not remove it.");
        }

        [Test]
        public async Task ProjectionMaterializedNodeCountCoversOnlyTheView()
        {
            await RegisterTd("src-3", TestMaterialization.Td("urn:src-3"));
            await RegisterTd("view-3",
                Projection("urn:view:3", "http://example.com/scenario/Three", "urn:src-3"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-3");
            // A projection with no organizing links materializes exactly one Node:
            // the View. The organized source Nodes are never counted.
            Assert.That(projection.MaterializedNodeCount, Is.EqualTo(1u));
        }

        [Test]
        public async Task OutOfAddressSpaceSelectionIsOmittedAndReportedButStaysActive()
        {
            await RegisterTd("src-4", TestMaterialization.Td("urn:src-4"));
            await RegisterTd("view-4",
                Projection("urn:view:4", "http://example.com/scenario/Four", "urn:src-4"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            // The fake source Nodes carry no portable uav:id and a non-string
            // root, so the selected affordance cannot be located in this address
            // space and is omitted; the load nonetheless reaches Active.
            WotViewProjectionRequest request = m_viewHost.Applied.Single();
            int omittedCount = request.Plan.Omissions.Count;
            Assert.That(omittedCount, Is.GreaterThan(0),
                "An unlocatable selection must be recorded as an omission.");
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-4");
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning),
                "A View that selected a member but organized none must not report plain Success.");
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active),
                "Omission is not a failure; the resource still reaches Active.");
            Assert.That(projection.Message, Does.Contain("organizing 0 Node(s)"));
            Assert.That(projection.Message, Does.Contain("omitted all"));
            Assert.That(projection.Message, Does.Contain("omitted"),
                "The omission must be reported in the load-result Message.");
        }

        [Test]
        public async Task ProjectionViewWithSomeSelectionsOmittedReportsWarning()
        {
            ConfigureSourceRoot("src-partial-a", "PartialSourceA");
            await RegisterTd("src-partial-a", Td("urn:src-partial-a", "valueA"));
            await RegisterTd("src-partial-b", Td("urn:src-partial-b", "valueB"));
            await RegisterTd("view-partial",
                Projection(
                    "urn:view:partial",
                    "http://example.com/scenario/Partial",
                    "urn:src-partial-a",
                    "urn:src-partial-b"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WotViewProjectionRequest request =
                m_viewHost.Applied.Single(v => v.ResourceXid.EndsWith("/view-partial",
                    StringComparison.Ordinal));
            Assert.That(request.Plan.OrganizedNodeIds, Has.Count.EqualTo(1));
            Assert.That(request.Plan.Omissions, Has.Count.EqualTo(1));
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-partial");
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(projection.Message, Does.Contain("organizing 1 of 2"));
            Assert.That(projection.Message, Does.Contain("omitted 1"));
            Assert.That(projection.Message, Does.Contain("urn:src-partial-b"));
        }

        [Test]
        public async Task ProjectionViewWithAllSelectionsMaterializedReportsSuccess()
        {
            ConfigureSourceRoot("src-clean", "CleanSource");
            await RegisterTd("src-clean", TestMaterialization.Td("urn:src-clean"));
            await RegisterTd("view-clean",
                Projection(
                    "urn:view:clean",
                    "http://example.com/scenario/Clean",
                    "urn:src-clean"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WotViewProjectionRequest request = m_viewHost.Applied.Single();
            Assert.That(request.Plan.OrganizedNodeIds, Has.Count.EqualTo(1));
            Assert.That(request.Plan.Omissions, Is.Empty);
            WoTResourceLoadResultDataType projection =
                result.Results.Single(r => r.ResourceId == "view-clean");
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(projection.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(projection.Message, Does.Contain("organizing 1 Node(s)"));
            Assert.That(projection.Message, Does.Not.Contain("omitted"));
        }

        [Test]
        public async Task CyclicProjectionGraphIsRejectedAtDependencyResolution()
        {
            await RegisterTd("cyc-a",
                Projection("urn:view:cyc-a", "http://example.com/scenario/A", "urn:view:cyc-b"));
            await RegisterTd("cyc-b",
                Projection("urn:view:cyc-b", "http://example.com/scenario/B", "urn:view:cyc-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_viewHost.Applied, Is.Empty,
                "A cyclic projection graph must not materialize any View.");
            Assert.That(m_host.AddCount, Is.Zero);
            WoTResourceLoadResultDataType[] cyclic = result.Results
                .Where(r => r.ResourceId is "cyc-a" or "cyc-b")
                .ToArray();
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
            await RegisterTd("src-5", TestMaterialization.Td("urn:src-5"));
            await RegisterTd("view-5",
                Projection("urn:view:5", "http://example.com/scenario/Five", "urn:src-5"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_viewHost.Applied, Has.Count.EqualTo(1));

            await m_coordinator.RemoveAllAsync();

            Assert.That(m_viewHost.Applied, Is.Empty,
                "Retiring the closure must remove the materialized View.");
        }

        private Task<WotRegistryMutationResult> RegisterTd(string resourceId, byte[] content)
        {
            return m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(content)
            }).AsTask();
        }

        private void ConfigureSourceRoot(string resourceId, string rootIdentifier)
        {
            NamespaceTable namespaces = m_coordinator.ServerNamespaceUris ?? new NamespaceTable();
            string modelUri = $"urn:wot:{WotRegistryGroups.ThingDescriptions}/{resourceId}";
            namespaces.GetIndexOrAppend(modelUri);
            m_coordinator.ServerNamespaceUris = namespaces;
            m_converter.SetRootNodeId(resourceId, new ExpandedNodeId(rootIdentifier, modelUri));
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
    }
}
