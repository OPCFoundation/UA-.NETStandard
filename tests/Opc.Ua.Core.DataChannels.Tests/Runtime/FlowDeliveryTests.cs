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
    public sealed class FlowDeliveryTests
    {
        [Test]
        public async Task EmittedConnectionCreditIsRevisionIndependentEvenWhenFlowCreditIsRevisionScoped()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            await pair.OpenAsync(FlowLoopback.Parameters(DataChannelDirection.OpenerToAcceptor))
                .ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Count(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit) >= 2)
                .ConfigureAwait(false);
            foreach (CapturedFlowWrite write in pair.AcceptorCarrier.Writes)
            {
                FlowRecord record = TestFlowCarrier.Record(write, pair.AcceptorCarrier.Kind);
                if (record.Kind != FlowRecordKind.Control ||
                    FlowControlCodec.Decode(in record, new FlowCodecLimits()) is not FlowCredit credit)
                {
                    continue;
                }
                if (credit.ConnectionCredit != 0)
                {
                    Assert.That(credit.Flow, Is.Zero);
                    Assert.That(record.Revision, Is.Zero);
                    Assert.That(credit.ChannelCredit, Is.Zero);
                }
                else
                {
                    Assert.That(credit.Flow, Is.EqualTo(1));
                    Assert.That(record.Revision, Is.EqualTo(1));
                    Assert.That(credit.ChannelCredit, Is.EqualTo(1024));
                }
            }
        }

        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task NativeLateResetRetainsPartialFailureWithoutClosingOtherFlows(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair reset = await pair.OpenAsync().ConfigureAwait(false);
            FlowPair healthy = await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.GateAfter = write => write.IsData && write.ChannelId == reset.Local.ChannelId;
            pair.InitiatorCarrier.SendOutcome = write => write.IsData && write.ChannelId == reset.Local.ChannelId
                ? DataChannelResult.Closed : DataChannelResult.Good;
            Task<DataChannelResult> sending = reset.Local.SendAsync(ByteString.Empty).AsTask();
            await pair.InitiatorCarrier.AfterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task closing = reset.Local.ResetAsync(DataChannelResult.NotAuthorized).AsTask();
            await FlowLoopback.EventuallyAsync(() => reset.Local.State == DataChannelState.Faulted)
                .ConfigureAwait(false);
            pair.InitiatorCarrier.AfterRelease.TrySetResult(true);
            Assert.That(await sending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Closed));
            await closing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(healthy.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(await healthy.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await healthy.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.MessageId, Is.EqualTo(1));
        }

        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task NativeStreamFailureFaultsOnlyItsFlow(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair failed = await pair.OpenAsync().ConfigureAwait(false);
            FlowPair healthy = await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.SendOutcome = write => write.IsData && write.ChannelId == failed.Local.ChannelId
                ? DataChannelResult.Closed : DataChannelResult.Good;
            Assert.That(await failed.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Closed));
            Assert.That(failed.Local.State, Is.EqualTo(DataChannelState.Faulted));
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(await healthy.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await healthy.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Length, Is.Zero);
        }

        [Test]
        public async Task NativeDatagramMtuDeclineRefusesOneMessageWithoutClosingContext()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            pair.InitiatorCarrier.SendOutcome = write => write.IsData
                ? DataChannelResult.TooLarge : DataChannelResult.Good;
            Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
            pair.InitiatorCarrier.SendOutcome = null;
            Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.MessageId, Is.EqualTo(2));
            Assert.That(flow.Local.GetDiagnostics().MessagesSent, Is.EqualTo(1));
        }

        [Test]
        public async Task SharedCreditStartsAtZeroAndChargesEmptyRecordsSeventeenBytes()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(DataChannelDirection.OpenerToAcceptor))
                .ConfigureAwait(false);
            Task<DataChannelResult> pending = flow.Local.SendAsync(ByteString.Empty).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(pair.InitiatorCarrier.Writes.Count(write => write.IsData), Is.Zero);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowCredit(flow.Local.ChannelId, 16, 16), 1, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowCredit(flow.Local.ChannelId, 1, 1), 1, 101).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.Length, Is.Zero);
                Assert.That(flow.Local.GetDiagnostics().BytesSent, Is.EqualTo(17));
                Assert.That(flow.Remote.GetDiagnostics().BytesReceived, Is.EqualTo(17));
            });
        }

        [Test]
        public async Task CreditDuplicateWrongRevisionAndOverflowCannotAdvanceBalancesOrHighWater()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(DataChannelDirection.OpenerToAcceptor))
                .ConfigureAwait(false);
            var grant = new FlowCredit(flow.Local.ChannelId, 17, 17);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(grant, 2, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(grant, 1, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(grant, 1, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Task<DataChannelResult> second = flow.Local.SendAsync(ByteString.Empty).AsTask();
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowCredit(flow.Local.ChannelId, uint.MaxValue, 17), 1, 101).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(grant, 1, 101).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? next = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(next!.MessageId, Is.EqualTo(2));
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.Quic)]
        public async Task OrderedReceiveBackpressureRetainsOneMessageUntilItsLeaseIsDisposed(
            DataChannelCarrierKind kind)
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 1,
                MaxContextMessages = 32
            };
            await using var pair = new FlowLoopback(kind, options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Task<DataChannelResult> second = flow.Local.SendAsync(new ByteString(new byte[] { 2 })).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorCarrier.Writes.Count(write => write.IsData) == 2)
                .ConfigureAwait(false);
            Assert.That(second.IsCompleted, Is.False);
            DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first!.Payload[0], Is.EqualTo(1));
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
            first!.Dispose();
            Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? next = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(next!.Payload[0], Is.EqualTo(2));
        }

        [Test]
        public async Task CancellationBeforeCommitDiscardsOwnedPayloadWithoutDeliveringIt()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write => write.IsData;
            using var cancellation = new CancellationTokenSource();
            Task<DataChannelResult> send = flow.Local.SendAsync(
                new ByteString("*"u8.ToArray()), cancellationToken: cancellation.Token).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () => await send.ConfigureAwait(false));
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorBudget.Usage.BufferedBytes == 0)
                .ConfigureAwait(false);
            Assert.That(flow.Local.GetDiagnostics().MessagesSent, Is.Zero);
            Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
        }

        [Test]
        public async Task CancellationAfterCommitCannotReleaseOrRetractWriterOwnedMessage()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorBudget.Usage.BufferedBytes == 0)
                .ConfigureAwait(false);
            pair.InitiatorCarrier.GateAfter = write => write.IsData;
            byte[] source = [12, 34];
            using var cancellation = new CancellationTokenSource();
            Task<DataChannelResult> send = flow.Local.SendAsync(
                new ByteString(source), cancellationToken: cancellation.Token).AsTask();
            await pair.InitiatorCarrier.AfterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            source[0] = 99;
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () => await send.ConfigureAwait(false));
            Assert.That(pair.InitiatorBudget.Usage.BufferedBytes, Is.EqualTo(19));
            pair.InitiatorCarrier.AfterRelease.TrySetResult(true);
            await FlowLoopback.EventuallyAsync(() => flow.Local.GetDiagnostics().MessagesSent == 1)
                .ConfigureAwait(false);
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Payload.ToArray(), Is.EqualTo(new byte[] { 12, 34 }));
            Assert.That(pair.InitiatorBudget.Usage.BufferedBytes, Is.Zero);
        }

        [Test]
        public async Task QueueExpiryDropsBeforeCommitAtExactDeadlineButNeverAfterCommit()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(expiry: true)).ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write => write.IsData;
            Task<DataChannelResult> expiring = flow.Local.SendAsync(ByteString.Empty, TimeSpan.FromSeconds(1)).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            Assert.That(await expiring.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            Assert.That(flow.Local.GetDiagnostics().QueueExpiryDrops, Is.EqualTo(1));
            pair.InitiatorCarrier.GateBefore = null;
            pair.InitiatorCarrier.GateAfter = write => write.IsData;
            Task<DataChannelResult> committed = flow.Local.SendAsync(
                ByteString.Empty, TimeSpan.FromSeconds(1)).AsTask();
            await pair.InitiatorCarrier.AfterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(committed.IsCompleted, Is.False);
            pair.InitiatorCarrier.AfterRelease.TrySetResult(true);
            Assert.That(await committed.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.MessageId, Is.EqualTo(2));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task ExpiryIsNeverSilentlyEnabledByTheSender(int milliseconds)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(ByteString.Empty, TimeSpan.FromMilliseconds(milliseconds))
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(flow.Local.GetDiagnostics().MessagesSent, Is.Zero);
        }

        [Test]
        [Repeat(20)]
        public async Task DatagramBucketRefillsAtTheExactMonotonicRateBoundary()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, maxMessage: 1, receiveBytes: 18, rate: 18))
                .ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false))
            {
                Assert.That(first!.Length, Is.EqualTo(1));
            }
            Task<DataChannelResult> second = flow.Local.SendAsync(new ByteString(new byte[] { 2 })).AsTask();
            pair.Clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(second.IsCompleted, Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Payload[0], Is.EqualTo(2));
            Assert.That(flow.Local.GetDiagnostics().BytesSent, Is.EqualTo(36));
        }

        [Test]
        public async Task DatagramAdmissionDropDoesNotClaimDeliveryOrLeakAdditiveCredit()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, maxMessage: 1, receiveBytes: 18, rate: 18))
                .ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[] { 2 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(flow.Remote.GetDiagnostics().AdmissionDrops, Is.EqualTo(1));
                Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.EqualTo(18 + (2 * FlowStreamInput.ParserStorageBytes)));
                Assert.That(pair.AcceptorCarrier.Writes.Any(
                    write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit),
                    Is.False);
            });
            using DataChannelMessage? retained = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(retained!.Payload[0], Is.EqualTo(1));
        }

        [Test]
        public async Task DatagramReplayWindowAcceptsNovelReorderingAtSixtyThreeButNotSixtyFour()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            uint id = flow.Local.ChannelId;
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(id, 1, 65, ByteString.Empty, true)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(id, 1, 2, ByteString.Empty, true)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(id, 1, 2, ByteString.Empty, true)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(id, 1, 1, ByteString.Empty, true)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(id, 2, 66, ByteString.Empty, true)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidState));
            using DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false);
            using DataChannelMessage? reordered = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first!.MessageId, Is.EqualTo(65));
                Assert.That(reordered!.MessageId, Is.EqualTo(2));
                Assert.That(flow.Remote.GetDiagnostics().ReplayDrops, Is.EqualTo(2));
                Assert.That(flow.Remote.GetDiagnostics().RevisionDrops, Is.EqualTo(1));
            });
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        public async Task UascNeverEmulatesRequestedDatagrams(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelException? failure = Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Initiator.OpenAsync(FlowLoopback.Parameters(
                    delivery: DataChannelDeliveryMode.Datagram, rate: 1000)).ConfigureAwait(false));
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.Unsupported));
            Assert.That(pair.Initiator.Channels.IsEmpty, Is.True);
        }
    }
}
