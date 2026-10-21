/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Moq;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using UadpDecoder = Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Verifies destination pinning through the actual receive and send loops without binding ports.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    [CancelAfter(10000)]
    public sealed class UdpPeerDestinationSecurityTests
    {
        private static readonly IPEndPoint s_peer = new(IPAddress.Parse("192.0.2.20"), 4840);
        private static readonly IPEndPoint s_attacker = new(IPAddress.Parse("192.0.2.99"), 4900);

        /// <summary>
        /// Even a datagram rejected by the downstream decoder cannot change future publication routing.
        /// </summary>
        [Test]
        public async Task SecurityR24RejectedInboundDatagramCannotRedirectPublicationAsync()
        {
            var io = new RecordingDatagramIo();
            await using UdpDatagramTransport transport = NewTransport(io);
            await transport.OpenAsync().ConfigureAwait(false);
            io.Enqueue([0xFF], s_attacker);
            await using IAsyncEnumerator<PubSubTransportFrame> received = transport.ReceiveAsync().GetAsyncEnumerator();
            Assert.That(await received.MoveNextAsync().ConfigureAwait(false), Is.True);
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()), new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low), TimeProvider.System);
            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(received.Current.Payload, context).ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
            await transport.SendAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
            Assert.That(io.Sent, Has.Count.EqualTo(1));
            Assert.That(io.Sent[0].Destination, Is.EqualTo(s_peer));
            Assert.That(io.Sent[0].Payload, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>
        /// A source-specific reply is permitted without changing the next general publication.
        /// </summary>
        [Test]
        public async Task SecurityR24AuthorizedRepliesDoNotChangeConfiguredPublicationPeerAsync()
        {
            var io = new RecordingDatagramIo();
            await using UdpDatagramTransport transport = NewTransport(io);
            await transport.OpenAsync().ConfigureAwait(false);
            await transport.SendToAsync(new byte[] { 42 }, s_attacker).ConfigureAwait(false);
            await transport.SendAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
            Assert.That(io.Sent, Has.Count.EqualTo(2));
            Assert.That(io.Sent[0].Destination, Is.EqualTo(s_attacker));
            Assert.That(io.Sent[0].Payload, Is.EqualTo(new byte[] { 42 }));
            Assert.That(io.Sent[1].Destination, Is.EqualTo(s_peer));
            Assert.That(io.Sent[1].Payload, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>
        /// Alternating sources, empty input, and explicit discovery sends leave the publication peer pinned.
        /// </summary>
        [Test]
        public async Task SecurityR24PeerPinningSurvivesAlternatingSourcesAndDiscoveryAsync()
        {
            var io = new RecordingDatagramIo();
            await using UdpDatagramTransport transport = NewTransport(io);
            await transport.OpenAsync().ConfigureAwait(false);
            io.Enqueue([], s_attacker);
            io.Enqueue([0xFF], s_attacker);
            io.Enqueue([0x01], s_peer);
            await using IAsyncEnumerator<PubSubTransportFrame> received = transport.ReceiveAsync().GetAsyncEnumerator();
            Assert.That(await received.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(received.Current.SourceEndpoint, Is.EqualTo(s_attacker));
            Assert.That(await received.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(received.Current.SourceEndpoint, Is.EqualTo(s_peer));
            await transport.SendDiscoveryAnnouncementAsync(new byte[] { 42 }).ConfigureAwait(false);
            await transport.SendAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
            Assert.That(io.Sent, Has.Count.EqualTo(2));
            Assert.That(io.Sent[0].Destination, Is.EqualTo(UdpDatagramTransport.StandardDiscoveryEndpoint));
            Assert.That(io.Sent[1].Destination, Is.EqualTo(s_peer));
        }

        /// <summary>
        /// Policy-denied sources and explicit reply destinations are evaluated before admission or send.
        /// </summary>
        [Test]
        public async Task SecurityR24PeerPinningSurvivesAlternatingSourcesAndPolicyDenialsAsync()
        {
            var io = new RecordingDatagramIo();
            var policy = new Mock<IUdpDatagramPolicy>();
            policy.Setup(p => p.AcceptReceive(s_attacker, It.IsAny<int>(), It.IsAny<int>())).Returns(false);
            policy.Setup(p => p.AcceptReceive(s_peer, It.IsAny<int>(), It.IsAny<int>())).Returns(true);
            policy.Setup(p => p.RequireSend(s_attacker, It.IsAny<int>()))
                .Throws(new InvalidOperationException("reply denied"));
            await using UdpDatagramTransport transport = NewTransport(io, policy.Object);
            await transport.OpenAsync().ConfigureAwait(false);
            io.Enqueue([0xFF], s_attacker);
            io.Enqueue([42], s_peer);
            await using IAsyncEnumerator<PubSubTransportFrame> received = transport.ReceiveAsync().GetAsyncEnumerator();
            Assert.That(await received.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(received.Current.SourceEndpoint, Is.EqualTo(s_peer));
            Assert.That(received.Current.Payload.ToArray(), Is.EqualTo(new byte[] { 42 }));
            await Assert.ThatAsync(async () => await transport.SendToAsync(new byte[] { 42 }, s_attacker)
                .ConfigureAwait(false), Throws.InvalidOperationException);
            Assert.That(io.Sent, Is.Empty);
            await transport.SendAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
            Assert.That(io.Sent, Has.Count.EqualTo(1));
            Assert.That(io.Sent[0].Destination, Is.EqualTo(s_peer));
            policy.Verify(p => p.RequireSend(s_peer, 3), Times.Once);
            policy.Verify(p => p.AcceptReceive(s_attacker, It.IsAny<int>(), 1), Times.Once);
        }

        private static UdpDatagramTransport NewTransport(RecordingDatagramIo io, IUdpDatagramPolicy? policy = null)
        {
            const string url = "opc.udp://192.0.2.20:4840";
            UdpTransportOptions options = UdpIntegrationTestHelpers.LoopbackOptions();
            if (policy is not null)
            {
                options.WithDatagramPolicy(policy);
            }
            return new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url), UdpEndpointParser.Parse(url),
                PubSubTransportDirection.SendReceive, null, NUnitTelemetryContext.Create(), TimeProvider.System,
                options, null,
                useConnectedUnicastClient: false, trackLastSeenUnicastPeer: true, datagramIo: io);
        }

        private sealed class RecordingDatagramIo : IUdpDatagramIo
        {
            private readonly Channel<(byte[] Payload, IPEndPoint Source)> m_incoming =
                Channel.CreateUnbounded<(byte[], IPEndPoint)>();

            public List<(byte[] Payload, IPEndPoint Destination)> Sent { get; } = [];

            public void Enqueue(byte[] payload, IPEndPoint source)
            {
                Assert.That(m_incoming.Writer.TryWrite((payload, source)), Is.True);
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload, IPEndPoint destination, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Sent.Add((payload.ToArray(), destination));
                return default;
            }

            public async ValueTask<SocketReceiveMessageFromResult> ReceiveAsync(
                Memory<byte> buffer, CancellationToken cancellationToken)
            {
                (byte[] payload, IPEndPoint source) = await m_incoming.Reader.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                payload.CopyTo(buffer);
                return new SocketReceiveMessageFromResult { ReceivedBytes = payload.Length, RemoteEndPoint = source };
            }
        }
    }
}
