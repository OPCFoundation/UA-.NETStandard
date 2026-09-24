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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeResourceMutationRetiresItsProjectionWithoutAutoRefresh(bool delete)
        {
            await VerifyResourceMutationAsync(delete, nativeMethod: true).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ProgrammaticResourceMutationRetiresItsProjectionWithoutAutoRefresh(bool delete)
        {
            await VerifyResourceMutationAsync(delete, nativeMethod: false).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedLifecycleDecisionKeepsMetadataAndNativeOwnerForRetry(bool delete)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-rejected-mutation")).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            m_failDecision = true;

            await Assert.ThatAsync(async () =>
            {
                if (delete)
                {
                    await m_registry.DeleteResourceAsync(source.GroupId, source.ResourceId).ConfigureAwait(false);
                }
                else
                {
                    await m_registry.SetEnabledAsync(source.GroupId, source.ResourceId, false).ConfigureAwait(false);
                }
            }, Throws.TypeOf<WotRegistryCommitNotCommittedException>()).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(probe.RuntimeCreatedCount - probe.RuntimeDisposedCount, Is.EqualTo(1));
            m_failDecision = false;
            if (delete)
            {
                await m_registry.DeleteResourceAsync(source.GroupId, source.ResourceId).ConfigureAwait(false);
            }
            else
            {
                await m_registry.SetEnabledAsync(source.GroupId, source.ResourceId, false).ConfigureAwait(false);
            }
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [TestCase(WoTDeletePolicyEnum.Reject)]
        [TestCase(WoTDeletePolicyEnum.Retire)]
        [TestCase(WoTDeletePolicyEnum.Cascade)]
        [TestCase(WoTDeletePolicyEnum.Force)]
        public async Task ProgrammaticUnloadAppliesPolicyWithoutReactivatingItsResolutionInput(WoTDeletePolicyEnum policy)
        {
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotResource model = await AddUnitModelAsync("policy-model").ConfigureAwait(false);
            WotResource dependent = await AddAsync("policy-dependent").ConfigureAwait(false);
            await SetUnitDependencyAsync(dependent, model.ResourceId).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-policy-unload")).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            m_coordinator.DeletePolicy = policy;

            WotRegistryMutationResult result = await m_registry.SetEnabledAsync(
                model.GroupId, model.ResourceId, false).ConfigureAwait(false);

            if (policy == WoTDeletePolicyEnum.Reject)
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That((await ReadNodeClassAsync(Root(model)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                return;
            }
            Assert.That(result.Outcome,
                Is.EqualTo(policy == WoTDeletePolicyEnum.Retire ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Success),
                result.Message);
            WotResource retired = m_registry.Current.FindResourceByXid(model.Xid)!;
            Assert.That(retired.Enabled, Is.False);
            Assert.That(retired.ActiveVersionId, Is.Null);
            Assert.That(retired.FindVersion("v1"), Is.Not.Null);
            Assert.That((await ReadNodeClassAsync(Root(model)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(dependent)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(policy == WoTDeletePolicyEnum.Retire ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
            if (policy == WoTDeletePolicyEnum.Force)
            {
                Assert.That(m_registry.Current.FindResourceByXid(dependent.Xid)!.LoadState,
                    Is.EqualTo(WoTLoadStateEnum.Failed));
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task MetadataOnlyLifecycleMutationDoesNotInventARuntimeGeneration(bool delete, bool warning)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotRegistrySnapshot previous = m_registry.Current;
            m_committedWarning = warning;

            WotRegistryMutationResult result = delete
                ? await m_registry.DeleteResourceAsync(resource.GroupId, resource.ResourceId).ConfigureAwait(false)
                : await m_registry.SetEnabledAsync(resource.GroupId, resource.ResourceId, false).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(warning ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Success));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(previous.Generation + 1));
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(m_registry.Current.RefreshGeneration, Is.Zero);
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            if (delete)
            {
                Assert.That(m_registry.Current.FindResourceByXid(resource.Xid), Is.Null);
            }
            else
            {
                Assert.That(m_registry.Current.FindResourceByXid(resource.Xid)!.Enabled, Is.False);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ExactVersionDeletionRetiresItsActiveOwnerWithoutDeletingSurvivingVersions(
            bool lastVersion, bool nativeMethod)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, allowNativeRefresh: true).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-version-delete")).ConfigureAwait(false);
            if (!lastVersion)
            {
                await UpsertStockSourceAsync(true).ConfigureAwait(false);
            }
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            if (nativeMethod)
            {
                var version = new WoTDocumentTypeClient(m_session, new NodeId(
                    $"WoTRegistry/groups/{resource.GroupId}/resources/{resource.ResourceId}/versions/v1",
                    ResourceId(resource).NamespaceIndex), NUnitTelemetryContext.Create());
                await version.DeleteAsync(0).ConfigureAwait(false);
            }
            else
            {
                WotRegistryMutationResult result = await m_registry.DeleteVersionAsync(
                    resource.GroupId, resource.ResourceId, "v1").ConfigureAwait(false);
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
            }

            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            WotResource? remaining = m_registry.Current.FindResourceByXid(resource.Xid);
            if (lastVersion)
            {
                Assert.That(remaining, Is.Null);
            }
            else
            {
                Assert.That(remaining, Is.Not.Null);
                Assert.That(remaining!.FindVersion("v1"), Is.Null);
                Assert.That(remaining.FindVersion("v2"), Is.Not.Null);
                Assert.That(remaining.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(remaining.ActiveVersionId, Is.Null);
                WotRefreshResult selected = await m_coordinator.RefreshAsync(
                    HandoffRequest("explicit-refresh-after-version-delete")).ConfigureAwait(false);
                Assert.That(selected.Summary.Failed, Is.Zero);
                Assert.That(m_registry.Current.FindResourceByXid(resource.Xid)!.ActiveVersionId, Is.EqualTo("v2"));
            }
        }

        [Test]
        public async Task DeletingAHostedGroupRetiresAllItsNativeResourceOwners()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-group-delete")).ConfigureAwait(false);

            WotRegistryMutationResult result = await m_registry.DeleteGroupAsync(resource.GroupId).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
            Assert.That(m_registry.Current.FindGroup(resource.GroupId), Is.Null);
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GroupDeleteRejectConsidersOnlyDependenciesOutsideTheDeletedCohort(bool externalDependent)
        {
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotResource first = await AddAsync("group-first").ConfigureAwait(false);
            WotResource second = await AddAsync("group-second").ConfigureAwait(false);
            await SetUnitDependencyAsync(second, first.ResourceId).ConfigureAwait(false);
            WotResource? outside = externalDependent
                ? await AddUnitModelAsync("outside-group").ConfigureAwait(false)
                : null;
            if (outside is not null)
            {
                WotRegistryMutationResult dependency = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = outside.GroupId, ResourceId = outside.ResourceId, VersionId = "v1", Kind = outside.Kind,
                    Content = ByteString.From(TestMaterialization.Tm(
                        "urn:" + outside.ResourceId, extendsHrefs: "urn:" + first.ResourceId))
                }).ConfigureAwait(false);
                Assert.That(dependency.Changed, Is.True, dependency.Message);
            }
            await m_coordinator.RefreshAsync(HandoffRequest("before-cohort-delete")).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;

            WotRegistryMutationResult result = await m_registry.DeleteGroupAsync(first.GroupId).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(externalDependent ? WoTOutcomeEnum.Rejected : WoTOutcomeEnum.Success),
                result.Message);
            if (externalDependent)
            {
                Assert.That(m_registry.Current, Is.SameAs(before));
            }
            else
            {
                Assert.That(m_registry.Current.FindGroup(first.GroupId), Is.Null);
            }
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(externalDependent ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(externalDependent ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task LifecycleMutationKeepsUnselectedPendingVersionsUnactivated()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, converter: m_converter).ConfigureAwait(false);
            WotResource target = await AddAsync("mutation-target").ConfigureAwait(false);
            WotResource untouched = await AddAsync("unselected-pending").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-unselected-pending")).ConfigureAwait(false);
            await UpdateHandoffResourceAsync(untouched).ConfigureAwait(false);
            WotResource pending = m_registry.Current.FindResourceByXid(untouched.Xid)!;

            await m_registry.DeleteResourceAsync(target.GroupId, target.ResourceId).ConfigureAwait(false);

            WotResource retained = m_registry.Current.FindResourceByXid(untouched.Xid)!;
            Assert.That(retained.DefaultVersionId, Is.EqualTo("v2"));
            Assert.That(retained.ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(retained.RefreshGeneration, Is.EqualTo(pending.RefreshGeneration));
            Assert.That(retained.RootNodeId, Is.EqualTo(pending.RootNodeId));
            Assert.That((await ReadNodeClassAsync(NewHandoffNode(untouched)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That((await ReadNodeClassAsync(Root(untouched)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(target)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LifecycleCancellationRespectsTheExistingDurableDecision(bool afterDecision)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-mutation-cancellation")).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            using var cancellation = new CancellationTokenSource();
            if (afterDecision)
            {
                probe.AfterDecisionAsync = _ =>
                {
                    cancellation.Cancel();
                    return default;
                };
                WotRegistryMutationResult result = await m_registry.DeleteResourceAsync(
                    resource.GroupId, resource.ResourceId, cancellationToken: cancellation.Token).ConfigureAwait(false);
                Assert.That(result.Changed, Is.True);
                Assert.That(m_registry.Current.FindResourceByXid(resource.Xid), Is.Null);
                Assert.That(m_coordinator.Generation, Is.EqualTo(2u));
                Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            else
            {
                probe.BeforeDecisionAsync = _ =>
                {
                    cancellation.Cancel();
                    return default;
                };
                await Assert.ThatAsync(async () => await m_registry.DeleteResourceAsync(
                    resource.GroupId, resource.ResourceId, cancellationToken: cancellation.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
                Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                probe.BeforeDecisionAsync = null;
                await m_registry.DeleteResourceAsync(resource.GroupId, resource.ResourceId).ConfigureAwait(false);
                Assert.That(m_registry.Current.FindResourceByXid(resource.Xid), Is.Null);
            }
        }

        [Test]
        public async Task LifecycleMutationRejectsAStaleResourceEpochWithoutChangingEitherImage()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-stale-mutation")).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            long epoch = before.FindResourceByXid(resource.Xid)!.MetaEpoch;

            WotRegistryMutationResult result = await m_registry.DeleteResourceAsync(
                resource.GroupId, resource.ResourceId, epoch + 1).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task CoordinatedMutationDoesNotScheduleAnAdditionalAutomaticRefresh()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views, autoRefresh: true).ConfigureAwait(false);
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<WotMaterializationEventArgs> observed = (_, change) =>
            {
                if (change.Kind == WotMaterializationEventKind.RefreshCompleted && change.RequestId == "auto")
                {
                    completed.TrySetResult(true);
                }
            };
            m_coordinator.Event += observed;
            WotResource resource;
            try
            {
                resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            finally
            {
                m_coordinator.Event -= observed;
            }
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            WotRegistryChangedEventArgs? mutation = null;
            m_registry.Changed += (_, change) => mutation = change;
            int decisions = probe.DecisionCount;

            await m_registry.DeleteResourceAsync(resource.GroupId, resource.ResourceId).ConfigureAwait(false);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);

            Assert.That(mutation, Is.Not.Null);
            Assert.That(mutation!.ProjectionOnly, Is.False);
            Assert.That(mutation.MaterializationHandled, Is.True);
            Assert.That(probe.DecisionCount, Is.EqualTo(decisions + 1));
            Assert.That(m_coordinator.Generation, Is.EqualTo(2u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LifecycleRecoveryFollowsTheDecidingRecordWithoutHalfRetirement(bool committed)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-uncertain-delete")).ConfigureAwait(false);
            WotRegistrySnapshot previous = m_registry.Current;
            m_indeterminateDecision = true;

            await Assert.ThatAsync(async () => await m_registry.DeleteResourceAsync(
                resource.GroupId, resource.ResourceId).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(previous));
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            await Assert.ThatAsync(async () => await m_registry.SetEnabledAsync(
                resource.GroupId, resource.ResourceId, false).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            string directory = Path.Combine(m_root, "registry");
            File.Move(Directory.GetFiles(directory,
                committed ? "manifest.json.tmp-*" : "manifest.json.replace-backup-*").Single(),
                Path.Combine(directory, "manifest.json"));
            m_indeterminateDecision = false;

            Assert.That(await m_coordinator.RecoverAsync().ConfigureAwait(false), Is.True);

            Assert.That(m_coordinator.Generation, Is.EqualTo(committed ? 2u : 1u));
            Assert.That(m_registry.Current.FindResourceByXid(resource.Xid), committed ? Is.Null : Is.Not.Null);
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(committed ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good));
            if (!committed)
            {
                await m_registry.DeleteResourceAsync(resource.GroupId, resource.ResourceId).ConfigureAwait(false);
                Assert.That(m_registry.Current.FindResourceByXid(resource.Xid), Is.Null);
            }
        }

        [Test]
        public async Task ReturningToTheLastValidVersionKeepsItsNativeRootAndActiveMetadata()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, converter: m_converter).ConfigureAwait(false);
            WotResource resource = await AddAsync("last-valid").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("last-valid-v1")).ConfigureAwait(false);
            await UpdateHandoffResourceAsync(resource).ConfigureAwait(false);
            m_converter.MarkInvalid(resource.ResourceId);
            WotRefreshResult failed = await m_coordinator.RefreshAsync(HandoffRequest("last-valid-v2", 1))
                .ConfigureAwait(false);
            Assert.That(failed.Summary.Failed, Is.EqualTo(1u));
            m_converter.ClearInvalid(resource.ResourceId);
            await m_registry.SetDefaultVersionAsync(resource.GroupId, resource.ResourceId, "v1").ConfigureAwait(false);

            WotRefreshResult restored = await m_coordinator.RefreshAsync(HandoffRequest("last-valid-return", 1))
                .ConfigureAwait(false);

            Assert.That(restored.NewGeneration, Is.EqualTo(1u));
            WotResource active = m_registry.Current.FindResourceByXid(resource.Xid)!;
            Assert.That(active.ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(active.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(active.RootNodeId, Is.EqualTo(Root(resource)));
            Assert.That(active.MaterializedNodeCount, Is.GreaterThan(0));
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        private async Task VerifyResourceMutationAsync(bool delete, bool nativeMethod)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views, allowNativeRefresh: true).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("before-native-mutation")).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            var document = new WoTDocumentTypeClient(m_session, ResourceId(source), NUnitTelemetryContext.Create());

            if (!nativeMethod)
            {
                WotRegistryMutationResult result = delete
                    ? await m_registry.DeleteResourceAsync(source.GroupId, source.ResourceId).ConfigureAwait(false)
                    : await m_registry.SetEnabledAsync(source.GroupId, source.ResourceId, false).ConfigureAwait(false);
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
            }
            else if (delete)
            {
                await document.DeleteAsync(0).ConfigureAwait(false);
            }
            else
            {
                await document.SetEnabledAsync(false, 0).ConfigureAwait(false);
            }

            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "A completed lifecycle mutation must retire its native projection even when AutoRefresh is false.");
            Assert.That(probe.RuntimeCreatedCount - probe.RuntimeDisposedCount, Is.Zero);
            if (delete)
            {
                Assert.That(m_registry.Current.FindResourceByXid(source.Xid), Is.Null);
            }
            else
            {
                WotResource disabled = m_registry.Current.FindResourceByXid(source.Xid)!;
                Assert.That(disabled.Enabled, Is.False);
                Assert.That(disabled.ActiveVersionId, Is.Null);
                Assert.That(disabled.RootNodeId.IsNull, Is.True);
                Assert.That(disabled.MaterializedNodeCount, Is.Zero);
            }
        }
    }
}
