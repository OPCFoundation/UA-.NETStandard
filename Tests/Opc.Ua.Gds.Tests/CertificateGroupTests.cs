using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

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
            Assert.That(
                () => certificateGroup.CreateCACertificateAsync(
                    "This is not the ValidSubjectName for my CertificateGroup",
                    certificateGroup.CertificateTypes[0]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
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
            using Certificate certificate = await certificateGroup
                .CreateCACertificateAsync(
                    configuration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.That(certificate, Is.Not.Null);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            using ICertificateStore trustedStore = certificateStoreIdentifier.OpenStore(telemetry);
            using CertificateCollection certs = await trustedStore
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(certs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Test_Ca_Signed_Cert_Can_Be_RevokedAsync()
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
            using Certificate certificate = await certificateGroup
                .CreateCACertificateAsync(
                    configuration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.That(certificate, Is.Not.Null);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            var certificateStoreIdentifier2 = new CertificateStoreIdentifier(
                configuration.BaseStorePath);
            ICertificateGroup otherCertGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            using ICertificateStore authStore = otherCertGroup.AuthoritiesStore.OpenStore(telemetry);
            using CertificateCollection authStoreCerts = authStore.EnumerateAsync().GetAwaiter().GetResult();

            Certificate firstAuthStoreCert = authStoreCerts[0];
            var id = new CertificateIdentifier
            {
                Thumbprint = firstAuthStoreCert.Thumbprint,
                StorePath = authStore.StorePath,
                StoreType = authStore.StoreType
            };
            using Certificate authCert = id.LoadPrivateKeyAsync(null).GetAwaiter().GetResult();
            using ICertificateStore trustedStore = certificateStoreIdentifier.OpenStore(telemetry);
            using CertificateCollection storeCerts = trustedStore.EnumerateAsync().GetAwaiter().GetResult();
            using CertificateCollection certs = await trustedStore
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(certs, Is.Not.Empty);
            using Certificate signedCert = CertificateBuilder.Create("CN=signedCert")
               .SetIssuer(authCert)
               .CreateForRSA();
            await trustedStore.AddAsync(signedCert).ConfigureAwait(false);
            X509CRL crl = await certificateGroup.RevokeCertificateAsync(signedCert).ConfigureAwait(false);
            Assert.That(crl, Is.Not.Null);
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
            ICertificateGroup certificateGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            ICertificateStore store = certificateStoreIdentifier.OpenStore(telemetry);
            ICertificateStore authStore = certificateGroup.AuthoritiesStore.OpenStore(telemetry);
            try
            {
                X509CRLCollection crls = await store.EnumerateCRLsAsync().ConfigureAwait(false);
                if (crls != null)
                {
                    foreach (X509CRL crl in crls)
                    {
                        await store.DeleteCRLAsync(crl).ConfigureAwait(false);
                    }
                }
                using Certificate certificate = await certificateGroup
                    .CreateCACertificateAsync(
                        configuration.SubjectName,
                        certificateGroup.CertificateTypes[0])
                    .ConfigureAwait(false);
                Assert.That(certificate, Is.Not.Null);
                crls = await store.EnumerateCRLsAsync().ConfigureAwait(false);
                Assert.That(crls, Is.Not.Null);
                Assert.That(crls, Has.Count.EqualTo(1));
                X509CRL crl2 = await CertificateGroup
                    .LoadCrlCreateEmptyIfNonExistantAsync(certificate, certificateStoreIdentifier, telemetry)
                    .ConfigureAwait(false);
                Assert.That(crl2, Is.Not.Null);
                Assert.That(crls[0].RawData.SequenceEqual(crl2.RawData));
                var id = new CertificateIdentifier
                {
                    Thumbprint = certificate.Thumbprint,
                    StorePath = authStore.StorePath,
                    StoreType = authStore.StoreType
                };
                using Certificate authCert = id.LoadPrivateKeyAsync(null).GetAwaiter().GetResult();
                using Certificate signedCert = CertificateBuilder.Create("CN=signedCert")
               .SetIssuer(authCert)
               .CreateForRSA();
                await store.AddAsync(signedCert).ConfigureAwait(false);
                X509CRL crlWithRevokedCert = await certificateGroup.RevokeCertificateAsync(signedCert).ConfigureAwait(false);
                Assert.That(crlWithRevokedCert, Is.Not.Null);
                Assert.That(crlWithRevokedCert.RevokedCertificates.Any(el => el.SerialNumber == signedCert.SerialNumber));
                X509CRL crlWithRevokedCert2 = await CertificateGroup
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
            using Certificate ca = CertificateBuilder.Create("CN=TestCA").SetCAConstraint().CreateForRSA();
            X509CRL crl = await CertificateGroup.CreateEmptyCrlAsync(ca).ConfigureAwait(false);
            Assert.That(crl, Is.Not.Null);
            Assert.That(crl.RevokedCertificates, Is.Empty);
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
            using Certificate certificate = await certificateGroup
                .CreateCACertificateAsync(
                    configuration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.That(certificate, Is.Not.Null);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            var certificateStoreIdentifier2 = new CertificateStoreIdentifier(
                configuration.BaseStorePath);
            ICertificateGroup otherCertGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            using ICertificateStore authStore = otherCertGroup.AuthoritiesStore.OpenStore(telemetry);
            using CertificateCollection authStoreCerts = authStore.EnumerateAsync().GetAwaiter().GetResult();

            Certificate firstAuthStoreCert = authStoreCerts[0];
            var id = new CertificateIdentifier
            {
                Thumbprint = firstAuthStoreCert.Thumbprint,
                StorePath = authStore.StorePath,
                StoreType = authStore.StoreType
            };
            using Certificate authCert = id.LoadPrivateKeyAsync(null).GetAwaiter().GetResult();
            using ICertificateStore trustedStore = certificateStoreIdentifier.OpenStore(telemetry);
            using CertificateCollection storeCerts = trustedStore.EnumerateAsync().GetAwaiter().GetResult();
            using CertificateCollection certs = await trustedStore
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(certs, Is.Not.Empty);
            using Certificate signedCACert = CertificateBuilder.Create("CN=signedCert")
                .SetCAConstraint()
                .SetIssuer(authCert)
               .CreateForRSA();
            await trustedStore.AddAsync(signedCACert).ConfigureAwait(false);
            X509CRL crl = await certificateGroup.RevokeCertificateAsync(signedCACert).ConfigureAwait(false);
            Assert.That(crl, Is.Not.Null);
            Assert.That(crl.RevokedCertificates.Any(el => el.SerialNumber == signedCACert.SerialNumber));
        }

        [Test]
        public async Task Test_Root_CA_throws_Exception()
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
            using Certificate certificate = await certificateGroup
                .CreateCACertificateAsync(
                    configuration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.That(certificate, Is.Not.Null);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.TrustedListPath);
            var certificateStoreIdentifier2 = new CertificateStoreIdentifier(
                configuration.BaseStorePath);
            ICertificateGroup otherCertGroup = new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            using ICertificateStore authStore = otherCertGroup.AuthoritiesStore.OpenStore(telemetry);
            using CertificateCollection authStoreCerts = authStore.EnumerateAsync().GetAwaiter().GetResult();

            Certificate firstAuthStoreCert = authStoreCerts[0];
            var id = new CertificateIdentifier
            {
                Thumbprint = firstAuthStoreCert.Thumbprint,
                StorePath = authStore.StorePath,
                StoreType = authStore.StoreType
            };
            using Certificate authCert = id.LoadPrivateKeyAsync(null).GetAwaiter().GetResult();
            using ICertificateStore trustedStore = certificateStoreIdentifier.OpenStore(telemetry);
            using CertificateCollection storeCerts = trustedStore.EnumerateAsync().GetAwaiter().GetResult();
            using CertificateCollection certs = await trustedStore
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(certs, Is.Not.Empty);
            using Certificate signedCACert = CertificateBuilder.Create("CN=signedCert")
                .SetCAConstraint()
                .SetIssuer(authCert)
               .CreateForRSA();
            await trustedStore.AddAsync(signedCACert).ConfigureAwait(false);
            ServiceResultException exc = Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.RevokeCertificateAsync(authCert).ConfigureAwait(false));
            Assert.That(exc.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(exc.Message.Contains("Cannot revoke", StringComparison.Ordinal));
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
            using Certificate certificate = await certificateGroup
                .CreateCACertificateAsync(
                    cgConfiguration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.That(certificate, Is.Not.Null);
            using (
                ICertificateStore trustedStore =
                    applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates
                        .OpenStore(telemetry))
            {
                using CertificateCollection certs = await trustedStore
                    .FindByThumbprintAsync(certificate.Thumbprint)
                    .ConfigureAwait(false);
                Assert.That(certs, Has.Count.EqualTo(1));
                X509CRLCollection crls = await trustedStore.EnumerateCRLsAsync(certificate)
                    .ConfigureAwait(false);
                Assert.That(crls, Has.Count.EqualTo(1));
            }

            using Certificate certificateUpdated = await certificateGroup
                .CreateCACertificateAsync(
                    cgConfiguration.SubjectName,
                    certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            Assert.That(certificateUpdated, Is.Not.Null);
            using (
                ICertificateStore trustedStore =
                    applicatioConfiguration.SecurityConfiguration.TrustedIssuerCertificates
                        .OpenStore(telemetry))
            {
                using CertificateCollection certs = await trustedStore
                    .FindByThumbprintAsync(certificate.Thumbprint)
                    .ConfigureAwait(false);
                Assert.That(certs, Has.Count.EqualTo(1));
                X509CRLCollection crls = await trustedStore
                    .EnumerateCRLsAsync(certificateUpdated)
                    .ConfigureAwait(false);
                Assert.That(crls, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void TestUnknownCertificateTypeStringThrowsNotImplemented()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // A certificate type string that is not a well-known OPC UA type name should throw NotImplementedException
            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=GDS Custom CA, O=OPC Foundation",
                BaseStorePath = m_path,
                CertificateTypes = ["NotAKnownCertificateType"]
            };

            Assert.That(
                () => new CertificateGroup(telemetry).Create(m_path + "/authorities", configuration),
                Throws.TypeOf<NotImplementedException>());
        }
    }
}
