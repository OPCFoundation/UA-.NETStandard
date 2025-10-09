/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
#if NETCOREAPP2_1_OR_GREATER && !NET_STANDARD_TESTS
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("ApplicationInstance")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationInstanceTests
    {
        public const string ApplicationName = "UA Configuration Test";
        public const string ApplicationUri = "urn:localhost:opcfoundation.org:ConfigurationTest";
        public const string ProductUri = "http://opcfoundation.org/UA/ConfigurationTest";

        public const string SubjectName
            = "CN=UA Configuration Test, O=OPC Foundation, C=US, S=Arizona";

        public const string EndpointUrl = "opc.tcp://localhost:51000";

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            // pki directory root for test runs.
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            try
            {
                // pki directory root for test runs.
                Directory.Delete(m_pkiRoot, true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Load a file configuration.
        /// </summary>
        [Test]
        public async Task TestFileConfigAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);
            string configPath = Utils.GetAbsoluteFilePath(
                "Opc.Ua.Configuration.Tests.Config.xml",
                checkCurrentDirectory: true,
                createAlways: false);
            Assert.NotNull(configPath);
            ApplicationConfiguration applicationConfiguration = await applicationInstance
                .LoadApplicationConfigurationAsync(configPath, true)
                .ConfigureAwait(false);
            Assert.NotNull(applicationConfiguration);
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsClientAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestBadApplicationInstanceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // no app name
            var applicationInstance = new ApplicationInstance(telemetry);
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false));
            // discoveryserver can not be combined with client/server
            applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.DiscoveryServer
            };
            NUnit.Framework.Assert.ThrowsAsync<ArgumentException>(async () =>
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false));
            NUnit.Framework.Assert.ThrowsAsync<ArgumentException>(async () =>
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false));
            // server overrides client settings
            applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            };

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.AreEqual(ApplicationType.Server, applicationInstance.ApplicationType);

            // client overrides server setting
            applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Server
            };

            await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.AreEqual(ApplicationType.Client, applicationInstance.ApplicationType);

            // invalid sec policy testing
            applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            // invalid use, use AddUnsecurePolicyNone instead
            NUnit.Framework.Assert.ThrowsAsync<ArgumentException>(async () =>
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddPolicy(MessageSecurityMode.None, SecurityPolicies.None)
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false));
            // invalid mix sign / none
            NUnit.Framework.Assert.ThrowsAsync<ArgumentException>(async () =>
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.None)
                    .AddSecurityConfiguration(applicationCerts)
                    .CreateAsync()
                    .ConfigureAwait(false));
            // invalid policy
            NUnit.Framework.Assert.ThrowsAsync<ArgumentException>(async () =>
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddPolicy(MessageSecurityMode.Sign, "123")
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false));
            // invalid user token policy
            NUnit.Framework.Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddUserTokenPolicy(null)
                    .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task TestNoFileConfigAsServerMinimalAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .SetOperationTimeout(10000)
                .AsServer([EndpointUrl])
                .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsServerMaximalAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .SetTransportQuotas(new TransportQuotas { OperationTimeout = 10000 })
                .AsServer([EndpointUrl])
                .AddSignPolicies()
                .AddSignAndEncryptPolicies()
                .AddUnsecurePolicyNone()
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15)
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .AddUserTokenPolicy(
                    new UserTokenPolicy(UserTokenType.Certificate)
                    {
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    })
                .SetDiagnosticsEnabled(true)
                .SetPublishingResolution(100)
                .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                .SetAddAppCertToTrustedStore(true)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetMinimumCertificateKeySize(1024)
                .SetRejectSHA1SignedCertificates(false)
                .SetSendCertificateChain(true)
                .SetSuppressNonceValidationErrors(true)
                .SetRejectUnknownRevocationStatus(true)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsClientAndServerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .SetMaxBufferSize(32768)
                .AsServer([EndpointUrl])
                .AddUnsecurePolicyNone()
                .AddSignPolicies()
                .AddSignAndEncryptPolicies()
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                .SetDiagnosticsEnabled(true)
                .AsClient()
                .AddSecurityConfiguration(
                    applicationCerts,
                    CertificateStoreType.Directory,
                    CertificateStoreType.X509Store)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        /// <summary>
        /// Test case when app cert already exists or when new
        /// cert is created in X509Store.
        /// </summary>
        [Test]
        [Repeat(2)]
        public async Task TestNoFileConfigAsServerX509StoreAsync()
        {
#if NETCOREAPP2_1_OR_GREATER && !NET_STANDARD_TESTS
            // this test fails on macOS, ignore
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NUnit.Framework.Assert.Ignore("X509Store trust lists not supported on mac OS.");
            }
