using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture, Category("PEM")]
    public class PEMTests
    {
        [Test]
        public void ImportCertificateChainFromPem()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            // Arrange
            var file = File.ReadAllBytes(TestUtils.EnumerateTestAssets("Test_chain.pem").First());


            // Act
            var certs = PEMReader.ImportPublicKeysFromPEM(file);

            // Assert
            Assert.IsNotNull(certs, "Certificates collection should not be null.");
            Assert.IsNotEmpty(certs, "Certificates collection should not be empty.");
            Assert.AreEqual(3, certs.Count, "Expected 3 certificates in the collection.");
            Assert.NotNull(certs.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySerialNumber, "029D603370C20AE2", false)[0]);
            Assert.NotNull(certs.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySerialNumber, "6E4385A67BDE4505", false)[0]);
            var leaf = certs.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySerialNumber, "51BB4F74500125AD", false)[0];
            Assert.NotNull(leaf);

            //Act
            Assert.False(PEMReader.ContainsPrivateKey(file), "PEM file should not contain a private key.");

            // Remove leaf certificate from the collection
            Assert.True(PEMWriter.TryRemovePublicKeyFromPEM(leaf.Thumbprint, file, out var updatedFile));

            Assert.IsNotNull(updatedFile, "Updated PEM file should not be null.");
            var updatedCerts = PEMReader.ImportPublicKeysFromPEM(updatedFile);
            Assert.IsNotNull(updatedCerts, "Certificates collection should not be null.");
            Assert.IsNotEmpty(updatedCerts, "Certificates collection should not be empty.");
            Assert.AreEqual(2, updatedCerts.Count, "Expected 2 certificates in the collection.");
            //root
            Assert.NotNull(updatedCerts.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySerialNumber, "029D603370C20AE2", false)[0]);
            //intermediate
            Assert.NotNull(updatedCerts.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySerialNumber, "6E4385A67BDE4505", false)[0]);
            // leaf
            Assert.AreEqual(0, updatedCerts.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySerialNumber, "51BB4F74500125AD", false).Count);
        }

        [Test]
        public void ImportPublicPrivateKeyPairFromPEM()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            // Arrange
            var file = DecryptKeyPairPemBase64();

            // Act
            var certs = PEMReader.ImportPublicKeysFromPEM(file);

            // Assert
            Assert.IsNotNull(certs, "Certificates collection should not be null.");
            Assert.IsNotEmpty(certs, "Certificates collection should not be empty.");
            Assert.AreEqual(1, certs.Count, "Expected 1 certificate in the collection.");
            var leaf = certs.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindBySerialNumber, "51BB4F74500125AD", false)[0];
            Assert.NotNull(leaf);


            //Act
            Assert.True(PEMReader.ContainsPrivateKey(file), "PEM file should contain a private key.");

            X509Certificate2 newCert = null;
            try
            {
                newCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(leaf, file);


                Assert.NotNull(newCert, "New certificate with private key should not be null.");
                Assert.True(newCert.HasPrivateKey, "New certificate should have a private key.");
            }
            finally
            {
                newCert?.Dispose(); // Dispose the certificate to release resources
            }
        }

        private const string s_keyPairPemBase64Encrypted = "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";
        private static readonly byte[] s_aesKey = new byte[] { 0x13, 0x5e, 0xcf, 0xdd, 0x96, 0xf2, 0x99, 0x63, 0x9e, 0x2d, 0x50, 0x1c, 0x3a, 0xbb, 0xde, 0x02 }; // 16 bytes for AES-128
        private static readonly byte[] s_aesIV = new byte[] { 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01 }; // 16 bytes

        private static byte[] DecryptKeyPairPemBase64()
        {
            var encryptedBytes = Convert.FromBase64String(s_keyPairPemBase64Encrypted);
            using var aes = Aes.Create();
            aes.Key = s_aesKey;
            aes.IV = s_aesIV;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(encryptedBytes);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var output = new MemoryStream();
            cs.CopyTo(output);
            return output.ToArray();
        }
    }
}
