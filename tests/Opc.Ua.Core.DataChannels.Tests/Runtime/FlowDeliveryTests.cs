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
            ByteString payload = new(new byte[] { 1, 2, 3 });
            Assert.That(await flow.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.Multiple(() =>
            {
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(flow.Local.GetDiagnostics().BytesSent, Is.Zero);
            });
            pair.InitiatorCarrier.SendOutcome = null;
            Assert.That(await flow.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(received!.MessageId, Is.EqualTo(2));
                Assert.That(received.Payload, Is.EqualTo(payload));
                Assert.That(flow.Local.GetDiagnostics().MessagesSent, Is.EqualTo(1));
                Assert.That(flow.Local.GetDiagnostics().BytesSent, Is.EqualTo((ulong)payload.Length + 17));
                Assert.That(flow.Remote.GetDiagnostics().BytesReceived, Is.EqualTo((ulong)payload.Length + 17));
            });
        }

        [Test]
        public async Task SharedCreditStartsAtZeroAndChargesTheCompleteFloBody()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(DataChannelDirection.OpenerToAcceptor))
                .ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x01 }));
            Task<DataChannelResult> pending = flow.Local.SendAsync(payload).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(pair.InitiatorCarrier.Writes.Count(write => write.IsData), Is.Zero);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowCredit(0, 0, (uint)payload.Length), 0, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowCredit(flow.Local.ChannelId, (uint)(payload.Length - 1), 0), 1, 101)
                .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowCredit(flow.Local.ChannelId, 1, 0), 1, 102).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.ToArray(), Is.EqualTo(payload.ToArray()));
                Assert.That(flow.Local.GetDiagnostics().BytesSent, Is.EqualTo(payload.Length));
                Assert.That(flow.Remote.GetDiagnostics().BytesReceived, Is.EqualTo(payload.Length));
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
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x01 }));
            uint payloadSize = checked((uint)payload.Length);
            var grant = new FlowCredit(flow.Local.ChannelId, payloadSize, 0);
            var connectionGrant = new FlowCredit(0, 0, payloadSize);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(connectionGrant, 0, 100)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(grant, 2, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(grant, 1, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(grant, 1, 100).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await flow.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Task<DataChannelResult> second = flow.Local.SendAsync(payload).AsTask();
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                new FlowCredit(flow.Local.ChannelId, uint.MaxValue, 0), 1, 101).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(connectionGrant, 0, 101)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
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
            ByteString firstPayload = FlowLoopback.TestPayload(kind, new ByteString(new byte[] { 1 }));
            var options = new DataChannelOptions
            {
                MaxMessageSize = firstPayload.Length,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 1,
                MaxContextMessages = 32
            };
            await using var pair = new FlowLoopback(kind, options);
            await pair.StartAsync().ConfigureAwait(false);
            uint receiveBytes = (uint)(firstPayload.Length +
                (kind is DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport ? 13 : 0));
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                maxMessage: (uint)firstPayload.Length, receiveBytes: receiveBytes))
                .ConfigureAwait(false);
            ByteString secondPayload = FlowLoopback.TestPayload(kind, new ByteString(new byte[] { 2 }));
            Assert.That(await flow.Local.SendAsync(firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Task<DataChannelResult> second = flow.Local.SendAsync(secondPayload).AsTask();
            if (kind is DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport)
            {
                await FlowLoopback.EventuallyAsync(
                    () => pair.InitiatorCarrier.Writes.Count(write => write.IsData) == 2).ConfigureAwait(false);
            }
            else
            {
                Assert.That(pair.InitiatorCarrier.Writes.Count(write => write.IsData), Is.EqualTo(1));
            }
            Assert.That(second.IsCompleted, Is.False);
            DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first!.Payload.ToArray(), Is.EqualTo(firstPayload.ToArray()));
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
            first!.Dispose();
            Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? next = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(next!.Payload.ToArray(), Is.EqualTo(secondPayload.ToArray()));
        }

        [Test]
        public async Task CancellationBeforeCommitDiscardsOwnedPayloadWithoutDeliveringIt()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write => write.IsData;
            using var cancellation = new CancellationTokenSource();
            Task<DataChannelResult> send = flow.Local.SendAsync(FlowLoopback.UadpPayload(
                new ByteString("*"u8.ToArray())), cancellationToken: cancellation.Token).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () => await send.ConfigureAwait(false))
                .ConfigureAwait(false);
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
            byte[] source = FlowLoopback.UadpPayload(new ByteString(new byte[] { 12, 34 })).ToArray();
            byte[] expected = source.ToArray();
            using var cancellation = new CancellationTokenSource();
            Task<DataChannelResult> send = flow.Local.SendAsync(
                new ByteString(source), cancellationToken: cancellation.Token).AsTask();
            await pair.InitiatorCarrier.AfterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            source[^1] = 99;
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () => await send.ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(pair.InitiatorBudget.Usage.BufferedBytes, Is.EqualTo(expected.Length));
            pair.InitiatorCarrier.AfterRelease.TrySetResult(true);
            await FlowLoopback.EventuallyAsync(() => flow.Local.GetDiagnostics().MessagesSent == 1)
                .ConfigureAwait(false);
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Payload.ToArray(), Is.EqualTo(expected));
            Assert.That(pair.InitiatorBudget.Usage.BufferedBytes, Is.Zero);
        }

        [Test]
        public async Task QueueExpiryDropsBeforeCommitAtExactDeadlineButNeverAfterCommit()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(expiry: true)).ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write => write.IsData;
            Task<DataChannelResult> expiring = flow.Local.SendAsync(
                FlowLoopback.UadpPayload(ByteString.Empty), TimeSpan.FromSeconds(1)).AsTask();
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
                FlowLoopback.UadpPayload(ByteString.Empty), TimeSpan.FromSeconds(1)).AsTask();
            await pair.InitiatorCarrier.AfterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(committed.IsCompleted, Is.False);
            pair.InitiatorCarrier.AfterRelease.TrySetResult(true);
            Assert.That(await committed.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.MessageId, Is.EqualTo(1));
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
            Assert.That(flow.Remote.GetDiagnostics().BytesReceived, Is.EqualTo(36));
        }

        [TestCase(DataChannelCarrierKind.Quic, DataChannelDeliveryMode.Ordered)]
        [TestCase(DataChannelCarrierKind.Quic, DataChannelDeliveryMode.Datagram)]
        [TestCase(DataChannelCarrierKind.WebTransport, DataChannelDeliveryMode.Ordered)]
        [TestCase(DataChannelCarrierKind.WebTransport, DataChannelDeliveryMode.Datagram)]
        [TestCase(DataChannelCarrierKind.SharedUasc, DataChannelDeliveryMode.Ordered)]
        public async Task DiagnosticsCountCarrierChargedBytesWithoutTransportOverhead(
            DataChannelCarrierKind carrier,
            DataChannelDeliveryMode delivery)
        {
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: delivery,
                maxMessage: 64,
                receiveBytes: 256,
                rate: delivery == DataChannelDeliveryMode.Datagram ? 4096 : 0)).ConfigureAwait(false);
            ByteString payload = new(new byte[] { 1, 3, 5, 7, 9 });

            Assert.That(await flow.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            ulong chargedBytes = (ulong)payload.Length + (carrier == DataChannelCarrierKind.SharedUasc
                ? 0ul : delivery == DataChannelDeliveryMode.Datagram ? 17ul : 13ul);

            Assert.Multiple(() =>
            {
                Assert.That(received!.Payload, Is.EqualTo(payload));
                Assert.That(flow.Local.GetDiagnostics().BytesSent, Is.EqualTo(chargedBytes));
                Assert.That(flow.Remote.GetDiagnostics().BytesReceived, Is.EqualTo(chargedBytes));
            });
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
                Assert.That(flow.Remote.GetDiagnostics().BytesReceived, Is.EqualTo(18));
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
            DataChannelException? failure = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Initiator.OpenAsync(FlowLoopback.Parameters(
                    delivery: DataChannelDeliveryMode.Datagram, rate: 1000)).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.Unsupported));
            Assert.That(pair.Initiator.Channels.IsEmpty, Is.True);
        }

        [TestCase(DataChannelCarrierKind.SharedUasc)]
        public async Task UascPublisherRoutesSelectExactResourceAndKeepIndependentMessageIds(
            DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:first"))
                .ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:second"))
                .ConfigureAwait(false);
            using IDisposable secondRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:second", new Variant(42u));
            using IDisposable firstRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:first", new Variant(17u));
            ByteString secondPayload = PublisherRoutePayload(new Variant(42u));
            ByteString firstPayload = PublisherRoutePayload(new Variant(17u));

            Assert.That(await ReceivePublisherFloAsync(pair, secondPayload, second.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            await AssertPublisherMessageAsync(second.Remote, secondPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, firstPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, secondPayload, second.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(second.Remote, secondPayload, 2).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(2));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Each half of a duplex flow admits only the PublisherId configured by its receiving context.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        public async Task DuplexFlowAuthorizesPublisherIdsIndependentlyPerDirection(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:duplex"))
                .ConfigureAwait(false);
            using IDisposable openerPublisher = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:duplex", new Variant(42u));
            using IDisposable acceptorPublisher = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:duplex", new Variant(17u));
            ByteString openerPayload = PublisherRoutePayload(new Variant(42u));
            ByteString acceptorPayload = PublisherRoutePayload(new Variant(17u));

            Assert.That(await ReceivePublisherFloAsync(
                pair.Acceptor, openerPayload, flow.Remote).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Remote, openerPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(
                pair.Initiator, acceptorPayload, flow.Local).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Local, acceptorPayload, 1).ConfigureAwait(false);

            Assert.That(await ReceivePublisherFloAsync(
                pair.Acceptor, acceptorPayload, flow.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));
            Assert.That(await ReceivePublisherFloAsync(
                pair.Initiator, openerPayload, flow.Local).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));

            Assert.That(await ReceivePublisherFloAsync(
                pair.Acceptor, openerPayload, flow.Remote).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Remote, openerPayload, 2).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(
                pair.Initiator, acceptorPayload, flow.Local).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Local, acceptorPayload, 2).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Routes an ordinary Part 14 DataSetMessage using its visible typed PublisherId.
        /// </summary>
        [Test]
        public async Task UascPublisherRoutesOrdinaryUadpDataSetMessageByPublisherId()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair target = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:ordinary"))
                .ConfigureAwait(false);
            FlowPair other = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:other"))
                .ConfigureAwait(false);
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:ordinary", new Variant(42u));
            ByteString networkMessage = ByteString.FromHexString("91022a00000021436587");

            Assert.That(await ReceivePublisherFloAsync(pair, networkMessage).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(target.Remote, networkMessage, 1).ConfigureAwait(false);
            Assert.That(other.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
        }

        /// <summary>
        /// Refuses non-UADP bodies even when one ordinary UADP flow needs no routing table.
        /// </summary>
        [TestCase("7b7d", TestName = "UascSingleUadpFlowRejectsJsonNetworkMessage")]
        [TestCase("dead", TestName = "UascSingleUadpFlowRejectsBareOpaqueBytes")]
        public async Task UascSingleUadpFlowRejectsNonUadpPayload(string payloadHex)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:single-uadp"))
                .ConfigureAwait(false);
            ByteString invalidPayload = ByteString.FromHexString(payloadHex);

            Assert.That(await ReceivePublisherFloAsync(pair, invalidPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });

            ByteString validPayload = ByteString.FromHexString("91022a000000dead");
            Assert.That(await ReceivePublisherFloAsync(pair, validPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Remote, validPayload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Requires an explicit application-payload profile before accepting type 3.
        /// </summary>
        [Test]
        public async Task UascOrdinaryUadpFlowRejectsApplicationPayloadType()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:ordinary-uadp"))
                .ConfigureAwait(false);
            using IDisposable ordinaryRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:ordinary-uadp", new Variant(42u), null, null, 0);
            using IDisposable applicationRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:ordinary-uadp", new Variant(42u), null, null, 3);
            ByteString applicationPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), new ByteString(new byte[] { 0x21, 0x43, 0x65, 0x87 }), 256);

            Assert.That(await ReceivePublisherFloAsync(pair, applicationPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
            ByteString validPayload = ByteString.FromHexString("91022a000000dead");
            Assert.That(await ReceivePublisherFloAsync(pair, validPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Remote, validPayload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Routes a chunked ordinary UADP NetworkMessage with its variable-width String PublisherId.
        /// </summary>
        [Test]
        public async Task UascPublisherRoutesChunkedUadpStringPublisherId()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair target = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:string"))
                .ConfigureAwait(false);
            FlowPair other = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:other"))
                .ConfigureAwait(false);
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:string", new Variant("publisher-73001"));
            ByteString networkMessage = ByteString.FromHexString(
                "f1a4010f0000007075626c69736865722d373330303101341245230000000000000000dead");

            Assert.That(await ReceivePublisherFloAsync(pair, networkMessage).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(target.Remote, networkMessage, 1).ConfigureAwait(false);
            Assert.That(other.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
        }

        /// <summary>
        /// Routes visible writer associations and rejects a message whose writers span resources.
        /// </summary>
        [Test]
        public async Task UascPublisherRoutesByWriterAssociationAndRejectsCrossResourceMessages()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:writer-34"))
                .ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:writer-35"))
                .ConfigureAwait(false);
            FlowPair third = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:group-13"))
                .ConfigureAwait(false);
            using IDisposable firstRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:writer-34", new Variant(42u), 12, 34);
            using IDisposable secondRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:writer-35", new Variant(42u), 12, 35);
            using IDisposable thirdRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:group-13", new Variant(42u), 13, 34);
            ByteString firstPayload = UadpWriterPayload(12, 34);
            ByteString secondPayload = UadpWriterPayload(12, 35);
            ByteString thirdPayload = UadpWriterPayload(13, 34);

            Assert.That(await ReceivePublisherFloAsync(pair, firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, firstPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, secondPayload, second.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(second.Remote, secondPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, thirdPayload, third.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(third.Remote, thirdPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, UadpWriterPayload(12, 34, 35), first.Remote)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.UnknownChannel));
            Assert.That(await ReceivePublisherFloAsync(pair, thirdPayload, third.Remote)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(third.Remote, thirdPayload, 2).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, UadpWriterPayload(12, 36), first.Remote)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.UnknownChannel));
            Assert.That(await ReceivePublisherFloAsync(pair, PublisherRoutePayload(new Variant(42u)), first.Remote)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidState));
            Assert.Multiple(() =>
            {
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(third.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(2));
                Assert.That(first.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(second.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(third.Remote.State, Is.EqualTo(DataChannelState.Open));
            });
        }

        /// <summary>
        /// Uses the writer IDs carried in UADP discovery bodies and rejects undiscriminated responses.
        /// </summary>
        [Test]
        public async Task UascDiscoveryMessagesRouteByWriterAndRejectAmbiguousPublisherOnlyResponse()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair target = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:discovery-writer"))
                .ConfigureAwait(false);
            FlowPair other = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:discovery-other"))
                .ConfigureAwait(false);
            using IDisposable targetRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:discovery-writer", new Variant(42u), null, 34);
            using IDisposable otherRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:discovery-other", new Variant(42u), null, 35);
            ByteString request = ByteString.FromHexString("9182042a00000002010000002200");
            ByteString response = ByteString.FromHexString("9182082a0000000201002200dead");
            ByteString undiscriminatedResponse = ByteString.FromHexString("9182082a000000010100dead");

            Assert.That(await ReceivePublisherFloAsync(pair, request).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(target.Remote, request, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, response, target.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(target.Remote, response, 2).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, undiscriminatedResponse, target.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.Multiple(() =>
            {
                Assert.That(target.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(2));
                Assert.That(other.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            });
        }

        /// <summary>
        /// Routes discovery messages without writer IDs through explicit PublisherId, message class, and subtype.
        /// </summary>
        [Test]
        public async Task UascPublisherRoutesPublisherOnlyDiscoveryBySubtype()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair requestTarget = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:discovery-request"))
                .ConfigureAwait(false);
            FlowPair responseTarget = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:discovery-response"))
                .ConfigureAwait(false);
            FlowPair metadataTarget = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:discovery-metadata"))
                .ConfigureAwait(false);
            using IDisposable requestRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:discovery-request", new Variant(42u), null, null, 1, 2);
            using IDisposable responseRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:discovery-response", new Variant(42u), null, null, 2, 1);
            using IDisposable metadataRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:discovery-metadata", new Variant(42u), null, null, 2, 2);
            ByteString request = ByteString.FromHexString("9182042a0000000200000000");
            ByteString publisherEndpoints = ByteString.FromHexString("9182082a00000001010000000000000000");
            ByteString metadataResponse = ByteString.FromHexString("9182082a0000000201002200dead");

            Assert.That(await ReceivePublisherFloAsync(pair, request, requestTarget.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(requestTarget.Remote, request, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(
                pair, publisherEndpoints, responseTarget.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(responseTarget.Remote, publisherEndpoints, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, metadataResponse, metadataTarget.Remote)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(metadataTarget.Remote, metadataResponse, 1).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(requestTarget.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(responseTarget.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(metadataTarget.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Rejects secured discovery when its hidden subtype would select a subtype-scoped route.
        /// </summary>
        [Test]
        public async Task UascPublisherRoutesRejectSecuredDiscoveryWithHiddenSubtype()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:secured-first"))
                .ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:secured-second"))
                .ConfigureAwait(false);
            FlowPair broad = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:secured-broad"))
                .ConfigureAwait(false);
            using IDisposable firstRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:secured-first", new Variant(42u), null, 34, 2, 2);
            using IDisposable secondRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:secured-second", new Variant(42u), null, 35, 2, 2);
            using IDisposable broadRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:secured-broad", new Variant(42u), null, null, 2);
            ByteString securedDiscovery = ByteString.FromHexString("9192082a000000deadbeef");

            Assert.That(await ReceivePublisherFloAsync(pair, securedDiscovery, first.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));
            Assert.Multiple(() =>
            {
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(broad.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(first.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(second.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(broad.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Uses NetworkMessageType as an independent selector for the same publisher and writer.
        /// </summary>
        [Test]
        public async Task UascPublisherRoutesByNetworkMessageType()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair dataTarget = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:typed-data"))
                .ConfigureAwait(false);
            FlowPair discoveryTarget = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:typed-discovery"))
                .ConfigureAwait(false);
            DataChannelParametersDataType applicationParameters =
                PublisherRouteParameters("urn:publisher:typed-application");
            applicationParameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            applicationParameters.ContentType = "application/octet-stream";
            using IDisposable localApplicationRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:typed-application", new Variant(42u), null, null, 3);
            using IDisposable dataRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:typed-data", new Variant(42u), 12, 34, 0);
            using IDisposable discoveryRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:typed-discovery", new Variant(42u), null, 34, 2);
            using IDisposable applicationRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:typed-application", new Variant(42u), null, null, 3);
            FlowPair applicationTarget = await pair.OpenAsync(applicationParameters).ConfigureAwait(false);
            ByteString data = UadpWriterPayload(12, 34);
            ByteString discoveryResponse = ByteString.FromHexString("9182082a0000000201002200dead");
            ByteString discoveryRequest = ByteString.FromHexString("9182042a00000002010000002200");
            ByteString applicationPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), new ByteString(new byte[] { 0x21, 0x43, 0x65, 0x87 }), 256);

            Assert.That(await ReceivePublisherFloAsync(pair, data, dataTarget.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(dataTarget.Remote, data, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, discoveryResponse, discoveryTarget.Remote)
                .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(discoveryTarget.Remote, discoveryResponse, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, applicationPayload, applicationTarget.Remote)
                .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(applicationTarget.Remote, applicationPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, discoveryRequest, discoveryTarget.Remote)
                .ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));
            Assert.Multiple(() =>
            {
                Assert.That(dataTarget.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(discoveryTarget.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(applicationTarget.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:invalid-type", Variant.Null, null, null, 4));
            Assert.Throws<ArgumentException>(() =>
            {
                using IDisposable invalid = pair.Acceptor.RegisterUascPublisherRoute(
                    "urn:publisher:invalid-discovery-class", Variant.Null, null, null, 0, 1);
            });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using IDisposable invalid = pair.Acceptor.RegisterUascPublisherRoute(
                    "urn:publisher:invalid-discovery-type", Variant.Null, null, null, 2, 7);
            });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using IDisposable invalid = pair.Acceptor.RegisterUascPublisherRoute(
                    "urn:publisher:invalid-response-probe", Variant.Null, null, null, 2, 6);
            });
        }

        /// <summary>
        /// A minimal shared-UASC receiver must configure finite typed PublisherIds before admission.
        /// </summary>
        [TestCase(DataChannelCarrierKind.SharedUasc, true, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, false, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, true, true)]
        [TestCase(DataChannelCarrierKind.SharedUasc, false, true)]
        [TestCase(DataChannelCarrierKind.QuicUasc, true, false)]
        [TestCase(DataChannelCarrierKind.QuicUasc, false, false)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, true, false)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, false, false)]
        public async Task UascStreamingRequiresTypedReceiveAssociationBeforeReady(
            DataChannelCarrierKind carrier,
            bool fromInitiator,
            bool wildcard)
        {
            const string resource = "urn:publisher:streaming";
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = PublisherRouteParameters(resource);
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";
            parameters.Direction = DataChannelDirection.OpenerToAcceptor;
            DataChannelContext receiver = fromInitiator ? pair.Acceptor : pair.Initiator;
            using IDisposable? route = wildcard
                ? receiver.RegisterUascPublisherRoute(resource, Variant.Null, null, null, 3)
                : null;

            DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            await FlowLoopback.EventuallyAsync(() =>
                pair.Initiator.Channels.IsEmpty && pair.Acceptor.Channels.IsEmpty).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.InitiatorCarrier.Writes.Concat(pair.AcceptorCarrier.Writes).Any(write =>
                    TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Ready), Is.False);
                Assert.That(pair.InitiatorBudget.Usage.Flows, Is.Zero);
                Assert.That(pair.AcceptorBudget.Usage.Flows, Is.Zero);
                Assert.That(pair.InitiatorBudget.Usage.Operations, Is.Zero);
                Assert.That(pair.AcceptorBudget.Usage.Operations, Is.Zero);
            });
            using IDisposable typedRoute = receiver.RegisterUascPublisherRoute(
                resource, new Variant(42u), null, null, 3);
            FlowPair admitted = await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false);
            ByteString payload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("dead"), 64);
            Assert.That(await admitted.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(admitted.Remote, payload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Ordinary message-class and writer-only associations cannot authorize a minimal streaming header.
        /// </summary>
        [TestCase((ushort)12, null, null)]
        [TestCase(null, (ushort)34, null)]
        [TestCase(null, null, (byte)0)]
        [TestCase(null, null, (byte)1)]
        [TestCase(null, null, (byte)2)]
        public async Task UascStreamingRequiresCompatiblePublisherSelectorsBeforeReady(
            ushort? writerGroupId,
            ushort? dataSetWriterId,
            byte? networkMessageType)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using IDisposable localRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:selectors", new Variant(42u), writerGroupId, dataSetWriterId, networkMessageType);
            using IDisposable remoteRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:selectors", new Variant(42u), writerGroupId, dataSetWriterId, networkMessageType);
            DataChannelParametersDataType parameters = PublisherRouteParameters("urn:publisher:selectors");
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";

            DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.OpenAsync(parameters).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(pair.InitiatorCarrier.Writes.Concat(pair.AcceptorCarrier.Writes).Any(write =>
                TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Ready), Is.False);
        }

        /// <summary>
        /// A second independently admitted flow cannot take an already admitted receive PublisherId.
        /// </summary>
        [TestCase(true, DataChannelDirection.OpenerToAcceptor)]
        [TestCase(false, DataChannelDirection.OpenerToAcceptor)]
        [TestCase(true, DataChannelDirection.AcceptorToOpener)]
        [TestCase(false, DataChannelDirection.AcceptorToOpener)]
        [TestCase(true, DataChannelDirection.Duplex)]
        [TestCase(false, DataChannelDirection.Duplex)]
        public async Task UascStreamingDuplicatePublisherIsRefusedBeforeReady(
            bool fromInitiator,
            DataChannelDirection direction)
        {
            const string resource = "urn:publisher:streaming";
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = PublisherRouteParameters(resource);
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";
            using IDisposable firstRoute = pair.Initiator.RegisterUascPublisherRoute(
                resource, new Variant(42u), null, null, 3);
            using IDisposable secondRoute = pair.Acceptor.RegisterUascPublisherRoute(
                resource, new Variant(42u), null, null, 3);
            FlowPair original = await pair.OpenAsync(parameters).ConfigureAwait(false);
            parameters.Direction = direction;

            DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            await FlowLoopback.EventuallyAsync(() =>
                pair.Initiator.Channels.Count == 1 && pair.Acceptor.Channels.Count == 1).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(original.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(original.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.InitiatorCarrier.Writes.Concat(pair.AcceptorCarrier.Writes).Count(write =>
                    TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Ready),
                    Is.EqualTo(1));
            });
            ByteString payload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("dead"), 64);
            Assert.That(await original.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(original.Remote, payload, 1).ConfigureAwait(false);
            await original.Local.ResetAsync().ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() =>
                pair.Initiator.Channels.IsEmpty && pair.Acceptor.Channels.IsEmpty).ConfigureAwait(false);
            FlowPair replacement = await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false);
            Assert.That(replacement.Local.ChannelId, Is.Not.EqualTo(original.Local.ChannelId));
            DataChannel sender = direction == DataChannelDirection.AcceptorToOpener
                ? replacement.Remote : replacement.Local;
            DataChannel receiver = direction == DataChannelDirection.AcceptorToOpener
                ? replacement.Local : replacement.Remote;
            Assert.That(await sender.SendAsync(payload).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(receiver, payload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// A peer refusal releases the opener's provisional associations so a corrected configuration can retry.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task UascStreamingPeerRefusalReleasesProvisionalPublisherIds(bool fromInitiator)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelContext opener = fromInitiator ? pair.Initiator : pair.Acceptor;
            DataChannelContext acceptor = fromInitiator ? pair.Acceptor : pair.Initiator;
            using IDisposable localRoute = opener.RegisterUascPublisherRoute(
                "urn:publisher:retry", new Variant(42u), null, null, 3);
            DataChannelParametersDataType parameters = PublisherRouteParameters("urn:publisher:retry");
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";
            DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            await FlowLoopback.EventuallyAsync(() =>
                pair.Initiator.Channels.IsEmpty && pair.Acceptor.Channels.IsEmpty).ConfigureAwait(false);
            using IDisposable remoteRoute = acceptor.RegisterUascPublisherRoute(
                "urn:publisher:retry", new Variant(42u), null, null, 3);
            FlowPair admitted = await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false);
            ByteString payload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("dead"), 64);
            Assert.That(await admitted.Local.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(admitted.Remote, payload, 1).ConfigureAwait(false);
            Assert.That(await admitted.Remote.SendAsync(payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(admitted.Local, payload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Equal PublisherId values with different encoded types remain independent streaming identities.
        /// </summary>
        [TestCaseSource(nameof(TypedPublisherRouteCases))]
        public async Task UascStreamingTypedPublisherIdsRemainDistinct(Variant publisherId)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            Variant otherId = publisherId.Equals(new Variant(7u)) ? new Variant((ushort)7) : new Variant(7u);
            using IDisposable firstLocalRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:first", publisherId, null, null, 3);
            using IDisposable firstRemoteRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:first", publisherId, null, null, 3);
            using IDisposable secondLocalRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:second", otherId, null, null, 3);
            using IDisposable secondRemoteRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:second", otherId, null, null, 3);
            DataChannelParametersDataType parameters = PublisherRouteParameters("urn:publisher:first");
            parameters.Protocol = DataChannelProfiles.UadpAvApplicationPayload;
            parameters.ContentType = "video/h264";
            FlowPair first = await pair.OpenAsync(parameters).ConfigureAwait(false);
            parameters.ResourceUri = "urn:publisher:second";
            FlowPair second = await pair.OpenAsync(parameters, fromInitiator: false).ConfigureAwait(false);
            ByteString firstPayload = UadpApplicationPayloadHeaderCodec.Encode(
                publisherId, ByteString.FromHexString("dead"), 64);
            ByteString secondPayload = UadpApplicationPayloadHeaderCodec.Encode(
                otherId, ByteString.FromHexString("beef"), 64);
            Assert.That(await first.Local.SendAsync(firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.Remote.SendAsync(secondPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, firstPayload, 1).ConfigureAwait(false);
            await AssertPublisherMessageAsync(second.Local, secondPayload, 1).ConfigureAwait(false);
            Assert.That(await first.Remote.SendAsync(firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.Local.SendAsync(secondPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Local, firstPayload, 1).ConfigureAwait(false);
            await AssertPublisherMessageAsync(second.Remote, secondPayload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Identical typed PublisherIds may be admitted on independent opposite receiving directions.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task UascStreamingOppositeReceiveDirectionsCanReusePublisherIds(bool fromInitiator)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using IDisposable localRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:opposite", new Variant(42u), null, null, 3);
            using IDisposable remoteRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:opposite", new Variant(42u), null, null, 3);
            DataChannelParametersDataType parameters = PublisherRouteParameters("urn:publisher:opposite");
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";
            parameters.Direction = DataChannelDirection.OpenerToAcceptor;
            FlowPair first = await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false);
            parameters.Direction = DataChannelDirection.AcceptorToOpener;
            FlowPair second = await pair.OpenAsync(parameters, fromInitiator).ConfigureAwait(false);
            ByteString firstPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("dead"), 64);
            ByteString secondPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("beef"), 64);

            Assert.That(await first.Local.SendAsync(firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.Remote.SendAsync(secondPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, firstPayload, 1).ConfigureAwait(false);
            await AssertPublisherMessageAsync(second.Local, secondPayload, 1).ConfigureAwait(false);
            Assert.That(first.Local.ChannelId, Is.Not.EqualTo(second.Local.ChannelId));
        }

        /// <summary>
        /// Configuration ownership and route edits cannot redirect already admitted streaming DATA.
        /// </summary>
        [Test]
        public async Task UascStreamingAdmissionSnapshotSurvivesRouteRebindingAndReferenceDisposal()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using IDisposable firstLocal = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:shared", new Variant(42u), null, null, 3);
            using IDisposable firstRemote = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:shared", new Variant(42u), null, null, 3);
            using IDisposable retainedLocal = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:shared", new Variant(42u), null, null, 3);
            using IDisposable retainedRemote = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:shared", new Variant(42u), null, null, 3);
            firstLocal.Dispose();
            firstRemote.Dispose();
            DataChannelParametersDataType parameters = PublisherRouteParameters("urn:publisher:shared");
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";
            FlowPair first = await pair.OpenAsync(parameters).ConfigureAwait(false);
            retainedLocal.Dispose();
            retainedRemote.Dispose();
            using IDisposable newLocal = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:shared", new Variant(43u), null, null, 3);
            using IDisposable newRemote = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:shared", new Variant(43u), null, null, 3);
            FlowPair second = await pair.OpenAsync(parameters).ConfigureAwait(false);
            ByteString firstPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("dead"), 64);
            ByteString secondPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(43u), ByteString.FromHexString("beef"), 64);
            Assert.That(await first.Local.SendAsync(firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.Local.SendAsync(secondPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, firstPayload, 1).ConfigureAwait(false);
            await AssertPublisherMessageAsync(second.Remote, secondPayload, 1).ConfigureAwait(false);
            using IDisposable lateRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:shared", new Variant(99u), null, null, 3);
            using IDisposable lateWildcard = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:shared", Variant.Null, null, null, 3);
            ByteString unadmittedPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(99u), ByteString.FromHexString("cafe"), 64);
            Assert.That(await ReceivePublisherFloAsync(pair, unadmittedPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));
            using IDisposable conflictingLocal = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:rebound", new Variant(42u), null, null, 3);
            using IDisposable conflictingRemote = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:rebound", new Variant(42u), null, null, 3);
            parameters.ResourceUri = "urn:publisher:rebound";
            DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.OpenAsync(parameters).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(second.Local.State, Is.EqualTo(DataChannelState.Open));
            await first.Local.ResetAsync().ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() =>
                pair.Initiator.Channels.Count == 1 && pair.Acceptor.Channels.Count == 1).ConfigureAwait(false);
            FlowPair replacement = await pair.OpenAsync(parameters).ConfigureAwait(false);
            Assert.That(await replacement.Local.SendAsync(firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(replacement.Remote, firstPayload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Provisional admissions reserve their typed receive identities before the first OPEN is committed.
        /// </summary>
        [Test]
        public async Task UascStreamingPendingAdmissionReservesPublisherIds()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using IDisposable localRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:pending", new Variant(42u), null, null, 3);
            using IDisposable remoteRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:pending", new Variant(42u), null, null, 3);
            DataChannelParametersDataType parameters = PublisherRouteParameters("urn:publisher:pending");
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";
            pair.InitiatorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Open;
            try
            {
                Task<FlowPair> pending = pair.OpenAsync(parameters);
                await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task<DataChannel> rejected = pair.Initiator.OpenAsync(parameters).AsTask();
                await FlowLoopback.EventuallyAsync(() =>
                    pair.InitiatorPolicy.Requests.Count == 2 && pair.Initiator.Channels.Count == 1)
                    .ConfigureAwait(false);
                Assert.That(pair.Acceptor.Channels.IsEmpty, Is.True);
                Assert.That(pair.Initiator.Channels[0].State, Is.EqualTo(DataChannelState.Opening));
                pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
                FlowPair admitted = await pending.ConfigureAwait(false);
                DataChannelException failure = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await rejected.ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(failure.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(admitted.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.InitiatorCarrier.Writes.Count(write =>
                    TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Ready),
                    Is.EqualTo(1));
            }
            finally
            {
                pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            }
        }

        /// <summary>
        /// Refuses selectors absent from minimal streaming headers without changing ordinary writer routing.
        /// </summary>
        [TestCase((ushort)0, null)]
        [TestCase((ushort)12, null)]
        [TestCase(null, (ushort)0)]
        [TestCase(null, (ushort)34)]
        [TestCase((ushort)12, (ushort)34)]
        public async Task UascApplicationPublisherRouteRejectsAbsentWriterFields(
            ushort? writerGroupId,
            ushort? dataSetWriterId)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute(
                    "urn:publisher:invalid-application", new Variant(42u),
                    writerGroupId, dataSetWriterId, 3))!;
            Assert.That(error.ParamName, Is.EqualTo("networkMessageType"));
            using IDisposable applicationRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:application", new Variant(42u), null, null, 3);
            using IDisposable localApplicationRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:publisher:application", new Variant(42u), null, null, 3);
            using IDisposable ordinaryRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:ordinary", new Variant(42u), 12, 34, 0);
            DataChannelParametersDataType parameters = PublisherRouteParameters("urn:publisher:application");
            parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            parameters.ContentType = "application/octet-stream";
            FlowPair application = await pair.OpenAsync(parameters).ConfigureAwait(false);
            FlowPair ordinary = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:ordinary"))
                .ConfigureAwait(false);
            ByteString applicationPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("dead"), 64);
            ByteString ordinaryPayload = UadpWriterPayload(12, 34);
            Assert.That(await ReceivePublisherFloAsync(pair, applicationPayload, application.Remote)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(application.Remote, applicationPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, ordinaryPayload, ordinary.Remote)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(ordinary.Remote, ordinaryPayload, 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Admits type 3 only after selecting an application-payload protocol on the flow.
        /// </summary>
        [TestCase(DataChannelProfiles.UadpAvApplicationPayload, "video/h264",
            TestName = "UascApplicationPayloadAvProfileRoutesTypeThree")]
        [TestCase(DataChannelProfiles.UadpOpaqueApplicationPayload, "application/octet-stream",
            TestName = "UascApplicationPayloadOpaqueProfileRoutesTypeThree")]
        public async Task UascApplicationPayloadProfileRoutesTypeThree(string protocol, string contentType)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = FlowLoopback.Parameters();
            parameters.Protocol = protocol;
            parameters.ContentType = contentType;
            using IDisposable localRoute = pair.Initiator.RegisterUascPublisherRoute(
                "urn:runtime:test:resource", new Variant(42u), null, null, 3);
            using IDisposable remoteRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:runtime:test:resource", new Variant(42u), null, null, 3);
            FlowPair flow = await pair.OpenAsync(parameters).ConfigureAwait(false);
            ByteString applicationPayload = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(42u), ByteString.FromHexString("dead"), 64);

            Assert.That(await ReceivePublisherFloAsync(pair, applicationPayload, flow.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Remote, applicationPayload, 1).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(flow.Remote.Parameters.Protocol, Is.EqualTo(protocol));
                Assert.That(flow.Remote.Parameters.ContentType, Is.EqualTo(contentType));
            });
        }

        /// <summary>
        /// Rejects type-3 ChunkMessage while preserving ordinary Part 14 chunked UADP delivery.
        /// </summary>
        [Test]
        public async Task UascApplicationPayloadRejectsChunkMessageAndPreservesOrdinaryChunking()
        {
            await using (var applicationPair = new FlowLoopback(DataChannelCarrierKind.SharedUasc))
            {
                await applicationPair.StartAsync().ConfigureAwait(false);
                DataChannelParametersDataType parameters = FlowLoopback.Parameters();
                parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
                parameters.ContentType = "application/octet-stream";
                using IDisposable localRoute = applicationPair.Initiator.RegisterUascPublisherRoute(
                    "urn:runtime:test:resource", new Variant((byte)42), null, null, 3);
                using IDisposable remoteRoute = applicationPair.Acceptor.RegisterUascPublisherRoute(
                    "urn:runtime:test:resource", new Variant((byte)42), null, null, 3);
                FlowPair applicationFlow = await applicationPair.OpenAsync(parameters).ConfigureAwait(false);

                Assert.That(await applicationFlow.Local.SendAsync(
                    ByteString.FromHexString("91800d2adead")).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.InvalidArgument));
                Assert.That(applicationFlow.Local.State, Is.EqualTo(DataChannelState.Open));
            }

            await using var ordinaryPair = new FlowLoopback(DataChannelCarrierKind.SharedUasc);
            await ordinaryPair.StartAsync().ConfigureAwait(false);
            FlowPair ordinaryFlow = await ordinaryPair.OpenAsync().ConfigureAwait(false);
            ByteString ordinaryChunk = ByteString.FromHexString(
                "f1a4010f0000007075626c69736865722d373330303101341245230000000000000000dead");

            Assert.That(await ordinaryFlow.Local.SendAsync(ordinaryChunk).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received =
                (await ordinaryFlow.Remote.ReadAsync().ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(ordinaryChunk));
        }

        [TestCase(true, DataChannelCarrierKind.SharedUasc)]
        [TestCase(false, DataChannelCarrierKind.SharedUasc)]
        public async Task UascPublisherExactRoutePrecedesWildcardRegardlessOfRegistrationOrder(
            bool wildcardFirst,
            DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair fallback = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:fallback"))
                .ConfigureAwait(false);
            FlowPair exact = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:exact"))
                .ConfigureAwait(false);
            using IDisposable first = pair.Acceptor.RegisterUascPublisherRoute(
                wildcardFirst ? "urn:publisher:fallback" : "urn:publisher:exact",
                wildcardFirst ? Variant.Null : new Variant(42u));
            using IDisposable second = pair.Acceptor.RegisterUascPublisherRoute(
                wildcardFirst ? "urn:publisher:exact" : "urn:publisher:fallback",
                wildcardFirst ? new Variant(42u) : Variant.Null);
            ByteString exactPayload = PublisherRoutePayload(new Variant(42u));
            ByteString fallbackPayload = PublisherRoutePayload(new Variant("unconfigured-发布者"));

            Assert.That(await ReceivePublisherFloAsync(pair, exactPayload, exact.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(fallback.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            await AssertPublisherMessageAsync(exact.Remote, exactPayload, 1).ConfigureAwait(false);
            Assert.That(await ReceivePublisherFloAsync(pair, fallbackPayload, fallback.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(fallback.Remote, fallbackPayload, 1).ConfigureAwait(false);
            Assert.That(exact.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(TypedPublisherRouteCases))]
        public async Task UascPublisherRoutesPreservePublisherIdType(Variant publisherId)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair exact = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:typed"))
                .ConfigureAwait(false);
            FlowPair other = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:other-type"))
                .ConfigureAwait(false);
            using IDisposable exactRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:typed", publisherId);
            // Equal numeric/text representations are different PublisherIds, not aliases.
            Variant otherId = publisherId.Equals(new Variant(7u)) ? new Variant((ushort)7) : new Variant(7u);
            using IDisposable otherRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:other-type", otherId);
            ByteString payload = PublisherRoutePayload(publisherId);
            Assert.That(await ReceivePublisherFloAsync(pair, payload, exact.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(exact.Remote, payload, 1).ConfigureAwait(false);
            Assert.That(other.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            ByteString otherPayload = PublisherRoutePayload(otherId);
            Assert.That(await ReceivePublisherFloAsync(pair, otherPayload, other.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(other.Remote, otherPayload, 1).ConfigureAwait(false);
            Assert.That(exact.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
        }

        private static System.Collections.Generic.IEnumerable<TestCaseData> TypedPublisherRouteCases()
        {
            yield return new TestCaseData(new Variant((byte)7)).SetArgDisplayNames("Byte");
            yield return new TestCaseData(new Variant((ushort)7)).SetArgDisplayNames("UInt16");
            yield return new TestCaseData(new Variant(7u)).SetArgDisplayNames("UInt32");
            yield return new TestCaseData(new Variant(7ul)).SetArgDisplayNames("UInt64");
            yield return new TestCaseData(new Variant("7")).SetArgDisplayNames("String");
        }

        [TestCase("unmatched", DataChannelResult.UnknownChannel)]
        [TestCase("missing-resource", DataChannelResult.UnknownChannel)]
        [TestCase("ambiguous-exact", DataChannelResult.InvalidState)]
        [TestCase("ambiguous-wildcard", DataChannelResult.InvalidState)]
        [TestCase("unregistered", DataChannelResult.InvalidState)]
        public async Task UascPublisherRouteSelectionNeverDeliversToAnotherReadyFlow(
            string scenario,
            DataChannelResult expected)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:first"))
                .ConfigureAwait(false);
            bool ambiguous = scenario.StartsWith("ambiguous", StringComparison.Ordinal);
            FlowPair second = await pair.OpenAsync(PublisherRouteParameters(
                ambiguous ? "urn:publisher:first" : "urn:publisher:second")).ConfigureAwait(false);
            using IDisposable? route = scenario == "unregistered" ? null :
                pair.Acceptor.RegisterUascPublisherRoute(
                    scenario == "missing-resource" ? "urn:publisher:absent" : "urn:publisher:first",
                    scenario == "ambiguous-wildcard" ? Variant.Null : new Variant(42u));
            using IDisposable? fallback = scenario == "missing-resource" ?
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:second", Variant.Null) : null;
            ByteString payload = PublisherRoutePayload(new Variant(scenario == "unmatched" ? 99u : 42u));

            Assert.That(await ReceivePublisherFloAsync(pair, payload).ConfigureAwait(false), Is.EqualTo(expected));
            Assert.Multiple(() =>
            {
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(first.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(second.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        [TestCaseSource(nameof(MalformedPublisherRouteHeaders))]
        public async Task UascPublisherMalformedHeaderDoesNotUseWildcardOrConsumeMessageId(byte[] header)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:first"))
                .ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:second"))
                .ConfigureAwait(false);
            using IDisposable wildcard = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:first", Variant.Null);
            Assert.That(await ReceivePublisherFloAsync(pair, new ByteString(header)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.Multiple(() =>
            {
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
            ByteString valid = PublisherRoutePayload(new Variant(42u));
            Assert.That(await ReceivePublisherFloAsync(pair, valid).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, valid, 1).ConfigureAwait(false);
            Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
        }

        private static System.Collections.Generic.IEnumerable<TestCaseData> MalformedPublisherRouteHeaders()
        {
            yield return new TestCaseData(new byte[] { 0x91, 0x82 }).SetArgDisplayNames("truncated-header");
            yield return new TestCaseData(new byte[] { 0x90, 0x80, 0x0c, 42 }).SetArgDisplayNames("wrong-flags");
            yield return new TestCaseData(new byte[] { 0x91, 0x80, 0x10, 42 }).SetArgDisplayNames("wrong-message-type");
            yield return new TestCaseData(new byte[] { 0x91, 0x88, 0x0c, 42 }).SetArgDisplayNames("reserved-flags");
            yield return new TestCaseData(new byte[] { 0x91, 0x82, 0x0c, 42 }).SetArgDisplayNames("truncated-publisher");
            yield return new TestCaseData(new byte[] { 0x91, 0x80, 0x0c, 0 }).SetArgDisplayNames("zero-publisher");
            yield return new TestCaseData(new byte[] { 0x91, 0x87, 0x0c, 42 }).SetArgDisplayNames("invalid-publisher-type");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UascPublisherConflictingRegistrationPreservesOriginalRoute(bool wildcard)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:first"))
                .ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:second"))
                .ConfigureAwait(false);
            Variant publisherId = wildcard ? Variant.Null : new Variant(42u);
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:first", publisherId);
            DataChannelException? failure = Assert.Throws<DataChannelException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:second", publisherId));
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            ByteString payload = PublisherRoutePayload(new Variant(42u));
            Assert.That(await ReceivePublisherFloAsync(pair, payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, payload, 1).ConfigureAwait(false);
            Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UascPublisherRegistrationReferenceCountsAndDisposesIdempotently(bool wildcard)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:first"))
                .ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:second"))
                .ConfigureAwait(false);
            Variant publisherId = wildcard ? Variant.Null : new Variant(42u);
            using IDisposable owner = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:first", publisherId);
            using IDisposable duplicate = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:first", publisherId);
            owner.Dispose();
            owner.Dispose();
            DataChannelException? conflict = Assert.Throws<DataChannelException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:second", publisherId));
            Assert.That(conflict!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            ByteString payload = PublisherRoutePayload(new Variant(42u));
            Assert.That(await ReceivePublisherFloAsync(pair, payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(first.Remote, payload, 1).ConfigureAwait(false);
            duplicate.Dispose();
            using IDisposable decoy = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:decoy", new Variant(43u));
            Assert.That(await ReceivePublisherFloAsync(pair, payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel), "The final owner must remove the route.");
            decoy.Dispose();
            using IDisposable replacement = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:second", publisherId);
            owner.Dispose();
            duplicate.Dispose();
            Assert.That(await ReceivePublisherFloAsync(pair, payload, second.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(second.Remote, payload, 1).ConfigureAwait(false);
            Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public async Task UascPublisherRegistrationRejectsMissingResource(string? resourceUri)
        {
            await using var pair = new FlowLoopback();
            ArgumentNullException? failure = Assert.Throws<ArgumentNullException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute(resourceUri!, new Variant(42u)));
            Assert.That(failure!.ParamName, Is.EqualTo("resourceUri"));
            using IDisposable valid = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:valid", new Variant(42u));
        }

        [TestCase("relative/resource")]
        [TestCase("://invalid")]
        public async Task UascPublisherRegistrationRejectsNonAbsoluteResource(string resourceUri)
        {
            await using var pair = new FlowLoopback();
            ArgumentException? failure = Assert.Throws<ArgumentException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute(resourceUri, new Variant(42u)));
            Assert.That(failure!.ParamName, Is.EqualTo("resourceUri"));
            using IDisposable valid = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:valid", new Variant(42u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UascPublisherRegistrationBoundsResourceByUtf8Bytes(bool multibyte)
        {
            await using var pair = new FlowLoopback();
            string boundary = "urn:" + new string(multibyte ? 'é' : 'x', multibyte ? 2046 : 4092);
            ArgumentException? failure = Assert.Throws<ArgumentException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute(boundary + "x", new Variant(42u)));
            Assert.That(failure!.ParamName, Is.EqualTo("resourceUri"));
            using IDisposable valid = pair.Acceptor.RegisterUascPublisherRoute(boundary, new Variant(42u));
            DataChannelException? conflict = Assert.Throws<DataChannelException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:other", new Variant(42u)));
            Assert.That(conflict!.Result, Is.EqualTo(DataChannelResult.InvalidArgument));
        }

        [TestCaseSource(nameof(InvalidPublisherRouteIds))]
        public async Task UascPublisherRegistrationRejectsInvalidPublisherId(Variant publisherId)
        {
            await using var pair = new FlowLoopback();
            Assert.Throws<FlowCodecException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:invalid", publisherId));
            using IDisposable wildcard = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:wildcard", Variant.Null);
            using IDisposable valid = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:valid", new Variant(42u));
        }

        private static System.Collections.Generic.IEnumerable<TestCaseData> InvalidPublisherRouteIds()
        {
            yield return new TestCaseData(new Variant((byte)0)).SetArgDisplayNames("zero-byte");
            yield return new TestCaseData(new Variant((ushort)0)).SetArgDisplayNames("zero-ushort");
            yield return new TestCaseData(new Variant(0u)).SetArgDisplayNames("zero-uint");
            yield return new TestCaseData(new Variant(0ul)).SetArgDisplayNames("zero-ulong");
            yield return new TestCaseData(new Variant(-1)).SetArgDisplayNames("signed-int");
            yield return new TestCaseData(new Variant(true)).SetArgDisplayNames("boolean");
            yield return new TestCaseData(new Variant(string.Empty)).SetArgDisplayNames("empty-string");
            yield return new TestCaseData(new Variant("\ud800")).SetArgDisplayNames("invalid-unicode");
        }

        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task UascPublisherRegistrationRejectsNativeContext(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            DataChannelException? failure = Assert.Throws<DataChannelException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:valid", new Variant(42u)));
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.Unsupported));
            Assert.That(pair.Acceptor.Channels.IsEmpty, Is.True);
        }

        [Test]
        public async Task UascPublisherRegistrationCannotOutliveClosedContext()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:first", new Variant(42u));
            await pair.Acceptor.InvalidateAsync(DataChannelResult.Closed).ConfigureAwait(false);
            DataChannelException? failure = Assert.Throws<DataChannelException>(() =>
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:second", new Variant(42u)));
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.Closed));
            route.Dispose();
            route.Dispose();
            Assert.That(await ReceivePublisherFloAsync(pair, PublisherRoutePayload(new Variant(42u)))
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Closed));
            Assert.That(pair.Acceptor.IsReady, Is.False);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc, false)]
        [TestCase(DataChannelCarrierKind.SharedUasc, true)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc, true)]
        public async Task UascPublisherRoutingPreservesUnregisteredOrdinaryAndDedicatedFlows(
            DataChannelCarrierKind kind,
            bool uadp)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = uadp ?
                PublisherRouteParameters("urn:publisher:single") : FlowLoopback.Parameters();
            if (!uadp)
            {
                parameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
                parameters.ContentType = "application/octet-stream";
            }
            FlowPair flow = await pair.OpenAsync(parameters).ConfigureAwait(false);
            ByteString payload = PublisherRoutePayload(new Variant(42u));
            using IDisposable? unrelatedRoute = uadp ? null :
                pair.Acceptor.RegisterUascPublisherRoute("urn:publisher:unrelated", new Variant(42u));
            Assert.That(await ReceivePublisherFloAsync(pair, payload).ConfigureAwait(false),
                Is.EqualTo(uadp ? DataChannelResult.Good : DataChannelResult.UnknownChannel));
            if (uadp)
            {
                await AssertPublisherMessageAsync(flow.Remote, payload, 1).ConfigureAwait(false);
            }
            else
            {
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            }
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UascPublisherDedicatedSingleFlowUsesConfiguredRoute(bool wildcard)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.DedicatedUasc);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:single"))
                .ConfigureAwait(false);
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:single", wildcard ? Variant.Null : new Variant(42u));
            ByteString payload = PublisherRoutePayload(new Variant(42u));
            Assert.That(await ReceivePublisherFloAsync(pair, payload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(flow.Remote, payload, 1).ConfigureAwait(false);
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UascPublisherRoutesIgnoreOpaqueFlowWithSameResource(bool opaqueFirst)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelParametersDataType opaqueParameters = FlowLoopback.Parameters();
            opaqueParameters.ResourceUri = "urn:publisher:shared-resource";
            opaqueParameters.Protocol = DataChannelProfiles.UadpOpaqueApplicationPayload;
            opaqueParameters.ContentType = "application/octet-stream";
            using IDisposable localStreamingRoute = pair.Initiator.RegisterUascPublisherRoute(
                opaqueParameters.ResourceUri, new Variant(43u), null, null, 3);
            using IDisposable remoteStreamingRoute = pair.Acceptor.RegisterUascPublisherRoute(
                opaqueParameters.ResourceUri, new Variant(43u), null, null, 3);
            FlowPair first = await pair.OpenAsync(opaqueFirst ? opaqueParameters :
                PublisherRouteParameters(opaqueParameters.ResourceUri)).ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(opaqueFirst ?
                PublisherRouteParameters(opaqueParameters.ResourceUri) : opaqueParameters).ConfigureAwait(false);
            FlowPair uadp = opaqueFirst ? second : first;
            FlowPair opaque = opaqueFirst ? first : second;
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute(
                opaqueParameters.ResourceUri, new Variant(42u), null, null, 0);
            ByteString payload = PublisherRoutePayload(new Variant(42u));
            Assert.That(await ReceivePublisherFloAsync(pair, payload, uadp.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(uadp.Remote, payload, 1).ConfigureAwait(false);
            Assert.That(opaque.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
            Assert.That(opaque.Remote.State, Is.EqualTo(DataChannelState.Open));
        }

        [Test]
        public async Task UascPublisherResourceMatchingIsOrdinal()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair upper = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:Resource"))
                .ConfigureAwait(false);
            FlowPair lower = await pair.OpenAsync(PublisherRouteParameters("urn:publisher:resource"))
                .ConfigureAwait(false);
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:publisher:resource", new Variant(42u));
            ByteString payload = PublisherRoutePayload(new Variant(42u));
            Assert.That(await ReceivePublisherFloAsync(pair, payload, lower.Remote).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await AssertPublisherMessageAsync(lower.Remote, payload, 1).ConfigureAwait(false);
            Assert.That(upper.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
        }

        private static DataChannelParametersDataType PublisherRouteParameters(string resourceUri)
        {
            DataChannelParametersDataType parameters = FlowLoopback.Parameters();
            parameters.ResourceUri = resourceUri;
            parameters.Protocol = DataChannelProfiles.UadpPayload;
            parameters.ContentType = "application/opcua+uadp";
            return parameters;
        }

        /// <summary>
        /// Builds a type-0 UADP message with the codec's typed PublisherId encoding.
        /// </summary>
        private static ByteString PublisherRoutePayload(Variant publisherId)
        {
            byte[] networkMessage = UadpApplicationPayloadHeaderCodec.Encode(
                publisherId, new ByteString(new byte[] { 0x21, 0x43, 0x65, 0x87 }), 256).ToArray();
            networkMessage[2] = 0;
            return new ByteString(networkMessage);
        }

        private static ByteString UadpWriterPayload(ushort writerGroupId, params ushort[] dataSetWriterIds)
        {
            string writerIds = string.Concat(dataSetWriterIds.Select(static writerId =>
                $"{writerId & 0xFF:x2}{writerId >> 8:x2}"));
            return ByteString.FromHexString(
                $"f182002a00000001{writerGroupId & 0xFF:x2}{writerGroupId >> 8:x2}" +
                $"{dataSetWriterIds.Length:x2}{writerIds}dead");
        }

        private static ValueTask<DataChannelResult> ReceivePublisherFloAsync(
            FlowLoopback pair,
            ByteString payload,
            DataChannel? target = null)
        {
            var channels = pair.Acceptor.Channels;
            target ??= channels.IsEmpty ? null : channels[0];
            uint channelId = target?.ChannelId ?? 1;
            uint messageId = checked((uint)(target?.GetDiagnostics().MessagesReceived ?? 0) + 1);
            return ReceivePublisherFloAsync(pair.Acceptor, channelId, messageId, payload);
        }

        private static ValueTask<DataChannelResult> ReceivePublisherFloAsync(
            DataChannelContext receiver,
            ByteString payload,
            DataChannel target)
        {
            uint messageId = checked((uint)target.GetDiagnostics().MessagesReceived + 1);
            return ReceivePublisherFloAsync(receiver, target.ChannelId, messageId, payload);
        }

        private static ValueTask<DataChannelResult> ReceivePublisherFloAsync(
            DataChannelContext receiver,
            uint channelId,
            uint messageId,
            ByteString payload)
        {
            var record = new FlowRecord(FlowRecordKind.Data, 1, messageId, payload);
            FlowCarrier carrier = receiver.CarrierKind == DataChannelCarrierKind.DedicatedUasc
                ? FlowCarrier.DedicatedUasc
                : FlowCarrier.SharedUasc;
            ByteString body = UascFlowMessageCodec.Encode(carrier, channelId, in record, new(), out _);
            return receiver.ReceiveUascAsync(DataChannelUascMessageKind.Flo, (byte)'F', 0, body);
        }

        private static async Task AssertPublisherMessageAsync(DataChannel channel, ByteString payload, uint messageId)
        {
            Assert.That(channel.GetDiagnostics().MessagesReceived, Is.EqualTo(messageId));
            using DataChannelMessage? message = await channel.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message, Is.Not.Null);
                Assert.That(message!.MessageId, Is.EqualTo(messageId));
                Assert.That(message.Payload.ToArray(), Is.EqualTo(payload.ToArray()));
            });
        }
    }
}
