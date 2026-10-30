// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Quic.Tests
{
    public sealed partial class QuicNativeFlowTests
    {
        /// <summary>
        /// All semantic payload directions retain real bidirectional native lifecycle streams for either opener.
        /// </summary>
        [TestCase(true, DataChannelDirection.OpenerToAcceptor)]
        [TestCase(false, DataChannelDirection.OpenerToAcceptor)]
        [TestCase(true, DataChannelDirection.AcceptorToOpener)]
        [TestCase(false, DataChannelDirection.AcceptorToOpener)]
        [TestCase(true, DataChannelDirection.Duplex)]
        [TestCase(false, DataChannelDirection.Duplex)]
        public async Task EveryPayloadDirectionUsesActualBidirectionalStream(
            bool clientOpens,
            DataChannelDirection direction)
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel opener, DataChannel acceptor) = await pair.OpenAsync(
                clientOpens, NativeFlowTestEnvironment.Parameters(direction: direction)).ConfigureAwait(false);
            QuicDataChannelConnection connection = clientOpens ? pair.Client : pair.Server;
            Assert.That(connection.NativeConnection.TryGetStream(clientOpens ? 4UL : 1UL, out NativeQuicStream? stream),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(stream!.IsBidirectional, Is.True);
                Assert.That(stream.IsLocal, Is.True);
                Assert.That(opener.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(acceptor.State, Is.EqualTo(DataChannelState.Open));
            });
            DataChannel source = direction == DataChannelDirection.AcceptorToOpener ? acceptor : opener;
            DataChannel sink = direction == DataChannelDirection.AcceptorToOpener ? opener : acceptor;
            Assert.That(await source.SendAsync(Bytes(6)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 6 }));
            if (direction != DataChannelDirection.Duplex)
            {
                Assert.That(await sink.SendAsync(Bytes(9)).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.InvalidState));
            }
        }

        /// <summary>
        /// Equal numeric stream IDs from another connection are never accepted as an owned native handle.
        /// </summary>
        [Test]
        public async Task ForeignConnectionStreamCannotBeBound()
        {
            await using NativeFlowPair first = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await using NativeFlowPair second = await m_environment.ConnectPairAsync().ConfigureAwait(false);
            await using NativeQuicStream foreign = await second.Client.NativeConnection.OpenStreamAsync().ConfigureAwait(false);
            Assert.That(first.Client.NativeConnection.TryGetStream(foreign.StreamId, out _), Is.False);
            Assert.That(await first.Client.BindStreamAsync(foreign.StreamId, 101, true).ConfigureAwait(false), Is.False);
            (DataChannel source, DataChannel sink) = await first.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(53)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 53 }));
        }

        /// <summary>
        /// A real unidirectional stream cannot become either management or a flow association.
        /// </summary>
        [Test]
        public async Task UnidirectionalStreamIsRefusedWithoutClosingOtherFlows()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await using NativeQuicStream unidirectional = await pair.Client.NativeConnection.OpenStreamAsync(true)
                .ConfigureAwait(false);
            Assert.That(unidirectional.IsBidirectional, Is.False);
            Assert.That(await pair.Client.BindStreamAsync(unidirectional.StreamId, 101, true).ConfigureAwait(false), Is.False);
            NativeQuicCompletion? sent = await unidirectional.SendAsync(Bytes(1)).ConfigureAwait(false);
            Assert.That(sent!.Status, Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion stopped = await unidirectional.WaitStoppedAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stopped.Status, Is.EqualTo(NativeQuicStatus.Success));
                Assert.That(stopped.Remote, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
            });
            await pair.OpenAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// An actual common OPEN naming a different stream is rejected before ACCEPT/READY or DATA exposure.
        /// </summary>
        [Test]
        public async Task MismatchedOpenStreamIsRejectedBeforeApplicationData()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await using NativeQuicStream actual = await pair.Client.NativeConnection.OpenStreamAsync().ConfigureAwait(false);
            var limits = new FlowCodecLimits();
            var open = new FlowOpen(
                101, actual.StreamId + 4, FlowModel.ToWire(NativeFlowTestEnvironment.Parameters()),
                FlowWireAuthorization.Configured, null);
            FlowRecord record = FlowControlCodec.Encode(open, 1, 1, limits);
            NativeQuicCompletion? sent = await actual.SendAsync(FlowRecordCodec.Encode(in record, limits))
                .ConfigureAwait(false);
            Assert.That(sent!.Status, Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion refused = await actual.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(refused.ReadOutcome, Is.EqualTo(2));
                Assert.That(refused.Remote, Is.True);
                Assert.That(pair.Server.Context.Channels, Is.Empty);
                Assert.That(pair.Server.Context.IsReady, Is.True);
            });
            await pair.OpenAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Accepted stream prefixes with no complete OPEN have bounded lifetime and cannot consume permanent state.
        /// </summary>
        [Test]
        public async Task UnboundStreamsAreBoundedAndExpire()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await using NativeQuicStream stream = await pair.Client.NativeConnection.OpenStreamAsync().ConfigureAwait(false);
            NativeQuicCompletion? sent = await stream.SendAsync(Bytes(9)).ConfigureAwait(false);
            Assert.That(sent!.Status, Is.EqualTo(NativeQuicStatus.Success));
            NativeQuicCompletion read;
            do
            {
                read = await stream.ReadAsync().ConfigureAwait(false);
            }
            while (read.Status == NativeQuicStatus.Timeout);
            Assert.Multiple(() =>
            {
                Assert.That(read.ReadOutcome, Is.EqualTo(2));
                Assert.That(pair.Server.Context.Channels, Is.Empty);
                Assert.That(pair.Server.Context.IsReady, Is.True);
            });
            await pair.OpenAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Malformed common records reset their actual bound native flow rather than the management association.
        /// </summary>
        [Test]
        public async Task MalformedRecordResetsOnlyBoundFlow()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel invalid, DataChannel invalidPeer) = await pair.OpenAsync().ConfigureAwait(false);
            (DataChannel survivor, DataChannel survivorPeer) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(pair.Client.NativeConnection.TryGetStream(4, out NativeQuicStream? stream), Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<DataChannelMessage?> closed = invalidPeer.ReadAsync(timeout.Token).AsTask();
            Task<DataChannelMessage?> localClosed = invalid.ReadAsync(timeout.Token).AsTask();
            NativeQuicCompletion? sent = await stream!.SendAsync(Bytes(0, 0, 0, 0)).ConfigureAwait(false);
            Assert.That(sent!.Status, Is.EqualTo(NativeQuicStatus.Success));
            await Assert.ThrowsAsync<DataChannelException>(async () => await closed.ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<DataChannelException>(async () => await localClosed.ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(await survivor.SendAsync(Bytes(17)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await survivorPeer.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 17 }));
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
                Assert.That(invalid.State, Is.EqualTo(DataChannelState.Faulted));
            });
        }

        /// <summary>
        /// The carrier does not emit a refused write and the next real Core message retains its identity.
        /// </summary>
        [Test]
        public async Task CommitmentRefusalNeverAdmitsNativeBytes()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            FlowRecord record = new(FlowRecordKind.Data, 1, 1, Bytes(99));
            var write = new DataChannelWrite(
                DataChannelPath.Flow, source.ChannelId, 4, 0,
                FlowRecordCodec.Encode(in record, new FlowCodecLimits()), true, 1,
                () => DataChannelResult.Timeout);
            Assert.That(await pair.Client.SendAsync(write).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(write.IsCommitted, Is.False);
            Assert.That(await source.SendAsync(Bytes(5)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 5 }));
                Assert.That(message.MessageId, Is.EqualTo(1));
                Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Caller cancellation at the exact commit callback cannot retract a native record or release its bytes early.
        /// </summary>
        [Test]
        public async Task CommittedWriteDrainsBeforeReset()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            FlowRecord record = new(FlowRecordKind.Data, 1, 1, Bytes(41, 42));
            var write = new DataChannelWrite(
                DataChannelPath.Flow, source.ChannelId, 4, 0,
                FlowRecordCodec.Encode(in record, new FlowCodecLimits()), true, 1,
                () =>
                {
                    cancelled.Cancel();
                    return DataChannelResult.Good;
                });
            Assert.That(await pair.Client.SendAsync(write, cancelled.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(write.IsCommitted, Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 41, 42 }));
            await source.ResetAsync(DataChannelResult.Closed, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Each native connection gets fresh context/challenges and no previous flow or Session state.
        /// </summary>
        [Test]
        public async Task ReconnectCreatesFreshContextAndChallenges()
        {
            Guid context;
            ByteString challenge;
            await using (NativeFlowPair first = await m_environment!.ConnectPairAsync().ConfigureAwait(false))
            {
                context = first.Client.Context.Identity.ContextId;
                challenge = first.Client.Context.Identity.LocalChallenge;
            }
            await using NativeFlowPair second = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(second.Client.Context.Identity.ContextId, Is.Not.EqualTo(context));
                Assert.That(second.Client.Context.Identity.LocalChallenge, Is.Not.EqualTo(challenge));
                Assert.That(second.Client.Context.Channels, Is.Empty);
            });
            await second.OpenAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Peer RESET without STOP is observed while Core data admission backpressures the ordered native reader.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task PeerResetWithoutStopInterruptsBackpressuredIngressAndPreservesOtherFlow(bool clientOpens)
        {
            QuicDataChannelOptions binding = m_environment!.BindingOptions with
            {
                Native = m_environment.BindingOptions.Native with { OperationTimeout = TimeSpan.FromSeconds(10) }
            };
            await using NativeFlowTestEnvironment environment = await NativeFlowTestEnvironment.CreateAsync(
                m_backend!, m_environment.Options with { MaxQueueMessages = 1 }, binding).ConfigureAwait(false);
            await using NativeFlowPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync(clientOpens).ConfigureAwait(false);
            (DataChannel survivor, DataChannel survivorPeer) = await pair.OpenAsync(clientOpens).ConfigureAwait(false);
            QuicDataChannelConnection sender = clientOpens ? pair.Client : pair.Server;
            Assert.That(sender.NativeConnection.TryGetStream(clientOpens ? 4UL : 1UL, out NativeQuicStream? stream),
                Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(await source.SendAsync(Bytes(1)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? held = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(held!.Payload, Is.EqualTo(Bytes(1)));
            NativeQuicCounts before = await m_backend!.GetCountsAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(2)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            while ((await m_backend.GetCountsAsync(timeout.Token).ConfigureAwait(false)).Operations >= before.Operations)
            {
                await Task.Delay(1, timeout.Token).ConfigureAwait(false);
            }
            Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            Assert.That((await stream!.ResetSendAsync((ulong)DataChannelResult.InvalidState).ConfigureAwait(false)).Status,
                Is.EqualTo(NativeQuicStatus.Success));
            DataChannelException? reset = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await sink.ReadAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(reset!.Result, Is.EqualTo(DataChannelResult.InvalidState));
            Assert.That(await survivor.SendAsync(Bytes(31)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? other = await survivorPeer.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(other!.Payload, Is.EqualTo(Bytes(31)));
                Assert.That(held.Payload, Is.EqualTo(Bytes(1)));
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
            });
        }
    }
}