#endif
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddUnsecurePolicyNone()
                .AddSignAndEncryptPolicies()
                .AddUserTokenPolicy(UserTokenType.UserName)
                .AsClient()
                .SetDefaultSessionTimeout(10000)
                .AddSecurityConfiguration(applicationCerts, CertificateStoreType.X509Store)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            CertificateIdentifier applicationCertificate = applicationInstance
                .ApplicationConfiguration
                .SecurityConfiguration
                .ApplicationCertificate;

            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);

            bool deleteAfterUse = applicationCertificate.Certificate != null;

            Assert.True(certOK);
            using (
                ICertificateStore store =
                    applicationInstance.ApplicationConfiguration.SecurityConfiguration
                        .TrustedPeerCertificates
                        .OpenStore(telemetry))
            {
                // store public key in trusted store
                byte[] rawData = applicationCertificate.Certificate.RawData;
                await store.AddAsync(X509CertificateLoader.LoadCertificate(rawData))
                    .ConfigureAwait(false);
            }

            if (deleteAfterUse)
            {
                string thumbprint = applicationCertificate.Certificate.Thumbprint;
                using (ICertificateStore store = applicationCertificate.OpenStore(telemetry))
                {
                    bool success = await store.DeleteAsync(thumbprint).ConfigureAwait(false);
                    Assert.IsTrue(success);
                }
                using (
                    ICertificateStore store =
                        applicationInstance.ApplicationConfiguration.SecurityConfiguration
                            .TrustedPeerCertificates
                            .OpenStore(telemetry))
                {
                    bool success = await store.DeleteAsync(thumbprint).ConfigureAwait(false);
                    Assert.IsTrue(success);
                }
            }
        }

        [Test]
        public async Task TestNoFileConfigAsServerCustomAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl, "opc.https://localhost:51001"], s_alternateBaseAddresses)
                .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                .SetAddAppCertToTrustedStore(true)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        public enum InvalidCertType
        {
            NoIssues,
            NoIssuer,
            Expired,
            IssuerExpired,
            NotYetValid,
            IssuerNotYetValid,
            KeySize1024,
            HostName
        }

        /// <summary>
        /// Test to verify that an existing cert with suppressible issues
        /// is not recreated/replaced.
        /// </summary>
        [Test]
        [TestCase(InvalidCertType.NoIssues, true, true)]
        [TestCase(InvalidCertType.NotYetValid, true, true)]
        [TestCase(InvalidCertType.Expired, true, true)]
        [TestCase(InvalidCertType.HostName, true, false)]
        [TestCase(InvalidCertType.HostName, false, true)]
        [TestCase(InvalidCertType.KeySize1024, true, false)]
        public async Task TestInvalidAppCertDoNotRecreateAsync(
            InvalidCertType certType,
            bool server,
            bool suppress)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for test runs.
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    pkiRoot);

            ApplicationConfiguration config;
            if (server)
            {
                config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer(s_baseAddresses)
                    .AddSecurityConfiguration(applicationCerts, pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
            }
            else
            {
                config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCerts, pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
            }

            Assert.NotNull(config);

            CertificateIdentifier applicationCertificate = applicationInstance
                .ApplicationConfiguration
                .SecurityConfiguration
                .ApplicationCertificate;
            Assert.IsNull(applicationCertificate.Certificate);

            X509Certificate2 publicKey = null;
            using (X509Certificate2 testCert = CreateInvalidCert(certType))
            {
                Assert.NotNull(testCert);
                Assert.True(testCert.HasPrivateKey);
                await testCert.AddToStoreAsync(
                    applicationCertificate.StoreType,
                    applicationCertificate.StorePath,
                    password: null,
                    telemetry).ConfigureAwait(false);
                publicKey = X509CertificateLoader.LoadCertificate(testCert.RawData);
            }

            using (publicKey)
            {
                if (suppress)
                {
                    bool certOK = await applicationInstance
                        .CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false);

                    Assert.True(certOK);
                    Assert.AreEqual(publicKey, applicationCertificate.Certificate);
                }
                else
                {
                    ServiceResultException sre = NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(async () =>
                            await applicationInstance.CheckApplicationInstanceCertificatesAsync(
                                true)
                            .ConfigureAwait(false));
                    Assert.AreEqual(StatusCodes.BadConfigurationError, sre.StatusCode);
                }
            }
        }

        /// <summary>
        /// Test to verify that an existing cert with suppressible issues
        /// is not recreated/replaced.
        /// </summary>
        [Test]
        [TestCase(InvalidCertType.NoIssues, true, true)]
        [TestCase(InvalidCertType.NoIssuer, true, false)]
        [TestCase(InvalidCertType.NotYetValid, true, true)]
        [TestCase(InvalidCertType.Expired, true, true)]
        [TestCase(InvalidCertType.IssuerNotYetValid, true, true)]
        [TestCase(InvalidCertType.IssuerExpired, true, true)]
        [TestCase(InvalidCertType.HostName, true, false)]
        [TestCase(InvalidCertType.HostName, false, true)]
        //TODO [TestCase(InvalidCertType.KeySize1024, true, false)]
        public async Task TestInvalidAppCertChainDoNotRecreateAsync(
            InvalidCertType certType,
            bool server,
            bool suppress)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for test runs.
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    pkiRoot);

            ApplicationConfiguration config;
            if (server)
            {
                config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer(s_baseAddresses)
                    .AddSecurityConfiguration(applicationCerts, pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
            }
            else
            {
                config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCerts, pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
            }
            Assert.NotNull(config);

            CertificateIdentifier applicationCertificate = applicationInstance
                .ApplicationConfiguration
                .SecurityConfiguration
                .ApplicationCertificate;
            Assert.IsNull(applicationCertificate.Certificate);

            X509Certificate2Collection testCerts = CreateInvalidCertChain(certType);
            if (certType != InvalidCertType.NoIssuer)
            {
                using X509Certificate2 issuerCert = testCerts[1];
                Assert.NotNull(issuerCert);
                Assert.False(issuerCert.HasPrivateKey);
                await issuerCert.AddToStoreAsync(
                    applicationInstance
                        .ApplicationConfiguration
                        .SecurityConfiguration
                        .TrustedIssuerCertificates
                        .StoreType,
                    applicationInstance
                        .ApplicationConfiguration
                        .SecurityConfiguration
                        .TrustedIssuerCertificates
                        .StorePath,
                    password: null,
                    telemetry).ConfigureAwait(false);
            }

            X509Certificate2 publicKey = null;
            using (X509Certificate2 testCert = testCerts[0])
            {
                Assert.NotNull(testCert);
                Assert.True(testCert.HasPrivateKey);
                await testCert.AddToStoreAsync(
                    applicationCertificate.StoreType,
                    applicationCertificate.StorePath,
                    password: null,
                    telemetry).ConfigureAwait(false);
                publicKey = X509CertificateLoader.LoadCertificate(testCert.RawData);
            }

            using (publicKey)
            {
                if (suppress)
                {
                    bool certOK = await applicationInstance
                        .CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false);

                    Assert.True(certOK);
                    Assert.AreEqual(publicKey, applicationCertificate.Certificate);
                }
                else
                {
                    ServiceResultException sre = NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(async () =>
                            await applicationInstance.CheckApplicationInstanceCertificatesAsync(
                                true)
                            .ConfigureAwait(false));
                    Assert.AreEqual(StatusCodes.BadConfigurationError, sre.StatusCode);
                }
            }
        }

        /// <summary>
        /// Tests that a supplied certificate is stored in the Trusted store of the Server after calling method AddOwnCertificateToTrustedStoreAsync
        /// </summary>
        [Test]
        public async Task TestAddOwnCertificateToTrustedStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange Application Instance
            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            ApplicationConfiguration configuration = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .SetOperationTimeout(10000)
                .AsServer([EndpointUrl])
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);

            //Arrange cert
            DateTime notBefore = DateTime.Today.AddDays(-30);
            DateTime notAfter = DateTime.Today.AddDays(30);

            using X509Certificate2 cert = CertificateFactory
                .CreateCertificate(SubjectName)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .SetCAConstraint(-1)
                .CreateForRSA();
            //Act
            await applicationInstance
                .AddOwnCertificateToTrustedStoreAsync(cert, new CancellationToken())
                .ConfigureAwait(false);
            ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates
                .OpenStore(telemetry);
            X509Certificate2Collection storedCertificates = await store
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);

            //Assert
            Assert.IsTrue(storedCertificates.Contains(cert));
        }

        /// <summary>
        /// Test to verify that a new cert is not recreated/replaced if DisableCertificateAutoCreation is set.
        /// </summary>
        [Theory]
        public async Task TestDisableCertificateAutoCreationAsync(
            bool server,
            bool disableCertificateAutoCreation)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for test runs.
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;

            var applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                DisableCertificateAutoCreation = disableCertificateAutoCreation
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config;

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            if (server)
            {
                config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsServer(s_baseAddressesArray)
                    .AddSecurityConfiguration(applicationCerts, pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
            }
            else
            {
                config = await applicationInstance
                    .Build(ApplicationUri, ProductUri)
                    .AsClient()
                    .AddSecurityConfiguration(applicationCerts, pkiRoot)
                    .CreateAsync()
                    .ConfigureAwait(false);
            }
            Assert.NotNull(config);

            CertificateIdentifier applicationCertificate = applicationInstance
                .ApplicationConfiguration
                .SecurityConfiguration
                .ApplicationCertificate;
            Assert.IsNull(applicationCertificate.Certificate);

            if (disableCertificateAutoCreation)
            {
                ServiceResultException sre = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                        .ConfigureAwait(false));
                Assert.AreEqual(StatusCodes.BadConfigurationError, sre.StatusCode);
            }
            else
            {
                bool certOK = await applicationInstance
                    .CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false);
                Assert.True(certOK);
            }
        }

        /// <summary>
        /// Test RejectCertificateUriMismatch flag with matching ApplicationUri.
        /// When flag is true and URIs match, should succeed.
        /// </summary>
        [Test]
        public async Task TestRejectCertificateUriMismatchMatchingUriSucceedsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(applicationCerts, m_pkiRoot)
                .SetRejectCertificateUriMismatch(true)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            Assert.True(config.SecurityConfiguration.RejectCertificateUriMismatch);

            // This should succeed because the URIs match
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        /// <summary>
        /// Test RejectCertificateUriMismatch flag with mismatched ApplicationUri.
        /// When flag is true and URIs don't match, should throw exception.
        /// </summary>
        [Test]
        public async Task TestRejectCertificateUriMismatchMismatchedUriThrowsExceptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            // Create a certificate with a different ApplicationUri
            const string differentUri = "urn:localhost:opcfoundation.org:DifferentApp";
            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(differentUri, ApplicationName, SubjectName, [Utils.GetHostName()])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddYears(1))
                .CreateForRSA();

            // Save the certificate to a store
            string certStorePath = m_pkiRoot + "own";
            var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
            using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
            {
                await certStore.AddAsync(cert).ConfigureAwait(false);
                certStore.Close();
            }

            var certId = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = certStorePath,
                SubjectName = SubjectName
            };

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri) // Using ApplicationUri, but cert has differentUri
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetRejectCertificateUriMismatch(true)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            Assert.True(config.SecurityConfiguration.RejectCertificateUriMismatch);

            // Load the certificate into the identifier
            await certId.FindAsync(false, null, telemetry).ConfigureAwait(false);

            // Replace the application certificate with our custom one
            config.SecurityConfiguration.ApplicationCertificate = certId;

            // This should throw because the URIs don't match and flag is true
            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false));
            Assert.AreEqual(StatusCodes.BadCertificateUriInvalid, sre.StatusCode);
            Assert.That(sre.Message, Does.Contain("does not match"));
        }

        /// <summary>
        /// Test RejectCertificateUriMismatch flag disabled (default behavior).
        /// When flag is false and URIs don't match, should update the config Uri.
        /// </summary>
        [Test]
        public async Task TestRejectCertificateUriMismatchDisabledUpdatesConfigUriAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            // Create a certificate with a different ApplicationUri
            const string differentUri = "urn:localhost:opcfoundation.org:DifferentApp";
            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(differentUri, ApplicationName, SubjectName, [Utils.GetHostName()])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddYears(1))
                .CreateForRSA();

            // Save the certificate to a store
            string certStorePath = m_pkiRoot + "own";
            var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
            using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
            {
                await certStore.AddAsync(cert).ConfigureAwait(false);
                certStore.Close();
            }

            var certId = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = certStorePath,
                SubjectName = SubjectName
            };

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri) // Using ApplicationUri, but cert has differentUri
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetRejectCertificateUriMismatch(false) // Explicitly set to false (default)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);
            Assert.False(config.SecurityConfiguration.RejectCertificateUriMismatch);

            // Load the certificate into the identifier
            await certId.FindAsync(false, null, telemetry).ConfigureAwait(false);

            // Replace the application certificate with our custom one
            config.SecurityConfiguration.ApplicationCertificate = certId;

            string originalUri = config.ApplicationUri;
            Assert.AreEqual(ApplicationUri, originalUri);

            // This should succeed and update the ApplicationUri to match the certificate
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);

            // Verify that the ApplicationUri was updated to match the certificate
            Assert.AreNotEqual(originalUri, config.ApplicationUri);
            Assert.AreEqual(differentUri, config.ApplicationUri);
        }

        /// <summary>
        /// Test that multiple certificates with different ApplicationUris throw an exception.
        /// Even though FindAsync might load certificates by SubjectName without checking ApplicationUri,
        /// the explicit validation ensures all loaded certificates have the same URI.
        /// </summary>
        [Test]
        public async Task TestMultipleCertificatesDifferentUrisThrowsExceptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            // Create two certificates with different ApplicationUris
            const string uri1 = "urn:localhost:opcfoundation.org:App1";
            const string uri2 = "urn:localhost:opcfoundation.org:App2";

            X509Certificate2 cert1 = CertificateFactory
                .CreateCertificate(uri1, ApplicationName, SubjectName, [Utils.GetHostName()])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddYears(1))
                .CreateForRSA();

            const string subjectName2 = "CN=UA Configuration Test 2, O=OPC Foundation, C=US, S=Arizona";
            X509Certificate2 cert2 = CertificateFactory
                .CreateCertificate(uri2, ApplicationName, subjectName2, [Utils.GetHostName()])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddYears(1))
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();

            // Save certificates to stores
            string certStorePath = m_pkiRoot + "own";
            var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
            using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
            {
                await certStore.AddAsync(cert1).ConfigureAwait(false);
                await certStore.AddAsync(cert2).ConfigureAwait(false);
                certStore.Close();
            }

            var certId1 = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = certStorePath,
                SubjectName = SubjectName
            };

            var certId2 = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = certStorePath,
                SubjectName = subjectName2
            };

            ApplicationConfiguration config = await applicationInstance
                .Build(uri1, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetRejectCertificateUriMismatch(false)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);

            // Set multiple certificates
            config.SecurityConfiguration.ApplicationCertificates = new CertificateIdentifierCollection
            {
                certId1,
                certId2
            };

            // This should throw because all certificates must have the same ApplicationUri
            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await applicationInstance.CheckApplicationInstanceCertificatesAsync(true)
                    .ConfigureAwait(false));
            Assert.AreEqual(StatusCodes.BadCertificateUriInvalid, sre.StatusCode);
            Assert.That(sre.Message, Does.Contain("must have the same ApplicationUri"));
        }

        /// <summary>
        /// Test that multiple certificates with the same ApplicationUri succeed.
        /// </summary>
        [Test]
        public async Task TestMultipleCertificatesSameUriSucceedsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            Assert.NotNull(applicationInstance);

            // Create two certificates with the same ApplicationUri
            X509Certificate2 cert1 = CertificateFactory
                .CreateCertificate(ApplicationUri, ApplicationName, SubjectName, [Utils.GetHostName()])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddYears(1))
                .CreateForRSA();

            const string subjectName2 = "CN=UA Configuration Test RSA, O=OPC Foundation, C=US, S=Arizona";
            X509Certificate2 cert2 = CertificateFactory
                .CreateCertificate(ApplicationUri, ApplicationName, subjectName2, [Utils.GetHostName()])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddYears(1))
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();

            // Save certificates to stores
            string certStorePath = m_pkiRoot + "own";
            var certStoreIdentifier = new CertificateStoreIdentifier(certStorePath, CertificateStoreType.Directory, false);
            using (ICertificateStore certStore = certStoreIdentifier.OpenStore(telemetry))
            {
                await certStore.AddAsync(cert1).ConfigureAwait(false);
                await certStore.AddAsync(cert2).ConfigureAwait(false);
                certStore.Close();
            }

            var certId1 = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = certStorePath,
                SubjectName = SubjectName
            };

            var certId2 = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = certStorePath,
                SubjectName = subjectName2
            };

            // Load the certificates
            await certId1.FindAsync(false, null, telemetry).ConfigureAwait(false);
            await certId2.FindAsync(false, null, telemetry).ConfigureAwait(false);

            ApplicationConfiguration config = await applicationInstance
                .Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.NotNull(config);

            // Set multiple certificates with same URI
            config.SecurityConfiguration.ApplicationCertificates = new CertificateIdentifierCollection
            {
                certId1,
                certId2
            };

            // This should succeed because all certificates have the same ApplicationUri
            bool certOK = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.True(certOK);
        }

        private static X509Certificate2 CreateInvalidCert(InvalidCertType certType)
        {
            // reasonable defaults
            DateTime notBefore = DateTime.Today.AddDays(-30);
            DateTime notAfter = DateTime.Today.AddDays(30);
            ushort keySize = CertificateFactory.DefaultKeySize;
            string[] domainNames = [Utils.GetHostName()];
            switch (certType)
            {
                case InvalidCertType.Expired:
                    notBefore = DateTime.Today.AddMonths(-12);
                    notAfter = DateTime.Today.AddDays(-7);
                    break;
                case InvalidCertType.NotYetValid:
                    notBefore = DateTime.Today.AddDays(7);
                    notAfter = notBefore.AddMonths(12);
                    break;
                case InvalidCertType.KeySize1024:
                    keySize = 1024;
                    break;
                case InvalidCertType.HostName:
                    domainNames = ["myhost", "1.2.3.4"];
                    break;
            }

            return CertificateFactory
                .CreateCertificate(ApplicationUri, ApplicationName, SubjectName, domainNames)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .SetRSAKeySize(keySize)
                .CreateForRSA();
        }

        private static X509Certificate2Collection CreateInvalidCertChain(InvalidCertType certType)
        {
            // reasonable defaults
            DateTime notBefore = DateTime.Today.AddYears(-1);
            DateTime notAfter = DateTime.Today.AddYears(1);
            DateTime issuerNotBefore = notBefore;
            DateTime issuerNotAfter = notAfter;
            ushort keySize = CertificateFactory.DefaultKeySize;
            string[] domainNames = [Utils.GetHostName()];
            switch (certType)
            {
                case InvalidCertType.Expired:
                    notAfter = DateTime.Today.AddDays(-7);
                    break;
                case InvalidCertType.IssuerExpired:
                    issuerNotAfter = DateTime.Today.AddDays(-7);
                    break;
                case InvalidCertType.NotYetValid:
                    notBefore = DateTime.Today.AddDays(7);
                    break;
                case InvalidCertType.IssuerNotYetValid:
                    issuerNotBefore = DateTime.Today.AddDays(7);
                    break;
                case InvalidCertType.KeySize1024:
                    keySize = 1024;
                    break;
                case InvalidCertType.HostName:
                    domainNames = ["myhost", "1.2.3.4"];
                    break;
            }

            const string rootCASubjectName = "CN=Root CA Test, O=OPC Foundation, C=US, S=Arizona";
            using X509Certificate2 rootCA = CertificateFactory
                .CreateCertificate(rootCASubjectName)
                .SetNotBefore(issuerNotBefore)
                .SetNotAfter(issuerNotAfter)
                .SetCAConstraint(-1)
                .CreateForRSA();
            X509Certificate2 appCert = CertificateFactory
                .CreateCertificate(ApplicationUri, ApplicationName, SubjectName, domainNames)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .SetIssuer(rootCA)
                .SetRSAKeySize(keySize)
                .CreateForRSA();

            return [appCert, X509CertificateLoader.LoadCertificate(rootCA.RawData)];
        }

        private string m_pkiRoot;

        private static readonly string[] s_alternateBaseAddresses
            = ["opc.tcp://192.168.1.100:51000"];

        private static readonly string[] s_baseAddresses
            = ["opc.tcp://localhost:12345/Configuration"];

        private static readonly string[] s_baseAddressesArray
            = ["opc.tcp://localhost:12345/Configuration"];
    }
}
