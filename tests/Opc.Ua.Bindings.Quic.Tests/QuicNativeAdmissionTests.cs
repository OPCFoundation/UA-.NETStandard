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
        /// Application-owned messages keep receive admission charged, preserving order while native ingress waits.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task ApplicationBackpressurePreservesOrderedMessages()
        {
            await using NativeFlowTestEnvironment environment = await NativeFlowTestEnvironment.CreateAsync(
                m_backend!, m_environment!.Options with { MaxQueueMessages = 1 }).ConfigureAwait(false);
            await using NativeFlowPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(await source.SendAsync(Bytes(1), cancellationToken: timeout.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage first = await sink.ReadAsync(timeout.Token).ConfigureAwait(false) ??
                throw new InvalidOperationException("Missing initial payload.");
            Assert.That(await source.SendAsync(Bytes(2), cancellationToken: timeout.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Task<DataChannelMessage?> blocked = sink.ReadAsync(timeout.Token).AsTask();
            Assert.That(await pair.Client.Context.PingAsync(timeout.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            // The application budget also includes the asynchronously completing PONG write.
            while (environment.ServerBudget.Usage.Messages != 1 && !blocked.IsCompleted)
            {
                await Task.Delay(1, timeout.Token).ConfigureAwait(false);
            }
            Assert.Multiple(() =>
            {
                Assert.That(blocked.IsCompleted, Is.False);
                Assert.That(environment.ServerBudget.Usage.Messages, Is.EqualTo(1));
                Assert.That(first.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 1 }));
            });
            first.Dispose();
            using DataChannelMessage? second = await blocked.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(second!.Payload.Span.ToArray(), Is.EqualTo(new byte[] { 2 }));
                Assert.That(second.MessageId, Is.EqualTo(2));
                Assert.That(environment.ServerBudget.Usage.Messages, Is.EqualTo(1));
            });
            second!.Dispose();
            await pair.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                DataChannelResourceAssertions.AreReleased(environment.ClientBudget.Usage);
                DataChannelResourceAssertions.AreReleased(environment.ServerBudget.Usage);
            });
        }

        /// <summary>
        /// Header-sized native reads split HELLO/OPEN/READY and DATA across real awaited ingress calls.
        /// </summary>
        [Test]
        public async Task SegmentedNativeIngressUsesOnlyCommonRecordFraming()
        {
            QuicDataChannelOptions binding = m_environment!.BindingOptions with
            {
                Native = m_environment.BindingOptions.Native with { ReadSize = 13 }
            };
            await using NativeFlowTestEnvironment environment = await NativeFlowTestEnvironment.CreateAsync(
                m_backend!, bindingOptions: binding).ConfigureAwait(false);
            await using NativeFlowPair pair = await environment.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            byte[] payload = new byte[255];
            for (int index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)index;
            }
            Task<DataChannelResult> sent = source.SendAsync(new ByteString(payload)).AsTask();
            payload.AsSpan().Fill(0xff);
            Assert.That(await sent.ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? received = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(received, Is.Not.Null);
            for (int index = 0; index < received!.Length; index++)
            {
                Assert.That(received.Payload.Span[index], Is.EqualTo((byte)index));
            }
            Assert.That(received.Length, Is.EqualTo(255));
        }

        /// <summary>
        /// A CONTROL body cannot redirect a bound stream to a different common flow.
        /// </summary>
        [Test]
        public async Task MisdirectedControlResetsOnlyItsActualBoundStream()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel first, DataChannel firstPeer) = await pair.OpenAsync().ConfigureAwait(false);
            (DataChannel second, DataChannel secondPeer) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(pair.Client.NativeConnection.TryGetStream(4, out NativeQuicStream? stream), Is.True);
            var limits = new FlowCodecLimits();
            FlowRecord wrong = FlowControlCodec.Encode(new FlowSignal(FlowOperation.End, second.ChannelId), 1, 3, limits);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<DataChannelMessage?> reset = firstPeer.ReadAsync(timeout.Token).AsTask();
            Assert.That((await stream!.SendAsync(FlowRecordCodec.Encode(in wrong, limits)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            await Assert.ThrowsAsync<DataChannelException>(async () => await reset.ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(await second.SendAsync(Bytes(38)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? valid = await secondPeer.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(valid!.Payload.Span.ToArray(), Is.EqualTo("&"u8.ToArray()));
                Assert.That(firstPeer.ChannelId, Is.EqualTo(first.ChannelId));
                Assert.That(secondPeer.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Server.Context.IsReady, Is.True);
            });
        }

        /// <summary>
        /// Actual UDP ownership disappears after listeners and accepted connections are drained.
        /// </summary>
        [Test]
        public async Task ListenerPortCanBeReboundAfterConnectionsDrain()
        {
            NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            Uri endpoint = pair.Listener.EndpointUrl;
            Assert.That(endpoint.Port, Is.GreaterThan(0));
            await pair.DisposeAsync().ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
            await using QuicDataChannelListener replacement = await m_environment.CreateServer().ListenAsync(endpoint)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replacement.IsListening, Is.True);
                Assert.That(replacement.EndpointUrl, Is.EqualTo(endpoint));
            });
        }

        /// <summary>
        /// Rejected URL forms never reach native admission or fall back to a Service transport.
        /// </summary>
        [TestCase("https://localhost:4840")]
        [TestCase("opc.tcp://localhost:4840")]
        [TestCase("opc.quic://localhost:0")]
        [TestCase("opc.quic://localhost:4840/unselected")]
        [TestCase("opc.quic://localhost:4840?resource=other")]
        [TestCase("opc.quic://user@localhost:4840")]
        public async Task InvalidNativeEndpointIsRejectedBeforeNetworkAdmission(string endpoint)
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_environment!.CreateClient().ConnectAsync(new Uri(endpoint)).ConfigureAwait(false))
                .ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(m_backend!).ConfigureAwait(false);
        }

        /// <summary>
        /// Empty opaque DATA remains a complete owned message, distinct from directional EOF.
        /// </summary>
        [TestCase(DataChannelDeliveryMode.Ordered)]
        [TestCase(DataChannelDeliveryMode.Datagram)]
        public async Task EmptyOpaquePayloadIsNotEndOfStream(DataChannelDeliveryMode delivery)
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync(
                parameters: NativeFlowTestEnvironment.Parameters(delivery)).ConfigureAwait(false);
            Assert.That(await source.SendAsync(ByteString.Empty).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? empty = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(empty, Is.Not.Null);
                Assert.That(empty!.Length, Is.Zero);
                Assert.That(empty.Payload.IsEmpty, Is.True);
                Assert.That(empty.MessageId, Is.EqualTo(1));
            });
            Assert.That(await source.SendAsync(Bytes(7)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? next = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(next!.Payload, Is.EqualTo(Bytes(7)));
                Assert.That(next.MessageId, Is.EqualTo(2));
                Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Queue expiry refuses native admission without resetting the flow or reusing its retired DATA identifier.
        /// </summary>
        [Test]
        public async Task ExpiredNativeWriteDoesNotEmitBytesOrResetFlow()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            DataChannelParametersDataType parameters = NativeFlowTestEnvironment.Parameters();
            parameters.AllowQueueExpiry = true;
            (DataChannel source, DataChannel sink) = await pair.OpenAsync(parameters: parameters).ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(99), TimeSpan.Zero).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Timeout));
            Assert.That(await source.SendAsync(Bytes(8)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload, Is.EqualTo(Bytes(8)));
                Assert.That(message.MessageId, Is.EqualTo(2));
                Assert.That(sink.GetDiagnostics().MessagesReceived, Is.EqualTo(1));
                Assert.That(source.State, Is.EqualTo(DataChannelState.Open));
            });
        }

        /// <summary>
        /// IPv6 authorities retain actual hostname/certificate selection and native flow behavior without SNI fallback.
        /// </summary>
        [Test]
        public async Task Ipv6ListenerAuthenticatesAndCarriesNativeFlow()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync(
                listenEndpoint: new Uri("opc.quic://[::1]:0")).ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(await source.SendAsync(Bytes(6, 6)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload, Is.EqualTo(Bytes(6, 6)));
                Assert.That(pair.Listener.EndpointUrl.HostNameType, Is.EqualTo(UriHostNameType.IPv6));
                Assert.That(pair.Server.NativeConnection.Peer.LocalCertificateUsed, Is.True);
                Assert.That(pair.Client.NativeConnection.Peer.HandshakeConfirmed, Is.True);
            });
        }

        /// <summary>
        /// A late RESET for a retired/unknown flow is scoped even when it precedes another management record.
        /// </summary>
        [Test]
        public async Task UnknownFlowManagementResetDoesNotCloseContextOrOtherFlows()
        {
            await using NativeFlowPair pair = await m_environment!.ConnectPairAsync().ConfigureAwait(false);
            (DataChannel source, DataChannel sink) = await pair.OpenAsync().ConfigureAwait(false);
            Assert.That(pair.Client.NativeConnection.TryGetStream(0, out NativeQuicStream? management), Is.True);
            var limits = new FlowCodecLimits();
            FlowRecord reset = FlowControlCodec.Encode(
                new FlowOutcome(FlowOperation.Reset, 999, FlowWireStatus.Closed), 1, 1, limits);
            Assert.That((await management!.SendAsync(FlowRecordCodec.Encode(in reset, limits)).ConfigureAwait(false))!.Status,
                Is.EqualTo(NativeQuicStatus.Success));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(await pair.Client.Context.PingAsync(timeout.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await source.SendAsync(Bytes(23)).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await sink.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(message!.Payload, Is.EqualTo(Bytes(23)));
                Assert.That(pair.Client.Context.IsReady, Is.True);
                Assert.That(pair.Server.Context.IsReady, Is.True);
            });
        }
    }
}
