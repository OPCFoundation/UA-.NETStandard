using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [SetCulture("en-us")]
    public class CertificateStoreTypeTest
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            CertificateStoreType.RegisterCertificateStoreType("testStoreType", new TestStoreType());
        }

        [Test]
        public async Task CertificateStoreTypeConfigTestAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fileInfo = new FileInfo(
                Path.Combine(
                    TestContext.CurrentContext.TestDirectory,
                    "Security",
                    "Certificates",
                    "CertificateStoreTypeTestConfig.xml"));
            ApplicationConfiguration appConfig = await ApplicationConfiguration
                .LoadAsync(fileInfo, ApplicationType.Client, null)
                .ConfigureAwait(false);
            int instancesCreatedWhileLoadingConfig = TestCertStore.InstancesCreated;
            Assert.IsTrue(instancesCreatedWhileLoadingConfig > 0);
            CertificateTrustList trustedIssuers = appConfig.SecurityConfiguration
                .TrustedIssuerCertificates;
            using ICertificateStore trustedIssuersStore = trustedIssuers.OpenStore(telemetry);
            trustedIssuersStore.Close();
            int instancesCreatedWhileOpeningAuthRootStore = TestCertStore.InstancesCreated;
            Assert.IsTrue(
                instancesCreatedWhileLoadingConfig < instancesCreatedWhileOpeningAuthRootStore);

            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                TestCertStore.StoreTypePrefix + @"CurrentUser\Disallowed");
            using ICertificateStore store = certificateStoreIdentifier.OpenStore(telemetry);
            Assert.IsTrue(
                instancesCreatedWhileOpeningAuthRootStore < TestCertStore.InstancesCreated);
        }
    }

    internal sealed class TestStoreType : ICertificateStoreType
    {
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new TestCertStore(telemetry);
        }

        public bool SupportsStorePath(string storePath)
        {
            return storePath != null && storePath.StartsWith(TestCertStore.StoreTypePrefix);
        }
    }

    internal sealed class TestCertStore : ICertificateStore
    {
        public TestCertStore(ITelemetryContext telemetry)
        {
            s_instancesCreated++;
            m_innerStore = new X509CertificateStore(telemetry);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_innerStore.Dispose();
        }

        /// <inheritdoc/>
        public void Open(string location, bool noPrivateKeys)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            if (!location.StartsWith(StoreTypePrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Expected argument {nameof(location)} starting with {StoreTypePrefix}");
            }
            m_innerStore.Open(location[StoreTypePrefix.Length..], noPrivateKeys);
        }

        /// <inheritdoc/>
        public void Close()
        {
            m_innerStore.Close();
        }

        /// <inheritdoc/>
        public string StoreType => StoreTypePrefix[..^1];

        /// <inheritdoc/>
        public string StorePath => m_innerStore.StorePath;

        /// <inheritdoc/>
        public bool NoPrivateKeys => m_innerStore.NoPrivateKeys;

        /// <inheritdoc/>
        public Task Add(X509Certificate2 certificate, string password = null)
        {
            return m_innerStore.AddAsync(certificate, password);
        }

        /// <inheritdoc/>
        public Task AddAsync(
            X509Certificate2 certificate,
            string password = null,
            CancellationToken ct = default)
        {
            return m_innerStore.AddAsync(certificate, password, ct);
        }

        /// <inheritdoc/>
        public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            return m_innerStore.DeleteAsync(thumbprint, ct);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> Enumerate()
        {
            return m_innerStore.EnumerateAsync();
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> EnumerateAsync(CancellationToken ct = default)
        {
            return m_innerStore.EnumerateAsync(ct);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            return m_innerStore.FindByThumbprintAsync(thumbprint, ct);
        }

        /// <inheritdoc/>
        public bool SupportsCRLs => m_innerStore.SupportsCRLs;

        /// <inheritdoc/>
        public Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            return m_innerStore.AddCRLAsync(crl, ct);
        }

        /// <inheritdoc/>
        public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            return m_innerStore.DeleteCRLAsync(crl, ct);
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            return m_innerStore.EnumerateCRLsAsync(ct);
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(
            X509Certificate2 issuer,
            bool validateUpdateTime = true,
            CancellationToken ct = default)
        {
            return m_innerStore.EnumerateCRLsAsync(issuer, validateUpdateTime, ct);
        }

        /// <inheritdoc/>
        public Task<StatusCode> IsRevokedAsync(
            X509Certificate2 issuer,
            X509Certificate2 certificate,
            CancellationToken ct = default)
        {
            return m_innerStore.IsRevokedAsync(issuer, certificate, ct);
        }

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => m_innerStore.SupportsLoadPrivateKey;

        /// <inheritdoc/>
        public Task<X509Certificate2> LoadPrivateKeyAsync(
            string thumbprint,
            string subjectName,
            string applicationUri,
            NodeId certificateType,
            string password,
            CancellationToken ct = default)
        {
            return m_innerStore.LoadPrivateKeyAsync(
                thumbprint,
                subjectName,
                applicationUri,
                certificateType,
                password,
                ct);
        }

        /// <inheritdoc/>
        public Task AddRejectedAsync(
            X509Certificate2Collection certificates,
            int maxCertificates,
            CancellationToken ct = default)
        {
            return m_innerStore.AddRejectedAsync(certificates, maxCertificates, ct);
        }

        public static int InstancesCreated => s_instancesCreated;

        internal const string StoreTypePrefix = "testStoreType:";
        private readonly X509CertificateStore m_innerStore;
        private static volatile int s_instancesCreated;
    }
}
