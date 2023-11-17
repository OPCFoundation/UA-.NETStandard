using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture, Category("GDS")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class CertificateGroupTests
    {
        #region Test Methods

        [Test]
        public void TestCreateCACertificateAsyncThrowsException()
        {
            var configuration = new CertificateGroupConfiguration();
            configuration.SubjectName = "CN=GDS Test CA, O=OPC Foundation";
            var certificateGroup = new CertificateGroup().Create("./TestStore", configuration);
            Assert.That(() => certificateGroup.CreateCACertificateAsync("This is not the ValidSubjectName for my CertificateGroup"), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task TestCreateCACertificateAsyncCertIsInTrustedStoreAsync()
        {
            var configuration = new CertificateGroupConfiguration();
            configuration.SubjectName = "CN=GDS Test CA, O=OPC Foundation";
            var certificateGroup = new CertificateGroup().Create("./TestStore", configuration);
            var certificate = await certificateGroup.CreateCACertificateAsync(configuration.SubjectName).ConfigureAwait(false);
            Assert.NotNull(certificate);
            using (ICertificateStore trustedStore = CertificateStoreIdentifier.OpenStore(configuration.TrustedListPath))
            {
                X509Certificate2Collection certs = await trustedStore.FindByThumbprint(certificate.Thumbprint).ConfigureAwait(false);
                Assert.IsTrue(certs.Count == 1);
            }
            Directory.Delete("./TestStore", true);
        }
        #endregion
    }
}
