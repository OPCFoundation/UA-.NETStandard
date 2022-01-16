using System;
using System.Collections.Generic;
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
            var fileInfo = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, @"Security\Certificates\CertificateStoreTypeTestConfig.xml"));
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
        public ICertificateStore CreateStore() => new TestCertStore();

        public bool SupportsStorePath(string storePath) => storePath != null && storePath.StartsWith(TestCertStore.StoreTypePrefix);
    }

    internal sealed class TestCertStore : ICertificateStore
    {
        public TestCertStore()
        {
            s_instancesCreated++;
            m_innerStore = new X509CertificateStore();
        }

        public void Open(string location)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            if (!location.StartsWith(StoreTypePrefix))
            {
                throw new ArgumentException($"Expected argument {nameof(location)} starting with {StoreTypePrefix}");
            }
            m_innerStore.Open(location.Substring(StoreTypePrefix.Length));
        }

        public void Close()
            => m_innerStore.Close();

        public void Dispose()
            => m_innerStore.Dispose();

        public Task Add(X509Certificate2 certificate, string password = null)
            => m_innerStore.Add(certificate, password);

        public Task<bool> Delete(string thumbprint)
            => m_innerStore.Delete(thumbprint);

        public Task<X509Certificate2Collection> Enumerate()
            => m_innerStore.Enumerate();

        public Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
            => m_innerStore.FindByThumbprint(thumbprint);

        public bool SupportsCRLs
            => m_innerStore.SupportsCRLs;

        public void AddCRL(X509CRL crl)
            => m_innerStore.AddCRL(crl);

        public bool DeleteCRL(X509CRL crl)
            => m_innerStore.DeleteCRL(crl);

        public X509CRLCollection EnumerateCRLs()
            => m_innerStore.EnumerateCRLs();

        public X509CRLCollection EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
            => m_innerStore.EnumerateCRLs(issuer, validateUpdateTime);

        public StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
            => m_innerStore.IsRevoked(issuer, certificate);

        public static int InstancesCreated => s_instancesCreated;

        #region data members
        internal const string StoreTypePrefix = "testStoreType:";
        private static int s_instancesCreated = 0;
        private readonly X509CertificateStore m_innerStore;
        #endregion data members
    }
}
