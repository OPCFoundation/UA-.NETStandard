// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Exercises authenticated direct datagram admission independently of ordered streams and baseline FLO.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowDirectDatagramRuntimeTests
    {
        /// <summary>
        /// Every permitted direction preserves UADP, revision ownership and secured management progress.
        /// </summary>
        [TestCase(DataChannelDirection.Duplex, true)]
        [TestCase(DataChannelDirection.Duplex, false)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, true)]
        [TestCase(DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDirection.AcceptorToOpener, false)]
        public async Task AuthenticatedDatagramsUseTokensWithoutCreatingStreams(
            DataChannelDirection direction, bool initiatorOpens)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(direction,
                DataChannelDeliveryMode.Datagram, rate: 100000), initiatorOpens)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            DataChannel sender = direction == DataChannelDirection.AcceptorToOpener ? flow.Remote : flow.Local;
            DataChannel receiver = direction == DataChannelDirection.AcceptorToOpener ? flow.Local : flow.Remote;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1, 2, 3 }));
            Assert.That(await sender.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? old = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(old!.Payload, Is.EqualTo(payload));
            DataChannelParametersDataType revised = flow.Local.Parameters;
            revised.MaxMessageSize = 128;
            Assert.That(await flow.Local.UpdateAsync(revised, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await sender.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? current = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(old.Revision, Is.EqualTo(1u));
                    Assert.That(current!.Revision, Is.EqualTo(2u));
                    Assert.That(current.MessageId, Is.EqualTo(2u));
                    Assert.That(current.Payload, Is.EqualTo(payload));
                });
            }
            CapturedFlowWrite[] writes = [.. pair.InitiatorCarrier.Writes, .. pair.AcceptorCarrier.Writes];
            CapturedFlowWrite first = writes.First(write => write.IsData);
            Assert.Multiple(() =>
            {
                Assert.That(writes.All(write => !write.IsData || write.Path == DataChannelPath.Datagram),
                    Is.True);
                Assert.That(first.Payload.Length, Is.EqualTo(payload.Length + 24));
                Assert.That(first.Payload[24..], Is.EqualTo(payload));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(first.Payload.Span[16..]), Is.EqualTo(1u));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(first.Payload.Span[20..]), Is.EqualTo(1u));
                Assert.That(pair.InitiatorCarrier.DirectStreams!.Writes, Is.Empty);
                Assert.That(writes.Any(write => !write.IsData &&
                    TestFlowCarrier.Operation(write, DataChannelCarrierKind.QuicUasc) == FlowOperation.Credit),
                    Is.False);
            });
            old.Dispose();
            Assert.That(await sender.EndAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await receiver.ReadAsync(deadline.Token).ConfigureAwait(false), Is.Null);
            await pair.DisposeAsync().ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(pair.InitiatorBudget.Usage);
            DataChannelResourceAssertions.AreReleased(pair.AcceptorBudget.Usage);
        }

        /// <summary>
        /// Replayed and foreign-token datagrams do not become application messages or damage the context.
        /// </summary>
        [Test]
        public async Task ReplayAndForeignTokensAreDroppedBeforeApplicationAdmission()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? first = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                Assert.That(first!.MessageId, Is.EqualTo(1u));
            }
            CapturedFlowWrite original = pair.InitiatorCarrier.Writes.Single(write => write.IsData);
            await pair.InitiatorCarrier.DeliverAsync(original).ConfigureAwait(false);
            await FlowLoopback.EventuallyAsync(() => flow.Remote.StateData.ReplayDrops == 1).ConfigureAwait(false);
            ByteString foreign = DirectDatagramCodec.Encode(
                Guid.NewGuid(), 1, 2, payload, new FlowCodecLimits(), 2048);
            await pair.InitiatorCarrier.DeliverAsync(original with { Payload = foreign }).ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? next = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(next!.MessageId, Is.EqualTo(2u));
                Assert.That(next.Payload, Is.EqualTo(payload));
                Assert.That(flow.Remote.StateData.Incoming, Is.Empty);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// The 24-byte prefix is included at the exact path boundary, without allocating an ID on refusal.
        /// </summary>
        [Test]
        public async Task PathCapacityIncludesPrefixAndShrinkingMtuDoesNotCloseContext()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            pair.InitiatorCarrier.MaxDirectDatagramSize = 40;
            pair.AcceptorCarrier.MaxDirectDatagramSize = 40;
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            ByteString exact = FlowLoopback.UadpPayload(new ByteString(new byte[9]));
            ByteString larger = FlowLoopback.UadpPayload(new ByteString(new byte[10]));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.Parameters.MaxMessageSize, Is.EqualTo(16u));
                Assert.That(exact.Length, Is.EqualTo(16));
            });
            Assert.That(await flow.Local.SendAsync(larger, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.That(flow.Local.StateData.LastDataId, Is.Zero);
            Assert.That(await flow.Local.SendAsync(exact, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? first = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(first!.MessageId, Is.EqualTo(1u));
            pair.InitiatorCarrier.MaxDirectDatagramSize = 39;
            Assert.That(await flow.Local.SendAsync(exact, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.TooLarge));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.StateData.LastDataId, Is.EqualTo(1u));
                Assert.That(pair.Initiator.IsReady && pair.Acceptor.IsReady, Is.True);
                Assert.That(flow.Remote.StateData.ReceiveBytes, Is.EqualTo(40));
            });
        }

        /// <summary>
        /// One exact framed-message slot drops excess input and admits its replay only after lease release.
        /// </summary>
        [Test]
        public async Task FullQueueDropsWithoutWaitingOrConsumingReplaySequence()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            uint cost = (uint)payload.Length + 24;
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, maxMessage: (uint)payload.Length,
                receiveBytes: cost, rate: 100000)).ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? first = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            pair.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await FlowLoopback.EventuallyAsync(() => flow.Remote.GetDiagnostics().AdmissionDrops == 1)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(flow.Remote.StateData.ReceiveBytes, Is.EqualTo(cost));
                Assert.That(flow.Remote.StateData.ReceiveMessages, Is.EqualTo(1));
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(flow.Remote.GetDiagnostics().ReplayDrops, Is.Zero);
            });
            first!.Dispose();
            CapturedFlowWrite second = pair.InitiatorCarrier.Writes.Last(write => write.IsData);
            Assert.That(await pair.Acceptor.ReceiveDatagramAsync(second.Payload, deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? retained = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(retained!.MessageId, Is.EqualTo(2u));
                Assert.That(retained.Payload, Is.EqualTo(payload));
                Assert.That(flow.Remote.GetDiagnostics().BytesReceived, Is.EqualTo(((ulong)payload.Length + 24) * 2));
            });
        }

        /// <summary>
        /// Datagram rate uses prefix-inclusive bytes and refills at the exact monotonic boundary.
        /// </summary>
        [Test]
        public async Task RateBucketRefillsOnlyAfterFullFramedCost()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            uint cost = (uint)payload.Length + 24;
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, maxMessage: (uint)payload.Length,
                receiveBytes: cost, rate: cost)).ConfigureAwait(false);
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using (DataChannelMessage? first = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                Assert.That(first!.Payload, Is.EqualTo(payload));
            }
            Task<DataChannelResult> second = flow.Local.SendAsync(payload, cancellationToken: deadline.Token).AsTask();
            pair.Clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(second.IsCompleted, Is.False);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await second.WaitAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? current = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(current!.MessageId, Is.EqualTo(2u));
        }

        /// <summary>
        /// The final nonzero sequence is usable, but the next send requires a new flow admission.
        /// </summary>
        [Test]
        public async Task SequenceExhaustionCannotWrapOrReuseAssociation()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            flow.Local.StateData.LastDataId = uint.MaxValue - 1;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? last = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(last!.MessageId, Is.EqualTo(uint.MaxValue));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            Assert.Multiple(() =>
            {
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(pair.InitiatorCarrier.Writes.Count(write => write.IsData), Is.EqualTo(1));
                Assert.That(pair.Initiator.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Old-revision delivery honors the committed cutoff and the exact bounded grace interval.
        /// </summary>
        [Test]
        public async Task PreviousRevisionRequiresCutoffAndUnexpiredGrace()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            pair.InitiatorCarrier.Drop = write => write.IsData;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            CapturedFlowWrite withheld = pair.InitiatorCarrier.Writes.Single(write => write.IsData);
            Guid token = new(withheld.Payload.Span[..16].ToArray());
            DataChannelParametersDataType revised = flow.Local.Parameters;
            revised.Priority = 1;
            Assert.That(await flow.Local.UpdateAsync(revised, 1, deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            ByteString beyondCutoff = DirectDatagramCodec.Encode(token, 1, 2, payload, new(), 2048);
            Assert.That(await pair.Acceptor.ReceiveDatagramAsync(beyondCutoff, deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.InvalidState));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1999));
            Assert.That(await pair.Acceptor.ReceiveDatagramAsync(withheld.Payload, deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? old = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(old!.Revision, Is.EqualTo(1u));
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await pair.Acceptor.ReceiveDatagramAsync(withheld.Payload, deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(flow.Remote.GetDiagnostics().RevisionDrops, Is.EqualTo(2));
        }

        /// <summary>
        /// A committed path-size refusal is a datagram outcome, not a loss of the authenticated baseline.
        /// </summary>
        [TestCase(DataChannelResult.TooLarge)]
        [TestCase(DataChannelResult.WouldBlock)]
        public async Task CommittedDatagramRefusalDoesNotCloseFlowOrManagement(DataChannelResult refusal)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            pair.InitiatorCarrier.SendOutcome = write => write.IsData ? refusal : DataChannelResult.Good;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(refusal));
            pair.InitiatorCarrier.SendOutcome = null;
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(received!.MessageId, Is.EqualTo(2u));
                Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Initiator.IsReady && pair.Acceptor.IsReady, Is.True);
                Assert.That(flow.Local.GetDiagnostics().MessagesSent, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Invalid framing, revision, payload class and source cannot consume replay state or fault the context.
        /// </summary>
        [TestCase(0, DataChannelResult.InvalidArgument)]
        [TestCase(1, DataChannelResult.InvalidArgument)]
        [TestCase(2, DataChannelResult.InvalidState)]
        [TestCase(3, DataChannelResult.InvalidArgument)]
        [TestCase(4, DataChannelResult.InvalidArgument)]
        [TestCase(5, DataChannelResult.UnknownChannel)]
        [TestCase(6, DataChannelResult.UnknownChannel)]
        [TestCase(7, DataChannelResult.UnknownChannel)]
        public async Task InvalidDatagramsCannotAuthorizePayloadOrPoisonReplay(int variant, DataChannelResult expected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            pair.InitiatorCarrier.Drop = write => write.IsData;
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            CapturedFlowWrite original = pair.InitiatorCarrier.Writes.Single(write => write.IsData);
            byte[] invalid = original.Payload.ToArray();
            switch (variant)
            {
                case 0:
                    invalid = invalid.AsSpan(0, 23).ToArray();
                    break;
                case 1:
                    BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(20), 0);
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(16), 2);
                    break;
                case 3:
                    invalid.AsSpan(0, 16).Clear();
                    break;
                case 4:
                    invalid[26] = 0x0C;
                    break;
                case 5:
                    invalid[27] = 17;
                    break;
                case 6:
                    Guid.NewGuid().ToByteArray().CopyTo(invalid, 0);
                    break;
                case 7:
                    invalid[27] = 18;
                    break;
            }
            using IDisposable route = pair.Acceptor.RegisterUascPublisherRoute(
                flow.Remote.Parameters.ResourceUri ?? throw new InvalidOperationException("Missing admitted resource."),
                new Variant(42u));
            using IDisposable otherRoute = pair.Acceptor.RegisterUascPublisherRoute(
                "urn:runtime:other-resource", new Variant(17u));
            Assert.That(await pair.Acceptor.ReceiveDatagramAsync(new ByteString(invalid), deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(expected));
            Assert.That(await pair.Acceptor.ReceiveDatagramAsync(original.Payload, deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(received!.MessageId, Is.EqualTo(1u));
                Assert.That(received.Payload, Is.EqualTo(payload));
                Assert.That(flow.Remote.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(flow.Remote.GetDiagnostics().ReplayDrops, Is.Zero);
                Assert.That(pair.Acceptor.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Temporary receive registration refusal is flow-scoped and does not permanently disable the provider.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task FailedReceiveRegistrationCanRecoverWithoutReplacingContext(bool refuseAcceptor)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var failureLog = new ReceiveRegistrationFailureLog(deadline.Token);
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true,
                configureLogging: builder => builder.SetMinimumLevel(LogLevel.Warning).AddProvider(failureLog));
            TestFlowCarrier refused = refuseAcceptor ? pair.AcceptorCarrier : pair.InitiatorCarrier;
            refused.DirectDatagramReceiveResult = DataChannelResult.WouldBlock;
            await pair.StartAsync().ConfigureAwait(false);
            Task<DataChannel> opening = pair.Initiator.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000), cancellationToken: deadline.Token).AsTask();
            try
            {
                await failureLog.Entered.WaitAsync(deadline.Token).ConfigureAwait(false);
                Task settled = await Task.WhenAny(opening, Task.Delay(100, deadline.Token)).ConfigureAwait(false);
                Assert.That(settled, Is.Not.SameAs(opening),
                    "Registration failure must not escape before its receive worker has retired.");
            }
            finally
            {
                failureLog.Release();
            }
            DataChannelException failure = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await opening.ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(failure.Result, Is.EqualTo(DataChannelResult.Closed));
                Assert.That(pair.Initiator.IsReady && pair.Acceptor.IsReady, Is.True);
            });
            refused.DirectDatagramReceiveResult = DataChannelResult.Good;
            FlowPair recovered = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            Assert.That(await recovered.Local.SendAsync(payload, cancellationToken: deadline.Token)
                .ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await recovered.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload, Is.EqualTo(payload));
        }

        /// <summary>
        /// A queued datagram is retagged for the committed revision without changing its source bytes or sequence.
        /// </summary>
        [Test]
        public async Task QueuedDatagramRetainsTokenSequenceAndUadpAcrossRevision()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            pair.InitiatorCarrier.GateBefore = write => write.IsData;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 0x11, 0x22, 0x33 }));
            Task<DataChannelResult> first = flow.Local.SendAsync(payload, cancellationToken: deadline.Token).AsTask();
            await pair.InitiatorCarrier.BeforeEntered.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            Task<DataChannelResult> second = flow.Local.SendAsync(payload, cancellationToken: deadline.Token).AsTask();
            DataChannelParametersDataType revised = flow.Local.Parameters;
            revised.Priority = 1;
            Task<DataChannelResult> update = flow.Local.UpdateAsync(revised, cancellationToken: deadline.Token).AsTask();
            await FlowLoopback.EventuallyAsync(() => flow.Local.StateData.Update is { CommitQueued: true })
                .ConfigureAwait(false);
            Assert.That(second.IsCompleted, Is.False);
            pair.InitiatorCarrier.BeforeRelease.TrySetResult(true);
            Assert.That(await first.WaitAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await update.WaitAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await second.WaitAsync(deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? old = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            using DataChannelMessage? current = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            CapturedFlowWrite[] writes = pair.InitiatorCarrier.Writes.Where(write => write.IsData).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(old!.Revision, Is.EqualTo(1u));
                Assert.That(current!.Revision, Is.EqualTo(2u));
                Assert.That(current.MessageId, Is.EqualTo(2u));
                Assert.That(current.Payload, Is.EqualTo(payload));
                Assert.That(writes, Has.Length.EqualTo(2));
                Assert.That(writes[1].Payload[..16], Is.EqualTo(writes[0].Payload[..16]));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(writes[1].Payload.Span[16..]), Is.EqualTo(2u));
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(writes[1].Payload.Span[20..]), Is.EqualTo(2u));
                Assert.That(writes[1].Payload[24..], Is.EqualTo(payload));
            });
        }

        /// <summary>
        /// Novel reordering is bounded to 63 positions behind the high-water sequence, independently of UADP.
        /// </summary>
        [Test]
        public async Task DirectReplayWindowDistinguishesSixtyThreeAndSixtyFour()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var pair = new FlowLoopback(DataChannelCarrierKind.QuicUasc,
                enableDirectBinding: true, enableDirectStreams: true, enableDirectDatagrams: true);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(FlowLoopback.Parameters(
                delivery: DataChannelDeliveryMode.Datagram, rate: 100000)).ConfigureAwait(false);
            pair.InitiatorCarrier.Drop = write => write.IsData;
            ByteString payload = FlowLoopback.UadpPayload(new ByteString(new byte[] { 1 }));
            Assert.That(await flow.Local.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Guid token = new(pair.InitiatorCarrier.Writes.Single(write => write.IsData).Payload.Span[..16].ToArray());
            foreach ((uint sequence, DataChannelResult expected) in new[]
            {
                (65u, DataChannelResult.Good),
                (2u, DataChannelResult.Good),
                (2u, DataChannelResult.InvalidState),
                (1u, DataChannelResult.InvalidState)
            })
            {
                ByteString datagram = DirectDatagramCodec.Encode(token, 1, sequence, payload, new(), 2048);
                Assert.That(await pair.Acceptor.ReceiveDatagramAsync(datagram, deadline.Token).ConfigureAwait(false),
                    Is.EqualTo(expected));
            }
            using DataChannelMessage? high = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            using DataChannelMessage? reordered = await flow.Remote.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(high!.MessageId, Is.EqualTo(65u));
                Assert.That(reordered!.MessageId, Is.EqualTo(2u));
                Assert.That(flow.Remote.GetDiagnostics().ReplayDrops, Is.EqualTo(2));
                Assert.That(flow.Remote.StateData.Incoming, Is.Empty);
            });
        }

        /// <summary>
        /// Holds a failed receive worker after publishing its registration result.
        /// </summary>
        private sealed class ReceiveRegistrationFailureLog : ILoggerProvider, ILogger
        {
            /// <summary>
            /// Creates a bounded synchronous logging gate, not a blocked asynchronous provider.
            /// </summary>
            public ReceiveRegistrationFailureLog(CancellationToken cancellationToken)
            {
                m_cancellationToken = cancellationToken;
            }

            /// <summary>
            /// Gets the signal that the failed receive worker is still retiring.
            /// </summary>
            internal Task Entered => m_entered.Task;

            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName) => this;

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (exception is DataChannelException { Result: DataChannelResult.WouldBlock })
                {
                    m_entered.TrySetResult(true);
                    m_release.Wait(m_cancellationToken);
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    m_release.Dispose();
                }
            }

            /// <summary>
            /// Allows the receive worker to finish retiring.
            /// </summary>
            internal void Release() => m_release.Set();

            /// <summary>The finite bound on the logging gate.</summary>
            private readonly CancellationToken m_cancellationToken;

            /// <summary>The failed worker has reached its retirement log.</summary>
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The synchronous logger release, independent of asynchronous receive completion.</summary>
            private readonly ManualResetEventSlim m_release = new();

            /// <summary>Both the fixture and telemetry may dispose the shared provider.</summary>
            private int m_disposed;
        }
    }
}
