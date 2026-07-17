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
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Direct unit tests for
    /// <see cref="AdditionalEntropyCertificateKeyGenerator"/>, verifying that
    /// the caller-supplied §7.10.10 nonce is genuinely incorporated into the
    /// generated private key rather than merely validated for length.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class AdditionalEntropyCertificateKeyGeneratorTests
    {
        private static readonly DateTime s_notBefore = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_notAfter = new(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string[] s_domainNames = ["localhost"];

        private static AdditionalEntropyCertificateKeyGenerator CreateGenerator(byte[] fixedServerEntropy)
        {
            return new AdditionalEntropyCertificateKeyGenerator(
                DefaultCertificateFactory.Instance,
                count =>
                {
                    var buffer = new byte[count];
                    for (int i = 0; i < count; i++)
                    {
                        buffer[i] = fixedServerEntropy[i % fixedServerEntropy.Length];
                    }

                    return buffer;
                });
        }

        private static PushCertificateKeyGenerationRequest CreateRequest(NodeId certificateType, byte[] nonce)
        {
            return new PushCertificateKeyGenerationRequest
            {
                CertificateTypeId = certificateType,
                ApplicationUri = "urn:opcua:test:keygen",
                ApplicationName = "KeyGen Test",
                SubjectName = "CN=KeyGen Test",
                DomainNames = ArrayOf.Wrapped(s_domainNames),
                KeySizeInBits = 2048,
                NotBefore = s_notBefore,
                NotAfter = s_notAfter,
                AdditionalEntropy = ByteString.From(nonce)
            };
        }

        private static byte[] RsaModulus(Certificate certificate)
        {
            using RSA rsa = certificate.GetRSAPublicKey();
            Assert.That(rsa, Is.Not.Null);
            return rsa.ExportParameters(false).Modulus;
        }

        private static byte[] CreateNonce(byte seed = 0x5A)
        {
            var nonce = new byte[32];
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)(seed + i);
            }

            return nonce;
        }

        [Test]
        public void GeneratedRsaCertificateHasAWorkingPrivateKey()
        {
            AdditionalEntropyCertificateKeyGenerator generator = CreateGenerator(new byte[] { 1, 2, 3, 4 });
            byte[] nonce = CreateNonce();

            using Certificate certificate = generator.CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonce));

            Assert.That(certificate.HasPrivateKey, Is.True);

            byte[] data = [10, 20, 30, 40];
            using RSA privateKey = certificate.GetRSAPrivateKey();
            byte[] signature = privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using RSA publicKey = certificate.GetRSAPublicKey();
            Assert.That(
                publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.True);
            Assert.That(RsaModulus(certificate), Has.Length.EqualTo(256), "expected a 2048-bit RSA key");
        }

        [Test]
        public void SameServerEntropyAndSameNonceProduceTheSameRsaKey()
        {
            byte[] serverEntropy = [9, 8, 7, 6, 5];
            var nonce = new byte[32];
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)i;
            }

            using Certificate first = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonce));
            using Certificate second = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonce));

            Assert.That(RsaModulus(first), Is.EqualTo(RsaModulus(second)));
        }

        [Test]
        public void DifferentNonceProducesADifferentRsaKeyForTheSameServerEntropy()
        {
            byte[] serverEntropy = [4, 4, 4, 4];
            var nonceA = new byte[32];
            var nonceB = new byte[32];
            nonceB[0] = 1;

            using Certificate certificateA = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonceA));
            using Certificate certificateB = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonceB));

            Assert.That(
                RsaModulus(certificateA),
                Is.Not.EqualTo(RsaModulus(certificateB)),
                "the caller nonce must genuinely change the private key");
        }

        [Test]
        public void ARequestSigningRequestCanBeProducedFromTheGeneratedCertificate()
        {
            AdditionalEntropyCertificateKeyGenerator generator = CreateGenerator(new byte[] { 5, 6, 7 });
            byte[] nonce = CreateNonce(0x11);

            using Certificate certificate = generator.CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonce));

            byte[] csr = DefaultCertificateFactory.Instance.CreateSigningRequest(
                certificate,
                X509Utils.GetDomainsFromCertificate(certificate).ToArray());

            Assert.That(csr, Is.Not.Null.And.Length.GreaterThan(0));
        }

#if NET5_0_OR_GREATER
        [Test]
        public void GeneratedEccCertificateHasAWorkingPrivateKey()
        {
            AdditionalEntropyCertificateKeyGenerator generator = CreateGenerator(new byte[] { 2, 2, 2, 2 });
            byte[] nonce = CreateNonce(0x33);

            using Certificate certificate = generator.CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.EccNistP256ApplicationCertificateType, nonce));

            Assert.That(certificate.HasPrivateKey, Is.True);

            byte[] data = [11, 22, 33];
            using ECDsa privateKey = certificate.GetECDsaPrivateKey();
            byte[] signature = privateKey.SignData(data, HashAlgorithmName.SHA256);
            using ECDsa publicKey = certificate.GetECDsaPublicKey();
            Assert.That(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256), Is.True);
        }

        [Test]
        public void DifferentNonceProducesADifferentEccKeyForTheSameServerEntropy()
        {
            byte[] serverEntropy = [3, 1, 4, 1, 5];
            var nonceA = new byte[32];
            var nonceB = new byte[32];
            nonceB[5] = 0xAA;

            using Certificate certificateA = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.EccNistP256ApplicationCertificateType, nonceA));
            using Certificate certificateB = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.EccNistP256ApplicationCertificateType, nonceB));

            using ECDsa publicA = certificateA.GetECDsaPublicKey();
            using ECDsa publicB = certificateB.GetECDsaPublicKey();
            byte[] qxA = publicA.ExportParameters(false).Q.X;
            byte[] qxB = publicB.ExportParameters(false).Q.X;
            Assert.That(qxA, Is.Not.EqualTo(qxB), "the caller nonce must genuinely change the ECC private key");
        }
#else
        [Test]
        public void EccRegenerateKeyThrowsBadNotSupportedWhenAdditionalEntropyCannotBeIncorporated()
        {
            // On .NET Framework / netstandard2.1 the platform cannot import a
            // private-only EC scalar, so the caller-supplied §7.10.10 nonce
            // cannot be genuinely incorporated into an ECC key. The generator
            // must fail explicitly with Bad_NotSupported rather than silently
            // return a key that ignores the mandated additional entropy.
            AdditionalEntropyCertificateKeyGenerator generator = CreateGenerator(new byte[] { 2, 2, 2, 2 });
            byte[] nonce = CreateNonce(0x44);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                generator.CreateApplicationCertificate(
                    CreateRequest(ObjectTypeIds.EccNistP256ApplicationCertificateType, nonce)));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void RsaRegenerateKeyRemainsNonceDerivedOnNetFramework()
        {
            // RSA key regeneration must genuinely incorporate the nonce on every
            // target framework, including .NET Framework / netstandard2.1.
            byte[] serverEntropy = [7, 7, 7, 7];
            var nonceA = new byte[32];
            var nonceB = new byte[32];
            nonceB[3] = 0x5C;

            using Certificate certificateA = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonceA));
            using Certificate certificateB = CreateGenerator(serverEntropy).CreateApplicationCertificate(
                CreateRequest(ObjectTypeIds.RsaSha256ApplicationCertificateType, nonceB));

            Assert.That(certificateA.HasPrivateKey, Is.True);
            Assert.That(
                RsaModulus(certificateA),
                Is.Not.EqualTo(RsaModulus(certificateB)),
                "the caller nonce must genuinely change the RSA private key on .NET Framework");
        }
#endif
    }
}
