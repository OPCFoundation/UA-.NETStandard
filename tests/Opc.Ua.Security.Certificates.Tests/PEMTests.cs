using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Tests;

#if !NET8_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture]
    [Category("PEM")]
    public class PEMTests
    {
        [Test]
        public void ImportCertificateChainFromPem()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert
                    .Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            // Arrange
            byte[] file = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain.pem").First());

            // Act
            X509Certificate2Collection certs = PEMReader.ImportPublicKeysFromPEM(file);

            // Assert
            Assert.That(certs, Is.Not.Null, "Certificates collection should not be null.");
            Assert.That(certs, Is.Not.Empty, "Certificates collection should not be empty.");
            Assert.That(certs, Has.Count.EqualTo(3), "Expected 3 certificates in the collection.");
            Assert.That(
                certs.Find(X509FindType.FindBySerialNumber, "029D603370C20AE2", false)[0],
                Is.Not.Null);
            Assert.That(
                certs.Find(X509FindType.FindBySerialNumber, "6E4385A67BDE4505", false)[0],
                Is.Not.Null);
            X509Certificate2 leaf = certs.Find(
                X509FindType.FindBySerialNumber,
                "51BB4F74500125AD",
                false)[0];
            Assert.That(leaf, Is.Not.Null);

            //Act
            Assert.That(
                PEMReader.ContainsPrivateKey(file),
                Is.False,
                "PEM file should not contain a private key.");

            // Remove leaf certificate from the collection
            Assert.That(
                PEMWriter.TryRemovePublicKeyFromPEM(leaf.Thumbprint, file, out byte[] updatedFile),
                Is.True);

            Assert.That(updatedFile, Is.Not.Null, "Updated PEM file should not be null.");
            X509Certificate2Collection updatedCerts = PEMReader.ImportPublicKeysFromPEM(
                updatedFile);
            Assert.That(updatedCerts, Is.Not.Null, "Certificates collection should not be null.");
            Assert.That(updatedCerts, Is.Not.Empty, "Certificates collection should not be empty.");
            Assert.That(updatedCerts, Has.Count.EqualTo(2), "Expected 2 certificates in the collection.");
            //root
            Assert.That(
                updatedCerts.Find(X509FindType.FindBySerialNumber, "029D603370C20AE2", false)[0],
                Is.Not.Null);
            //intermediate
            Assert.That(
                updatedCerts.Find(X509FindType.FindBySerialNumber, "6E4385A67BDE4505", false)[0],
                Is.Not.Null);
            // leaf
            Assert.That(
                updatedCerts.Find(X509FindType.FindBySerialNumber, "51BB4F74500125AD", false)
,
                Is.Empty);
        }

        [Test]
        public void ImportPublicPrivateKeyPairFromPEM()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert
                    .Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            // Arrange
            byte[] file = DecryptKeyPairPemBase64();

            // Act
            X509Certificate2Collection certs = PEMReader.ImportPublicKeysFromPEM(file);

            // Assert
            Assert.That(certs, Is.Not.Null, "Certificates collection should not be null.");
            Assert.That(certs, Is.Not.Empty, "Certificates collection should not be empty.");
            Assert.That(certs, Has.Count.EqualTo(1), "Expected 1 certificate in the collection.");
            X509Certificate2 leaf = certs.Find(
                X509FindType.FindBySerialNumber,
                "51BB4F74500125AD",
                false)[0];
            Assert.That(leaf, Is.Not.Null);

            //Act
            Assert.That(
                PEMReader.ContainsPrivateKey(file),
                Is.True,
                "PEM file should contain a private key.");

            Certificate newCert = null;
            try
            {
                using var leafCert = Certificate.FromRawData(leaf.RawData);
                newCert = DefaultCertificateFactory.Instance.CreateWithPEMPrivateKey(
                    leafCert, file);

                Assert.That(newCert, Is.Not.Null, "New certificate with private key should not be null.");
                Assert.That(newCert.HasPrivateKey, Is.True, "New certificate should have a private key.");
            }
            finally
            {
                newCert?.Dispose(); // Dispose the certificate to release resources
            }
        }

        /// <summary>
        /// A certificate whose subject or issuer is an empty distinguished name
        /// identifies nothing, so it is skipped by the reader on every target
        /// framework rather than being handed to a trust decision.
        /// </summary>
        [Test]
        public void ImportCertificateChainWithEmptyDistinguishedNamesReturnsNoCertificates()
        {
            byte[] file = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain_empty_dn.pem").First());

            X509Certificate2Collection certs = PEMReader.ImportPublicKeysFromPEM(file);

            Assert.That(certs, Is.Not.Null);
            Assert.That(certs, Is.Empty, "Certificates with an empty DN must not be imported.");
        }

        /// <summary>
        /// One unparseable entry must not discard the rest of the file: the
        /// valid certificates around it are still returned.
        /// </summary>
        [Test]
        public void ImportCertificateChainSkipsOnlyTheEntriesWithEmptyDistinguishedNames()
        {
            byte[] valid = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain.pem").First());
            byte[] empty = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain_empty_dn.pem").First());

            // Interleave so a rejected entry is both the first block and between
            // two good ones.
            byte[] mixed = [.. empty, .. valid];

            X509Certificate2Collection certs = PEMReader.ImportPublicKeysFromPEM(mixed);

            Assert.That(certs, Has.Count.EqualTo(3), "The well formed chain must survive.");
            foreach (X509Certificate2 cert in certs)
            {
                Assert.That(
                    DistinguishedNameUtils.HasEmptyDistinguishedName(cert),
                    Is.False);
            }
        }

        /// <summary>
        /// Verifies the empty distinguished name detection itself.
        /// </summary>
        [Test]
        public void HasEmptyDistinguishedNameDetectsEmptyNames()
        {
            byte[] empty = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain_empty_dn.pem").First());
            byte[] valid = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain.pem").First());

            using X509Certificate2 emptyDnCertificate = LoadFirstCertificate(empty);
            using X509Certificate2 validCertificate = LoadFirstCertificate(valid);

            Assert.Multiple(() =>
            {
                Assert.That(
                    DistinguishedNameUtils.HasEmptyDistinguishedName(emptyDnCertificate),
                    Is.True);
                Assert.That(
                    DistinguishedNameUtils.HasEmptyDistinguishedName(validCertificate),
                    Is.False);
                Assert.That(DistinguishedNameUtils.IsEmpty(null), Is.True);
                Assert.That(
                    DistinguishedNameUtils.IsEmpty(emptyDnCertificate.IssuerName),
                    Is.True);
                Assert.That(
                    DistinguishedNameUtils.IsEmpty(validCertificate.SubjectName),
                    Is.False);
            });
        }

        /// <summary>
        /// Loads the first certificate of a PEM file without going through the
        /// reader under test.
        /// </summary>
        private static X509Certificate2 LoadFirstCertificate(byte[] pem)
        {
            string text = System.Text.Encoding.UTF8.GetString(pem);
            const string begin = "-----BEGIN CERTIFICATE-----";
            const string end = "-----END CERTIFICATE-----";
            int start = text.IndexOf(begin, StringComparison.Ordinal) + begin.Length;
            int stop = text.IndexOf(end, StringComparison.Ordinal);
            byte[] der = Convert.FromBase64String(
                new string([.. text[start..stop].Where(c => !char.IsWhiteSpace(c))]));
            return X509CertificateLoader.LoadCertificate(der);
        }

        /// <summary>
        /// A block the PEM parser rejects must not hide the private key that
        /// follows it. On .NET Framework BouncyCastle 2.7.0 throws on an empty
        /// issuer name, which used to abandon the rest of the file and silently
        /// cost an application certificate its private key.
        /// </summary>
        [Test]
        public void ContainsPrivateKeyLooksPastUnparseableBlocks()
        {
            byte[] emptyDn = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain_empty_dn.pem").First());
            byte[] keyPair = DecryptKeyPairPemBase64();

            byte[] combined = [.. emptyDn, .. keyPair];

            Assert.That(
                PEMReader.ContainsPrivateKey(combined),
                Is.True,
                "the private key after the rejected blocks must still be found.");
        }

        /// <summary>
        /// Same for the private key import itself.
        /// </summary>
        [Test]
        public void ImportPrivateKeyLooksPastUnparseableBlocks()
        {
            byte[] emptyDn = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain_empty_dn.pem").First());
            byte[] keyPair = DecryptKeyPairPemBase64();

            byte[] combined = [.. emptyDn, .. keyPair];

            using RSA rsa = PEMReader.ImportRsaPrivateKeyFromPEM(combined, ReadOnlySpan<char>.Empty);

            Assert.That(rsa, Is.Not.Null);
            Assert.That(rsa.KeySize, Is.GreaterThan(0));
        }

        /// <summary>
        /// A private key that follows a long run of certificates must still be
        /// found. The reader caps the certificates it returns, but that cap must
        /// not stop it scanning for the key.
        /// </summary>
        [Test]
        public void ContainsPrivateKeyLooksPastMoreCertificatesThanItWouldReturn()
        {
            byte[] chain = File.ReadAllBytes(
                TestUtils.EnumerateTestAssets("Test_chain.pem").First());
            byte[] keyPair = DecryptKeyPairPemBase64();

            // 40 copies of a three certificate chain is 120 certificates, well
            // past the 99 the importer is willing to return.
            byte[] combined =
            [
                .. Enumerable.Repeat(chain, 40).SelectMany(c => c),
                .. keyPair
            ];

            Assert.Multiple(() =>
            {
                Assert.That(
                    PEMReader.ImportPublicKeysFromPEM(combined),
                    Has.Count.EqualTo(99),
                    "the importer still caps what it returns.");
                Assert.That(
                    PEMReader.ContainsPrivateKey(combined),
                    Is.True,
                    "the key past the cap must still be found.");
            });
        }

        private const string kKeyPairPemBase64Encrypted =
            "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";

        /// <summary>
        /// 16 bytes for AES-128
        /// </summary>
        private static readonly byte[] s_aesKey =
        [
            0x13,
            0x5e,
            0xcf,
            0xdd,
            0x96,
            0xf2,
            0x99,
            0x63,
            0x9e,
            0x2d,
            0x50,
            0x1c,
            0x3a,
            0xbb,
            0xde,
            0x02
        ];

        /// <summary>
        /// 16 bytes
        /// </summary>
        private static readonly byte[] s_aesIV =
        [
            0xFE,
            0xDC,
            0xBA,
            0x98,
            0x76,
            0x54,
            0x32,
            0x10,
            0xEF,
            0xCD,
            0xAB,
            0x89,
            0x67,
            0x45,
            0x23,
            0x01
        ];

        private static byte[] DecryptKeyPairPemBase64()
        {
            byte[] encryptedBytes = Convert.FromBase64String(kKeyPairPemBase64Encrypted);
            using var aes = Aes.Create();
            aes.Key = s_aesKey;
            aes.IV = s_aesIV;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(encryptedBytes);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var output = new MemoryStream();
            cs.CopyTo(output);
            return output.ToArray();
        }
    }
}
