#if NET8_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 certificate authentication from RFC 8446 §4.4.2-§4.4.3.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 8446 §4.4.2")]
    [TestSpec("RFC 8446 §4.4.3")]
    public sealed class DtlsCertificateAuthenticatorTests
    {
        [Test]
        public void CertificateMessageRoundTripsAndCertificateVerifyValidates()
        {
            using X509Certificate2 certificate = CreateEcdsaCertificate();
            byte[] transcriptHash = SHA256.HashData(new byte[] { 0x01, 0x02, 0x03 });

            byte[] certificateMessage = DtlsCertificateAuthenticator.EncodeCertificate([certificate]);
            var decoded = DtlsCertificateAuthenticator.DecodeCertificate(certificateMessage);
            byte[] verifyBody = DtlsCertificateAuthenticator.SignCertificateVerify(
                certificate,
                DtlsCipherSuite.TlsAes128GcmSha256,
                transcriptHash);

            Assert.Multiple(() =>
            {
                Assert.That(decoded[0].RawData, Is.EqualTo(certificate.RawData));
                Assert.That(() => DtlsCertificateAuthenticator.VerifyCertificateVerify(
                    decoded[0],
                    DtlsCipherSuite.TlsAes128GcmSha256,
                    transcriptHash,
                    verifyBody,
                    isServer: true), Throws.Nothing);
            });
        }

        [Test]
        public void TamperedCertificateVerifyFailsClosed()
        {
            using X509Certificate2 certificate = CreateEcdsaCertificate();
            byte[] transcriptHash = SHA256.HashData(new byte[] { 0x01, 0x02 });
            byte[] verifyBody = DtlsCertificateAuthenticator.SignCertificateVerify(
                certificate,
                DtlsCipherSuite.TlsAes128GcmSha256,
                transcriptHash);
            verifyBody[^1] ^= 0xff;

            Assert.That(() => DtlsCertificateAuthenticator.VerifyCertificateVerify(
                certificate,
                DtlsCipherSuite.TlsAes128GcmSha256,
                transcriptHash,
                verifyBody,
                isServer: true), Throws.TypeOf<DtlsHandshakeException>());
        }

        [Test]
        public void RsaCertificateIsRejectedForCertificateVerify()
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=dtls-rsa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(10));

            Assert.That(() => DtlsCertificateAuthenticator.SignCertificateVerify(
                certificate,
                DtlsCipherSuite.TlsAes128GcmSha256,
                SHA256.HashData(Array.Empty<byte>())), Throws.TypeOf<DtlsHandshakeException>());
        }

        [Test]
        public async Task PeerCertificateValidationUsesInjectedValidatorAsync()
        {
            using X509Certificate2 certificate = CreateEcdsaCertificate();
            var validator = new Mock<ICertificateValidatorEx>(MockBehavior.Strict);
            validator.Setup(v => v.ValidateAsync(
                    It.IsAny<Opc.Ua.Security.Certificates.Certificate>(),
                    It.IsAny<Opc.Ua.Security.Certificates.TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);

            await DtlsCertificateAuthenticator.ValidatePeerCertificateAsync(
                validator.Object,
                [certificate],
                CancellationToken.None).ConfigureAwait(false);

            validator.VerifyAll();
        }

        private static X509Certificate2 CreateEcdsaCertificate()
        {
            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=dtls-ecdsa", ecdsa, HashAlgorithmName.SHA256);
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        }
    }
}
#endif

