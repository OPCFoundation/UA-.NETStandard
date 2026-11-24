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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Tests for the CSR checks of <see cref="CertificateGroup"/>.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public sealed class CertificateGroupSigningRequestTests
    {
        private const string ApplicationUri = "urn:localhost:test:csr";
        private const string ProductInstanceUri = "urn:localhost:test:product";
        private const string UnapprovedUri = "urn:localhost:test:unapproved";

        /// <summary>
        /// OPC 10000-12 §7.9.3: the ApplicationUri shall be specified in the CSR.
        /// </summary>
        [Test]
        public void CsrWithoutApplicationUriIsRejected()
        {
            using var certificateGroup = new CertificateGroup(NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(application, CreateSigningRequest(null))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateUriInvalid));
            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(application, CreateSigningRequest("urn:other"))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateUriInvalid));
            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(application, CreateSigningRequest(ApplicationUri))
                    .ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public async Task SigningRequestAddsApprovedUriAbsentFromCsrAndOmitsUnapprovedUris()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            using Certificate issued = await certificateGroup.SigningRequestWithAdditionalUriSansAsync(
                application,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                [],
                CreateSigningRequestWithUris(ApplicationUri, UnapprovedUri),
                [ProductInstanceUri]).ConfigureAwait(false);

            X509SubjectAltNameExtension altNames = issued.FindExtension<X509SubjectAltNameExtension>()!;
            Assert.That(altNames.Uris, Is.EqualTo(s_applicationAndProductUris));
        }

        [Test]
        public async Task SigningRequestRejectsInvalidApprovedUri()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestWithAdditionalUriSansAsync(
                    application,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [],
                    CreateSigningRequestWithUris(ApplicationUri),
                    ["not an absolute URI"]).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
        }

        [Test]
        public async Task SigningRequestStillRequiresApplicationUriInCsr()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestWithAdditionalUriSansAsync(
                    application,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [],
                    CreateSigningRequestWithUris(ProductInstanceUri),
                    [ProductInstanceUri]).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
        }

        [Test]
        public async Task LegacySigningRequestOverrideIsPreservedAndApprovedUriSigningRequiresOptIn()
        {
            using Certificate expected = CertificateBuilder
                .Create("CN=Legacy CA")
                .CreateForRSA();
            using var certificateGroup = new LegacyCertificateGroup(
                expected,
                NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            using Certificate actual = await certificateGroup.SigningRequestAsync(
                application,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                [],
                CreateSigningRequest(ApplicationUri)).ConfigureAwait(false);

            Assert.That(actual.Thumbprint, Is.EqualTo(expected.Thumbprint));
            Assert.That(certificateGroup.LegacySigningRequestCalls, Is.EqualTo(1));

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestWithAdditionalUriSansAsync(
                    application,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [],
                    CreateSigningRequest(ApplicationUri),
                    [ProductInstanceUri]).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(certificateGroup.LegacySigningRequestCalls, Is.EqualTo(1));

            exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    "CN=Approved Device",
                    ApplicationUri,
                    ProductInstanceUri,
                    [],
                    [],
                    CreateSigningRequest(null)).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(certificateGroup.LegacySigningRequestCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task AuthenticatedEvidenceSigningUsesOnlyHostApprovedSubjectAndSans()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());
            using var deviceKey = RSA.Create(2048);

            using Certificate issued = await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                "CN=Approved Device",
                ApplicationUri,
                ProductInstanceUri,
                [],
                ["approved.example", "192.0.2.10"],
                CreateSigningRequestWithUris(deviceKey, UnapprovedUri)).ConfigureAwait(false);

            X509SubjectAltNameExtension altNames = issued.FindExtension<X509SubjectAltNameExtension>()!;
            using RSA issuedKey = issued.GetRSAPublicKey()!;
            RSAParameters expectedKey = deviceKey.ExportParameters(false);
            RSAParameters actualKey = issuedKey.ExportParameters(false);
            Assert.That(issued.Subject, Does.Contain("CN=Approved Device"));
            Assert.That(issued.Subject, Does.Not.Contain("CN=CsrTest"));
            Assert.That(altNames.Uris, Is.EqualTo(s_applicationAndProductUris));
            Assert.That(altNames.DomainNames, Is.EqualTo(s_approvedDnsNames));
            Assert.That(altNames.IPAddresses, Is.EqualTo(s_approvedIpAddresses));
            Assert.That(actualKey.Modulus, Is.EqualTo(expectedKey.Modulus));
            Assert.That(actualKey.Exponent, Is.EqualTo(expectedKey.Exponent));
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Checks that authenticated signing verifies and preserves a P-256 CSR key.
        /// </summary>
        [Test]
        public async Task AuthenticatedEvidenceSigningPreservesP256CsrKey()
        {
            ECCurve curve = ECCurve.NamedCurves.nistP256;
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .SetECCurve(curve)
                .CreateForECDsa();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create(),
                nameof(Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType));
            using var deviceKey = ECDsa.Create(curve);
            var request = new CertificateRequest("CN=CsrTest", deviceKey, HashAlgorithmName.SHA256);

            using Certificate issued = await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                "CN=Approved Device",
                ApplicationUri,
                ProductInstanceUri,
                [],
                [],
                ByteString.From(request.CreateSigningRequest())).ConfigureAwait(false);

            using ECDsa issuedKey = issued.GetECDsaPublicKey()!;
            Assert.That(issuedKey.ExportSubjectPublicKeyInfo(), Is.EqualTo(deviceKey.ExportSubjectPublicKeyInfo()));
        }
