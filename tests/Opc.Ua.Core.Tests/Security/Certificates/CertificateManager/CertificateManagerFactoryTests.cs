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

#pragma warning disable CA2007
#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for <see cref="CertificateManagerFactory"/> and <see cref="CertificateManagerOptions"/>.
    /// </summary>
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    public class CertificateManagerFactoryTests
    {
        private ITelemetryContext m_telemetry = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
        }

        [Test]
        public void AddTrustListReturnsSameOptionsAndRegistersTrustList()
        {
            var store = new Mock<ICertificateStore>(MockBehavior.Strict);
            store.Setup(s => s.Open("mock-trusted", true));
            store.Setup(s => s.Dispose());

            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(p => p.StoreTypeName).Returns(CertificateStoreType.Directory);
            // Store type resolution probes each provider by path before falling
            // back to the built-in types; this provider claims no path scheme of
            // its own and is selected by its store type name instead.
            provider.Setup(p => p.SupportsStorePath(It.IsAny<string>())).Returns(false);
            provider.Setup(p => p.CreateStore(m_telemetry)).Returns(store.Object);

            var trustList = new TrustListIdentifier("CustomTrustList");
            using CertificateManager manager = CertificateManagerFactory.Create(
                new SecurityConfiguration(),
                m_telemetry,
                options =>
                {
                    CertificateManagerOptions returned = options.AddTrustList(trustList.Name, "mock-trusted");
                    Assert.That(returned, Is.SameAs(options));
                    options.AddStoreProvider(provider.Object);
                });

            Assert.That(manager.TrustLists, Does.Contain(trustList));

            using ICertificateStore openedStore = manager.OpenTrustedStore(trustList);
            Assert.That(openedStore, Is.SameAs(store.Object));
            provider.Verify(p => p.CreateStore(m_telemetry), Times.Once);
            store.Verify(s => s.Open("mock-trusted", true), Times.Once);
        }

        [Test]
        public void AddStoreProviderRejectsNullProvider()
        {
            var options = new CertificateManagerOptions();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => options.AddStoreProvider(null!))!;

            Assert.That(exception.ParamName, Is.EqualTo("provider"));
        }

        [Test]
        public void AddStoreProviderReturnsSameOptionsAndAddsProvider()
        {
            var options = new CertificateManagerOptions();
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);

            CertificateManagerOptions returned = options.AddStoreProvider(provider.Object);

            Assert.That(returned, Is.SameAs(options));
            Assert.That(options.StoreProviders, Has.Count.EqualTo(1));
            Assert.That(options.StoreProviders, Does.Contain(provider.Object));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ConfigurationValidationUsesScopedTrustStoreProviderAsync(bool customProvider, bool builtInIssuers)
        {
            string root = Path.Combine(Path.GetTempPath(), "sdk-trust-provider-" + Guid.NewGuid().ToString("N"));
            string storeType = customProvider ? "ScopedValidationDirectory" : CertificateStoreType.Directory;
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(instance => instance.StoreTypeName).Returns("ScopedValidationDirectory");
            provider.Setup(instance => instance.SupportsStorePath(It.IsAny<string>())).Returns(false);
            provider.Setup(instance => instance.CreateStore(m_telemetry))
                .Returns(() => new DirectoryCertificateStore(false, m_telemetry));
            var security = new SecurityConfiguration
            {
                ApplicationCertificates = [new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(root, "own"),
                    SubjectName = "CN=ScopedValidation",
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }],
                TrustedPeerCertificates = TrustStore("trusted"),
                TrustedIssuerCertificates = TrustStore("issuer"),
                TrustedHttpsCertificates = TrustStore("https-trusted"),
                HttpsIssuerCertificates = TrustStore("https-issuer"),
                TrustedUserCertificates = TrustStore("user-trusted"),
                UserIssuerCertificates = TrustStore("user-issuer")
            };
            try
            {
                using CertificateManager manager = CertificateManagerFactory.Create(security, m_telemetry, options =>
                {
                    if (customProvider)
                    {
                        options.AddStoreProvider(provider.Object);
                    }
                });
                var configuration = new ApplicationConfiguration(m_telemetry)
                {
                    ApplicationName = "ScopedValidation",
                    ApplicationUri = "urn:localhost:ScopedValidation",
                    ApplicationType = ApplicationType.Client,
                    ClientConfiguration = new ClientConfiguration(),
                    SecurityConfiguration = security,
                    CertificateManager = manager
                };
                await configuration.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
                Assert.That(security.TrustedPeerCertificates.StoreType, Is.EqualTo(storeType));
                provider.Verify(instance => instance.CreateStore(m_telemetry),
                    customProvider ? Times.Exactly(builtInIssuers ? 3 : 6) : Times.Never());
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }

            CertificateTrustList TrustStore(string name) => new()
            {
                StoreType = builtInIssuers && name.EndsWith("issuer", StringComparison.Ordinal)
                    ? CertificateStoreType.Directory : storeType,
                StorePath = Path.Combine(root, name)
            };
        }

        [TestCase("unknown-provider")]
        [TestCase("provider-failure")]
        [TestCase("missing-path")]
        public async Task ConfigurationValidationRejectsInvalidScopedStoresAsync(string failure)
        {
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(instance => instance.StoreTypeName).Returns("InvalidScopedStore");
            provider.Setup(instance => instance.SupportsStorePath(It.IsAny<string>())).Returns(false);
            provider.Setup(instance => instance.CreateStore(m_telemetry)).Throws(new IOException("Provider unavailable"));
            var security = new SecurityConfiguration
            {
                ApplicationCertificates = [new CertificateIdentifier
                {
                    SubjectName = "CN=ScopedFailure", CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }],
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "InvalidScopedStore", StorePath = "scoped-trusted" },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = "InvalidScopedStore", StorePath = failure == "missing-path" ? string.Empty : "scoped-issuer"
                }
            };
            using CertificateManager manager = CertificateManagerFactory.Create(security, m_telemetry, options =>
            {
                if (failure != "unknown-provider")
                {
                    options.AddStoreProvider(provider.Object);
                }
            });
            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "ScopedFailure", ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = security, CertificateManager = manager
            };
            ServiceResultException? error = null;
            try
            {
                await configuration.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                error = exception;
            }
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            provider.Verify(instance => instance.CreateStore(m_telemetry),
                failure == "provider-failure" ? Times.Once() : Times.Never());
        }

        [Test]
        public void CreateRejectsNullSecurityConfiguration()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => CertificateManagerFactory.Create(null!, m_telemetry))!;

            Assert.That(exception.ParamName, Is.EqualTo("securityConfiguration"));
        }

        [Test]
        public void StoreResolversKeepSameNamedProvidersInstanceScoped()
        {
            var firstStore = new Mock<ICertificateStore>(MockBehavior.Strict);
            firstStore.Setup(store => store.Open("same-path", false));
            firstStore.Setup(store => store.Dispose());
            var secondStore = new Mock<ICertificateStore>(MockBehavior.Strict);
            secondStore.Setup(store => store.Open("same-path", true));
            secondStore.Setup(store => store.Dispose());
            var firstProvider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            firstProvider.SetupGet(provider => provider.StoreTypeName).Returns("SameProviderName");
            firstProvider.Setup(provider => provider.CreateStore(m_telemetry)).Returns(firstStore.Object);
            var secondProvider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            secondProvider.SetupGet(provider => provider.StoreTypeName).Returns("SameProviderName");
            secondProvider.Setup(provider => provider.CreateStore(m_telemetry)).Returns(secondStore.Object);
            using var first = new CertificateManager(m_telemetry, [firstProvider.Object]);
            using var second = new CertificateManager(m_telemetry, [secondProvider.Object]);
            using (ICertificateStore opened = first.OpenCertificateStore("same-path", "SameProviderName", false))
            {
                Assert.That(opened, Is.SameAs(firstStore.Object));
            }
            using (ICertificateStore opened = second.OpenCertificateStore("same-path", "SameProviderName", true))
            {
                Assert.That(opened, Is.SameAs(secondStore.Object));
            }
            firstProvider.Verify(provider => provider.CreateStore(m_telemetry), Times.Once());
            secondProvider.Verify(provider => provider.CreateStore(m_telemetry), Times.Once());
            firstStore.Verify(store => store.Open("same-path", false), Times.Once());
            secondStore.Verify(store => store.Open("same-path", true), Times.Once());
        }

        [Test]
        public void CreateRejectsNullTelemetryContext()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => CertificateManagerFactory.Create(new SecurityConfiguration(), null!))!;

            Assert.That(exception.ParamName, Is.EqualTo("telemetry"));
        }
    }
}
