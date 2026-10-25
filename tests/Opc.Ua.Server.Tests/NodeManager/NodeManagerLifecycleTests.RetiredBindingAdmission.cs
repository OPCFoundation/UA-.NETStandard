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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                null, timeout.Token).ConfigureAwait(false);
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
            await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(registration)], null, timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult result = await removal.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            await Assert.ThatAsync(() => result.Retirement.WaitAsync(timeout.Token),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo("First retirement attempt failed."))
                .ConfigureAwait(false);

            Task<IPreparedNodeManagerBatch> preparing = lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    303, _ => { }, kReadinessProbeNamespaceUri))], null, timeout.Token).AsTask();
            try
            {
                await retryStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedNodeManagerBatch candidate = await preparing
                    .WaitAsync(TimeSpan.FromSeconds(5), timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.IsCommitted, Is.False);
                Assert.That(RequireLifecycleValue(original).DisposeCount, Is.Zero);
                Assert.That(attempts, Is.EqualTo(2));
            }
            finally
            {
                release.TrySetResult(true);
                await using IPreparedNodeManagerBatch candidate = await preparing.WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
            }
            await RequireLifecycleValue(original).DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(result.Retirement.IsFaulted, Is.True,
                "Retrying cleanup must not replace the original publication's observable failure.");
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                null, timeout.Token).ConfigureAwait(false);
            var deleting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RequireLifecycleValue(original).BeforeDeleteAddressSpaceAsync = async token =>
            {
                deleting.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            NodeManagerBatchChange change = replace
                ? NodeManagerBatchChange.Replace(registration,
                    CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), true)
                : NodeManagerBatchChange.Remove(registration, true);
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [change], null, timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> publication = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
            NodeManagerBatchResult result;
            try
            {
                await deleting.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                result = await publication.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Retirement.IsCompleted, Is.False);
                Assert.That(RequireLifecycleValue(original).DisposeCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                await publication.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }), null, timeout.Token)
                .ConfigureAwait(false);
            var rootId = new NodeId(kRootNodeId, checked((ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri)));
            BaseObjectState folder = RequireLifecycleValue(
                server.FindPredefinedNode<BaseObjectState>(ObjectIds.ObjectsFolder));
            Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.True);
            NodeManagerRoutingTable.RoutingSnapshot oldView =
                ((IDynamicNodeManagerBatchHost)server.NodeManager).RoutingRevision;
            Assert.That(oldView.TryPin(out int stripe), Is.True);
            NodeManagerBatchResult removal;
            try
            {
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(registration, true)], null, timeout.Token).ConfigureAwait(false);
                removal = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(removal.CleanupFailure, Is.Null);
                Assert.That(removal.Retirement.IsCompleted, Is.False);
                Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.False);
            }
            finally
            {
                oldView.Epoch.Exit(stripe);
            }
            await removal.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
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
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? original = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => original = manager),
                null, timeout.Token)
                .ConfigureAwait(false);
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
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(registration,
                        CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), true)],
                    null, timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult replacement = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(replacement.CleanupFailure, Is.Null);
                oldView.Epoch.Exit(stripe);
                pinned = false;
                await deleting.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(folder.ReferenceExists(ReferenceTypeIds.Organizes, false, rootId), Is.True);

                int captured = 0;
                if (overlap)
                {
                    await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                        303,
                        manager => manager.ManagerHandleCallback = async (nodeId, _) =>
                        {
                            // The core owner is visited before candidates, so its reference plan is already frozen.
                            if (nodeId == ObjectIds.ObjectsFolder &&
                                Interlocked.Exchange(ref captured, 1) == 0)
                            {
                                release.TrySetResult(true);
                                await replacement.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                            }
                        },
                        kReadinessProbeNamespaceUri), null, timeout.Token).ConfigureAwait(false);
                    Assert.That(captured, Is.EqualTo(1));
                }
                else
                {
                    release.TrySetResult(true);
                    await replacement.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
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
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
            {
                OperationTimeout = 60000,
                SessionTimeout = 60000
            };
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager? original = null;
            TrackingLifecycleNodeManager? candidate = null;
            TrackingLifecycleNodeManager? nested = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
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
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(registration, CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value), true)], null, timeout.Token)
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager candidateManager = candidate ??
                throw new AssertionException("The prepared replacement generation was not created.");
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var factory = new CountingNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerRegistration>? adding = null;
            int callbackClaims = 0;
            originalManager.AllEventsCallback = (item, unsubscribe, _) =>
            {
                if (item.Id != eventId || unsubscribe != delete ||
                    Interlocked.CompareExchange(ref callbackClaims, 1, 0) != 0)
                {
                    return default;
                }
                // A callback has no request context, so the add is an ordinary external call. It is not awaited
                // here: awaiting it would make the admitted callback wait for the commit that drains it.
                adding = lifecycle.AddAsync(factory, null, timeout.Token).AsTask();
                queued.TrySetResult(true);
                return default;
            };
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<StatusCode> mutation = MutateAsync();
            try
            {
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task<NodeManagerRegistration> pendingAdd = adding ??
                    throw new AssertionException("The external lifecycle add was not started.");
                Assert.That(pendingAdd.IsCompleted, Is.False, "The external add must not borrow the admission.");
                Assert.That(factory.CreateCount, Is.Zero);
                release.TrySetResult(true);
                await Task.WhenAll(commit, mutation).WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                    .ConfigureAwait(false);
                await pendingAdd.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await commit.ConfigureAwait(false);
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

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedRetirementLetsAdmittedSessionReleaseItsNotificationLeaseAsync(bool immediate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create())
            {
                OperationTimeout = 60000,
                SessionTimeout = 60000
            };
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            TrackingLifecycleNodeManager? original = null;
            TrackingLifecycleNodeManager? candidate = null;
            TrackingLifecycleNodeManager? nested = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kGeneration1Value, value => original = value), null, timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager originalManager = original ??
                throw new AssertionException("The original generation was not created.");
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(registration, CreateTrackingNodeManagementFactory(
                    kGeneration2Value, value => candidate = value), immediate)], null, timeout.Token)
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager candidateManager = candidate ??
                throw new AssertionException("The prepared replacement generation was not created.");
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                303, value => nested = value, kReadinessProbeNamespaceUri);
            var factory = new CountingNodeManagerFactory([kReadinessProbeNamespaceUri], inner.CreateAsync);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerRegistration>? adding = null;
            originalManager.SessionActivatedCallback = _ =>
            {
                // A callback has no request context, so the add is an ordinary external call. It is not awaited
                // here: awaiting it would make the admitted callback wait for the commit that drains it.
                adding = lifecycle.AddAsync(factory, null, timeout.Token).AsTask();
                queued.TrySetResult(true);
                return default;
            };
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<Opc.Ua.Client.ISession> activation = client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);
            Opc.Ua.Client.ISession? session = null;
            try
            {
                await queued.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task<NodeManagerRegistration> pendingAdd = adding ??
                    throw new AssertionException("The external lifecycle add was not started.");
                Assert.That(pendingAdd.IsCompleted, Is.False, "The external add must not borrow the admission.");
                Assert.That(factory.CreateCount, Is.Zero);
                release.TrySetResult(true);
                await Task.WhenAll(commit, activation).WaitAsync(TimeSpan.FromSeconds(10), timeout.Token)
                    .ConfigureAwait(false);
                session = await activation.ConfigureAwait(false);
                await pendingAdd.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await commit.ConfigureAwait(false);
                ISession initialSession = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken) ??
                    throw new AssertionException("The original server session was not found.");
                Opc.Ua.Client.ISession activatedSession = session ??
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
                Opc.Ua.Client.ISession completedSession = RequireLifecycleValue(session);
                await completedSession.CloseAsync(timeout.Token).ConfigureAwait(false);
                completedSession.Dispose();
            }
        }
    }
}
