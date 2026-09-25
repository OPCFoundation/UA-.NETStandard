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
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using static Opc.Ua.Client.Tests.Stack.Client.Fakes.ManagedSessionReconnectHarness;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Exercises public manual channel replacement without bypassing managed-session or channel-manager ownership.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [NonParallelizable]
    public sealed class ManagedSessionChannelReplacementTests
    {
        /// <summary>
        /// A supplied owning lease becomes the current binding and the wrapper follows only its recovery events.
        /// Retired final notifications cannot stop its keepalive or release its active recovery.
        /// </summary>
        /// <param name="replaceChannel">Whether to supply a fresh lease from another real channel manager.</param>
        [TestCase(false, TestName = "SupplyingCurrentManagedLeasePreservesBindingAndEventsAsync")]
        [TestCase(true, TestName = "SuppliedManagedLeaseRebindsSessionAndChannelEventsAsync")]
        public async Task SuppliedManagedLeaseRebindsSessionAndChannelEventsAsync(bool replaceChannel)
        {
            var replacementReconnect = new AsyncOperationGate();
            await using var replacementChannels = new SessionChannelHarness(
                timeProvider: new ObservableFakeTimeProvider(),
                configureChannel: channel =>
                {
                    channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                    channel.ReconnectHandler = replacementReconnect.WaitAsync;
                });
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                RecoveryActivationStatus = StatusCodes.Good
            };
            ConfigureManualReconnectPolicy(harness);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.RecoveryActivation.Release();
            Session originalSession = harness.Session.InnerSession;
            IManagedTransportChannel originalChannel = harness.Lease;
            NodeId originalSessionId = originalSession.SessionId;
            var sibling = new Mock<IReconnectParticipant>();
            sibling.SetupGet(participant => participant.Id).Returns("retired-channel-participant");
            sibling.SetupGet(participant => participant.Endpoint).Returns(originalSession.ConfiguredEndpoint);
            sibling.Setup(participant => participant.OnReconnectAsync(
                    It.IsAny<IManagedTransportChannel>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated));
            using IManagedTransportChannel retiredSibling = await harness.Channels.Manager.GetAsync(
                sibling.Object, harness.CancellationToken).ConfigureAwait(false);
            using IManagedTransportChannel? replacement = replaceChannel
                ? await replacementChannels.Manager.GetAsync(originalSession, harness.CancellationToken)
                    .ConfigureAwait(false)
                : null;
            IManagedTransportChannel suppliedChannel = replacement ?? originalChannel;
            AsyncOperationGate reconnectGate = replaceChannel ? replacementReconnect : harness.TransportReconnect;
            var forwarded = new ConcurrentQueue<ChannelStateChange>();
            Task? channelRecovery = null;
            try
            {
                await WaitForPhaseAsync(
                    harness.Session.ReconnectAsync(null, suppliedChannel, harness.CancellationToken),
                    "manual reconnect with the supplied owning lease").ConfigureAwait(false);
                IManagedTransportChannel? bindingAfterReplacement = originalSession.ManagedChannel;
                IClientChannelManager? managerAfterReplacement = originalSession.ChannelManager;
                harness.Session.ChannelStateChanged += (_, change) => forwarded.Enqueue(change);

                channelRecovery = suppliedChannel.Manager.ReconnectAsync(
                    suppliedChannel, harness.CancellationToken).AsTask();
                await WaitForPhaseAsync(reconnectGate.Entered, "current channel transport recovery")
                    .ConfigureAwait(false);
                bool ownerDuringRecovery = originalSession.ChannelRecoveryInProgress;
                ChannelStateChange[] eventsDuringRecovery = [.. forwarded];
                Assert.That(suppliedChannel.State, Is.EqualTo(ChannelState.TransportReconnecting));
                Assert.That(channelRecovery.IsCompleted, Is.False);
                Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                if (replaceChannel)
                {
                    ParticipantReconnectResult retiredResult = await ((IReconnectParticipant)originalSession)
                        .OnReconnectAsync(originalChannel, -1, harness.CancellationToken).AsTask()
                        .WaitAsync(PhaseTimeout).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(retiredResult, Is.EqualTo(ParticipantReconnectResult.FatalForParticipant));
                        Assert.That(originalSession.ChannelRecoveryInProgress, Is.True);
                        Assert.That(originalSession.ManagedChannel, Is.SameAs(suppliedChannel));
                        Assert.That(suppliedChannel.State, Is.EqualTo(ChannelState.TransportReconnecting));
                        Assert.That(channelRecovery.IsCompleted, Is.False);
                        Assert.That(reconnectGate.Cancelled.IsCompleted, Is.False);
                    });
                }

                reconnectGate.Release();
                harness.RecoveryActivation.Release();
                await WaitForPhaseAsync(channelRecovery, "current channel recovery completion").ConfigureAwait(false);
                ChannelStateChange[] currentEvents = [.. forwarded];
                if (replaceChannel)
                {
                    harness.TransportReconnect.Release();
                    await WaitForPhaseAsync(
                        harness.Channels.Manager.ReconnectAsync(retiredSibling, harness.CancellationToken).AsTask(),
                        "independent recovery of the retired entry").ConfigureAwait(false);
                    await AssertRetiredFinalPreservesKeepAliveAsync(harness, originalSession, originalChannel)
                        .ConfigureAwait(false);
                }

                DataValue value = await harness.Session.ReadValueAsync(
                    VariableIds.Server_ServerStatus_State, harness.CancellationToken).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(harness.Session.InnerSession, Is.SameAs(originalSession));
                    Assert.That(originalSession.SessionId, Is.EqualTo(originalSessionId));
                    Assert.That(harness.Lease, Is.SameAs(suppliedChannel));
                    Assert.That(bindingAfterReplacement, Is.SameAs(suppliedChannel));
                    Assert.That(managerAfterReplacement, Is.SameAs(suppliedChannel.Manager));
                    Assert.That(ownerDuringRecovery, Is.True);
                    Assert.That(eventsDuringRecovery.Select(change => change.NewState),
                        Is.EqualTo([ChannelState.TransportReconnecting]));
                    Assert.That(currentEvents.Select(change => change.NewState), Is.EqualTo(s_recoveryStates));
                    Assert.That(forwarded.ToArray(), Is.EqualTo(currentEvents),
                        "A retired entry must not publish channel events through the current managed session.");
                    Assert.That(originalSession.ManagedChannel, Is.SameAs(suppliedChannel));
                    Assert.That(originalSession.ChannelManager, Is.SameAs(suppliedChannel.Manager));
                    Assert.That(originalSession.ChannelRecoveryInProgress, Is.False);
                    Assert.That(originalSession.Reconnecting, Is.False);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetValue(out int state), Is.True);
                    Assert.That(state, Is.EqualTo((int)ServerState.Running));
                });
            }
            finally
            {
                replacementReconnect.Release();
                harness.ReleaseAllGates();
                if (channelRecovery != null)
                {
                    await WaitForPhaseAsync(channelRecovery, "current channel recovery cleanup").ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Failure after activation starts keeps the managed binding truthful and permits recovery of the live session.
        /// </summary>
        /// <param name="cancelActivation">Whether caller cancellation replaces the scripted activation failure.</param>
        [TestCase(false, TestName = "FailedSuppliedReconnectKeepsBindingRecoverableAsync")]
        [TestCase(true, TestName = "CancelledSuppliedReconnectKeepsBindingRecoverableAsync")]
        public async Task UnsuccessfulSuppliedReconnectKeepsBindingRecoverableAsync(bool cancelActivation)
        {
            var activation = new AsyncOperationGate();
            int activationCount = 0;
            await using var replacementChannels = new SessionChannelHarness(
                timeProvider: new ObservableFakeTimeProvider(),
                configureChannel: channel =>
                {
                    channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                    channel.RequestHandler = async (request, ct) =>
                    {
                        if (request is ActivateSessionRequest && Interlocked.Increment(ref activationCount) == 1)
                        {
                            await activation.WaitAsync(ct).ConfigureAwait(false);
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        return channel.CreateResponse(request);
                    };
                });
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                RecoveryActivationStatus = StatusCodes.Good
            };
            ConfigureManualReconnectPolicy(harness);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.ReleaseAllGates();
            Session originalSession = harness.Session.InnerSession;
            NodeId originalSessionId = originalSession.SessionId;
            IManagedTransportChannel suppliedChannel = await replacementChannels.Manager.GetAsync(
                originalSession, harness.CancellationToken).ConfigureAwait(false);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(harness.CancellationToken);
            var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Session.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Disconnected)
                {
                    disconnected.TrySetResult(true);
                }
            };
            try
            {
                await CompleteUnsuccessfulReconnectAsync(
                    harness.Session.ReconnectAsync(null, suppliedChannel, cancellation.Token),
                    activation, cancellation, cancelActivation).ConfigureAwait(false);
                await WaitForPhaseAsync(disconnected.Task, "manual reconnect attempt fully settled")
                    .ConfigureAwait(false);

                IManagedTransportChannel actualAfterFailure = harness.Lease;
                IManagedTransportChannel? bindingAfterFailure = originalSession.ManagedChannel;
                IClientChannelManager? managerAfterFailure = originalSession.ChannelManager;
                bool recoveryAfterFailure = originalSession.ChannelRecoveryInProgress;
                bool reconnectingAfterFailure = originalSession.Reconnecting;
                ScriptedChannel recoveryTransport = ReferenceEquals(actualAfterFailure, suppliedChannel)
                    ? replacementChannels.CreatedChannels[0]
                    : harness.Channels.CreatedChannels[0];
                int reconnectsBeforeRetry = recoveryTransport.ReconnectCount;
                var forwarded = new ConcurrentQueue<ChannelStateChange>();
                harness.Session.ChannelStateChanged += (_, change) => forwarded.Enqueue(change);
                await WaitForPhaseAsync(
                    harness.Session.ReconnectAsync(null, null, harness.CancellationToken),
                    "subsequent recovery after unsuccessful manual activation").ConfigureAwait(false);

                DataValue value = await harness.Session.ReadValueAsync(
                    VariableIds.Server_ServerStatus_State, harness.CancellationToken).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(harness.Session.InnerSession, Is.SameAs(originalSession));
                    Assert.That(originalSession.SessionId, Is.EqualTo(originalSessionId));
                    Assert.That(bindingAfterFailure, Is.SameAs(actualAfterFailure));
                    Assert.That(managerAfterFailure, Is.SameAs(actualAfterFailure.Manager));
                    Assert.That(recoveryAfterFailure, Is.False);
                    Assert.That(reconnectingAfterFailure, Is.False);
                    Assert.That(harness.Lease, Is.SameAs(actualAfterFailure),
                        "A successful retry must reconnect the transport that the failed attempt actually retained.");
                    Assert.That(recoveryTransport.ReconnectCount, Is.EqualTo(reconnectsBeforeRetry + 1));
                    Assert.That(forwarded.Select(change => change.NewState), Is.EqualTo(s_recoveryStates));
                    Assert.That(originalSession.ManagedChannel, Is.SameAs(harness.Lease));
                    Assert.That(originalSession.ChannelManager, Is.SameAs(harness.Lease.Manager));
                    Assert.That(originalSession.ChannelRecoveryInProgress, Is.False);
                    Assert.That(originalSession.Reconnecting, Is.False);
                    Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetValue(out int state), Is.True);
                    Assert.That(state, Is.EqualTo((int)ServerState.Running));
                });
            }
            finally
            {
                activation.Release();
                await suppliedChannel.CloseAsync(harness.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A competing public Session reconnect cannot release or replace an active channel-reactivation owner.
        /// </summary>
        [Test]
        public async Task SuppliedReconnectDuringActivationPreservesRecoveryOwnerAsync()
        {
            await using var replacementChannels = new SessionChannelHarness(
                timeProvider: new ObservableFakeTimeProvider(),
                configureChannel: channel => channel.SupportedFeatures = TransportChannelFeatures.Reconnect);
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                RecoveryActivationStatus = StatusCodes.Good
            };
            await harness.ConnectAsync(false).ConfigureAwait(false);
            Session originalSession = harness.Session.InnerSession;
            IManagedTransportChannel originalChannel = harness.Lease;
            NodeId originalSessionId = originalSession.SessionId;
            IManagedTransportChannel suppliedChannel = await replacementChannels.Manager.GetAsync(
                originalSession, harness.CancellationToken).ConfigureAwait(false);
            try
            {
                ScriptedChannel replacementTransport = replacementChannels.CreatedChannels[0];
                int overlappingActivation = 0;
                replacementTransport.RequestHandler = (request, _) =>
                {
                    if (request is ActivateSessionRequest && !harness.RecoveryActivation.Exited.IsCompleted)
                    {
                        Interlocked.Increment(ref overlappingActivation);
                    }
                    return new ValueTask<IServiceResponse>(replacementTransport.CreateResponse(request));
                };
                harness.TransportReconnect.Release();
                Task recovery = harness.StartRecoveryAsync();
                await WaitForPhaseAsync(harness.RecoveryActivation.Entered, "active manager-owned reactivation")
                    .ConfigureAwait(false);
                await ObserveCompetingReconnectAsync(
                    originalSession.ReconnectAsync(null, suppliedChannel, harness.CancellationToken),
                    recovery,
                    () => Assert.Multiple(() =>
                    {
                        Assert.That(originalSession.Reconnecting, Is.True);
                        Assert.That(originalSession.ChannelRecoveryInProgress, Is.True);
                        Assert.That(originalSession.ManagedChannel, Is.SameAs(originalChannel));
                        Assert.That(originalSession.TransportChannel, Is.SameAs(originalChannel));
                        Assert.That(originalChannel.State,
                            Is.EqualTo(ChannelState.TransportConnectedSessionReactivating));
                        Assert.That(harness.RecoveryActivation.Exited.IsCompleted, Is.False);
                        Assert.That(harness.RecoveryActivation.Cancelled.IsCompleted, Is.False);
                        Assert.That(recovery.IsCompleted, Is.False);
                        Assert.That(Volatile.Read(ref overlappingActivation), Is.Zero);
                    }),
                    harness.ReleaseAllGates).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(Volatile.Read(ref overlappingActivation), Is.Zero);
                    Assert.That(harness.Session.InnerSession, Is.SameAs(originalSession));
                    Assert.That(originalSession.SessionId, Is.EqualTo(originalSessionId));
                    Assert.That(originalSession.ManagedChannel, Is.SameAs(harness.Lease));
                    Assert.That(originalSession.ChannelManager, Is.SameAs(harness.Lease.Manager));
                    Assert.That(originalSession.ChannelRecoveryInProgress, Is.False);
                    Assert.That(originalSession.Reconnecting, Is.False);
                });
            }
            finally
            {
                harness.ReleaseAllGates();
                await suppliedChannel.CloseAsync(harness.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A competing supplied-channel reconnect cannot replace an active subscription-restoration owner.
        /// </summary>
        /// <param name="outerOwner">Whether the outer managed session owns deferred subscription restoration.</param>
        /// <param name="useManagedSession">
        /// Whether to request replacement through the public managed-session wrapper.
        /// </param>
        [TestCase(false, false, TestName = "SuppliedReconnectDuringRestorationPreservesChannelOwnerAsync")]
        [TestCase(false, true, TestName = "ManagedSuppliedReconnectDuringRestorationPreservesChannelOwnerAsync")]
        [TestCase(true, true, TestName = "SuppliedReconnectDuringRestorationPreservesOuterOwnerAsync")]
        public async Task SuppliedReconnectDuringRestorationPreservesOwnerAsync(
            bool outerOwner,
            bool useManagedSession)
        {
            await using var replacementChannels = new SessionChannelHarness(
                timeProvider: new ObservableFakeTimeProvider(),
                configureChannel: channel => channel.SupportedFeatures = TransportChannelFeatures.Reconnect);
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                HoldSubscriptionRestoration = true
            };
            ConfigureManualReconnectPolicy(harness);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            Session originalSession = harness.Session.InnerSession;
            IManagedTransportChannel originalChannel = harness.Lease;
            harness.AddSubscription();
            await WaitForPhaseAsync(harness.NextMonitoredItemsAsync().AsTask(), "initial monitored item")
                .ConfigureAwait(false);
            IManagedTransportChannel suppliedChannel = await replacementChannels.Manager.GetAsync(
                originalSession, harness.CancellationToken).ConfigureAwait(false);
            try
            {
                ScriptedChannel replacementTransport = replacementChannels.CreatedChannels[0];
                ScriptedChannel originalTransport = harness.Channels.CreatedChannels[0];
                int overlappingActivation = 0;
                replacementTransport.RequestHandler = (request, ct) =>
                {
                    if (request is ActivateSessionRequest && !harness.ReplacementSubscription.Exited.IsCompleted)
                    {
                        Interlocked.Increment(ref overlappingActivation);
                    }
                    return originalTransport.RequestHandler!(request, ct);
                };
                harness.TransportReconnect.Release();
                harness.RecoveryActivation.Release();
                harness.ReplacementSession.Release();
                Task recovery = outerOwner ? harness.StartOuterRecoveryAsync() : harness.StartRecoveryAsync();
                await WaitForPhaseAsync(
                    harness.ReplacementSubscription.Entered, "active subscription restoration")
                    .ConfigureAwait(false);
                await ObserveCompetingReconnectAsync(
                    useManagedSession
                        ? harness.Session.ReconnectAsync(null, suppliedChannel, harness.CancellationToken)
                        : originalSession.ReconnectAsync(null, suppliedChannel, harness.CancellationToken),
                    recovery,
                    () => Assert.Multiple(() =>
                    {
                        Assert.That(harness.Session.StateMachine.State,
                            Is.EqualTo(outerOwner ? ConnectionState.Reconnecting : ConnectionState.Connected));
                        Assert.That(originalSession.Reconnecting, Is.False);
                        if (!outerOwner)
                        {
                            Assert.That(originalSession.ChannelRecoveryInProgress, Is.True);
                        }
                        Assert.That(originalSession.ManagedChannel, Is.SameAs(originalChannel));
                        Assert.That(originalSession.TransportChannel, Is.SameAs(originalChannel));
                        Assert.That(originalSession.ChannelManager, Is.SameAs(harness.Channels.Manager));
                        Assert.That(harness.ReplacementSubscription.Exited.IsCompleted, Is.False);
                        Assert.That(harness.ReplacementSubscription.Cancelled.IsCompleted, Is.False);
                        Assert.That(recovery.IsCompleted, Is.False);
                        Assert.That(Volatile.Read(ref overlappingActivation), Is.Zero);
                    }),
                    harness.ReleaseAllGates).ConfigureAwait(false);

                CreateMonitoredItemsRequest restored = await harness.NextMonitoredItemsAsync().AsTask()
                    .WaitAsync(PhaseTimeout).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(Volatile.Read(ref overlappingActivation), Is.Zero);
                    Assert.That(harness.Session.InnerSession, Is.SameAs(originalSession));
                    Assert.That(originalSession.SessionId, Is.EqualTo(new NodeId("session-2", 1)));
                    Assert.That(originalSession.ManagedChannel, Is.SameAs(harness.Lease));
                    Assert.That(originalSession.ChannelManager, Is.SameAs(harness.Lease.Manager));
                    Assert.That(originalSession.ChannelRecoveryInProgress, Is.False);
                    Assert.That(originalSession.Reconnecting, Is.False);
                    Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                    Assert.That(restored.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("token-2", 1)));
                    Assert.That(restored.ItemsToCreate.Count, Is.EqualTo(1));
                });
            }
            finally
            {
                harness.ReleaseAllGates();
                await suppliedChannel.CloseAsync(harness.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A late activation response from a cancelled cycle cannot clear ownership of recovery on a supplied lease.
        /// </summary>
        [Test]
        public async Task LateRetiredActivationCannotClearReplacementRecoveryOwnerAsync()
        {
            var successorTransport = new AsyncOperationGate();
            await using var replacementChannels = new SessionChannelHarness(
                timeProvider: new ObservableFakeTimeProvider(),
                configureChannel: channel =>
                {
                    channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                    channel.ReconnectHandler = successorTransport.WaitAsync;
                });
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero)
            {
                RecoveryActivationStatus = StatusCodes.Good
            };
            ConfigureManualReconnectPolicy(harness);
            harness.RecoveryActivation.IgnoreCancellation = true;
            await harness.ConnectAsync(false).ConfigureAwait(false);
            Session originalSession = harness.Session.InnerSession;
            IManagedTransportChannel originalChannel = harness.Lease;
            NodeId originalSessionId = originalSession.SessionId;
            ScriptedChannel originalTransport = harness.Channels.CreatedChannels[0];
            await WaitForPhaseAsync(
                originalTransport.NextOperationAsync<ActivateSessionRequest>(harness.CancellationToken).AsTask(),
                "initial session activation").ConfigureAwait(false);
            harness.TransportReconnect.Release();
            Task retiredRecovery = harness.StartRecoveryAsync();
            Task? successorRecovery = null;
            try
            {
                await WaitForPhaseAsync(harness.RecoveryActivation.Entered, "old activation awaiting its response")
                    .ConfigureAwait(false);
                ScriptedChannel.RequestOperation lateActivation = await originalTransport
                    .NextOperationAsync<ActivateSessionRequest>(harness.CancellationToken).AsTask()
                    .WaitAsync(PhaseTimeout).ConfigureAwait(false);
                await WaitForPhaseAsync(originalChannel.CloseAsync(harness.CancellationToken).AsTask(),
                    "retiring the old owning lease").ConfigureAwait(false);
                await WaitForPhaseAsync(harness.RecoveryActivation.Cancelled, "old activation token cancellation")
                    .ConfigureAwait(false);
                Assert.That(async () => await retiredRecovery.WaitAsync(PhaseTimeout).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.Multiple(() =>
                {
                    Assert.That(originalChannel.State, Is.EqualTo(ChannelState.Closed));
                    Assert.That(originalSession.Reconnecting, Is.False);
                    Assert.That(harness.RecoveryActivation.Exited.IsCompleted, Is.False);
                    Assert.That(lateActivation.Operation.IsCompleted, Is.False);
                });

                using IManagedTransportChannel suppliedChannel = await replacementChannels.Manager.GetAsync(
                    originalSession, harness.CancellationToken).ConfigureAwait(false);
                await WaitForPhaseAsync(
                    harness.Session.ReconnectAsync(null, suppliedChannel, harness.CancellationToken),
                    "manual replacement while the retired raw response remains pending").ConfigureAwait(false);
                var forwarded = new ConcurrentQueue<ChannelStateChange>();
                harness.Session.ChannelStateChanged += (_, change) => forwarded.Enqueue(change);
                successorRecovery = replacementChannels.Manager.ReconnectAsync(
                    suppliedChannel, harness.CancellationToken).AsTask();
                await WaitForPhaseAsync(successorTransport.Entered, "new lease recovery holding transport reconnect")
                    .ConfigureAwait(false);
                Assert.That(originalSession.ChannelRecoveryInProgress, Is.True);

                harness.RecoveryActivation.Release();
                IServiceResponse lateResponse = await lateActivation.Operation.WaitAsync(PhaseTimeout)
                    .ConfigureAwait(false);
                await WaitForPhaseAsync(harness.RecoveryActivation.Exited, "late retired activation response")
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(lateResponse.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(harness.Session.InnerSession, Is.SameAs(originalSession));
                    Assert.That(originalSession.SessionId, Is.EqualTo(originalSessionId));
                    Assert.That(originalSession.ManagedChannel, Is.SameAs(suppliedChannel));
                    Assert.That(originalSession.ChannelManager, Is.SameAs(replacementChannels.Manager));
                    Assert.That(harness.Lease, Is.SameAs(suppliedChannel));
                    Assert.That(originalSession.ChannelRecoveryInProgress, Is.True);
                    Assert.That(suppliedChannel.State, Is.EqualTo(ChannelState.TransportReconnecting));
                    Assert.That(successorRecovery.IsCompleted, Is.False);
                    Assert.That(successorTransport.Cancelled.IsCompleted, Is.False);
                    Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                    Assert.That(forwarded.Select(change => change.NewState),
                        Is.EqualTo([ChannelState.TransportReconnecting]));
                });

                successorTransport.Release();
                await WaitForPhaseAsync(successorRecovery, "successor recovery completing after the late response")
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(originalSession.ManagedChannel, Is.SameAs(suppliedChannel));
                    Assert.That(originalSession.ChannelRecoveryInProgress, Is.False);
                    Assert.That(originalSession.Reconnecting, Is.False);
                    Assert.That(suppliedChannel.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(forwarded.Select(change => change.NewState), Is.EqualTo(s_recoveryStates));
                });
            }
            finally
            {
                successorTransport.Release();
                harness.ReleaseAllGates();
                if (successorRecovery != null)
                {
                    await WaitForPhaseAsync(successorRecovery, "successor recovery cleanup").ConfigureAwait(false);
                }
            }
        }

        private static async Task AssertRetiredFinalPreservesKeepAliveAsync(
            ManagedSessionReconnectHarness harness,
            Session session,
            IManagedTransportChannel retiredChannel)
        {
            var keepAlives = System.Threading.Channels.Channel.CreateUnbounded<KeepAliveEventArgs>();
            harness.Session.KeepAlive += (_, args) => keepAlives.Writer.TryWrite(args);
            var interval = TimeSpan.FromMilliseconds(100);
            Task timerChanged = harness.Clock.WaitForTimerChangedAsync(interval, interval);
            harness.Session.KeepAliveInterval = (int)interval.TotalMilliseconds;
            await WaitForPhaseAsync(timerChanged, "replacement keepalive schedule").ConfigureAwait(false);
            harness.Clock.Advance(interval);
            KeepAliveEventArgs before = await keepAlives.Reader.ReadAsync(harness.CancellationToken).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            DateTime lastKeepAlive = session.LastKeepAliveTime;

            IReconnectParticipant participant = session;
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => participant.OnReconnectAsync(retiredChannel, -1, cancellation.Token)
                    .AsTask().WaitAsync(PhaseTimeout),
                Throws.InstanceOf<OperationCanceledException>());
            ParticipantReconnectResult result = await participant
                .OnReconnectAsync(retiredChannel, -1, harness.CancellationToken).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);

            harness.Clock.Advance(interval);
            KeepAliveEventArgs after = await keepAlives.Reader.ReadAsync(harness.CancellationToken).AsTask()
                .WaitAsync(PhaseTimeout).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ParticipantReconnectResult.FatalForParticipant));
                Assert.That(before.Status, Is.Null);
                Assert.That(before.CurrentState, Is.EqualTo(ServerState.Running));
                Assert.That(after.Status, Is.Null);
                Assert.That(after.CurrentState, Is.EqualTo(ServerState.Running));
                Assert.That(session.LastKeepAliveTime, Is.EqualTo(lastKeepAlive.Add(interval)));
                Assert.That(session.ManagedChannel, Is.SameAs(harness.Lease));
                Assert.That(harness.Lease.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            });
        }

        private static void ConfigureManualReconnectPolicy(ManagedSessionReconnectHarness harness)
        {
            harness.OuterPolicy.Setup(policy => policy.GetNextDelay(
                    It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns<int, CancellationToken>(static (attempt, _) => attempt == 0 ? TimeSpan.Zero : null);
        }

        private static async Task CompleteUnsuccessfulReconnectAsync(
            Task reconnect,
            AsyncOperationGate activation,
            CancellationTokenSource cancellation,
            bool cancelActivation)
        {
            await WaitForPhaseAsync(activation.Entered, "supplied-channel activation").ConfigureAwait(false);
            if (cancelActivation)
            {
                await cancellation.CancelAsync().WaitAsync(PhaseTimeout).ConfigureAwait(false);
                await WaitForPhaseAsync(activation.Cancelled, "caller cancellation reaching activation")
                    .ConfigureAwait(false);
            }
            else
            {
                activation.Release();
            }
            await WaitForPhaseAsync(Task.WhenAny(reconnect), "unsuccessful manual reconnect completion")
                .ConfigureAwait(false);
            if (cancelActivation)
            {
                Exception error = Assert.CatchAsync(async () => await reconnect.ConfigureAwait(false))!;
                Assert.That(error, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<ServiceResultException>());
                if (error is ServiceResultException serviceError)
                {
                    // The worker can settle its cancellation failure before the caller's cancellation wait wins.
                    Assert.That(serviceError.StatusCode, Is.EqualTo(StatusCodes.Bad));
                }
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(activation.Cancelled.IsCompleted, Is.True);
            }
            else
            {
                Assert.That(async () => await reconnect.ConfigureAwait(false),
                    Throws.InstanceOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadUserAccessDenied));
            }
            await WaitForPhaseAsync(activation.Exited, "unsuccessful activation unwind").ConfigureAwait(false);
        }

        private static async Task ObserveCompetingReconnectAsync(
            Task competingReconnect,
            Task recovery,
            Action assertOwner,
            Action release)
        {
            try
            {
                assertOwner();
            }
            finally
            {
                release();
                await WaitForPhaseAsync(recovery, "original recovery completion").ConfigureAwait(false);
                await WaitForPhaseAsync(Task.WhenAny(competingReconnect), "competing reconnect completion")
                    .ConfigureAwait(false);
                try
                {
                    await competingReconnect.ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                }
            }
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

        private static readonly ChannelState[] s_recoveryStates =
        [
            ChannelState.TransportReconnecting,
            ChannelState.TransportConnectedSessionReactivating,
            ChannelState.Ready
        ];
    }
}
