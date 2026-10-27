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
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    [TestFixture]
    [NonParallelizable]
    public sealed class FlowLifecycleTests
    {
        [Test]
        public async Task NativeResetInterruptsBlockedDataBeforeWaitingForCreditBlockedManagementReset()
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 1
            };
            await using var pair = new FlowLoopback(DataChannelCarrierKind.WebTransport, options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair blocked = await pair.OpenAsync().ConfigureAwait(false);
            FlowPair healthy = await pair.OpenAsync().ConfigureAwait(false);
            await blocked.Local.SendAsync(ByteString.Empty).ConfigureAwait(false);
            Task<DataChannelResult> body = blocked.Local.SendAsync(ByteString.Empty).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorCarrier.Writes.Count(write => write.IsData) == 2)
                .ConfigureAwait(false);
            Assert.That(body.IsCompleted, Is.False);
            pair.InitiatorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Reset;
            pair.InitiatorCarrier.ReleaseManagementOnFlowClose = true;
            await blocked.Local.ResetAsync(DataChannelResult.NotAuthorized).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await body.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.That(blocked.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(healthy.Local.State, Is.EqualTo(DataChannelState.Open));
            await healthy.Local.SendAsync(ByteString.Empty).ConfigureAwait(false);
            using DataChannelMessage? message = await healthy.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.MessageId, Is.EqualTo(1));
        }

        [Test]
        public async Task ResetAndRepeatedDisposeJoinActualNativeFlowReleaseAndRetainItsStreamSlot()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.BlockFlowFinish = true;
            Task reset = flow.Local.ResetAsync().AsTask();
            await pair.InitiatorCarrier.FlowFinishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task repeated = flow.Local.DisposeAsync().AsTask();
            Assert.Multiple(() =>
            {
                Assert.That(reset.IsCompleted, Is.False);
                Assert.That(repeated.IsCompleted, Is.False);
                Assert.That(pair.InitiatorBudget.Usage.Streams, Is.EqualTo(2));
                Assert.That(pair.InitiatorCarrier.FinishedFlows, Is.EqualTo(1));
                Assert.That(pair.InitiatorCarrier.ClosedFlows, Is.Zero);
            });
            pair.InitiatorCarrier.FlowFinishRelease.TrySetResult(true);
            await Task.WhenAll(reset, repeated).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(pair.InitiatorBudget.Usage.Streams, Is.EqualTo(1));
            Assert.That(pair.Initiator.IsReady, Is.True);
        }

        [Test]
        public async Task ContextDisposalCannotDestroyNativeCarrierBeforeItsFlowReleaseCompletes()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.BlockFlowClose = true;
            Task disposing = pair.Initiator.DisposeAsync().AsTask();
            await pair.InitiatorCarrier.FlowCloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(disposing.IsCompleted, Is.False);
            Assert.That(pair.InitiatorCarrier.CloseCalls, Is.Zero);
            pair.InitiatorCarrier.FlowCloseRelease.TrySetResult(true);
            await disposing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(pair.InitiatorCarrier.CloseCalls, Is.EqualTo(1));
            Assert.That(pair.InitiatorBudget.Usage.Streams, Is.Zero);
        }

        [Test]
        public async Task HelloTimeoutAtTenSecondsReleasesUnconfirmedContextWithoutAFlow()
        {
            await using var pair = new FlowLoopback();
            pair.InitiatorCarrier.Drop = _ => true;
            Task starting = pair.Initiator.StartAsync().AsTask();
            await FlowLoopback.EventuallyAsync(() => !pair.InitiatorCarrier.Writes.IsEmpty).ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(9999));
            Assert.That(starting.IsCompleted, Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await starting.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(pair.InitiatorBudget.Usage.Contexts, Is.Zero);
            Assert.That(pair.InitiatorBudget.Usage.Flows, Is.Zero);
        }

        [Test]
        [Repeat(20)]
        public async Task OpenDeadlineCancelsPendingAuthorizationAndReleasesAllProvisionalResources()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.AcceptorPolicy.Handler = async (request, cancellationToken) =>
            {
                await never.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new DataChannelAuthorization(DataChannelResult.Good, request.Parameters);
            };
            await pair.StartAsync().ConfigureAwait(false);
            Task<DataChannel> opening = pair.Initiator.OpenAsync(FlowLoopback.Parameters()).AsTask();
            await FlowLoopback.EventuallyAsync(() => !pair.AcceptorPolicy.Requests.IsEmpty).ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(9999));
            Assert.That(opening.IsCompleted, Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await opening.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.Timeout));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorBudget.Usage.Operations == 0 &&
                pair.InitiatorBudget.Usage.Operations == 0).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.InitiatorBudget.Usage.Flows, Is.Zero);
                Assert.That(pair.AcceptorBudget.Usage.Flows, Is.Zero);
                Assert.That(pair.InitiatorBudget.Usage.ReservedBytes, Is.Zero);
                Assert.That(pair.AcceptorBudget.Usage.ReservedBytes, Is.Zero);
                Assert.That(pair.Initiator.IsReady, Is.True);
            });
        }

        [Test]
        public async Task ReservedIdentifierExpiresExactlyAtOpenDeadlineWithoutReuse()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            uint id = pair.Initiator.ReserveChannelId();
            pair.Clock.Advance(TimeSpan.FromMilliseconds(9999));
            Assert.That(pair.InitiatorBudget.Usage.Flows, Is.EqualTo(1));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(pair.InitiatorBudget.Usage.Flows, Is.Zero);
            Assert.That(pair.Initiator.ReleaseChannelId(id), Is.False);
            Assert.That(pair.Initiator.ReserveChannelId(), Is.EqualTo(id + 2));
        }

        [Test]
        public async Task EndDrainsThenHalfClosesOnlyTheSendingDirection()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await flow.Local.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? queued = await flow.Remote.ReadAsync().ConfigureAwait(false))
            {
                Assert.That(queued!.Payload[0], Is.EqualTo(1));
            }
            Assert.That(await flow.Remote.ReadAsync().ConfigureAwait(false), Is.Null);
            Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await flow.Remote.SendAsync(new ByteString(new byte[] { 2 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? feedback = await flow.Local.ReadAsync().ConfigureAwait(false))
            {
                Assert.That(feedback!.Payload[0], Is.EqualTo(2));
            }
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(await flow.Remote.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await FlowLoopback.EventuallyAsync(() => flow.Local.State == DataChannelState.Closed &&
                flow.Remote.State == DataChannelState.Closed).ConfigureAwait(false);
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [Test]
        public async Task ReceiveOnlyDirectionIsAlreadyEndedAtReadinessWithoutForgingPeerEnd()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(DataChannelDirection.AcceptorToOpener))
                .ConfigureAwait(false);
            Assert.That(await flow.Local.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.GetStatus().OpenerEnded, Is.True);
                Assert.That(flow.Local.GetStatus().AcceptorEnded, Is.False);
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
            });
            Assert.That(await flow.Remote.SendAsync(FlowLoopback.UadpPayload(ByteString.Empty))
                .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Local.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.MessageId, Is.EqualTo(1));
        }

        [Test]
        public async Task DrainDeadlineResetsBlockedDataButConnectionControlStillMakesProgress()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            Task<DataChannelResult> send = flow.Local.SendAsync(new ByteString(new byte[] { 0x01 })).AsTask();
            Task<DataChannelResult> end = flow.Local.EndAsync().AsTask();
            Assert.That(await pair.Initiator.PingAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(4999));
            Assert.That(end.IsCompleted, Is.False);
            Assert.That(send.IsCompleted, Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await end.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(await send.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Faulted));
            Assert.That(flow.Local.GetStatus().Result, Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(pair.Initiator.IsReady, Is.True);
        }

        [TestCase(DataChannelResult.Good, DataChannelState.Closed)]
        [TestCase(DataChannelResult.NotAuthorized, DataChannelState.Faulted)]
        [TestCase(DataChannelResult.Timeout, DataChannelState.Faulted)]
        public async Task ResetIsScopedAndCannotStealAnApplicationOwnedMessage(
            DataChannelResult result,
            DataChannelState terminal)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync().ConfigureAwait(false);
            FlowPair other = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await first.Local.SendAsync(new ByteString("*"u8.ToArray())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? owned = await first.Remote.ReadAsync().ConfigureAwait(false);
            await first.Local.ResetAsync(result).ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => first.Remote.State == terminal).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Local.State, Is.EqualTo(terminal));
                Assert.That(owned!.Payload[0], Is.EqualTo(42));
                Assert.That(other.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Initiator.IsReady, Is.True);
            });
            Assert.That(await other.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? unaffected = await other.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(unaffected!.Length, Is.Zero);
        }

        [Test]
        public async Task RevocationBetweenAuthorizationAndAcceptPreventsUnauthorizedReadiness()
        {
            await using var pair = new FlowLoopback();
            using var revoked = new CancellationTokenSource();
            pair.AcceptorPolicy.Handler = (request, _) => new ValueTask<DataChannelAuthorization>(
                new DataChannelAuthorization(
                    DataChannelResult.Good, request.Parameters, revocationToken: revoked.Token));
            pair.AcceptorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Accept;
            await pair.StartAsync().ConfigureAwait(false);
            Task<DataChannel> opening = pair.Initiator.OpenAsync(FlowLoopback.Parameters()).AsTask();
            await pair.AcceptorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await revoked.CancelAsync().ConfigureAwait(false);
            pair.AcceptorCarrier.BeforeRelease.TrySetResult(true);
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                async () => await opening.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(pair.AcceptorCarrier.Writes.Any(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Accept),
                Is.False);
            Assert.That(pair.Acceptor.IsReady, Is.True);
            Assert.That(pair.Acceptor.Channels.IsEmpty, Is.True);
        }

        [Test]
        [Repeat(20)]
        public async Task ImmediateConfiguredPolicyRevocationDiscardsUnsecuredCreditBlockedQueue()
        {
            await using var pair = new FlowLoopback();
            using var revoked = new CancellationTokenSource();
            pair.InitiatorPolicy.Handler = (request, _) => new ValueTask<DataChannelAuthorization>(
                new DataChannelAuthorization(
                    DataChannelResult.Good, request.Parameters, revocationToken: revoked.Token));
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            Task<DataChannelResult> send = flow.Local.SendAsync(new ByteString(new byte[] { 0x01 })).AsTask();
            await revoked.CancelAsync().ConfigureAwait(false);
            Assert.That(await send.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.GetStatus().SessionId.IsNull, Is.True);
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(flow.Local.GetDiagnostics().MessagesSent, Is.Zero);
                Assert.That(pair.Initiator.IsReady, Is.True);
            });
        }

        [Test]
        public async Task PeriodicAuthorizationIsRecheckedAtSixtySecondsWithoutSessionDependency()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorPolicy.Handler = (request, _) => new ValueTask<DataChannelAuthorization>(
                request.Purpose == DataChannelAuthorizationPurpose.Recheck
                    ? new DataChannelAuthorization(DataChannelResult.NotAuthorized)
                    : new DataChannelAuthorization(DataChannelResult.Good, request.Parameters));
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(59999));
            Assert.That(pair.AcceptorPolicy.Requests.Any(
                request => request.Purpose == DataChannelAuthorizationPurpose.Recheck), Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await FlowLoopback.EventuallyAsync(() => flow.Remote.State == DataChannelState.Faulted)
                .ConfigureAwait(false);
            Assert.That(flow.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [Test]
        public async Task ExplicitRecheckAppliesChangedPolicyWithoutWaitingForPeriodicTimer()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.AcceptorPolicy.Handler = (_, _) => new ValueTask<DataChannelAuthorization>(
                new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            await pair.Acceptor.RecheckAuthorizationAsync().ConfigureAwait(false);
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Faulted));
            Assert.That(flow.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(pair.Clock.GetUtcNow(), Is.EqualTo(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero)));
        }

        [Test]
        public async Task AuthorizationLifetimeExpiresAtBoundaryWithoutRecallingOwnedMessages()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorPolicy.Handler = (request, _) => new ValueTask<DataChannelAuthorization>(
                request.Purpose == DataChannelAuthorizationPurpose.Recheck
                    ? new DataChannelAuthorization(DataChannelResult.NotAuthorized)
                    : new DataChannelAuthorization(DataChannelResult.Good, request.Parameters,
                        lifetime: TimeSpan.FromSeconds(1)));
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 7 }));
            Assert.That(await flow.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? owned = await flow.Remote.ReadAsync().ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Faulted));
            Assert.That(owned!.Payload.ToArray(), Is.EqualTo(payload.ToArray()));
            Assert.That(flow.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.NotAuthorized));
        }

        [Test]
        public async Task ShortAuthorizationLifetimeRechecksRenewedPolicyInsteadOfKillingALiveSession()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorPolicy.Handler = (request, _) => new ValueTask<DataChannelAuthorization>(
                new DataChannelAuthorization(DataChannelResult.Good, request.Parameters,
                    lifetime: TimeSpan.FromSeconds(1)));
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorPolicy.Requests.Any(
                request => request.Purpose == DataChannelAuthorizationPurpose.Recheck)).ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(FlowLoopback.UadpPayload(ByteString.Empty)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.MessageId, Is.EqualTo(1));
        }

        [Test]
        public async Task ExpiredAuthorizationBlocksIncomingBodyUntilLiveRecheckCompletes()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.AcceptorPolicy.Handler = async (request, token) =>
            {
                if (request.Purpose == DataChannelAuthorizationPurpose.Recheck)
                {
                    started.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }
                return new DataChannelAuthorization(DataChannelResult.Good, request.Parameters,
                    lifetime: TimeSpan.FromSeconds(1));
            };
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Task<DataChannelResult> data = flow.Local.SendAsync(new ByteString("\t"u8.ToArray())).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorCarrier.Writes.Any(write => write.IsData))
                .ConfigureAwait(false);
            Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.EqualTo(2 * FlowStreamInput.ParserStorageBytes));
            release.TrySetResult(true);
            Assert.That(await data.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Payload[0], Is.EqualTo(9));
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
        }

        [Test]
        public async Task ProbesAreBoundedByFlowContextRateAndExactTimeout()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            var flows = new FlowPair[9];
            for (int i = 0; i < flows.Length; i++)
            {
                flows[i] = await pair.OpenAsync().ConfigureAwait(false);
            }
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Pong;
            var pings = new Task<DataChannelResult>[8];
            pings[0] = flows[0].Local.PingAsync().AsTask();
            Assert.That(await flows[0].Local.PingAsync().ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(await flows[1].Local.PingAsync().ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.WouldBlock));
            for (int i = 1; i < pings.Length; i++)
            {
                pair.Clock.Advance(TimeSpan.FromSeconds(1));
                pings[i] = flows[i].Local.PingAsync().AsTask();
            }
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(await flows[8].Local.PingAsync().ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(pair.InitiatorBudget.Usage.Probes, Is.EqualTo(8));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1999));
            Assert.That(pings[0].IsCompleted, Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await pings[0].ConfigureAwait(false), Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(pair.InitiatorBudget.Usage.Probes, Is.EqualTo(7));
            pair.Clock.Advance(TimeSpan.FromSeconds(7));
            await Task.WhenAll(pings).ConfigureAwait(false);
            Assert.That(pair.InitiatorBudget.Usage.Probes, Is.Zero);
        }

        /// <summary>
        /// A clock read can synchronously deliver an expired probe timer without corrupting the scheduler.
        /// </summary>
        [Test]
        public async Task ClockReadDuringProbeArmingPreservesSchedulerAndProbeOwnership()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            var dropped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.AcceptorCarrier.Drop = write =>
            {
                if (TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) != FlowOperation.Pong)
                {
                    return false;
                }
                dropped.TrySetResult(true);
                return true;
            };
            pair.Clock.AutoAdvanceAmount = pair.Options.PingTimeout + TimeSpan.FromSeconds(1);
            DataChannelResult expired;
            try
            {
                expired = await pair.Initiator.PingAsync(0, CancellationToken.None).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            finally
            {
                pair.Clock.AutoAdvanceAmount = TimeSpan.Zero;
            }
            Assert.That(expired, Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(pair.InitiatorBudget.Usage.Probes, Is.Zero);

            await dropped.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await pair.FlushAsync().ConfigureAwait(false);
            pair.AcceptorCarrier.Drop = null;
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            DataChannelResult resumed = await pair.Initiator.PingAsync(0, CancellationToken.None).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resumed, Is.EqualTo(DataChannelResult.Good));
                Assert.That(pair.InitiatorBudget.Usage.Probes, Is.Zero);
                Assert.That(pair.Initiator.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Rate-deadline inspection shares the timer scan's timestamp without running another clock callback.
        /// </summary>
        [Test]
        public void RateDeadlineUsesTheSuppliedSnapshotWithoutReadingTheClock()
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
            long timestamp = clock.GetTimestamp();
            var bucket = new FlowTokenBucket();
            bucket.Configure(1, 1, timestamp, initial: false);
            clock.AutoAdvanceAmount = TimeSpan.FromSeconds(1);

            TimeSpan delay = bucket.Delay(1, clock, timestamp);

            Assert.Multiple(() =>
            {
                Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(clock.GetTimestamp(), Is.EqualTo(timestamp));
            });
        }

        [Test]
        public async Task ContextClosureIsReportedOnceAndRetainsOnlyApplicationOwnedBytes()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            int closed = 0;
            pair.Acceptor.Closed += _ => Interlocked.Increment(ref closed);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            await flow.Local.SendAsync(new ByteString("*"u8.ToArray())).ConfigureAwait(false);
            DataChannelMessage? owned = await flow.Remote.ReadAsync().ConfigureAwait(false);
            await pair.Initiator.InvalidateAsync(DataChannelResult.SecurityFailure).ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => Volatile.Read(ref closed) == 1).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(flow.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.SecurityFailure));
                Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.EqualTo(14));
                Assert.That(pair.AcceptorBudget.Usage.ReservedBytes, Is.EqualTo(1024));
                Assert.That(pair.AcceptorBudget.Usage.Contexts, Is.Zero);
                Assert.That(pair.AcceptorBudget.Usage.Flows, Is.Zero);
                Assert.That(owned!.Payload[0], Is.EqualTo(42));
            });
            owned!.Dispose();
            owned.Dispose();
            Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.Zero);
            Assert.That(pair.AcceptorBudget.Usage.ReservedBytes, Is.Zero);
            await pair.Acceptor.InvalidateAsync().ConfigureAwait(false);
            Assert.That(closed, Is.EqualTo(1));
        }
    }
}
