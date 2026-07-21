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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.Binding.Samples;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises the materialization coordinator's binding lifecycle: strict vs
    /// degraded closure selection, non-executable degradation, and the
    /// activate-after-commit / deactivate-before-retire ordering.
    /// </summary>
    [TestFixture]
    public sealed class WotBindingCoordinatorTests
    {
        private static byte[] Td(string id, string href, string extraTerms = "")
        {
            string terms = string.IsNullOrEmpty(extraTerms) ? string.Empty : "," + extraTerms;
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"@type\":\"uav:object\"," +
                "\"id\":\"" + id + "\",\"title\":\"t\"," +
                "\"properties\":{\"value\":{\"type\":\"number\",\"forms\":[{\"href\":\"" + href + "\"" +
                terms + "}]}}}";
            return Encoding.UTF8.GetBytes(td);
        }

        private static WotRegistryService Registry() => new WotRegistryService();

        private static Task Upsert(WotRegistryService registry, string resourceId, byte[] content)
            => registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = content
            }).AsTask();

        [Test]
        public async Task Strict_UnsupportedForm_FailsClosure()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, binders, documentConverter: new FakeWotDocumentConverter())
            {
                StrictBindings = true
            };
            await Upsert(registry, "td-a", Td("urn:td-a", "ftp://legacy/x"));

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(host.AddCount, Is.EqualTo(0), "A strict closure with unsupported forms must not project.");
            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task Degraded_UnsupportedForm_MaterializesWithWarningAndBindingFailure()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, binders, documentConverter: new FakeWotDocumentConverter())
            {
                StrictBindings = false
            };
            var events = new List<WotMaterializationEventArgs>();
            coordinator.Event += (_, e) => events.Add(e);
            await Upsert(registry, "td-a", Td("urn:td-a", "ftp://legacy/x"));

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(host.AddCount, Is.EqualTo(1), "A degraded closure still materializes nodes.");
            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(events.Any(e => e.Kind == WotMaterializationEventKind.BindingFailure), Is.True,
                "Degraded mode must emit a binding failure event.");
        }

        [Test]
        public async Task NonExecutableForm_DegradesClosure()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, binders, documentConverter: new FakeWotDocumentConverter());
            await Upsert(registry, "td-a", Td("urn:td-a", "coap://d/temp", "\"cov:method\":\"GET\""));

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(host.AddCount, Is.EqualTo(1));
            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Warning),
                "A validated but non-executable binding degrades the closure.");
        }

        [Test]
        public async Task ExecutableForm_IsNotDegraded()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new MemoryWotBinder() },
                new IWotBindingExecutor[] { new MemoryWotBindingExecutor(new MemoryWotStore()) });
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, binders, documentConverter: new FakeWotDocumentConverter());
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/value"));

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Success),
                "A fully executable binding is not degraded.");
        }

        [Test]
        public async Task Lifecycle_ActivatesAfterCommit_DeactivatesBeforeRetire()
        {
            WotRegistryService registry = Registry();
            var timeline = new List<string>();
            var host = new RecordingProjectionHost(timeline);
            var binders = new RecordingBinderRegistry(timeline);
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, binders, documentConverter: new FakeWotDocumentConverter());
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/value"));

            await coordinator.RefreshAsync(new WotRefreshRequest());
            await registry.DeleteResourceAsync(WotRegistryGroups.ThingDescriptions, "td-a");
            await coordinator.RefreshAsync(new WotRefreshRequest());

            int add = timeline.IndexOf("add");
            int activate = timeline.IndexOf("activate");
            int deactivate = timeline.IndexOf("deactivate");
            int remove = timeline.IndexOf("remove");

            Assert.That(add, Is.GreaterThanOrEqualTo(0));
            Assert.That(activate, Is.GreaterThan(add), "Activate must follow the projection commit.");
            Assert.That(deactivate, Is.GreaterThanOrEqualTo(0));
            Assert.That(remove, Is.GreaterThan(deactivate), "Deactivate must precede retirement.");
        }

        [Test]
        public async Task Update_DeactivatesExactlyOldPlans_InCorrectOrder()
        {
            WotRegistryService registry = Registry();
            var recorder = new PlanRecorder();
            var host = new PlanRecordingHost(recorder);
            var binders = new PlanRecordingBinderRegistry(recorder);
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, binders, documentConverter: new FakeWotDocumentConverter());

            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v1"));
            await coordinator.RefreshAsync(new WotRefreshRequest());

            // A content change triggers a shadow reload (an update, not a first add).
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v2"));
            await coordinator.RefreshAsync(new WotRefreshRequest());

            WotBindingPlan planV1 = binders.ActivatedPlans[0];
            WotBindingPlan planV2 = binders.ActivatedPlans[1];
            Assert.That(planV2, Is.Not.SameAs(planV1), "The update must prepare a new plan.");

            // Exactly one deactivation, and it is the old plan (never the new one).
            Assert.That(binders.DeactivatedPlans, Has.Count.EqualTo(1));
            Assert.That(binders.DeactivatedPlans[0], Is.SameAs(planV1),
                "Only the previously tracked plan may be deactivated on update.");

            // Order: the shadow switch happens first, then the old plan is
            // deactivated, then the new plan is activated.
            int shadow = recorder.IndexOf("shadow");
            int deactivateOld = recorder.IndexOf("deactivate", planV1);
            int activateNew = recorder.IndexOf("activate", planV2);
            Assert.That(shadow, Is.GreaterThanOrEqualTo(0), "The update must shadow-reload the projection.");
            Assert.That(deactivateOld, Is.GreaterThan(shadow),
                "The old plan must be deactivated only after the shadow switch succeeds.");
            Assert.That(activateNew, Is.GreaterThan(deactivateOld),
                "The new plan must be activated after the old plan is deactivated.");
        }

        [Test]
        public async Task Update_ShadowReloadFails_OldPlansRemainActive()
        {
            WotRegistryService registry = Registry();
            var recorder = new PlanRecorder();
            var host = new PlanRecordingHost(recorder);
            var binders = new PlanRecordingBinderRegistry(recorder);
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, binders, documentConverter: new FakeWotDocumentConverter());

            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v1"));
            await coordinator.RefreshAsync(new WotRefreshRequest());
            WotBindingPlan planV1 = binders.ActivatedPlans[0];

            // The shadow switch fails: the old plans must remain active (no
            // deactivation) and no new plan may be activated (rollback ordering).
            host.FailShadowReload = true;
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v2"));
            await coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.That(binders.DeactivatedPlans, Is.Empty,
                "A failed shadow switch must not deactivate the still-active old plan.");
            Assert.That(binders.ActivatedPlans, Has.Count.EqualTo(1),
                "A failed shadow switch must not activate the new plan.");
            Assert.That(binders.ActivatedPlans[0], Is.SameAs(planV1));
        }

        private sealed class PlanRecorder
        {
            public List<(string Action, WotBindingPlan? Plan)> Events { get; } = new();

            public void Record(string action, WotBindingPlan? plan = null)
            {
                lock (Events)
                {
                    Events.Add((action, plan));
                }
            }

            public int IndexOf(string action, WotBindingPlan? plan = null)
            {
                lock (Events)
                {
                    return Events.FindIndex(e =>
                        e.Action == action && (plan is null || ReferenceEquals(e.Plan, plan)));
                }
            }
        }

        private sealed class PlanRecordingHost : IWotProjectionHost
        {
            public PlanRecordingHost(PlanRecorder recorder) => m_recorder = recorder;

            public bool FailShadowReload { get; set; }

            public ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                m_recorder.Record("add");
                return new ValueTask<WotProjectionHandle>(Handle(document));
            }

            public ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                if (FailShadowReload)
                {
                    throw new System.IO.IOException("Injected shadow reload failure.");
                }
                m_recorder.Record("shadow");
                return new ValueTask<WotProjectionHandle>(Handle(document));
            }

            public ValueTask RemoveAsync(WotProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                m_recorder.Record("remove");
                return default;
            }

            private static WotProjectionHandle Handle(WotProjectionDocument document)
                => new WotProjectionHandle(document.ClosureKey, 1, new object(), ImmutableArray<NodeId>.Empty, 0);

            private readonly PlanRecorder m_recorder;
        }

        private sealed class PlanRecordingBinderRegistry : IWotBinderRegistry
        {
            public PlanRecordingBinderRegistry(PlanRecorder recorder) => m_recorder = recorder;

            public List<WotBindingPlan> ActivatedPlans { get; } = new();
            public List<WotBindingPlan> DeactivatedPlans { get; } = new();

            public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }
                = System.Array.Empty<WoTBindingCapabilityDataType>();

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
                    ImmutableArray<WotCredentialReference>.Empty, isExecutable: true);
                // A fresh plan instance per Prepare so old and new plans are
                // distinguishable by reference identity.
                return new WotBindingPlan(request.ResourceXid,
                    ImmutableArray<WoTBindingCapabilityDataType>.Empty,
                    ImmutableArray.Create(entry),
                    ImmutableArray<WotAffordanceForm>.Empty,
                    ImmutableArray<WotBindingDiagnostic>.Empty);
            }

            public ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                ActivatedPlans.Add(plan);
                m_recorder.Record("activate", plan);
                return default;
            }

            public ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                DeactivatedPlans.Add(plan);
                m_recorder.Record("deactivate", plan);
                return default;
            }

            private readonly PlanRecorder m_recorder;
        }

        private sealed class RecordingProjectionHost : IWotProjectionHost
        {
            public RecordingProjectionHost(List<string> timeline) => m_timeline = timeline;

            public ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                m_timeline.Add("add");
                return new ValueTask<WotProjectionHandle>(Handle(document));
            }

            public ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                m_timeline.Add("shadow");
                return new ValueTask<WotProjectionHandle>(Handle(document));
            }

            public ValueTask RemoveAsync(WotProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                m_timeline.Add("remove");
                return default;
            }

            private static WotProjectionHandle Handle(WotProjectionDocument document)
                => new WotProjectionHandle(document.ClosureKey, 1, new object(), ImmutableArray<NodeId>.Empty, 0);

            private readonly List<string> m_timeline;
        }

        private sealed class RecordingBinderRegistry : IWotBinderRegistry
        {
            public RecordingBinderRegistry(List<string> timeline) => m_timeline = timeline;

            public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }
                = System.Array.Empty<WoTBindingCapabilityDataType>();

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
                    ImmutableArray<WotCredentialReference>.Empty, isExecutable: true);
                return new WotBindingPlan(request.ResourceXid,
                    ImmutableArray<WoTBindingCapabilityDataType>.Empty,
                    ImmutableArray.Create(entry),
                    ImmutableArray<WotAffordanceForm>.Empty,
                    ImmutableArray<WotBindingDiagnostic>.Empty);
            }

            public ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                m_timeline.Add("activate");
                return default;
            }

            public ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                m_timeline.Add("deactivate");
                return default;
            }

            private readonly List<string> m_timeline;
        }
    }
}
