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
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Tests.Support;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

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
        [SetUp]
        public async Task SetUpAsync()
        {
            m_runtime = await PreparedWotTestRuntime.StartAsync().ConfigureAwait(false);
            m_registry = await m_runtime.CreateRegistryAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_runtime is not null)
            {
                await m_runtime.DisposeAsync().ConfigureAwait(false);
                m_runtime = null;
            }
        }

        private static byte[] Td(string id, string href, string extraTerms = "")
        {
            string terms = string.IsNullOrEmpty(extraTerms) ? string.Empty : "," + extraTerms;
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"@type\":\"uav:object\"," +
                "\"id\":\"" +
                id +
                "\",\"title\":\"t\"," +
                "\"properties\":{\"value\":{\"type\":\"number\",\"forms\":[{\"href\":\"" +
                href +
                "\"" +
                terms +
                "}]}}}";
            return Encoding.UTF8.GetBytes(td);
        }

        private WotRegistryService Registry()
        {
            return m_registry;
        }

        private static Task<WotRegistryMutationResult> Upsert(
            WotRegistryService registry, string resourceId, byte[] content)
        {
            return registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(content)
            }).AsTask();
        }

        [Test]
        public async Task StrictUnsupportedFormFailsClosure()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted), binders,
                documentConverter: new FakeWotDocumentConverter())
            {
                StrictBindings = true
            };
            await Upsert(registry, "td-a", Td("urn:td-a", "ftp://legacy/x")).ConfigureAwait(false);

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(host.AddCount, Is.Zero, "A strict closure with unsupported forms must not project.");
            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task DegradedUnsupportedFormMaterializesWithWarningAndBindingFailure()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted), binders,
                documentConverter: new FakeWotDocumentConverter())
            {
                StrictBindings = false
            };
            var events = new List<WotMaterializationEventArgs>();
            coordinator.Event += (_, e) => events.Add(e);
            await Upsert(registry, "td-a", Td("urn:td-a", "ftp://legacy/x")).ConfigureAwait(false);

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(host.AddCount, Is.EqualTo(1), "A degraded closure still materializes nodes.");
            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(events.Any(e => e.Kind == WotMaterializationEventKind.BindingFailure), Is.True,
                "Degraded mode must emit a binding failure event.");
        }

        [Test]
        public async Task NonExecutableFormDegradesClosure()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted), binders,
                documentConverter: new FakeWotDocumentConverter());
            await Upsert(
                registry, "td-a", Td("urn:td-a", "coap://d/temp", "\"cov:method\":\"GET\"")).ConfigureAwait(false);

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(host.AddCount, Is.EqualTo(1));
            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Warning),
                "A validated but non-executable binding degrades the closure.");
        }

        [Test]
        public async Task ExecutableFormIsNotDegraded()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(
                [new MemoryWotBinder()],
                [new MemoryWotBindingExecutor(new MemoryWotStore())]);
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted), binders,
                documentConverter: new FakeWotDocumentConverter());
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/value")).ConfigureAwait(false);

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(result.Results.Single(r => r.ResourceId == "td-a").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Success),
                "A fully executable binding is not degraded.");
        }

        [Test]
        public async Task RefreshPassesPreparedBindingPlansIntoTheProjectionDocument()
        {
            WotRegistryService registry = Registry();
            var host = new FakeWotProjectionHost();
            var binders = new WotProtocolBinderRegistry(
                [new MemoryWotBinder()],
                [new MemoryWotBindingExecutor(new MemoryWotStore())]);
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted), binders,
                documentConverter: new FakeWotDocumentConverter());
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/value")).ConfigureAwait(false);

            await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            HostOperation operation = host.Operations.Single(o => o.Op == "add");
            Assert.That(operation.Document, Is.Not.Null);
            Assert.That(operation.Document!.BindingPlans.Count, Is.EqualTo(1),
                "The coordinator must pass exactly one prepared plan per projected member.");
            Assert.That(operation.Document.BindingPlans[0].HasExecutableForms, Is.True,
                "The plan passed to the host must be the executable plan the coordinator prepared.");
        }

        [Test]
        public async Task LifecycleBinderNotificationsObserveCommittedProjectionImages()
        {
            WotRegistryService registry = Registry();
            var timeline = new List<string>();
            var observations = new List<(bool Active, WotCommittedPublicationState Image)>();
            var host = new RecordingProjectionHost(timeline);
            var binders = new RecordingBinderRegistry(timeline);
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted), binders,
                documentConverter: new FakeWotDocumentConverter());
            binders.Notification = active => observations.Add((active, coordinator.CommittedPublication));
            int registrations = m_runtime.Lifecycle.Registrations.Count;
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/value")).ConfigureAwait(false);

            await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(m_runtime.Lifecycle.Registrations.Count, Is.EqualTo(registrations + 1));
            await registry.DeleteResourceAsync(WotRegistryGroups.ThingDescriptions, "td-a").ConfigureAwait(false);
            WotRefreshResult retired = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            int add = timeline.IndexOf("activation-commit");
            int activate = timeline.IndexOf("activate");
            int deactivate = timeline.IndexOf("deactivate");
            int remove = timeline.IndexOf("retirement-commit");

            Assert.That(add, Is.GreaterThanOrEqualTo(0));
            Assert.That(activate, Is.GreaterThan(add), "Activate must follow the projection commit.");
            Assert.That(remove, Is.GreaterThan(activate));
            Assert.That(deactivate, Is.GreaterThan(remove),
                "Binder bookkeeping must observe the committed retirement, not precede its decision.");
            Assert.That(observations, Has.Count.EqualTo(2));
            Assert.That(observations[0].Active, Is.True);
            Assert.That(observations[0].Image.RefreshGeneration, Is.EqualTo(1U));
            Assert.That(observations[0].Image.ActiveBindingPlans.Count, Is.EqualTo(1));
            Assert.That(observations[0].Image.RegistrySnapshot
                .FindResource(WotRegistryGroups.ThingDescriptions, "td-a")!.LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(observations[1].Active, Is.False);
            Assert.That(observations[1].Image.RefreshGeneration, Is.EqualTo(2U));
            Assert.That(observations[1].Image.ActiveBindingPlans.IsEmpty, Is.True);
            Assert.That(observations[1].Image.RegistrySnapshot
                .FindResource(WotRegistryGroups.ThingDescriptions, "td-a"), Is.Null);
            Assert.That(retired.Summary.Retired, Is.EqualTo(1U));
            Assert.That(m_runtime.Lifecycle.Registrations.Count, Is.EqualTo(registrations));
        }

        [Test]
        public async Task UpdateDeactivatesExactlyOldPlansInCorrectOrder()
        {
            WotRegistryService registry = Registry();
            var recorder = new PlanRecorder();
            var host = new PlanRecordingHost(recorder);
            var binders = new PlanRecordingBinderRegistry(recorder);
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted, prepare: host.ValidatePreparation), binders,
                documentConverter: new FakeWotDocumentConverter());

            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v1")).ConfigureAwait(false);
            await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            // A content change triggers a shadow reload (an update, not a first add).
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v2")).ConfigureAwait(false);
            await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

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
        public async Task UpdateShadowReloadFailsOldPlansRemainActive()
        {
            WotRegistryService registry = Registry();
            var recorder = new PlanRecorder();
            var host = new PlanRecordingHost(recorder);
            var binders = new PlanRecordingBinderRegistry(recorder);
            using var coordinator = new WotMaterializationCoordinator(
                registry, m_runtime!.Observe(host.RecordCommitted, prepare: host.ValidatePreparation), binders,
                documentConverter: new FakeWotDocumentConverter());

            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v1")).ConfigureAwait(false);
            await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            WotBindingPlan planV1 = binders.ActivatedPlans[0];

            // The shadow switch fails: the old plans must remain active (no
            // deactivation) and no new plan may be activated (rollback ordering).
            host.FailShadowReload = true;
            await Upsert(registry, "td-a", Td("urn:td-a", "mem://store/v2")).ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            WotCommittedPublicationState published = coordinator.CommittedPublication;
            var registrations = m_runtime.Lifecycle.Registrations;
            await Assert.ThatAsync(async () =>
                await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false),
                Throws.TypeOf<System.IO.IOException>().With.Message.EqualTo("Injected shadow reload failure."))
                .ConfigureAwait(false);

            Assert.That(binders.DeactivatedPlans, Is.Empty,
                "A failed shadow switch must not deactivate the still-active old plan.");
            Assert.That(binders.ActivatedPlans, Has.Count.EqualTo(1),
                "A failed shadow switch must not activate the new plan.");
            Assert.That(binders.ActivatedPlans[0], Is.SameAs(planV1));
            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(coordinator.CommittedPublication, Is.SameAs(published));
            Assert.That(m_runtime.Lifecycle.Registrations, Is.EqualTo(registrations));
            Assert.That(recorder.IndexOf("shadow"), Is.EqualTo(-1));

            host.FailShadowReload = false;
            WotRefreshResult retry = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(retry.Summary.Failed, Is.Zero);
            Assert.That(retry.NewGeneration, Is.EqualTo(published.RefreshGeneration + 1));
            Assert.That(binders.DeactivatedPlans, Has.Count.EqualTo(1));
            Assert.That(binders.DeactivatedPlans[0], Is.SameAs(planV1));
            Assert.That(binders.ActivatedPlans, Has.Count.EqualTo(2));
            WotBindingPlan activated = binders.ActivatedPlans[1];
            Assert.That(activated, Is.Not.SameAs(planV1));
            WotBindingPlan committed = coordinator.CommittedPublication.ActiveBindingPlans.ToList().Single();
            Assert.That(committed.ResourceXid, Is.EqualTo(activated.ResourceXid));
            Assert.That(committed.CompiledForms.Single(), Is.SameAs(activated.CompiledForms.Single()));
            Assert.That(committed.CompiledForms.Single(), Is.Not.SameAs(planV1.CompiledForms.Single()));
        }

        private PreparedWotTestRuntime? m_runtime;
        private WotRegistryService m_registry = null!;

        private sealed class PlanRecorder
        {
            public List<(string Action, WotBindingPlan? Plan)> Events { get; } = [];

            public void Record(string action, WotBindingPlan? plan = null)
            {
                lock (m_lock)
                {
                    Events.Add((action, plan));
                }
            }

            public int IndexOf(string action, WotBindingPlan? plan = null)
            {
                lock (m_lock)
                {
                    return Events.FindIndex(e =>
                        e.Action == action && (plan is null || ReferenceEquals(e.Plan, plan)));
                }
            }

            private readonly Lock m_lock = new();
        }

        private sealed class PlanRecordingHost
        {
            public PlanRecordingHost(PlanRecorder recorder)
            {
                m_recorder = recorder;
            }

            public bool FailShadowReload { get; set; }

            public void RecordCommitted(ArrayOf<WotProjectionChange> changes)
            {
                foreach (WotProjectionChange change in changes)
                {
                    m_recorder.Record(change.Document is null ? "remove" :
                        change.Current is null ? "add" :
                        change.RetirementPolicy == WotProjectionRetirementPolicy.Immediate ? "immediate" : "shadow");
                }
            }

            public void ValidatePreparation(ArrayOf<WotProjectionChange> changes)
            {
                if (FailShadowReload && changes.ToList().Any(change =>
                    change.Document is not null && change.Current is not null))
                {
                    throw new System.IO.IOException("Injected shadow reload failure.");
                }
            }

            private readonly PlanRecorder m_recorder;
        }

        private sealed class PlanRecordingBinderRegistry : IWotBinderRegistry
        {
            public PlanRecordingBinderRegistry(PlanRecorder recorder)
            {
                m_recorder = recorder;
            }

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
                // A fresh plan instance per Prepare so old and new plans are
                // distinguishable by reference identity.
                return new WotBindingPlan(request.ResourceXid,
                    [],
                    [entry],
                    [],
                    []);
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

        private sealed class RecordingProjectionHost
        {
            public RecordingProjectionHost(List<string> timeline)
            {
                m_timeline = timeline;
            }

            public void RecordCommitted(ArrayOf<WotProjectionChange> changes)
            {
                foreach (WotProjectionChange change in changes)
                {
                    m_timeline.Add(change.Document is null ? "retirement-commit" :
                        change.Current is null ? "activation-commit" :
                        change.RetirementPolicy == WotProjectionRetirementPolicy.Immediate ? "immediate" : "shadow");
                }
            }

            private readonly List<string> m_timeline;
        }

        private sealed class RecordingBinderRegistry : IWotBinderRegistry
        {
            public RecordingBinderRegistry(List<string> timeline)
            {
                m_timeline = timeline;
            }

            public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities { get; }
                = [];

            public Action<bool>? Notification { get; set; }

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
                return new WotBindingPlan(request.ResourceXid,
                    [],
                    [entry],
                    [],
                    []);
            }

            public ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                m_timeline.Add("activate");
                Notification?.Invoke(true);
                return default;
            }

            public ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                m_timeline.Add("deactivate");
                Notification?.Invoke(false);
                return default;
            }

            private readonly List<string> m_timeline;
        }
    }
}
