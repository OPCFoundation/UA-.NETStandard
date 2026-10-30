// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Exercises the public managed QUIC baseline without custom-native images or direct delivery.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    [TestFixture]
    [NonParallelizable]
    public sealed class ManagedQuicUascLoopbackTests
    {
        /// <summary>
        /// Negotiates the exact baseline, preserves UACP chunks, and keeps accepted connections after listener disposal.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ManagedQuicBaselinePreservesChunksAndOwnsItsConnectionAsync()
        {
            if (!QuicConnection.IsSupported || !QuicListener.IsSupported)
            {
                Assert.That(Environment.GetEnvironmentVariable("OPCUA_REQUIRE_MANAGED_QUIC"),
                    Is.Not.EqualTo("true"), "This validation lane requires the operating system QUIC runtime.");
                Assert.Ignore("The .NET QUIC runtime is unavailable on this platform.");
            }
            using Certificate certificateHandle = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                "urn:opcfoundation:tests:managed-quic", "Managed QUIC",
                "CN=127.0.0.1", ["127.0.0.1"])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using X509Certificate2 certificate = certificateHandle.AsX509Certificate2();
            string thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            var alpn = new SslApplicationProtocol(DataChannelProfiles.UascQuicAlpn);
            var serverTls = new SslServerAuthenticationOptions
            {
                ApplicationProtocols = [alpn],
                ServerCertificate = certificate
            };
            var clientTls = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [alpn],
                RemoteCertificateValidationCallback = (_, peer, _, _) =>
                    string.Equals(peer?.GetCertHashString(HashAlgorithmName.SHA256),
                        thumbprint, StringComparison.OrdinalIgnoreCase),
                TargetHost = IPAddress.Loopback.ToString()
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var transport = new QuicUascTransport();
            await using QuicUascListener listener = await transport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/ua"), serverTls, timeout.Token).ConfigureAwait(false);
            Task<QuicUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
            await using QuicUascConnection client = await transport.ConnectAsync(
                listener.EndpointUrl, clientTls, timeout.Token).ConfigureAwait(false);
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            using var telemetryOwner = (IDisposable)telemetry;
            var clientBuffers = new BufferManager("ManagedQuicClient", 65536, telemetry);
            var serverBuffers = new BufferManager("ManagedQuicServer", 65536, telemetry);
            IUaSCByteTransport clientBytes = client.Create(clientBuffers, 65536, telemetry);
            try
            {
                await clientBytes.ConnectAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false);
                byte[] endpoint = Encoding.UTF8.GetBytes(listener.EndpointUrl.AbsoluteUri);
                byte[] hello = CreateChunk("HELF"u8, 32 + endpoint.Length);
                BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(28), endpoint.Length);
                endpoint.CopyTo(hello.AsSpan(32));
                await clientBytes.SendChunkAsync(hello, timeout.Token).ConfigureAwait(false);
                await using QuicUascConnection server = await accepting.ConfigureAwait(false);
                IUaSCByteTransport serverBytes = server.Create(serverBuffers, 65536, telemetry);
                try
                {
                    await AssertChunkAsync(serverBytes, serverBuffers, hello, timeout.Token).ConfigureAwait(false);
                    IUaSCByteTransport duplicate = client.Create(clientBuffers, 65536, telemetry);
                    try
                    {
                        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                            await duplicate.ConnectAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false))
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        duplicate.Close();
                    }
                    await listener.DisposeAsync().ConfigureAwait(false);
                    byte[] acknowledgement = CreateChunk("ACKF"u8, 28);
                    await serverBytes.SendChunkAsync(acknowledgement, timeout.Token).ConfigureAwait(false);
                    await AssertChunkAsync(clientBytes, clientBuffers, acknowledgement, timeout.Token)
                        .ConfigureAwait(false);
                    byte[] data = CreateChunk("MSGF"u8, 32768);
                    data.AsSpan(8).Fill(0x5A);
                    await clientBytes.SendChunkAsync(data, timeout.Token).ConfigureAwait(false);
                    await AssertChunkAsync(serverBytes, serverBuffers, data, timeout.Token).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(client.TransportProfileUri, Is.EqualTo(DataChannelProfiles.UascQuic));
                        Assert.That(server.TransportProfileUri, Is.EqualTo(DataChannelProfiles.UascQuic));
                        Assert.That(client.EndpointUrl, Is.EqualTo(server.EndpointUrl));
                    });
                }
                finally
                {
                    serverBytes.Close();
                }
            }
            finally
            {
                clientBytes.Close();
            }
        }

        /// <summary>
        /// Creates a complete UACP chunk with explicit buffer limits.
        /// </summary>
        private static byte[] CreateChunk(ReadOnlySpan<byte> type, int length)
        {
            byte[] chunk = new byte[length];
            type.CopyTo(chunk);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)length);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), 65536);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(16), 65536);
            return chunk;
        }

        /// <summary>
        /// Verifies unchanged complete-chunk delivery and returns the borrowed receive buffer.
        /// </summary>
        private static async Task AssertChunkAsync(
            IUaSCByteTransport receiver,
            BufferManager buffers,
            byte[] expected,
            CancellationToken cancellationToken)
        {
            ArraySegment<byte> received = await receiver.ReceiveChunkAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Assert.That(received.AsSpan().SequenceEqual(expected), Is.True);
            }
            finally
            {
                buffers.ReturnBuffer(received.Array, nameof(IUaSCByteTransport.ReceiveChunkAsync));
            }
        }
    }
}
