// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Exercises authenticated association, readiness and actual ordered byte streams through the runtime.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowDirectOrderedRuntimeTests
    {
        /// <summary>
        /// All permitted opener-relative directions use token-bound streams, never FLO or additive credit.
        /// </summary>
        [TestCase(DataChannelDirection.Duplex, true)]
        [TestCase(DataChannelDirection.Duplex, false)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDirection.AcceptorToOpener, false)]
        public async Task AuthenticatedDirectionsUseActualStreamsAfterExactAckAndReady(
            DataChannelDirection direction, bool initiatorOpens)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(direction), initiatorOpens)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            if (direction != DataChannelDirection.AcceptorToOpener)
            {
                Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(received!.Payload, Is.EqualTo(payload));
            }
            if (direction != DataChannelDirection.OpenerToAcceptor)
            {
                Assert.That(await flow.Remote.SendAsync(payload, cancellationToken: deadline.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage? received = await flow.Local.ReadAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(received!.Payload, Is.EqualTo(payload));
            }
            CapturedFlowWrite[] baseline = [.. pair.InitiatorCarrier.Writes, .. pair.AcceptorCarrier.Writes];
            Assert.Multiple(() =>
            {
                Assert.That(baseline.Any(write => write.IsData), Is.False);
                Assert.That(baseline.Any(write =>
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Credit),
                    Is.False);
                Assert.That(baseline.Count(write =>
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Associate),
                    Is.EqualTo(direction == DataChannelDirection.Duplex ? 2 : 1));
                Assert.That(baseline.Count(write =>
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.AssociateAck),
                    Is.EqualTo(direction == DataChannelDirection.Duplex ? 2 : 1));
                Assert.That(pair.InitiatorCarrier.DirectStreams!.Writes,
                    Has.Count.EqualTo(direction == DataChannelDirection.Duplex ? 2 : 1));
            });
        }

        /// <summary>
        /// One spare DATA message slot suffices for decoding, delivering and releasing a complete frame.
        /// </summary>
        [Test]
        public async Task DirectReceiveTransfersItsOnlyAvailableMessageSlotToApplication()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxMessages = DataChannelApplicationBudget.ControlHeadroomMessages + 1
            });
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                initiatorBudget: budget, enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(DataChannelDirection.AcceptorToOpener))
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            for (int index = 0; index < 2; index++)
            {
                Assert.That(await flow.Remote.SendAsync(payload, cancellationToken: deadline.Token)
                    .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage? received = await flow.Local.ReadAsync(deadline.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(received!.Payload, Is.EqualTo(payload));
                    Assert.That(budget.Usage.Messages, Is.EqualTo(1));
                });
                if (index == 0)
                {
                    DataChannelParametersDataType revised = flow.Local.Parameters;
                    revised.MaxMessageSize = 128;
                    Assert.That(await flow.Local.UpdateAsync(revised, cancellationToken: deadline.Token)
                        .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
                    Assert.That(received!.Revision, Is.EqualTo(1u));
                }
            }
            await pair.DisposeAsync().ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(budget.Usage);
        }

        /// <summary>
        /// An uncommitted exact ACK prevents READY and all actual-stream writes.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task WithheldAssociationAcknowledgementPreventsReadinessAndPayload(bool initiatorOpens)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            TestFlowCarrier openerCarrier = initiatorOpens ? pair.InitiatorCarrier : pair.AcceptorCarrier;
            DataChannelContext opener = initiatorOpens ? pair.Initiator : pair.Acceptor;
            DataChannelContext acceptor = initiatorOpens ? pair.Acceptor : pair.Initiator;
            openerCarrier.GateBefore = write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.AssociateAck;
            Task<DataChannel> opening = opener.OpenAsync(
                FlowLoopback.Parameters(), cancellationToken: deadline.Token).AsTask();
            Task<DataChannel?> accepting = acceptor.AcceptAsync(deadline.Token).AsTask();
            await openerCarrier.BeforeEntered.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(accepting.IsCompleted, Is.False);
                Assert.That(pair.InitiatorCarrier.DirectStreams!.Writes, Is.Empty);
                Assert.That(openerCarrier.Writes.Any(write =>
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Ready), Is.False);
            });
            openerCarrier.BeforeRelease.TrySetResult(true);
            DataChannel local = await opening.WaitAsync(deadline.Token).ConfigureAwait(false);
            DataChannel? remote = await accepting.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(remote!.State, Is.EqualTo(DataChannelState.Open));
        }

        /// <summary>
        /// Exact association retries reuse the admitted streams, while changed fields cannot replace their owners.
        /// </summary>
        [Test]
        public async Task ExactAssociationReplayPreservesStreamsAndConflictingReplayIsRefused()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            CapturedFlowWrite original = pair.AcceptorCarrier.Writes.First(write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Associate);
            FlowRecord record = TestFlowCarrier.Record(original, DataChannelCarrierKind.QuicUasc);
            var association = (FlowAssociation)FlowControlCodec.Decode(in record, new());
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(association, record.Revision, record.MessageId)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            var acknowledgement = new FlowAssociation(
                FlowOperation.AssociateAck, association.Flow, association.Direction, association.Token);
            Assert.That(await pair.InitiatorCarrier.InjectControlAsync(
                acknowledgement, record.Revision, record.MessageId).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            var changed = new FlowAssociation(
                FlowOperation.Associate, association.Flow, (byte)(1 - association.Direction), association.Token);
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(changed, record.Revision, record.MessageId)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await pair.AcceptorCarrier.InjectControlAsync(
                association, record.Revision, record.MessageId + 10).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x12 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
        }

        /// <summary>
        /// Both immediate and queued secured ingress reject management-only END without ending actual DATA.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagementEndCannotSubstituteForActualStreamBarrier(bool queuedIngress)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            pair.InitiatorCarrier.UseQueuedUascIngress = queuedIngress;
            DataChannelResult injected = await pair.InitiatorCarrier.InjectControlAsync(
                new FlowSignal(FlowOperation.End, flow.Local.ChannelId), flow.Local.Revision,
                flow.Local.StateData.LastControlId + 1).ConfigureAwait(false);
            Assert.That(injected, Is.EqualTo(
                queuedIngress ? DataChannelResult.Good : DataChannelResult.InvalidState));
            Assert.That(await flow.Local.PingAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x12 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
            Assert.That(flow.Remote.State, Is.EqualTo(DataChannelState.Open));
        }

        /// <summary>
        /// A sending-only acceptor may deliver COMMIT before ACCEPT-UPDATE on independent transport paths.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ActualBarrierWaitsForSecuredAcceptanceWhenItOvertakesManagement(bool initiatorOpens)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(
                FlowLoopback.Parameters(DataChannelDirection.AcceptorToOpener), initiatorOpens)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            TestFlowCarrier acceptor = initiatorOpens ? pair.AcceptorCarrier : pair.InitiatorCarrier;
            acceptor.Drop = write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) is
                    FlowOperation.AcceptUpdate or FlowOperation.Ack;
            DataChannelParametersDataType revised = flow.Local.Parameters;
            revised.MaxMessageSize = 128;
            Task<DataChannelResult> updating = flow.Local.UpdateAsync(
                revised, cancellationToken: deadline.Token).AsTask();
            await FlowLoopback.EventuallyAsync(() => pair.InitiatorCarrier.DirectStreams!.Writes.Any(
                bytes => DirectDeliveryFrameCodec.DecodeExact(bytes, new()).Control is FlowCommit))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(updating.IsCompleted, Is.False);
                Assert.That(flow.Local.Revision, Is.EqualTo(1u));
            });
            CapturedFlowWrite acceptance = acceptor.Writes.First(write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.AcceptUpdate);
            await acceptor.DeliverAsync(acceptance).ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => acceptor.Writes.Any(write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Ack))
                .ConfigureAwait(false);
            CapturedFlowWrite acknowledgement = acceptor.Writes.First(write =>
                TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Ack);
            await acceptor.DeliverAsync(acknowledgement).ConfigureAwait(false);
            Assert.That(await updating.WaitAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(flow.Local.Revision, Is.EqualTo(2u));
        }

        /// <summary>
        /// A selected direct context cannot silently deliver an unsupported datagram flow over baseline FLO.
        /// </summary>
        [Test]
        public async Task SelectedDirectDatagramWithoutProviderIsExplicitlyUnsupported()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelException? rejected = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Initiator.OpenAsync(
                    FlowLoopback.Parameters(delivery: DataChannelDeliveryMode.Datagram, rate: 100000),
                    cancellationToken: deadline.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(rejected!.Result, Is.EqualTo(DataChannelResult.Unsupported));
            FlowPair ordered = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(ordered.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        /// <summary>
        /// Provider refusal completes the opening flow without escaping ingress or closing secured management.
        /// </summary>
        [TestCase(DataChannelDirection.Duplex)]
        [TestCase(DataChannelDirection.AcceptorToOpener)]
        public async Task FailedDirectReceiveAdmissionIsFlowScopedAndLaterAdmissionCanSucceed(
            DataChannelDirection direction)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            pair.InitiatorCarrier.DirectStreams!.ReceivingAdmissionResult = DataChannelResult.WouldBlock;
            DataChannelException? rejected = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Initiator.OpenAsync(FlowLoopback.Parameters(direction), cancellationToken: deadline.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
            pair.InitiatorCarrier.DirectStreams.ReceivingAdmissionResult = DataChannelResult.Good;
            FlowPair accepted = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(accepted.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(accepted.Remote.State, Is.EqualTo(DataChannelState.Open));
        }

        /// <summary>
        /// A bad actual-stream frame resets only its owner, not another admitted flow or secured management.
        /// </summary>
        [TestCase("oversize")]
        [TestCase("kind")]
        [TestCase("truncated")]
        [TestCase("foreign-flow")]
        public async Task InvalidActualStreamFrameIsIsolatedToItsOwner(string failure)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair damaged = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            FlowPair unaffected = await pair.OpenAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            Stream stream = await damaged.Local.StateData.DirectLane!.Sender.Task
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            ByteString invalid = failure switch
            {
                "oversize" => ByteString.FromHexString("0201000000"),
                "kind" => ByteString.FromHexString("01000000ff"),
                "truncated" => ByteString.FromHexString("080000000091"),
                _ => DirectDeliveryFrameCodec.EncodeBarrier(
                    new FlowSignal(FlowOperation.End, unaffected.Remote.ChannelId), 1, 99, new())
            };
            try
            {
                await stream.WriteAsync(invalid.Memory, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                Assert.That(damaged.Remote.State, Is.EqualTo(DataChannelState.Faulted),
                    "A scoped rejection may close the actual stream before the invalid write completes.");
            }
            if (failure == "truncated")
            {
                stream.Dispose();
            }
            await FlowLoopback.EventuallyAsync(() => damaged.Remote.State == DataChannelState.Faulted)
                .ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x12 }));
            Assert.That(await unaffected.Local.SendAsync(payload, cancellationToken: deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? received = await unaffected.Remote.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                Assert.That(received!.Payload, Is.EqualTo(payload));
            }
            Assert.Multiple(() =>
            {
                Assert.That(pair.Initiator.IsReady, Is.True);
                Assert.That(pair.Acceptor.IsReady, Is.True);
                Assert.That(unaffected.Remote.State, Is.EqualTo(DataChannelState.Open));
            });
            await pair.DisposeAsync().ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(pair.InitiatorBudget.Usage);
            DataChannelResourceAssertions.AreReleased(pair.AcceptorBudget.Usage);
        }

        /// <summary>
        /// Orderly sender release retains a locally accepted END until the transport makes it reliable.
        /// </summary>
        [TestCase(DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDirection.AcceptorToOpener, false)]
        [TestCase(DataChannelDirection.Duplex, true)]
        [TestCase(DataChannelDirection.Duplex, false)]
        public async Task OrderlyEndCompletesTransportRetentionBeforeSenderDisposal(
            DataChannelDirection direction, bool initiatorOpens)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.WebTransportUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            pair.InitiatorCarrier.DirectStreams!.DeferEndUntilCompletion = true;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(direction), initiatorOpens)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            DataChannel sender = direction == DataChannelDirection.AcceptorToOpener ? flow.Remote : flow.Local;
            DataChannel receiver = direction == DataChannelDirection.AcceptorToOpener ? flow.Local : flow.Remote;
            ByteString payload = FlowLoopback.UadpPayload(ByteString.FromHexString("00ff"));
            Assert.That(await sender.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? received = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                Assert.That(received!.Payload, Is.EqualTo(payload));
            }
            Assert.That(await sender.EndAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await receiver.ReadAsync(deadline.Token).ConfigureAwait(false), Is.Null);
            if (direction == DataChannelDirection.Duplex)
            {
                Assert.That(await receiver.EndAsync(deadline.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                Assert.That(await sender.ReadAsync(deadline.Token).ConfigureAwait(false), Is.Null);
            }
            await pair.DisposeAsync().ConfigureAwait(false);
            Assert.That(pair.InitiatorCarrier.DirectStreams.CompletedSenders,
                Is.EqualTo(direction == DataChannelDirection.Duplex ? 2 : 1));
            DataChannelResourceAssertions.AreReleased(pair.InitiatorBudget.Usage);
            DataChannelResourceAssertions.AreReleased(pair.AcceptorBudget.Usage);
        }

        /// <summary>
        /// Both COMMIT barriers follow old data on their actual streams and END follows the new data.
        /// </summary>
        [TestCase(DataChannelDirection.Duplex, true)]
        [TestCase(DataChannelDirection.Duplex, false)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDirection.AcceptorToOpener, false)]
        public async Task DirectionalCommitAndEndUseActualStreamsAndReleaseResources(
            DataChannelDirection direction, bool initiatorOpens)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(direction), initiatorOpens)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            DataChannel sender = direction == DataChannelDirection.AcceptorToOpener ? flow.Remote : flow.Local;
            DataChannel receiver = direction == DataChannelDirection.AcceptorToOpener ? flow.Local : flow.Remote;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            Assert.That(await sender.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? old = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false);
            DataChannelParametersDataType proposal = flow.Local.Parameters;
            proposal.MaxMessageSize = 128;
            proposal.MaxReceiveBytes = 512;
            Assert.That(await flow.Local.UpdateAsync(proposal, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.Revision, Is.EqualTo(2u));
                Assert.That(flow.Remote.Revision, Is.EqualTo(2u));
                Assert.That(old!.Revision, Is.EqualTo(1u));
                Assert.That(old.Payload, Is.EqualTo(payload));
            });
            Assert.That(await sender.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? current = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                Assert.That(current!.Revision, Is.EqualTo(2u));
                Assert.That(current.MessageId, Is.EqualTo(2u));
                Assert.That(current.Payload, Is.EqualTo(payload));
            }
            Assert.That(await sender.EndAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await receiver.ReadAsync(deadline.Token).ConfigureAwait(false), Is.Null);
            if (direction == DataChannelDirection.Duplex)
            {
                Assert.That(await receiver.EndAsync(deadline.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
            }
            DirectDeliveryFrame[] frames = [.. pair.InitiatorCarrier.DirectStreams!.Writes.Select(
                bytes => DirectDeliveryFrameCodec.DecodeExact(bytes, new()))];
            Assert.Multiple(() =>
            {
                int directions = direction == DataChannelDirection.Duplex ? 2 : 1;
                Assert.That(frames.Count(frame => frame.Control is FlowCommit), Is.EqualTo(directions));
                Assert.That(frames.Where(frame => frame.Control is FlowCommit)
                    .All(frame => ((FlowCommit)frame.Control!).LastOldMessageId == 0), Is.True);
                Assert.That(frames.Count(frame => frame.Control is FlowSignal { Operation: FlowOperation.End }),
                    Is.EqualTo(directions));
                Assert.That(frames[0].Kind, Is.EqualTo(DirectDeliveryFrameKind.Data));
                Assert.That(frames[^1].Control!.Operation, Is.EqualTo(FlowOperation.End));
            });
            old!.Dispose();
            await pair.DisposeAsync().ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(pair.InitiatorBudget.Usage);
            DataChannelResourceAssertions.AreReleased(pair.AcceptorBudget.Usage);
        }
    }
}
