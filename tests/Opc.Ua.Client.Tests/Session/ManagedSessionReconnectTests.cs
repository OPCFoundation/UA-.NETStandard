/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 *
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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Tests;
using static Opc.Ua.Client.Tests.Stack.Client.Fakes.ManagedSessionReconnectHarness;
using static Opc.Ua.Client.Tests.Stack.Client.Fakes.ObservingSubscriptionEngineFactory;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Exercises bounded managed-session channel recovery with the real session and V2 publish pipeline,
    /// using gated transport scripts and fake time to observe cancellation and generation boundaries.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [NonParallelizable]
    public sealed class ManagedSessionReconnectTests
    {
        /// <summary>
        /// Verifies that session recreation cancels and drains a publish parked at channel readiness,
        /// drops old-generation acknowledgements, and resumes the original subscription and publish worker.
        /// </summary>
        /// <param name="transferSubscriptionsOnRecreate">
        /// Whether to attempt a scripted failing subscription transfer before recreating the subscription.
        /// </param>
        [TestCase(false, TestName = "RecreateDrainsParkedPublishWithoutTransferAsync")]
        [TestCase(true, TestName = "RecreateDrainsParkedPublishWithTransferFallbackAsync")]
        public async Task RecreateDrainsParkedPublishAsync(bool transferSubscriptionsOnRecreate)
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(transferSubscriptionsOnRecreate).ConfigureAwait(false);
            IManagedTransportChannel originalLease = harness.Lease;
            NodeId originalSessionId = harness.Session.SessionId;
            DefaultSubscriptionEngine originalEngine = harness.EngineFactory.Engine;
            ISubscriptionManager originalManager = originalEngine.SubscriptionManager;
            Assert.That(harness.Session.TryGetSubscriptionManager(out ISubscriptionManager manager), Is.True);
            Assert.That(manager, Is.SameAs(originalManager));

            ISubscription subscription = harness.AddSubscription();
            CreateMonitoredItemsRequest initialItems = await WaitForPhaseAsync(
                harness.NextMonitoredItemsAsync().AsTask(), "initial monitored item creation").ConfigureAwait(false);
            Assert.That(initialItems.ItemsToCreate, Has.Count.EqualTo(1));
            uint clientHandle = initialItems.ItemsToCreate[0].RequestedParameters.ClientHandle;
            Assert.That(subscription.MonitoredItems.TryGetMonitoredItemByClientHandle(
                clientHandle, out IMonitoredItem monitoredItem), Is.True);

            WirePublish initialPublish = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "initial wire publish").ConfigureAwait(false);
            PublishAttempt firstAttempt = await WaitForPhaseAsync(
                harness.EngineFactory.NextAttemptAsync(harness.CancellationToken).AsTask(),
                "initial real publish attempt").ConfigureAwait(false);
            Assert.That(firstAttempt.RequestHandle, Is.EqualTo(initialPublish.Request.RequestHeader.RequestHandle));
            initialPublish.ReplyWithValue(clientHandle, 101);
            ReceivedData initial = await WaitForPhaseAsync(
                harness.NextNotificationAsync().AsTask(), "initial notification").ConfigureAwait(false);
            AssertNotification(initial, subscription, monitoredItem, 101);
            await WaitForPhaseAsync(harness.FirstNotification.Entered, "initial notification acknowledgement gate")
                .ConfigureAwait(false);

            Task backoffTimer = harness.Clock.WaitForTimerCreatedAsync(ReconnectDelay);
            Task recovery = harness.StartRecoveryAsync();
            await WaitForPhaseAsync(backoffTimer, "registered recovery backoff timer").ConfigureAwait(false);
            harness.Clock.Advance(ReconnectDelay);
            await WaitForPhaseAsync(harness.TransportReconnect.Entered, "held transport reconnect")
                .ConfigureAwait(false);
            Assert.That(originalLease.State, Is.EqualTo(ChannelState.TransportReconnecting));

            harness.FirstNotification.Release();
            PublishAttempt parked = await WaitForPhaseAsync(
                harness.EngineFactory.NextAttemptAsync(harness.CancellationToken).AsTask(),
                "publish parked in the real ready gate").ConfigureAwait(false);
            Assert.That(parked.Operation.IsCompleted, Is.False);
            Assert.That(parked.CancellationToken.IsCancellationRequested, Is.False);
            AssertAcknowledgement(parked.Acknowledgements);
            Assert.That(harness.Requests.ToList().OfType<PublishRequest>()
                .Select(request => request.RequestHeader.RequestHandle),
                Is.EqualTo([firstAttempt.RequestHandle]),
                "The second real publish must be waiting before the scripted transport.");

            harness.TransportReconnect.Release();
            await WaitForPhaseAsync(harness.RecoveryActivation.Entered, "held recovery activation")
                .ConfigureAwait(false);
            Assert.That(originalLease.State, Is.EqualTo(ChannelState.TransportConnectedSessionReactivating));
            harness.RecoveryActivation.Release();
            await WaitForPhaseAsync(
                harness.ReplacementSession.Entered,
                "replacement CreateSession after parked publish cancellation and drain").ConfigureAwait(false);

            Assert.That(harness.ReplacementSession.IsReleased, Is.False);
            Assert.That(parked.CancellationToken.IsCancellationRequested, Is.True,
                "Recreate must cancel the parked publish before replacement CreateSession can finish.");
            Assert.That(parked.Operation.IsCanceled, Is.True,
                "Recreate must await the real publish unwind, not merely request cancellation.");
            Assert.That(recovery.IsCompleted, Is.False);
            Assert.That(originalLease.State, Is.EqualTo(ChannelState.TransportConnectedSessionReactivating));
            Assert.That(originalEngine.PublishWorkerCount, Is.EqualTo(1));

            Task resumedAckTimer = harness.Clock.WaitForTimerCreatedAsync(subscription.CurrentPublishingInterval);
            Task<WirePublish> resumedPublishTask = harness.NextWirePublishAsync().AsTask();
            harness.ReplacementSession.Release();
            await WaitForPhaseAsync(recovery, "completed session and subscription recreation").ConfigureAwait(false);
            Assert.That(harness.Lease, Is.SameAs(originalLease));
            Assert.That(originalLease.State, Is.EqualTo(ChannelState.Ready));
            Assert.That(harness.Session.SessionId.IsNull, Is.False);
            Assert.That(harness.Session.SessionId, Is.Not.EqualTo(originalSessionId));
            Assert.That(harness.Session.SessionId, Is.EqualTo(new NodeId("session-2", 1)));
            Assert.That(harness.EngineFactory.Engine, Is.SameAs(originalEngine));
            Assert.That(harness.EngineFactory.CreateCount, Is.EqualTo(1));
            Assert.That(harness.Session.TryGetSubscriptionManager(out ISubscriptionManager resumedManager), Is.True);
            Assert.That(resumedManager, Is.SameAs(originalManager));
            Assert.That(resumedManager.Items.Single(), Is.SameAs(subscription));

            Assert.That(harness.Requests.ToList().OfType<CreateSessionRequest>().Count(), Is.EqualTo(2));
            Assert.That(harness.Requests.ToList().OfType<ActivateSessionRequest>().Count(), Is.EqualTo(3));
            Assert.That(harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Count(), Is.EqualTo(2));
            TransferSubscriptionsRequest[] transfers =
                [.. harness.Requests.ToList().OfType<TransferSubscriptionsRequest>()];
            Assert.That(transfers, Has.Length.EqualTo(transferSubscriptionsOnRecreate ? 1 : 0));
            if (transferSubscriptionsOnRecreate)
            {
                Assert.That(transfers[0].SubscriptionIds.ToArray(), Is.EqualTo([SubscriptionId]));
                Assert.That(transfers[0].SendInitialValues, Is.False);
            }

            CreateMonitoredItemsRequest recreatedItems = await WaitForPhaseAsync(
                harness.NextMonitoredItemsAsync().AsTask(), "recreated monitored item").ConfigureAwait(false);
            Assert.That(recreatedItems.SubscriptionId, Is.EqualTo(initialItems.SubscriptionId));
            Assert.That(recreatedItems.SubscriptionId, Is.EqualTo(SubscriptionId));
            Assert.That(recreatedItems.ItemsToCreate, Has.Count.EqualTo(1));
            Assert.That(recreatedItems.ItemsToCreate[0].RequestedParameters.ClientHandle, Is.EqualTo(clientHandle));

            Task resumedPhase = await WaitForPhaseAsync(
                Task.WhenAny(resumedAckTimer, resumedPublishTask), "resumed publish or acknowledgement timer")
                .ConfigureAwait(false);
            if (ReferenceEquals(resumedPhase, resumedAckTimer))
            {
                harness.Clock.Advance(subscription.CurrentPublishingInterval);
            }
            WirePublish resumedPublish = await WaitForPhaseAsync(
                resumedPublishTask, "resumed wire publish").ConfigureAwait(false);
            Assert.That(resumedPublish.Request.SubscriptionAcknowledgements.ToArray(), Is.Empty,
                "Rolled-back acknowledgements must be dropped before the server subscription id is reused.");
            Assert.That(resumedPublish.Request.RequestHeader.AuthenticationToken,
                Is.EqualTo(new NodeId("token-2", 1)));
            resumedPublish.ReplyWithValue(clientHandle, 202);
            ReceivedData resumed = await WaitForPhaseAsync(
                harness.NextNotificationAsync().AsTask(), "notification after recreate").ConfigureAwait(false);
            AssertNotification(resumed, subscription, monitoredItem, 202);

            WirePublish acknowledgement = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "acknowledgement of the new generation").ConfigureAwait(false);
            AssertAcknowledgement(acknowledgement.Request.SubscriptionAcknowledgements);
            Assert.That(originalEngine.PublishWorkerCount, Is.EqualTo(1));
            Assert.That(harness.Logs.Records.Count(record => record.EventId.Name == "PublishWorkerStarted"),
                Is.EqualTo(1), "Recovery must resume the original worker, not silently replace it.");
            Assert.That(harness.Logs.Records.Count(record => record.EventId.Name == "PublishWorkerStopped"), Is.Zero);
            Assert.That(harness.Channels.CreatedChannels, Has.Count.EqualTo(1));
            Assert.That(harness.Channels.CreatedChannels[0].ReconnectCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that the channel deadline cancels and unwinds the stalled inner phase before outer recovery,
        /// without waiting for another keepalive, and restores subscriptions with the original publish worker.
        /// </summary>
        /// <param name="stallActivation">
        /// Whether to stall activation and exercise endpoint failover after a replacement open fails,
        /// rather than stall transport reconnect.
        /// </param>
        [TestCase(false, TestName = "TransportDeadlineStartsOuterRecoveryAfterUnwindAsync")]
        [TestCase(true, TestName = "ActivationDeadlineStartsOuterRecoveryAfterUnwindAsync")]
        public async Task DeadlineStartsOuterRecoveryAfterUnwindAsync(bool stallActivation)
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                ChannelReconnectTimeout = s_recoveryTimeout,
                FailFirstReplacementOpen = stallActivation
            };
            ConfiguredEndpoint? alternate = stallActivation
                ? SessionChannelHarness.CreateEndpoint("opc.tcp://alternate:4841")
                : null;
            alternate?.Description.Server.ApplicationUri = "opc.tcp://localhost:4840";
            await harness.ConnectAsync(false, alternate).ConfigureAwait(false);
            LiveSubscription live = await PrepareSubscriptionAsync(harness).ConfigureAwait(false);
            IManagedTransportChannel originalLease = harness.Lease;
            int initialKeepAliveReads = harness.KeepAliveReadCount;
            (Task recovery, _) = await StartBoundedRecoveryAsync(harness, stallActivation).ConfigureAwait(false);
            AsyncOperationGate stalled = stallActivation ? harness.RecoveryActivation : harness.TransportReconnect;

            harness.Clock.Advance(s_recoveryTimeout - s_boundaryStep);
            Assert.That(stalled.Cancelled.IsCompleted, Is.False);
            Assert.That(recovery.IsCompleted, Is.False);
            AssertNoOuterRecovery(harness);
            Task publishBackoff = harness.Clock.WaitForTimerCreatedAsync(s_publishErrorBackoff);
            harness.Clock.Advance(s_boundaryStep);

            await AssertDeadlineExpiredAsync(harness, recovery).ConfigureAwait(false);
            RecoveryBoundary boundary = await WaitForPhaseAsync(
                harness.OuterRecoveryStarted, "outer policy after the channel deadline").ConfigureAwait(false);
            Assert.That(stallActivation ? boundary.ActivationExited : boundary.TransportExited, Is.True,
                "The expired inner operation must unwind before the outer policy is consulted.");
            Assert.That(stalled.Cancelled.IsCompleted, Is.True);
            Assert.That(stalled.IsReleased, Is.False, "Cancellation, not a test release, must end the stalled phase.");
            Assert.That(boundary.InnerRecoveryInProgress, Is.False);
            Assert.That(boundary.SessionReconnecting, Is.False);
            Assert.That(boundary.ChannelState, Is.EqualTo(ChannelState.Faulted));
            Assert.That(boundary.Elapsed, Is.EqualTo(s_recoveryTimeout));
            Assert.That(boundary.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(harness.Session.KeepAliveInterval)));
            Assert.That(boundary.KeepAliveReads, Is.EqualTo(initialKeepAliveReads),
                "Deadline expiry must hand off without another keepalive request.");

            await WaitForPhaseAsync(harness.ReplacementSession.Entered, "outer replacement CreateSession")
                .ConfigureAwait(false);
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(harness.OuterRecoveryCompleted.IsCompleted, Is.False);
            Assert.That(harness.Channels.CreatedChannels, Has.Count.EqualTo(stallActivation ? 3 : 2));
            string expectedEndpoint = alternate?.Description.EndpointUrl ?? "opc.tcp://localhost:4840";
            Assert.That(harness.Channels.CreatedChannels[^1].OpenedEndpointUrl, Is.EqualTo(expectedEndpoint));
            harness.OuterPolicy.Verify(
                policy => policy.GetNextDelay(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

            Task<TimeSpan> ackTimer = harness.Clock.WaitForTimerCreatedAsync(
                TimeSpan.Zero, live.Subscription.CurrentPublishingInterval);
            harness.ReplacementSession.Release();
            await WaitForPhaseAsync(harness.OuterRecoveryCompleted, "outer recovery and subscription restoration")
                .ConfigureAwait(false);
            Assert.That(harness.Lease, stallActivation ? Is.Not.SameAs(originalLease) : Is.SameAs(originalLease));
            Assert.That(harness.Lease.EndpointDescription.EndpointUrl, Is.EqualTo(expectedEndpoint));
            await AssertRecreatedSubscriptionAsync(harness, live, 2, ackTimer, publishBackoff)
                .ConfigureAwait(false);
            Assert.That(harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Count(), Is.EqualTo(2));
            AssertOriginalWorker(harness, live);
        }

        /// <summary>
        /// Verifies that recovery just below the deadline retains the session, subscription, and worker
        /// and continues publishing beyond the original deadline without invoking the outer policy.
        /// </summary>
        /// <param name="stallActivation">
        /// Whether activation, rather than transport reconnect, is held until just before the deadline.
        /// </param>
        [TestCase(false, TestName = "SlowTransportRecoveryBelowDeadlineAvoidsOuterPolicyAsync")]
        [TestCase(true, TestName = "SlowActivationRecoveryBelowDeadlineAvoidsOuterPolicyAsync")]
        public async Task SlowRecoveryBelowDeadlineAvoidsOuterPolicyAsync(bool stallActivation)
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                ChannelReconnectTimeout = s_recoveryTimeout,
                RecoveryActivationStatus = StatusCodes.Good
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            LiveSubscription live = await PrepareSubscriptionAsync(harness).ConfigureAwait(false);
            IManagedTransportChannel originalLease = harness.Lease;
            NodeId originalSessionId = harness.Session.SessionId;
            (Task recovery, _) = await StartBoundedRecoveryAsync(harness, stallActivation).ConfigureAwait(false);
            AsyncOperationGate stalled = stallActivation ? harness.RecoveryActivation : harness.TransportReconnect;

            harness.Clock.Advance(s_recoveryTimeout - s_boundaryStep);
            Assert.That(recovery.IsCompleted, Is.False);
            Assert.That(stalled.Cancelled.IsCompleted, Is.False);
            AssertNoOuterRecovery(harness);
            harness.TransportReconnect.Release();
            harness.RecoveryActivation.Release();
            await WaitForPhaseAsync(recovery, "successful recovery just below the deadline").ConfigureAwait(false);

            Assert.That(harness.Lease, Is.SameAs(originalLease));
            Assert.That(originalLease.State, Is.EqualTo(ChannelState.Ready));
            Assert.That(harness.Session.SessionId, Is.EqualTo(originalSessionId));
            Assert.That(harness.Session.InnerSession.ChannelRecoveryInProgress, Is.False);
            Assert.That(harness.Requests.ToList().OfType<CreateSessionRequest>().Count(), Is.EqualTo(1));
            Assert.That(harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Count(), Is.EqualTo(1));
            WirePublish publish = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "resumed publish on the original session")
                .ConfigureAwait(false);
            Assert.That(publish.Request.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("token-1", 1)));
            publish.ReplyWithValue(live.ClientHandle, 202, 2);
            ReceivedData notification = await WaitForPhaseAsync(
                harness.NextNotificationAsync().AsTask(), "data after successful bounded recovery")
                .ConfigureAwait(false);
            AssertNotification(notification, live.Subscription, live.MonitoredItem, 202, 2);

            WirePublish acknowledgement = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "fresh acknowledgement before crossing the old deadline")
                .ConfigureAwait(false);
            AssertAcknowledgement(acknowledgement.Request.SubscriptionAcknowledgements, 2);
            harness.Clock.Advance(s_boundaryStep + s_boundaryStep);
            acknowledgement.ReplyWithValue(live.ClientHandle, 303, 3);
            ReceivedData afterDeadline = await WaitForPhaseAsync(
                harness.NextNotificationAsync().AsTask(), "continued delivery after the old deadline")
                .ConfigureAwait(false);
            AssertNotification(afterDeadline, live.Subscription, live.MonitoredItem, 303, 3);
            WirePublish following = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "acknowledgement after the old deadline")
                .ConfigureAwait(false);
            AssertAcknowledgement(following.Request.SubscriptionAcknowledgements, 3);
            AssertNoOuterRecovery(harness);
            Assert.That(stalled.Cancelled.IsCompleted, Is.False);
            Assert.That(harness.Channels.CreatedChannels, Has.Count.EqualTo(1));
            AssertOriginalWorker(harness, live);
        }

        /// <summary>
        /// Verifies that a late, cancellation-ignoring create-session response cannot overwrite the recovered
        /// session generation or cause stale subscription restoration.
        /// </summary>
        [Test]
        public async Task LateCreateSessionResponseCannotReplaceRecoveredGenerationAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                ChannelReconnectTimeout = s_recoveryTimeout
            };
            harness.ReplacementSession.IgnoreCancellation = true;
            await harness.ConnectAsync(false).ConfigureAwait(false);
            LiveSubscription live = await PrepareSubscriptionAsync(harness).ConfigureAwait(false);
            (Task recovery, PublishAttempt parked) = await StartBoundedRecoveryAsync(harness, true)
                .ConfigureAwait(false);
            harness.RecoveryActivation.Release();
            await WaitForPhaseAsync(harness.ReplacementSession.Entered, "old replacement CreateSession")
                .ConfigureAwait(false);
            Assert.That(parked.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(parked.Operation.IsCanceled, Is.True);
            ScriptedChannel firstChannel = harness.Channels.CreatedChannels[0];
            await WaitForPhaseAsync(
                firstChannel.NextOperationAsync<CreateSessionRequest>(harness.CancellationToken).AsTask(),
                "initial CreateSession transport operation").ConfigureAwait(false);
            ScriptedChannel.RequestOperation oldCreate = await WaitForPhaseAsync(
                firstChannel.NextOperationAsync<CreateSessionRequest>(harness.CancellationToken).AsTask(),
                "held CreateSession transport operation").ConfigureAwait(false);
            Assert.That(oldCreate.Operation.IsCompleted, Is.False);

            Task publishBackoff = harness.Clock.WaitForTimerCreatedAsync(s_publishErrorBackoff);
            harness.Clock.Advance(s_recoveryTimeout);
            await AssertDeadlineExpiredAsync(harness, recovery).ConfigureAwait(false);
            await WaitForPhaseAsync(harness.ReplacementSession.Cancelled, "cancellation of the old service token")
                .ConfigureAwait(false);
            RecoveryBoundary boundary = await WaitForPhaseAsync(
                harness.OuterRecoveryStarted, "outer recovery after fenced CreateSession").ConfigureAwait(false);
            Assert.That(boundary.ChannelState, Is.EqualTo(ChannelState.Faulted));
            Assert.That(boundary.Elapsed, Is.EqualTo(s_recoveryTimeout));
            Assert.That(boundary.InnerRecoveryInProgress, Is.False);
            Assert.That(boundary.SessionReconnecting, Is.False);
            Assert.That(boundary.SessionCreateExited, Is.False,
                "Only the low-level transport task may outlive cancellation; the Session must already have unwound.");
            Assert.That(oldCreate.CancellationToken.IsCancellationRequested, Is.True);
            await WaitForPhaseAsync(harness.SuccessorSession.Entered, "outer generation-three CreateSession")
                .ConfigureAwait(false);
            Task<TimeSpan> ackTimer = harness.Clock.WaitForTimerCreatedAsync(
                TimeSpan.Zero, live.Subscription.CurrentPublishingInterval);
            harness.SuccessorSession.Release();
            await WaitForPhaseAsync(harness.OuterRecoveryCompleted, "recovery while the old response is still pending")
                .ConfigureAwait(false);
            WirePublish acknowledgement = await AssertRecreatedSubscriptionAsync(
                harness, live, 3, ackTimer, publishBackoff).ConfigureAwait(false);
            Assert.That(harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Count(), Is.EqualTo(2));
            Assert.That(oldCreate.Operation.IsCompleted, Is.False);

            harness.ReplacementSession.Release();
            IServiceResponse response = await WaitForPhaseAsync(
                oldCreate.Operation, "late old CreateSession response").ConfigureAwait(false);
            Assert.That(response, Is.TypeOf<CreateSessionResponse>());
            Assert.That(((CreateSessionResponse)response).SessionId, Is.EqualTo(new NodeId("session-2", 1)));
            await AssertFreshGenerationAfterLateResponseAsync(harness, live, acknowledgement, 2, 2)
                .ConfigureAwait(false);
            Assert.That(harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Any(
                request => request.RequestHeader.AuthenticationToken == new NodeId("token-2", 1)), Is.False);
            AssertOriginalWorker(harness, live);
        }

        /// <summary>
        /// Verifies that keepalive failures are suppressed during bounded subscription restoration
        /// and deadline expiry hands off to outer recovery without allowing a late response to revive stale state.
        /// </summary>
        /// <param name="lateResponse">
        /// Whether the old subscription-creation script ignores cancellation and returns after the successor recovers.
        /// </param>
        [TestCase(false, TestName = "RestorationDeadlineSuppressesKeepAliveUntilOuterHandoffAsync")]
        [TestCase(true, TestName = "LateSubscriptionResponseCannotResurrectRecoveredGenerationAsync")]
        public async Task RestorationDeadlineSuppressesKeepAliveUntilOuterHandoffAsync(bool lateResponse)
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                ChannelReconnectTimeout = s_recoveryTimeout,
                HoldSubscriptionRestoration = true
            };
            harness.ReplacementSubscription.IgnoreCancellation = lateResponse;
            harness.ReplacementSession.Release();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            LiveSubscription live = await PrepareSubscriptionAsync(harness).ConfigureAwait(false);
            (Task recovery, _) = await StartBoundedRecoveryAsync(harness, true).ConfigureAwait(false);
            harness.RecoveryActivation.Release();
            await WaitForPhaseAsync(harness.ReplacementSubscription.Entered, "provisional-Ready subscription restore")
                .ConfigureAwait(false);
            Assert.That(harness.Lease.State, Is.EqualTo(ChannelState.Ready));
            Assert.That(harness.Session.InnerSession.Reconnecting, Is.False);
            Assert.That(harness.Session.InnerSession.ChannelRecoveryInProgress, Is.True);
            Assert.That(harness.Session.SessionId, Is.EqualTo(new NodeId("session-2", 1)));
            Assert.That(recovery.IsCompleted, Is.False);

            ScriptedChannel firstChannel = harness.Channels.CreatedChannels[0];
            await WaitForPhaseAsync(
                firstChannel.NextOperationAsync<CreateSubscriptionRequest>(harness.CancellationToken).AsTask(),
                "initial CreateSubscription transport operation").ConfigureAwait(false);
            ScriptedChannel.RequestOperation oldCreate = await WaitForPhaseAsync(
                firstChannel.NextOperationAsync<CreateSubscriptionRequest>(harness.CancellationToken).AsTask(),
                "held restoration transport operation").ConfigureAwait(false);
            var originalKeepAlive = TimeSpan.FromMilliseconds(harness.Session.KeepAliveInterval);
            var keepAliveInterval = TimeSpan.FromMilliseconds(100);
            harness.KeepAliveStatus = StatusCodes.BadNoCommunication;
            Task keepAliveTimer = harness.Clock.WaitForTimerChangedAsync(keepAliveInterval, keepAliveInterval);
            harness.Session.KeepAliveInterval = (int)keepAliveInterval.TotalMilliseconds;
            await WaitForPhaseAsync(keepAliveTimer, "registered short keepalive interval").ConfigureAwait(false);
            harness.Clock.Advance(keepAliveInterval);
            Assert.That(
                await WaitForPhaseAsync(harness.FailedKeepAlive, "real failed keepalive during restoration")
                    .ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(harness.Logs.Records.Count(
                record => record.EventId.Name == "ManagedSessionKeepAliveFailureSuppressedWhile"), Is.GreaterThan(0));
            AssertNoOuterRecovery(harness);
            Assert.That(harness.Lease.State, Is.EqualTo(ChannelState.Ready));
            Assert.That(recovery.IsCompleted, Is.False);

            harness.KeepAliveStatus = StatusCodes.Good;
            Task resetKeepAlive = harness.Clock.WaitForTimerChangedAsync(originalKeepAlive, originalKeepAlive);
            harness.Session.KeepAliveInterval = (int)originalKeepAlive.TotalMilliseconds;
            await WaitForPhaseAsync(resetKeepAlive, "restored keepalive interval").ConfigureAwait(false);
            Task publishBackoff = harness.Clock.WaitForTimerCreatedAsync(s_publishErrorBackoff);
            harness.Clock.Advance(s_recoveryTimeout - keepAliveInterval);
            await AssertDeadlineExpiredAsync(harness, recovery).ConfigureAwait(false);
            RecoveryBoundary boundary = await WaitForPhaseAsync(
                harness.OuterRecoveryStarted, "outer handoff after subscription restoration expires")
                .ConfigureAwait(false);
            Assert.That(boundary.ChannelState, Is.EqualTo(ChannelState.Faulted));
            Assert.That(boundary.Elapsed, Is.EqualTo(s_recoveryTimeout));
            Assert.That(boundary.InnerRecoveryInProgress, Is.False);
            Assert.That(boundary.SessionReconnecting, Is.False);
            Assert.That(boundary.SubscriptionCreateExited, Is.EqualTo(!lateResponse));
            Assert.That(oldCreate.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(harness.ReplacementSubscription.IsReleased, Is.False);
            await WaitForPhaseAsync(harness.SuccessorSession.Entered, "replacement after failed restoration")
                .ConfigureAwait(false);
            Task<TimeSpan> ackTimer = harness.Clock.WaitForTimerCreatedAsync(
                TimeSpan.Zero, live.Subscription.CurrentPublishingInterval);
            harness.SuccessorSession.Release();
            await WaitForPhaseAsync(harness.OuterRecoveryCompleted, "outer completion after restoration timeout")
                .ConfigureAwait(false);
            WirePublish acknowledgement = await AssertRecreatedSubscriptionAsync(
                harness, live, 3, ackTimer, publishBackoff).ConfigureAwait(false);

            if (lateResponse)
            {
                int subscriptionCreates = harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Count();
                int monitoredItemCreates = harness.Requests.ToList().OfType<CreateMonitoredItemsRequest>().Count();
                Assert.That(oldCreate.Operation.IsCompleted, Is.False);
                harness.ReplacementSubscription.Release();
                IServiceResponse response = await WaitForPhaseAsync(
                    oldCreate.Operation, "late old CreateSubscription response").ConfigureAwait(false);
                Assert.That(response, Is.TypeOf<CreateSubscriptionResponse>());
                Assert.That(((CreateSubscriptionResponse)response).SubscriptionId, Is.EqualTo(SubscriptionId));
                await AssertFreshGenerationAfterLateResponseAsync(
                    harness, live, acknowledgement, subscriptionCreates, monitoredItemCreates)
                    .ConfigureAwait(false);
            }
            else
            {
                Assert.That(oldCreate.Operation.IsCanceled, Is.True);
            }
            AssertOriginalWorker(harness, live);
        }

        private static async Task<LiveSubscription> PrepareSubscriptionAsync(ManagedSessionReconnectHarness harness)
        {
            harness.Clock.Advance(TimeSpan.Zero);
            await WaitForPhaseAsync(harness.InitialKeepAliveRead, "initial real keepalive request")
                .ConfigureAwait(false);
            ISubscription subscription = harness.AddSubscription();
            CreateMonitoredItemsRequest items = await WaitForPhaseAsync(
                harness.NextMonitoredItemsAsync().AsTask(), "initial monitored item").ConfigureAwait(false);
            Assert.That(items.ItemsToCreate, Has.Count.EqualTo(1));
            uint clientHandle = items.ItemsToCreate[0].RequestedParameters.ClientHandle;
            Assert.That(subscription.MonitoredItems.TryGetMonitoredItemByClientHandle(
                clientHandle, out IMonitoredItem monitoredItem), Is.True);
            WirePublish publish = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "initial wire publish").ConfigureAwait(false);
            PublishAttempt attempt = await WaitForPhaseAsync(
                harness.EngineFactory.NextAttemptAsync(harness.CancellationToken).AsTask(),
                "initial real publish attempt").ConfigureAwait(false);
            Assert.That(attempt.RequestHandle, Is.EqualTo(publish.Request.RequestHeader.RequestHandle));
            publish.ReplyWithValue(clientHandle, 101);
            ReceivedData notification = await WaitForPhaseAsync(
                harness.NextNotificationAsync().AsTask(), "initial data").ConfigureAwait(false);
            AssertNotification(notification, subscription, monitoredItem, 101);
            await WaitForPhaseAsync(harness.FirstNotification.Entered, "initial notification completion gate")
                .ConfigureAwait(false);
            return new LiveSubscription(subscription, monitoredItem, clientHandle, harness.EngineFactory.Engine);
        }

        private static async Task<(Task Recovery, PublishAttempt Parked)> StartBoundedRecoveryAsync(
            ManagedSessionReconnectHarness harness,
            bool stallActivation)
        {
            Task deadlineTimer = harness.Clock.WaitForTimerCreatedAsync(s_recoveryTimeout);
            Task recovery = harness.StartRecoveryAsync();
            await WaitForPhaseAsync(deadlineTimer, "registered channel deadline").ConfigureAwait(false);
            await WaitForPhaseAsync(harness.TransportReconnect.Entered, "held transport reconnect")
                .ConfigureAwait(false);
            harness.FirstNotification.Release();
            PublishAttempt parked = await WaitForPhaseAsync(
                harness.EngineFactory.NextAttemptAsync(harness.CancellationToken).AsTask(),
                "publish parked during bounded recovery").ConfigureAwait(false);
            Assert.That(parked.Operation.IsCompleted, Is.False);
            AssertAcknowledgement(parked.Acknowledgements);
            Assert.That(harness.Requests.ToList().OfType<PublishRequest>().Count(), Is.EqualTo(1));
            if (stallActivation)
            {
                harness.TransportReconnect.Release();
                await WaitForPhaseAsync(harness.RecoveryActivation.Entered, "held recovery activation")
                    .ConfigureAwait(false);
            }
            return (recovery, parked);
        }

        private static async Task AssertDeadlineExpiredAsync(
            ManagedSessionReconnectHarness harness,
            Task recovery)
        {
            await WaitForPhaseAsync(recovery, "failed channel cycle at its deadline").ConfigureAwait(false);
            RecordedLogRecord[] expirations = [.. harness.Logs.Records
                .Where(record => record.EventId.Name == "ChannelReconnectDeadlineExpired")];
            Assert.That(expirations, Has.Length.EqualTo(1));
            Assert.That(expirations[0].Properties.TryGetValue("Duration", out object? duration), Is.True);
            Assert.That(duration, Is.EqualTo(s_recoveryTimeout));
            Assert.That(expirations[0].Properties.TryGetValue("Elapsed", out object? elapsed), Is.True);
            Assert.That(elapsed, Is.EqualTo(s_recoveryTimeout));
        }

        private static async Task<WirePublish> AssertRecreatedSubscriptionAsync(
            ManagedSessionReconnectHarness harness,
            LiveSubscription live,
            int sessionNumber,
            Task<TimeSpan> ackTimer,
            Task publishBackoff)
        {
            string suffix = sessionNumber.ToString(CultureInfo.InvariantCulture);
            var token = new NodeId($"token-{suffix}", 1);
            Assert.That(harness.Session.SessionId, Is.EqualTo(new NodeId($"session-{suffix}", 1)));
            Assert.That(harness.Lease.State, Is.EqualTo(ChannelState.Ready));
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            Assert.That(harness.Session.InnerSession.ChannelRecoveryInProgress, Is.False);
            harness.OuterPolicy.Verify(
                policy => policy.GetNextDelay(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(harness.Requests.ToList().OfType<CreateSessionRequest>().Count(), Is.EqualTo(sessionNumber));
            Assert.That(harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Count(
                request => request.RequestHeader.AuthenticationToken == token), Is.EqualTo(1));
            CreateMonitoredItemsRequest recreated = await WaitForPhaseAsync(
                NextMonitoredItemsForSessionAsync(harness, token), "restored monitored item").ConfigureAwait(false);
            Assert.That(recreated.SubscriptionId, Is.EqualTo(SubscriptionId));
            Assert.That(recreated.ItemsToCreate, Has.Count.EqualTo(1));
            Assert.That(recreated.ItemsToCreate[0].RequestedParameters.ClientHandle, Is.EqualTo(live.ClientHandle));
            Assert.That(recreated.RequestHeader.AuthenticationToken, Is.EqualTo(token));

            Task<WirePublish> nextPublish = NextPublishForSessionAsync(harness, token);
            Task resumed = await WaitForPhaseAsync(
                Task.WhenAny(nextPublish, ackTimer, publishBackoff), "resumed publish or registered worker timer")
                .ConfigureAwait(false);
            if (ReferenceEquals(resumed, publishBackoff))
            {
                harness.Clock.Advance(s_publishErrorBackoff);
                resumed = await WaitForPhaseAsync(
                    Task.WhenAny(nextPublish, ackTimer), "resumed publish after its error backoff")
                    .ConfigureAwait(false);
            }
            if (ReferenceEquals(resumed, ackTimer))
            {
                harness.Clock.Advance(await ackTimer.ConfigureAwait(false));
            }
            WirePublish publish = await WaitForPhaseAsync(nextPublish, "resumed real publish").ConfigureAwait(false);
            Assert.That(publish.Request.RequestHeader.AuthenticationToken, Is.EqualTo(token));
            Assert.That(publish.Request.SubscriptionAcknowledgements.ToArray(), Is.Empty);
            publish.ReplyWithValue(live.ClientHandle, 202);
            ReceivedData notification = await WaitForPhaseAsync(
                harness.NextNotificationAsync().AsTask(), "new-generation notification").ConfigureAwait(false);
            AssertNotification(notification, live.Subscription, live.MonitoredItem, 202);
            WirePublish acknowledgement = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "new-generation acknowledgement").ConfigureAwait(false);
            AssertAcknowledgement(acknowledgement.Request.SubscriptionAcknowledgements);
            AssertSubscriptionIdentity(harness, live);
            return acknowledgement;
        }

        private static async Task<CreateMonitoredItemsRequest> NextMonitoredItemsForSessionAsync(
            ManagedSessionReconnectHarness harness,
            NodeId token)
        {
            while (true)
            {
                CreateMonitoredItemsRequest request = await harness.NextMonitoredItemsAsync().ConfigureAwait(false);
                if (request.RequestHeader.AuthenticationToken == token)
                {
                    return request;
                }
                Assert.That(request.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("token-2", 1)));
            }
        }

        private static async Task<WirePublish> NextPublishForSessionAsync(
            ManagedSessionReconnectHarness harness,
            NodeId token)
        {
            while (true)
            {
                WirePublish publish = await harness.NextWirePublishAsync().ConfigureAwait(false);
                if (publish.Request.RequestHeader.AuthenticationToken == token)
                {
                    return publish;
                }
                Assert.That(publish.CancellationToken.IsCancellationRequested, Is.True,
                    "An earlier-generation wire request must be cancelled before replacement-session publishing.");
            }
        }

        private static void AssertNoOuterRecovery(ManagedSessionReconnectHarness harness)
        {
            harness.OuterPolicy.Verify(
                policy => policy.GetNextDelay(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(harness.OuterRecoveryStarted.IsCompleted, Is.False);
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
        }

        private static async Task AssertFreshGenerationAfterLateResponseAsync(
            ManagedSessionReconnectHarness harness,
            LiveSubscription live,
            WirePublish acknowledgement,
            int subscriptionCreates,
            int monitoredItemCreates)
        {
            Assert.That(acknowledgement.Request.RequestHeader.AuthenticationToken,
                Is.EqualTo(new NodeId("token-3", 1)));
            acknowledgement.ReplyWithValue(live.ClientHandle, 303, 2);
            ReceivedData notification = await WaitForPhaseAsync(
                harness.NextNotificationAsync().AsTask(), "fresh data after the stale response is released")
                .ConfigureAwait(false);
            AssertNotification(notification, live.Subscription, live.MonitoredItem, 303, 2);
            WirePublish following = await WaitForPhaseAsync(
                harness.NextWirePublishAsync().AsTask(), "current-generation publish after the late response")
                .ConfigureAwait(false);
            AssertAcknowledgement(following.Request.SubscriptionAcknowledgements, 2);
            Assert.That(following.Request.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("token-3", 1)));
            Assert.That(harness.Session.SessionId, Is.EqualTo(new NodeId("session-3", 1)));
            Assert.That(harness.Requests.ToList().OfType<CreateSessionRequest>().Count(), Is.EqualTo(3));
            Assert.That(harness.Requests.ToList().OfType<CreateSubscriptionRequest>().Count(),
                Is.EqualTo(subscriptionCreates));
            Assert.That(harness.Requests.ToList().OfType<CreateMonitoredItemsRequest>().Count(),
                Is.EqualTo(monitoredItemCreates));
            Assert.That(harness.Lease.State, Is.EqualTo(ChannelState.Ready));
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            harness.OuterPolicy.Verify(
                policy => policy.GetNextDelay(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
            AssertSubscriptionIdentity(harness, live);
        }

        private static void AssertSubscriptionIdentity(ManagedSessionReconnectHarness harness, LiveSubscription live)
        {
            Assert.That(harness.EngineFactory.Engine, Is.SameAs(live.Engine));
            Assert.That(harness.EngineFactory.CreateCount, Is.EqualTo(1));
            Assert.That(harness.Session.TryGetSubscriptionManager(out ISubscriptionManager manager), Is.True);
            Assert.That(manager, Is.SameAs(live.Engine.SubscriptionManager));
            Assert.That(manager.Items.Single(), Is.SameAs(live.Subscription));
            Assert.That(live.Engine.PublishWorkerCount, Is.EqualTo(1));
        }

        private static void AssertOriginalWorker(ManagedSessionReconnectHarness harness, LiveSubscription live)
        {
            AssertSubscriptionIdentity(harness, live);
            Assert.That(harness.Logs.Records.Count(record => record.EventId.Name == "PublishWorkerStarted"),
                Is.EqualTo(1),
                "Restoration must retain the worker while server subscription ids are temporarily absent.");
            Assert.That(harness.Logs.Records.Count(record => record.EventId.Name == "PublishWorkerStopped"), Is.Zero);
        }

        private static void AssertNotification(
            ReceivedData received,
            ISubscription subscription,
            IMonitoredItem monitoredItem,
            int value,
            uint sequenceNumber = 1)
        {
            Assert.That(received.Subscription, Is.SameAs(subscription));
            Assert.That(received.SequenceNumber, Is.EqualTo(sequenceNumber));
            Assert.That(received.Change.PartitionServerId, Is.EqualTo(SubscriptionId));
            Assert.That(received.Change.MonitoredItem, Is.SameAs(monitoredItem));
            Assert.That(received.Change.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(received.Change.Value.WrappedValue.TryGetValue(out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(value));
        }

        private static void AssertAcknowledgement(
            ArrayOf<SubscriptionAcknowledgement> acknowledgements,
            uint sequenceNumber = 1)
        {
            Assert.That(acknowledgements, Has.Count.EqualTo(1));
            Assert.That(acknowledgements[0].SubscriptionId, Is.EqualTo(SubscriptionId));
            Assert.That(acknowledgements[0].SequenceNumber, Is.EqualTo(sequenceNumber));
        }

        private static async Task WaitForPhaseAsync(Task phase, string description)
        {
            try
            {
                await phase.WaitAsync(PhaseTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new AssertionException($"Timed out waiting for {description}.", exception);
            }
        }

        private static async Task<T> WaitForPhaseAsync<T>(Task<T> phase, string description)
        {
            await WaitForPhaseAsync((Task)phase, description).ConfigureAwait(false);
            return await phase.ConfigureAwait(false);
        }

        /// <summary>
        /// Retains the original subscription objects and client handle for identity checks across recovery generations.
        /// </summary>
        /// <param name="Subscription">The real subscription whose identity must survive recovery.</param>
        /// <param name="MonitoredItem">
        /// The original monitored item used to verify notification dispatch identity.
        /// </param>
        /// <param name="ClientHandle">
        /// The client handle expected in initial and recreated monitored-item requests.
        /// </param>
        /// <param name="Engine">The real engine whose instance and publish worker must be retained.</param>
        private sealed record LiveSubscription(
            ISubscription Subscription,
            IMonitoredItem MonitoredItem,
            uint ClientHandle,
            DefaultSubscriptionEngine Engine);

        private static readonly TimeSpan s_recoveryTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan s_boundaryStep = TimeSpan.FromMilliseconds(1);
        private static readonly TimeSpan s_publishErrorBackoff = TimeSpan.FromMilliseconds(200);
    }
}
