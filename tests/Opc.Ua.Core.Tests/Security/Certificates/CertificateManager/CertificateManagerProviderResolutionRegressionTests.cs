/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CertificateManagerProviderResolutionRegressionTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void InjectedProviderResolvesRegisteredAndConfiguredTrustStores(
            bool fromConfiguration, bool includeBuiltInProviders)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var trustedStore = new Mock<ICertificateStore>(MockBehavior.Strict);
            trustedStore.Setup(store => store.Open(kTrustedPath, true));
            trustedStore.Setup(store => store.Dispose());
            var issuerStore = new Mock<ICertificateStore>(MockBehavior.Strict);
            issuerStore.Setup(store => store.Open(kIssuerPath, true));
            issuerStore.Setup(store => store.Dispose());
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(store => store.StoreTypeName).Returns("FeedbackToken");
            provider.Setup(store => store.SupportsStorePath(It.IsAny<string>()))
                .Returns((string path) => path.StartsWith("pkcs11:", StringComparison.Ordinal));
            provider.SetupSequence(store => store.CreateStore(telemetry))
                .Returns(trustedStore.Object)
                .Returns(issuerStore.Object);
            var configuration = new SecurityConfiguration();
            if (fromConfiguration)
            {
                configuration.TrustedPeerCertificates = new CertificateTrustList { StorePath = kTrustedPath };
                configuration.TrustedIssuerCertificates = new CertificateTrustList { StorePath = kIssuerPath };
                Assert.That(configuration.TrustedPeerCertificates.StoreType,
                    Is.EqualTo(CertificateStoreType.Directory));
            }

            using CertificateManager manager = CertificateManagerFactory.Create(
                configuration,
                telemetry,
                options =>
                {
                    if (includeBuiltInProviders)
                    {
                        options.AddStoreProvider(new DirectoryStoreProvider());
                        options.AddStoreProvider(new X509StoreProvider());
                    }
                    options.AddStoreProvider(provider.Object);
                    if (!fromConfiguration)
                    {
                        options.AddTrustList(TrustListIdentifier.Peers.Name, kTrustedPath, kIssuerPath);
                    }
                });

            using ICertificateStore trusted = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            using ICertificateStore issuer = manager.OpenIssuerStore(TrustListIdentifier.Peers);
            Assert.That(trusted, Is.SameAs(trustedStore.Object));
            Assert.That(issuer, Is.SameAs(issuerStore.Object));
            trustedStore.Verify(store => store.Open(kTrustedPath, true), Times.Once);
            issuerStore.Verify(store => store.Open(kIssuerPath, true), Times.Once);
            provider.Verify(store => store.CreateStore(telemetry), Times.Exactly(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InjectedProviderRemainsAvailableDuringCertificateValidationAsync(bool fromConfiguration)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate trusted = CertificateBuilder.Create("CN=Injected Provider Validation").CreateForRSA();
            using Certificate untrusted = CertificateBuilder.Create("CN=Injected Provider Validation").CreateForRSA();
            var store = new Mock<ICertificateStore>(MockBehavior.Strict);
            store.Setup(value => value.Open(kTrustedPath, true));
            store.Setup(value => value.Dispose());
            store.Setup(value => value.FindByThumbprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string thumbprint, CancellationToken _) => Task.FromResult(
                    thumbprint == trusted.Thumbprint
                        ? new CertificateCollection { trusted }
                        : new CertificateCollection()));
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.StoreTypeName).Returns("FeedbackToken");
            provider.Setup(value => value.SupportsStorePath(It.IsAny<string>()))
                .Returns((string path) => path.StartsWith("pkcs11:", StringComparison.Ordinal));
            provider.Setup(value => value.CreateStore(telemetry)).Returns(store.Object);
            var configuration = new SecurityConfiguration();
            if (fromConfiguration)
            {
                configuration.TrustedPeerCertificates = new CertificateTrustList { StorePath = kTrustedPath };
            }
            await using CertificateManager manager = CertificateManagerFactory.Create(
                configuration,
                telemetry,
                options =>
                {
                    options.AddStoreProvider(provider.Object);
                    if (!fromConfiguration)
                    {
                        options.AddTrustList(TrustListIdentifier.Peers.Name, kTrustedPath);
                    }
                });

            CertificateValidationResult accepted = await manager.ValidateAsync(trusted, TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            CertificateValidationResult rejected = await manager.ValidateAsync(untrusted, TrustListIdentifier.Peers)
                .ConfigureAwait(false);

            Assert.That(accepted.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(accepted.IsValid, Is.True);
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(rejected.IsValid, Is.False);
            provider.Verify(value => value.CreateStore(telemetry), Times.Once);
            store.Verify(value => value.FindByThumbprintAsync(trusted.Thumbprint, It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Test]
        public void InjectedProviderOpenFailureDisposesStoreAndPropagatesError()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var failure = new InvalidOperationException("Injected store open failure.");
            var store = new Mock<ICertificateStore>(MockBehavior.Strict);
            store.Setup(value => value.Open(kTrustedPath, true)).Throws(failure);
            store.Setup(value => value.Dispose());
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.StoreTypeName).Returns("FeedbackToken");
            provider.Setup(value => value.SupportsStorePath(kTrustedPath)).Returns(true);
            provider.Setup(value => value.CreateStore(telemetry)).Returns(store.Object);
            using var manager = new CertificateManager(telemetry, [provider.Object]);
            manager.RegisterTrustList(TrustListIdentifier.Peers, kTrustedPath);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                manager.OpenTrustedStore(TrustListIdentifier.Peers));

            Assert.That(error, Is.SameAs(failure));
            store.Verify(value => value.Dispose(), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConfiguredCustomStoreTypeTakesPrecedenceOverPathRecognition(bool setTypeFirst)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var store = new Mock<ICertificateStore>(MockBehavior.Strict);
            store.Setup(value => value.Open(kTrustedPath, true));
            store.Setup(value => value.Dispose());
            var pathProvider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            pathProvider.SetupGet(provider => provider.StoreTypeName).Returns("FeedbackToken");
            pathProvider.Setup(provider => provider.SupportsStorePath(It.IsAny<string>())).Returns(true);
            var configuredProvider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            configuredProvider.SetupGet(provider => provider.StoreTypeName).Returns("FeedbackOverride");
            configuredProvider.Setup(provider => provider.SupportsStorePath(It.IsAny<string>())).Returns(false);
            configuredProvider.Setup(provider => provider.CreateStore(telemetry)).Returns(store.Object);
            var trusted = new CertificateTrustList();
            if (setTypeFirst)
            {
                trusted.StoreType = "FeedbackOverride";
                trusted.StorePath = kTrustedPath;
            }
            else
            {
                trusted.StorePath = kTrustedPath;
                trusted.StoreType = "FeedbackOverride";
            }

            using var manager = new CertificateManager(telemetry, [pathProvider.Object, configuredProvider.Object]);
            manager.MapFromSecurityConfiguration(new SecurityConfiguration { TrustedPeerCertificates = trusted });
            using ICertificateStore opened = manager.OpenTrustedStore(TrustListIdentifier.Peers);

            Assert.That(opened, Is.SameAs(store.Object));
            Assert.That(trusted.StoreType, Is.EqualTo("FeedbackOverride"));
            configuredProvider.Verify(provider => provider.CreateStore(telemetry), Times.Once);
            pathProvider.Verify(provider => provider.CreateStore(It.IsAny<ITelemetryContext>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnclaimedPathsKeepDirectoryAndX509Stores(bool x509)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var provider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.StoreTypeName).Returns("FeedbackToken");
            provider.Setup(value => value.SupportsStorePath(It.IsAny<string>())).Returns(false);
            string path = x509
                ? "CurrentUser\\My"
                : Path.Combine(Path.GetTempPath(), "provider-resolution-" + Guid.NewGuid().ToString("N"));
            using var manager = new CertificateManager(telemetry, [provider.Object]);

            manager.RegisterTrustList(TrustListIdentifier.Peers, path);
            using ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers);

            Assert.That(store, Is.TypeOf(x509 ? typeof(X509CertificateStore) : typeof(DirectoryCertificateStore)));
            provider.Verify(value => value.CreateStore(It.IsAny<ITelemetryContext>()), Times.Never);
        }

        private const string kTrustedPath = "pkcs11:token=feedback-trusted";
        private const string kIssuerPath = "pkcs11:token=feedback-issuers";
    }
}
