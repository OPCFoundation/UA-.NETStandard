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
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    [TestFixture]
    [NonParallelizable]
    public sealed class FlowAdmissionTests
    {
        /// <summary>
        /// An uncredited DATA message is refused immediately without consuming the receive queue slot.
        /// </summary>
        [Test]
        public async Task BorrowedDedicatedIngressCannotBlockControlsWithoutADeadline()
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 1
            };
            await using var pair = new FlowLoopback(DataChannelCarrierKind.DedicatedUasc, options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            ByteString firstPayload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            await flow.Local.SendAsync(firstPayload).ConfigureAwait(false);
            using DataChannelMessage? retained = await flow.Remote.ReadAsync().ConfigureAwait(false);
            ByteString secondPayload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2 }));
            var record = new FlowRecord(FlowRecordKind.Data, 1, 2, secondPayload);
            ByteString payload = UascFlowMessageCodec.Encode(
                FlowCarrier.DedicatedUasc, flow.Remote.ChannelId, in record, new(), out _);

            Task<DataChannelResult> blocked = pair.Acceptor.ReceiveUascAsync(
                DataChannelUascMessageKind.Msg, (byte)'F', 0, payload).AsTask();
            Assert.That(blocked.IsCompleted, Is.True);

            Assert.That(await blocked.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            Assert.That(retained!.Payload.ToArray(), Is.EqualTo(firstPayload.ToArray()));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        /// <summary>
        /// Rejected or empty provisional streams relinquish parser storage without waiting for the OPEN timeout.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic, false)]
        [TestCase(DataChannelCarrierKind.Quic, true)]
        [TestCase(DataChannelCarrierKind.WebTransport, false)]
        [TestCase(DataChannelCarrierKind.WebTransport, true)]
        public async Task RejectedProvisionalStreamReleasesItsParserImmediately(
            DataChannelCarrierKind carrier, bool emptyEnd)
        {
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            ByteString payload = emptyEnd ? ByteString.Empty : ByteString.FromHexString("09000000000100000001000000");

            DataChannelResult result = await pair.Acceptor.ReceiveStreamAsync(4, payload, endOfStream: emptyEnd)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(emptyEnd ? DataChannelResult.Closed : DataChannelResult.InvalidState));
                Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.EqualTo(FlowStreamInput.ParserStorageBytes));
                Assert.That(pair.AcceptorBudget.Usage.Streams, Is.EqualTo(1));
                Assert.That(pair.AcceptorBudget.Usage.Messages, Is.Zero);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// A full receive queue resets its flow without blocking controls or stealing application-owned storage.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        public async Task UascQueueOverflowResetsWouldBlockWithoutBlockingControls(DataChannelCarrierKind carrier)
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768,
                MaxQueueMessages = 1
            };
            await using var pair = new FlowLoopback(carrier, options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            ByteString firstPayload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            Assert.That(await flow.Local.SendAsync(firstPayload).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? first = await flow.Remote.ReadAsync().ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorBudget.Usage.Messages == 1).ConfigureAwait(false);
            ByteString secondPayload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2 }));
            var second = new FlowRecord(FlowRecordKind.Data, 1, 2, secondPayload);
            ByteString payload = UascFlowMessageCodec.Encode(
                FlowModel.ToWire(carrier), flow.Remote.ChannelId, in second, new FlowCodecLimits(),
                out uint requestId);
            pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, payload);
            FlowRecord ping = FlowControlCodec.Encode(new FlowProbe(FlowOperation.Ping, 0, 42), 0, 99, new());
            ByteString probe = UascFlowMessageCodec.Encode(
                FlowModel.ToWire(carrier), 0, in ping, new FlowCodecLimits(), out requestId);
            pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, probe);
            await FlowLoopback.EventuallyAsync(() => flow.Remote.State == DataChannelState.Faulted)
                .ConfigureAwait(false);
            Assert.That(flow.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(first!.Payload.ToArray(), Is.EqualTo(firstPayload.ToArray()));
            Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(write =>
                TestFlowCarrier.Operation(write, carrier) == FlowOperation.Pong)).ConfigureAwait(false);
            Assert.That(pair.Acceptor.IsReady, Is.True);
            first!.Dispose();
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorBudget.Usage.Messages == 0).ConfigureAwait(false);
            Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.Zero);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        public async Task SynchronousUascIngressOwnsPlaintextBeforeTheReceiveBufferIsReturned(
            DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            pair.InitiatorCarrier.UseQueuedUascIngress = true;
            pair.AcceptorCarrier.UseQueuedUascIngress = true;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            ByteString expectedPayload = FlowLoopback.UadpPayload(
                new ByteString(new byte[] { 1, 12, 34 }));
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, expectedPayload);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowModel.ToWire(kind), flow.Remote.ChannelId, in record, new FlowCodecLimits(),
                out uint requestId);
            byte[] transportBuffer = encoded.ToArray();
            pair.Acceptor.QueueVerifiedUasc(
                DataChannelUascMessageKind.Msg,
                (byte)'F', requestId, new ByteString(transportBuffer));
            transportBuffer.AsSpan().Fill(0xDD);
            using DataChannelMessage? received = await flow.Remote.ReadAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(received!.Payload.ToArray(), Is.EqualTo(expectedPayload.ToArray()));
            Assert.That(received.MessageId, Is.EqualTo(1));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [Test]
        public async Task ScopedManagementRefusalDoesNotDiscardTheNextCoalescedControlRecord()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            var limits = new FlowCodecLimits();
            FlowRecord unknown = FlowControlCodec.Encode(
                new FlowOutcome(FlowOperation.Reset, 99, FlowWireStatus.Closed), 1, 100, limits);
            FlowRecord ping = FlowControlCodec.Encode(new FlowProbe(FlowOperation.Ping, 0, 12345), 0, 100, limits);
            ByteString first = FlowRecordCodec.Encode(in unknown, limits);
            ByteString second = FlowRecordCodec.Encode(in ping, limits);
            byte[] segment = new byte[first.Length + second.Length];
            first.Span.CopyTo(segment);
            second.Span.CopyTo(segment.AsSpan(first.Length));
            Assert.That(await pair.Acceptor.ReceiveStreamAsync(0, new ByteString(segment)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Pong))
                .ConfigureAwait(false);
            CapturedFlowWrite pong = pair.AcceptorCarrier.Writes.Single(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Pong);
            FlowRecord pongRecord = TestFlowCarrier.Record(pong, pair.AcceptorCarrier.Kind);
            Assert.That(((FlowProbe)FlowControlCodec.Decode(in pongRecord, limits)).Timestamp, Is.EqualTo(12345));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        public async Task ExactOversizedDataContainerIsFlowScoped(DataChannelCarrierKind kind)
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 2048,
                MaxReceiveBytes = 4096,
                MaxContextBytes = 32768
            };
            await using var pair = new FlowLoopback(kind, options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            byte[] oversized = new byte[1025];
            oversized[0] = 1;
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, new ByteString(oversized));
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowModel.ToWire(kind), flow.Remote.ChannelId, in record, new FlowCodecLimits(),
                out uint requestId);
            pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded);
            await FlowLoopback.EventuallyAsync(() => flow.Remote.State == DataChannelState.Faulted)
                .ConfigureAwait(false);
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Faulted));
            Assert.That(flow.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [Test]
        public async Task ResetControlThroughUascIngressTerminatesOnlyTheAddressedFlow()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair invalid = await pair.OpenAsync().ConfigureAwait(false);
            FlowPair unaffected = await pair.OpenAsync().ConfigureAwait(false);
            var limits = new FlowCodecLimits();
            FlowRecord record = FlowControlCodec.Encode(
                new FlowOutcome(FlowOperation.Reset, invalid.Local.ChannelId, FlowWireStatus.InvalidArgument),
                1, 100, limits);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, invalid.Local.ChannelId, in record, limits, out uint requestId);
            pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded);
            await FlowLoopback.EventuallyAsync(() => invalid.Remote.State == DataChannelState.Faulted)
                .ConfigureAwait(false);
            await invalid.Remote.StateData.Released.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(invalid.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(invalid.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(pair.AcceptorCarrier.Writes.Any(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Reset), Is.False);
            Assert.That(pair.Acceptor.IsReady, Is.True);
            Assert.That(unaffected.Remote.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(await unaffected.Local.SendAsync(
                FlowLoopback.UadpPayload(ByteString.Empty)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await unaffected.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.MessageId, Is.EqualTo(1));
        }

        /// <summary>
        /// A defined post-HELLO control with a recoverable scope is refused without losing admission.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MalformedCorrelatableControlReceivesInvalidArgument(bool trailingBytes)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            var limits = new FlowCodecLimits();
            FlowRecord record = FlowControlCodec.Encode(
                new FlowProbe(FlowOperation.Ping, flow.Local.ChannelId, 42), 1, 91, limits);
            ByteString valid = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, flow.Local.ChannelId, in record, limits, out uint requestId);
            byte[] malformed = trailingBytes
                ? valid.ToArray().Concat(new byte[] { 0 }).ToArray()
                : valid.ToArray().Take(valid.Length - 1).ToArray();
            pair.Acceptor.QueueVerifiedUasc(
                DataChannelUascMessageKind.Msg, (byte)'F', requestId, new ByteString(malformed));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Status))
                .ConfigureAwait(false);
            CapturedFlowWrite response = pair.AcceptorCarrier.Writes.Single(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Status);
            FlowRecord reply = TestFlowCarrier.Record(response, pair.AcceptorCarrier.Kind);
            var status = (FlowOutcome)FlowControlCodec.Decode(in reply, limits);
            Assert.Multiple(() =>
            {
                Assert.That(status.Status, Is.EqualTo(FlowWireStatus.InvalidArgument));
                Assert.That(status.Flow, Is.EqualTo(flow.Local.ChannelId));
                Assert.That(status.RelatedOperation, Is.EqualTo(91));
                Assert.That(reply.MessageId, Is.Not.Zero.And.Not.EqualTo(91));
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Malformed terminal outcomes never create outcome-response loops.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MalformedOutcomesWithRecoverableScopesAreDiscardedWithoutReply(bool reset)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            FlowRecord record = FlowControlCodec.Encode(
                new FlowOutcome(reset ? FlowOperation.Reset : FlowOperation.Status,
                    flow.Local.ChannelId, FlowWireStatus.Closed), 1, 91, new());
            ByteString valid = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, flow.Local.ChannelId, in record, new(), out uint requestId);
            byte[] malformed = valid.ToArray();
            malformed[15] = 255;
            pair.Acceptor.QueueVerifiedUasc(
                DataChannelUascMessageKind.Msg, (byte)'F', requestId, new ByteString(malformed));
            await pair.FlushAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.AcceptorCarrier.Writes.Any(write =>
                    TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) is
                        FlowOperation.Status or FlowOperation.Reset), Is.False);
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Credit refusal resets only the affected admission and returns the discarded aggregate charge.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UncreditedDataResetsWouldBlockAndRestoresConnectionCredit(bool queuedIngress)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            FlowPair unaffected = await pair.OpenAsync(
                FlowLoopback.Parameters(DataChannelDirection.AcceptorToOpener)).ConfigureAwait(false);
            await pair.FlushAsync().ConfigureAwait(false);
            flow.Remote.StateData.ReceiveCredit = 0;
            int before = pair.AcceptorCarrier.Writes.Count;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, payload);
            ByteString encoded = UascFlowMessageCodec.Encode(FlowCarrier.SharedUasc,
                flow.Local.ChannelId, in record, new(), out uint requestId);
            if (queuedIngress)
            {
                pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded);
            }
            else
            {
                Assert.That(await pair.Acceptor.ReceiveUascAsync(
                    DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.WouldBlock));
            }
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Skip(before).Any(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit))
                .ConfigureAwait(false);
            FlowRecord reset = TestFlowCarrier.Record(pair.AcceptorCarrier.Writes.Skip(before).Single(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Reset),
                pair.AcceptorCarrier.Kind);
            var outcome = (FlowOutcome)FlowControlCodec.Decode(in reset, new());
            FlowRecord credit = TestFlowCarrier.Record(pair.AcceptorCarrier.Writes.Skip(before).Single(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Credit),
                pair.AcceptorCarrier.Kind);
            var grant = (FlowCredit)FlowControlCodec.Decode(in credit, new());
            Assert.Multiple(() =>
            {
                Assert.That(outcome.Status, Is.EqualTo(FlowWireStatus.WouldBlock));
                Assert.That(outcome.RelatedOperation, Is.Zero);
                Assert.That(grant.Flow, Is.Zero);
                Assert.That(grant.ChannelCredit, Is.Zero);
                Assert.That(grant.ConnectionCredit, Is.EqualTo(payload.Length));
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(unaffected.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// A uniquely matched UADP admission retains its exact DATA failure scope in both receive paths.
        /// </summary>
        [TestCase("before-ready", false, DataChannelResult.InvalidState)]
        [TestCase("before-ready", true, DataChannelResult.InvalidState)]
        [TestCase("wrong-direction", false, DataChannelResult.NotAuthorized)]
        [TestCase("wrong-direction", true, DataChannelResult.NotAuthorized)]
        [TestCase("after-end", false, DataChannelResult.Closed)]
        [TestCase("after-end", true, DataChannelResult.Closed)]
        public async Task MatchedDataFailureUsesExactFlowReset(
            string scenario, bool queuedIngress, DataChannelResult expected)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            DataChannel receiver;
            Task<DataChannel>? opening = null;
            if (scenario == "before-ready")
            {
                pair.InitiatorCarrier.GateBefore = write =>
                    TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Ready;
                opening = pair.Initiator.OpenAsync(FlowLoopback.Parameters()).AsTask();
                await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                receiver = pair.Acceptor.Channels[0];
                Assert.That(receiver.State, Is.EqualTo(DataChannelState.Opening));
            }
            else
            {
                FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                    scenario == "wrong-direction"
                        ? DataChannelDirection.AcceptorToOpener : DataChannelDirection.Duplex))
                    .ConfigureAwait(false);
                receiver = flow.Remote;
                if (scenario == "after-end")
                {
                    Assert.That(await flow.Local.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                    await FlowLoopback.EventuallyAsync(() => receiver.StateData.RemoteEnded).ConfigureAwait(false);
                }
            }
            int before = pair.AcceptorCarrier.Writes.Count;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, payload);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, receiver.ChannelId, in record, new(), out uint requestId);
            if (queuedIngress)
            {
                pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded);
            }
            else
            {
                Assert.That(await pair.Acceptor.ReceiveUascAsync(
                    DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded).ConfigureAwait(false),
                    Is.EqualTo(expected));
            }
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Skip(before).Any(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Reset))
                .ConfigureAwait(false);
            FlowRecord reset = TestFlowCarrier.Record(pair.AcceptorCarrier.Writes.Skip(before).Single(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Reset),
                pair.AcceptorCarrier.Kind);
            var outcome = (FlowOutcome)FlowControlCodec.Decode(in reset, new());
            Assert.Multiple(() =>
            {
                Assert.That(outcome.Status, Is.EqualTo(FlowModel.ToWire(expected)));
                Assert.That(outcome.Flow, Is.EqualTo(receiver.ChannelId));
                Assert.That(outcome.RelatedOperation, Is.Zero);
                Assert.That(receiver.GetStatus().Result, Is.EqualTo(expected));
                Assert.That(receiver.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(pair.Acceptor.IsReady, Is.True);
                Assert.That(pair.AcceptorCarrier.Writes.Skip(before).Any(write =>
                    TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Status), Is.False);
            });
            if (opening != null)
            {
                pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
                DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await opening.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(exception!.Result, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// A routable UADP message with a missing or truncated DataSetMessage belongs to the PubSub reader.
        /// </summary>
        [TestCase("512A010700", false)]
        [TestCase("512A010700", true)]
        [TestCase("512A010700FF", false)]
        [TestCase("512A010700FF", true)]
        [TestCase("9000", false)]
        [TestCase("9000", true)]
        public async Task MatchedMalformedUadpIsDeliveredUnchanged(string hex, bool queuedIngress)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute(
                flow.Remote.Parameters.ResourceUri ?? throw new InvalidOperationException(), new Variant((byte)42));
            int before = pair.AcceptorCarrier.Writes.Count;
            ByteString payload = ByteString.FromHexString(hex);
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, payload);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, flow.Local.ChannelId, in record, new(), out uint requestId);
            if (queuedIngress)
            {
                pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded);
            }
            else
            {
                Assert.That(await pair.Acceptor.ReceiveUascAsync(
                    DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
            }
            using DataChannelMessage? message = await flow.Remote.ReadAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.ToArray(), Is.EqualTo(payload.ToArray()));
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
                Assert.That(pair.AcceptorCarrier.Writes.Skip(before).Any(write =>
                    TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) is
                        FlowOperation.Status or FlowOperation.Reset), Is.False);
            });
        }

        /// <summary>
        /// A malformed UADP header is not delivered when multiple admissions make its route ambiguous.
        /// </summary>
        [Test]
        public async Task AmbiguousMalformedUadpIsNotDeliveredToEitherAdmission()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair first = await pair.OpenAsync(FlowLoopback.Parameters()).ConfigureAwait(false);
            FlowPair second = await pair.OpenAsync(FlowLoopback.Parameters(
                receiveBytes: 2048)).ConfigureAwait(false);
            using IDisposable firstRoute = pair.Acceptor.RegisterUascPublisherRoute(
                first.Remote.Parameters.ResourceUri ?? throw new InvalidOperationException(), new Variant((byte)42));
            using IDisposable secondRoute = pair.Acceptor.RegisterUascPublisherRoute(
                second.Remote.Parameters.ResourceUri ?? throw new InvalidOperationException(), new Variant((byte)43));
            int before = pair.AcceptorCarrier.Writes.Count;
            ByteString payload = ByteString.FromHexString("9000");
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, payload);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, first.Local.ChannelId, in record, new(), out uint requestId);
            Assert.That(await pair.Acceptor.ReceiveUascAsync(
                DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Skip(before).Any(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Status))
                .ConfigureAwait(false);
            FlowRecord statusRecord = TestFlowCarrier.Record(pair.AcceptorCarrier.Writes.Skip(before).Single(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Status),
                pair.AcceptorCarrier.Kind);
            var status = (FlowOutcome)FlowControlCodec.Decode(in statusRecord, new());
            Assert.Multiple(() =>
            {
                Assert.That(status.Status, Is.EqualTo(FlowWireStatus.UnknownChannel));
                Assert.That(status.Flow, Is.Zero);
                Assert.That(first.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(second.Remote.GetDiagnostics().MessagesReceived, Is.Zero);
                Assert.That(first.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(second.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// OPEN refusals identify the received operation before and after provisional flow allocation.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task OpenRefusalCorrelatesTheReceivedControlOperation(bool refuseBeforeAllocation)
        {
            await using var pair = new FlowLoopback(options: new DataChannelOptions { MaxFlows = 1 });
            await pair.StartAsync().ConfigureAwait(false);
            uint channelId = 1;
            if (refuseBeforeAllocation)
            {
                await pair.OpenAsync().ConfigureAwait(false);
                channelId = 3;
            }
            else
            {
                pair.AcceptorPolicy.Handler = static (_, _) =>
                    new ValueTask<DataChannelAuthorization>(
                        new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }
            var limits = new FlowCodecLimits();
            var open = new FlowOpen(channelId, 0, FlowModel.ToWire(FlowLoopback.Parameters()),
                FlowWireAuthorization.Configured, null);
            FlowRecord record = FlowControlCodec.Encode(open, 1, 91, limits);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, channelId, in record, limits, out uint requestId);
            pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded);
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Reset))
                .ConfigureAwait(false);
            CapturedFlowWrite reset = pair.AcceptorCarrier.Writes.Single(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Reset);
            FlowRecord response = TestFlowCarrier.Record(reset, pair.AcceptorCarrier.Kind);
            var outcome = (FlowOutcome)FlowControlCodec.Decode(in response, limits);
            Assert.Multiple(() =>
            {
                Assert.That(outcome.Flow, Is.EqualTo(channelId));
                Assert.That(outcome.RelatedOperation, Is.EqualTo(91));
                Assert.That(outcome.Status, Is.EqualTo(refuseBeforeAllocation
                    ? FlowWireStatus.WouldBlock : FlowWireStatus.NotAuthorized));
                Assert.That(response.MessageId, Is.Not.Zero.And.Not.EqualTo(91));
                Assert.That(pair.Acceptor.IsReady, Is.True);
                Assert.That(pair.InitiatorCarrier.Writes.Any(write =>
                    TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Reset), Is.False);
            });
        }

        /// <summary>
        /// A native flow whose FIN exchange exceeds DrainTimeout closes its containing context.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task NativeFinDrainTimeoutClosesTheContainingContext(DataChannelCarrierKind carrier)
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));
            var options = new DataChannelOptions { DrainTimeout = TimeSpan.FromSeconds(5) };
            var pair = new FlowLoopback(carrier, options, clock: clock);
            pair.InitiatorCarrier.BlockFlowFinish = true;
            pair.AcceptorCarrier.BlockFlowFinish = true;
            bool expectedDrainTimeout = false;
            try
            {
                await pair.StartAsync().ConfigureAwait(false);
                FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);

                Assert.That(await flow.Local.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                Assert.That(await flow.Remote.ReadAsync().ConfigureAwait(false), Is.Null);
                Task<DataChannelResult> ending = flow.Remote.EndAsync().AsTask();
                await Task.WhenAll(
                    pair.InitiatorCarrier.FlowFinishEntered.Task,
                    pair.AcceptorCarrier.FlowFinishEntered.Task).WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                clock.Advance(options.DrainTimeout);
                await FlowLoopback.EventuallyAsync(() => !pair.Initiator.IsReady && !pair.Acceptor.IsReady)
                    .ConfigureAwait(false);
                expectedDrainTimeout = true;
                Assert.Multiple(() =>
                {
                    Assert.That(flow.Local.GetStatus().Result, Is.EqualTo(DataChannelResult.Timeout));
                    Assert.That(
                        flow.Remote.GetStatus().Result,
                        Is.EqualTo(DataChannelResult.Timeout),
                        $"initiatorReady={pair.Initiator.IsReady}, acceptorReady={pair.Acceptor.IsReady}, " +
                        $"local={flow.Local.GetStatus().State}/{flow.Local.GetStatus().Result}, " +
                        $"remote={flow.Remote.GetStatus().State}/{flow.Remote.GetStatus().Result}, " +
                        $"closeCalls={pair.InitiatorCarrier.CloseCalls}/{pair.AcceptorCarrier.CloseCalls}");
                });
                DataChannelException? endingFailure = null;
                try
                {
                    await ending.ConfigureAwait(false);
                }
                catch (DataChannelException exception)
                {
                    endingFailure = exception;
                }
                if (endingFailure != null)
                {
                    Assert.That(endingFailure.Result, Is.EqualTo(DataChannelResult.Timeout));
                }
            }
            finally
            {
                try
                {
                    await pair.DisposeAsync().ConfigureAwait(false);
                }
                catch (DataChannelException) when (expectedDrainTimeout)
                {
                    // The expected native release failure is surfaced by context disposal.
                }
            }
        }

        /// <summary>
        /// Native ordered END writes carry FIN and retire a flow without disturbing its context.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task NativeOrderedEndFinishesBothStreamDirections(DataChannelCarrierKind carrier)
        {
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);

            Assert.That(await flow.Local.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await flow.Remote.ReadAsync().ConfigureAwait(false), Is.Null);
            Assert.That(await flow.Remote.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));

            await FlowLoopback.EventuallyAsync(() =>
                pair.InitiatorCarrier.FinishedFlows == 1 && pair.AcceptorCarrier.FinishedFlows == 1)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.InitiatorCarrier.EndDirectionWrites, Is.EqualTo(1));
                Assert.That(pair.AcceptorCarrier.EndDirectionWrites, Is.EqualTo(1));
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// A received native RESET closes the flow without a reply, and both stream directions finish.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task NativeResetIsNotEchoedBeforeBothStreamDirectionsFinish(DataChannelCarrierKind carrier)
        {
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);

            await flow.Local.ResetAsync(DataChannelResult.NotAuthorized).ConfigureAwait(false);
            await flow.Remote.StateData.Released.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.GetStatus().State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(flow.Local.GetStatus().Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(flow.Remote.GetStatus().State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(flow.Remote.GetStatus().Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(pair.InitiatorCarrier.Writes.Count(
                    write => TestFlowCarrier.Operation(write, carrier) == FlowOperation.Reset), Is.EqualTo(1));
                Assert.That(pair.AcceptorCarrier.Writes.Count(
                    write => TestFlowCarrier.Operation(write, carrier) == FlowOperation.Reset), Is.Zero);
                Assert.That(pair.InitiatorCarrier.FinishedFlows, Is.EqualTo(1));
                Assert.That(pair.AcceptorCarrier.FinishedFlows, Is.EqualTo(1));
                Assert.That(pair.InitiatorCarrier.ClosedFlows, Is.Zero);
                Assert.That(pair.AcceptorCarrier.ClosedFlows, Is.Zero);
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task ActualCommonOpenAnnouncesNativeStreamBeforeAnyApplicationData(DataChannelCarrierKind kind)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            CapturedFlowWrite open = pair.InitiatorCarrier.Writes.Single(
                write => TestFlowCarrier.Operation(write, kind) == FlowOperation.Open);
            CapturedFlowWrite ready = pair.InitiatorCarrier.Writes.Single(
                write => TestFlowCarrier.Operation(write, kind) == FlowOperation.Ready);
            Assert.Multiple(() =>
            {
                Assert.That(open.Path, Is.EqualTo(DataChannelPath.Flow));
                Assert.That(open.StreamId, Is.Not.Zero);
                Assert.That(flow.Local.GetStatus().TransportStreamId, Is.Zero);
                Assert.That(flow.Remote.GetStatus().TransportStreamId, Is.Zero);
                Assert.That(ready.StreamId, Is.EqualTo(open.StreamId));
                Assert.That(ready.Path, Is.EqualTo(DataChannelPath.Flow));
                Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);
                Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.InitiatorBudget.Usage.Streams, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task NativeManagementReadyCannotAuthorizeAnUnverifiedDataPath()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Ready;
            Task<DataChannel> opening = pair.Initiator.OpenAsync(FlowLoopback.Parameters()).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            uint channel = pair.Acceptor.Channels[0].ChannelId;
            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                new FlowSignal(FlowOperation.Ready, channel), 1, 123).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(pair.Acceptor.Channels[0].State, Is.EqualTo(DataChannelState.Opening));
            Assert.That(opening.IsCompleted, Is.False);
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            DataChannel opened = await opening.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannel? accepted = await pair.Acceptor.AcceptAsync().ConfigureAwait(false);
            Assert.That(opened.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(accepted!.State, Is.EqualTo(DataChannelState.Open));
        }

        [Test]
        public async Task NativeOpenRejectsUnannouncedOrMismatchedActualStreamAssociations()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            var unannounced = new FlowOpen(1, 999, FlowModel.ToWire(FlowLoopback.Parameters()),
                FlowWireAuthorization.Configured, null);
            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(unannounced, 1, 1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(pair.Acceptor.Channels.IsEmpty, Is.True);
            ulong actual = await pair.InitiatorCarrier.OpenStreamAsync().ConfigureAwait(false);
            var mismatched = new FlowOpen(3, actual + 4, FlowModel.ToWire(FlowLoopback.Parameters()),
                FlowWireAuthorization.Configured, null);
            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                mismatched, 1, 1, DataChannelPath.Flow, actual).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.Multiple(() =>
            {
                Assert.That(pair.Acceptor.Channels.IsEmpty, Is.True);
                Assert.That(pair.AcceptorBudget.Usage.Streams, Is.EqualTo(1));
                Assert.That(pair.AcceptorBudget.Usage.Flows, Is.Zero);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IncompleteProvisionalStreamIsBoundedAndExpiresAtOpenTimeout(bool delayHelloCompletion)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic,
                options: new DataChannelOptions { KeepAliveInterval = TimeSpan.FromMinutes(1) });
            if (delayHelloCompletion)
            {
                pair.AcceptorCarrier.GateAfterDelivery = write =>
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.Quic) == FlowOperation.Hello;
            }
            await pair.StartAsync().ConfigureAwait(false);
            if (delayHelloCompletion)
            {
                await pair.AcceptorCarrier.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(pair.AcceptorCarrier.ActiveSends, Is.EqualTo(1));
                pair.AcceptorCarrier.DeliveryRelease.TrySetResult(true);
            }
            // HELLO readiness can precede release of its writer-owned buffers.
            await FlowLoopback.EventuallyAsync(() =>
                pair.AcceptorBudget.Usage.BufferedBytes == FlowStreamInput.ParserStorageBytes &&
                pair.AcceptorBudget.Usage.Messages == 0).ConfigureAwait(false);
            byte[] header = Header(100);
            Assert.That(await pair.Acceptor.ReceiveStreamAsync(4, new ByteString(header)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(pair.AcceptorBudget.Usage.Streams, Is.EqualTo(2));
                Assert.That(pair.AcceptorBudget.Usage.BufferedBytes,
                    Is.EqualTo((2 * FlowStreamInput.ParserStorageBytes) + (3 * 113)));
                Assert.That(pair.AcceptorBudget.Usage.Messages, Is.EqualTo(1));
                Assert.That(pair.Acceptor.Channels.IsEmpty, Is.True);
            });
            pair.Clock.Advance(TimeSpan.FromMilliseconds(9999));
            Assert.That(pair.AcceptorBudget.Usage.Streams, Is.EqualTo(2));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.EqualTo(FlowStreamInput.ParserStorageBytes));
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorBudget.Usage.Streams == 1).ConfigureAwait(false);
            Assert.That(await pair.Acceptor.ReceiveStreamAsync(4, new ByteString(header)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.UnknownChannel));
        }

        [Test]
        public async Task OversizedInitialStreamHeaderIsRefusedBeforeBodyAllocationAndPreservesContext()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            Assert.That(await pair.Acceptor.ReceiveStreamAsync(4, new ByteString(Header(4096))).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.EqualTo(FlowStreamInput.ParserStorageBytes));
            Assert.That(pair.AcceptorBudget.Usage.Streams, Is.EqualTo(1));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [Test]
        public async Task DeclaredStreamEofRejectsAnIncompleteRecordWithoutLeakingItsReservation()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            Assert.That(await pair.Acceptor.ReceiveStreamAsync(
                4, new ByteString(Header(100)), endOfStream: true).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(pair.AcceptorBudget.Usage.BufferedBytes, Is.EqualTo(FlowStreamInput.ParserStorageBytes));
            Assert.That(pair.AcceptorBudget.Usage.Streams, Is.EqualTo(1));
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnknownFlowDataHasBoundedErrorsAndNeverImplicitlyAllocatesAFlow(bool queuedIngress)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, ByteString.FromHexString("0100"));
            ByteString payload = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 99, in record, new(), out _);
            for (int i = 0; i < 10; i++)
            {
                if (queuedIngress)
                {
                    pair.Acceptor.QueueVerifiedUasc(DataChannelUascMessageKind.Msg, (byte)'F', 0, payload);
                }
                else
                {
                    Assert.That(await pair.Acceptor.ReceiveUascAsync(
                        DataChannelUascMessageKind.Msg, (byte)'F', 0, payload).ConfigureAwait(false),
                        Is.EqualTo(DataChannelResult.UnknownChannel));
                }
            }
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Status))
                .ConfigureAwait(false);
            CapturedFlowWrite[] refusals = pair.AcceptorCarrier.Writes.Where(write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Status).ToArray();
            Assert.That(refusals, Has.Length.EqualTo(1));
            FlowRecord refusal = TestFlowCarrier.Record(refusals[0], pair.AcceptorCarrier.Kind);
            var status = (FlowOutcome)FlowControlCodec.Decode(in refusal, new());
            Assert.Multiple(() =>
            {
                Assert.That(status.Status, Is.EqualTo(FlowWireStatus.UnknownChannel));
                Assert.That(status.Flow, Is.Zero);
                Assert.That(status.RelatedOperation, Is.Zero);
                Assert.That(pair.AcceptorCarrier.Writes.Any(write =>
                    TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Reset), Is.False);
            });
            Assert.That(pair.Acceptor.Channels.Count, Is.Zero);
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        /// <summary>
        /// Retired local and peer namespaces discard late controls without retaining per-flow reply state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredFlowControlsAreDiscardedWithoutReply(bool localReceiver)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            await flow.Local.ResetAsync(DataChannelResult.Closed).ConfigureAwait(false);
            await pair.FlushAsync().ConfigureAwait(false);
            DataChannelContext receiver = localReceiver ? pair.Initiator : pair.Acceptor;
            TestFlowCarrier carrier = localReceiver ? pair.InitiatorCarrier : pair.AcceptorCarrier;
            int before = carrier.Writes.Count;
            FlowControl[] controls =
            [
                new FlowProbe(FlowOperation.Ping, flow.Local.ChannelId, 42),
                new FlowCredit(flow.Local.ChannelId, 10, 10),
                new FlowPrepare(flow.Local.ChannelId, FlowModel.Limits(FlowLoopback.Parameters())),
                new FlowOpen(flow.Local.ChannelId, 0, FlowModel.ToWire(FlowLoopback.Parameters()),
                    FlowWireAuthorization.Configured, null)
            ];
            for (int i = 0; i < controls.Length; i++)
            {
                FlowRecord record = FlowControlCodec.Encode(controls[i], i == 2 ? 2u : 1u, (uint)i + 100, new());
                ByteString encoded = UascFlowMessageCodec.Encode(FlowCarrier.SharedUasc,
                    flow.Local.ChannelId, in record, new(), out uint requestId);
                Assert.That(await receiver.ReceiveUascAsync(
                    DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
            }
            await pair.FlushAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(carrier.Writes, Has.Count.EqualTo(before));
                Assert.That(receiver.Channels, Is.Empty);
                Assert.That(receiver.IsReady, Is.True);
            });
            FlowPair replacement = await pair.OpenAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replacement.Local.ChannelId, Is.GreaterThan(flow.Local.ChannelId));
                Assert.That(replacement.Remote.State, Is.EqualTo(DataChannelState.Open));
            });
        }

        [Test]
        public async Task PeerHelloIdentityMismatchClosesContextBeforeAuthorization()
        {
            await using var pair = new FlowLoopback();
            DataChannelPeerIdentity old = pair.AcceptorCarrier.Identity;
            pair.AcceptorCarrier.Identity = new DataChannelPeerIdentity(
                old.LocalApplicationUri, "urn:unexpected:peer",
                old.LocalKeyThumbprint, old.PeerKeyThumbprint, old.Protection, old.IsTransportInitiator);
            DataChannelException? failure = await Assert.ThrowsAsync<DataChannelException>(
                pair.StartAsync).ConfigureAwait(false);
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.SecurityFailure));
            Assert.That(pair.AcceptorPolicy.Requests, Is.Empty);
            Assert.That(pair.AcceptorBudget.Usage.Contexts, Is.Zero);
        }

        [Test]
        public async Task ActualCarrierDatagramCapacityIsARefusalNotAFragmentationHint()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            pair.InitiatorCarrier.MaxDatagramSize = 0;
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelException? unsupported = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Initiator.OpenAsync(FlowLoopback.Parameters(
                    delivery: DataChannelDeliveryMode.Datagram, rate: 1000)).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(unsupported!.Result, Is.EqualTo(DataChannelResult.Unsupported));
            pair.InitiatorCarrier.MaxDatagramSize = 128;
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, maxMessage: 256, rate: 1000)).ConfigureAwait(false);
            Assert.That(flow.Local.Parameters.MaxMessageSize, Is.EqualTo(111));
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[112])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[111])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.Length, Is.EqualTo(111));
        }

        [Test]
        public async Task ConfiguredPolicyDenialNeverPublishesAnAcceptedFlow()
        {
            await using var pair = new FlowLoopback();
            pair.AcceptorPolicy.Handler = (_, _) => new ValueTask<DataChannelAuthorization>(
                new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelException? failure = await Assert.ThrowsAsync<DataChannelException>(
                async () => await pair.Initiator.OpenAsync(FlowLoopback.Parameters()).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(pair.Acceptor.Channels.IsEmpty, Is.True);
            Assert.That(pair.AcceptorCarrier.Writes.Any(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Accept),
                Is.False);
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        private static byte[] Header(uint bodyLength)
        {
            byte[] header = new byte[13];
            BinaryPrimitives.WriteUInt32LittleEndian(header, bodyLength + 9);
            header[4] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(9), 1);
            return header;
        }
    }
}
