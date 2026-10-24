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
    public sealed class FlowContextTests
    {
        [Test]
        public async Task PhysicalCarrierCloseNotificationNeverReentersCarrierCloseDuringDisposal()
        {
            await using var pair = new FlowLoopback();
            pair.Initiator.CarrierClosed(DataChannelResult.Closed);
            pair.Acceptor.CarrierClosed(DataChannelResult.Closed);
            await pair.DisposeAsync().ConfigureAwait(false);
            Assert.That(pair.InitiatorCarrier.CloseCalls, Is.Zero);
            Assert.That(pair.AcceptorCarrier.CloseCalls, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposingAnIdleContextDoesNotRequireAnotherIncomingMessage(bool connected)
        {
            var pair = new FlowLoopback();
            if (!connected)
            {
                pair.InitiatorCarrier.Peer = null;
                pair.AcceptorCarrier.Peer = null;
            }
            await pair.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.That(pair.InitiatorBudget.Usage.Contexts, Is.Zero);
            Assert.That(pair.AcceptorBudget.Usage.Contexts, Is.Zero);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task AllProfilesExchangeCompleteOwnedMessagesWithoutSessions(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            byte[] source = [1, 2, 0, 255];
            ByteString payload = FlowLoopback.TestPayload(kind, new ByteString(source));
            byte[] expected = payload.ToArray();
            DataChannelResult sent = await flow.Local.SendAsync(payload)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            source[0] = 99;
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(sent, Is.EqualTo(DataChannelResult.Good));
                Assert.That(received, Is.Not.Null);
                Assert.That(received!.Payload.ToArray(), Is.EqualTo(expected));
                Assert.That(received.Revision, Is.EqualTo(1));
                Assert.That(received.MessageId, Is.EqualTo(1));
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(flow.Remote.GetStatus().SessionId.IsNull, Is.True);
                Assert.That(pair.Initiator.Identity.ContextId, Is.EqualTo(pair.Acceptor.Identity.ContextId));
                Assert.That(pair.Initiator.Identity.Generation, Is.Not.Zero);
                Assert.That(pair.Initiator.Identity.LocalChallenge, Is.EqualTo(pair.Acceptor.Identity.PeerChallenge));
                Assert.That(pair.Initiator.Identity.LocalChallenge,
                    Is.Not.EqualTo(pair.Acceptor.Identity.LocalChallenge));
                Assert.That(flow.Local.CarrierKind, Is.EqualTo(kind));
            });
        }

        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.Quic)]
        public async Task AcceptorCanOpenEvenFlowAndPublishIndependentlyOfTransportRole(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(
                FlowLoopback.Parameters(DataChannelDirection.OpenerToAcceptor), false).ConfigureAwait(false);
            Assert.That(flow.Local.ChannelId, Is.EqualTo(2));
            Assert.That(flow.Local.IsLocalOpener, Is.True);
            Assert.That(flow.Remote.IsLocalOpener, Is.False);
            Assert.That(await flow.Remote.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            ByteString payload = FlowLoopback.TestPayload(kind, ByteString.Empty);
            Assert.That(await flow.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.Length, Is.EqualTo(payload.Length));
            Assert.That(message.MessageId, Is.EqualTo(1));
        }

        [Test]
        public async Task DedicatedAcceptorAllocatesEvenFlowIdentifiers()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.DedicatedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(fromInitiator: false).ConfigureAwait(false);
            Assert.That(flow.Local.ChannelId, Is.EqualTo(2));
            uint next = pair.Acceptor.ReserveChannelId();
            Assert.That(next, Is.EqualTo(4));
        }

        [Test]
        public async Task CancelledGrantReservationReleasesBudgetsButNeverReusesIdentifier()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            uint first = pair.Initiator.ReserveChannelId();
            Assert.That(pair.InitiatorBudget.Usage.Flows, Is.EqualTo(1));
            Assert.That(pair.Initiator.ReleaseChannelId(first), Is.True);
            Assert.That(pair.Initiator.ReleaseChannelId(first), Is.False);
            uint next = pair.Initiator.ReserveChannelId();
            Assert.That(next, Is.EqualTo(first + 2));
            Assert.That(pair.InitiatorBudget.Usage.Flows, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadinessIsNotInferredBeforeReadyCommitOrReceipt()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore =
                write => TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Ready;
            Task<DataChannel> opened = pair.Initiator.OpenAsync(FlowLoopback.Parameters()).AsTask();
            using var cancellation = new CancellationTokenSource();
            Task<DataChannel?> accepted = pair.Acceptor.AcceptAsync(cancellation.Token).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(opened.IsCompleted, Is.False);
                Assert.That(accepted.IsCompleted, Is.False);
                Assert.That(pair.Acceptor.Channels[0].State, Is.EqualTo(DataChannelState.Opening));
            });
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            DataChannel local = await opened.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannel? remote = await accepted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(remote!.State, Is.EqualTo(DataChannelState.Open));
        }

        [Test]
        public async Task NativeStreamsReassembleEveryByteBoundaryWithoutPartialDelivery()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            pair.InitiatorCarrier.SegmentSize = 1;
            pair.AcceptorCarrier.SegmentSize = 1;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            byte[] payload = [.. Enumerable.Range(0, 128).Select(value => (byte)value)];
            Assert.That(await flow.Local.SendAsync(new ByteString(payload)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.Payload.ToArray(), Is.EqualTo(payload));
            Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
        }

        [Test]
        public async Task NoSuccessfulCarrierWriteWithoutActualCommitment()
        {
            await using var pair = new FlowLoopback();
            pair.InitiatorCarrier.SkipCommit = true;
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(
                pair.StartAsync).ConfigureAwait(false);
            Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(pair.Initiator.IsReady, Is.False);
            Assert.That(pair.InitiatorBudget.Usage.Contexts, Is.Zero);
        }
    }
}
