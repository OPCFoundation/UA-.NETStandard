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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises the materialization coordinator against a recording projection
    /// host and a deterministic converter, covering the dependency-closure,
    /// unchanged-refresh, invalid-retention, shadow-reload and retirement
    /// behaviours required by the WoT Connectivity V2 runtime.
    /// </summary>
    [TestFixture]
    public sealed class WotMaterializationCoordinatorTests
    {
        private WotRegistryService m_registry = null!;
        private FakeWotProjectionHost m_host = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private WotMaterializationCoordinator m_coordinator = null!;

        [SetUp]
        public void SetUp()
        {
            m_registry = new WotRegistryService();
            m_host = new FakeWotProjectionHost();
            m_converter = new FakeWotDocumentConverter();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, m_host, documentConverter: m_converter);
        }

        [TearDown]
        public void TearDown()
        {
            m_coordinator.Dispose();
            m_registry.Dispose();
        }

        private Task RegisterTd(string resourceId, byte[] content)
            => m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = content
            }).AsTask();

        private Task RegisterTm(string resourceId, byte[] content)
            => m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = content
            }).AsTask();

        [Test]
        public async Task TmBeforeTd_CreatesSingleClosure_TmOrderedFirst()
        {
            await RegisterTm("tm-a", TestMaterialization.Tm("urn:tm-a"));
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "A shared closure must project as one runtime NodeManager.");
            HostOperation op = m_host.Operations.Single(o => o.Op == "add");
            Assert.That(op.SourceNames, Is.EqualTo(new[] { "tm-a", "td-a" }),
                "Thing Models must be ordered before the Thing Descriptions that extend them.");
            // With the default (no-op) binder, affordance forms have no binder and
            // materialize as degraded nodes, so the projected outcome is Warning;
            // both members nonetheless reach the Active load state.
            Assert.That(
                result.Results.Count(r =>
                    r.Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning),
                Is.EqualTo(2));
            Assert.That(
                m_registry.Current.FindResource(WotRegistryGroups.ThingModels, "tm-a")!.LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!
                    .LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
        }

        [Test]
        public async Task TdBeforeTm_FailsThenSucceedsAfterTmRegistration()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"));

            WotRefreshResult first = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(0),
                "A Thing Description with a missing model dependency must not project.");
            WoTResourceLoadResultDataType tdResult =
                first.Results.Single(r => r.ResourceId == "td-a");
            Assert.That(tdResult.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(tdResult.Phase, Is.EqualTo(WoTPhaseEnum.DependencyResolution));
            Assert.That(
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!
                    .LoadState,
                Is.EqualTo(WoTLoadStateEnum.Failed));

            await RegisterTm("tm-a", TestMaterialization.Tm("urn:tm-a"));
            WotRefreshResult second = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "Registering the missing model must let the closure project.");
            Assert.That(
                second.Results.Count(r =>
                    r.Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning),
                Is.EqualTo(2));
            Assert.That(
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!
                    .LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
        }

        [Test]
        public async Task UnchangedRefresh_PreservesRegistration_NoModelEvent()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.AddCount, Is.EqualTo(1));

            WotRefreshResult second = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1), "No new add on an unchanged refresh.");
            Assert.That(m_host.ShadowCount, Is.EqualTo(0), "No shadow reload on an unchanged refresh.");
            Assert.That(
                second.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Unchanged));
        }

        [Test]
        public async Task InvalidVersion_Failure_RetainsPreviousActiveProjection()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v1"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            WotResource afterFirst =
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!;
            string activeBefore = afterFirst.ActiveVersionId!;
            Assert.That(afterFirst.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));

            // A new version whose conversion fails.
            m_converter.MarkInvalid("td-a");
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v2"));
            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.RemoveCount, Is.EqualTo(0),
                "A failed refresh must retain the previous active projection.");
            WotResource afterFail =
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!;
            Assert.That(afterFail.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
            Assert.That(afterFail.ActiveVersionId, Is.EqualTo(activeBefore),
                "The previously active version must be retained on failure.");
            Assert.That(
                result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(
                events.Any(e => e.Kind == WotMaterializationEventKind.ValidationFailure),
                Is.True, "A validation failure event must be emitted.");
        }

        [Test]
        public async Task VersionSwitch_UsesShadowReload()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v1"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.AddCount, Is.EqualTo(1));

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v2"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1), "A version switch must not re-add.");
            Assert.That(m_host.ShadowCount, Is.EqualTo(1),
                "A version switch must shadow-reload the projection.");
        }

        [Test]
        public async Task Delete_RetiresProjection()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.AddCount, Is.EqualTo(1));

            await m_registry.DeleteResourceAsync(WotRegistryGroups.ThingDescriptions, "td-a");
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.RemoveCount, Is.EqualTo(1),
                "A deleted resource's projection must be retired.");
        }

        [Test]
        public async Task IndependentClosures_PartialSuccess()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await RegisterTd("td-b", TestMaterialization.Td("urn:td-b"));
            m_converter.MarkInvalid("td-b");

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "Only the projectable closure commits.");
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Summary.Failed, Is.EqualTo(1u));
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!
                    .LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-b")!
                    .LoadState,
                Is.EqualTo(WoTLoadStateEnum.Failed));
        }

        [Test]
        public async Task Refresh_ExpectedGenerationMismatch_IsRejected()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = 99999
            });

            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(m_host.AddCount, Is.EqualTo(0));
        }

        [Test]
        public async Task DryRun_DoesNotCommit()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { DryRun = true }
            });

            Assert.That(m_host.AddCount, Is.EqualTo(0), "A dry run must not project.");
            Assert.That(result.NewGeneration, Is.EqualTo(0u));
        }

        [Test]
        public async Task DetailedResults_CarryNodeCountAndDigest()
        {
            m_converter.SetNodeCount("td-a", 7);
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "req-1"
            });

            WoTResourceLoadResultDataType td = result.Results.Single(r => r.ResourceId == "td-a");
            Assert.That(td.MaterializedNodeCount, Is.EqualTo(7u));
            Assert.That(td.ContentDigest.Length, Is.GreaterThan(0));
            Assert.That(result.Summary.RequestId, Is.EqualTo("req-1"));
        }

        [Test]
        public async Task RootNodeId_IsRecordedFromGeneratedNodeSet()
        {
            // The fake converter emits a NodeSet whose model namespace is
            // urn:wot:{group}/{resource}; register it so the coordinator can
            // resolve the recorded projection root into a server NodeId.
            var namespaces = new NamespaceTable();
            string modelUri = $"urn:wot:{WotRegistryGroups.ThingDescriptions}/td-a";
            namespaces.Append(modelUri);
            m_coordinator.ServerNamespaceUris = namespaces;
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WoTResourceLoadResultDataType td = result.Results.Single(r => r.ResourceId == "td-a");
            Assert.That(td.RootNodeId.IsNull, Is.False,
                "A document with a root must report a non-null RootNodeId.");
            Assert.That(td.RootNodeId.NamespaceIndex,
                Is.EqualTo((ushort)namespaces.GetIndex(modelUri)));
            WotResource resource = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "td-a")!;
            Assert.That(resource.RootNodeId, Is.Not.Null);
        }

        [Test]
        public async Task PlaceholderResource_WithoutVersion_IsNotProjected()
        {
            await m_registry.TryCreateResourceAsync(
                WotRegistryGroups.ThingDescriptions, "empty",
                WoTDocumentKindEnum.ThingDescription);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(0),
                "A content-less placeholder resource must not project.");
            Assert.That(result.Results, Is.Empty);
        }
    }
}
