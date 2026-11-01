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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// An admitted all-events create request binds generations published before its item becomes visible.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task AllEventsItemCreatedAfterPublicationBindsCurrentGenerationAsync(
            bool replace,
            bool publishDuringBind)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager source = RequireLifecycleValue(original);
            IServerInternal server = m_server.CurrentInstance;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var entered = new TaskCompletionSource<OperationContext>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bindEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBind = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_server.RequestValidatedAsyncCallback = async context =>
            {
                if (context.RequestType == RequestType.CreateMonitoredItems)
                {
                    entered.TrySetResult(context);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            };
            Task<(uint SubscriptionId, uint MonitoredItemId)>? creating = null;
            NodeManagerBatchResult? publication = null;
            NodeManagerBatchResult? secondPublication = null;
            uint subscriptionId = 0;
            try
            {
                creating = CreateSubscriptionAndEventMonitoredItemAsync(services, ObjectIds.Server);
                OperationContext admitted = await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(admitted.RoutingPinMatchesSnapshot, Is.True);
                Assert.That(server.EventManager.GetMonitoredItems(), Is.Empty,
                    "Publication must reconcile before the admitted request creates its actual event item.");
                TrackingLifecycleNodeManager? current = null;
                IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                    kGeneration2Value,
                    manager => current = manager,
                    replace ? kModelNamespaceUri : kModelNamespaceUri + ":AllEventsAddition");
                await using IPreparedNodeManagerBatch prepared = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [replace
                        ? NodeManagerBatchChange.Replace(registration, factory)
                        : NodeManagerBatchChange.Add(factory)],
                    timeout.Token).ConfigureAwait(false);
                publication = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration target = prepared.Registrations[0];
                TrackingLifecycleNodeManager destination = RequireLifecycleValue(current);
                OperationContext? callbackContext = null;
                bool targetAdmitted = false;
                destination.AllEventsContextCallback = (context, unsubscribe, _) =>
                {
                    if (!unsubscribe)
                    {
                        callbackContext = context;
                        targetAdmitted = target.Identity.Gate.ActiveCount > 0;
                    }
                    return default;
                };
                destination.AllEventsCallback = async (_, unsubscribe, token) =>
                {
                    if (publishDuringBind && !unsubscribe)
                    {
                        bindEntered.TrySetResult(true);
                        await releaseBind.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                };
                Assert.That(destination.AllEventsSubscribeCount, Is.Zero);
                if (replace)
                {
                    Assert.That(publication.Retirement.IsCompleted, Is.False);
                }
                release.SetResult(true);
                TrackingLifecycleNodeManager intermediate = destination;
                NodeManagerRegistration intermediateRegistration = target;
                if (publishDuringBind)
                {
                    await bindEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    TrackingLifecycleNodeManager? latest = null;
                    await using IPreparedNodeManagerBatch second = await m_server.NodeManagerLifecycle.PrepareAsync(
                        [NodeManagerBatchChange.Replace(
                            target,
                            CreateTrackingNodeManagementFactory(
                                kGeneration2Value + 1,
                                manager => latest = manager,
                                replace ? kModelNamespaceUri : kModelNamespaceUri + ":AllEventsAddition"))],
                        timeout.Token).ConfigureAwait(false);
                    secondPublication = await second.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                    Assert.That(secondPublication.Retirement.IsCompleted, Is.False);
                    Assert.That(intermediate.DisposeCount, Is.Zero);
                    target = second.Registrations[0];
                    destination = RequireLifecycleValue(latest);
                    releaseBind.SetResult(true);
                }
                (subscriptionId, uint itemId) = await creating.WaitAsync(timeout.Token).ConfigureAwait(false);
                creating = null;
                await publication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (secondPublication is not null)
                {
                    await secondPublication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(intermediate.AllEventsSubscribeCount, Is.EqualTo(1));
                    Assert.That(intermediate.AllEventsUnsubscribeCount, Is.EqualTo(1));
                    Assert.That(intermediate.DisposeCount, Is.EqualTo(1));
                    Assert.That(intermediateRegistration.Identity.Gate.ActiveCount, Is.Zero);
                    Assert.That(((MasterNodeManager)server.NodeManager).RetiringEventGenerations,
                        Has.None.SameAs(intermediateRegistration.Identity));
                }
                IEventMonitoredItem item = server.EventManager.GetMonitoredItems()
                    .Single(candidate => candidate.Id == itemId);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(item.MonitoringAllEvents, Is.True);
                    Assert.That(callbackContext, Is.SameAs(admitted),
                        "Changing subscription owners must not replace the request's context or authorization identity.");
                    Assert.That(targetAdmitted, Is.True);
                    Assert.That(destination.AllEventsSubscribeCount, Is.EqualTo(1),
                        "A new live generation must not miss an item created by a request pinned before publication.");
                    Assert.That(target.Identity.EventBindings.ContainsKey(item), Is.True,
                        "The subscription must have an exact generation binding, not an unrecorded direct callback.");
                    Assert.That(source.AllEventsSubscribeCount, Is.EqualTo(replace ? 0 : 1),
                        "A retired manager must not acquire an unrecorded subscription from the old routing view.");
                    Assert.That(source.DisposeCount, Is.EqualTo(replace ? 1 : 0));
                    if (publishDuringBind)
                    {
                        Assert.That(intermediateRegistration.Identity.EventBindings[item].State,
                            Is.EqualTo(GenerationEventBindingState.Removed));
                    }
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                subscriptionId = 0;
                Assert.That(destination.AllEventsUnsubscribeCount, Is.EqualTo(1));
                Assert.That(source.AllEventsUnsubscribeCount, Is.EqualTo(replace ? 0 : 1));
                if (publishDuringBind)
                {
                    Assert.That(intermediate.AllEventsUnsubscribeCount, Is.EqualTo(1));
                }
            }
            finally
            {
                release.TrySetResult(true);
                releaseBind.TrySetResult(true);
                m_server.RequestValidatedAsyncCallback = null;
                try
                {
                    if (creating is not null && subscriptionId == 0)
                    {
                        (subscriptionId, _) = await creating.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (publication is not null)
                    {
                        await publication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (secondPublication is not null)
                    {
                        await secondPublication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (subscriptionId != 0)
                    {
                        await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Replacement and client deletion preserve the exact binding while a recorded refresh callback is suspended.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AllEventsBindingAdmissionSurvivesReplacementAndDeletionAsync(bool deleteDuringCallback)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager source = RequireLifecycleValue(original);
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            IEventMonitoredItem item = server.EventManager.GetMonitoredItems()
                .Single(candidate => candidate.Id == itemId);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.AllEventsCallback = async (target, unsubscribe, token) =>
            {
                if (!unsubscribe && ReferenceEquals(target, item))
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }
            };
            Task? modifying = null;
            NodeManagerBatchResult? publication = null;
            try
            {
                modifying = ModifyEventMonitoredItemAsync(services, subscriptionId, itemId);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                TrackingLifecycleNodeManager? replacement = null;
                await using IPreparedNodeManagerBatch prepared = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(
                        registration,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, manager => replacement = manager))],
                    timeout.Token).ConfigureAwait(false);
                publication = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration target = prepared.Registrations[0];
                TrackingLifecycleNodeManager destination = RequireLifecycleValue(replacement);
                Assert.That(publication.Retirement.IsCompleted, Is.False);
                Assert.That(source.DisposeCount, Is.Zero);
                Assert.That(destination.SubscribedAllEventIds.ToList().Count(id => id == itemId), Is.EqualTo(1));
                if (deleteDuringCallback)
                {
                    await DeleteMonitoredItemAsync(services, subscriptionId, itemId)
                        .WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(release.Task.IsCompleted, Is.False);
                    Assert.That(server.EventManager.GetMonitoredItems().Any(candidate => candidate.Id == itemId),
                        Is.False);
                }
                release.SetResult(true);
                await modifying.WaitAsync(timeout.Token).ConfigureAwait(false);
                await publication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(source.AllEventsSubscribeCount, Is.EqualTo(2),
                        "A refresh reaches its existing owner once, independently of the initial subscription.");
                    Assert.That(source.AllEventsUnsubscribeCount, Is.EqualTo(1));
                    Assert.That(source.DisposeCount, Is.EqualTo(1));
                    Assert.That(registration.Identity.Gate.ActiveCount, Is.Zero);
                    if (registration.Identity.EventBindings.TryGetValue(
                        item, out GenerationEventBindingRecord? record))
                    {
                        Assert.That(record.State, Is.EqualTo(GenerationEventBindingState.Removed),
                            "A sealed generation may retain only its released tombstone, not an active binding.");
                    }
                    Assert.That(master.RetiringEventGenerations, Has.None.SameAs(registration.Identity));
                    Assert.That(target.Identity.EventBindings.ContainsKey(item), Is.EqualTo(!deleteDuringCallback));
                    if (!deleteDuringCallback)
                    {
                        Assert.That(target.Identity.EventBindings[item].State,
                            Is.EqualTo(GenerationEventBindingState.Bound));
                    }
                    Assert.That(destination.AllEventsUnsubscribeCount, Is.EqualTo(deleteDuringCallback ? 1 : 0));
                }
                if (!deleteDuringCallback)
                {
                    await DeleteMonitoredItemAsync(services, subscriptionId, itemId).ConfigureAwait(false);
                    Assert.That(destination.AllEventsUnsubscribeCount, Is.EqualTo(1));
                }
                Assert.That(source.AllEventsUnsubscribeCount, Is.EqualTo(1),
                    "Client deletion must not repeat a retired generation's completed release.");
            }
            finally
            {
                release.TrySetResult(true);
                source.AllEventsCallback = (_, _, _) => default;
                try
                {
                    if (modifying is not null)
                    {
                        await modifying.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (publication is not null)
                    {
                        await publication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// An old-view request creating its item after transition discovery still receives the chosen handoff policy.
        /// </summary>
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 0)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 1)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 2)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 3)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 4)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 5)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 6)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 7)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 8)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 9)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 10)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Migrate, 11)]
        [TestCase(NodeManagerMonitoredItemTransitionMode.Invalidate, 0)]
        public async Task ItemCreatedAfterTransitionSnapshotFollowsCommittedPolicyAsync(
            NodeManagerMonitoredItemTransitionMode mode,
            int subsequentChange)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                timeout.Token).ConfigureAwait(false);
            if (subsequentChange >= 10)
            {
                NodeManagerSlot previousSlot = registration.Identity.Slot;
                TrackingLifecycleNodeManager previousSource = RequireLifecycleValue(original);
                NodeManagerBatchChange previousChange = subsequentChange == 11
                    ? NodeManagerBatchChange.Remove(registration)
                    : NodeManagerBatchChange.Replace(
                        registration,
                        CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                        NodeManagerMonitoredItemTransitionMode.Invalidate);
                await using IPreparedNodeManagerBatch previous = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [previousChange], timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult previousResult = await previous.CommitAsync(
                    _ => default, timeout.Token).ConfigureAwait(false);
                await previousResult.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(previousSource.DisposeCount, Is.EqualTo(1));
                if (subsequentChange == 11)
                {
                    registration = await m_server.NodeManagerLifecycle.AddAsync(
                        CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                        timeout.Token).ConfigureAwait(false);
                    Assert.That(registration.Identity.Slot, Is.Not.SameAs(previousSlot));
                }
                else
                {
                    registration = previous.Registrations[0];
                    Assert.That(registration.Identity.Slot, Is.SameAs(previousSlot));
                    Assert.That(previousSlot.LateMigrationRevision, Is.EqualTo(1));
                }
            }
            TrackingLifecycleNodeManager source = RequireLifecycleValue(original);
            IServerInternal server = m_server.CurrentInstance;
            var nodeId = new NodeId(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var migrationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var migrationRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.BeforeCreateItemsAsync = async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            Task<(uint SubscriptionId, uint MonitoredItemId)>? creating = null;
            NodeManagerBatchResult? publication = null;
            NodeManagerBatchResult? nextPublication = null;
            TrackingLifecycleNodeManager? validatingTarget = null;
            uint subscriptionId = 0;
            try
            {
                creating = CreateSubscriptionAndMonitoredItemAsync(services, nodeId, clientHandle: 37);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(registration.Identity.MonitoredItems, Is.Empty,
                    "The real request must pause before its item is bound, not after transition discovery.");
                await using IPreparedNodeManagerBatch replacement = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(
                        registration, CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), mode)],
                    timeout.Token).ConfigureAwait(false);
                publication = await replacement.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration target = replacement.Registrations[0];
                Assert.That(publication.CleanupFailure, Is.Null);
                Assert.That(publication.Retirement.IsCompleted, Is.False,
                    "The paused real request still owns the old routing view.");
                if (subsequentChange is > 0 and < 10)
                {
                    var intermediate = (TrackingLifecycleNodeManager)target.NodeManager;
                    bool overlapsAdoption = subsequentChange is >= 4 and <= 6;
                    if (subsequentChange >= 7)
                    {
                        long policyRevision = target.Identity.Slot.LateMigrationRevision;
                        await using (IPreparedNodeManagerBatch abandoned =
                            await m_server.NodeManagerLifecycle.PrepareAsync(
                                [NodeManagerBatchChange.Replace(
                                    target,
                                    CreateTrackingNodeManagementFactory(kGeneration2Value + 2, _ => { }),
                                    NodeManagerMonitoredItemTransitionMode.Invalidate)],
                                timeout.Token).ConfigureAwait(false))
                        {
                            if (subsequentChange == 8)
                            {
                                using var cancelled = new CancellationTokenSource();
                                cancelled.Cancel();
                                await Assert.ThatAsync(async () =>
                                    await abandoned.CommitAsync(_ => default, cancelled.Token).ConfigureAwait(false),
                                    Throws.InstanceOf<OperationCanceledException>())
                                    .ConfigureAwait(false);
                            }
                            else if (subsequentChange == 9)
                            {
                                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                                    await abandoned.CommitAsync(
                                        _ => throw new InvalidOperationException("Reject the invalidation decision."),
                                        timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                            }
                        }
                        Assert.That(target.Identity.Slot.LateMigrationRevision, Is.EqualTo(policyRevision),
                            "An uncommitted invalidation must not change the slot's migration policy.");
                    }
                    if (overlapsAdoption)
                    {
                        int callbacks = 0;
                        validatingTarget = intermediate;
                        intermediate.MonitoringFilterValidationCallback = async (id, token) =>
                        {
                            if (id == nodeId && Interlocked.Increment(ref callbacks) == 1)
                            {
                                migrationEntered.TrySetResult(true);
                                await migrationRelease.Task.WaitAsync(token).ConfigureAwait(false);
                            }
                        };
                        release.SetResult(true);
                        await migrationEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(callbacks, Is.EqualTo(1));
                            Assert.That(target.Identity.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                            Assert.That(target.Identity.Gate.ActiveCount, Is.GreaterThan(0),
                                "Late source validation must run under the selected target's admission.");
                            Assert.That(registration.Identity.MonitoredItems, Has.Count.EqualTo(1),
                                "The paused validation must precede release of the original source.");
                            Assert.That(target.Identity.MonitoredItems, Is.Empty);
                            Assert.That(intermediate.DisposeCount, Is.Zero);
                        });
                    }
                    bool removes = subsequentChange is 3 or 6;
                    bool invalidates = subsequentChange is 2 or 5;
                    NodeManagerBatchChange nextChange = removes
                        ? NodeManagerBatchChange.Remove(target)
                        : NodeManagerBatchChange.Replace(
                            target,
                            CreateTrackingNodeManagementFactory(kGeneration2Value + 1, _ => { }),
                            invalidates
                                ? NodeManagerMonitoredItemTransitionMode.Invalidate
                                : NodeManagerMonitoredItemTransitionMode.Migrate);
                    await using IPreparedNodeManagerBatch nextReplacement =
                        await m_server.NodeManagerLifecycle.PrepareAsync(
                            [nextChange], timeout.Token).ConfigureAwait(false);
                    nextPublication = await nextReplacement.CommitAsync(
                        _ => default, timeout.Token).ConfigureAwait(false);
                    if (overlapsAdoption)
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(nextPublication.Retirement.IsCompleted, Is.False,
                                $"Target admission must hold retirement across late validation and adoption. " +
                                $"Task={nextPublication.Retirement.Status}; " +
                                $"Failure={nextPublication.Retirement.Exception}; " +
                                $"Gate={target.Identity.Gate.State}; Active={target.Identity.Gate.ActiveCount}; " +
                                $"Disposed={intermediate.DisposeCount}.");
                            Assert.That(target.Identity.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                            Assert.That(target.Identity.Gate.ActiveCount, Is.GreaterThan(0));
                            Assert.That(registration.Identity.MonitoredItems, Has.Count.EqualTo(1));
                            Assert.That(target.Identity.MonitoredItems, Is.Empty);
                            Assert.That(intermediate.DisposeCount, Is.Zero);
                        });
                        migrationRelease.SetResult(true);
                    }
                    await nextPublication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(intermediate.DisposeCount, Is.EqualTo(1));
                    if (!removes)
                    {
                        target = nextReplacement.Registrations[0];
                    }
                }
                release.TrySetResult(true);
                (subscriptionId, uint itemId) = await creating.WaitAsync(timeout.Token).ConfigureAwait(false);
                await publication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                ISubscription subscription = server.SubscriptionManager.GetSubscriptions()
                    .Single(candidate => candidate.Id == subscriptionId);
                var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
                Assert.That(tracker.HasMonitoredItems(source), Is.False);
                bool migrates = mode == NodeManagerMonitoredItemTransitionMode.Migrate &&
                    subsequentChange is 0 or 1 or 4 or >= 7;
                Assert.That(tracker.HasMonitoredItems(target.NodeManager),
                    Is.EqualTo(migrates),
                    "A compatible late item must migrate rather than becoming terminal during retirement.");
                Assert.That(source.DisposeCount, Is.EqualTo(1));
                subscription.GetMonitoredItems(out ArrayOf<uint> ids, out _);
                Assert.That(ids.ToArray(), Does.Contain(itemId));
                (DataValue? value, _) = await PublishForDataChangeAsync(
                    services, subscriptionId, default, clientHandle: 37).ConfigureAwait(false);
                Assert.That(value.HasValue, Is.True);
                if (migrates)
                {
                    Assert.That(value.GetValueOrDefault().StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.GetValueOrDefault().WrappedValue.GetInt32(),
                        Is.EqualTo(kGeneration2Value + (subsequentChange is 0 or >= 10 ? 0 : 1)));
                }
                else
                {
                    Assert.That(value.GetValueOrDefault().StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                }
            }
            finally
            {
                release.TrySetResult(true);
                migrationRelease.TrySetResult(true);
                if (validatingTarget is not null)
                {
                    validatingTarget.MonitoringFilterValidationCallback = null;
                }
                source.BeforeCreateItemsAsync = null;
                try
                {
                    if (creating is not null)
                    {
                        (subscriptionId, _) = await creating.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (publication is not null)
                    {
                        await publication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (nextPublication is not null)
                    {
                        await nextPublication.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (subscriptionId != 0)
                    {
                        await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                    }
                }
            }
        }

        [Test]
        public async Task PublicationRaceBindsSessionOnceWhenActivationObservesCurrentViewAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);

            TrackingLifecycleNodeManager? candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(
                        registration,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager))
                ],
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(candidate);

            var activationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseActivation = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var decisionEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDecision = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            original.SessionActivatedCallback = async token =>
            {
                activationEntered.TrySetResult(true);
                await releaseActivation.Task.WaitAsync(token).ConfigureAwait(false);
            };

            Task<(RequestHeader Header, SecureChannelContext Channel)>? lateSession = null;
            Task<NodeManagerBatchResult>? commit = null;
            try
            {
                lateSession = m_server.CreateAndActivateSessionAsync(
                    "ActivationDuringPublicationRace",
                    clientApplicationUri: kClientApplicationUri);
                await activationEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                // Keep the durable decision open while this activation is already admitted
                // against the old view, then let publication race with its completion.
                commit = prepared.CommitAsync(async token =>
                {
                    decisionEntered.TrySetResult(true);
                    await releaseDecision.Task.WaitAsync(token).ConfigureAwait(false);
                }, timeout.Token).AsTask();
                await decisionEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(commit.IsCompleted, Is.False);
                releaseDecision.TrySetResult(true);
                releaseActivation.TrySetResult(true);

                NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                (RequestHeader header, SecureChannelContext channel) =
                    await lateSession.WaitAsync(timeout.Token).ConfigureAwait(false);
                IServerInternal server = m_server.CurrentInstance;
                NodeId newSessionId = RequireLifecycleValue(
                    server.SessionManager.GetSession(header.AuthenticationToken)).Id;
                NodeId existingSessionId = RequireLifecycleValue(
                    server.SessionManager.GetSession(m_requestHeader.AuthenticationToken)).Id;
                NodeId valueNodeId = new(
                    kValueNodeId,
                    (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
                DataValue publishedValue = await ReadValueAsync(valueNodeId).ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(GetBranchRegistration(lifecycle, registration.Id).NodeManager,
                        Is.SameAs(candidate));
                    Assert.That(original.SessionActivatedCount, Is.EqualTo(2),
                        "The old generation observes the setup session and the one racing activation once each.");
                    Assert.That(original.ActivatedSessionIds.ToList().Count(id => id == newSessionId), Is.EqualTo(1));
                    Assert.That(candidate.ActivatedSessionIds.ToList().Count(id => id == newSessionId), Is.EqualTo(1),
                        "The activation admitted before the view swap must be reconciled exactly once to the new generation.");
                    Assert.That(candidate.ActivatedSessionIds.ToList().Count(id => id == existingSessionId), Is.EqualTo(1));
                    Assert.That(publishedValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(publishedValue.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value),
                        "The existing server session must observe the replacement generation's route.");
                }
            }
            finally
            {
                releaseDecision.TrySetResult(true);
                releaseActivation.TrySetResult(true);
                try
                {
                    if (commit is not null)
                    {
                        await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (lateSession is not null)
                    {
                        (RequestHeader header, SecureChannelContext channel) =
                            await lateSession.WaitAsync(timeout.Token).ConfigureAwait(false);
                        header.Timestamp = DateTimeUtc.Now;
                        await m_server.CloseSessionAsync(
                            channel, header, true, RequestLifetime.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    original.SessionActivatedCallback = _ => default;
                }
            }
        }

        [Test]
        public async Task ActivationRaceWithPostPublishReconciliationBindsSessionOnceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);

            TrackingLifecycleNodeManager? candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(
                        registration,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager))
                ],
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(candidate);

            var reconciliationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReconciliation = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var concurrentActivationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int activationCallbacks = 0;
            candidate.SessionActivatedCallback = async token =>
            {
                if (Interlocked.Increment(ref activationCallbacks) == 1)
                {
                    reconciliationEntered.TrySetResult(true);
                    await releaseReconciliation.Task.WaitAsync(token).ConfigureAwait(false);
                }
                else
                {
                    concurrentActivationEntered.TrySetResult(true);
                }
            };

            Task<NodeManagerBatchResult>? commit = null;
            Task<(RequestHeader Header, SecureChannelContext Channel)>? lateSession = null;
            bool concurrentActivationEnteredBeforeEnumerationReleased = false;
            bool candidateViewWasPublishedAtReconciliation = false;
            try
            {
                commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
                await reconciliationEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                candidateViewWasPublishedAtReconciliation = ReferenceEquals(
                    GetBranchRegistration(lifecycle, registration.Id).NodeManager, candidate);

                // The first callback holds reconciliation inside its current session
                // enumeration while a new session is activated against the published view.
                lateSession = m_server.CreateAndActivateSessionAsync(
                    "ActivationDuringPostPublishReconciliation",
                    clientApplicationUri: kClientApplicationUri);
                try
                {
                    await concurrentActivationEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(5), timeout.Token).ConfigureAwait(false);
                    concurrentActivationEnteredBeforeEnumerationReleased =
                        !releaseReconciliation.Task.IsCompleted;
                }
                catch (TimeoutException)
                {
                    // The assertion after releasing the barrier clearly identifies an
                    // activation serialized behind post-publication enumeration.
                }
            }
            finally
            {
                releaseReconciliation.TrySetResult(true);
                try
                {
                    if (commit is not null)
                    {
                        await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (lateSession is not null)
                    {
                        (RequestHeader header, SecureChannelContext channel) =
                            await lateSession.WaitAsync(timeout.Token).ConfigureAwait(false);
                        IServerInternal server = m_server.CurrentInstance;
                        NodeId lateSessionId = RequireLifecycleValue(
                            server.SessionManager.GetSession(header.AuthenticationToken)).Id;
                        NodeId existingSessionId = RequireLifecycleValue(
                            server.SessionManager.GetSession(m_requestHeader.AuthenticationToken)).Id;
                        NodeId valueNodeId = new(
                            kValueNodeId,
                            (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
                        DataValue value = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                        header.Timestamp = DateTimeUtc.Now;
                        try
                        {
                            using (Assert.EnterMultipleScope())
                            {
                                Assert.That(concurrentActivationEnteredBeforeEnumerationReleased, Is.True,
                                    "A concurrently activated session must enter the published generation while reconciliation enumeration is in progress.");
                                Assert.That(candidateViewWasPublishedAtReconciliation, Is.True);
                                Assert.That(candidate.ActivatedSessionIds.ToList().Count(id => id == lateSessionId),
                                    Is.EqualTo(1),
                                    "Reconciliation and direct activation must not miss or duplicate this session binding.");
                                Assert.That(candidate.ActivatedSessionIds.ToList().Count(id => id == existingSessionId),
                                    Is.EqualTo(1));
                                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                                Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
                            }
                        }
                        finally
                        {
                            await m_server.CloseSessionAsync(
                                channel, header, true, RequestLifetime.None).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    candidate.SessionActivatedCallback = _ => default;
                }
            }
        }

        [Test]
        public async Task DurableDecisionDoesNotWaitForUnrelatedBindingCallbackBeforeViewSwapAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint existingItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(services, ObjectIds.Server)
                    .ConfigureAwait(false);

            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);

            TrackingLifecycleNodeManager? candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(
                        registration,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager))
                ],
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(candidate);

            var unrelatedCallbackEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseUnrelatedCallback = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var candidateCallbackEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCandidateCallback = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var decisionReturned = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            bool unrelatedCallbackStillBlockedAtCandidateEntry = false;

            original.AllEventsCallback = async (item, unsubscribe, token) =>
            {
                if (!unsubscribe && item.Id != existingItemId)
                {
                    unrelatedCallbackEntered.TrySetResult(true);
                    await releaseUnrelatedCallback.Task.WaitAsync(token).ConfigureAwait(false);
                }
            };
            candidate.SessionActivatedCallback = async token =>
            {
                candidateCallbackEntered.TrySetResult(true);
                await releaseCandidateCallback.Task.WaitAsync(token).ConfigureAwait(false);
            };

            Task<uint>? heldAdmission = null;
            Task<NodeManagerBatchResult>? commit = null;
            bool candidateCallbackRanBeforeRelease = false;
            bool replacementWasVisibleBeforeRelease = false;
            DataValue valueBeforeRelease = new();
            bool valueReadBeforeRelease = false;
            try
            {
                // Hold the admission gate with an unrelated item callback while a durable
                // decision is completed for the replacement.
                heldAdmission = CreateEventMonitoredItemAsync(
                    services, subscriptionId, ObjectIds.Server, 2);
                await unrelatedCallbackEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                commit = prepared.CommitAsync(_ =>
                {
                    decisionReturned.TrySetResult(true);
                    return default;
                }, timeout.Token).AsTask();
                await decisionReturned.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                // This is a barrier for post-swap candidate work, not a timing-based
                // assumption that the commit continuation has run.
                try
                {
                    await candidateCallbackEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(5), timeout.Token).ConfigureAwait(false);
                    candidateCallbackRanBeforeRelease = true;
                    unrelatedCallbackStillBlockedAtCandidateEntry =
                        !releaseUnrelatedCallback.Task.IsCompleted;
                    replacementWasVisibleBeforeRelease = ReferenceEquals(
                        GetBranchRegistration(lifecycle, registration.Id).NodeManager, candidate);
                    IServerInternal server = m_server.CurrentInstance;
                    NodeId valueNodeId = new(
                        kValueNodeId,
                        (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
                    valueBeforeRelease = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                    valueReadBeforeRelease = true;
                }
                catch (TimeoutException)
                {
                    // The assertion after releasing the callbacks reports this as a failed
                    // publication-isolation requirement instead of leaving work stranded.
                }
            }
            finally
            {
                releaseUnrelatedCallback.TrySetResult(true);
                releaseCandidateCallback.TrySetResult(true);
                try
                {
                    if (heldAdmission is not null)
                    {
                        await heldAdmission.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (commit is not null)
                    {
                        await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    original.AllEventsCallback = (_, _, _) => default;
                    candidate.SessionActivatedCallback = _ => default;
                    await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                }
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidateCallbackRanBeforeRelease, Is.True,
                    "The new generation must begin post-swap reconciliation while unrelated old-generation callback work remains blocked.");
                Assert.That(unrelatedCallbackStillBlockedAtCandidateEntry, Is.True,
                    "The unrelated callback must still be held when candidate work begins.");
                Assert.That(replacementWasVisibleBeforeRelease, Is.True,
                    "The routing view must already expose the committed generation.");
                Assert.That(valueReadBeforeRelease, Is.True);
                Assert.That(valueBeforeRelease.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(valueBeforeRelease.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value),
                    "Client-visible reads must use the replacement route before the unrelated callback is released.");
            }
        }

        [Test]
        public async Task DeletedAllEventsItemDuringBindingIsUnboundAndNotRecordedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint eventItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(services, ObjectIds.Server)
                    .ConfigureAwait(false);

            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(original);

            TrackingLifecycleNodeManager? candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(
                        registration,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager))
                ],
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(candidate);

            var bindEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBind = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            candidate.AllEventsCallback = async (item, unsubscribe, token) =>
            {
                if (!unsubscribe && item.Id == eventItemId)
                {
                    bindEntered.TrySetResult(true);
                    await releaseBind.Task.WaitAsync(token).ConfigureAwait(false);
                }
            };

            Task<NodeManagerBatchResult>? commit = null;
            Task? deletion = null;
            TrackingLifecycleNodeManager? replacement = null;
            bool deletionCompletedWhileBinding = false;
            bool deletedItemWasAbsentWhileBinding = false;
            try
            {
                commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
                await bindEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                // Issue deletion while the candidate is still inside its subscribe callback.
                // It must complete against the live server item set without waiting for the
                // unrelated asynchronous subscribe callback to finish.
                deletion = DeleteMonitoredItemAsync(services, subscriptionId, eventItemId);
                try
                {
                    await deletion.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token).ConfigureAwait(false);
                    deletionCompletedWhileBinding = true;
                    deletedItemWasAbsentWhileBinding = !m_server.CurrentInstance.EventManager
                        .GetMonitoredItems().Any(item => item.Id == eventItemId);
                }
                catch (TimeoutException)
                {
                    // The post-cleanup assertions report that deletion was serialized behind
                    // the binding callback rather than occurring during it.
                }
                releaseBind.TrySetResult(true);
                NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                await deletion.WaitAsync(timeout.Token).ConfigureAwait(false);

                IServerInternal server = m_server.CurrentInstance;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(GetBranchRegistration(lifecycle, registration.Id).NodeManager,
                        Is.SameAs(candidate));
                    Assert.That(candidate.SubscribedAllEventIds.ToList().Count(id => id == eventItemId),
                        Is.EqualTo(1));
                    Assert.That(candidate.UnsubscribedAllEventIds.ToList().Count(id => id == eventItemId),
                        Is.EqualTo(1),
                        "The item deleted during the asynchronous bind must be unsubscribed from the committed generation.");
                    Assert.That(deletionCompletedWhileBinding, Is.True,
                        "The delete operation must finish while the generation's subscribe callback is still held.");
                    Assert.That(deletedItemWasAbsentWhileBinding, Is.True,
                        "The server event manager must no longer expose the item before the bind callback is released.");
                    Assert.That(server.EventManager.GetMonitoredItems().Any(item => item.Id == eventItemId),
                        Is.False,
                        "The deleted all-events item must no longer be live in the server event manager.");
                }

                // A subsequent publication is a public probe for a stale binding record:
                // a deleted item must not be replayed to the next generation.
                await using IPreparedNodeManagerBatch second = await lifecycle.PrepareAsync(
                    [
                        NodeManagerBatchChange.Replace(
                            GetBranchRegistration(lifecycle, registration.Id),
                            CreateTrackingNodeManagementFactory(
                                kGeneration2Value + 1, manager => replacement = manager))
                    ],
                    null,
                    timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(replacement);
                NodeManagerBatchResult secondResult = await second.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                NodeId valueNodeId = new(
                    kValueNodeId,
                    (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
                DataValue replacementValue = await ReadValueAsync(valueNodeId).ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(secondResult.CleanupFailure, Is.Null);
                    Assert.That(GetBranchRegistration(lifecycle, registration.Id).NodeManager,
                        Is.SameAs(replacement));
                    Assert.That(replacement.AllEventsSubscribeCount, Is.Zero);
                    Assert.That(replacement.SubscribedAllEventIds.ToList(), Is.Empty,
                        "A later generation must not inherit a stale record for the deleted item.");
                    Assert.That(replacementValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(replacementValue.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value + 1));
                }
            }
            finally
            {
                releaseBind.TrySetResult(true);
                try
                {
                    if (deletion is not null)
                    {
                        await deletion.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (commit is not null)
                    {
                        await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    candidate.AllEventsCallback = (_, _, _) => default;
                    if (subscriptionId != 0)
                    {
                        await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                    }
                }
            }
        }

        private sealed partial class TrackingLifecycleNodeManager
        {
            /// <summary>
            /// Gets or sets a barrier before a real service request creates and binds data monitored items.
            /// </summary>
            public Func<CancellationToken, ValueTask>? BeforeCreateItemsAsync { get; set; }

            /// <summary>
            /// Gets or sets a barrier during monitored-item source filter validation.
            /// </summary>
            public Func<NodeId, CancellationToken, ValueTask>? MonitoringFilterValidationCallback { get; set; }

            /// <inheritdoc/>
            public override async ValueTask CreateMonitoredItemsAsync(
                OperationContext context,
                uint subscriptionId,
                double publishingInterval,
                TimestampsToReturn timestampsToReturn,
                ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
                IList<ServiceResult> errors,
                IList<MonitoringFilterResult> filterErrors,
                IList<IMonitoredItem> monitoredItems,
                bool createDurable,
                MonitoredItemIdFactory monitoredItemIdFactory,
                CancellationToken cancellationToken = default)
            {
                if (BeforeCreateItemsAsync is { } beforeCreate)
                {
                    await beforeCreate(cancellationToken).ConfigureAwait(false);
                }
                await base.CreateMonitoredItemsAsync(
                    context, subscriptionId, publishingInterval, timestampsToReturn, itemsToCreate,
                    errors, filterErrors, monitoredItems, createDurable, monitoredItemIdFactory,
                    cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            protected override async ValueTask<ValidateMonitoringFilterResult> ValidateMonitoringFilterAsync(
                ServerSystemContext context,
                NodeHandle handle,
                uint attributeId,
                double samplingInterval,
                uint queueSize,
                ExtensionObject filter,
                CancellationToken cancellationToken = default)
            {
                if (MonitoringFilterValidationCallback is { } validating)
                {
                    await validating(handle.NodeId, cancellationToken).ConfigureAwait(false);
                }
                return await base.ValidateMonitoringFilterAsync(
                    context, handle, attributeId, samplingInterval, queueSize, filter, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
