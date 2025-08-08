/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/


using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the custom certificate store config extensions.
    /// </summary>
    [TestFixture, Category("CertificateStore")]
    [SetCulture("en-us")]
    public class CertificateStoreTypeTest
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            CertificateStoreType.RegisterCertificateStoreType(TestCertStore.StoreTypePrefix, new TestStoreType());
        }

        [SetUp]
        public void SetUp()
        {
            m_tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(m_tempPath);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_tempPath, true);
        }

        #region Test Methods
        [Test]
        public async Task CertificateStoreTypeNoConfigTest()
        {
            ApplicationInstance application = new ApplicationInstance() {
                ApplicationName = "Application",
            };

            string appStorePath = m_tempPath + Path.DirectorySeparatorChar + "own";
            string trustedStorePath = m_tempPath + Path.DirectorySeparatorChar + "trusted";
            string issuerStorePath = m_tempPath + Path.DirectorySeparatorChar + "issuer";
            string trustedUserStorePath = m_tempPath + Path.DirectorySeparatorChar + "trustedUser";
            string issuerUserStorePath = m_tempPath + Path.DirectorySeparatorChar + "userIssuer";

            var appConfigBuilder = application.Build(
                applicationUri: "urn:localhost:CertStoreTypeTest",
                productUri: "uri:opcfoundation.org:Tests:CertStoreTypeTest")
                .AsClient()
                .AddSecurityConfigurationStores(
                    subjectName: "CN=CertStoreTypeTest, O=OPC Foundation",
                    appRoot: TestCertStore.StoreTypePrefix + appStorePath,
                    trustedRoot: TestCertStore.StoreTypePrefix + trustedStorePath,
                    issuerRoot: TestCertStore.StoreTypePrefix + issuerStorePath
                    )
                .AddSecurityConfigurationUserStore(
                    trustedRoot: TestCertStore.StoreTypePrefix + trustedUserStorePath,
                    issuerRoot: TestCertStore.StoreTypePrefix + issuerUserStorePath
                );

            // patch custom stores before creating the config
            ApplicationConfiguration appConfig = await appConfigBuilder.Create().ConfigureAwait(false);

            bool certOK = await application.CheckApplicationInstanceCertificates(true).ConfigureAwait(false);
            Assert.True(certOK);

            int instancesCreatedWhileLoadingConfig = TestCertStore.InstancesCreated;
            Assert.IsTrue(instancesCreatedWhileLoadingConfig > 0);

            OpenCertStore(appConfig.SecurityConfiguration.TrustedIssuerCertificates);
            OpenCertStore(appConfig.SecurityConfiguration.TrustedPeerCertificates);
            OpenCertStore(appConfig.SecurityConfiguration.UserIssuerCertificates);
            OpenCertStore(appConfig.SecurityConfiguration.TrustedUserCertificates);

            int instancesCreatedWhileOpeningAuthRootStore = TestCertStore.InstancesCreated;
            Assert.IsTrue(instancesCreatedWhileLoadingConfig < instancesCreatedWhileOpeningAuthRootStore);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(TestCertStore.StoreTypePrefix + trustedUserStorePath);
            using (var store = certificateStoreIdentifier.OpenStore())
            {
                Assert.IsTrue(instancesCreatedWhileOpeningAuthRootStore < TestCertStore.InstancesCreated);
            }
        }
        #endregion Test Methods

        #region Private Methods
        private void OpenCertStore(CertificateTrustList trustList)
        {
            using (ICertificateStore trustListStore = trustList.OpenStore())
            {
                var certs = trustListStore.Enumerate();
                var crls = trustListStore.EnumerateCRLsAsync();
                trustListStore.Close();
            }
        }
        #endregion

        #region Private Members
        private string m_tempPath;
        #endregion
    }

    internal sealed class TestStoreType : ICertificateStoreType
    {
        public ICertificateStore CreateStore()
        {
            return new TestCertStore();
        }

        public bool SupportsStorePath(string storePath)
        {
            return storePath != null && storePath.StartsWith(TestCertStore.StoreTypePrefix, StringComparison.Ordinal);
        }
    }

    internal sealed class TestCertStore : ICertificateStore
    {
        public const string StoreTypePrefix = "testStoreType:";

        public TestCertStore()
        {
            s_instancesCreated++;
            m_innerStore = new DirectoryCertificateStore(true);
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
        public string StorePath => m_innerStore.StorePath;

        /// <inheritdoc/>
        public bool NoPrivateKeys => m_innerStore.NoPrivateKeys;

        /// <inheritdoc/>
        public Task Add(X509Certificate2 certificate, string password = null)
        {
            return m_innerStore.AddAsync(certificate, password);
        }

        /// <inheritdoc/>
        public Task AddAsync(X509Certificate2 certificate, string password = null)
        {
            return m_innerStore.AddAsync(certificate, password);
        }

        /// <inheritdoc/>
        [Obsolete("Use DeleteAsync instead.")]
        public Task<bool> Delete(string thumbprint)
        {
            return DeleteAsync(thumbprint);
        }

        /// <inheritdoc/>
        public Task<bool> DeleteAsync(string thumbprint)
        {
            return m_innerStore.DeleteAsync(thumbprint);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> Enumerate()
        {
            return m_innerStore.EnumerateAsync();
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> EnumerateAsync()
        {
            return m_innerStore.EnumerateAsync();
        }

        /// <inheritdoc/>
        [Obsolete("Use FindByThumbprintAsync instead.")]
        public Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
        {
            return FindByThumbprintAsync(thumbprint);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> FindByThumbprintAsync(string thumbprint)
        {
            return m_innerStore.FindByThumbprintAsync(thumbprint);
        }

        /// <inheritdoc/>
        public bool SupportsCRLs
            => m_innerStore.SupportsCRLs;

        /// <inheritdoc/>
        public Task AddCRL(X509CRL crl)
            => m_innerStore.AddCRLAsync(crl);

        /// <inheritdoc/>
        public Task AddCRLAsync(X509CRL crl)
            => m_innerStore.AddCRLAsync(crl);

        /// <inheritdoc/>
        [Obsolete("Use DeleteCRLAsync instead.")]
        public Task<bool> DeleteCRL(X509CRL crl)
        {
            return DeleteCRLAsync(crl);
        }

        /// <inheritdoc/>
        public Task<bool> DeleteCRLAsync(X509CRL crl)
            => m_innerStore.DeleteCRLAsync(crl);

        /// <inheritdoc/>
        [Obsolete("Use EnumerateCRLsAsync instead.")]
        public Task<X509CRLCollection> EnumerateCRLs()
        {
            return EnumerateCRLsAsync();
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync()
            => m_innerStore.EnumerateCRLsAsync();

        /// <inheritdoc/>
        [Obsolete("Use EnumerateCRLsAsync instead.")]
        public Task<X509CRLCollection> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
        {
            return EnumerateCRLsAsync(issuer, validateUpdateTime);
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(X509Certificate2 issuer, bool validateUpdateTime = true)
            => m_innerStore.EnumerateCRLsAsync(issuer, validateUpdateTime);

        /// <inheritdoc/>
        [Obsolete("Use IsRevokedAsync instead.")]
        public Task<StatusCode> IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            return IsRevokedAsync(issuer, certificate);
        }

        /// <inheritdoc/>
        public Task<StatusCode> IsRevokedAsync(X509Certificate2 issuer, X509Certificate2 certificate)
            => m_innerStore.IsRevokedAsync(issuer, certificate);

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => m_innerStore.SupportsLoadPrivateKey;

        /// <inheritdoc/>
        [Obsolete("Use LoadPrivateKeyAsync instead.")]
        public Task<X509Certificate2> LoadPrivateKey(string thumbprint, string subjectName, string applicationUri, NodeId certificateType, string password)
        {
            return LoadPrivateKeyAsync(thumbprint, subjectName, applicationUri, certificateType, password);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2> LoadPrivateKeyAsync(string thumbprint, string subjectName, string applicationUri, NodeId certificateType, string password)
            => m_innerStore.LoadPrivateKeyAsync(thumbprint, subjectName, applicationUri, certificateType, password);

        /// <inheritdoc/>
        [Obsolete("Use AddRejectedAsync instead.")]
        public Task AddRejected(X509Certificate2Collection certificates, int maxCertificates)
        {
            return AddRejectedAsync(certificates, maxCertificates);
        }

        /// <inheritdoc/>
        public Task AddRejectedAsync(X509Certificate2Collection certificates, int maxCertificates)
            => m_innerStore.AddRejectedAsync(certificates, maxCertificates);

        public static int InstancesCreated => s_instancesCreated;

        #region Private Members
        private static int s_instancesCreated = 0;
        private readonly DirectoryCertificateStore m_innerStore;
        #endregion 
    }
}
