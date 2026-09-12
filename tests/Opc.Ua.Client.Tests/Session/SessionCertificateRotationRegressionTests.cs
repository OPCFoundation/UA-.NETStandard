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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    public sealed class SessionCertificateRotationRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task SessionCertificateUsesTheActiveRegistryAndRetainsItsOwnKeyAsync(bool sendChain)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate active = CertificateBuilder.Create("CN=ActiveSessionCertificate").CreateForRSA();
            using Certificate issuer = CertificateBuilder.Create("CN=SessionCertificateIssuer").CreateForRSA();
            using var issuers = new CertificateCollection { issuer };
            using var registered = new CertificateEntry(
                active,
                issuers,
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var manager = new Mock<ICertificateManager>();
            manager.Setup(m => m.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256))
                .Returns(() => registered.AddRef());
            var configuration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = manager.Object,
                SecurityConfiguration = new SecurityConfiguration { SendCertificateChain = sendChain }
            };

            using CertificateEntry loaded = await Session.LoadInstanceCertificateEntryAsync(
                configuration,
                SecurityPolicies.Basic256Sha256,
                telemetry,
                useCertificateRegistry: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Certificate.Thumbprint, Is.EqualTo(active.Thumbprint));
                Assert.That(loaded.CertificateType, Is.EqualTo(ObjectTypeIds.RsaSha256ApplicationCertificateType));
                Assert.That(loaded.IssuerChain, Has.Count.EqualTo(sendChain ? 1 : 0));
                if (sendChain)
                {
                    Assert.That(loaded.IssuerChain[0].Thumbprint, Is.EqualTo(issuer.Thumbprint));
                }
            });
            registered.Dispose();
            issuers.Dispose();
            issuer.Dispose();
            active.Dispose();

            byte[] data = [1, 3, 5, 7];
            using RSA privateKey = loaded.Certificate.GetRSAPrivateKey()!;
            using RSA publicKey = loaded.Certificate.GetRSAPublicKey()!;
            byte[] signature = privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(
                publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.True);
            if (sendChain)
            {
                using RSA issuerKey = loaded.IssuerChain[0].GetRSAPublicKey()!;
                Assert.That(issuerKey.KeySize, Is.GreaterThanOrEqualTo(2048));
            }
            manager.Verify(
                m => m.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256),
                Times.Once);
        }

        [Test]
        public void CancelledCertificateLoadDoesNotAcquireARegistryEntry()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new Mock<ICertificateManager>();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = manager.Object
            };
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(() => Session.LoadInstanceCertificateEntryAsync(
                configuration,
                SecurityPolicies.Basic256Sha256,
                telemetry,
                useCertificateRegistry: true,
                cancellation.Token));
            manager.Verify(m => m.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void MissingRegistryAndConfiguredCertificateIsAnExplicitConfigurationError()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new Mock<ICertificateManager>();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = manager.Object
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => Session.LoadInstanceCertificateEntryAsync(
                    configuration,
                    SecurityPolicies.Basic256Sha256,
                    telemetry,
                    useCertificateRegistry: true))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void ActiveRegistryCertificateWithoutAPrivateKeyIsRejectedExplicitly()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate owner = CertificateBuilder.Create("CN=PublicSessionCertificate").CreateForRSA();
            using Certificate certificate = Utils.ParseCertificateBlob(owner.RawData, telemetry);
            using var issuers = new CertificateCollection();
            using var registered = new CertificateEntry(
                certificate,
                issuers,
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var manager = new Mock<ICertificateManager>();
            manager.Setup(m => m.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256))
                .Returns(() => registered.AddRef());
            var configuration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = manager.Object
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => Session.LoadInstanceCertificateEntryAsync(
                    configuration,
                    SecurityPolicies.Basic256Sha256,
                    telemetry,
                    useCertificateRegistry: true))!;

            Assert.Multiple(() =>
            {
                Assert.That(certificate.HasPrivateKey, Is.False);
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(error.Message, Does.Contain("private key"));
            });
        }

        [Test]
        public async Task UnmanagedCertificateLoadHonorsAnExplicitConfiguredStoreReplacementAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate active = CertificateBuilder.Create("CN=PreviouslyActiveClient").CreateForRSA();
            using Certificate replacement = CertificateBuilder.Create("CN=ConfiguredReplacementClient").CreateForRSA();
            using var issuers = new CertificateCollection();
            using var registered = new CertificateEntry(
                active,
                issuers,
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var manager = new Mock<ICertificateManager>();
            manager.Setup(m => m.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256))
                .Returns(() => registered.AddRef());
            string path = Path.Combine(Path.GetTempPath(), "SessionCertificate-" + Guid.NewGuid().ToString("N"));
            var identifier = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = path,
                Thumbprint = replacement.Thumbprint,
                SubjectName = replacement.Subject,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };
            var configuration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = manager.Object,
                SecurityConfiguration = new SecurityConfiguration { ApplicationCertificate = identifier }
            };
            try
            {
                using (ICertificateStore store = CertificateIdentifierResolver.OpenStore(identifier, telemetry))
                {
                    await store.AddAsync(replacement).ConfigureAwait(false);
                }

                using CertificateEntry loaded = await Session.LoadInstanceCertificateEntryAsync(
                    configuration,
                    SecurityPolicies.Basic256Sha256,
                    telemetry).ConfigureAwait(false);

                Assert.That(loaded.Certificate.Thumbprint, Is.EqualTo(replacement.Thumbprint));
                manager.Verify(m => m.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()), Times.Never);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }
    }
}
