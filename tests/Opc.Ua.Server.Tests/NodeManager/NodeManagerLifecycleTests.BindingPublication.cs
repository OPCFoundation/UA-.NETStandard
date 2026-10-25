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
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
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
    }
}
