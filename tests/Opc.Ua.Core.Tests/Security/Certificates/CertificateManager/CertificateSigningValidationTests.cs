/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateValidationCore")]
    [Category("SecurityRegression")]
    public sealed class CertificateSigningValidationTests
    {
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
        public async Task ValidateForSigningRequiresDigitalSignatureInsteadOfApplicationDataEncipherment()
        {
            using Certificate certificate = CreateRsaCertificate(
                X509KeyUsageFlags.DigitalSignature, "CN=Signing-only RSA certificate");
            using var core = new CertificateValidationCore(m_telemetry);
            core.Update(null, TrustListWith(certificate), null);
            using var chain = new CertificateCollection { certificate };

            CertificateValidationResult applicationResult = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);
            CertificateValidationResult signingResult = await core.ValidateAsync(
                chain,
                null,
                new ValidationOptions { ValidateForSigning = true },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(applicationResult.IsValid, Is.False);
            Assert.That(ContainsStatus(applicationResult, StatusCodes.BadCertificateUseNotAllowed), Is.True);
            Assert.That(signingResult.IsValid, Is.True);
            Assert.That(signingResult.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ValidateForSigningNeitherConsumesNorPopulatesApplicationCertificateCache()
        {
            using Certificate certificate = CreateRsaCertificate(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.DataEncipherment,
                "CN=Signing cache isolation");
            int nativeCopies = 0;
            using var core = new CertificateValidationCore(
                m_telemetry,
                static _ => null,
                value =>
                {
                    nativeCopies++;
                    return value.AsX509Certificate2();
                });
            core.Update(null, TrustListWith(certificate), null);
            core.UseValidatedCertificates = true;
            using var chain = new CertificateCollection { certificate };

            CertificateValidationResult initialApplicationResult = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(initialApplicationResult.IsValid, Is.True);
            int afterInitialApplication = nativeCopies;

            CertificateValidationResult signingWithCachedApplicationPath = await core.ValidateAsync(
                chain,
                null,
                new ValidationOptions { ValidateForSigning = true },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(signingWithCachedApplicationPath.IsValid, Is.True);
            Assert.That(nativeCopies, Is.GreaterThan(afterInitialApplication),
                "Signing validation must not consume the cached application validation path.");

            core.ResetValidatedCertificates();
            CertificateValidationResult signingWithoutCache = await core.ValidateAsync(
                chain,
                null,
                new ValidationOptions { ValidateForSigning = true },
                CancellationToken.None).ConfigureAwait(false);
            int afterSigningWithoutCache = nativeCopies;
            CertificateValidationResult applicationAfterSigning = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(signingWithoutCache.IsValid, Is.True);
            Assert.That(applicationAfterSigning.IsValid, Is.True);
            Assert.That(nativeCopies, Is.GreaterThan(afterSigningWithoutCache),
                "Signing validation must not populate the application validation cache.");
        }

        [Test]
        public async Task PerCallValidationOverridesDoNotMutateManagerDefaults()
        {
            await using var manager = new CertificateManager(m_telemetry)
            {
                AutoAcceptUntrustedCertificates = true
            };
            using Certificate certificate = CertificateBuilder.Create("CN=Per-call override isolation")
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var chain = new CertificateCollection { certificate };

            CertificateValidationResult strictPerCall = await manager.ValidateAsync(
                chain,
                TrustListIdentifier.Peers,
                new ValidationOptions
                {
                    AutoAcceptUntrustedCertificates = false,
                    RecordRejectedCertificates = false
                },
                CancellationToken.None).ConfigureAwait(false);
            CertificateValidationResult defaultCall = await manager.ValidateAsync(
                chain, TrustListIdentifier.Peers, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(strictPerCall.IsValid, Is.False);
            Assert.That(defaultCall.IsValid, Is.True,
                "The next call must retain the manager's permissive global setting.");
            Assert.That(manager.AutoAcceptUntrustedCertificates, Is.True);
        }

        private static CertificateTrustList TrustListWith(Certificate certificate)
        {
            return new CertificateTrustList
            {
                TrustedCertificates = [new CertificateIdentifier { RawData = certificate.RawData }]
            };
        }

        private static bool ContainsStatus(CertificateValidationResult result, StatusCode statusCode)
        {
            foreach (ServiceResult error in result.Errors)
            {
                if (error.StatusCode == statusCode)
                {
                    return true;
                }
            }
            return result.StatusCode == statusCode;
        }

        private static Certificate CreateRsaCertificate(X509KeyUsageFlags keyUsage, string subject)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .AddExtension(new X509KeyUsageExtension(keyUsage, critical: true))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static readonly DateTime s_notBefore =
            new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_notAfter =
            new(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private ITelemetryContext m_telemetry = null!;
    }
}
