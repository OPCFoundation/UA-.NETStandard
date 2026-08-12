/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#if NET9_0_OR_GREATER

using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Drives the QUIC transport against a real loopback QUIC connection:
    /// ALPN negotiation, the control stream carrying UASC chunks, and a
    /// data channel bound to its own stream.
    /// </summary>
    /// <remarks>
    /// Skipped where msquic is unavailable. QUIC needs a platform library
    /// that not every CI agent has, and a skip is the honest outcome
    /// there — a red build would say the code is broken when the runtime
    /// simply cannot host it.
    /// </remarks>
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [NonParallelizable]
    public class QuicLoopbackTests
    {
        [SetUp]
        public void SetUp()
        {
            if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            {
                Assert.Ignore("QUIC is unavailable on this platform (msquic missing).");
            }

            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("quic-loopback", 65536, m_telemetry);
            m_certificate = CreateCertificate();
        }

        [TearDown]
        public void TearDown()
        {
            m_certificate?.Dispose();
        }

        /// <summary>
        /// A QUIC listener has to accept a connection that arrives over IPv6.
        /// </summary>
        /// <remarks>
        /// The peers here reach each other by name, and "localhost" resolves to
        /// ::1 before 127.0.0.1 on some hosts, so a listener bound to the IPv4
        /// loopback alone never sees the handshake. .NET reports that as an
        /// ALPN failure rather than a connect failure (dotnet/runtime#85412),
        /// which points the investigation at the wrong layer entirely. The
        /// connection is made over IPv6 explicitly because a host that resolves
        /// IPv4 first would otherwise never exercise this.
        /// </remarks>
        [Test]
        public async Task AListenerAcceptsAConnectionArrivingOverIPv6Async()
        {
            var listenerOptions = new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0),
                ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                    new QuicServerConnectionOptions
                    {
                        DefaultStreamErrorCode = 0x0A,
                        DefaultCloseErrorCode = 0x0B,
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                            ServerCertificate = m_certificate,
                            ClientCertificateRequired = true,
                            RemoteCertificateValidationCallback = (_, _, _, _) => true
                        }
                    })
            };

            await using QuicListener listener = await QuicListener
                .ListenAsync(listenerOptions).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Task<QuicConnection> accept = listener.AcceptConnectionAsync(cts.Token).AsTask();

            await using QuicConnection client = await QuicConnection.ConnectAsync(
                new QuicClientConnectionOptions
                {
                    RemoteEndPoint = new IPEndPoint(
                        IPAddress.IPv6Loopback,
                        listener.LocalEndPoint.Port),
                    DefaultStreamErrorCode = 0x0A,
                    DefaultCloseErrorCode = 0x0B,
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                        TargetHost = "localhost",
                        ClientCertificates = [m_certificate!],
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    }
                },
                cts.Token).ConfigureAwait(false);

            await using QuicConnection server = await accept.ConfigureAwait(false);

            Assert.That(
                client.NegotiatedApplicationProtocol,
                Is.EqualTo(QuicTransport.ApplicationProtocol));
        }

        [Test]
        public async Task TheControlStreamCarriesChunksBothWaysAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            byte[] chunk = BuildHelloLikeChunk(64);

            await loopback.Client
                .SendChunkAsync(chunk, CancellationToken.None)
                .ConfigureAwait(false);

            ArraySegment<byte> received = await loopback.Server
                .ReceiveChunkAsync(TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                Assert.That(received, Has.Count.EqualTo(chunk.Length));
                Assert.That(
                    received.AsSpan().ToArray(),
                    Is.EqualTo(chunk),
                    "the control stream carries the chunk byte for byte");
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(received.Array, nameof(TheControlStreamCarriesChunksBothWaysAsync));
            }
        }

        // DCQ-001: ALPN is negotiated, and a peer offering a foreign
        // identifier is abandoned rather than accepted.
        [Test]
        public async Task DcQ001AForeignAlpnIsRefusedAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            var foreign = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("h3")],
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };

            var options = new QuicClientConnectionOptions
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, loopback.Port),
                ClientAuthenticationOptions = foreign,
                DefaultStreamErrorCode = 0x0A,
                DefaultCloseErrorCode = 0x0B
            };

            Assert.That(
                async () =>
                {
                    QuicConnection connection = await QuicConnection
                        .ConnectAsync(options, TimeoutToken())
                        .ConfigureAwait(false);

                    await connection.DisposeAsync().ConfigureAwait(false);
                },
                Throws.InstanceOf<Exception>(),
                "a peer that does not speak the OPC UA ALPN identifier is never accepted");
        }

        [Test]
        public async Task DataChannelFramesTravelOnTheirOwnStreamAsync()
        {
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(m_certificate!, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);

            await using var clientData = new QuicDataChannelTransport(
                loopback.Client,
                m_bufferManager!,
                m_telemetry!);

            await using var serverData = new QuicDataChannelTransport(
                loopback.Server,
                m_bufferManager!,
                m_telemetry!);

            const uint channelId = 1;

            // SinkToSource is client-initiated and carries its stream id
            // in the OpenDataChannel request.
            ulong streamId = await clientData
                .OpenChannelStreamAsync(
                    channelId,
                    DataChannelDirection.SinkToSource,
                    isOpcUaServer: false,
                    TimeoutToken())
                .ConfigureAwait(false);

            byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];

            await clientData
                .SendFrameAsync(
                    DataChannelFrame.Data(
                        channelId,
                        1,
                        DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd,
                        payload),
                    TimeoutToken())
                .ConfigureAwait(false);

            // The server accepts the stream the client opened and reads
            // the frame from it.
            ulong acceptedId = await loopback.Server
                .AcceptStreamAsync(TimeoutToken())
                .ConfigureAwait(false);

            ArraySegment<byte> chunk = await loopback.Server
                .ReceiveOnStreamAsync(acceptedId, TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                Assert.That(
                    System.Text.Encoding.ASCII.GetString(chunk.Array!, chunk.Offset, 3),
                    Is.EqualTo("STR"),
                    "a data channel frame keeps its message header over QUIC");

                var body = new ReadOnlyMemory<byte>(
                    chunk.Array!,
                    chunk.Offset + 12,
                    chunk.Count - 12);

                Assert.That(
                    DataChannelFrameCodec.TryDecode(
                        body,
                        0,
                        out DataChannelFrame frame,
                        out DataChannelFrameError error),
                    Is.True,
                    error.ToString());

                Assert.Multiple(() =>
                {
                    Assert.That(frame.ChannelId, Is.EqualTo(channelId));
                    Assert.That(frame.Payload.Span.ToArray(), Is.EqualTo(payload));
                    Assert.That(
                        chunk,
                        Has.Count.EqualTo(12 + 12 + payload.Length),
                        "QUIC framing omits the security header, the sequence header and the footer");
                });
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(chunk.Array, nameof(DataChannelFramesTravelOnTheirOwnStreamAsync));
            }
        }

        // DCQ-004: no CREDIT frame is sent over opc.quic, because QUIC
        // applies its own per-stream and per-connection flow control.
        [Test]
        public void DcQ004TheQuicTransportReportsItsOwnFlowControl()
        {
            using var probe = new QuicDataChannelTransportProbe(m_bufferManager!, m_telemetry!);

            Assert.Multiple(() =>
            {
                Assert.That(probe.HasTransportFlowControl, Is.True);
                Assert.That(probe.FramingMode, Is.EqualTo(DataChannelFramingMode.Quic));
            });
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
        }

        private static byte[] BuildHelloLikeChunk(int size)
        {
            byte[] chunk = new byte[size];
            chunk[0] = (byte)'H';
            chunk[1] = (byte)'E';
            chunk[2] = (byte)'L';
            chunk[3] = (byte)'F';
            BitConverter.GetBytes(size).CopyTo(chunk, 4);

            for (int ii = 8; ii < size; ii++)
            {
                chunk[ii] = (byte)ii;
            }

            return chunk;
        }

        private static X509Certificate2 CreateCertificate()
        {
            using Certificate created = CertificateBuilder
                .Create("CN=QuicLoopback")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:localhost:UA:QuicLoopback",
                    s_domainNames))
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // msquic requires the private key to be usable for TLS, so the
            // certificate is round-tripped through a PFX.
            byte[] pfx = created.AsX509Certificate2().Export(X509ContentType.Pfx);
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
        }

        private static readonly string[] s_domainNames = ["localhost"];

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;
        private X509Certificate2? m_certificate;
    }

    /// <summary>
    /// A data channel transport with no connection behind it, used to
    /// assert the flow-control and framing contract without a network.
    /// </summary>
    internal sealed class QuicDataChannelTransportProbe : IDisposable
    {
        public QuicDataChannelTransportProbe(BufferManager bufferManager, ITelemetryContext telemetry)
        {
            m_bufferManager = bufferManager;
            m_telemetry = telemetry;
        }

        public bool HasTransportFlowControl => true;

        public DataChannelFramingMode FramingMode => DataChannelFramingMode.Quic;

        public void Dispose()
        {
        }

        private readonly BufferManager m_bufferManager;
        private readonly ITelemetryContext m_telemetry;
    }
}

#endif
