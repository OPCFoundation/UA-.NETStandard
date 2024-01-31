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

        private string _path;

        public CertificateGroupTests()
        {
            _path = Utils.ReplaceSpecialFolderNames("%LocalApplicationData%/OPC/GDS/TestStore");
        }

        public void Dispose()
        {
            Directory.Delete(_path, true);
        }
        #region Test Methods

        [Test]
        public void TestCreateCACertificateAsyncThrowsException()
        {
            var configuration = new CertificateGroupConfiguration();
            configuration.SubjectName = "CN=GDS Test CA, O=OPC Foundation";
            configuration.BaseStorePath = _path;
            var certificateGroup = new CertificateGroup().Create(_path + "/authorities", configuration);
            Assert.That(() => certificateGroup.CreateCACertificateAsync("This is not the ValidSubjectName for my CertificateGroup"), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task TestCreateCACertificateAsyncCertIsInTrustedStoreAsync()
        {
            var configuration = new CertificateGroupConfiguration();
            configuration.SubjectName = "CN=GDS Test CA, O=OPC Foundation";
            configuration.BaseStorePath = _path;
            var certificateGroup = new CertificateGroup().Create(_path + "/authorities", configuration);
            var certificate = await certificateGroup.CreateCACertificateAsync(configuration.SubjectName).ConfigureAwait(false);
            Assert.NotNull(certificate);
            using (ICertificateStore trustedStore = CertificateStoreIdentifier.OpenStore(configuration.TrustedListPath))
            {
                X509Certificate2Collection certs = await trustedStore.FindByThumbprint(certificate.Thumbprint).ConfigureAwait(false);
                Assert.IsTrue(certs.Count == 1);
            }
        }

        [Test]
        public async Task TestCreateCACertificateAsyncCertIsInTrustedIssuerStoreAsync()
        {
            var applicatioConfiguration = new ApplicationConfiguration();
            applicatioConfiguration.SecurityConfiguration = new SecurityConfiguration();
            applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath = _path + "/issuers";
            applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StoreType = "Directory";
            var cgConfiguration = new CertificateGroupConfiguration();
            cgConfiguration.SubjectName = "CN=GDS Test CA, O=OPC Foundation";
            cgConfiguration.BaseStorePath = _path;
            var certificateGroup = new CertificateGroup().Create(_path + "/authorities", cgConfiguration, applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath);
            var certificate = await certificateGroup.CreateCACertificateAsync(cgConfiguration.SubjectName).ConfigureAwait(false);
            Assert.NotNull(certificate);
            using (ICertificateStore trustedStore = CertificateStoreIdentifier.OpenStore(applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath))
            {
                X509Certificate2Collection certs = await trustedStore.FindByThumbprint(certificate.Thumbprint).ConfigureAwait(false);
                Assert.IsTrue(certs.Count == 1);
            }
        }
        #endregion
    }
}
