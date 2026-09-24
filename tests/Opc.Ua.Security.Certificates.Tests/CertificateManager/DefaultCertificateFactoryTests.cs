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

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class DefaultCertificateFactoryTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(16)]
        public void ParseChainBlobAcceptsPssSignatureWithoutHashAlgorithmPrevalidation(int certificateCount)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=PSS Signed Peer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            using X509Certificate2 pss = request.CreateSelfSigned(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using Certificate pkcs1 = CertificateBuilder.Create("CN=PKCS1 Peer").CreateForRSA();
            var blob = new List<byte>();
            for (int index = 0; index < certificateCount; index++)
            {
                blob.AddRange(index % 2 == 0 ? pss.RawData : pkcs1.RawData);
            }

            using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(blob.ToArray());

            Assert.That(chain, Has.Count.EqualTo(certificateCount));
            for (int index = 0; index < certificateCount; index++)
            {
                Assert.That(chain[index].RawData, Is.EqualTo(index % 2 == 0 ? pss.RawData : pkcs1.RawData));
                Assert.That(chain[index].SignatureAlgorithm.Value,
                    Is.EqualTo(index % 2 == 0 ? "1.2.840.113549.1.1.10" : pkcs1.SignatureAlgorithm.Value));
            }
        }

#if NETFRAMEWORK
        [TestCase(false)]
        [TestCase(true)]
        public void ParseChainBlobAcceptsEdDsaSignatureWithoutHashAlgorithmPrevalidation(bool ed448)
        {
            var random = new Org.BouncyCastle.Security.SecureRandom();
            Org.BouncyCastle.Crypto.IAsymmetricCipherKeyPairGenerator generator = ed448
                ? new Org.BouncyCastle.Crypto.Generators.Ed448KeyPairGenerator()
                : new Org.BouncyCastle.Crypto.Generators.Ed25519KeyPairGenerator();
            generator.Init(ed448
                ? new Org.BouncyCastle.Crypto.Parameters.Ed448KeyGenerationParameters(random)
                : new Org.BouncyCastle.Crypto.Parameters.Ed25519KeyGenerationParameters(random));
            Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair key = generator.GenerateKeyPair();
            var builder = new Org.BouncyCastle.X509.X509V3CertificateGenerator();
            var subject = new Org.BouncyCastle.Asn1.X509.X509Name("CN=EdDSA Signed Peer");
            builder.SetSerialNumber(Org.BouncyCastle.Math.BigInteger.One);
            builder.SetIssuerDN(subject);
            builder.SetSubjectDN(subject);
            builder.SetNotBefore(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            builder.SetNotAfter(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            builder.SetPublicKey(key.Public);
            byte[] encoded = builder.Generate(new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory(
                ed448 ? "Ed448" : "Ed25519", key.Private, random)).GetEncoded();
            using var supported = new X509Certificate2(encoded);
            using Certificate rsa = CertificateBuilder.Create("CN=RSA Chain Suffix").CreateForRSA();
            byte[] blob = [.. encoded, .. rsa.RawData];

            using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(blob);

            Assert.That(chain, Has.Count.EqualTo(2));
            Assert.That(chain[0].RawData, Is.EqualTo(supported.RawData));
            Assert.That(chain[0].SignatureAlgorithm.Value, Is.EqualTo(ed448 ? "1.3.101.113" : "1.3.101.112"));
            Assert.That(chain[1].RawData, Is.EqualTo(rsa.RawData));
        }
#endif

        [Test]
        public void CreateFromRawDataCreatesValidCertificate()
        {
            var factory = new DefaultCertificateFactory();

            using Certificate original = CertificateBuilder
                .Create("CN=TestCert")
                .CreateForRSA();

            using Certificate result = factory.CreateFromRawData(original.RawData);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Thumbprint, Is.EqualTo(original.Thumbprint));
        }

        [Test]
        public void ParseChainBlobParsesMultipleCerts()
        {
            var factory = new DefaultCertificateFactory();

            using Certificate cert1 = CertificateBuilder
                .Create("CN=Cert1")
                .CreateForRSA();
            using Certificate cert2 = CertificateBuilder
                .Create("CN=Cert2")
                .CreateForRSA();

            byte[] blob = new byte[cert1.RawData.Length + cert2.RawData.Length];
            Buffer.BlockCopy(cert1.RawData, 0, blob, 0, cert1.RawData.Length);
            Buffer.BlockCopy(cert2.RawData, 0, blob, cert1.RawData.Length, cert2.RawData.Length);

            using CertificateCollection chain = factory.ParseChainBlob(blob);

            Assert.That(chain, Has.Count.EqualTo(2));
            Assert.That(chain[0].Thumbprint, Is.EqualTo(cert1.Thumbprint));
            Assert.That(chain[1].Thumbprint, Is.EqualTo(cert2.Thumbprint));
        }

        [Test]
        public void CreateCertificateReturnsBuilder()
        {
            var factory = new DefaultCertificateFactory();

            ICertificateBuilder builder = factory.CreateCertificate("CN=Test");

            Assert.That(builder, Is.Not.Null);

            using Certificate cert = builder.CreateForRSA();

            Assert.That(cert, Is.Not.Null);
            Assert.That(cert.Subject, Does.Contain("CN=Test"));
        }

        [Test]
        public void CreateApplicationCertificateIncludesSAN()
        {
            var factory = new DefaultCertificateFactory();

            ICertificateBuilder builder = factory.CreateApplicationCertificate(
                "urn:test:app",
                "TestApp",
                "CN=TestApp,O=Test",
                ["localhost", "testhost"]);

            using Certificate cert = builder.CreateForRSA();

            Assert.That(cert, Is.Not.Null);

            X509SubjectAltNameExtension sanExtension =
                cert.FindExtension<X509SubjectAltNameExtension>();

            Assert.That(sanExtension, Is.Not.Null);
            Assert.That(sanExtension.Uris, Does.Contain("urn:test:app"));
            Assert.That(sanExtension.DomainNames, Does.Contain("localhost"));
        }

        [Test]
        public void CreateSigningRequestReturnsBytes()
        {
            var factory = new DefaultCertificateFactory();

            using Certificate cert = CertificateBuilder
                .Create("CN=CSRTest")
                .AddExtension(
                    new X509SubjectAltNameExtension(
                        "urn:test:csr",
                        ["localhost"]))
                .CreateForRSA();

            byte[] csr = factory.CreateSigningRequest(cert);

            Assert.That(csr, Is.Not.Null);
            Assert.That(csr, Is.Not.Empty);
        }

        [Test]
        public void CreateWithPEMPrivateKeyWorks()
        {
            var factory = new DefaultCertificateFactory();

            using Certificate certWithKey = CertificateBuilder
                .Create("CN=PEMTest")
                .CreateForRSA();

            Assert.That(certWithKey.HasPrivateKey, Is.True);

            byte[] pemBlob = PEMWriter.ExportPrivateKeyAsPEM(certWithKey);

            // Create a public-only cert from raw data
            using var publicOnly = Certificate.FromRawData(certWithKey.RawData);
            Assert.That(publicOnly.HasPrivateKey, Is.False);

            using Certificate result = factory.CreateWithPEMPrivateKey(publicOnly, pemBlob);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasPrivateKey, Is.True);
        }

        [Test]
        public void CreateWithPrivateKeyWorks()
        {
            var factory = new DefaultCertificateFactory();

            using Certificate certWithKey = CertificateBuilder
                .Create("CN=PrivKeyTest")
                .CreateForRSA();

            // Create a public-only cert from raw data
            using var publicOnly = Certificate.FromRawData(certWithKey.RawData);
            Assert.That(publicOnly.HasPrivateKey, Is.False);

            using Certificate result = factory.CreateWithPrivateKey(publicOnly, certWithKey);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasPrivateKey, Is.True);
            Assert.That(result.Thumbprint, Is.EqualTo(certWithKey.Thumbprint));
        }
    }
}
