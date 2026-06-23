#if NET8_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Security.Certificates;

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
            DtlsProfile profile = new DtlsProfileRegistry().Resolve("ECC_nistP384_AesGcm");
            await RunHandshakeAndApplicationRoundTripAsync(profile!).ConfigureAwait(false);
        }

        [Test]
        public async Task HandshakeCompletesAndProtectsApplicationDatagramForIntegrityOnlyAsync()
        {
            DtlsProfile profile = new DtlsProfileRegistry().Resolve("ECC_nistP256");
            await RunHandshakeAndApplicationRoundTripAsync(profile!).ConfigureAwait(false);
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
            DtlsProfile profile = new DtlsProfileRegistry().Resolve("ECC_nistP384_AesGcm");
            using X509Certificate2 certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            var validator = CreateSuccessfulValidator();
            var pair = InMemoryDtlsDatagramChannel.CreatePair(serverToClientTransform: DowngradeServerCipherSuite);
            using var client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using var server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);
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
            DtlsProfile profile = new DtlsProfileRegistry().Resolve("ECC_nistP256_AesGcm");
            using X509Certificate2 certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            var validator = CreateSuccessfulValidator();
            var pair = InMemoryDtlsDatagramChannel.CreatePair(serverToClientTransform: TamperFirstFinished);
            using var client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using var server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);
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
            DtlsProfile profile = new DtlsProfileRegistry().Resolve("ECC_nistP256_AesGcm");
            using X509Certificate2 certificate = CreateEcdsaCertificate(profile.CertificateCurve);
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
            var pair = InMemoryDtlsDatagramChannel.CreatePair();
            using var client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using var server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Task clientTask = client.OpenAsync(pair.Client, cts.Token).AsTask();
            Task serverTask = server.OpenAsync(pair.Server, cts.Token).AsTask();

            Assert.That(async () => await clientTask.ConfigureAwait(false), Throws.Exception);
            await cts.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await serverTask.ConfigureAwait(false), Throws.Exception);
        }

        private static async Task RunHandshakeAndApplicationRoundTripAsync(DtlsProfile profile)
        {
            using X509Certificate2 certificate = CreateEcdsaCertificate(profile.CertificateCurve);
            var validator = CreateSuccessfulValidator();
            var pair = InMemoryDtlsDatagramChannel.CreatePair();
            using var client = CreateContext(profile, DtlsEndpointRole.Client, certificate, validator.Object);
            using var server = CreateContext(profile, DtlsEndpointRole.Server, certificate, validator.Object);

            await Task.WhenAll(
                client.OpenAsync(pair.Client, CancellationToken.None).AsTask(),
                server.OpenAsync(pair.Server, CancellationToken.None).AsTask()).ConfigureAwait(false);

            byte[] payload = [0x55, 0x41, 0x44, 0x50];
            ReadOnlyMemory<byte> record = await client.ProtectAsync(payload, CancellationToken.None).ConfigureAwait(false);
            ReadOnlyMemory<byte> plaintext = await server.UnprotectAsync(record, CancellationToken.None).ConfigureAwait(false);

            Assert.That(plaintext.ToArray(), Is.EqualTo(payload));
            Assert.That(() => server.UnprotectAsync(record, CancellationToken.None).AsTask(), Throws.Exception,
                "RFC 9147 §4.5.1 replayed records must be dropped by the anti-replay window.");
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
            if (datagram.Length > serverHelloCipherSuiteOffset + 1
                && datagram[0] == (byte)DtlsHandshakeType.ServerHello
                && datagram[serverHelloCipherSuiteOffset] == 0x13
                && datagram[serverHelloCipherSuiteOffset + 1] == 0x02)
            {
                datagram[serverHelloCipherSuiteOffset + 1] = 0x01;
            }

            return datagram;
        }

        private static byte[] TamperFirstFinished(byte[] datagram)
        {
            if (datagram.Length > DtlsHandshakeCodec.HandshakeHeaderLength
                && datagram[0] == (byte)DtlsHandshakeType.Finished)
            {
                datagram[^1] ^= 0xff;
            }

            return datagram;
        }

        private static DtlsHandshakeContext CreateContext(
            DtlsProfile profile,
            DtlsEndpointRole role,
            X509Certificate2 certificate,
            ICertificateValidatorEx validator)
        {
            var options = new DtlsTransportOptions
            {
                LocalCertificate = certificate,
                PeerCertificateValidator = validator,
                RequireHelloRetryRequestCookie = true
            };
            return new DtlsHandshakeContext(
                profile,
                options,
                validator,
                role,
                new UdpEndpoint(IPAddress.Loopback, 4843, UdpAddressType.Unicast, "opc.dtls://localhost:4843", true,
                    profile.Name),
                TimeProvider.System);
        }

        private static X509Certificate2 CreateEcdsaCertificate(DtlsNamedCurve curve)
        {
            using ECDsa ecdsa = ECDsa.Create(ToEccCurve(curve));
            var request = new CertificateRequest("CN=dtls-handshake", ecdsa, GetHash(curve));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
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

        private sealed class InMemoryDtlsDatagramChannel : IDtlsDatagramChannel
        {
            private InMemoryDtlsDatagramChannel(
                Channel<ReadOnlyMemory<byte>> inbound,
                Channel<ReadOnlyMemory<byte>> outbound,
                IPEndPoint remoteEndpoint,
                Func<byte[], byte[]>? outboundTransform)
            {
                m_inbound = inbound;
                m_outbound = outbound;
                RemoteEndpoint = remoteEndpoint;
                m_outboundTransform = outboundTransform;
            }

            public IPEndPoint? RemoteEndpoint { get; }

            public static (InMemoryDtlsDatagramChannel Client, InMemoryDtlsDatagramChannel Server) CreatePair(
                Func<byte[], byte[]>? clientToServerTransform = null,
                Func<byte[], byte[]>? serverToClientTransform = null)
            {
                Channel<ReadOnlyMemory<byte>> clientInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
                Channel<ReadOnlyMemory<byte>> serverInbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
                var client = new InMemoryDtlsDatagramChannel(
                    clientInbound,
                    serverInbound,
                    new IPEndPoint(IPAddress.Loopback, 4843),
                    clientToServerTransform);
                var server = new InMemoryDtlsDatagramChannel(
                    serverInbound,
                    clientInbound,
                    new IPEndPoint(IPAddress.Loopback, 55000),
                    serverToClientTransform);
                return (client, server);
            }

            public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
            {
                byte[] copy = datagram.ToArray();
                if (m_outboundTransform is not null)
                {
                    copy = m_outboundTransform(copy);
                }

                return m_outbound.Writer.WriteAsync(copy, cancellationToken);
            }

            public ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
            {
                return m_inbound.Reader.ReadAsync(cancellationToken);
            }

            private readonly Channel<ReadOnlyMemory<byte>> m_inbound;
            private readonly Channel<ReadOnlyMemory<byte>> m_outbound;
            private readonly Func<byte[], byte[]>? m_outboundTransform;
        }
    }
}
#endif
