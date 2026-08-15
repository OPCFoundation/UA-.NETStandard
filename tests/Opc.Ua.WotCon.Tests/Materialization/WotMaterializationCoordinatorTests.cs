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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

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
        private static readonly string[] s_tmTdSourceNames = ["tm-a", "td-a"];

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

        private Task<WotRegistryMutationResult> RegisterTm(string resourceId, byte[] content)
        {
            return m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(content)
            }).AsTask();
        }

        [Test]
        public async Task TmBeforeTdCreatesSingleClosureTmOrderedFirst()
        {
            await RegisterTm("tm-a", TestMaterialization.Tm("urn:tm-a"));
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "A shared closure must project as one runtime NodeManager.");
            HostOperation op = m_host.Operations.Single(o => o.Op == "add");
            Assert.That(op.SourceNames, Is.EqualTo(s_tmTdSourceNames),
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
        public async Task TdBeforeTmFailsThenSucceedsAfterTmRegistration()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"));

            WotRefreshResult first = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.Zero,
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
        public async Task ExternalWebDependencyIsNotResolvedOutsideRegistry()
        {
            await RegisterTd(
                "td-a",
                TestMaterialization.Td(
                    "urn:td-a",
                    extendsHrefs: "https://example.invalid/models/pump.tm.jsonld"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.Zero);
            WoTResourceLoadResultDataType tdResult =
                result.Results.Single(r => r.ResourceId == "td-a");
            Assert.That(tdResult.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(tdResult.Phase, Is.EqualTo(WoTPhaseEnum.DependencyResolution));
        }

        [Test]
        public async Task UnchangedRefreshPreservesRegistrationNoModelEvent()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.AddCount, Is.EqualTo(1));

            WotRefreshResult second = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.EqualTo(1), "No new add on an unchanged refresh.");
            Assert.That(m_host.ShadowCount, Is.Zero, "No shadow reload on an unchanged refresh.");
            Assert.That(m_host.ImmediateCount, Is.Zero,
                "No immediate reload on an unchanged refresh.");
            Assert.That(
                second.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Unchanged));
        }

        [Test]
        public async Task InvalidVersionFailureRetainsPreviousActiveProjection()
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

            Assert.That(m_host.RemoveCount, Is.Zero,
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
        public async Task VersionSwitchUsesShadowReload()
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
        public async Task ProjectionConversionFailureRaisesLoadFailure()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            m_converter.MarkProjectionInvalid("td-a");

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WoTResourceLoadResultDataType tdResult =
                result.Results.Single(r => r.ResourceId == "td-a");
            Assert.That(tdResult.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(tdResult.Phase, Is.EqualTo(WoTPhaseEnum.Projection));
            Assert.That(
                m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!
                    .LoadState,
                Is.EqualTo(WoTLoadStateEnum.Failed));
            Assert.That(
                events.Any(e =>
                    e.Kind == WotMaterializationEventKind.LoadFailure &&
                    e.Phase == WoTPhaseEnum.Projection &&
                    e.LoadState == WoTLoadStateEnum.Failed),
                Is.True,
                "Projection failures must raise WoTLoadFailureEventType data, not validation failures.");
        }

        [Test]
        public async Task VersionSwitchUsesImmediateReloadWhenConfigured()
        {
            m_coordinator.RetirementPolicy = WotProjectionRetirementPolicy.Immediate;
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v1"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v2"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.ShadowCount, Is.Zero);
            Assert.That(m_host.ImmediateCount, Is.EqualTo(1),
                "Immediate retirement must use the host's immediate reload path.");
        }

        [Test]
        public async Task VersionSwitchCleanupWarningTracksCommittedReplacement()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v1"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            m_host.NextReloadWarning = "Prior-generation cleanup is pending.";
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a", "v2"));
            WotRefreshResult switched = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WoTResourceLoadResultDataType result =
                switched.Results.Single(r => r.ResourceId == "td-a");
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.Message, Does.Contain("cleanup is pending"));

            WotRefreshResult unchanged = await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.ShadowCount, Is.EqualTo(1),
                "The committed replacement handle must remain tracked after a cleanup warning.");
            Assert.That(
                unchanged.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Unchanged));
        }

        [Test]
        public async Task DeleteRetiresProjection()
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
        public async Task IndependentClosuresPartialSuccess()
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
        public async Task RefreshExpectedGenerationMismatchIsRejected()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = 99999
            });

            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(m_host.AddCount, Is.Zero);
        }

        [Test]
        public async Task DryRunDoesNotCommit()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { DryRun = true }
            });

            Assert.That(m_host.AddCount, Is.Zero, "A dry run must not project.");
            Assert.That(result.NewGeneration, Is.Zero);
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(result.Results.Single().Generation, Is.EqualTo(1u));
        }

        [Test]
        public async Task DryRunRetirementDoesNotTearDownProjection()
        {
            var binders = new RecordingBinderRegistry();
            m_coordinator.Dispose();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, m_host, binders, documentConverter: m_converter);

            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            await m_coordinator.RefreshAsync(new WotRefreshRequest());
            Assert.That(m_host.AddCount, Is.EqualTo(1));
            Assert.That(binders.ActivatedPlans, Has.Count.EqualTo(1));

            await m_registry.DeleteResourceAsync(WotRegistryGroups.ThingDescriptions, "td-a");
            WotRefreshResult dryRun = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { DryRun = true }
            });

            Assert.That(m_host.RemoveCount, Is.Zero, "A dry-run retirement must not remove the projection.");
            Assert.That(binders.DeactivatedPlans, Is.Empty,
                "A dry-run retirement must not deactivate active binding plans.");
            Assert.That(dryRun.Summary.Retired, Is.EqualTo(1u));
            WoTResourceLoadResultDataType retired = dryRun.Results.Single();
            Assert.That(retired.ResourceId, Is.EqualTo("td-a"));
            Assert.That(retired.Outcome, Is.EqualTo(WoTOutcomeEnum.Skipped));
            Assert.That(retired.LoadState, Is.EqualTo(WoTLoadStateEnum.Unloaded));
            Assert.That(retired.Message, Does.Contain("would be retired"));

            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.RemoveCount, Is.EqualTo(1),
                "The committed retirement must still find the tracked closure after the dry run.");
            Assert.That(binders.DeactivatedPlans, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DryRunsDoNotAdvanceExpectedGeneration()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));
            WotRefreshResult committed = await m_coordinator.RefreshAsync(new WotRefreshRequest());
            uint expectedGeneration = committed.NewGeneration;
            Assert.That(expectedGeneration, Is.EqualTo(m_coordinator.Generation));

            for (int i = 0; i < 2; i++)
            {
                WotRefreshResult dryRun = await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    Options = new WoTRefreshOptionsDataType { DryRun = true }
                });

                Assert.That(dryRun.NewGeneration, Is.Zero);
                Assert.That(m_coordinator.Generation, Is.EqualTo(expectedGeneration));
            }

            WotRefreshResult afterDryRuns = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = expectedGeneration
            });

            Assert.That(afterDryRuns.Summary.Outcome, Is.Not.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(m_coordinator.Generation, Is.EqualTo(expectedGeneration + 1));
        }

        [Test]
        public async Task DetailedResultsCarryNodeCountAndDigest()
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
        public async Task RootNodeIdIsRecordedFromGeneratedNodeSet()
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
            Assert.That(resource.RootNodeId.IsNull, Is.False);
        }

        [Test]
        public async Task RootNodeIdIsNullWhenNamespaceCannotBeResolved()
        {
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WoTResourceLoadResultDataType td = result.Results.Single(r => r.ResourceId == "td-a");
            Assert.That(td.RootNodeId.IsNull, Is.True,
                "A document with an unresolved root namespace must report NodeId.Null.");
            WotResource resource = m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "td-a")!;
            Assert.That(resource.RootNodeId.IsNull, Is.True);
        }

        [Test]
        public async Task LoadFailureEventFailedNodeIdDefaultsToNullNodeId()
        {
            var events = new List<WotMaterializationEventArgs>();
            m_coordinator.Event += (_, e) => events.Add(e);
            m_converter.MarkInvalid("td-a");
            await RegisterTd("td-a", TestMaterialization.Td("urn:td-a"));

            await m_coordinator.RefreshAsync(new WotRefreshRequest());

            WotMaterializationEventArgs failure = events.Single(
                e => e.Kind == WotMaterializationEventKind.ValidationFailure);
            Assert.That(failure.FailedNodeId.IsNull, Is.True);
        }

        [Test]
        public void FailedNodeIdCanCarryConcreteNodeId()
        {
            var args = new WotMaterializationEventArgs(WotMaterializationEventKind.LoadFailure)
            {
                FailedNodeId = new NodeId(1234, 2)
            };

            Assert.That(args.FailedNodeId.IsNull, Is.False);
            Assert.That(args.FailedNodeId, Is.EqualTo(new NodeId(1234, 2)));
        }

        [Test]
        public async Task PlaceholderResourceWithoutVersionIsNotProjected()
        {
            await m_registry.TryCreateResourceAsync(
                WotRegistryGroups.ThingDescriptions, "empty",
                WoTDocumentKindEnum.ThingDescription);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(m_host.AddCount, Is.Zero,
                "A content-less placeholder resource must not project.");
            Assert.That(result.Results, Is.Empty);
        }

        private sealed class RecordingBinderRegistry : IWotBinderRegistry
        {
            public List<WotBindingPlan> ActivatedPlans { get; } = [];
            public List<WotBindingPlan> DeactivatedPlans { get; } = [];

            public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }
                = [];

            public WotBindingPlan Prepare(WotBindingPlanRequest request)
            {
                var entry = new WotCompiledForm(
                    new WotBindingIdentity("rec", "1.0", "urn:rec"),
                    WotAffordanceKind.Property, "value", "/properties/value/forms/0",
                    WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                    new WotEndpointDescriptor("rec", null, -1, "rec://x"),
                    new WotAddressingDescriptor("value"),
                    new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                    new WotPayloadDescriptor("application/json", "json"),
                    [], isExecutable: true);
                return new WotBindingPlan(request.ResourceXid, [], [entry], [], []);
            }

            public ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                ActivatedPlans.Add(plan);
                return default;
            }

            public ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                DeactivatedPlans.Add(plan);
                return default;
            }
        }
    }
}
