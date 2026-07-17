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
        public async Task RemotePeerChangesOnlyAfterAuthenticatedRecordAsync()
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
            await using var transport = new DtlsDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "dtls-peer"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions(),
                diagnostics,
                new MarkerContextFactory(),
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

            using var spoofSocket = NewLoopbackSocket();
            using var authenticatedSocket = NewLoopbackSocket();
            var destination = new IPEndPoint(IPAddress.Loopback, port);
            Task<PubSubTransportFrame?> receiveTask = UdpIntegrationTestHelpers.ReceiveOneAsync(
                transport,
                TimeSpan.FromSeconds(5));

            await spoofSocket.SendToAsync(
                new byte[] { 0x00 },
                SocketFlags.None,
                destination).ConfigureAwait(false);
            await WaitForReceivedCountAsync(diagnostics, expectedCount: 1).ConfigureAwait(false);

            var spoofEndpoint = (IPEndPoint)spoofSocket.LocalEndPoint!;
            Assert.That(transport.RemoteEndpoint, Is.Not.EqualTo(spoofEndpoint));

            await authenticatedSocket.SendToAsync(
                new byte[] { MarkerContext.Marker, 0x55 },
                SocketFlags.None,
                destination).ConfigureAwait(false);
            PubSubTransportFrame? frame = await receiveTask.ConfigureAwait(false);
            var authenticatedEndpoint = (IPEndPoint)authenticatedSocket.LocalEndPoint!;

            Assert.Multiple(() =>
            {
                Assert.That(frame, Is.Not.Null);
                Assert.That(frame!.Value.Payload.ToArray(), Is.EqualTo(new byte[] { 0x55 }));
                Assert.That(frame.Value.SourceEndpoint, Is.EqualTo(authenticatedEndpoint));
                Assert.That(transport.RemoteEndpoint, Is.EqualTo(authenticatedEndpoint));
            });
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages) < expectedCount)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token).ConfigureAwait(false);
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
            public ValueTask<IDtlsContext> CreateAsync(
                PubSubConnectionDataType connection,
                UdpEndpoint endpoint,
                DtlsProfile profile,
                ITelemetryContext telemetry,
                TimeProvider timeProvider,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IDtlsContext>(new MarkerContext(profile));
            }
        }

        private sealed class MarkerContext : IDtlsContext
        {
            public const byte Marker = 0xA5;

            public MarkerContext(DtlsProfile profile)
            {
                Profile = profile;
            }

            public DtlsProfile Profile { get; }

            public ValueTask OpenAsync(
                IDtlsDatagramChannel channel,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                if (record.IsEmpty || record.Span[0] != Marker)
                {
                    throw new CryptographicException("Unauthenticated test record.");
                }

                return new ValueTask<ReadOnlyMemory<byte>>(record[1..]);
            }

            public void Dispose()
            {
            }
        }
    }
}
#endif
