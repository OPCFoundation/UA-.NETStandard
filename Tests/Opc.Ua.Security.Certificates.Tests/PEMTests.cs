using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // Arrange
            var file = File.ReadAllBytes(TestUtils.EnumerateTestAssets("Test_keyPair.pem").First());


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
    }
}