#endif

        [Test]
        public async Task AuthenticatedEvidenceSigningAllowsApplicationAndProductUriToBeSame()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());

            using Certificate issued = await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                "CN=Approved Device",
                ApplicationUri,
                ApplicationUri,
                [],
                [],
                CreateSigningRequest(null)).ConfigureAwait(false);

            X509SubjectAltNameExtension altNames = issued.FindExtension<X509SubjectAltNameExtension>()!;
            Assert.That(altNames.Uris, Is.EqualTo(s_applicationUris));
        }

        /// <summary>
        /// Checks that the configured issuer's credential still needs explicit registrar approval.
        /// </summary>
        [Test]
        public async Task ConfiguredIssuerCertificateRequiresExternalRegistrarApproval()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string path = Path.Combine(Path.GetTempPath(), "gds-issuer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                var configuration = new CertificateGroupConfiguration
                {
                    SubjectName = "CN=GDS External Test CA",
                    BaseStorePath = path,
                    CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
                };
                using var factory = new CertificateGroup(telemetry);
                using ICertificateGroup group = factory.Create(Path.Combine(path, "authorities"), configuration);
                await group.InitAsync().ConfigureAwait(false);
                Assert.That(group, Is.InstanceOf<ICertificateGroupWithAuthenticatedSigningRequest>());
                var issuer = (ICertificateGroupWithAuthenticatedSigningRequest)group;
                using Certificate issued = await issuer.SigningRequestForAuthenticatedEvidenceAsync(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    "CN=Approved External DCA",
                    ApplicationUri,
                    ProductInstanceUri,
                    [],
                    [],
                    CreateSigningRequest(null)).ConfigureAwait(false);
                ByteString der = ByteString.From(issued.RawData.AsSpan());

                await using var certificates = new CertificateManager(telemetry);
                certificates.RegisterTrustList(
                    ExternalAuthenticationProvider.AuthenticationAuthorities, configuration.TrustedListPath);
                var decision = new DeviceRegistrarExternalAuthenticationResult(
                    ProductInstanceUri,
                    new DeviceRegistrarOnboardingDecision(
                        new ApplicationRecordDataType { ApplicationUri = ApplicationUri }, false, null));
                var policy = new Mock<IDeviceRegistrarExternalAuthenticationPolicy>(MockBehavior.Strict);
                policy.SetupSequence(value => value.EvaluateAsync(
                    der,
                    It.Is<ArrayOf<string>>(uris =>
                        uris.Count == 2 && uris.Contains(ApplicationUri) && uris.Contains(ProductInstanceUri)),
                    It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied))
                    .ReturnsAsync(decision);
                var provider = new ExternalAuthenticationProvider(certificates, policy.Object);

                ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                    () => provider.EvaluateAsync(der).AsTask());
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(await provider.EvaluateAsync(der).ConfigureAwait(false), Is.SameAs(decision));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        /// <summary>
        /// Distinguishes an unsupported group type from a configured type without an initialized CA.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task AuthenticatedEvidenceSigningReportsUnavailableAuthority(bool typeConfigured)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=Uninitialized CA",
                BaseStorePath = "unused",
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            using var factory = new CertificateGroup(telemetry);
            using ICertificateGroup group = factory.Create("unused", configuration);
            var issuer = (ICertificateGroupWithAuthenticatedSigningRequest)group;
            NodeId type = typeConfigured
                ? Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
                : Ua.ObjectTypeIds.RsaMinApplicationCertificateType;

            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await issuer.SigningRequestForAuthenticatedEvidenceAsync(
                    type,
                    "CN=Approved External DCA",
                    ApplicationUri,
                    ProductInstanceUri,
                    [],
                    [],
                    CreateSigningRequest(null)).ConfigureAwait(false));

            Assert.That(error.StatusCode,
                Is.EqualTo(typeConfigured ? StatusCodes.BadConfigurationError : StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task AuthenticatedEvidenceSigningRejectsInvalidApprovedIdentity()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    "CN=Approved Device",
                    "invalid application uri",
                    ProductInstanceUri,
                    [],
                    [],
                    CreateSigningRequest(null)).ConfigureAwait(false));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));

            exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    "CN=Approved Device",
                    ApplicationUri,
                    ProductInstanceUri,
                    [],
                    ["bad domain name!"],
                    CreateSigningRequest(null)).ConfigureAwait(false));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AuthenticatedEvidenceSigningChecksCsrSignatureAndKeyType()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());
            ByteString validCsr = CreateSigningRequest(null);
            byte[] tamperedCsrBytes = validCsr.ToArray();
            tamperedCsrBytes[^1] ^= 0x01;
            ByteString tamperedCsr = ByteString.From(tamperedCsrBytes);

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    "CN=Approved Device",
                    ApplicationUri,
                    ProductInstanceUri,
                    [],
                    [],
                    tamperedCsr).ConfigureAwait(false));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(exception.Message, Does.Contain("CSR signature invalid"));

            exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                    "CN=Approved Device",
                    ApplicationUri,
                    ProductInstanceUri,
                    [],
                    [],
                    validCsr).ConfigureAwait(false));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AuthenticatedEvidenceSigningRejectsTrailingCsrData()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());
            byte[] csr = [.. CreateSigningRequest(null).Span, 0x05, 0x00];

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup.SigningRequestForAuthenticatedEvidenceAsync(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    "CN=Approved Device",
                    ApplicationUri,
                    ProductInstanceUri,
                    [],
                    [],
                    ByteString.From(csr)).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task OrdinarySigningRequestOmitsUnapprovedUrisAndPreservesDnsNames()
        {
            using Certificate signingCertificate = CertificateBuilder
                .Create("CN=Test CA")
                .SetCAConstraint()
                .CreateForRSA();
            using var certificateGroup = new TestCertificateGroup(
                signingCertificate,
                NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            using Certificate issued = await certificateGroup.SigningRequestAsync(
                application,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                ["fallback.example"],
                CreateSigningRequestWithUris(ApplicationUri, UnapprovedUri)).ConfigureAwait(false);

            X509SubjectAltNameExtension altNames = issued.FindExtension<X509SubjectAltNameExtension>()!;
            Assert.That(altNames.Uris, Is.EqualTo(s_applicationUris));
            Assert.That(altNames.DomainNames, Is.EqualTo(s_csrDnsNames));
        }

        /// <summary>
        /// OPC 10000-12 §7.9.3: a CSR whose key does not fit the requested
        /// CertificateTypeId is rejected by StartSigningRequest with
        /// Bad_InvalidArgument, not accepted and failed in FinishRequest.
        /// </summary>
        [Test]
        public async Task RsaCsrForEccCertificateTypeIsRejected()
        {
            using var certificateGroup = new CertificateGroup(NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            ServiceResultException sre = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(
                        application,
                        Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                        CreateSigningRequest(ApplicationUri))
                    .ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(sre.Message, Does.Contain("RSA").And.Contain("ECC"));

            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(
                        application,
                        Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CreateSigningRequest(ApplicationUri))
                    .ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public void EccCsrForRsaCertificateTypeIsRejected()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    CreateEccSigningRequest(ECCurve.NamedCurves.nistP256)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(sre.Message, Does.Contain("nistP256").And.Contain("RSA"));
        }

        [Test]
        public void EccCsrWithOtherCurveIsRejected()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                    CreateEccSigningRequest(ECCurve.NamedCurves.nistP384)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(sre.Message, Does.Contain("nistP384").And.Contain("nistP256"));
        }

        /// <summary>
        /// OPC 10000-12 §7.8.4.8 / §7.8.4.9 key sizes; §7.9.3 returns
        /// Bad_NotSupported for an unsupported key size.
        /// </summary>
        [Test]
        public void RsaKeySizeOutsideCertificateTypeRangeIsNotSupported()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    CreateSigningRequest(ApplicationUri, 1024)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(sre.Message, Does.Contain("1024"));

            sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                    CreateSigningRequest(ApplicationUri, 3072)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(sre.Message, Does.Contain("3072"));
        }

        [Test]
        public void UnsupportedEccCertificateTypeIsNotSupported()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType,
                    CreateEccSigningRequest(ECCurve.NamedCurves.nistP256)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void MatchingCsrKeysAreAccepted()
        {
            ByteString rsa2048 = CreateSigningRequest(ApplicationUri);
            ByteString nistP256 = CreateEccSigningRequest(ECCurve.NamedCurves.nistP256);

            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, rsa2048),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType, rsa2048),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.ApplicationCertificateType, rsa2048),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType, nistP256),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccApplicationCertificateType, nistP256),
                Throws.Nothing);
        }

        /// <summary>
        /// Each ECC certificate type accepts only its own named curve; the
        /// abstract EccApplicationCertificateType accepts any supported one.
        /// <paramref name="requiredCurve"/> is the curve the type requires.
        /// </summary>
        [TestCase("EccNistP256ApplicationCertificateType", "nistP256", "nistP256")]
        [TestCase("EccNistP256ApplicationCertificateType", "nistP384", "nistP256")]
        [TestCase("EccNistP384ApplicationCertificateType", "nistP384", "nistP384")]
        [TestCase("EccNistP384ApplicationCertificateType", "nistP256", "nistP384")]
        [TestCase("EccBrainpoolP256r1ApplicationCertificateType", "nistP256", "brainpoolP256r1")]
        [TestCase("EccBrainpoolP384r1ApplicationCertificateType", "nistP384", "brainpoolP384r1")]
        [TestCase("EccApplicationCertificateType", "nistP384", "nistP384")]
        public void EccCurveMustMatchCertificateType(
            string certificateTypeName,
            string curveName,
            string requiredCurve)
        {
            NodeId certificateType = Ua.ObjectTypeIds.GetIdentifier(certificateTypeName);
            ByteString request = CreateEccSigningRequest(
                curveName == "nistP256" ? ECCurve.NamedCurves.nistP256 : ECCurve.NamedCurves.nistP384);

            // the abstract type accepts every supported curve
            if (curveName == requiredCurve)
            {
                Assert.That(
                    () => CertificateGroup.VerifySigningRequestKey(certificateType, request),
                    Throws.Nothing);
                return;
            }

            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(certificateType, request));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(sre.Message, Does.Contain(curveName).And.Contain(requiredCurve));
        }

        [Test]
        public void RsaCsrWithinCertificateTypeRangeIsAccepted()
        {
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    CreateSigningRequest(ApplicationUri, 3072)),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                    CreateSigningRequest(ApplicationUri, 1024)),
                Throws.Nothing);
        }

        [Test]
        public void UndecodableCsrIsRejected()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    ByteString.From([0x30, 0x03, 0x02, 0x01, 0x00])));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// OPC 10000-12 §7.9.3: a public key algorithm the GDS does not
        /// support returns Bad_NotSupported, also through the typed overload
        /// whose signature verification would otherwise report
        /// Bad_InvalidArgument for it.
        /// </summary>
        [Test]
        public async Task UnsupportedKeyAlgorithmIsNotSupported()
        {
            using var certificateGroup = new CertificateGroup(NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };
            ByteString dsaRequest = CreateUnsupportedKeySigningRequest();

            ServiceResultException sre = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(
                        application,
                        Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        dsaRequest)
                    .ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(sre.Message, Does.Contain(DsaOid));

            sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                    dsaRequest));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        private const string DsaOid = "1.2.840.10040.4.1";

        /// <summary>
        /// A CSR with a DSA SubjectPublicKeyInfo and a placeholder signature.
        /// </summary>
        private static ByteString CreateUnsupportedKeySigningRequest()
        {
            var publicKey = new PublicKey(
                new Oid(DsaOid),
                new AsnEncodedData([0x05, 0x00]),
                new AsnEncodedData([0x02, 0x01, 0x01]));
            var request = new CertificateRequest(
                new X500DistinguishedName("CN=CsrTest"),
                publicKey,
                HashAlgorithmName.SHA256);
            AddApplicationUri(request, ApplicationUri);
            return ByteString.From(
                request.CreateSigningRequest(new PlaceholderSignatureGenerator(publicKey)));
        }

        private sealed class PlaceholderSignatureGenerator : X509SignatureGenerator
        {
            private readonly PublicKey m_publicKey;

            public PlaceholderSignatureGenerator(PublicKey publicKey)
            {
                m_publicKey = publicKey;
            }

            public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
            {
                // sha256WithRSAEncryption, NULL parameters
                return
                [
                    0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86,
                    0xF7, 0x0D, 0x01, 0x01, 0x0B, 0x05, 0x00
                ];
            }

            public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
            {
                return new byte[64];
            }

            protected override PublicKey BuildPublicKey()
            {
                return m_publicKey;
            }
        }

        private static ByteString CreateSigningRequest(string? applicationUri, int keySize = 2048)
        {
            using var rsa = RSA.Create(keySize);
            var request = new CertificateRequest(
                "CN=CsrTest",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            AddApplicationUri(request, applicationUri);
            return ByteString.From(request.CreateSigningRequest());
        }

        private static ByteString CreateSigningRequestWithUris(params string[] applicationUris)
        {
            using var rsa = RSA.Create(2048);
            return CreateSigningRequestWithUris(rsa, applicationUris);
        }

        private static ByteString CreateSigningRequestWithUris(RSA rsa, params string[] applicationUris)
        {
            var request = new CertificateRequest(
                "CN=CsrTest",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var altNames = new SubjectAlternativeNameBuilder();
            foreach (string uri in applicationUris)
            {
                altNames.AddUri(new Uri(uri));
            }
            altNames.AddDnsName("localhost");
            request.CertificateExtensions.Add(altNames.Build());
            return ByteString.From(request.CreateSigningRequest());
        }

        private static ByteString CreateEccSigningRequest(ECCurve curve)
        {
            using var ecdsa = ECDsa.Create(curve);
            var request = new CertificateRequest("CN=CsrTest", ecdsa, HashAlgorithmName.SHA256);
            AddApplicationUri(request, ApplicationUri);
            return ByteString.From(request.CreateSigningRequest());
        }

        private static void AddApplicationUri(CertificateRequest request, string? applicationUri)
        {
            if (applicationUri != null)
            {
                var altNames = new SubjectAlternativeNameBuilder();
                altNames.AddUri(new Uri(applicationUri));
                altNames.AddDnsName("localhost");
                request.CertificateExtensions.Add(altNames.Build());
            }
        }

        private static readonly string[] s_applicationAndProductUris = [ApplicationUri, ProductInstanceUri];
        private static readonly string[] s_applicationUris = [ApplicationUri];
        private static readonly string[] s_approvedDnsNames = ["approved.example"];
        private static readonly string[] s_approvedIpAddresses = ["192.0.2.10"];
        private static readonly string[] s_csrDnsNames = ["localhost"];

        private sealed class TestCertificateGroup : CertificateGroup
        {
            public TestCertificateGroup(
                Certificate signingCertificate,
                ITelemetryContext telemetry,
                string certificateType = nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType))
                : base(
                    "unused",
                    new CertificateGroupConfiguration
                    {
                        SubjectName = "CN=Test CA",
                        BaseStorePath = "unused",
                        CertificateTypes = [certificateType]
                    },
                    telemetry)
            {
                Certificates[Ua.ObjectTypeIds.GetIdentifier(certificateType)] = signingCertificate.AddRef();
            }

            public override Task<Certificate> SigningRequestWithAdditionalUriSansAsync(
                ApplicationRecordDataType application,
                NodeId certificateType,
                ArrayOf<string> domainNames,
                ByteString certificateRequest,
                ArrayOf<string> approvedAdditionalUriSans,
                CancellationToken ct = default)
            {
                return SigningRequestWithApprovedUriSansAsync(
                    application,
                    certificateType,
                    domainNames.Span.ToArray(),
                    certificateRequest,
                    approvedAdditionalUriSans,
                    ct);
            }

            public override Task<Certificate> SigningRequestForAuthenticatedEvidenceAsync(
                NodeId certificateType,
                string approvedSubjectName,
                string applicationUri,
                string productInstanceUri,
                ArrayOf<string> approvedAdditionalUriSans,
                ArrayOf<string> approvedDomainNames,
                ByteString certificateRequest,
                CancellationToken ct = default)
            {
                return SigningRequestWithApprovedIdentityAsync(
                    certificateType,
                    approvedSubjectName,
                    applicationUri,
                    productInstanceUri,
                    approvedAdditionalUriSans,
                    approvedDomainNames,
                    certificateRequest,
                    ct);
            }

            public override Task<Certificate> LoadSigningKeyAsync(
                Certificate signingCertificate,
                char[]? signingKeyPassword,
                ITelemetryContext? telemetry = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(signingCertificate.AddRef());
            }
        }

        private sealed class LegacyCertificateGroup : CertificateGroup
        {
            private readonly Certificate m_expectedCertificate;

            public LegacyCertificateGroup(Certificate expectedCertificate, ITelemetryContext telemetry)
                : base(telemetry)
            {
                m_expectedCertificate = expectedCertificate.AddRef();
                Certificates[Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType] =
                    expectedCertificate.AddRef();
            }

            public int LegacySigningRequestCalls { get; private set; }

            public override Task<Certificate> SigningRequestAsync(
                ApplicationRecordDataType application,
                NodeId certificateType,
                string[] domainNames,
                ByteString certificateRequest,
                CancellationToken ct = default)
            {
                LegacySigningRequestCalls++;
                return Task.FromResult(m_expectedCertificate.AddRef());
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_expectedCertificate.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
