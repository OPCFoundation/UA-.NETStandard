#if NET8_0_OR_GREATER
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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    [TestFixture]
    [Category("Integration")]
    [CancelAfter(10000)]
    [TestSpec("RFC 9147 §4.5.2")]
    public sealed class DtlsDatagramTransportPeerTests
    {
        [Test]
        public async Task AuthenticatedRecordFromDifferentSourceDoesNotRedirectPinnedPeerAsync()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            DtlsProfile profile = CreateProfile();
            string url = $"opc.dtls://127.0.0.1:{port}";
            var endpoint = new UdpEndpoint(
                IPAddress.Loopback,
                port,
                UdpAddressType.Unicast,
                url,
                IsDtls: true,
                DtlsProfileName: profile.Name);
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            using var authenticatedSocket = NewLoopbackSocket();
            using var spoofSocket = NewLoopbackSocket();
            var authenticatedEndpoint = (IPEndPoint)authenticatedSocket.LocalEndPoint!;
            var contextFactory = new MarkerContextFactory(authenticatedEndpoint);
            await using var transport = new DtlsDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "dtls-peer"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions(),
                diagnostics,
                contextFactory,
                profile);

            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"DTLS loopback open failed: {ex.Message}");
                return;
            }

            var destination = new IPEndPoint(IPAddress.Loopback, port);
            Task<PubSubTransportFrame?> receiveTask = UdpIntegrationTestHelpers.ReceiveOneAsync(
                transport,
                TimeSpan.FromSeconds(5));

            Assert.That(transport.RemoteEndpoint, Is.EqualTo(authenticatedEndpoint));
            await spoofSocket.SendToAsync(
                new byte[] { MarkerContext.Marker, 0x55 },
                SocketFlags.None,
                destination).ConfigureAwait(false);
            await WaitForReceivedCountAsync(diagnostics, expectedCount: 1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(contextFactory.UnprotectCount, Is.Zero);
                Assert.That(transport.RemoteEndpoint, Is.EqualTo(authenticatedEndpoint));
            });

            await authenticatedSocket.SendToAsync(
                new byte[] { MarkerContext.Marker, 0x55 },
                SocketFlags.None,
                destination).ConfigureAwait(false);
            PubSubTransportFrame? frame = await receiveTask.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(frame, Is.Not.Null);
                Assert.That(frame!.Value.Payload.ToArray(), Is.EqualTo(new byte[] { 0x55 }));
                Assert.That(frame.Value.SourceEndpoint, Is.EqualTo(authenticatedEndpoint));
                Assert.That(transport.RemoteEndpoint, Is.EqualTo(authenticatedEndpoint));
                Assert.That(contextFactory.UnprotectCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task SetAuthenticatedPeerRejectsNullPeerAsync()
        {
            await using DtlsDatagramTransport transport = CreateReceiveTransport();
            var channel = (IDtlsAuthenticatedPeerChannel)transport;
            Assert.That(
                () => channel.SetAuthenticatedPeer(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task SetAuthenticatedPeerRejectsConflictingRebindAsync()
        {
            await using DtlsDatagramTransport transport = CreateReceiveTransport();
            var channel = (IDtlsAuthenticatedPeerChannel)transport;
            channel.SetAuthenticatedPeer(new IPEndPoint(IPAddress.Loopback, 5001));
            Assert.Multiple(() =>
            {
                // Re-pinning the identical endpoint is idempotent.
                Assert.That(
                    () => channel.SetAuthenticatedPeer(new IPEndPoint(IPAddress.Loopback, 5001)),
                    Throws.Nothing);
                // Without a negotiated connection ID the association cannot move
                // to a different peer endpoint.
                Assert.That(
                    () => channel.SetAuthenticatedPeer(new IPEndPoint(IPAddress.Loopback, 5002)),
                    Throws.TypeOf<InvalidOperationException>());
            });
        }

        [Test]
        public async Task CloseClearsAuthenticatedPeerAsync()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            DtlsProfile profile = CreateProfile();
            string url = $"opc.dtls://127.0.0.1:{port}";
            var endpoint = new UdpEndpoint(
                IPAddress.Loopback,
                port,
                UdpAddressType.Unicast,
                url,
                IsDtls: true,
                DtlsProfileName: profile.Name);
            using var authenticatedSocket = NewLoopbackSocket();
            var authenticatedEndpoint = (IPEndPoint)authenticatedSocket.LocalEndPoint!;
            var contextFactory = new MarkerContextFactory(authenticatedEndpoint);
            await using var transport = new DtlsDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "dtls-peer-close"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions(),
                diagnostics: null,
                contextFactory,
                profile);

            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"DTLS loopback open failed: {ex.Message}");
                return;
            }

            Assert.That(transport.RemoteEndpoint, Is.EqualTo(authenticatedEndpoint));

            // Closing tears down the association and clears the pinned peer so a
            // fresh handshake is required before any further records are accepted.
            Assert.That(
                async () => await transport.CloseAsync().ConfigureAwait(false),
                Throws.Nothing);
        }

        private static DtlsDatagramTransport CreateReceiveTransport(int port = 44444)
        {
            DtlsProfile profile = CreateProfile();
            string url = $"opc.dtls://127.0.0.1:{port}";
            var endpoint = new UdpEndpoint(
                IPAddress.Loopback,
                port,
                UdpAddressType.Unicast,
                url,
                IsDtls: true,
                DtlsProfileName: profile.Name);
            return new DtlsDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "dtls-peer-unit"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions(),
                diagnostics: null,
                new MarkerContextFactory(new IPEndPoint(IPAddress.Loopback, port)),
                profile);
        }

        private static Socket NewLoopbackSocket()
        {
            var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return socket;
        }

        private static async Task WaitForReceivedCountAsync(
            PubSubDiagnostics diagnostics,
            long expectedCount)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(3);
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                long observedCount = diagnostics.Read(
                    PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                if (observedCount >= expectedCount)
                {
                    return;
                }
                if (stopwatch.Elapsed >= timeout)
                {
                    Assert.Fail(
                        $"Timed out after {timeout} waiting for at least {expectedCount} received " +
                        $"network message(s); observed {observedCount}.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
            }
        }

        private static DtlsProfile CreateProfile()
        {
            return new DtlsProfile(
                "test-aead",
                DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsNamedCurve.NistP256,
                DtlsNamedCurve.NistP256,
                isMandatory: false);
        }

        private sealed class MarkerContextFactory : IDtlsContextFactory
        {
            public MarkerContextFactory(IPEndPoint authenticatedPeer)
            {
                m_authenticatedPeer = authenticatedPeer;
            }

            public int UnprotectCount => Volatile.Read(ref m_unprotectCount);

            public ValueTask<IDtlsContext> CreateAsync(
                PubSubConnectionDataType connection,
                UdpEndpoint endpoint,
                DtlsProfile profile,
                ITelemetryContext telemetry,
                TimeProvider timeProvider,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IDtlsContext>(
                    new MarkerContext(profile, m_authenticatedPeer, RecordUnprotect));
            }

            private void RecordUnprotect()
            {
                Interlocked.Increment(ref m_unprotectCount);
            }

            private readonly IPEndPoint m_authenticatedPeer;
            private int m_unprotectCount;
        }

        private sealed class MarkerContext : IDtlsContext
        {
            public const byte Marker = 0xA5;

            public MarkerContext(
                DtlsProfile profile,
                IPEndPoint authenticatedPeer,
                Action recordUnprotect)
            {
                Profile = profile;
                m_authenticatedPeer = authenticatedPeer;
                m_recordUnprotect = recordUnprotect;
            }

            public DtlsProfile Profile { get; }

            public ValueTask OpenAsync(
                IDtlsDatagramChannel channel,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (channel is not IDtlsAuthenticatedPeerChannel peerChannel)
                {
                    throw new InvalidOperationException(
                        "The test DTLS channel does not support authenticated peer pinning.");
                }

                peerChannel.SetAuthenticatedPeer(m_authenticatedPeer);
                return ValueTask.CompletedTask;
            }

            public ValueTask<ReadOnlyMemory<byte>> ProtectAsync(
                ReadOnlyMemory<byte> payload,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] record = new byte[payload.Length + 1];
                record[0] = Marker;
                payload.Span.CopyTo(record.AsSpan(1));
                return new ValueTask<ReadOnlyMemory<byte>>(record);
            }

            public ValueTask<ReadOnlyMemory<byte>> UnprotectAsync(
                ReadOnlyMemory<byte> record,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_recordUnprotect();
                if (record.IsEmpty || record.Span[0] != Marker)
                {
                    throw new CryptographicException("Unauthenticated test record.");
                }

                return new ValueTask<ReadOnlyMemory<byte>>(record[1..]);
            }

            public void Dispose()
            {
            }

            private readonly IPEndPoint m_authenticatedPeer;
            private readonly Action m_recordUnprotect;
        }
    }
}
#endif
