// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

#if NET8_0_OR_GREATER
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Exercises the managed HTTP/3 capsule client and listener against each other over loopback QUIC.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    [TestFixture]
    [NonParallelizable]
    public sealed class ManagedWebTransportLoopbackTests
    {
        /// <summary>
        /// Completes HTTP/3 SETTINGS, capsule CONNECT, origin policy, and stream-zero credit setup.
        /// </summary>
        [Test]
        public async Task ConnectAndAcceptEstablishCapsuleUascBaselinesAsync()
        {
            if (!QuicConnection.IsSupported || !QuicListener.IsSupported)
            {
                Assert.That(Environment.GetEnvironmentVariable("OPCUA_REQUIRE_MANAGED_QUIC"),
                    Is.Not.EqualTo("true"), "This validation lane requires the operating system QUIC runtime.");
                Assert.Ignore("The .NET QUIC runtime is unavailable on this platform.");
            }

            using Certificate certificateHandle = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                "urn:opcfoundation:tests:managed-webtransport", "Managed WebTransport",
                "CN=127.0.0.1", ["127.0.0.1"])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using X509Certificate2 certificate = certificateHandle.AsX509Certificate2();
            string expectedThumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            string? observedThumbprint = null;
            var h3 = new SslApplicationProtocol("h3");
            var serverTls = new SslServerAuthenticationOptions
            {
                ApplicationProtocols = [h3],
                ServerCertificate = certificate
            };
            var clientTls = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [h3],
                RemoteCertificateValidationCallback = (_, peerCertificate, _, _) =>
                {
                    observedThumbprint = peerCertificate?.GetCertHashString(HashAlgorithmName.SHA256);
                    return string.Equals(
                        observedThumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
                },
                TargetHost = IPAddress.Loopback.ToString()
            };

            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits
            {
                MaxBufferedBytes = 2 * ManagedWebTransportUascTransport.ConnectionBufferedBytes
            });
            var server = new ManagedWebTransportUascServer(budget);
            await using ManagedWebTransportUascListener listener = await server.ListenAsync(
                new Uri("https://127.0.0.1:0/ua/wt"),
                new IPEndPoint(IPAddress.Loopback, 0),
                serverTls,
                static (_, _) => ValueTask.FromResult(true));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var client = new ManagedWebTransportUascTransport(budget);
            using (var cancelled = new CancellationTokenSource())
            {
                await cancelled.CancelAsync();
                await Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
                    await listener.AcceptAsync(cancelled.Token).ConfigureAwait(false)).ConfigureAwait(false);
                await Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
                    await client.ConnectAsync(listener.EndpointUrl, clientTls, cancelled.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(budget.Usage, Is.Default);
            }
            Task<ManagedWebTransportUascConnection> rejectedAcceptTask = listener.AcceptAsync(timeout.Token).AsTask();
            Uri mismatchedEndpoint = new UriBuilder(listener.EndpointUrl)
            {
                Path = "/wrong"
            }.Uri;
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await client.ConnectAsync(mismatchedEndpoint, clientTls, timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<AuthenticationException>(async () =>
                await rejectedAcceptTask.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(budget.Usage, Is.Default);

            Task<ManagedWebTransportUascConnection> acceptTask = listener.AcceptAsync(timeout.Token).AsTask();
            ManagedWebTransportUascConnection clientConnection;
            try
            {
                clientConnection = await client.ConnectAsync(listener.EndpointUrl, clientTls, timeout.Token);
            }
            catch
            {
                Assert.That(observedThumbprint, Is.EqualTo(expectedThumbprint));
                throw;
            }
            ManagedWebTransportUascConnection serverConnection = await acceptTask.ConfigureAwait(false);
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            using var telemetryOwner = (IDisposable)telemetry;
            try
            {
                DataChannelResourceUsage admitted = budget.Usage;
                Assert.That(admitted.BufferedBytes,
                    Is.EqualTo(2 * ManagedWebTransportUascTransport.ConnectionBufferedBytes));
                Assert.That(admitted.Streams, Is.EqualTo(140));
                DataChannelException? clientRefusal = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await client.ConnectAsync(listener.EndpointUrl, clientTls, timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                DataChannelException? serverRefusal = await Assert.ThrowsAsync<DataChannelException>(async () =>
                    await listener.AcceptAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(clientRefusal?.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(serverRefusal?.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(budget.Usage, Is.EqualTo(admitted),
                    "Refused admission must not change any shared budget dimension.");
                Assert.That(observedThumbprint, Is.EqualTo(expectedThumbprint));
                Assert.That(clientConnection.TransportProfileUri, Is.EqualTo(serverConnection.TransportProfileUri));
                Assert.That(clientConnection.EndpointUrl, Is.EqualTo(listener.EndpointUrl));
                Assert.That(serverConnection.EndpointUrl, Is.EqualTo(listener.EndpointUrl));

                var clientBuffers = new BufferManager("ManagedWebTransportClient", 65536, telemetry);
                var serverBuffers = new BufferManager("ManagedWebTransportServer", 65536, telemetry);
                IUaSCByteTransport clientTransport = clientConnection.Create(clientBuffers, 65536, telemetry);
                IUaSCByteTransport serverTransport = serverConnection.Create(serverBuffers, 65536, telemetry);
                await clientTransport.ConnectAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false);
                byte[] endpointUrl = Encoding.UTF8.GetBytes(listener.EndpointUrl.AbsoluteUri);
                byte[] hello = new byte[32 + endpointUrl.Length];
                "HELF"u8.CopyTo(hello);
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(4), (uint)hello.Length);
                uint[] helloFields = [0, 65536, 65536, 0, 0];
                int helloOffset = 8;
                foreach (uint field in helloFields)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(helloOffset), field);
                    helloOffset += sizeof(uint);
                }
                BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(helloOffset), endpointUrl.Length);
                endpointUrl.CopyTo(hello.AsSpan(helloOffset + sizeof(int)));
                await clientTransport.SendChunkAsync(hello, timeout.Token).ConfigureAwait(false);
                ArraySegment<byte> receivedHello = await serverTransport.ReceiveChunkAsync(timeout.Token)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(receivedHello.AsSpan().ToArray(), Is.EqualTo(hello));
                }
                finally
                {
                    serverBuffers.ReturnBuffer(
                        receivedHello.Array, nameof(IUaSCByteTransport.ReceiveChunkAsync));
                }

                byte[] acknowledgement = new byte[28];
                "ACKF"u8.CopyTo(acknowledgement);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    acknowledgement.AsSpan(4), (uint)acknowledgement.Length);
                uint[] acknowledgementFields = [0, 65536, 65536, 0, 0];
                int acknowledgementOffset = 8;
                foreach (uint field in acknowledgementFields)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        acknowledgement.AsSpan(acknowledgementOffset), field);
                    acknowledgementOffset += sizeof(uint);
                }
                await serverTransport.SendChunkAsync(acknowledgement, timeout.Token).ConfigureAwait(false);
                ArraySegment<byte> receivedAcknowledgement = await clientTransport.ReceiveChunkAsync(timeout.Token)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(receivedAcknowledgement.AsSpan().ToArray(), Is.EqualTo(acknowledgement));
                }
                finally
                {
                    clientBuffers.ReturnBuffer(
                        receivedAcknowledgement.Array, nameof(IUaSCByteTransport.ReceiveChunkAsync));
                }
                clientTransport.Close();
                serverTransport.Close();
                await clientConnection.DisposeAsync().ConfigureAwait(false);
                Assert.That(budget.Usage.BufferedBytes,
                    Is.EqualTo(ManagedWebTransportUascTransport.ConnectionBufferedBytes));
            }
            finally
            {
                await Task.WhenAll(
                    clientConnection.DisposeAsync().AsTask(),
                    serverConnection.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
            Assert.That(budget.Usage, Is.Default);
        }
    }
}
#endif
