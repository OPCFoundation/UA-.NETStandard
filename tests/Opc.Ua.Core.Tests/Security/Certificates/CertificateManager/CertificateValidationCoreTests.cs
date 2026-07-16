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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Coverage tests for the internal certificate chain validation engine
    /// <see cref="CertificateValidationCore"/>. Small deterministic RSA chains
    /// are built with fixed validity dates and driven through the public
    /// surface to exercise the chain loop, key-usage, trust/issuer resolution
    /// and revocation mapping branches.
    /// </summary>
    [TestFixture]
    [Category("CertificateValidationCore")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CertificateValidationCoreTests
    {
        private static readonly DateTime s_rootFrom = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_rootTo = new(2099, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_leafFrom = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_leafTo = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_pastFrom = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_pastTo = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_futureFrom = new(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_futureTo = new(2095, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private ITelemetryContext m_telemetry;
        private readonly List<string> m_tempDirs = [];
        private readonly List<CertificateValidationCore> m_cores = [];

        private Certificate m_rootCa;
        private Certificate m_leaf;
        private Certificate m_intermediateCa;
        private Certificate m_leafUnderIntermediate;
        private Certificate m_selfSignedApp;
        private Certificate m_expiredLeaf;
        private Certificate m_notYetValidLeaf;
        private Certificate m_expiredRootCa;
        private Certificate m_leafUnderExpiredRoot;
        private Certificate m_leafDigitalSignatureOnly;
        private Certificate m_weakIssuerCa;
        private Certificate m_leafUnderWeakIssuer;
        private Certificate m_ecdsaTrustedLeaf;
        private Certificate m_ecdsaWeakHashLeaf;
        private Certificate m_ecdsaNoDigitalSignatureLeaf;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();

            m_rootCa = CertificateBuilder
                .Create("CN=CVC Root CA, O=OPC Foundation")
                .SetNotBefore(s_rootFrom)
                .SetNotAfter(s_rootTo)
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            m_leaf = CreateLeaf("CN=CVC Leaf", m_rootCa, s_leafFrom, s_leafTo);

            m_intermediateCa = CertificateBuilder
                .Create("CN=CVC Intermediate CA, O=OPC Foundation")
                .SetNotBefore(s_rootFrom)
                .SetNotAfter(s_rootTo)
                .SetCAConstraint(0)
                .SetIssuer(m_rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            m_leafUnderIntermediate = CreateLeaf(
                "CN=CVC Leaf Under Intermediate", m_intermediateCa, s_leafFrom, s_leafTo);

            m_selfSignedApp = CertificateBuilder
                .Create("CN=CVC Self Signed App")
                .SetNotBefore(s_leafFrom)
                .SetNotAfter(s_leafTo)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            m_expiredLeaf = CreateLeaf("CN=CVC Expired Leaf", m_rootCa, s_pastFrom, s_pastTo);

            m_notYetValidLeaf = CreateLeaf(
                "CN=CVC Future Leaf", m_rootCa, s_futureFrom, s_futureTo);

            m_expiredRootCa = CertificateBuilder
                .Create("CN=CVC Expired Root CA, O=OPC Foundation")
                .SetNotBefore(s_pastFrom)
                .SetNotAfter(s_pastTo)
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            m_leafUnderExpiredRoot = CreateLeaf(
                "CN=CVC Leaf Under Expired Root", m_expiredRootCa, s_pastFrom, s_pastTo);

            m_leafDigitalSignatureOnly = CertificateBuilder
                .Create("CN=CVC Leaf DigitalSignature Only")
                .SetNotBefore(s_leafFrom)
                .SetNotAfter(s_leafTo)
                .AddExtension(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true))
                .SetIssuer(m_rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            m_weakIssuerCa = CertificateBuilder
                .Create("CN=CVC Weak Issuer CA, O=OPC Foundation")
                .SetNotBefore(s_rootFrom)
                .SetNotAfter(s_rootTo)
                .SetCAConstraint(-1)
                .AddExtension(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            m_leafUnderWeakIssuer = CreateLeaf(
                "CN=CVC Leaf Under Weak Issuer", m_weakIssuerCa, s_leafFrom, s_leafTo);

            m_ecdsaTrustedLeaf = CertificateBuilder
                .Create("CN=CVC ECDSA Leaf")
                .SetNotBefore(s_leafFrom)
                .SetNotAfter(s_leafTo)
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();

            m_ecdsaWeakHashLeaf = CertificateBuilder
                .Create("CN=CVC ECDSA Weak Hash")
                .SetNotBefore(s_leafFrom)
                .SetNotAfter(s_leafTo)
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();

            m_ecdsaNoDigitalSignatureLeaf = CertificateBuilder
                .Create("CN=CVC ECDSA No DigitalSignature")
                .SetNotBefore(s_leafFrom)
                .SetNotAfter(s_leafTo)
                .AddExtension(
                    new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation, critical: true))
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_rootCa?.Dispose();
            m_leaf?.Dispose();
            m_intermediateCa?.Dispose();
            m_leafUnderIntermediate?.Dispose();
            m_selfSignedApp?.Dispose();
            m_expiredLeaf?.Dispose();
            m_notYetValidLeaf?.Dispose();
            m_expiredRootCa?.Dispose();
            m_leafUnderExpiredRoot?.Dispose();
            m_leafDigitalSignatureOnly?.Dispose();
            m_weakIssuerCa?.Dispose();
            m_leafUnderWeakIssuer?.Dispose();
            m_ecdsaTrustedLeaf?.Dispose();
            m_ecdsaWeakHashLeaf?.Dispose();
            m_ecdsaNoDigitalSignatureLeaf?.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (CertificateValidationCore core in m_cores)
            {
                core.Dispose();
            }
            m_cores.Clear();

            foreach (string dir in m_tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // best effort cleanup; the OS releases the handles shortly after.
                }
            }
            m_tempDirs.Clear();
        }

        [Test]
        public void ValidateAsyncNullChainThrowsArgumentNullException()
        {
            CertificateValidationCore core = NewCore();

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(
                () => core.ValidateAsync(null, null, null, CancellationToken.None));

            Assert.That(ex.ParamName, Is.EqualTo("chain"));
        }

        [Test]
        public async Task ValidateAsyncEmptyChainReturnsBadCertificateInvalidAsync()
        {
            CertificateValidationCore core = NewCore();
            using var chain = new CertificateCollection();

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(result.IsSuppressible, Is.False);
        }

        [Test]
        public void UpdateAsyncNullConfigurationThrowsArgumentNullException()
        {
            CertificateValidationCore core = NewCore();

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(
                () => core.UpdateAsync(null));

            Assert.That(ex.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public async Task ValidateAsyncUntrustedSelfSignedReturnsBadCertificateUntrustedAsync()
        {
            CertificateValidationCore core = NewCore();
            using CertificateCollection chain = Chain(m_selfSignedApp);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncUnknownIssuerReturnsBadCertificateChainIncompleteAsync()
        {
            CertificateValidationCore core = NewCore();
            using CertificateCollection chain = Chain(m_leafUnderIntermediate);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateChainIncomplete));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncTrustedRootChainReturnsSuccessAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_leaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ValidateAsyncIntermediateChainReturnsSuccessAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            string issuerDir = await WriteStoreAsync([m_intermediateCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir, issuerDir);
            using CertificateCollection chain = Chain(m_leafUnderIntermediate);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ValidateAsyncSelfSignedInTrustedStoreReturnsSuccessAsync()
        {
            string trustedDir = await WriteStoreAsync([m_selfSignedApp]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_selfSignedApp);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ValidateAsyncExpiredLeafReturnsBadCertificateTimeInvalidAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_expiredLeaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateTimeInvalid));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncNotYetValidLeafReturnsBadCertificateTimeInvalidAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_notYetValidLeaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateTimeInvalid));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncExpiredIssuerReturnsBadCertificateIssuerTimeInvalidAsync()
        {
            string trustedDir = await WriteStoreAsync([m_expiredRootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_leafUnderExpiredRoot);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                ContainsStatusCode(result, StatusCodes.BadCertificateIssuerTimeInvalid), Is.True);
        }

        [Test]
        public async Task ValidateAsyncLeafWithoutDataEnciphermentReturnsBadCertificateUseNotAllowedAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_leafDigitalSignatureOnly);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUseNotAllowed));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncWeakIssuerKeyUsageReturnsBadCertificateIssuerUseNotAllowedAsync()
        {
            string trustedDir = await WriteStoreAsync([m_weakIssuerCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_leafUnderWeakIssuer);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                ContainsStatusCode(result, StatusCodes.BadCertificateIssuerUseNotAllowed), Is.True);
        }

        [Test]
        public async Task ValidateAsyncKeySizeBelowMinimumReturnsBadCertificatePolicyCheckFailedAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            core.MinimumCertificateKeySize = 3072;
            using CertificateCollection chain = Chain(m_leaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.StatusCode, Is.EqualTo(StatusCodes.BadCertificatePolicyCheckFailed));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncRevokedLeafReturnsBadCertificateRevokedAsync()
        {
            var crl = new X509CRL(CrlBuilder
                .Create(m_rootCa.SubjectName)
                .AddRevokedCertificate(m_leaf)
                .CreateForRSA(m_rootCa));
            string trustedDir = await WriteStoreAsync([m_rootCa], [crl]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_leaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateRevoked));
            Assert.That(result.IsSuppressible, Is.False);
        }

        [Test]
        public async Task ValidateAsyncUnknownRevocationRejectedReturnsBadCertificateRevocationUnknownAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            core.RejectUnknownRevocationStatus = true;
            using CertificateCollection chain = Chain(m_leaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                ContainsStatusCode(result, StatusCodes.BadCertificateRevocationUnknown), Is.True);
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncAutoAcceptUntrustedReturnsSuccessAsync()
        {
            CertificateValidationCore core = NewCore();
            core.AutoAcceptUntrustedCertificates = true;
            using CertificateCollection chain = Chain(m_selfSignedApp);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ValidateAsyncAcceptErrorCallbackAcceptsUntrustedAsync()
        {
            CertificateValidationCore core = NewCore();
            StatusCode observed = default;
            using CertificateCollection chain = Chain(m_selfSignedApp);

            CertificateValidationResult result = await core.ValidateAsync(
                chain,
                (_, error) =>
                {
                    observed = error.StatusCode;
                    return true;
                },
                null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(observed, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
        }

        [Test]
        public async Task ValidateAsyncThrowingAcceptErrorIsRejectedAsync()
        {
            CertificateValidationCore core = NewCore();
            using CertificateCollection chain = Chain(m_selfSignedApp);

            CertificateValidationResult result = await core.ValidateAsync(
                chain,
                (_, _) => throw new InvalidOperationException("callback failure"),
                null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncUsesValidatedCertificateFastPathAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            core.UseValidatedCertificates = true;

            using (CertificateCollection first = Chain(m_leaf))
            {
                CertificateValidationResult firstResult = await core.ValidateAsync(
                    first, null, null, CancellationToken.None).ConfigureAwait(false);
                Assert.That(firstResult.IsValid, Is.True);
            }

            using CertificateCollection second = Chain(m_leaf);
            CertificateValidationResult secondResult = await core.ValidateAsync(
                second, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(secondResult.IsValid, Is.True);
            Assert.That(secondResult.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task GetIssuersAsyncWithTrustedRootReturnsTrueAsync()
        {
            string trustedDir = await WriteStoreAsync([m_rootCa]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            var issuers = new List<CertificateIssuerReference>();

            using var leaf = Certificate.FromRawData(m_leaf.RawData);
            bool trusted = await core.GetIssuersAsync(leaf, issuers, CancellationToken.None).ConfigureAwait(false);

            try
            {
                Assert.That(trusted, Is.True);
                Assert.That(issuers, Has.Count.EqualTo(1));
                Assert.That(issuers[0].Certificate.Subject, Is.EqualTo(m_rootCa.Subject));
            }
            finally
            {
                foreach (CertificateIssuerReference issuer in issuers)
                {
                    issuer.Certificate.Dispose();
                }
            }
        }

        [Test]
        public async Task GetIssuersAsyncWithSelfSignedReturnsFalseAsync()
        {
            CertificateValidationCore core = NewCore();
            var issuers = new List<CertificateIssuerReference>();

            using var app = Certificate.FromRawData(m_selfSignedApp.RawData);
            bool trusted = await core.GetIssuersAsync(app, issuers, CancellationToken.None).ConfigureAwait(false);

            Assert.That(trusted, Is.False);
            Assert.That(issuers, Is.Empty);
        }

        [Test]
        public void ValidateDomainsMismatchThrowsBadCertificateHostNameInvalid()
        {
            CertificateValidationCore core = NewCore();
            ConfiguredEndpoint endpoint = CreateEndpoint(
                "opc.tcp://cvc-mismatch-host.invalid:4840", "urn:cvc:test:server");

            Assert.That(
                () => core.ValidateDomains(m_leaf, endpoint, serverValidation: false, null),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateHostNameInvalid));
        }

        [Test]
        public void ValidateDomainsAcceptErrorSuppressesHostNameError()
        {
            CertificateValidationCore core = NewCore();
            ConfiguredEndpoint endpoint = CreateEndpoint(
                "opc.tcp://cvc-mismatch-host.invalid:4840", "urn:cvc:test:server");
            StatusCode observed = default;

            Assert.That(
                () => core.ValidateDomains(
                    m_leaf,
                    endpoint,
                    serverValidation: false,
                    (_, error) =>
                    {
                        observed = error.StatusCode;
                        return true;
                    }),
                Throws.Nothing);

            Assert.That(observed, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
        }

        [Test]
        public void ValidateApplicationUriMissingUriThrowsBadCertificateUriInvalid()
        {
            CertificateValidationCore core = NewCore();
            ConfiguredEndpoint endpoint = CreateEndpoint(
                "opc.tcp://cvc-host:4840", "urn:cvc:test:server:appuri");

            Assert.That(
                () => core.ValidateApplicationUri(m_leaf, endpoint, null),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateUriInvalid));
        }

        [Test]
        public void ValidateApplicationUriAcceptErrorSuppressesUriError()
        {
            CertificateValidationCore core = NewCore();
            ConfiguredEndpoint endpoint = CreateEndpoint(
                "opc.tcp://cvc-host:4840", "urn:cvc:test:server:appuri");
            StatusCode observed = default;

            Assert.That(
                () => core.ValidateApplicationUri(
                    m_leaf,
                    endpoint,
                    (_, error) =>
                    {
                        observed = error.StatusCode;
                        return true;
                    }),
                Throws.Nothing);

            Assert.That(observed, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
        }

        [Test]
        public void PropertySettersRoundTripValues()
        {
            CertificateValidationCore core = NewCore();

            core.AutoAcceptUntrustedCertificates = true;
            core.RejectSHA1SignedCertificates = false;
            core.RejectUnknownRevocationStatus = true;
            core.MinimumCertificateKeySize = 3072;
            core.UseValidatedCertificates = true;

            Assert.That(core.AutoAcceptUntrustedCertificates, Is.True);
            Assert.That(core.RejectSHA1SignedCertificates, Is.False);
            Assert.That(core.RejectUnknownRevocationStatus, Is.True);
            Assert.That(core.MinimumCertificateKeySize, Is.EqualTo((ushort)3072));
            Assert.That(core.UseValidatedCertificates, Is.True);
        }

        [Test]
        public async Task ValidateAsyncEcdsaTrustedLeafReturnsSuccessAsync()
        {
            string trustedDir = await WriteStoreAsync([m_ecdsaTrustedLeaf]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_ecdsaTrustedLeaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ValidateAsyncEcdsaWeakHashReturnsBadCertificatePolicyCheckFailedAsync()
        {
            string trustedDir = await WriteStoreAsync([m_ecdsaWeakHashLeaf]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_ecdsaWeakHashLeaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.StatusCode, Is.EqualTo(StatusCodes.BadCertificatePolicyCheckFailed));
            Assert.That(result.IsSuppressible, Is.True);
        }

        [Test]
        public async Task ValidateAsyncEcdsaWithoutDigitalSignatureReturnsUseNotAllowedAsync()
        {
            string trustedDir = await WriteStoreAsync([m_ecdsaNoDigitalSignatureLeaf]).ConfigureAwait(false);
            CertificateValidationCore core = NewCore(trustedDir);
            using CertificateCollection chain = Chain(m_ecdsaNoDigitalSignatureLeaf);

            CertificateValidationResult result = await core.ValidateAsync(
                chain, null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                ContainsStatusCode(result, StatusCodes.BadCertificateUseNotAllowed), Is.True);
            Assert.That(result.IsSuppressible, Is.True);
        }

        private static Certificate CreateLeaf(
            string subjectName, Certificate issuer, DateTime notBefore, DateTime notAfter)
        {
            return CertificateBuilder
                .Create(subjectName)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static ConfiguredEndpoint CreateEndpoint(string endpointUrl, string applicationUri)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                Server = new ApplicationDescription { ApplicationUri = applicationUri }
            };
            return new ConfiguredEndpoint(null, description);
        }

        private static CertificateCollection Chain(params Certificate[] certificates)
        {
            var chain = new CertificateCollection();
            foreach (Certificate certificate in certificates)
            {
                chain.Add(certificate);
            }
            return chain;
        }

        private static bool ContainsStatusCode(CertificateValidationResult result, StatusCode code)
        {
            foreach (ServiceResult error in result.Errors)
            {
                ServiceResult current = error;
                while (current != null)
                {
                    if (current.StatusCode == code)
                    {
                        return true;
                    }
                    current = current.InnerResult;
                }
            }
            return false;
        }

        private CertificateValidationCore NewCore()
        {
            var core = new CertificateValidationCore(m_telemetry);
            m_cores.Add(core);
            return core;
        }

        private CertificateValidationCore NewCore(string trustedDir, string issuerDir = null)
        {
            CertificateValidationCore core = NewCore();
            core.Update(
                issuerDir == null ? null : TrustList(issuerDir),
                trustedDir == null ? null : TrustList(trustedDir),
                null);
            return core;
        }

        private static CertificateTrustList TrustList(string dir)
        {
            return new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = dir
            };
        }

        private string NewTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "opcua-cvc-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(dir);
            m_tempDirs.Add(dir);
            return dir;
        }

        private async Task<string> WriteStoreAsync(
            IEnumerable<Certificate> certificates,
            IEnumerable<X509CRL> crls = null)
        {
            string dir = NewTempDir();
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(dir, noPrivateKeys: true);
            foreach (Certificate certificate in certificates)
            {
                await store.AddAsync(certificate).ConfigureAwait(false);
            }
            if (crls != null)
            {
                foreach (X509CRL crl in crls)
                {
                    await store.AddCRLAsync(crl).ConfigureAwait(false);
                }
            }
            return dir;
        }
    }
}
