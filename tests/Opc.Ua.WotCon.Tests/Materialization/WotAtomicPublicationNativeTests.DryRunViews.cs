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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
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
        public async Task DryRunValidatesPreparedSourceAndViewWithoutPublishing(bool invalidView)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kStockSourceNamespace);
            WotResource child = await AddStockViewAsync("child", false, collideWithSource: invalidView)
                .ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            m_events.Clear();
            WotRefreshRequest request = HandoffRequest("prepared-view-dry-run");
            request.Options.DryRun = true;

            WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.Zero);
            Assert.That(result.Summary.Total, Is.EqualTo(2u));
            Assert.That(result.Results.Select(row => row.Xid), Is.EquivalentTo(new[] { source.Xid, child.Xid }));
            if (invalidView)
            {
                Assert.That(result.Summary.Failed, Is.GreaterThan(0u),
                    "A dry run must diagnose the same invalid candidate View that actual preparation rejects.");
                WoTResourceLoadResultDataType failed = result.Results.Single(row => row.Xid == child.Xid);
                Assert.That(failed.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(failed.Message, Is.Not.Null.And.Not.Empty);
            }
            else
            {
                Assert.That(result.Summary.Failed, Is.Zero);
                Assert.That(result.Results.All(row => row.Outcome != WoTOutcomeEnum.Failed), Is.True);
            }
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(m_coordinator.CommittedPublication.Views.IsEmpty, Is.True);
            Assert.That(m_registry.Current.CanonicalViewGraphState.IsNull, Is.True);
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(StockView("child")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(probe.DecisionCount, Is.Zero);
            Assert.That(m_events, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DryRunAllocatesOnlyPrivateNamespaceAndTypeMappings(bool thingModel)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false, thingModel).ConfigureAwait(false);
            await AddStockViewAsync("child", false, thingModel: thingModel).ConfigureAwait(false);
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            ArrayOf<string> before = namespaces.GetSnapshot(out long version);
            var candidateRoot = new NodeId("Source", (ushort)before.Count);
            WotRegistrySnapshot registry = m_registry.Current;
            Assert.That(namespaces.GetIndex(kStockSourceNamespace), Is.EqualTo(-1));
            WotRefreshRequest request = HandoffRequest("private-dry-run-mappings");
            request.Options.DryRun = true;
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(result.Summary.Failed, Is.Zero,
                string.Join("; ", result.Results.Select(row => row.Message)));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(2u));
            Assert.That(result.Results.Single(row => row.ResourceId == "stock-source").RootNodeId.IsNull, Is.True);
            Assert.That(result.Results.Single(row => row.ResourceId == "stock-source").MaterializedNodeCount,
                Is.GreaterThan(0u));
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(1),
                "Validation must run the actual private native candidate pipeline.");
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(1));
            Assert.That(namespaces.ToArrayOf(), Is.EqualTo(before));
            Assert.That(namespaces.Version, Is.EqualTo(version));
            Assert.That(m_server.CurrentInstance.TypeTree.IsKnown(candidateRoot), Is.False);
            Assert.That((await ReadNodeClassAsync(candidateRoot).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_registry.Current, Is.SameAs(registry));
            Assert.That(m_coordinator.LastRefreshPlan, Is.Null);
            Assert.That(probe.DecisionCount, Is.Zero);
            Assert.That(m_events, Is.Empty);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task DryRunReplacementAndNoOpKeepTheExactLiveImage(bool changed, bool force)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-dry-run")).ConfigureAwait(false);
            if (changed)
            {
                await UpsertStockSourceAsync(true).ConfigureAwait(false);
            }
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            WotCommittedPublicationState published = m_coordinator.CommittedPublication;
            WotRefreshRequest request = HandoffRequest("replacement-dry-run", 1, force);
            request.Options.DryRun = true;
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(result.Summary.Failed, Is.Zero,
                string.Join("; ", result.Results.Select(row => row.Message)));
            Assert.That(result.Summary.Unchanged, Is.EqualTo(changed || force ? 0u : 2u));
            Assert.That(result.Results.All(row => row.Generation == 1), Is.True);
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.CommittedPublication, Is.SameAs(published));
            Assert.That(m_coordinator.LastRefreshPlan!.RequestId, Is.EqualTo("before-dry-run"));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(changed || force ? 2 : 1));
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(changed || force ? 1 : 0));
            Assert.That(m_events, Is.Empty);
        }

        [TestCase(WoTAtomicityEnum.PerResource)]
        [TestCase(WoTAtomicityEnum.PerGroup)]
        [TestCase(WoTAtomicityEnum.PerClosure)]
        [TestCase(WoTAtomicityEnum.PerRegistry)]
        public async Task DryRunValidatesExactPrerequisiteUnitsWithoutCommitting(WoTAtomicityEnum atomicity)
        {
            WotResource dependency = await AddUnitModelAsync("dependency").ConfigureAwait(false);
            WotResource dependent = await AddAsync("dependent").ConfigureAwait(false);
            await SetUnitDependencyAsync(dependent, "dependency").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [UnitSelector(dependent)],
                Options = new WoTRefreshOptionsDataType { Atomicity = atomicity, DryRun = true }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Atomicity, Is.EqualTo(atomicity));
            Assert.That(result.Summary.Failed, Is.Zero,
                string.Join("; ", result.Results.Select(row => row.Message)));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(2u));
            Assert.That(result.Results.Select(row => row.Xid), Is.EquivalentTo(new[] { dependency.Xid, dependent.Xid }));
            Assert.That(result.Results.All(row => row.Generation == 0), Is.True);
            Assert.That(result.NewGeneration, Is.Zero);
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That((await ReadNodeClassAsync(Root(dependency)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_events, Is.Empty);
        }

        [Test]
        public async Task DryRunFailedPrerequisiteKeepsTheIndependentPrediction()
        {
            WotResource dependency = await AddUnitModelAsync("dependency").ConfigureAwait(false);
            WotResource dependent = await AddAsync("dependent").ConfigureAwait(false);
            WotResource independent = await AddAsync("independent").ConfigureAwait(false);
            await SetUnitDependencyAsync(dependent, "dependency").ConfigureAwait(false);
            m_converter.MarkInvalid(dependency.ResourceId);
            WotRegistrySnapshot before = m_registry.Current;
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource, DryRun = true }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Total, Is.EqualTo(3u));
            Assert.That(result.Summary.Failed, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Results.Single(row => row.Xid == dependent.Xid).Phase,
                Is.EqualTo(WoTPhaseEnum.DependencyResolution));
            Assert.That(result.NewGeneration, Is.Zero);
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That((await ReadNodeClassAsync(Root(independent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_events, Is.Empty);

            WotRefreshResult actual = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);
            WoTResourceLoadResultDataType predicted = result.Results.Single(row => row.Xid == independent.Xid);
            WoTResourceLoadResultDataType committed = actual.Results.Single(row => row.Xid == independent.Xid);
            Assert.That(committed.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(predicted.Outcome, Is.EqualTo(committed.Outcome));
            Assert.That(predicted.Message, Is.EqualTo(committed.Message));
            Assert.That(actual.NewGeneration, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(independent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task DryRunRejectsAViewFootprintOutsideItsActualUnit()
        {
            (WotMaterializationCoordinator coordinator, WotResource source, WotResource unrelated, WotResource view) =
                await CreateViewFootprintViolationAsync().ConfigureAwait(false);
            using var lifetime = coordinator;
            WotRegistrySnapshot before = m_registry.Current;

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [UnitSelector(view)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry, DryRun = true }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Failed, Is.EqualTo(2u));
            Assert.That(result.Results.All(row => row.Phase == WoTPhaseEnum.Activation), Is.True);
            Assert.That(result.Results.All(row => row.Message!.Contains(
                "outside its publication unit", StringComparison.Ordinal)), Is.True);
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(coordinator.LastRefreshPlan, Is.Null);
            Assert.That((await ReadNodeClassAsync(Root(unrelated)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(source)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task DryRunRetainsAnEarlierIndependentPredictionWhenNativeViewPreparationFails()
        {
            (WotMaterializationCoordinator coordinator, WotResource source, WotResource unrelated, WotResource view) =
                await CreateViewFootprintViolationAsync().ConfigureAwait(false);
            using var lifetime = coordinator;
            WotResource independent = await AddAsync("aaa-independent").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [UnitSelector(view), UnitSelector(independent)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource, DryRun = true }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(result.Summary.Total, Is.EqualTo(3u));
            Assert.That(result.Summary.Failed, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Results.Single(row => row.Xid == independent.Xid).Outcome,
                Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(coordinator.LastRefreshPlan, Is.Null);
            Assert.That((await ReadNodeClassAsync(Root(unrelated)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(source)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(independent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task CancelledDryRunReleasesPrivateCandidatesAndAdmitsTheActualRefresh()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<string> namespaces = m_server.CurrentInstance.NamespaceUris.ToArrayOf();
            using var cancellation = new CancellationTokenSource();
            probe.OnRuntimeCreated = cancellation.Cancel;
            WotRefreshRequest request = HandoffRequest("cancelled-dry-run");
            request.Options.DryRun = true;
            m_events.Clear();

            await Assert.ThatAsync(async () =>
                await m_coordinator.RefreshAsync(request, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.CurrentInstance.NamespaceUris.ToArrayOf(), Is.EqualTo(namespaces));
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(1));
            Assert.That(probe.RuntimeDisposedCount, Is.EqualTo(1));
            Assert.That(probe.DecisionCount, Is.Zero);
            Assert.That(m_events, Is.Empty);
            probe.OnRuntimeCreated = null;
            request.Options.DryRun = false;

            WotRefreshResult committed = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(committed.Summary.Failed, Is.Zero);
            Assert.That(committed.NewGeneration, Is.EqualTo(1u));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DryRunDoesNotReleaseNativeModelChangeIntent()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 100, 1000, 1, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse monitored = await m_session.CreateMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Neither,
                    [NativeModelChangeItem()], CancellationToken.None).ConfigureAwait(false);
                Assert.That(monitored.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                WotRefreshRequest request = HandoffRequest("native-event-dry-run");
                request.Options.DryRun = true;

                WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

                Assert.That(result.Summary.Failed, Is.Zero);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                PublishResponse dry = await m_session.PublishAsync(null, [], timeout.Token).ConfigureAwait(false);
                Assert.That(dry.SubscriptionId, Is.EqualTo(subscription.SubscriptionId));
                Assert.That(dry.NotificationMessage.NotificationData.IsEmpty, Is.True);
                request.Options.DryRun = false;
                await m_coordinator.RefreshAsync(request).ConfigureAwait(false);
                PublishResponse committed = await PublishNativeModelChangeAsync(subscription.SubscriptionId)
                    .ConfigureAwait(false);
                Assert.That(committed.NotificationMessage.NotificationData.IsEmpty, Is.False,
                    "The same subscription must receive the actual publication's structural intent.");
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DryRunRetirementKeepsTheLiveSourceAndViewUntilTheActualRefresh(bool withView)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource? child = withView
                ? await AddStockViewAsync("child", false).ConfigureAwait(false)
                : null;
            await m_coordinator.RefreshAsync(HandoffRequest("before-preview-retirement")).ConfigureAwait(false);
            await m_registry.SetEnabledAsync(source.GroupId, source.ResourceId, false).ConfigureAwait(false);
            if (child is not null)
            {
                await m_registry.SetEnabledAsync(child.GroupId, child.ResourceId, false).ConfigureAwait(false);
            }
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            WotRefreshRequest request = HandoffRequest("preview-retirement", 1);
            request.Options.DryRun = true;
            m_events.Clear();

            WotRefreshResult preview = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(preview.Summary.Failed, Is.Zero);
            Assert.That(preview.Summary.Retired, Is.Zero);
            Assert.That(preview.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            if (withView)
            {
                await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            }
            Assert.That(probe.RuntimeDisposedCount, Is.Zero);
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(m_events, Is.Empty);
            request.Options.DryRun = false;

            WotRefreshResult retired = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(retired.NewGeneration, Is.EqualTo(2u));
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(probe.DecisionCount, Is.EqualTo(2));
        }

        [Test]
        public async Task DryRunDoesNotPerformPendingAuthoritativeRecovery()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-unresolved-dry-run")).ConfigureAwait(false);
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            m_indeterminateDecision = true;
            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                HandoffRequest("unresolved-before-dry-run", 1)).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);
            m_indeterminateDecision = false;
            int created = probe.RuntimeCreatedCount;
            int decisions = probe.DecisionCount;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            WotRefreshRequest request = HandoffRequest("blocked-dry-run", 1);
            request.Options.DryRun = true;
            m_events.Clear();

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(request).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);

            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(probe.RuntimeCreatedCount, Is.EqualTo(created));
            Assert.That(probe.DecisionCount, Is.EqualTo(decisions));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            Assert.That(m_events, Is.Empty);
        }

        [Test]
        public async Task DryRunRejectsAProviderThatCannotBindTheRequiredNativeReadImage()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-metadata-preview")).ConfigureAwait(false);
            WotResource previous = m_registry.Current.FindResourceByXid(source.Xid)!;
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            probe.RejectReadImages = true;
            WotRefreshRequest request = HandoffRequest("metadata-preview", 1);
            request.Options.DryRun = true;
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(result.Summary.Failed, Is.EqualTo(1u));
            Assert.That(result.Results[0].Phase, Is.EqualTo(WoTPhaseEnum.Activation));
            Assert.That(result.Results[0].Message, Does.Contain("cannot retain read images"));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(m_events, Is.Empty);
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                await NativeResourceReadNodesAsync(source).ConfigureAwait(false), CancellationToken.None)
                .ConfigureAwait(false);
            AssertNativeResourceImage(read, previous, 42);
            request.Options.DryRun = false;

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(request).ConfigureAwait(false),
                Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DryRunRetirementReportsItsPreparationFailureWithoutFailingUnrelatedSkippedResources(
            bool withView)
        {
            await AssertRejectedDryRunRetirementAsync(withView, explicitSelection: true).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DryRunRetirementIncludesFailuresForPreviouslyOmittedResources(bool withView)
        {
            await AssertRejectedDryRunRetirementAsync(withView, explicitSelection: false).ConfigureAwait(false);
        }

        [Test]
        public async Task DryRunLaterUnitFailureDoesNotRelabelAnEarlierValidatedRetirement()
        {
            HandoffProbe probe = ObserveHandoff();
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotResource retired = await AddAsync("retired").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-split-preview")).ConfigureAwait(false);
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            await m_registry.SetEnabledAsync(retired.GroupId, retired.ResourceId, false).ConfigureAwait(false);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            probe.OnRuntimeCreated = () =>
            {
                if (probe.RuntimeCreatedCount > 2)
                {
                    probe.RejectReadImages = true;
                }
            };
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                ExpectedGeneration = 1,
                Selection = [new WoTResourceSelectorDataType { Kind = WoTDocumentKindEnum.All }],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource, DryRun = true }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerResource));
            Assert.That(result.Summary.Total, Is.EqualTo(3u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Summary.Failed, Is.EqualTo(1u));
            Assert.That(result.Summary.Skipped, Is.EqualTo(1u));
            Assert.That(result.Results.Single(row => row.Xid == retired.Xid).Outcome,
                Is.EqualTo(WoTOutcomeEnum.Skipped));
            Assert.That(result.Results.Single(row => row.Xid == first.Xid).Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            WoTResourceLoadResultDataType failed = result.Results.Single(row => row.Xid == second.Xid);
            Assert.That(failed.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(failed.Message, Does.Contain("cannot retain read images"));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That((await ReadNodeClassAsync(Root(retired)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(m_events, Is.Empty);
        }

        private async Task AssertRejectedDryRunRetirementAsync(bool withView, bool explicitSelection)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource? child = withView
                ? await AddStockViewAsync("child", false).ConfigureAwait(false)
                : null;
            await m_coordinator.RefreshAsync(HandoffRequest("before-rejected-retirement")).ConfigureAwait(false);
            WotResource unrelated = await AddAsync("unrelated-disabled").ConfigureAwait(false);
            await m_registry.SetEnabledAsync(unrelated.GroupId, unrelated.ResourceId, false).ConfigureAwait(false);
            await m_registry.SetEnabledAsync(source.GroupId, source.ResourceId, false).ConfigureAwait(false);
            if (child is not null)
            {
                await m_registry.SetEnabledAsync(child.GroupId, child.ResourceId, false).ConfigureAwait(false);
            }
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            probe.RejectReadImages = true;
            WotRefreshRequest request = HandoffRequest("rejected-preview-retirement", 1);
            request.Options.DryRun = true;
            if (explicitSelection)
            {
                request.Selection = [new WoTResourceSelectorDataType { Kind = WoTDocumentKindEnum.All }];
            }
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(result.Summary.Failed, Is.EqualTo(withView ? 2u : 1u));
            Assert.That(result.Summary.Skipped, Is.EqualTo(explicitSelection ? 1u : 0u));
            WoTResourceLoadResultDataType failed = result.Results.Single(row => row.Xid == source.Xid);
            Assert.That(failed.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(failed.Phase, Is.EqualTo(WoTPhaseEnum.Activation));
            Assert.That(failed.Message, Does.Contain("cannot retain read images"));
            Assert.That(failed.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(failed.Generation, Is.EqualTo(1u));
            Assert.That(failed.RootNodeId, Is.EqualTo(StockNode("Source")));
            if (explicitSelection)
            {
                Assert.That(result.Results.Single(row => row.Xid == unrelated.Xid).Outcome,
                    Is.EqualTo(WoTOutcomeEnum.Skipped));
            }
            else
            {
                Assert.That(result.Results.Any(row => row.Xid == unrelated.Xid), Is.False);
            }
            if (child is not null)
            {
                WoTResourceLoadResultDataType failedView = result.Results.Single(row => row.Xid == child.Xid);
                Assert.That(failedView.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(failedView.Phase, Is.EqualTo(WoTPhaseEnum.Activation));
                Assert.That(failedView.RootNodeId, Is.EqualTo(StockView("child")));
                await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
            }
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Summary.Retired, Is.Zero);
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            Assert.That(probe.DecisionCount, Is.EqualTo(1));
            Assert.That(m_events, Is.Empty);
            probe.RejectReadImages = false;

            WotRefreshResult accepted = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(accepted.Summary.Failed, Is.Zero);
            Assert.That(accepted.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_events, Is.Empty);
            request.Options.DryRun = false;
            WotRefreshResult retired = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);
            Assert.That(retired.NewGeneration, Is.EqualTo(2u));
        }
    }
}
