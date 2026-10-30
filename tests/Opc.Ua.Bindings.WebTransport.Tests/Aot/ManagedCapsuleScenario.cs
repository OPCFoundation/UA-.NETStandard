// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Exercises the public managed capsule transport without custom native libraries.
    /// </summary>
    internal static class ManagedCapsuleScenario
    {
        /// <summary>
        /// Completes HTTP/3, CONNECT, capsule credit and a bidirectional UACP exchange.
        /// </summary>
        internal static async Task RunAsync()
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException("Managed QUIC qualification requires Windows or Linux.");
            }
            if (!QuicConnection.IsSupported || !QuicListener.IsSupported)
            {
                throw new PlatformNotSupportedException("The operating system QUIC runtime is unavailable.");
            }
            using Certificate certificateHandle = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                "urn:opcfoundation:tests:managed-capsule-aot", "Managed Capsule AOT",
                "CN=127.0.0.1", ["127.0.0.1"])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using X509Certificate2 certificate = certificateHandle.AsX509Certificate2();
            string thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            var h3 = new SslApplicationProtocol("h3");
            var serverTls = new SslServerAuthenticationOptions
            {
                ApplicationProtocols = [h3],
                ServerCertificate = certificate
            };
            var clientTls = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [h3],
                RemoteCertificateValidationCallback = (_, peer, _, _) =>
                    string.Equals(peer?.GetCertHashString(HashAlgorithmName.SHA256),
                        thumbprint, StringComparison.OrdinalIgnoreCase),
                TargetHost = IPAddress.Loopback.ToString()
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using ManagedWebTransportUascListener listener = await new ManagedWebTransportUascServer()
                .ListenAsync(new Uri("https://127.0.0.1:0/ua/wt"),
                    new IPEndPoint(IPAddress.Loopback, 0), serverTls,
                    static (_, _) => ValueTask.FromResult(true),
                    cancellationToken: timeout.Token).ConfigureAwait(false);
            Task<ManagedWebTransportUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
            await using ManagedWebTransportUascConnection client = await new ManagedWebTransportUascTransport()
                .ConnectAsync(listener.EndpointUrl, clientTls, timeout.Token).ConfigureAwait(false);
            await using ManagedWebTransportUascConnection server = await accepting.ConfigureAwait(false);
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            using var telemetryOwner = (IDisposable)telemetry;
            var clientBuffers = new BufferManager("ManagedCapsuleAotClient", 65536, telemetry);
            var serverBuffers = new BufferManager("ManagedCapsuleAotServer", 65536, telemetry);
            IUaSCByteTransport clientBytes = client.Create(clientBuffers, 65536, telemetry);
            IUaSCByteTransport serverBytes = server.Create(serverBuffers, 65536, telemetry);
            try
            {
                await clientBytes.ConnectAsync(listener.EndpointUrl, timeout.Token).ConfigureAwait(false);
                byte[] endpoint = Encoding.UTF8.GetBytes(listener.EndpointUrl.AbsoluteUri);
                byte[] hello = CreateMessage("HELF"u8, 32 + endpoint.Length);
                BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(28), endpoint.Length);
                endpoint.CopyTo(hello.AsSpan(32));
                await ExchangeAsync(clientBytes, serverBytes, serverBuffers, hello, timeout.Token)
                    .ConfigureAwait(false);
                byte[] acknowledgement = CreateMessage("ACKF"u8, 28);
                await ExchangeAsync(serverBytes, clientBytes, clientBuffers, acknowledgement, timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                clientBytes.Close();
                serverBytes.Close();
            }
        }

        /// <summary>
        /// Creates one bounded UACP message with the standard protocol limits.
        /// </summary>
        private static byte[] CreateMessage(ReadOnlySpan<byte> type, int length)
        {
            byte[] message = new byte[length];
            type.CopyTo(message);
            BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), (uint)length);
            BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(12), 65536);
            BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(16), 65536);
            return message;
        }

        /// <summary>
        /// Verifies the exact complete chunk and returns its receive buffer.
        /// </summary>
        private static async Task ExchangeAsync(
            IUaSCByteTransport sender,
            IUaSCByteTransport receiver,
            BufferManager receiveBuffers,
            byte[] expected,
            CancellationToken cancellationToken)
        {
            await sender.SendChunkAsync(expected, cancellationToken).ConfigureAwait(false);
            ArraySegment<byte> received = await receiver.ReceiveChunkAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!received.AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidOperationException("Managed capsule transport changed the UACP chunk.");
                }
            }
            finally
            {
                receiveBuffers.ReturnBuffer(received.Array, nameof(IUaSCByteTransport.ReceiveChunkAsync));
            }
        }
    }
}
