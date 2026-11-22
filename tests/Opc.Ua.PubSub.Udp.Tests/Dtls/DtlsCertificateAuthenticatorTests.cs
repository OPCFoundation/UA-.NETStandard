// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Security.Certificates;

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
            using Certificate certificate = CreateEcdsaCertificate();
            byte[] transcriptHash = DtlsHkdf.HashData(HashAlgorithmName.SHA256, [0x01, 0x02, 0x03]);

            byte[] certificateMessage = DtlsCertificateAuthenticator.EncodeCertificate([certificate]);
            using CertificateCollection decoded =
                DtlsCertificateAuthenticator.DecodeCertificate(certificateMessage);
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
        public void DecodeCertificateDisposesEveryDecodedHandle()
        {
            using Certificate first = CreateEcdsaCertificate();
            using Certificate second = CreateEcdsaCertificate();
            byte[] certificateMessage = DtlsCertificateAuthenticator.EncodeCertificate([first, second]);

            long liveBefore = Certificate.InstancesCreated - Certificate.InstancesDisposed;
            using (CertificateCollection decoded =
                DtlsCertificateAuthenticator.DecodeCertificate(certificateMessage))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(decoded, Has.Count.EqualTo(2));
                    Assert.That(decoded[0].RawData, Is.EqualTo(first.RawData));
                    Assert.That(decoded[1].RawData, Is.EqualTo(second.RawData));
                });
            }

            long liveAfter = Certificate.InstancesCreated - Certificate.InstancesDisposed;
            Assert.That(
                liveAfter,
                Is.EqualTo(liveBefore),
                "Disposing the decoded chain must release every Certificate handle it created.");
        }

        [Test]
        public void TamperedCertificateVerifyFailsClosed()
        {
            using Certificate certificate = CreateEcdsaCertificate();
            byte[] transcriptHash = DtlsHkdf.HashData(HashAlgorithmName.SHA256, [0x01, 0x02]);
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
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=dtls-rsa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = Certificate.From(request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(10)));

            Assert.That(() => DtlsCertificateAuthenticator.SignCertificateVerify(
                certificate,
                DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsHkdf.HashData(HashAlgorithmName.SHA256, [])), Throws.TypeOf<DtlsHandshakeException>());
        }

        [Test]
        public async Task PeerCertificateValidationUsesInjectedValidatorAsync()
        {
            using Certificate certificate = CreateEcdsaCertificate();
            using CertificateCollection chain = [certificate];
            var validator = new Mock<ICertificateValidatorEx>(MockBehavior.Strict);
            validator.Setup(v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);

            await DtlsCertificateAuthenticator.ValidatePeerCertificateAsync(
                validator.Object,
                chain,
                CancellationToken.None).ConfigureAwait(false);

            validator.VerifyAll();
        }

        private static Certificate CreateEcdsaCertificate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=dtls-ecdsa", ecdsa, HashAlgorithmName.SHA256);
            return Certificate.From(request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10)));
        }
    }
}
