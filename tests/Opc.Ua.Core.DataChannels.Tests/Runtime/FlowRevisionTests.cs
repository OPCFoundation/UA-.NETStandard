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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    [TestFixture]
    [NonParallelizable]
    public sealed class FlowRevisionTests
    {
        [Test]
        [Repeat(20)]
        public async Task StaleUncommittedCreditCannotStrandConnectionCapacityAcrossRevisionBarrier()
        {
            var options = new DataChannelOptions
            {
                MaxMessageSize = 495,
                MaxReceiveBytes = 512,
                MaxContextBytes = 1024
            };
            await using var pair = new FlowLoopback(options: options);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                DataChannelDirection.AcceptorToOpener, maxMessage: 495, receiveBytes: 512)).ConfigureAwait(false);
            ByteString payload = new(new byte[495]);
            Assert.That(await flow.Remote.SendAsync(payload).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? first = await flow.Local.ReadAsync().ConfigureAwait(false))
            {
                Assert.That(first!.Length, Is.EqualTo(495));
            }
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorCarrier.Writes.Count(
                write => TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Credit) >= 2)
                .ConfigureAwait(false);
            pair.AcceptorCarrier.GateBefore = write => write.IsData;
            pair.AcceptorCarrier.GateAfterDelivery = write => write.IsData;
            Task<DataChannelResult> oldData = flow.Remote.SendAsync(payload).AsTask();
            await pair.AcceptorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.Priority = 1;
            Task<DataChannelResult> update = flow.Local.UpdateAsync(proposal, 1).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorCarrier.Writes.Any(
                write => TestFlowCarrier.Operation(write, pair.InitiatorCarrier.Kind) == FlowOperation.Commit))
                .ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write =>
            {
                FlowRecord record = TestFlowCarrier.Record(write, pair.InitiatorCarrier.Kind);
                return record.Kind == FlowRecordKind.Control &&
                    record.Revision == 1 &&
                    FlowControlCodec.Decode(in record, new FlowCodecLimits()) is FlowCredit { ChannelCredit: > 0 };
            };
            pair.AcceptorCarrier.BeforeRelease.TrySetResult(true);
            await pair.AcceptorCarrier.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            using (DataChannelMessage? second = await flow.Local.ReadAsync().ConfigureAwait(false))
            {
                Assert.That(second!.Revision, Is.EqualTo(1));
            }
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            pair.AcceptorCarrier.DeliveryRelease.TrySetResult(true);
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.DeliveredWrites.Any(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Commit))
                .ConfigureAwait(false);
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            Assert.That(await update.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await oldData.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await flow.Remote.SendAsync(ByteString.Empty).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? current = await flow.Local.ReadAsync().ConfigureAwait(false);
            Assert.That(current!.Revision, Is.EqualTo(2));
            Assert.That(current.MessageId, Is.EqualTo(3));
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc, DataChannelDeliveryMode.Ordered)]
        [TestCase(DataChannelCarrierKind.SharedUasc, DataChannelDeliveryMode.Ordered)]
        [TestCase(DataChannelCarrierKind.Quic, DataChannelDeliveryMode.Ordered)]
        [TestCase(DataChannelCarrierKind.Quic, DataChannelDeliveryMode.Datagram)]
        [TestCase(DataChannelCarrierKind.WebTransport, DataChannelDeliveryMode.Datagram)]
        public async Task DirectionalBarriersCommitLimitsWhilePreservingOwnedOldMessages(
            DataChannelCarrierKind kind,
            DataChannelDeliveryMode delivery)
        {
            await using var pair = new FlowLoopback(kind);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: delivery, rate: delivery == DataChannelDeliveryMode.Datagram ? 100000 : 0))
                .ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await flow.Remote.SendAsync(new ByteString(new byte[] { 2 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? oldAtAcceptor = await flow.Remote.ReadAsync().ConfigureAwait(false);
            using DataChannelMessage? oldAtOpener = await flow.Local.ReadAsync().ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.MaxMessageSize = 128;
            proposal.MaxReceiveBytes = 512;
            proposal.Priority = 7;
            Assert.That(await flow.Local.UpdateAsync(proposal, 1).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.Revision, Is.EqualTo(2));
                Assert.That(flow.Remote.Revision, Is.EqualTo(2));
                Assert.That(flow.Local.GetStatus().PendingRevision, Is.Zero);
                Assert.That(flow.Remote.Parameters.MaxMessageSize, Is.EqualTo(128));
                Assert.That(flow.Remote.Parameters.Priority, Is.EqualTo(7));
                Assert.That(oldAtAcceptor!.Revision, Is.EqualTo(1));
                Assert.That(oldAtAcceptor.Payload[0], Is.EqualTo(1));
                Assert.That(oldAtOpener!.Revision, Is.EqualTo(1));
                Assert.That(oldAtOpener.Payload[0], Is.EqualTo(2));
            });
            CapturedFlowWrite localCommit = pair.InitiatorCarrier.Writes.First(
                write => TestFlowCarrier.Operation(write, kind) == FlowOperation.Commit);
            CapturedFlowWrite remoteCommit = pair.AcceptorCarrier.Writes.First(
                write => TestFlowCarrier.Operation(write, kind) == FlowOperation.Commit);
            FlowRecord localRecord = TestFlowCarrier.Record(localCommit, kind);
            FlowRecord remoteRecord = TestFlowCarrier.Record(remoteCommit, kind);
            Assert.That(((FlowCommit)FlowControlCodec.Decode(in localRecord, new FlowCodecLimits())).LastOldMessageId,
                Is.EqualTo(1));
            Assert.That(((FlowCommit)FlowControlCodec.Decode(in remoteRecord, new FlowCodecLimits())).LastOldMessageId,
                Is.EqualTo(1));
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[129])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(await flow.Local.SendAsync(new ByteString(new byte[128])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? current = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(current!.Revision, Is.EqualTo(2));
            Assert.That(current.MessageId, Is.EqualTo(2));
        }

        [TestCase("resource")]
        [TestCase("protocol")]
        [TestCase("contentType")]
        [TestCase("direction")]
        [TestCase("delivery")]
        [TestCase("protection")]
        [TestCase("expiry")]
        public async Task EveryImmutableDescriptorFieldRequiresANewFlow(string field)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            switch (field)
            {
                case "resource":
                    proposal.ResourceUri = "urn:different";
                    break;
                case "protocol":
                    proposal.Protocol = DataChannelProfiles.JsonPayload;
                    break;
                case "contentType":
                    proposal.ContentType = "application/different";
                    break;
                case "direction":
                    proposal.Direction = DataChannelDirection.OpenerToAcceptor;
                    break;
                case "delivery":
                    proposal.Delivery = DataChannelDeliveryMode.Datagram;
                    break;
                case "protection":
                    proposal.Protection = DataChannelProtectionMode.Integrity;
                    break;
                case "expiry":
                    proposal.AllowQueueExpiry = true;
                    break;
            }
            Assert.That(await flow.Local.UpdateAsync(proposal, 1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(flow.Local.Revision, Is.EqualTo(1));
            Assert.That(flow.Local.GetStatus().PendingRevision, Is.Zero);
        }

        [Test]
        public async Task AcceptorCannotInitiateRevisionAndReturnedDescriptorCannotMutateRuntime()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            DataChannelParametersDataType copy = flow.Local.Parameters;
            copy.MaxMessageSize = 1;
            Assert.That(flow.Local.Parameters.MaxMessageSize, Is.EqualTo(256));
            Assert.That(await flow.Remote.UpdateAsync(copy, 1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(flow.Remote.Revision, Is.EqualTo(1));
        }

        [Test]
        public async Task OperationHistoryEvictsResultsButNeverReappliesRetiredIdentifiers()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            for (uint id = 1; id <= 65; id++)
            {
                DataChannelParametersDataType proposal = flow.Local.Parameters;
                proposal.Priority = (byte)id;
                Assert.That(await flow.Local.UpdateAsync(proposal, id).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            }
            Assert.That(flow.Local.Revision, Is.EqualTo(66));
            Assert.That(flow.Local.GetOperationStatus(1).Result, Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(flow.Local.GetOperationStatus(2).Result, Is.EqualTo(DataChannelResult.Good));
            Assert.That(await flow.Local.UpdateAsync(flow.Local.Parameters, 1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(flow.Local.Revision, Is.EqualTo(66));
        }

        [Test]
        public async Task SameProposalReplaysButReusingIdentifierForDifferentLimitsIsInvalid()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.MaxMessageSize = 128;
            Assert.That(await flow.Local.UpdateAsync(proposal, 7).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            int writes = pair.InitiatorCarrier.Writes.Count;
            Assert.That(await flow.Local.UpdateAsync(proposal, 7).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            proposal.MaxMessageSize = 127;
            Assert.That(await flow.Local.UpdateAsync(proposal, 7).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidArgument));
            Assert.That(pair.InitiatorCarrier.Writes, Has.Count.EqualTo(writes));
            Assert.That(flow.Remote.Parameters.MaxMessageSize, Is.EqualTo(128));
        }

        [Test]
        public async Task UpdateTimeoutBeforeCommitAbortsWithoutChangingTheAdmittedRevision()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.AcceptUpdate;
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.MaxMessageSize = 128;
            Task<DataChannelResult> update = flow.Local.UpdateAsync(proposal, 1).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.AcceptUpdate))
                .ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(9999));
            Assert.That(update.IsCompleted, Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await update.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(flow.Local.Revision, Is.EqualTo(1));
            Assert.That(flow.Local.Parameters.MaxMessageSize, Is.EqualTo(256));
            Assert.That(flow.Local.GetOperationStatus(1).Result, Is.EqualTo(DataChannelResult.Timeout));
        }

        [Test]
        public async Task CommittedUpdateTimeoutAndAbortNeverRollBackTheRevision()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Ack;
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.MaxMessageSize = 128;
            Task<DataChannelResult> update = flow.Local.UpdateAsync(proposal, 1).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.Ack))
                .ConfigureAwait(false);
            Assert.That(flow.Local.Revision, Is.EqualTo(2));
            Assert.That(await flow.Local.AbortUpdateAsync(1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            pair.Clock.Advance(TimeSpan.FromSeconds(10));
            Assert.That(await update.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(flow.Local.Revision, Is.EqualTo(2));
            pair.AcceptorCarrier.Drop = null;
            pair.Clock.Advance(TimeSpan.FromSeconds(10));
            await FlowLoopback.EventuallyAsync(() => flow.Local.GetStatus().PendingRevision == 0).ConfigureAwait(false);
            Assert.That(await flow.Local.UpdateAsync(proposal, 1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(flow.Local.Revision, Is.EqualTo(2));
        }

        [Test]
        public async Task DatagramShrinkWouldBlockLeavesAcceptedOperationAndOldQueueIntactForRetry()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, maxMessage: 64, receiveBytes: 256, rate: 100000))
                .ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write => write.IsData;
            Task<DataChannelResult> first = flow.Local.SendAsync(new ByteString(new byte[64])).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task<DataChannelResult> second = flow.Local.SendAsync(new ByteString(new byte[64])).AsTask();
            DataChannelParametersDataType smaller = flow.Local.Parameters;
            smaller.MaxMessageSize = 32;
            smaller.MaxReceiveBytes = 64;
            Assert.That(await flow.Local.UpdateAsync(smaller, 1).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.Revision, Is.EqualTo(1));
                Assert.That(flow.Local.Parameters.MaxMessageSize, Is.EqualTo(64));
                Assert.That(flow.Local.GetStatus().PendingRevision, Is.EqualTo(2));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
            });
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            using DataChannelMessage? oldFirst = await flow.Remote.ReadAsync().ConfigureAwait(false);
            using DataChannelMessage? oldSecond = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(oldFirst!.Revision, Is.EqualTo(1));
            Assert.That(oldSecond!.Revision, Is.EqualTo(1));
            Assert.That(await flow.Local.UpdateAsync(smaller, 1).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(flow.Local.Revision, Is.EqualTo(2));
        }

        [Test]
        public async Task DatagramPreviousRevisionHonorsLastOldIdAndExactGraceBeforeThirdRevision()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            pair.InitiatorCarrier.Drop = write => write.IsData;
            Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.Priority = 1;
            Assert.That(await flow.Local.UpdateAsync(proposal, 1).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(
                flow.Local.ChannelId, 1, 2, ByteString.Empty, true).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(
                flow.Local.ChannelId, 1, 1, ByteString.Empty, true).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? old = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(old!.Revision, Is.EqualTo(1));
            proposal.Priority = 2;
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1999));
            Assert.That(await flow.Local.UpdateAsync(proposal, 2).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.WouldBlock));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await pair.InitiatorCarrier.InjectDataAsync(
                flow.Local.ChannelId, 1, 1, ByteString.Empty, true).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await flow.Local.UpdateAsync(proposal, 2).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            Assert.That(flow.Local.Revision, Is.EqualTo(3));
            Assert.That(old.Payload.IsEmpty, Is.True);
        }

        [Test]
        public async Task ManagementCommitIsNotAnOrderedNativeBarrier()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            pair.AcceptorCarrier.Drop = write =>
                TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.AcceptUpdate;
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.Priority = 1;
            Task<DataChannelResult> pending = flow.Local.UpdateAsync(proposal, 1).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.AcceptorCarrier.Writes.Any(
                write => TestFlowCarrier.Operation(write, pair.AcceptorCarrier.Kind) == FlowOperation.AcceptUpdate))
                .ConfigureAwait(false);
            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                new FlowCommit(flow.Local.ChannelId, 0), 2, 1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(flow.Remote.Revision, Is.EqualTo(1));
            Assert.That(flow.Remote.GetStatus().PendingRevision, Is.EqualTo(2));
            Assert.That(await flow.Local.AbortUpdateAsync(1).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await pending.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Closed));
        }
    }
}
