using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
            var file = Convert.FromBase64String(s_keyPairPemBase64);

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
        private const string s_keyPairPemBase64 = "LS0tLS1CRUdJTiBSU0EgUFJJVkFURSBLRVktLS0tLQ0KTUlJRXBRSUJBQUtDQVFFQXEvcG45L21EQ0x1Y2pxazF4QWxIWGdyclE3UDhmUzE3RGw5ckNjVlg4U2ZORktrTA0KL0lPTVV3aVJHc2dKOUVLM2tkVVVJNkJnYVQxWWtVZ1BlazhyVzd0R3A0a2k1ckVCQmNnZ3IrbmRuL1haaHZyTA0KREhWRnk2YzhkNStrNEgxYVRMa1NzODF3T2tNUSttbXZNV2NVNWZuSGsrdkxyc2prcEdkYlYzbDlNaUU1Y0hncg0Kb25CMllvTFNSWUd6SEtERFFLVVpjL3FpWUtmU2lhY3BPZTI5UUxRdStHd29FNWRXM09EcmlXeUlnMHo5MjllWA0KaHlRUG52N2lZL3gwQk5wZmJGRXJHY2N3YWpkZi96WFNaV05tcUR2eGNJMEFhTE5GY2JhY3kyeEFjbUM3MUd4Yw0KNTZXYlNDc08yTi9RQm8yMW15SXhVeHpLb2o1R25CSXV4d3hycXdJREFRQUJBb0lCQUZCS3hSNjNyYzgyZEdZQQ0KcHpzQzBBQ2VuVytMQ1M5U1BCa2VRR1diN3E2SEZBNzR5OEZWazM2eXliaFV1NTBBUng4OWViMFdZOXpPaU5xdg0KWjVhRmZ1dEJlTC9BWFh5U0dEV2dWVzU4QS81cU5HaDZEN0dkMFB1L2RSSXVoVFpRSUFnaEFoUFRMdm5zbFBFcw0KeFdBTnplcC9Id2czTnUzQUdrdU5lODVQeUwxTmxKaldCcTl2c2ZWdG1ZREtWY1pWZDQ5N3R4blJoYm9LQjY2Lw0KZGx5bkVzSER6eE5XQTZjYTRUay85NzVGcnRhT1QxeDkzbXdzRUZkYVF1WHMrekRUd2RwczFmcVdMSjNIc2JUdw0Ka1U0ZjB1RmhHaFQzd1Z6SU9HSmZiVmVha2lTRjhIYjFjS2NGVk5LRGJtbFFvVVhqQ3I2TzhzeFA5Vk1DL0ZNbg0KTXRqYkVmMENnWUVBMjRXVkcwck1Cd3NSNXRkRGtPOTlaajBoTm56a0dYbGs5MEhUMHhVRW80Q2dVLzlOMXJEcw0KSTQxTThKU2NMUzNpZVNJdEtSZUcxUmRFZ0psWDEwOVZXMjF2NnJqMGZUb1d3SGkzUUQ1TCtIeCtxMVF1SStmNg0KTUx0S0s1M3hYK1JxUDI0TUhra241WTdTSkFSbjJCT1FjVDhCOFNqcEZVNGdPNTd6TkphNXFQMENnWUVBeUk1VA0KUG93NVROMlZkMGo2a21iT3lRaEtUbWZsS1pzdVBreFFKRCtZdjJJS0hpeFh3RTVQODFOUS9QdTBXU3hOQjkzVQ0KTUxiclliL3hhb0VCS2QraVFHT2I4VXYrTFFEWU9lbzIrOEJZNy9kRm5pMlhML2JwQlV2U0ozdkkzWUllZkxvZw0KbkZzUVlSMzV4SVp3d29rNk90aVprcmlXcGNWZHlrZVJVeTlTKzhjQ2dZRUFrajhMV0RSYmVySEFTbWJOQjZqVg0KaFNCaW1SZFpLek41dFZRd2w2YWdBWUYyenA3K3IzSU16NTZhVElqbEJ5QlRpZG5mOWtsTE5YbWIxSVRVUllmLw0KMkxvdTNsUTc1WldtaExHbmUvQkUwcFcyR2RRcUxSZWwwWU5rNVd3QzI1eWp3QUJEcUlXYVE5QURaYVZkdlZGRA0KWUg3V2YyQis4QWV0WjZyOFllT3NhczBDZ1lFQXE2K2pOWTFHMURWd2FXQXhHVGtuVmxOaGdRTlIyeTg4QkJyQw0KRkhYWTVpVWdjam9WbU11eGg2VFFWUEdJcnpuTWE2cUxwblJBeGpwUmlaSU1FL09jNnpBYVpCTmc4TmVqUXRqcw0KM3RFSGtjMkZiR2FzNFdPbWtXRVo4N0QxQUNNT3hFbDE5MFBCbnRIUmFscUlseEJ3cDhXYW1rNm9zQnBvTXV6WA0KVEhYYnZTc0NnWUVBZ1lvR3I3MmVBT3pWOGkyWGtlaG1CQyt0eG1WSXpoWk82K2lzWHdwKytGT2owU0JEY1E5eg0KNUQrVUp1L1BVTlNkSUVORkpWNVdIQkQxOVJ6dlNpRWJndnE1eWVCYnoyYnRINWRjQXhUVjEzZ3JJUVhraWlsdA0Kb1NzMFBjTG02NUdoTmR2MktXQnVlVjI0OGFhR0ZUOVoyR3hIYzNvOUF6Yis1b3VUOXdGYTlYUT0NCi0tLS0tRU5EIFJTQSBQUklWQVRFIEtFWS0tLS0tDQotLS0tLUJFR0lOIENFUlRJRklDQVRFLS0tLS0NCk1JSURWakNDQWo2Z0F3SUJBZ0lJVWJ0UGRGQUJKYTB3RFFZSktvWklodmNOQVFFTEJRQXdBREFlRncweU5UQTINCk1Ea3hNakV4TURCYUZ3MHlOakEyTURreE1qRXhNREJhTUJveEN6QUpCZ05WQkFZVEFrUkZNUXN3Q1FZRFZRUUcNCkV3SkVSVENDQVNJd0RRWUpLb1pJaHZjTkFRRUJCUUFEZ2dFUEFEQ0NBUW9DZ2dFQkFLdjZaL2Y1Z3dpN25JNnANCk5jUUpSMTRLNjBPei9IMHRldzVmYXduRlYvRW56UlNwQy95RGpGTUlrUnJJQ2ZSQ3Q1SFZGQ09nWUdrOVdKRkkNCkQzcFBLMXU3UnFlSkl1YXhBUVhJSUsvcDNaLzEyWWI2eXd4MVJjdW5QSGVmcE9COVdreTVFclBOY0RwREVQcHANCnJ6Rm5GT1g1eDVQcnk2N0k1S1JuVzFkNWZUSWhPWEI0SzZKd2RtS0Mwa1dCc3h5Z3cwQ2xHWFA2b21DbjBvbW4NCktUbnR2VUMwTHZoc0tCT1hWdHpnNjRsc2lJTk0vZHZYbDRja0Q1Nys0bVA4ZEFUYVgyeFJLeG5ITUdvM1gvODENCjBtVmpacWc3OFhDTkFHaXpSWEcybk10c1FISmd1OVJzWE9lbG0wZ3JEdGpmMEFhTnRac2lNVk1jeXFJK1Jwd1MNCkxzY01hNnNDQXdFQUFhT0J1VENCdGpBTUJnTlZIUk1CQWY4RUFqQUFNQjBHQTFVZERnUVdCQlNUeXM0ZW9lZXMNCmY1NUpJamNXTFREaWJ4QVpYVEFmQmdOVkhTTUVHREFXZ0JSYjF0bEc0alF6UVozaTg2aDdqVldFcFBVUFlEQUwNCkJnTlZIUThFQkFNQ0ErZ3dFd1lEVlIwbEJBd3dDZ1lJS3dZQkJRVUhBd0V3RVFZRFZSMFJCQW93Q0lJR1kyOXcNCmVXTnVNQkVHQ1dDR1NBR0crRUlCQVFRRUF3SUdRREFlQmdsZ2hrZ0JodmhDQVEwRUVSWVBlR05oSUdObGNuUnANClptbGpZWFJsTUEwR0NTcUdTSWIzRFFFQkN3VUFBNElCQVFCb05qUzQ2SGRBUE1wUVR5c2ZxNFlSVG9NZHRwK04NCnRzVElSYmNaSDBMZFV5S00zbVR2bUovVmZ3RTlpSGUrT2NPaXNha25HMG1RaG5uR2ptNGhVYnorZW1SWDlGRVMNClZkSVh4alhvMnNOTGpjcEhWL1UrRUtSdk9Tb3l2OTBhVnFiTERCOEVSYzQrZUxqcTZTeWZBS1ZnMUxnK3NGRTcNCkNUUUk0Qk5XUTJseG9FaGd5UEhmZnZ2Ris0NUl6M2dVcUUwd3poV0hYRVFiVi9BVHJBRjVoZVZiVWdacCtsbjMNCnAydG9HVnowYnRJRW1FTmVvcmJYVUJBdHZMMjFWaThqc08vcmp6TEVUUVZ6UGlZV3JzMEtiaVBYY3pHWFk5czgNCkdjVjZSbGxjNmdBckQ4aHk3SVlIVGpwaVcyN2VxVENCeWdhQ2VvY2xBTmdQUnFFeU5obUZ2OWl1DQotLS0tLUVORCBDRVJUSUZJQ0FURS0tLS0tDQo=";

    }
}
