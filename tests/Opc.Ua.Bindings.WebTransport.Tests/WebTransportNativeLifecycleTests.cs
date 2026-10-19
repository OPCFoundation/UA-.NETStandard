// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    public sealed partial class WebTransportNativeFlowTests
    {
        /// <summary>
        /// A fully exchanged/acknowledged WT prefix remains reliable when the peer later sends STOP_SENDING.
        /// </summary>
        [Test]
        [Repeat(5)]
        public async Task AcknowledgedPrefixRemainsReliableAfterPeerStop()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            (DataChannel healthy, DataChannel healthySink) = await pair.OpenAsync(false).ConfigureAwait(false);
            Assert.That(await source.SendAsync(new ByteString(new byte[512])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using DataChannelMessage? message = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(message!.Payload.Length, Is.EqualTo(512));
            Assert.That(await sink.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? reply = await source.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(reply!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 1 }));
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            ulong streamId = FirstClientFlow(pair);
            Assert.That(m_environment.ClientBackend.Writes.Any(
                value => value.StreamId == streamId &&
                    value.Flags == 2 &&
                    value.Payload.Span.SequenceEqual(new byte[] { 0x40, 0x41, 0 })), Is.True);
            const ulong code = (uint)DataChannelResult.Closed;
            const ulong wire = 0x52e4a40fa8db + code + (code / 30);
            Task peerReset = sink.ResetAsync(DataChannelResult.Closed, deadline.Token).AsTask();
            for (int attempt = 0; attempt < 100 && m_environment.ClientBackend.Resets.IsEmpty; attempt++)
            {
                await Task.Delay(10, deadline.Token).ConfigureAwait(false);
            }
            Assert.That(m_environment.ClientBackend.Resets.Any(
                result => result.NativeStatus == NativeQuicStatus.Success), Is.True,
                $"Client: {pair.Client.Failure}; Server: {pair.Server.Failure}");
            NativeQuicCompletion actual = m_environment.ClientBackend.Resets.First(
                result => result.NativeStatus == NativeQuicStatus.Success);
            await peerReset.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(actual.Remote, Is.False);
                Assert.That(actual.ErrorCode, Is.EqualTo(wire));
                Assert.That(actual.ReliableSize, Is.GreaterThanOrEqualTo(3));
                Assert.That(actual.ResetFrameType, Is.EqualTo(0x24));
                Assert.That(pair.Client.Context.IsReady, Is.True, pair.Client.Failure?.ToString());
                Assert.That(pair.Server.Context.IsReady, Is.True, pair.Server.Failure?.ToString());
            });
            Assert.That(await healthy.SendAsync(new ByteString(new byte[] { 3 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await healthySink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 3 }));
        }

        /// <summary>
        /// Different crossing closes preserve actual first-error ownership instead of faulting the shared context.
        /// </summary>
        [Test]
        [Repeat(10)]
        public async Task CrossingFlowResetsPreserveFirstNativeErrorAndSession()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel peer) = await pair.OpenAsync().ConfigureAwait(false);
            (DataChannel healthy, DataChannel sink) = await pair.OpenAsync(false).ConfigureAwait(false);
            await Task.WhenAll(
                source.ResetAsync(DataChannelResult.Good).AsTask(),
                peer.ResetAsync(DataChannelResult.Closed).AsTask()).WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(await healthy.SendAsync(new ByteString(new byte[] { 13, 0, 14 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 13, 0, 14 }));
                Assert.That(pair.Client.Context.IsReady, Is.True, pair.Client.Failure?.ToString());
                Assert.That(pair.Server.Context.IsReady, Is.True, pair.Server.Failure?.ToString());
                Assert.That(m_environment.ClientBackend.Resets.Any(
                    result => result.NativeStatus == NativeQuicStatus.Success && !result.Remote), Is.True);
                Assert.That(m_environment.ServerBackend.Resets.Any(
                    result => result.NativeStatus == NativeQuicStatus.Success && !result.Remote), Is.True);
            });
        }

        /// <summary>
        /// A real RESET without a delivered STOP observation interrupts Core's full receive queue.
        /// The trace observer does not read, discard or manufacture the reliable WT prefix.
        /// </summary>
        [Test]
        [Repeat(10)]
        public async Task PeerResetWithoutStopInterruptsBackpressuredCoreReceive()
        {
            DataChannelOptions core = m_environment!.CoreOptions with { MaxContextMessages = 1 };
            await using WebTransportTestEnvironment environment = await WebTransportTestEnvironment.CreateAsync(
                m_backend!, m_library!, core, m_environment.Options).ConfigureAwait(false);
            environment.ServerBackend.BeforeOperation = async (_, operation, cancellationToken) =>
            {
                if (operation is NativeQuicStreamOperation.WaitStopped or NativeQuicStreamOperation.WaitPeerStopped)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
            };
            await using WebTransportPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(new ByteString(new byte[64])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await source.SendAsync(new ByteString(new byte[64])).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            ulong streamId = environment.ClientBackend.Streams.Values.Single(
                id => (id & 3) == 0 && id != 0 && id != pair.Client.ManagementStreamId);
            Assert.That(pair.Client.NativeConnection.TryGetStream(streamId, out NativeQuicStream? stream), Is.True);
            const ulong code = (uint)DataChannelResult.NotAuthorized;
            const ulong wire = 0x52e4a40fa8db + code + (code / 30);
            NativeQuicCompletion requested = await stream!.ResetSendAsync(wire).ConfigureAwait(false);
            Assert.That(requested.NativeStatus, Is.EqualTo(NativeQuicStatus.Success));
            for (int attempt = 0; attempt < 100 && sink.State == DataChannelState.Open; attempt++)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
            NativeQuicCompletion actual = environment.ServerBackend.ReceivedResets.First(
                result => result.NativeStatus == NativeQuicStatus.Success && result.StreamId == streamId);
            Assert.Multiple(() =>
            {
                Assert.That(actual.Remote, Is.True);
                Assert.That(actual.ReadOutcome, Is.EqualTo(2));
                Assert.That(actual.ErrorCode, Is.EqualTo(wire));
                Assert.That(actual.ResetFrameType, Is.EqualTo(0x24));
                Assert.That(actual.ReliableSize, Is.EqualTo(3));
                Assert.That(actual.Payload.IsEmpty, Is.True);
                Assert.That(actual.BytesTransferred, Is.Zero);
                Assert.That(environment.ServerBackend.PeerStops, Is.Empty);
                Assert.That(sink.State, Is.Not.EqualTo(DataChannelState.Open));
                Assert.That(pair.Client.Context.IsReady, Is.True, pair.Client.Failure?.ToString());
                Assert.That(pair.Server.Context.IsReady, Is.True, pair.Server.Failure?.ToString());
            });
        }

        /// <summary>
        /// Actual selected-key invalidation closes the authenticated context and all existing flows.
        /// </summary>
        [Test]
        public async Task CertificateRotationInvalidatesTheActualContext()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, _) = await pair.OpenAsync().ConfigureAwait(false);
            await m_environment.RotateClientCertificateAsync().ConfigureAwait(false);
            await Task.WhenAll(pair.Client.Completion, pair.Server.Completion).WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.NativeConnection.SecurityInvalidated, Is.True);
                Assert.That(pair.Client.Context.IsReady, Is.False);
                Assert.That(pair.Server.Context.IsReady, Is.False);
                Assert.That(pair.Client.GetResourceUsage(), Is.Default);
                Assert.That(pair.Server.GetResourceUsage(), Is.Default);
            });
            Assert.That(await source.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.Not.EqualTo(DataChannelResult.Good));
        }

        /// <summary>
        /// A full Core receive queue must not make a native flow reset wait forever behind WT session credit.
        /// </summary>
        [Test]
        public async Task ReceiveBackpressureDoesNotBlockFlowReset()
        {
            DataChannelOptions core = m_environment!.CoreOptions with { MaxContextMessages = 1 };
            WebTransportDataChannelOptions options = m_environment.Options with { InitialReceiveBytes = 257 };
            await using WebTransportTestEnvironment environment = await WebTransportTestEnvironment.CreateAsync(
                m_backend!, m_library!, core, options).ConfigureAwait(false);
            await using WebTransportPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            var sends = new List<Task<DataChannelResult>>();
            Task<DataChannelResult>? blocked = null;
            for (int index = 0; index < 8; index++)
            {
                Task<DataChannelResult> send = source.SendAsync(new ByteString(new byte[64])).AsTask();
                sends.Add(send);
                if (await Task.WhenAny(send, Task.Delay(250)).ConfigureAwait(false) != send)
                {
                    blocked = send;
                    break;
                }
                Assert.That(await send.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            }
            Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            Assert.That(blocked, Is.Not.Null);
            await source.ResetAsync(DataChannelResult.NotAuthorized).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.That(pair.Client.Context.IsReady, Is.True);
            Assert.That(pair.Server.Context.IsReady, Is.True);
        }

        /// <summary>
        /// Exhausting live native slots is a nonconsuming refusal, not loss of established flows or the session.
        /// </summary>
        [Test]
        public async Task NativeStreamLimitRefusesWithoutClosingContext()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            var streams = new List<ulong>();
            DataChannelException? refusal = null;
            int writes = 0;
            for (int count = 0; count < m_environment.Options.Native.MaxStreams; count++)
            {
                writes = m_environment.ClientBackend.Writes.Count(value => (value.Flags & 2) != 0);
                try
                {
                    streams.Add(await pair.Client.OpenStreamAsync().ConfigureAwait(false));
                }
                catch (DataChannelException exception) when (exception.Result == DataChannelResult.WouldBlock)
                {
                    refusal = exception;
                    break;
                }
            }
            Assert.Multiple(() =>
            {
                Assert.That(streams, Is.Not.Empty);
                Assert.That(refusal, Is.Not.Null);
                Assert.That(refusal!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
                Assert.That(m_environment.ClientBackend.Writes.Count(value => (value.Flags & 2) != 0),
                    Is.EqualTo(writes));
            });
            foreach (ulong stream in streams)
            {
                await pair.Client.CloseFlowAsync(stream, DataChannelResult.Closed).ConfigureAwait(false);
            }
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(new ByteString("I"u8.ToArray())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo("I"u8.ToArray()));
        }

        /// <summary>
        /// A complete zero-length opaque message is distinct from directional EOF and needs no empty native send.
        /// </summary>
        [Test]
        public async Task EmptyApplicationRecordIsNotEof()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(new ByteString(Array.Empty<byte>())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Payload.IsEmpty, Is.True);
            Assert.That(m_environment.ClientBackend.Writes.All(value => value.Payload.Length != 0), Is.True);
            await source.EndAsync().ConfigureAwait(false);
            using DataChannelMessage? end = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(end, Is.Null);
        }

        /// <summary>
        /// Repeated closed physical streams release provisional capacity while retaining anti-reuse tombstones.
        /// </summary>
        [Test]
        public async Task ClosedProvisionalStreamsReleaseCapacityWithoutReuse()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            uint baseline = (await m_backend!.GetCountsAsync().ConfigureAwait(false)).Streams;
            ulong previous = pair.Client.ManagementStreamId;
            for (int index = 0; index < m_environment.Options.MaxPendingStreams + 2; index++)
            {
                ulong stream = await pair.Client.OpenStreamAsync().ConfigureAwait(false);
                Assert.That(stream, Is.GreaterThan(previous));
                await pair.Client.CloseFlowAsync(stream, DataChannelResult.Closed).ConfigureAwait(false);
                Assert.That(await pair.Client.BindStreamAsync(stream, (uint)(101 + (2 * index)), true)
                    .ConfigureAwait(false), Is.False);
                for (int wait = 0; wait < 100; wait++)
                {
                    if ((await m_backend.GetCountsAsync().ConfigureAwait(false)).Streams == baseline)
                    {
                        break;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
                Assert.That((await m_backend.GetCountsAsync().ConfigureAwait(false)).Streams, Is.EqualTo(baseline));
                previous = stream;
            }
            Assert.That(pair.Client.Context.IsReady, Is.True);
            Assert.That(pair.Server.Context.IsReady, Is.True);
        }

        /// <summary>
        /// Ordinary Core RESET affects only its WT flow; another established flow and session remain usable.
        /// </summary>
        [Test]
        [Repeat(10)]
        public async Task ResetLeavesUnrelatedFlowsAndSessionAlive()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel closed, _) = await pair.OpenAsync().ConfigureAwait(false);
            (DataChannel surviving, DataChannel sink) = await pair.OpenAsync(false).ConfigureAwait(false);
            await closed.ResetAsync(DataChannelResult.NotAuthorized).ConfigureAwait(false);
            Assert.That(await surviving.SendAsync(new ByteString(new byte[] { 7, 0, 8 })).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 7, 0, 8 }));
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
                Assert.That(pair.Client.NativeConnection.IsClosed, Is.False);
                Assert.That(pair.Server.NativeConnection.IsClosed, Is.False);
            });
        }

        /// <summary>
        /// Caller cancellation before admission emits no flow bytes.
        /// </summary>
        [Test]
        public async Task CancelledWriteNeverCommitsPayload()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, _) = await pair.OpenAsync().ConfigureAwait(false);
            ulong stream = FirstClientFlow(pair);
            int writes = m_environment.ClientBackend.Writes.Count(value => value.StreamId == stream);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(() =>
                source.SendAsync(
                    new ByteString("34"u8.ToArray()), cancellationToken: cancellation.Token).AsTask())
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(m_environment.ClientBackend.Writes.Count(value => value.StreamId == stream),
                    Is.EqualTo(writes));
                Assert.That(source.GetDiagnostics().MessagesSent, Is.Zero);
                Assert.That(pair.Client.Context.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Cancels at actual native submission, after Core TryCommit; the complete payload still reaches the peer.
        /// </summary>
        [Test]
        public async Task CancellationAfterCommitDoesNotRetractOwnedRecord()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            ulong stream = FirstClientFlow(pair);
            using var cancellation = new CancellationTokenSource();
            int committed = 0;
            m_environment.ClientBackend.Sending = (id, _) =>
            {
                if (id == stream && Interlocked.Exchange(ref committed, 1) == 0)
                {
                    cancellation.Cancel();
                }
            };
            byte[] payload = [81, 82, 0, 83, 255];
            await Assert.CatchAsync<OperationCanceledException>(() =>
                source.SendAsync(new ByteString(payload), cancellationToken: cancellation.Token).AsTask())
                .ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(committed, Is.EqualTo(1));
                Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(payload));
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Small real WT credit and H3 output capacity cause ordered segmentation, not record loss or duplication.
        /// </summary>
        [Test]
        public async Task SegmentationAndBackpressurePreserveBytes()
        {
            WebTransportDataChannelOptions options = m_environment!.Options with
            {
                InitialReceiveBytes = 257,
                Http3OutputBytes = 4096,
                Http3OutputEvents = 528
            };
            await using WebTransportPair pair = await m_environment.ConnectPairAsync(options, options)
                .ConfigureAwait(false);
            DataChannelParametersDataType parameters = WebTransportTestEnvironment.Parameters();
            parameters.MaxMessageSize = 3072;
            (DataChannel source, DataChannel sink) = await pair.OpenAsync(parameters: parameters).ConfigureAwait(false);
            byte[] payload = [.. Enumerable.Range(0, 3072).Select(value => (byte)(value % 251))];
            Assert.That(await source.SendAsync(new ByteString(payload)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(payload));
            ulong stream = FirstClientFlow(pair);
            RecordedStreamWrite[] sends = [.. m_environment.ClientBackend.Writes.Where(
                value => value.StreamId == stream && value.Flags == 0)];
            Assert.Multiple(() =>
            {
                Assert.That(sends, Has.Length.GreaterThan(12));
                Assert.That(sends.All(value => value.Payload.Length <= 257), Is.True);
                Assert.That(source.GetDiagnostics().MessagesSent, Is.EqualTo(1));
                Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(pair.Server.GetResourceUsage().HostBufferedBytes,
                    Is.LessThanOrEqualTo(options.MaxHostBufferedBytes));
            });
        }

        /// <summary>
        /// Core directional end is observed only after the preceding DATA; the reverse direction remains open.
        /// </summary>
        [Test]
        public async Task DataPrecedesDirectionalEof()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(new ByteString(")"u8.ToArray())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await source.EndAsync().ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(")"u8.ToArray()));
            using DataChannelMessage? end = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(end, Is.Null);
            Assert.That(await sink.SendAsync(new ByteString("*"u8.ToArray())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? reverse = await source.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(reverse!.Payload.Span.ToArray(), Is.EqualTo("*"u8.ToArray()));
        }

        /// <summary>
        /// Closing a listener does not revoke contexts already accepted from it.
        /// </summary>
        [Test]
        public async Task ListenerDisposalPreservesAcceptedContext()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            await pair.Listener.DisposeAsync().ConfigureAwait(false);
            Assert.That(pair.Listener.IsListening, Is.False);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(new ByteString("["u8.ToArray())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo("["u8.ToArray()));
        }

        /// <summary>
        /// Actual transport loss wakes the peer and completes every host/native ownership lifetime.
        /// </summary>
        [Test]
        public async Task ContextLossReleasesAllOwnership()
        {
            await using WebTransportPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, _) = await pair.OpenAsync().ConfigureAwait(false);
            await pair.Client.NativeConnection.CloseAsync(55).ConfigureAwait(false);
            await Task.WhenAll(pair.Client.Completion, pair.Server.Completion).WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            NativeQuicCompletion remoteClose = m_environment.ServerBackend.Closures.Single(result => result.Remote);
            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.Context.IsReady, Is.False);
                Assert.That(pair.Server.Context.IsReady, Is.False);
                Assert.That(pair.Client.GetResourceUsage(), Is.Default);
                Assert.That(pair.Server.GetResourceUsage(), Is.Default);
                DataChannelResourceAssertions.AreReleased(m_environment.ClientBudget.Usage);
                DataChannelResourceAssertions.AreReleased(m_environment.ServerBudget.Usage);
                Assert.That(remoteClose.ErrorCode, Is.EqualTo(55));
            });
            Assert.That(await source.SendAsync(new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Is.Not.EqualTo(DataChannelResult.Good));
        }

        /// <summary>
        /// The reserved opaque/native lane is denied by default and works only with both explicit resource policies.
        /// </summary>
        [Test]
        public async Task ReservedOpaqueLaneRequiresExplicitAuthorization()
        {
            DataChannelParametersDataType parameters = WebTransportTestEnvironment.Parameters();
            parameters.ResourceUri = DataChannelProfiles.ClientServerResource;
            await using (WebTransportPair denied = await m_environment!.ConnectPairAsync().ConfigureAwait(false))
            {
                DataChannelException? failure = await Assert.ThrowsAsync<DataChannelException>(() =>
                    denied.Client.Context.OpenAsync(parameters).AsTask()).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                    Assert.That(denied.Client.Context.Channels, Is.Empty);
                    Assert.That(denied.Server.Context.Channels, Is.Empty);
                });
            }
            await using WebTransportPair allowed = await m_environment.ConnectPairAsync(
                clientFactory: m_environment.Factory(m_environment.ClientBudget, true),
                serverFactory: m_environment.Factory(m_environment.ServerBudget, true)).ConfigureAwait(false);
            Assert.That(allowed.Client.Context.Channels, Is.Empty);
            (DataChannel source, DataChannel sink) = await allowed.OpenAsync(parameters: parameters)
                .ConfigureAwait(false);
            byte[] opaque = [0x55, 0x41, 0, 0xff, 3, 4, 5];
            Assert.That(await source.SendAsync(new ByteString(opaque)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo(opaque));
        }

        /// <summary>
        /// A genuinely blocked native body is interrupted with the requested mapped code, not a fallback.
        /// H3 credit uses actual LOCAL final/reliable sizes after lease release; an unrelated flow survives.
        /// </summary>
        [Test]
        public async Task LateResetUsesActualLocalSizesAndMappedError()
        {
            DataChannelOptions core = m_environment!.CoreOptions with
            {
                MaxMessageSize = 262144,
                MaxReceiveBytes = 524288,
                MaxContextBytes = 4194304
            };
            WebTransportDataChannelOptions options = m_environment.Options with
            {
                MaxRecordSize = 1048576,
                InitialReceiveBytes = 1048576,
                Native = m_environment.Options.Native with { OperationTimeout = TimeSpan.FromSeconds(15) }
            };
            await using WebTransportTestEnvironment environment = await WebTransportTestEnvironment.CreateAsync(
                m_backend!, m_library!, core, options).ConfigureAwait(false);
            await using WebTransportPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = WebTransportTestEnvironment.Parameters();
            parameters.MaxMessageSize = 262144;
            parameters.MaxReceiveBytes = 524288;
            (DataChannel blocked, _) = await pair.OpenAsync(parameters: parameters).ConfigureAwait(false);
            (DataChannel healthy, DataChannel sink) = await pair.OpenAsync(false).ConfigureAwait(false);
            ulong stream = environment.ClientBackend.Streams.Values.Single(
                id => (id & 3) == 0 && id != 0 && id != pair.Client.ManagementStreamId);
            var readsReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int largeSends = 0;
            environment.ServerBackend.BeforeRead = async (id, cancellationToken) =>
            {
                if (id == stream)
                {
                    await readsReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            };
            environment.ClientBackend.Sending = (id, request) =>
            {
                if (id == stream && request.Payload.Length == 65536 && Interlocked.Increment(ref largeSends) == 2)
                {
                    secondSend.TrySetResult();
                }
            };
            Task<DataChannelResult> send = blocked.SendAsync(new ByteString(new byte[262144])).AsTask();
            try
            {
                await secondSend.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await Task.Delay(200).ConfigureAwait(false);
                Assert.That(send.IsCompleted, Is.False, "The real native stream must be blocked before reset.");
                await blocked.ResetAsync(DataChannelResult.NotAuthorized).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(await send.ConfigureAwait(false), Is.Not.EqualTo(DataChannelResult.Good));
                NativeQuicCompletion actual = environment.ClientBackend.Resets.Single(
                    result => result.NativeStatus == NativeQuicStatus.Success);
                const ulong applicationError = (uint)DataChannelResult.NotAuthorized;
                const ulong expectedWire = 0x52e4a40fa8db + applicationError + (applicationError / 30);
                NativeQuicCompletion partial = environment.ClientBackend.SendResults.Last(
                    result => result.NativeStatus != NativeQuicStatus.Success);
                Assert.Multiple(() =>
                {
                    Assert.That(actual.Remote, Is.False);
                    Assert.That(actual.ErrorCode, Is.EqualTo(expectedWire));
                    Assert.That(actual.ResetFrameType, Is.EqualTo(0x24));
                    Assert.That(actual.ReliableSize, Is.EqualTo(3));
                    Assert.That(actual.FinalSize, Is.GreaterThan(partial.BytesTransferred));
                    Assert.That(partial.ErrorCode, Is.EqualTo(expectedWire));
                    Assert.That(partial.CancellationRequested || partial.Remote, Is.True);
                    Assert.That(pair.Client.Context.IsReady, Is.True);
                    Assert.That(pair.Server.Context.IsReady, Is.True);
                });
            }
            finally
            {
                readsReleased.TrySetResult();
            }
            Assert.That(await healthy.SendAsync(new ByteString("Z"u8.ToArray())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(received!.Payload.Span.ToArray(), Is.EqualTo("Z"u8.ToArray()));
        }

        /// <summary>
        /// Finds the actual allocator result rather than assuming a managed stream-number sequence.
        /// </summary>
        private ulong FirstClientFlow(WebTransportPair pair)
        {
            return m_environment!.ClientBackend.Streams.Values.Single(
                id => (id & 3) == 0 && id != 0 && id != pair.Client.ManagementStreamId);
        }
    }
}
