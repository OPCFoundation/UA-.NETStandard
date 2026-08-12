#if NET9_0_OR_GREATER
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
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [NonParallelizable]
    public sealed class QuicClientSecurityTests
    {
        private const string ApplicationUri = "urn:localhost:UA:QuicClientSecurity";

        [SetUp]
        public void SetUp()
        {
            if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            {
                Assert.Ignore("QUIC is unavailable on this platform (msquic missing).");
            }

            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("quic-client-security", 65536, m_telemetry);
        }

        [Test]
        public async Task DcQ007DifferentTlsKeyIsRejectedByClientBindingAsync()
        {
            using X509Certificate2 tlsCertificate = CreateCertificate("CN=Server", "localhost");
            using X509Certificate2 applicationCertificate = CreateCertificate("CN=Server", "localhost");
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(tlsCertificate, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);
            EndpointDescription endpoint = CreateSecureEndpoint(loopback.Port, applicationCertificate);
            var client = new QuicPeerBindingTransport(
                loopback.Client,
                m_bufferManager!,
                endpoint);

            await loopback.Server
                .SendChunkAsync(BuildOpenSecureChannelResponse(applicationCertificate), TimeoutToken())
                .ConfigureAwait(false);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReceiveChunkAsync(TimeoutToken()).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public async Task KeyEqualTlsPeerIsAcceptedByClientBindingAsync()
        {
            using X509Certificate2 certificate = CreateCertificate("CN=Server", "localhost");
            await using QuicLoopback loopback = await QuicLoopback
                .StartAsync(certificate, m_bufferManager!, m_telemetry!)
                .ConfigureAwait(false);
            EndpointDescription endpoint = CreateSecureEndpoint(loopback.Port, certificate);
            var client = new QuicPeerBindingTransport(
                loopback.Client,
                m_bufferManager!,
                endpoint);

            byte[] response = BuildOpenSecureChannelResponse(certificate);
            await loopback.Server
                .SendChunkAsync(response, TimeoutToken())
                .ConfigureAwait(false);

            ArraySegment<byte> received = await client
                .ReceiveChunkAsync(TimeoutToken())
                .ConfigureAwait(false);

            try
            {
                Assert.That(received, Has.Count.EqualTo(response.Length));
            }
            finally
            {
                m_bufferManager!.ReturnBuffer(received.Array, nameof(KeyEqualTlsPeerIsAcceptedByClientBindingAsync));
            }
        }

        [Test]
        public async Task SanThatDoesNotCoverEndpointHostIsRejectedAsync()
        {
            using X509Certificate2 certificate = CreateCertificate("CN=Server", "other.example");
            await using QuicListener listener = await CreateRawListenerAsync(certificate)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<QuicConnection> accept = listener.AcceptConnectionAsync(timeout.Token).AsTask();
            var transport = new QuicMultiplexedTransport(
                m_bufferManager!,
                65536,
                m_telemetry!,
                new QuicClientOptions
                {
                    EndpointDescription = CreateSecureEndpoint(listener.LocalEndPoint.Port, certificate),
                    ServerCertificateValidation = (_, _, _, _) => true
                });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport
                    .ConnectAsync(
                        QuicTransport.CreateUrl("localhost", listener.LocalEndPoint.Port),
                        timeout.Token)
                    .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));

            if (accept.IsCompletedSuccessfully)
            {
                QuicConnection accepted = await accept.ConfigureAwait(false);
                await accepted.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static EndpointDescription CreateSecureEndpoint(
            int port,
            X509Certificate2 certificate)
        {
            return new EndpointDescription
            {
                EndpointUrl = QuicTransport.CreateUrl("localhost", port).ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.UaQuicTransport,
                ServerCertificate = certificate.RawData.ToByteString(),
                Server = new ApplicationDescription
                {
                    ApplicationName = new LocalizedText("QuicClientSecurity"),
                    ApplicationType = ApplicationType.Server,
                    ApplicationUri = ApplicationUri,
                    ProductUri = "urn:opcfoundation.org:QuicClientSecurity"
                },
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>()
            };
        }

        private static X509Certificate2 CreateCertificate(string subject, string dnsName)
        {
            ICertificateBuilder builder = CertificateBuilder
                .Create(subject)
                .AddExtension(new X509SubjectAltNameExtension(ApplicationUri, new[] { dnsName }));

            using Certificate created = builder
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            return created.AsX509Certificate2();
        }

        private static byte[] BuildOpenSecureChannelResponse(X509Certificate2 senderCertificate)
        {
            byte[] policy = Encoding.UTF8.GetBytes(SecurityPolicies.Basic256Sha256);
            byte[] certificate = senderCertificate.RawData;
            int size = 12 +
                sizeof(int) + policy.Length +
                sizeof(int) + certificate.Length +
                sizeof(int);
            byte[] chunk = new byte[size];
            chunk[0] = (byte)'O';
            chunk[1] = (byte)'P';
            chunk[2] = (byte)'N';
            chunk[3] = (byte)'F';
            BitConverter.GetBytes(size).CopyTo(chunk, 4);
            int offset = 12;
            WriteByteString(chunk, ref offset, policy);
            WriteByteString(chunk, ref offset, certificate);
            BitConverter.GetBytes(-1).CopyTo(chunk, offset);
            return chunk;
        }

        private static void WriteByteString(byte[] chunk, ref int offset, byte[] value)
        {
            BitConverter.GetBytes(value.Length).CopyTo(chunk, offset);
            offset += sizeof(int);
            Buffer.BlockCopy(value, 0, chunk, offset, value.Length);
            offset += value.Length;
        }

        private static async Task<QuicListener> CreateRawListenerAsync(X509Certificate2 certificate)
        {
            var options = new QuicListenerOptions
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
                            ServerCertificate = certificate
                        }
                    })
            };

            return await QuicListener.ListenAsync(options).ConfigureAwait(false);
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        }

        private ITelemetryContext? m_telemetry;
        private BufferManager? m_bufferManager;
    }
}
#endif
