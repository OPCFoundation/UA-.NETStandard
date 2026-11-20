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
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Schedules a failed generation's retry without blocking a new candidate on its physical deletion.
        /// </summary>
        [Test]
        public async Task PreparationDoesNotJoinQuiescentCleanupRetryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? original = null;
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => original = manager)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult1.Registrations[0]);
            var retryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            RequireLifecycleValue(original).BeforeDeleteAddressSpaceAsync = async token =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new SentinelException("First retirement attempt failed.");
                }
                retryStarted.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            LifecycleResult result;
            try
            {
                await using ILifecycleWriter exactWriter1 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges removal = await exactWriter1.PrepareAsync(
                    [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                result = await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                await exactWriter1.DisposeAsync().ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);

                // The second attempt only starts after the first one failed; retirement stays pending
                // until a retry reclaims the generation.
                await retryStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(result.Retired.IsCompleted, Is.False);

                await using IPreparedChanges candidate = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        303, _ => { }, kReadinessProbeNamespaceUri))], timeout.Token)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(candidate.IsCommitted, Is.False);
                Assert.That(RequireLifecycleValue(original).DisposeCount, Is.Zero);
                Assert.That(attempts, Is.EqualTo(2));
            }
            finally
            {
                // Never leave the retry blocked; server shutdown joins admitted cleanup callbacks.
                release.TrySetResult(true);
            }
            await RequireLifecycleValue(original).DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            await Assert.ThatAsync(() => result.Retired.WaitAsync(timeout.Token),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo("First retirement attempt failed."),
                "Retrying cleanup must not replace the original publication's observable failure.")
                .ConfigureAwait(false);
            Assert.That(RequireLifecycleValue(original).DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Returns the committed result without joining physical deletion even when the old view has no pins.
        /// </summary>
        /// <param name="replace">Whether to replace the generation instead of removing it.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublicationReturnsBeforeQuiescentPhysicalDeletionAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? original = null;
            LifecycleResult publicationResult2 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => original = manager)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult2.Registrations[0]);
            var deleting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RequireLifecycleValue(original).BeforeDeleteAddressSpaceAsync = async token =>
            {
                deleting.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            LifecycleChange change = replace
                ? LifecycleChange.Replace(
                    registration.Slot, CreateTrackingNodeManagementFactory(
                    kGeneration2Value, _ => { }), NodeManagerMonitoredItemTransitionMode.Invalidate)
                : LifecycleChange.Remove(registration.Slot);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [change], timeout.Token).ConfigureAwait(false);
            Task<LifecycleResult> publication = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
            LifecycleResult result;
            try
            {
                await deleting.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                result = await publication.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Retired.IsCompleted, Is.False);
                Assert.That(RequireLifecycleValue(original).DisposeCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                await publication.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(RequireLifecycleValue(original).DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Removes a real generation's contributed reference at publication, before physical deletion.
        /// </summary>
        [Test]
        public async Task RemovedGenerationReferenceIsWithdrawnBeforePhysicalRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult publicationResult3 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { })), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult3.Registrations[0]);
            var rootId = new NodeId(kRootNodeId, checked((ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri)));
            BaseObjectState folder = RequireLifecycleValue(
                server.FindPredefinedNode<BaseObjectState>(ObjectIds.ObjectsFolder));
            Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.True);
            NodeManagerRoutingTable.RoutingSnapshot oldView =
                ((IDynamicNodeManagerBatchHost)server.NodeManager).RoutingRevision;
            Assert.That(oldView.TryPin(out int stripe), Is.True);
            LifecycleResult removal;
            try
            {
                await using ILifecycleWriter exactWriter2 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter2.PrepareAsync(
                    [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                removal = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                await exactWriter2.DisposeAsync().ConfigureAwait(false);
                Assert.That(removal.CleanupFailure, Is.Null);
                Assert.That(removal.Retired.IsCompleted, Is.False);
                Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.False);
            }
            finally
            {
                oldView.Epoch.Exit(stripe);
            }
            await removal.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.False);
        }

        /// <summary>
        /// Keeps transaction-owned inbound references intact after a same-identifier generation retires.
        /// </summary>
        /// <param name="overlap">Whether retirement completes after an independent source plan is captured.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredGenerationCannotRemoveReplacementOrganizesReferenceAsync(bool overlap)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? original = null;
            LifecycleResult publicationResult4 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => original = manager)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult4.Registrations[0]);
            var rootId = new NodeId(kRootNodeId, checked((ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri)));
            BaseObjectState folder = RequireLifecycleValue(
                server.FindPredefinedNode<BaseObjectState>(ObjectIds.ObjectsFolder));
            Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.True);

            var deleting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeManagerRoutingTable.RoutingSnapshot oldView =
                ((IDynamicNodeManagerBatchHost)server.NodeManager).RoutingRevision;
            Assert.That(oldView.TryPin(out int stripe), Is.True);
            bool pinned = true;
            RequireLifecycleValue(original).BeforeDeleteAddressSpaceAsync = async token =>
            {
                deleting.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            try
            {
                await using ILifecycleWriter exactWriter3 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter3.PrepareAsync(
                    [LifecycleChange.Replace(
                    registration.Slot, CreateTrackingNodeManagementFactory(
                    kGeneration2Value, _ => { }), NodeManagerMonitoredItemTransitionMode.Invalidate)],
                    timeout.Token).ConfigureAwait(false);
                LifecycleResult replacement = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                await exactWriter3.DisposeAsync().ConfigureAwait(false);
                Assert.That(replacement.CleanupFailure, Is.Null);
                oldView.Epoch.Exit(stripe);
                pinned = false;
                await deleting.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.True);

                int captured = 0;
                if (overlap)
                {
                    RequireRegistration(
                        (await CommitChangeAsync(lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        303,
                        manager => manager.ManagerHandleCallback = async (nodeId, _) =>
                        {
                            // The core owner is visited before candidates, so its reference plan is already frozen.
                            if (nodeId == ObjectIds.ObjectsFolder &&
                                Interlocked.Exchange(ref captured, 1) == 0)
                            {
                                release.TrySetResult(true);
                                await replacement.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                            }
                        },
                        kReadinessProbeNamespaceUri)), timeout.Token).ConfigureAwait(false)).Registrations[0]);
                    Assert.That(captured, Is.EqualTo(1));
                }
                else
                {
                    release.TrySetResult(true);
                    await replacement.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                }

                Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.True,
                    "Retirement must not repeat reference removal already owned by publication.");
            }
            finally
            {
                release.TrySetResult(true);
                if (pinned)
                {
                    oldView.Epoch.Exit(stripe);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedImmediateRetirementFinishesAdmittedEventMutationAsync(bool delete)
        {
            Task exactWriter4Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                IServerInternal server = m_server.CurrentInstance;
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
                {
                    OperationTimeout = 60000,
                    SessionTimeout = 60000
                };
                await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                using Client.ISession session = await client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                    .ConfigureAwait(false);
                TrackingLifecycleNodeManager? original = null;
                TrackingLifecycleNodeManager? candidate = null;
                TrackingLifecycleNodeManager? nested = null;
                LifecycleResult publicationResult5 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, value => original = value)), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration registration = RequireRegistration(publicationResult5.Registrations[0]);
                TrackingLifecycleNodeManager originalManager = original ??
                    throw new AssertionException("The original generation was not created.");
                CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                    null, 100, 100, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
                CreateMonitoredItemsResponse created = await session.CreateMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = ObjectIds.Server,
                                AttributeId = Attributes.EventNotifier
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                Filter = new ExtensionObject(filter),
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    ], timeout.Token).ConfigureAwait(false);
                Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                uint eventId = created.Results[0].MonitoredItemId;
                await using ILifecycleWriter exactWriter4 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter4.PrepareAsync(
                    [LifecycleChange.Replace(registration.Slot, CreateTrackingNodeManagementFactory(
                        kGeneration2Value, value => candidate = value),
                            NodeManagerMonitoredItemTransitionMode.Invalidate)], timeout.Token)
                    .ConfigureAwait(false);
                TrackingLifecycleNodeManager candidateManager = candidate ??
                    throw new AssertionException("The prepared replacement generation was not created.");
                IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                    303, value => nested = value, kReadinessProbeNamespaceUri);
                var factory = new CountingNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<LifecycleResult>? adding = null;
                int callbackClaims = 0;
                originalManager.AllEventsCallback = (item, unsubscribe, _) =>
                {
                    if (item.Id != eventId ||
                        unsubscribe != delete ||
                        Interlocked.CompareExchange(ref callbackClaims, 1, 0) != 0)
                    {
                        return default;
                    }
                    // A callback has no request context, so the add is an ordinary external call. It is not awaited
                    // here: awaiting it would make the admitted callback wait for the commit that drains it.
                    adding = CommitChangeAsync(lifecycle, LifecycleChange.Add(factory), timeout.Token);
                    queued.TrySetResult(true);
                    return default;
                };
                Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }, timeout.Token).AsTask();
                exactWriter4Disposal = exactWriter4.DisposeAsync().AsTask();
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task<StatusCode> mutation = MutateAsync();
                try
                {
                    await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Task<LifecycleResult> pendingAdd = adding ??
                        throw new AssertionException("The external lifecycle add was not started.");
                    Assert.That(pendingAdd.IsCompleted, Is.False, "The external add must not borrow the admission.");
                    Assert.That(factory.CreateCount, Is.Zero);
                    release.TrySetResult(true);
                    await Task.WhenAll(commit, mutation).WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                        .ConfigureAwait(false);
                    InvalidOperationException? stale = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await pendingAdd.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    adding = null;
                    Assert.That(stale?.Message,
                        Is.EqualTo("The publication invocation conflicts with a newer address-space revision."));
                    Assert.That(factory.CreateCount, Is.Zero);
                    adding = CommitChangeAsync(lifecycle, LifecycleChange.Add(factory), timeout.Token);
                    await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                    LifecycleResult result = await commit.ConfigureAwait(false);
                    Assert.That(await mutation.ConfigureAwait(false), Is.EqualTo(StatusCodes.Good));
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(factory.CreateCount, Is.EqualTo(1));
                    Assert.That(server.EventManager.GetMonitoredItems().Any(item => item.Id == eventId),
                        Is.EqualTo(!delete));
                    TrackingLifecycleNodeManager nestedManager = nested ??
                        throw new AssertionException("The nested lifecycle generation was not created.");
                    Assert.That(candidateManager.SubscribedAllEventIds.ToList(),
                        Is.EqualTo(delete ? Array.Empty<uint>() : new[] { eventId }));
                    Assert.That(nestedManager.SubscribedAllEventIds.ToList(),
                        Is.EqualTo(delete ? Array.Empty<uint>() : new[] { eventId }),
                        "Nested lifecycle work must not bind an item already removed from its Subscription.");
                    Assert.That(nestedManager.UnsubscribedAllEventIds.IsEmpty, Is.True);
                }
                finally
                {
                    release.TrySetResult(true);
                    originalManager.AllEventsCallback = (_, _, _) => default;
                    if (adding is not null)
                    {
                        await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    await session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], timeout.Token)
                        .ConfigureAwait(false);
                    await session.CloseAsync(timeout.Token).ConfigureAwait(false);
                }

                async Task<StatusCode> MutateAsync()
                {
                    if (delete)
                    {
                        DeleteMonitoredItemsResponse response = await session.DeleteMonitoredItemsAsync(
                            null, subscription.SubscriptionId, [eventId], timeout.Token).ConfigureAwait(false);
                        return response.Results[0];
                    }
                    ModifyMonitoredItemsResponse modified = await session.ModifyMonitoredItemsAsync(
                        null, subscription.SubscriptionId, TimestampsToReturn.Both,
                        [
                            new MonitoredItemModifyRequest
                            {
                                MonitoredItemId = eventId,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 2,
                                    Filter = new ExtensionObject(filter),
                                    QueueSize = 2,
                                    DiscardOldest = true
                                }
                            }
                        ], timeout.Token).ConfigureAwait(false);
                    return modified.Results[0].StatusCode;
                }
            }
            finally
            {
                await exactWriter4Disposal.ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedRetirementLetsAdmittedSessionReleaseItsNotificationLeaseAsync(bool immediate)
        {
            Task exactWriter5Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                IServerInternal server = m_server.CurrentInstance;
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
                {
                    OperationTimeout = 60000,
                    SessionTimeout = 60000
                };
                await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                TrackingLifecycleNodeManager? original = null;
                TrackingLifecycleNodeManager? candidate = null;
                TrackingLifecycleNodeManager? nested = null;
                LifecycleResult publicationResult6 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, value => original = value)), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration registration = RequireRegistration(publicationResult6.Registrations[0]);
                TrackingLifecycleNodeManager originalManager = original ??
                    throw new AssertionException("The original generation was not created.");
                await using ILifecycleWriter exactWriter5 = await Capture(
                    m_server.NodeManagerLifecycle, registration).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges prepared = await exactWriter5.PrepareAsync(
                    [LifecycleChange.Replace(registration.Slot, CreateTrackingNodeManagementFactory(
                        kGeneration2Value, value => candidate = value),
                            immediate
                                ? NodeManagerMonitoredItemTransitionMode.Invalidate
                                : NodeManagerMonitoredItemTransitionMode.Migrate)], timeout.Token)
                    .ConfigureAwait(false);
                TrackingLifecycleNodeManager candidateManager = candidate ??
                    throw new AssertionException("The prepared replacement generation was not created.");
                IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                    303, value => nested = value, kReadinessProbeNamespaceUri);
                var factory = new CountingNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<LifecycleResult>? adding = null;
                originalManager.SessionActivatedCallback = _ =>
                {
                    // A callback has no request context, so the add is an ordinary external call. It is not awaited
                    // here: awaiting it would make the admitted callback wait for the commit that drains it.
                    adding = CommitChangeAsync(lifecycle, LifecycleChange.Add(factory), timeout.Token);
                    queued.TrySetResult(true);
                    return default;
                };
                Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }, timeout.Token).AsTask();
                exactWriter5Disposal = exactWriter5.DisposeAsync().AsTask();
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task<Client.ISession> activation = client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);
                Client.ISession? session = null;
                try
                {
                    await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Task<LifecycleResult> pendingAdd = adding ??
                        throw new AssertionException("The external lifecycle add was not started.");
                    Assert.That(pendingAdd.IsCompleted, Is.False, "The external add must not borrow the admission.");
                    Assert.That(factory.CreateCount, Is.Zero);
                    release.TrySetResult(true);
                    await Task.WhenAll(commit, activation).WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                        .ConfigureAwait(false);
                    session = await activation.ConfigureAwait(false);
                    InvalidOperationException? stale = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await pendingAdd.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    adding = null;
                    Assert.That(stale?.Message,
                        Is.EqualTo("The publication invocation conflicts with a newer address-space revision."));
                    Assert.That(factory.CreateCount, Is.Zero);
                    adding = CommitChangeAsync(lifecycle, LifecycleChange.Add(factory), timeout.Token);
                    await adding.WaitAsync(timeout.Token).ConfigureAwait(false);
                    LifecycleResult result = await commit.ConfigureAwait(false);
                    ISession initialSession = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken) ??
                        throw new AssertionException("The original server session was not found.");
                    Client.ISession activatedSession = session ??
                        throw new AssertionException("The admitted client session was not activated.");
                    TrackingLifecycleNodeManager nestedManager = nested ??
                        throw new AssertionException("The nested lifecycle generation was not created.");
                    NodeId initialId = initialSession.Id;
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(result.CleanupFailure, Is.Null);
                        Assert.That(prepared.IsCommitted, Is.True);
                        Assert.That(factory.CreateCount, Is.EqualTo(1));
                        Assert.That(candidateManager.ActivatedSessionIds.ToList(),
                            Is.EquivalentTo(new[] { initialId, activatedSession.SessionId }));
                        Assert.That(nestedManager.ActivatedSessionIds.ToList(),
                            Is.EquivalentTo(new[] { initialId, activatedSession.SessionId }));
                    }
                    await originalManager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                    Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                }
                finally
                {
                    release.TrySetResult(true);
                    originalManager.SessionActivatedCallback = _ => default;
                    Client.ISession completedSession = RequireLifecycleValue(session);
                    await completedSession.CloseAsync(timeout.Token).ConfigureAwait(false);
                    completedSession.Dispose();
                }
            }
            finally
            {
                await exactWriter5Disposal.ConfigureAwait(false);
            }
        }
    }
}
