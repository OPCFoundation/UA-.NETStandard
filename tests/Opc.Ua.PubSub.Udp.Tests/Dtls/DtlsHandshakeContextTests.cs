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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// End-to-end DTLS 1.3 flight driver tests from RFC 9147 §5 and RFC 8446 §4.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 9147 §5")]
    [TestSpec("RFC 9147 §5.1")]
    [TestSpec("RFC 8446 §4")]
    [TestSpec("Part 14 §7.3.2.4")]
    public sealed class DtlsHandshakeContextTests
    {
        [Test]
        public async Task HandshakeCompletesAndProtectsApplicationDatagramForNistAeadAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP384_AesGcm");
            await RunHandshakeAndApplicationRoundTripAsync(profile!).ConfigureAwait(false);
        }

        [Test]
        public async Task HandshakeCompletesAndProtectsApplicationDatagramForIntegrityOnlyAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256");
            await RunHandshakeAndApplicationRoundTripAsync(profile!).ConfigureAwait(false);
        }

        /// <summary>
        /// Both integrity hashes complete a real handshake and protect application data in each direction.
        /// </summary>
        [TestCase("ECC_nistP256")]
        [TestCase("ECC_nistP384")]
        [Category("SecurityRegression")]
        public async Task SecurityF20IntegrityOnlyHandshakeProtectsBothDirectionsAsync(string profileName)
        {
            DtlsProfile profile = ResolveOrIgnore(profileName);
            await RunHandshakeAndApplicationRoundTripAsync(profile).ConfigureAwait(false);
        }

        [Test]
        public async Task HandshakeCompletesAndProtectsApplicationDatagramForBrainpoolWhenAvailableAsync()
        {
            var registry = new DtlsProfileRegistry();
            if (!registry.TryResolve("ECC_brainpoolP256r1_AesGcm", out DtlsProfile? profile))
            {
                Assert.Ignore("Brainpool P256r1 is not available from this platform BCL.");
                return;
            }

            await RunHandshakeAndApplicationRoundTripAsync(profile!).ConfigureAwait(false);
        }

        [Test]
        public void Curve25519ProfileFailsFastBeforeHandshake()
        {
            var registry = new DtlsProfileRegistry();

            Assert.That(() => registry.Resolve("ECC_curve25519"), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public async Task CipherDowngradeIsRejectedAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP384_AesGcm");
            using Certificate certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair
                = InMemoryDtlsDatagramChannel.CreatePair(serverToClientTransform: DowngradeServerCipherSuite);
            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();

            Assert.That(async () => await clientTask.ConfigureAwait(false), Throws.TypeOf<DtlsHandshakeException>());
            await cts.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await serverTask.ConfigureAwait(false), Throws.Exception);
        }

        [Test]
        public async Task TamperedFinishedIsRejectedAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair
                = InMemoryDtlsDatagramChannel.CreatePair(serverToClientTransform: TamperFirstFinished);
            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();

            Assert.That(async () => await clientTask.ConfigureAwait(false), Throws.TypeOf<DtlsHandshakeException>());
            await cts.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await serverTask.ConfigureAwait(false), Throws.Exception);
        }

        [Test]
        public async Task BadPeerCertificateIsRejectedByInjectedValidatorAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            var validator = new Mock<ICertificateValidatorEx>(MockBehavior.Strict);
            validator.Setup(v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CertificateValidationResult(
                    isValid: false,
                    statusCode: StatusCodes.BadCertificateInvalid,
                    errors: [new ServiceResult(StatusCodes.BadCertificateInvalid)],
                    isSuppressible: false));
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair = InMemoryDtlsDatagramChannel.CreatePair();
            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();

            Assert.That(async () => await clientTask.ConfigureAwait(false), Throws.Exception);
            await cts.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await serverTask.ConfigureAwait(false), Throws.Exception);
        }

        /// <summary>
        /// Trusted certificates for another identity cannot establish an application association.
        /// </summary>
        [TestCase("opc.dtls://subscriber.example:4843", "unrelated.example", null)]
        [TestCase("opc.dtls://subscriber.example:4843", null, "127.0.0.1")]
        [TestCase("opc.dtls://subscriber.example:4843", "subscriber", null)]
        [TestCase("opc.dtls://subscriber.example:4843", null, null)]
        [TestCase("opc.dtls://127.0.0.1:4843", "127.0.0.1", null)]
        [TestCase("opc.dtls://127.0.0.1:4843", null, "127.0.0.2")]
        [TestCase("opc.dtls://[::1]:4843", null, "::2")]
        [Category("SecurityRegression")]
        public async Task SecurityF06HandshakeRequiresConfiguredPeerIdentityAsync(
            string configuredUrl,
            string? dnsName,
            string? ipAddress)
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate serverCertificate = CreateEcdsaCertificate(
                profile.CertificateCurve, dnsName, ipAddress, commonName: "subscriber.example");
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            UdpEndpoint endpoint = CreateEndpoint(profile) with { OriginalUrl = configuredUrl };
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair =
                InMemoryDtlsDatagramChannel.CreatePair();
            using IDtlsContext client = await CreateFactoryClientAsync(profile, endpoint, validator.Object)
                .ConfigureAwait(false);
            using DtlsHandshakeContext server = CreateContext(
                profile, DtlsEndpointRole.Server, serverCertificate, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();
            byte[] payload = [0x55, 0x41];

            try
            {
                await Assert.ThatAsync(
                    async () => await clientTask.ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadCertificateHostNameInvalid)).ConfigureAwait(false);
                Assert.That(pair.Client.AuthenticatedPeer, Is.Null);
                await Assert.ThatAsync(
                    async () => await client.ProtectAsync(payload).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>(),
                    "An unrelated trusted peer must never receive application traffic keys.");
                validator.Verify(v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()), Times.Once());
            }
            finally
            {
                await CancelPeerHandshakeAsync(serverTask, cts).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Matching configured identities preserve mutual authentication and bidirectional application traffic.
        /// </summary>
        [TestCase("opc.dtls://subscriber.example:4843", "subscriber.example", null)]
        [TestCase("opc.dtls://SUBSCRIBER.example:4843", "subscriber.example", null)]
        [TestCase("opc.dtls://localhost:4843", "localhost", null)]
        [TestCase("opc.dtls://127.0.0.1:4843", null, "127.0.0.1")]
        [TestCase("opc.dtls://[::1]:4843", null, "::1")]
        [TestCase(null, null, "127.0.0.1")]
        [Category("SecurityRegression")]
        public async Task SecurityF06HandshakeAcceptsConfiguredDnsOrIpSanAsync(
            string? configuredUrl,
            string? dnsName,
            string? ipAddress)
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate clientCertificate = CreateEcdsaCertificate(
                profile.CertificateCurve, "publisher.example");
            using Certificate serverCertificate = CreateEcdsaCertificate(
                profile.CertificateCurve, dnsName, ipAddress);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            UdpEndpoint endpoint = CreateEndpoint(profile) with { OriginalUrl = configuredUrl };
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair =
                InMemoryDtlsDatagramChannel.CreatePair();
            using IDtlsContext client = await CreateFactoryClientAsync(
                profile, endpoint, validator.Object, clientCertificate).ConfigureAwait(false);
            var serverOptions = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator.Object,
                RequireClientCertificate = true
            };
            serverOptions.LocalCertificates.Add(serverCertificate);
            using DtlsHandshakeContext server = CreateContext(
                profile, DtlsEndpointRole.Server, serverOptions, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            await Task.WhenAll(
                client.OpenAsync(pair.Client, cts.Token).AsTask(),
                server.OpenAsync(pair.Server, cts.Token).AsTask()).ConfigureAwait(false);

            byte[] publisherPayload = [0x50, 0x55, 0x42];
            byte[] subscriberPayload = [0x53, 0x55, 0x42];
            ReadOnlyMemory<byte> publisherRecord = await client.ProtectAsync(publisherPayload, cts.Token)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> subscriberRecord = await server.ProtectAsync(subscriberPayload, cts.Token)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> receivedPublisherPayload = await server.UnprotectAsync(publisherRecord, cts.Token)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> receivedSubscriberPayload = await client.UnprotectAsync(subscriberRecord, cts.Token)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.AuthenticatedPeer, Is.EqualTo(pair.Client.RemoteEndpoint));
                Assert.That(pair.Server.AuthenticatedPeer, Is.EqualTo(pair.Server.RemoteEndpoint));
                Assert.That(receivedPublisherPayload.ToArray(), Is.EqualTo(publisherPayload));
                Assert.That(receivedSubscriberPayload.ToArray(), Is.EqualTo(subscriberPayload));
                validator.Verify(v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(2));
            });
        }

        /// <summary>
        /// A matching identity cannot replace certificate trust, CertificateVerify or Finished authentication.
        /// </summary>
        [TestCase((byte)DtlsHandshakeType.Certificate)]
        [TestCase((byte)DtlsHandshakeType.CertificateVerify)]
        [TestCase((byte)DtlsHandshakeType.Finished)]
        [Category("SecurityRegression")]
        public async Task SecurityF06MatchingIdentityStillRequiresChainAndTranscriptAuthenticationAsync(
            byte rejectedMessageType)
        {
            var rejectedMessage = (DtlsHandshakeType)rejectedMessageType;
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            if (rejectedMessage == DtlsHandshakeType.Certificate)
            {
                validator.Setup(v => v.ValidateAsync(
                        It.IsAny<Certificate>(),
                        It.IsAny<TrustListIdentifier?>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CertificateValidationResult(
                        isValid: false,
                        statusCode: StatusCodes.BadCertificateInvalid,
                        errors: [new ServiceResult(StatusCodes.BadCertificateInvalid)],
                        isSuppressible: false));
            }

            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair =
                InMemoryDtlsDatagramChannel.CreatePair(serverToClientTransform: datagram =>
                {
                    if (rejectedMessage != DtlsHandshakeType.Certificate &&
                        datagram.Length > DtlsHandshakeCodec.HandshakeHeaderLength &&
                        datagram[0] == (byte)rejectedMessage)
                    {
                        datagram[^1] ^= 0xff;
                    }
                    return datagram;
                });
            using DtlsHandshakeContext client = CreateContext(
                profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using DtlsHandshakeContext server = CreateContext(
                profile, DtlsEndpointRole.Server, certificate, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();
            byte[] payload = [0x55, 0x41];

            try
            {
                if (rejectedMessage == DtlsHandshakeType.Certificate)
                {
                    await Assert.ThatAsync(
                        async () => await clientTask.ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadCertificateInvalid)).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(
                        async () => await clientTask.ConfigureAwait(false),
                        Throws.TypeOf<DtlsHandshakeException>()).ConfigureAwait(false);
                }

                Assert.That(pair.Client.AuthenticatedPeer, Is.Null);
                await Assert.ThatAsync(
                    async () => await client.ProtectAsync(payload).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>());
            }
            finally
            {
                await CancelPeerHandshakeAsync(serverTask, cts).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ServerSelectsLocalCertificateMatchingProfileCurveAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate nistP384 = CreateEcdsaCertificate(DtlsNamedCurve.NistP384);
            using Certificate nistP256 = CreateEcdsaCertificate(DtlsNamedCurve.NistP256);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair = InMemoryDtlsDatagramChannel.CreatePair();

            var clientOptions = new DtlsTransportOptions { PeerCertificateValidator = validator.Object };
            clientOptions.LocalCertificates.Add(nistP256);
            var serverOptions = new DtlsTransportOptions { PeerCertificateValidator = validator.Object };
            serverOptions.LocalCertificates.Add(nistP384);
            serverOptions.LocalCertificates.Add(nistP256);

            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, clientOptions, validator.Object);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, serverOptions, validator.Object);

            await Task.WhenAll(
                client.OpenAsync(pair.Client, CancellationToken.None).AsTask(),
                server.OpenAsync(pair.Server, CancellationToken.None).AsTask()).ConfigureAwait(false);

            byte[] payload = [0x55, 0x41];
            ReadOnlyMemory<byte> record = await client.ProtectAsync(payload, CancellationToken.None)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> plaintext = await server.UnprotectAsync(record, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(plaintext.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task ServerSelectsLocalCertificateResolvedFromIdentifierAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate clientCertificate = CreateEcdsaCertificate(profile.CertificateCurve);
            using Certificate serverCertificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            var certificateProvider = new Mock<ICertificateProvider>(MockBehavior.Strict);
            certificateProvider
                .Setup(p => p.GetPrivateKeyCertificateAsync(
                    It.Is<CertificateIdentifier>(id => id.Thumbprint == serverCertificate.Thumbprint),
                    null,
                    null,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Certificate?>(serverCertificate.AddRef()));
            var serverOptions = new DtlsTransportOptions { PeerCertificateValidator = validator.Object };
            serverOptions.LocalCertificateIdentifiers.Add(new CertificateIdentifier
            {
                Thumbprint = serverCertificate.Thumbprint
            });
            var factory = new DefaultDtlsContextFactory(
                Options.Create(serverOptions),
                new DtlsProfileRegistry(),
                validator.Object,
                certificateProvider.Object);
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair = InMemoryDtlsDatagramChannel.CreatePair();
            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, clientCertificate, validator.Object);
            using IDtlsContext server = await factory.CreateAsync(
                    new PubSubConnectionDataType { Name = "resolved-server" },
                    CreateEndpoint(profile),
                    profile,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System)
                .ConfigureAwait(false);

            await Task.WhenAll(
                client.OpenAsync(pair.Client, CancellationToken.None).AsTask(),
                server.OpenAsync(pair.Server, CancellationToken.None).AsTask()).ConfigureAwait(false);

            byte[] payload = [0x44, 0x54, 0x4c, 0x53];
            ReadOnlyMemory<byte> record = await client.ProtectAsync(payload, CancellationToken.None)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> plaintext = await server.UnprotectAsync(record, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(plaintext.ToArray(), Is.EqualTo(payload));
                certificateProvider.VerifyAll();
            });
        }

        [Test]
        public void ServerFailsClosedWhenNoLocalCertificateMatchesProfileCurve()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate nistP384 = CreateEcdsaCertificate(DtlsNamedCurve.NistP384);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair = InMemoryDtlsDatagramChannel.CreatePair();
            var serverOptions = new DtlsTransportOptions { PeerCertificateValidator = validator.Object };
            serverOptions.LocalCertificates.Add(nistP384);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, serverOptions, validator.Object);

            Assert.That(
                async () => await server.OpenAsync(pair.Server, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<DtlsHandshakeException>());
        }

        [Test]
        [TestSpec("RFC 8446 §4.1.4")]
        public void ClientAbortsAfterSecondHelloRetryRequest()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            var channel = new AlwaysHelloRetryRequestChannel(profile);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Assert.That(
                async () => await client.OpenAsync(channel, cts.Token).ConfigureAwait(false),
                Throws.TypeOf<DtlsHandshakeException>().With.Message.Contains("HelloRetryRequest"));
        }

        [Test]
        [TestSpec("RFC 8446 §4.3.2")]
        public async Task MutualAuthenticationHandshakeSucceedsWhenClientCertificateRequiredAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate clientCertificate = CreateEcdsaCertificate(profile.CertificateCurve);
            using Certificate serverCertificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair = InMemoryDtlsDatagramChannel.CreatePair();

            var clientOptions = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator.Object,
                RequireHelloRetryRequestCookie = true
            };
            clientOptions.LocalCertificates.Add(clientCertificate);
            var serverOptions = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator.Object,
                RequireHelloRetryRequestCookie = true,
                RequireClientCertificate = true
            };
            serverOptions.LocalCertificates.Add(serverCertificate);

            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, clientOptions, validator.Object);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, serverOptions, validator.Object);

            await Task.WhenAll(
                client.OpenAsync(pair.Client, CancellationToken.None).AsTask(),
                server.OpenAsync(pair.Server, CancellationToken.None).AsTask()).ConfigureAwait(false);

            byte[] payload = [0x4d, 0x41];
            ReadOnlyMemory<byte> record = await client.ProtectAsync(payload, CancellationToken.None)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> plaintext = await server.UnprotectAsync(record, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(plaintext.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        [TestSpec("RFC 8446 §4.3.2")]
        public async Task MutualAuthenticationFailsClosedWhenClientHasNoCertificateAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate serverCertificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair = InMemoryDtlsDatagramChannel.CreatePair();

            var clientOptions = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator.Object,
                RequireHelloRetryRequestCookie = true
            };
            var serverOptions = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator.Object,
                RequireHelloRetryRequestCookie = true,
                RequireClientCertificate = true
            };
            serverOptions.LocalCertificates.Add(serverCertificate);

            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, clientOptions, validator.Object);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, serverOptions, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();

            Assert.That(async () => await clientTask.ConfigureAwait(false), Throws.TypeOf<DtlsHandshakeException>());
            await cts.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await serverTask.ConfigureAwait(false), Throws.Exception);
        }

        [Test]
        public async Task ServerRejectsHandshakeCompletionFromDifferentEndpointAsync()
        {
            DtlsProfile profile = ResolveOrIgnore("ECC_nistP256_AesGcm");
            using Certificate certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            var spoofedEndpoint = new IPEndPoint(IPAddress.Loopback, 56000);
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair =
                InMemoryDtlsDatagramChannel.CreatePair(
                    clientToServerSourceSelector: (datagram, source) =>
                        datagram[0] == (byte)DtlsHandshakeType.ClientHello
                            ? source
                            : spoofedEndpoint);
            var clientOptions = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator.Object,
                RequireHelloRetryRequestCookie = true,
                InitialRetransmissionTimeout = TimeSpan.FromSeconds(1),
                MaxRetransmissionTimeout = TimeSpan.FromSeconds(1)
            };
            clientOptions.LocalCertificates.Add(certificate);
            var serverOptions = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator.Object,
                RequireHelloRetryRequestCookie = true,
                InitialRetransmissionTimeout = TimeSpan.FromMilliseconds(250),
                MaxRetransmissionTimeout = TimeSpan.FromMilliseconds(250)
            };
            serverOptions.LocalCertificates.Add(certificate);
            using DtlsHandshakeContext client = CreateContext(
                profile, DtlsEndpointRole.Client, clientOptions, validator.Object);
            using DtlsHandshakeContext server = CreateContext(
                profile, DtlsEndpointRole.Server, serverOptions, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();

            await clientTask.ConfigureAwait(false);
            Assert.That(
                async () => await serverTask.ConfigureAwait(false),
                Throws.TypeOf<DtlsHandshakeException>());
            Assert.That(pair.Server.AuthenticatedPeer, Is.Null);
        }

        /// <summary>
        /// Exercises both real handshake endpoints, directional traffic keys and ordinary duplicate rejection.
        /// </summary>
        private static async Task RunHandshakeAndApplicationRoundTripAsync(DtlsProfile profile)
        {
            using Certificate certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            Mock<ICertificateValidatorEx> validator = CreateSuccessfulValidator();
            (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) pair = InMemoryDtlsDatagramChannel.CreatePair();
            using DtlsHandshakeContext client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using DtlsHandshakeContext server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);

            await Task.WhenAll(
                client.OpenAsync(pair.Client, CancellationToken.None).AsTask(),
                server.OpenAsync(pair.Server, CancellationToken.None).AsTask()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pair.Client.AuthenticatedPeer, Is.EqualTo(pair.Client.RemoteEndpoint));
                Assert.That(pair.Server.AuthenticatedPeer, Is.EqualTo(pair.Server.RemoteEndpoint));
            });

            byte[] payload = [0x55, 0x41, 0x44, 0x50];
            ReadOnlyMemory<byte> record = await client.ProtectAsync(payload, CancellationToken.None)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> plaintext = await server.UnprotectAsync(record, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(plaintext.ToArray(), Is.EqualTo(payload));
            Assert.That(() => server.UnprotectAsync(record, CancellationToken.None).AsTask(), Throws.Exception,
                "RFC 9147 §4.5.1 replayed records must be dropped by the anti-replay window.");

            byte[] responsePayload = [0x53, 0x55, 0x42];
            ReadOnlyMemory<byte> responseRecord = await server.ProtectAsync(responsePayload, CancellationToken.None)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> response = await client.UnprotectAsync(responseRecord, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response.ToArray(), Is.EqualTo(responsePayload));
            await Assert.ThatAsync(
                () => client.UnprotectAsync(responseRecord, CancellationToken.None).AsTask(), Throws.Exception);
        }

        private static Mock<ICertificateValidatorEx> CreateSuccessfulValidator()
        {
            var validator = new Mock<ICertificateValidatorEx>(MockBehavior.Strict);
            validator.Setup(v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            return validator;
        }

        private static byte[] DowngradeServerCipherSuite(byte[] datagram)
        {
            const int serverHelloCipherSuiteOffset = DtlsHandshakeCodec.HandshakeHeaderLength + 2 + 32 + 1 + 32;
            if (datagram.Length > serverHelloCipherSuiteOffset + 1 &&
                datagram[0] == (byte)DtlsHandshakeType.ServerHello &&
                datagram[serverHelloCipherSuiteOffset] == 0x13 &&
                datagram[serverHelloCipherSuiteOffset + 1] == 0x02)
            {
                datagram[serverHelloCipherSuiteOffset + 1] = 0x01;
            }

            return datagram;
        }

        private static byte[] TamperFirstFinished(byte[] datagram)
        {
            if (datagram.Length > DtlsHandshakeCodec.HandshakeHeaderLength &&
                datagram[0] == (byte)DtlsHandshakeType.Finished)
            {
                datagram[^1] ^= 0xff;
            }

            return datagram;
        }

        private static DtlsHandshakeContext CreateContext(
            DtlsProfile profile,
            DtlsEndpointRole role,
            Certificate certificate,
            ICertificateValidatorEx validator)
        {
            var options = new DtlsTransportOptions
            {
                PeerCertificateValidator = validator,
                RequireHelloRetryRequestCookie = true
            };
            options.LocalCertificates.Add(certificate);
            return CreateContext(profile, role, options, validator);
        }

        private static DtlsHandshakeContext CreateContext(
            DtlsProfile profile,
            DtlsEndpointRole role,
            DtlsTransportOptions options,
            ICertificateValidatorEx validator)
        {
            return new DtlsHandshakeContext(
                profile,
                options,
                validator,
                role,
                CreateEndpoint(profile),
                TimeProvider.System);
        }

        private static UdpEndpoint CreateEndpoint(DtlsProfile profile)
        {
            return new UdpEndpoint(
                IPAddress.Loopback,
                4843,
                UdpAddressType.Unicast,
                "opc.dtls://localhost:4843",
                true,
                profile.Name);
        }

        /// <summary>
        /// Creates a publisher/client through the default factory without resolving or connecting a socket.
        /// </summary>
        private static ValueTask<IDtlsContext> CreateFactoryClientAsync(
            DtlsProfile profile,
            UdpEndpoint endpoint,
            ICertificateValidatorEx validator,
            Certificate? clientCertificate = null)
        {
            var options = new DtlsTransportOptions { PeerCertificateValidator = validator };
            if (clientCertificate is not null)
            {
                options.LocalCertificates.Add(clientCertificate);
            }

            var factory = new DefaultDtlsContextFactory(
                Options.Create(options), new DtlsProfileRegistry(), validator);
            return factory.CreateAsync(
                new PubSubConnectionDataType
                {
                    Name = "identity-bound-publisher",
                    WriterGroups = [new WriterGroupDataType { Name = "publisher" }]
                },
                endpoint,
                profile,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        /// <summary>
        /// Cancels and observes the peer that is still waiting after a deliberately rejected handshake.
        /// </summary>
        private static async Task CancelPeerHandshakeAsync(Task peerTask, CancellationTokenSource cancellation)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await peerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        private static DtlsProfile ResolveOrIgnore(string profileName)
        {
            var registry = new DtlsProfileRegistry();
            if (!registry.TryResolve(profileName, out DtlsProfile? profile))
            {
                Assert.Ignore($"DTLS profile '{profileName}' is not available from this platform BCL.");
            }
            return profile!;
        }

        /// <summary>
        /// Creates an ephemeral ECC certificate with independent DNS and IP subject alternative names.
        /// </summary>
        private static Certificate CreateEcdsaCertificate(
            DtlsNamedCurve curve,
            string? dnsName = "localhost",
            string? ipAddress = null,
            string commonName = "dtls-handshake")
        {
            using var ecdsa = ECDsa.Create(ToEccCurve(curve));
            var request = new CertificateRequest($"CN={commonName}", ecdsa, GetHash(curve));
            if (dnsName is not null || ipAddress is not null)
            {
                var subjectAltName = new SubjectAlternativeNameBuilder();
                if (dnsName is not null)
                {
                    subjectAltName.AddDnsName(dnsName);
                }
                if (ipAddress is not null)
                {
                    subjectAltName.AddIpAddress(IPAddress.Parse(ipAddress));
                }

                request.CertificateExtensions.Add(subjectAltName.Build());
            }

            return Certificate.From(request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10)));
        }

        private static ECCurve ToEccCurve(DtlsNamedCurve curve)
        {
            return curve switch
            {
                DtlsNamedCurve.NistP256 => ECCurve.NamedCurves.nistP256,
                DtlsNamedCurve.NistP384 => ECCurve.NamedCurves.nistP384,
                DtlsNamedCurve.BrainpoolP256r1 => ECCurve.CreateFromValue("1.3.36.3.3.2.8.1.1.7"),
                DtlsNamedCurve.BrainpoolP384r1 => ECCurve.CreateFromValue("1.3.36.3.3.2.8.1.1.11"),
                _ => throw new NotSupportedException("Unsupported test certificate curve.")
            };
        }

        private static HashAlgorithmName GetHash(DtlsNamedCurve curve)
        {
            return curve is DtlsNamedCurve.NistP384 or DtlsNamedCurve.BrainpoolP384r1
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256;
        }

        /// <summary>
        /// In-memory <see cref="IDtlsDatagramChannel"/> used to drive both ends of a DTLS handshake in tests.
        /// </summary>
        private sealed class InMemoryDtlsDatagramChannel :
            IDtlsDatagramChannel,
            IDtlsAuthenticatedPeerChannel
        {
            private InMemoryDtlsDatagramChannel(
                Channel<DtlsDatagram> inbound,
                Channel<DtlsDatagram> outbound,
                IPEndPoint localEndpoint,
                IPEndPoint remoteEndpoint,
                Func<byte[], byte[]>? outboundTransform,
                Func<byte[], IPEndPoint, IPEndPoint>? outboundSourceSelector)
            {
                m_inbound = inbound;
                m_outbound = outbound;
                m_localEndpoint = localEndpoint;
                RemoteEndpoint = remoteEndpoint;
                m_outboundTransform = outboundTransform;
                m_outboundSourceSelector = outboundSourceSelector;
            }

            /// <inheritdoc/>
            public IPEndPoint? RemoteEndpoint { get; }

            public IPEndPoint? AuthenticatedPeer { get; private set; }

            /// <summary>
            /// Creates a connected client/server channel pair backed by in-memory queues.
            /// </summary>
            public static (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) CreatePair(
                Func<byte[], byte[]>? clientToServerTransform = null,
                Func<byte[], byte[]>? serverToClientTransform = null,
                Func<byte[], IPEndPoint, IPEndPoint>? clientToServerSourceSelector = null,
                Func<byte[], IPEndPoint, IPEndPoint>? serverToClientSourceSelector = null)
            {
                var clientInbound = Channel.CreateUnbounded<DtlsDatagram>();
                var serverInbound = Channel.CreateUnbounded<DtlsDatagram>();
                var clientEndpoint = new IPEndPoint(IPAddress.Loopback, 55000);
                var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 4843);
                var client = new InMemoryDtlsDatagramChannel(
                    clientInbound,
                    serverInbound,
                    clientEndpoint,
                    serverEndpoint,
                    clientToServerTransform,
                    clientToServerSourceSelector);
                var server = new InMemoryDtlsDatagramChannel(
                    serverInbound,
                    clientInbound,
                    serverEndpoint,
                    clientEndpoint,
                    serverToClientTransform,
                    serverToClientSourceSelector);
                return (client, server);
            }

            /// <inheritdoc/>
            public ValueTask SendAsync(
                ReadOnlyMemory<byte> datagram,
                IPEndPoint? destination = null,
                CancellationToken cancellationToken = default)
            {
                _ = destination;
                byte[] copy = datagram.ToArray();
                if (m_outboundTransform is not null)
                {
                    copy = m_outboundTransform(copy);
                }

                IPEndPoint source = m_outboundSourceSelector?.Invoke(copy, m_localEndpoint)
                    ?? m_localEndpoint;
                return m_outbound.Writer.WriteAsync(
                    new DtlsDatagram(copy, source),
                    cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<DtlsDatagram> ReceiveAsync(CancellationToken cancellationToken = default)
            {
                return m_inbound.Reader.ReadAsync(cancellationToken);
            }

            void IDtlsAuthenticatedPeerChannel.SetAuthenticatedPeer(IPEndPoint peer)
            {
                AuthenticatedPeer = new IPEndPoint(peer.Address, peer.Port);
            }

            private readonly Channel<DtlsDatagram> m_inbound;
            private readonly Channel<DtlsDatagram> m_outbound;
            private readonly IPEndPoint m_localEndpoint;
            private readonly Func<byte[], byte[]>? m_outboundTransform;
            private readonly Func<byte[], IPEndPoint, IPEndPoint>? m_outboundSourceSelector;
        }

        /// <summary>
        /// Test channel that answers every ClientHello with a HelloRetryRequest so the client HRR cap
        /// (RFC 8446 §4.1.4 — at most one HelloRetryRequest) can be exercised.
        /// </summary>
        private sealed class AlwaysHelloRetryRequestChannel : IDtlsDatagramChannel
        {
            public AlwaysHelloRetryRequestChannel(DtlsProfile profile)
            {
                m_helloRetryRequest = DtlsHandshakeCodec.EncodeFrame(
                    DtlsHandshakeType.ServerHello,
                    0,
                    DtlsHandshakeCodec.EncodeServerHello(new DtlsServerHello(
                        new byte[32],
                        new byte[32],
                        profile.CipherSuite,
                        DtlsHelloExtensions.CreateDefault([profile.KeyExchangeCurve], [], new byte[16]))));
            }

            /// <inheritdoc/>
            public IPEndPoint? RemoteEndpoint => new(IPAddress.Loopback, 4843);

            /// <inheritdoc/>
            public ValueTask SendAsync(
                ReadOnlyMemory<byte> datagram,
                IPEndPoint? destination = null,
                CancellationToken cancellationToken = default)
            {
                _ = datagram;
                _ = destination;
                return default;
            }

            /// <inheritdoc/>
            public ValueTask<DtlsDatagram> ReceiveAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<DtlsDatagram>(new DtlsDatagram(m_helloRetryRequest, RemoteEndpoint));
            }

            private readonly byte[] m_helloRetryRequest;
        }
    }
}
