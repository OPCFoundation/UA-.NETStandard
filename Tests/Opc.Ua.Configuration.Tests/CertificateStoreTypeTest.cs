/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the custom certificate store config extensions.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [SetCulture("en-us")]
    public class CertificateStoreTypeTest
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            CertificateStoreType.RegisterCertificateStoreType(
                TestCertStore.StoreTypePrefix,
                new TestStoreType());
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

        [Test]
        public async Task CertificateStoreTypeNoConfigTestAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var application = new ApplicationInstance(telemetry) { ApplicationName = "Application" };

            string appStorePath = m_tempPath + Path.DirectorySeparatorChar + "own";
            string trustedStorePath = m_tempPath + Path.DirectorySeparatorChar + "trusted";
            string issuerStorePath = m_tempPath + Path.DirectorySeparatorChar + "issuer";
            string trustedUserStorePath = m_tempPath + Path.DirectorySeparatorChar + "trustedUser";
            string issuerUserStorePath = m_tempPath + Path.DirectorySeparatorChar + "userIssuer";

            IApplicationConfigurationBuilderSecurityOptionStores appConfigBuilder = application
                .Build(
                    applicationUri: "urn:localhost:CertStoreTypeTest",
                    productUri: "uri:opcfoundation.org:Tests:CertStoreTypeTest")
                .AsClient()
                .AddSecurityConfigurationStores(
                    subjectName: "CN=CertStoreTypeTest, O=OPC Foundation",
                    appRoot: TestCertStore.StoreTypePrefix + appStorePath,
                    trustedRoot: TestCertStore.StoreTypePrefix + trustedStorePath,
                    issuerRoot: TestCertStore.StoreTypePrefix + issuerStorePath)
                .AddSecurityConfigurationUserStore(
                    trustedRoot: TestCertStore.StoreTypePrefix + trustedUserStorePath,
                    issuerRoot: TestCertStore.StoreTypePrefix + issuerUserStorePath);

            // patch custom stores before creating the config
            ApplicationConfiguration appConfig = await appConfigBuilder.CreateAsync()
                .ConfigureAwait(false);

            bool certOK = await application.CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);

            int instancesCreatedWhileLoadingConfig = TestCertStore.InstancesCreated;
            Assert.IsTrue(instancesCreatedWhileLoadingConfig > 0);

            await OpenCertStoreAsync(appConfig.SecurityConfiguration.TrustedIssuerCertificates, telemetry)
                .ConfigureAwait(false);
            await OpenCertStoreAsync(appConfig.SecurityConfiguration.TrustedPeerCertificates, telemetry)
                .ConfigureAwait(false);
            await OpenCertStoreAsync(appConfig.SecurityConfiguration.UserIssuerCertificates, telemetry)
                .ConfigureAwait(false);
            await OpenCertStoreAsync(appConfig.SecurityConfiguration.TrustedUserCertificates, telemetry)
                .ConfigureAwait(false);

            int instancesCreatedWhileOpeningAuthRootStore = TestCertStore.InstancesCreated;
            Assert.IsTrue(
                instancesCreatedWhileLoadingConfig < instancesCreatedWhileOpeningAuthRootStore);
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                TestCertStore.StoreTypePrefix + trustedUserStorePath);
            using ICertificateStore store = certificateStoreIdentifier.OpenStore(telemetry);
            Assert.IsTrue(
                instancesCreatedWhileOpeningAuthRootStore < TestCertStore.InstancesCreated);
        }

        private static async Task OpenCertStoreAsync(CertificateTrustList trustList, ITelemetryContext telemetry)
        {
            using ICertificateStore trustListStore = trustList.OpenStore(telemetry);
            X509Certificate2Collection certs = await trustListStore.EnumerateAsync()
                .ConfigureAwait(false);
            X509CRLCollection crls = await trustListStore.EnumerateCRLsAsync()
                .ConfigureAwait(false);
            trustListStore.Close();
        }

        private string m_tempPath;
    }

    internal sealed class TestStoreType : ICertificateStoreType
    {
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new TestCertStore(telemetry);
        }

        public bool SupportsStorePath(string storePath)
        {
            return storePath != null &&
                storePath.StartsWith(TestCertStore.StoreTypePrefix, StringComparison.Ordinal);
        }
    }

    internal sealed class TestCertStore : ICertificateStore
    {
        public const string StoreTypePrefix = "testStoreType:";

        public TestCertStore(ITelemetryContext telemetry)
        {
            InstancesCreated++;
            m_innerStore = new DirectoryCertificateStore(true, telemetry);
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
        public Task AddAsync(
            X509Certificate2 certificate,
            char[] password = null,
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
        public Task AddCRL(X509CRL crl)
        {
            return m_innerStore.AddCRLAsync(crl);
        }

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
            char[] password,
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

        public static int InstancesCreated { get; set; }

        private readonly DirectoryCertificateStore m_innerStore;
    }
}
