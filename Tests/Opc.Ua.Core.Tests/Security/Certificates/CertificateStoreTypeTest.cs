using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture, Category("CertificateStore")]
    [SetCulture("en-us")]
    public class CertificateStoreTypeTest
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            CertificateStoreType.RegisterCertificateStoreType("testStoreType", new TestStoreType());
        }

        #region Test Methods
        [Test]
        public async Task CertifcateStoreTypeConfigTest()
        {
            var fileInfo = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Security", "Certificates", "CertificateStoreTypeTestConfig.xml"));
            var appConfig = await ApplicationConfiguration.Load(fileInfo, ApplicationType.Client, null).ConfigureAwait(false);
            int instancesCreatedWhileLoadingConfig = TestCertStore.InstancesCreated;
            Assert.IsTrue(instancesCreatedWhileLoadingConfig > 0);
            var trustedIssuers = appConfig.SecurityConfiguration.TrustedIssuerCertificates;
            ICertificateStore trustedIssuersStore = trustedIssuers.OpenStore();
            trustedIssuersStore.Close();
            int instancesCreatedWhileOpeningAuthRootStore = TestCertStore.InstancesCreated;
            Assert.IsTrue(instancesCreatedWhileLoadingConfig < instancesCreatedWhileOpeningAuthRootStore);
            CertificateStoreIdentifier.OpenStore(TestCertStore.StoreTypePrefix + @"CurrentUser\Disallowed");
            Assert.IsTrue(instancesCreatedWhileOpeningAuthRootStore < TestCertStore.InstancesCreated);
        }
        #endregion Test Methods
    }

    internal sealed class TestStoreType : ICertificateStoreType
    {
        public ICertificateStore CreateStore()
        {
            return new TestCertStore();
        }

        public bool SupportsStorePath(string storePath)
        {
            return storePath != null && storePath.StartsWith(TestCertStore.StoreTypePrefix);
        }
    }

    internal sealed class TestCertStore : ICertificateStore
    {
        public TestCertStore()
        {
            s_instancesCreated++;
            m_innerStore = new X509CertificateStore();
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
                throw new ArgumentException($"Expected argument {nameof(location)} starting with {StoreTypePrefix}");
            }
            m_innerStore.Open(location.Substring(StoreTypePrefix.Length), noPrivateKeys);
        }

        /// <inheritdoc/>
        public void Close()
        {
            m_innerStore.Close();
        }

        /// <inheritdoc/>
        public string StoreType => StoreTypePrefix.Substring(0, StoreTypePrefix.Length - 1);

        /// <inheritdoc/>
        public Task Add(X509Certificate2 certificate, string password = null)
        {
            return m_innerStore.Add(certificate, password);
        }

        /// <inheritdoc/>
        public Task<bool> Delete(string thumbprint)
        {
            return m_innerStore.Delete(thumbprint);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> Enumerate()
        {
            return m_innerStore.Enumerate();
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
        {
            return m_innerStore.FindByThumbprint(thumbprint);
        }

        /// <inheritdoc/>
        public bool SupportsCRLs
            => m_innerStore.SupportsCRLs;

        /// <inheritdoc/>
        public Task AddCRL(X509CRL crl)
            => m_innerStore.AddCRL(crl);

        /// <inheritdoc/>
        public Task<bool> DeleteCRL(X509CRL crl)
            => m_innerStore.DeleteCRL(crl);

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLs()
            => m_innerStore.EnumerateCRLs();

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
            => m_innerStore.EnumerateCRLs(issuer, validateUpdateTime);

        /// <inheritdoc/>
        public Task<StatusCode> IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
            => m_innerStore.IsRevoked(issuer, certificate);

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => m_innerStore.SupportsLoadPrivateKey;

        /// <inheritdoc/>
        public Task<X509Certificate2> LoadPrivateKey(string thumbprint, string subjectName, string password)
            => m_innerStore.LoadPrivateKey(thumbprint, subjectName, password);

        public static int InstancesCreated => s_instancesCreated;

        #region data members
        internal const string StoreTypePrefix = "testStoreType:";
        private static int s_instancesCreated = 0;
        private readonly X509CertificateStore m_innerStore;
        #endregion data members
    }
}
