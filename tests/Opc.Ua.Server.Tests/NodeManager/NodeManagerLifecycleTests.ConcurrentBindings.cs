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
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchReconcilesBindingsAdmittedDuringDecisionAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => originalManager = manager),
                null, timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(original,
                    CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager))],
                timeout.Token).ConfigureAwait(false);
            int preparedSessions = candidate.SessionActivatedCount;
            int preparedEvents = candidate.AllEventsSubscribeCount;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var rejection = new IOException("The durable decision was rejected.");
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (rejectDecision)
                {
                    throw rejection;
                }
            }, timeout.Token).AsTask();
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            RequestHeader admittedHeader = null;
            SecureChannelContext admittedChannel = null;
            uint subscriptionId = 0;
            try
            {
                int waitingSessions;
                int waitingEvents;
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    (admittedHeader, admittedChannel) = await m_server.CreateAndActivateSessionAsync(
                        "AdmittedDuringDecision", clientApplicationUri: kClientApplicationUri).ConfigureAwait(false);
                    (subscriptionId, _) = await CreateSubscriptionAndEventMonitoredItemAsync(
                        services, ObjectIds.Server).ConfigureAwait(false);
                    waitingSessions = candidate.SessionActivatedCount;
                    waitingEvents = candidate.AllEventsSubscribeCount;
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(commit.IsCompleted, Is.False);
                        Assert.That(prepared.IsCommitted, Is.False);
                        Assert.That(originalManager.SessionActivatedCount, Is.EqualTo(2));
                        Assert.That(originalManager.AllEventsSubscribeCount, Is.EqualTo(1));
                        Assert.That(originalManager.DeleteAddressSpaceCount, Is.Zero);
                        Assert.That(originalManager.DisposeCount, Is.Zero);
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                }

                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => commit,
                        Throws.TypeOf<IOException>().With.Message.EqualTo(rejection.Message)).ConfigureAwait(false);
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Retired, Is.EqualTo(1u));
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(preparedSessions, Is.Zero, "Preparation must not activate candidate sessions.");
                    Assert.That(preparedEvents, Is.Zero, "Preparation must not subscribe candidate event sources.");
                    Assert.That(waitingSessions, Is.Zero, "Candidate session effects must wait for commit.");
                    Assert.That(waitingEvents, Is.Zero, "Candidate event effects must wait for commit.");
                    Assert.That(candidate.SessionActivatedCount, Is.EqualTo(rejectDecision ? 0 : 2),
                        "Both still-live sessions must reach the committed candidate exactly once.");
                    Assert.That(candidate.AllEventsSubscribeCount, Is.EqualTo(rejectDecision ? 0 : 1),
                        "The all-events item admitted during the decision must reach the committed candidate once.");
                    Assert.That(prepared.IsCommitted, Is.EqualTo(!rejectDecision));
                    Assert.That(lifecycle.Registrations[0].NodeManager,
                        Is.SameAs(rejectDecision ? originalManager : candidate));
                    Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(rejectDecision ? 0 : 1));
                    Assert.That(originalManager.DisposeCount, Is.EqualTo(rejectDecision ? 0 : 1));
                    Assert.That(candidate.DisposeCount, Is.EqualTo(rejectDecision ? 1 : 0));
                }
            }
            finally
            {
                release.TrySetResult(true);
                if (subscriptionId != 0)
                {
                    await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                }
                if (admittedHeader is not null)
                {
                    admittedHeader.Timestamp = DateTimeUtc.Now;
                    await m_server.CloseSessionAsync(
                        admittedChannel, admittedHeader, true, RequestLifetime.None).ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchDoesNotResurrectRemovedBindingsAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => originalManager = manager),
                null, timeout.Token).ConfigureAwait(false);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            NodeId valueId = new(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
            (uint dataSubscription, uint dataItem) = await CreateSubscriptionAndMonitoredItemAsync(
                services, valueId, 1).ConfigureAwait(false);
            (uint eventSubscription, uint liveBefore) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            uint removedBefore = await CreateEventMonitoredItemAsync(
                services, eventSubscription, ObjectIds.Server, 2).ConfigureAwait(false);
            (RequestHeader closingHeader, _) = await m_server.CreateAndActivateSessionAsync(
                "ClosingBeforeDecision", clientApplicationUri: kClientApplicationUri).ConfigureAwait(false);
            ISession closingSession = server.SessionManager.GetSession(closingHeader.AuthenticationToken);
            using var beforeContext = new OperationContext(closingSession, DiagnosticsMasks.None);
            TrackingLifecycleNodeManager candidate = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(original,
                    CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager))],
                timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (rejectDecision)
                {
                    throw new IOException("Rejected live-binding reconciliation.");
                }
            }, timeout.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            RequestHeader liveHeader = null;
            SecureChannelContext liveChannel = null;
            Task closingBefore = null;
            Task closingDuring = null;
            OperationContext duringContext = null;
            uint liveDuring = 0;
            uint removedDuring = 0;
            try
            {
                try
                {
                    (liveHeader, liveChannel) = await m_server.CreateAndActivateSessionAsync(
                        "LiveDuringDecision", clientApplicationUri: kClientApplicationUri).ConfigureAwait(false);
                    (RequestHeader transientHeader, _) = await m_server.CreateAndActivateSessionAsync(
                        "ClosingDuringDecision", clientApplicationUri: kClientApplicationUri).ConfigureAwait(false);
                    ISession transientSession = server.SessionManager.GetSession(transientHeader.AuthenticationToken);
                    duringContext = new OperationContext(transientSession, DiagnosticsMasks.None);
                    closingBefore = server.CloseSessionAsync(
                        beforeContext, closingSession.Id, false, timeout.Token).AsTask();
                    closingDuring = server.CloseSessionAsync(
                        duringContext, transientSession.Id, false, timeout.Token).AsTask();
                    liveDuring = await CreateEventMonitoredItemAsync(
                        services, eventSubscription, ObjectIds.Server, 3).ConfigureAwait(false);
                    removedDuring = await CreateEventMonitoredItemAsync(
                        services, eventSubscription, ObjectIds.Server, 4).ConfigureAwait(false);
                    await DeleteMonitoredItemAsync(services, eventSubscription, removedBefore).ConfigureAwait(false);
                    await DeleteMonitoredItemAsync(services, eventSubscription, removedDuring).ConfigureAwait(false);
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(closingSession.IsClosing, Is.True);
                        Assert.That(transientSession.IsClosing, Is.True);
                        Assert.That(candidate.ActivatedSessionIds.ToList(), Is.Empty);
                        Assert.That(candidate.ClosedSessionIds.ToList(), Is.Empty);
                        Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.Empty);
                        Assert.That(candidate.UnsubscribedAllEventIds.ToList(), Is.Empty);
                        Assert.That(originalManager.UnsubscribedAllEventIds.ToList(),
                            Is.EquivalentTo(new[] { removedBefore, removedDuring }));
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                }
                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Retired, Is.Zero, "The old generation still owns the data MonitoredItem.");
                }
                await Task.WhenAll(closingBefore, closingDuring).WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeId initialId = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken).Id;
                NodeId liveId = server.SessionManager.GetSession(liveHeader.AuthenticationToken).Id;
                var owner = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager.GetSubscriptions()
                    .Single(subscription => subscription.Id == dataSubscription);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(candidate.ActivatedSessionIds.ToList(),
                        Is.EquivalentTo(rejectDecision ? Array.Empty<NodeId>() : new[] { initialId, liveId }));
                    Assert.That(candidate.SubscribedAllEventIds.ToList(),
                        Is.EquivalentTo(rejectDecision ? Array.Empty<uint>() : new[] { liveBefore, liveDuring }));
                    Assert.That(candidate.UnsubscribedAllEventIds.ToList(), Is.Empty);
                    Assert.That(owner.HasMonitoredItems(originalManager), Is.True);
                    Assert.That(owner.HasMonitoredItems(candidate), Is.False);
                    Assert.That(originalManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(originalManager.DisposeCount, Is.Zero);
                    Assert.That(candidate.DisposeCount, Is.EqualTo(rejectDecision ? 1 : 0));
                    if (rejectDecision)
                    {
                        Assert.That(candidate.ClosedSessionIds.ToList(), Is.Empty);
                    }
                }
                await DeleteMonitoredItemAsync(services, eventSubscription, liveBefore).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(services, eventSubscription, liveDuring).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(candidate.UnsubscribedAllEventIds.ToList(),
                        Is.EquivalentTo(rejectDecision ? Array.Empty<uint>() : new[] { liveBefore, liveDuring }));
                    Assert.That(originalManager.UnsubscribedAllEventIds.ToList(),
                        Is.EquivalentTo(new[] { removedBefore, removedDuring, liveBefore, liveDuring }));
                }
                await DeleteMonitoredItemAsync(services, dataSubscription, dataItem).ConfigureAwait(false);
                await lifecycle.RemoveAsync(lifecycle.Registrations[0], null, timeout.Token).ConfigureAwait(false);
                await originalManager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                duringContext?.Dispose();
                await DeleteSubscriptionAsync(services, eventSubscription).ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, dataSubscription).ConfigureAwait(false);
                if (liveHeader is not null)
                {
                    liveHeader.Timestamp = DateTimeUtc.Now;
                    await m_server.CloseSessionAsync(
                        liveChannel, liveHeader, true, RequestLifetime.None).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task PreparedBatchSerializesAdmissionsAcrossFinalBindingSnapshotAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint originalEvent) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            TrackingLifecycleNodeManager first = null;
            TrackingLifecycleNodeManager second = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue, manager => first = manager)),
                    NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kSecondRegistrationValue, manager => second = manager, kSecondModelNamespaceUri))
                ], timeout.Token).ConfigureAwait(false);
            var bindingEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBinding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            first.AllEventsCallback = async (item, unsubscribe, token) =>
            {
                if (!unsubscribe && item.Id == originalEvent)
                {
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(token.CanBeCanceled, Is.False);
                    bindingEntered.TrySetResult(true);
                    await releaseBinding.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            };
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
            Task<(RequestHeader, SecureChannelContext)> sessionAdmission = null;
            Task<uint> eventAdmission = null;
            try
            {
                await bindingEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                sessionAdmission = m_server.CreateAndActivateSessionAsync(
                    "AtFinalBindingSnapshot", clientApplicationUri: kClientApplicationUri);
                eventAdmission = CreateEventMonitoredItemAsync(services, subscriptionId, ObjectIds.Server, 2);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(sessionAdmission.IsCompleted, Is.False);
                    Assert.That(eventAdmission.IsCompleted, Is.False);
                    Assert.That(first.SessionActivatedCount, Is.EqualTo(1));
                    Assert.That(first.SubscribedAllEventIds.ToList(), Is.EquivalentTo(new[] { originalEvent }));
                    Assert.That(second.SessionActivatedCount, Is.Zero);
                    Assert.That(second.AllEventsSubscribeCount, Is.Zero);
                }
            }
            finally
            {
                releaseBinding.TrySetResult(true);
            }
            NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
            (RequestHeader header, SecureChannelContext channel) =
                await sessionAdmission.WaitAsync(timeout.Token).ConfigureAwait(false);
            uint lateEvent = await eventAdmission.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                IServerInternal server = m_server.CurrentInstance;
                NodeId initialId = server.SessionManager.GetSession(m_requestHeader.AuthenticationToken).Id;
                NodeId lateId = server.SessionManager.GetSession(header.AuthenticationToken).Id;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(first.ActivatedSessionIds.ToList(), Is.EquivalentTo(new[] { initialId, lateId }));
                    Assert.That(second.ActivatedSessionIds.ToList(), Is.EquivalentTo(new[] { initialId, lateId }));
                    Assert.That(first.SubscribedAllEventIds.ToList(),
                        Is.EquivalentTo(new[] { originalEvent, lateEvent }));
                    Assert.That(second.SubscribedAllEventIds.ToList(),
                        Is.EquivalentTo(new[] { originalEvent, lateEvent }));
                }
                await DeleteMonitoredItemAsync(services, subscriptionId, originalEvent).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(services, subscriptionId, lateEvent).ConfigureAwait(false);
                Assert.That(first.UnsubscribedAllEventIds.ToList(),
                    Is.EquivalentTo(new[] { originalEvent, lateEvent }));
                Assert.That(second.UnsubscribedAllEventIds.ToList(),
                    Is.EquivalentTo(new[] { originalEvent, lateEvent }));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
                header.Timestamp = DateTimeUtc.Now;
                await m_server.CloseSessionAsync(channel, header, true, RequestLifetime.None).ConfigureAwait(false);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task PreparedBatchReportsCommittedBindingFailuresAsync(bool failSession, bool badStatus)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint eventId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => originalManager = manager),
                null, timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager failing = null;
            TrackingLifecycleNodeManager succeeding = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(original, CreateTrackingNodeManagementFactory(
                        kGeneration2Value, manager => failing = manager)),
                    NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kSecondRegistrationValue, manager => succeeding = manager, kSecondModelNamespaceUri))
                ], timeout.Token).ConfigureAwait(false);
            var failure = new IOException("Committed binding callback failed.");
            if (failSession)
            {
                failing.SessionActivatedCallback = _ => throw failure;
            }
            else if (badStatus)
            {
                failing.AllEventsSubscribeResult = StatusCodes.BadUnexpectedError;
            }
            else
            {
                failing.AllEventsCallback = (_, unsubscribe, _) => unsubscribe ? default : throw failure;
            }
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.CleanupFailure, Is.Not.Null,
                    "A failed binding after the durable decision must report a committed warning, not success.");
                Assert.That(lifecycle.Registrations.Count, Is.EqualTo(2));
                Assert.That(lifecycle.Registrations.Contains(entry => ReferenceEquals(entry.NodeManager, failing)),
                    Is.True);
                Assert.That(succeeding.SessionActivatedCount, Is.EqualTo(1));
                Assert.That(succeeding.SubscribedAllEventIds.ToList(), Is.EquivalentTo(new[] { eventId }));
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                Assert.That(result.Retired, Is.EqualTo(1u));
            }
            var failures = ((AggregateException)result.CleanupFailure).Flatten().InnerExceptions;
            if (badStatus)
            {
                Assert.That(failures.OfType<ServiceResultException>()
                    .Any(error => error.StatusCode == StatusCodes.BadUnexpectedError), Is.True);
            }
            else
            {
                Assert.That(failures, Does.Contain(failure));
            }
            failing.SessionActivatedCallback = null;
            failing.AllEventsCallback = null;
            failing.AllEventsSubscribeResult = ServiceResult.Good;
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }
    }
}
