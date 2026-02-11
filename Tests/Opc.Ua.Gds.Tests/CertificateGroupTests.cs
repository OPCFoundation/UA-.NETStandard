using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CertificateGroupTests
    {
        private string m_path;

        [SetUp]
        public void Setup()
        {
            m_path = Path.Combine(Path.GetTempPath(), "OPC", "GDS", "TestStore");
        }

        [TearDown]
        public void Dispose()
        {
            if (Directory.Exists(m_path))
            {
                Directory.Delete(m_path, true);
            }
        }

        [Test]
        public void TestCreateCACertificateAsyncThrowsException()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                BaseStorePath = m_path,
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            ICertificateGroup certificateGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            NUnit.Framework.Assert.That(
                () => certificateGroup.CreateCACertificateAsync(
                    "This is not the ValidSubjectName for my CertificateGroup",
                    certificateGroup.CertificateTypes[0]),
                Throws.TypeOf<ArgumentException>());
            NUnit.Framework.Assert.That(
                () => certificateGroup.CreateCACertificateAsync(configuration.SubjectName, default),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task TestCreateCACertificateAsyncCertIsInTrustedStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                BaseStorePath = m_path,
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            ICertificateGroup certificateGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            X509Certificate2 certificate = await certificateGroup
                .CreateCACertificateAsync(
                    configuration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.NotNull(certificate);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            using ICertificateStore trustedStore = certificateStoreIdentifier.OpenStore(telemetry);
            X509Certificate2Collection certs = await trustedStore
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.IsTrue(certs.Count == 1);
        }

        [Test]
        public async Task Test_Ca_Signed_Cert_Can_Be_Revoked()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                BaseStorePath = m_path,
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            ICertificateGroup certificateGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            X509Certificate2 certificate = await certificateGroup
                .CreateCACertificateAsync(
                    configuration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.NotNull(certificate);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            var certificateStoreIdentifier2 = new CertificateStoreIdentifier(
                configuration.BaseStorePath);
            ICertificateGroup otherCertGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            using var authStore = otherCertGroup.AuthoritiesStore.OpenStore(telemetry);
            var authStoreCerts = authStore.EnumerateAsync().GetAwaiter().GetResult();    

            var firstAuthStoreCert = authStoreCerts[0];
            var id = new CertificateIdentifier
            {
                Thumbprint = firstAuthStoreCert.Thumbprint,
                StorePath = authStore.StorePath,
                StoreType = authStore.StoreType
            };
            var authCert = id.LoadPrivateKeyAsync(null).GetAwaiter().GetResult();
            using ICertificateStore trustedStore = certificateStoreIdentifier.OpenStore(telemetry);
            var storeCerts = trustedStore.EnumerateAsync().GetAwaiter().GetResult();
            X509Certificate2Collection certs = await trustedStore
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.IsTrue(certs.Count >= 1);
            var signedCert = CertificateBuilder.Create("CN=signedCert")
               .SetIssuer(authCert)
               .CreateForRSA();
            await trustedStore.AddAsync(signedCert).ConfigureAwait(false);
            var crl = await certificateGroup.RevokeCertificateAsync(signedCert).ConfigureAwait(false);
            Assert.NotNull(crl);
            Assert.That(crl.RevokedCertificates.Any(el => el.SerialNumber == signedCert.SerialNumber));
        }

        [Test]
        public async Task Test_Ca_Empty_Crl_Can_Be_Created_And_Is_Loaded()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                BaseStorePath = m_path,
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            var certificateGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            var store = certificateStoreIdentifier.OpenStore(telemetry);
            var authStore = certificateGroup.AuthoritiesStore.OpenStore(telemetry);
            try
            {
                var crls = await store.EnumerateCRLsAsync().ConfigureAwait(false);
                if(crls != null)
                {
                    foreach (var crl in crls)
                    {
                        await store.DeleteCRLAsync(crl).ConfigureAwait(false);
                    }
                }
                X509Certificate2 certificate = await certificateGroup
                    .CreateCACertificateAsync(
                        configuration.SubjectName,
                        certificateGroup.CertificateTypes[0])
                    .ConfigureAwait(false);
                Assert.NotNull(certificate);
                crls = await store.EnumerateCRLsAsync().ConfigureAwait(false);
                Assert.IsNotNull(crls);
                Assert.That(crls.Count, Is.EqualTo(1));
                var crl2 = await CertificateGroup
                    .LoadCrlCreateEmptyIfNonExistantAsync(certificate, certificateStoreIdentifier, telemetry)
                    .ConfigureAwait(false);
                Assert.IsNotNull(crl2);
                Assert.That(crls.First().RawData.SequenceEqual(crl2.RawData));
                var id = new CertificateIdentifier
                {
                    Thumbprint = certificate.Thumbprint,
                    StorePath = authStore.StorePath,
                    StoreType = authStore.StoreType
                };
                var authCert = id.LoadPrivateKeyAsync(null).GetAwaiter().GetResult();
                var signedCert = CertificateBuilder.Create("CN=signedCert")
               .SetIssuer(authCert)
               .CreateForRSA();
                await store.AddAsync(signedCert).ConfigureAwait(false);
                var crlWithRevokedCert = await certificateGroup.RevokeCertificateAsync(signedCert).ConfigureAwait(false);
                Assert.NotNull(crlWithRevokedCert);
                Assert.That(crlWithRevokedCert.RevokedCertificates.Any(el => el.SerialNumber == signedCert.SerialNumber));
                var crlWithRevokedCert2 = await CertificateGroup
                    .LoadCrlCreateEmptyIfNonExistantAsync(certificate, certificateStoreIdentifier, telemetry)
                    .ConfigureAwait(false);
                Assert.That(crlWithRevokedCert.RawData.SequenceEqual(crlWithRevokedCert2.RawData));
            }
            finally
            {
                authStore.Close();
                store.Close();
            }
        }

        [Test]
        public async Task Test_Ca_Empty_Crl_Can_Be_Created()
        {
            var ca = CertificateBuilder.Create("CN=TestCA").SetCAConstraint().CreateForRSA();
            var crl = await CertificateGroup.CreateEmptyCrlAsync(ca).ConfigureAwait(false);
            Assert.That(crl, Is.Not.Null);
            Assert.That(crl.RevokedCertificates.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task Test_Issuer_CA_Can_Be_Revoked()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                BaseStorePath = m_path,
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            ICertificateGroup certificateGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            X509Certificate2 certificate = await certificateGroup
                .CreateCACertificateAsync(
                    configuration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.NotNull(certificate);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            var certificateStoreIdentifier2 = new CertificateStoreIdentifier(
                configuration.BaseStorePath);
            ICertificateGroup otherCertGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            using var authStore = otherCertGroup.AuthoritiesStore.OpenStore(telemetry);
            var authStoreCerts = authStore.EnumerateAsync().GetAwaiter().GetResult();

            var firstAuthStoreCert = authStoreCerts[0];
            var id = new CertificateIdentifier
            {
                Thumbprint = firstAuthStoreCert.Thumbprint,
                StorePath = authStore.StorePath,
                StoreType = authStore.StoreType
            };
            var authCert = id.LoadPrivateKeyAsync(null).GetAwaiter().GetResult();
            using ICertificateStore trustedStore = certificateStoreIdentifier.OpenStore(telemetry);
            var storeCerts = trustedStore.EnumerateAsync().GetAwaiter().GetResult();
            X509Certificate2Collection certs = await trustedStore
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.IsTrue(certs.Count >= 1);
            var signedCACert = CertificateBuilder.Create("CN=signedCert")
                .SetCAConstraint()
                .SetIssuer(authCert)
               .CreateForRSA();
            await trustedStore.AddAsync(signedCACert).ConfigureAwait(false);
            var crl = await certificateGroup.RevokeCertificateAsync(signedCACert).ConfigureAwait(false);
            Assert.NotNull(crl);
            Assert.That(crl.RevokedCertificates.Any(el => el.SerialNumber == signedCACert.SerialNumber));
        }

        [Test]
        public async Task TestCreateCACertificateAsyncCertIsInTrustedIssuerStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicatioConfiguration = new ApplicationConfiguration(telemetry)
            {
                SecurityConfiguration = new SecurityConfiguration()
            };
            applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath =
                m_path + Path.DirectorySeparatorChar + "issuers";
            applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StoreType =
                CertificateStoreType.Directory;
            var cgConfiguration = new CertificateGroupConfiguration
            {
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)],
                SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                BaseStorePath = m_path
            };
            ICertificateGroup certificateGroup = new CertificateGroup(telemetry).Create(
                m_path + Path.DirectorySeparatorChar + "authorities",
                cgConfiguration,
                applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath);
            X509Certificate2 certificate = await certificateGroup
                .CreateCACertificateAsync(
                    cgConfiguration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.NotNull(certificate);
            using (
                ICertificateStore trustedStore =
                    applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates
                        .OpenStore(telemetry))
            {
                X509Certificate2Collection certs = await trustedStore
                    .FindByThumbprintAsync(certificate.Thumbprint)
                    .ConfigureAwait(false);
                Assert.IsTrue(certs.Count == 1);
                X509CRLCollection crls = await trustedStore.EnumerateCRLsAsync(certificate)
                    .ConfigureAwait(false);
                Assert.AreEqual(1, crls.Count);
            }

            X509Certificate2 certificateUpdated = await certificateGroup
                .CreateCACertificateAsync(
                    cgConfiguration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.NotNull(certificateUpdated);
            using (
                ICertificateStore trustedStore =
                    applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates
                        .OpenStore(telemetry))
            {
                X509Certificate2Collection certs = await trustedStore
                    .FindByThumbprintAsync(certificate.Thumbprint)
                    .ConfigureAwait(false);
                Assert.IsTrue(certs.Count == 1);
                X509CRLCollection crls = await trustedStore
                    .EnumerateCRLsAsync(certificateUpdated)
                    .ConfigureAwait(false);
                Assert.AreEqual(1, crls.Count);
            }
        }
    }
}
