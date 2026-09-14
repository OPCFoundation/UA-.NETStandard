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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Covers algorithm inference and RSA minimum-key-size policy during application certificate checks.
    /// </summary>
    [TestFixture]
    [Category("ApplicationInstance")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ApplicationInstanceCertificatePolicyRegressionTests
    {
        /// <summary>
        /// Verifies an unspecified certificate slot preserves its stored ECC key and infers the matching ECC type.
        /// </summary>
        [Test]
        public async Task SilentCertificateCheckPreservesUnspecifiedEccIdentityAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string path = Path.Combine(Path.GetTempPath(), "unspecified-ecc-" + Guid.NewGuid().ToString("N"));
            try
            {
                using Certificate original = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                    kApplicationUri, kApplicationName, kSubject, ["localhost"])
                    .SetNotBefore(s_validFrom)
                    .SetNotAfter(s_validTo)
                    .SetECCurve(ECCurve.NamedCurves.nistP256)
                    .CreateForECDsa();
                var identifier = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = path,
                    SubjectName = original.Subject,
                    Thumbprint = original.Thumbprint
                };
                using (ICertificateStore store = new CertificateStoreIdentifier(
                    path, CertificateStoreType.Directory, noPrivateKeys: false).OpenStore(telemetry))
                {
                    await store.AddAsync(original).ConfigureAwait(false);
                }
                await using var application = new ApplicationInstance(telemetry)
                {
                    ApplicationName = kApplicationName,
                    ApplicationConfiguration = CreateConfiguration(identifier, minimumKeySize: 2048)
                };
                // Collection assignment supplies an RSA default; this public slot
                // is deliberately left algorithm-unspecified before checking it.
                identifier.CertificateType = NodeId.Null;
                Assert.That(identifier.CertificateType.IsNull, Is.True);

                bool valid = await application.CheckApplicationInstanceCertificatesAsync(silent: true)
                    .ConfigureAwait(false);

                Assert.That(valid, Is.True);
                Assert.That(identifier.Thumbprint, Is.EqualTo(original.Thumbprint));
                Assert.That(identifier.CertificateType,
                    Is.EqualTo(ObjectTypeIds.EccNistP256ApplicationCertificateType));
                using Certificate reloaded = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                    identifier, passwordProvider: null, kApplicationUri, telemetry).ConfigureAwait(false);
                Assert.That(reloaded.RawData, Is.EqualTo(original.RawData));
                Assert.That(reloaded.HasPrivateKey, Is.True);
                using ECDsa key = reloaded.GetECDsaPrivateKey();
                Assert.That(key, Is.Not.Null);
                Assert.That(key.KeySize, Is.EqualTo(256));
                using ICertificateStore unchangedStore = new CertificateStoreIdentifier(path).OpenStore(telemetry);
                using CertificateCollection certificates = await unchangedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.That(certificates, Has.Count.EqualTo(1));
                Assert.That(certificates[0].RawData, Is.EqualTo(original.RawData));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies explicit RSA checks enforce the configured minimum without replacing the stored certificate.
        /// </summary>
        [TestCase((ushort)2048, false)]
        [TestCase((ushort)3072, true)]
        public async Task ExplicitRsaCertificateCheckEnforcesConfiguredMinimumAsync(ushort keySize, bool accepted)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string path = Path.Combine(Path.GetTempPath(), "explicit-rsa-" + Guid.NewGuid().ToString("N"));
            try
            {
                using Certificate original = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                    kApplicationUri, kApplicationName, kSubject, ["localhost"])
                    .SetNotBefore(s_validFrom)
                    .SetNotAfter(s_validTo)
                    .SetRSAKeySize(keySize)
                    .CreateForRSA();
                var identifier = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = path,
                    SubjectName = original.Subject,
                    Thumbprint = original.Thumbprint,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                };
                using (ICertificateStore store = new CertificateStoreIdentifier(
                    path, CertificateStoreType.Directory, noPrivateKeys: false).OpenStore(telemetry))
                {
                    await store.AddAsync(original).ConfigureAwait(false);
                }
                await using var application = new ApplicationInstance(telemetry)
                {
                    ApplicationName = kApplicationName,
                    ApplicationConfiguration = CreateConfiguration(identifier, minimumKeySize: 3072)
                };

                if (accepted)
                {
                    Assert.That(await application.CheckApplicationInstanceCertificatesAsync(silent: true)
                        .ConfigureAwait(false), Is.True);
                }
                else
                {
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() =>
                        application.CheckApplicationInstanceCertificatesAsync(silent: true).AsTask());
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                }

                Assert.That(identifier.Thumbprint, Is.EqualTo(original.Thumbprint));
                using Certificate unchanged = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                    identifier, passwordProvider: null, kApplicationUri, telemetry).ConfigureAwait(false);
                Assert.That(unchanged.RawData, Is.EqualTo(original.RawData));
                using RSA key = unchanged.GetRSAPrivateKey();
                Assert.That(key.KeySize, Is.EqualTo(keySize));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies provisioning an HTTPS certificate retains its type and creates the configured 3072-bit RSA key.
        /// </summary>
        [Test]
        public async Task HttpsCertificateProvisioningKeepsConfiguredRsaMinimumAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string path = Path.Combine(Path.GetTempPath(), "https-rsa-policy-" + Guid.NewGuid().ToString("N"));
            try
            {
                var identifier = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = path,
                    SubjectName = kSubject,
                    CertificateType = ObjectTypeIds.HttpsCertificateType
                };
                await using var application = new ApplicationInstance(telemetry)
                {
                    ApplicationName = kApplicationName,
                    ApplicationConfiguration = CreateConfiguration(identifier, minimumKeySize: 3072)
                };

                bool valid = await application.CheckApplicationInstanceCertificatesAsync(silent: true)
                    .ConfigureAwait(false);

                Assert.That(valid, Is.True);
                Assert.That(identifier.CertificateType, Is.EqualTo(ObjectTypeIds.HttpsCertificateType));
                using Certificate created = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                    identifier, passwordProvider: null, kApplicationUri, telemetry).ConfigureAwait(false);
                Assert.That(created.HasPrivateKey, Is.True);
                using RSA key = created.GetRSAPrivateKey();
                Assert.That(key, Is.Not.Null);
                Assert.That(key.KeySize, Is.EqualTo(3072));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Creates a client configuration with one certificate slot and an explicit minimum key size.
        /// </summary>
        private static ApplicationConfiguration CreateConfiguration(
            CertificateIdentifier identifier, ushort minimumKeySize)
        {
            return new ApplicationConfiguration
            {
                ApplicationName = kApplicationName,
                ApplicationUri = kApplicationUri,
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificates = [identifier],
                    MinimumCertificateKeySize = minimumKeySize,
                    AddAppCertToTrustedStore = false
                },
                ClientConfiguration = new ClientConfiguration()
            };
        }

        /// <summary>
        /// Names the application consistently in test configurations and generated certificates.
        /// </summary>
        private const string kApplicationName = "Certificate Policy Regression";

        /// <summary>
        /// Identifies the application in certificate URI validation.
        /// </summary>
        private const string kApplicationUri = "urn:localhost:certificate-policy-regression";

        /// <summary>
        /// Supplies the subject used to locate the test application certificate.
        /// </summary>
        private const string kSubject = "CN=Certificate Policy Regression";

        /// <summary>
        /// Starts certificate validity before the policy checks under test.
        /// </summary>
        private static readonly DateTime s_validFrom = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Keeps expiry independent of the key-policy assertions.
        /// </summary>
        private static readonly DateTime s_validTo = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
