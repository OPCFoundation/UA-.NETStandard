/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
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
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingClientSample;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the sample's actual key-selection and pre-installation checks without opening stores or sockets.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingDeviceCredentialsTests
    {
        /// <summary>
        /// Creates a private identity and strict host-owned certificate dependencies for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_identity = CreateIdentity(kApplicationUri);
            m_provider = new Mock<ICertificateProvider>(MockBehavior.Strict);
            m_manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            m_manager.SetupGet(m => m.CertificateProvider).Returns(m_provider.Object);
            m_identifier = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "sample-store-not-opened",
                CertificateType = CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                RawData = m_identity.RawData
            };
            m_configuration = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationUri = kApplicationUri,
                CertificateManager = m_manager.Object,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificates = [m_identifier]
                }
            };
            m_credentials = new OnboardingDeviceCredentials(m_configuration, m_telemetry);
        }

        /// <summary>
        /// Releases the fixture-owned key; production code must not dispose this handle.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_identity.Dispose();
        }

        /// <summary>
        /// Missing configuration, telemetry, or a host-owned manager cannot create a usable provider.
        /// </summary>
        [Test]
        public void ConstructorRejectsMissingHostDependencies()
        {
            Assert.That(() => new OnboardingDeviceCredentials(null!, m_telemetry),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
            Assert.That(() => new OnboardingDeviceCredentials(m_configuration, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
            m_configuration.CertificateManager = null!;
            Assert.That(() => new OnboardingDeviceCredentials(m_configuration, m_telemetry),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Missing profile and credential objects report explicit argument errors rather than dereferencing null.
        /// </summary>
        [Test]
        public async Task CredentialOperationsRejectMissingArgumentsAsync()
        {
            ArgumentNullException? profileError = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await m_credentials.CreateSigningRequestAsync(s_applicationId, null!).ConfigureAwait(false));
            ArgumentNullException? credentialsError = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await m_credentials.InstallAsync(s_applicationId, null!).ConfigureAwait(false));

            Assert.That(profileError!.ParamName, Is.EqualTo("profile"));
            Assert.That(credentialsError!.ParamName, Is.EqualTo("credentials"));
            m_manager.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A registrar may select only an exact certificate whose private key is available in this device.
        /// </summary>
        [Test]
        public async Task ActivateSelectsExactLocalKeyAndClearsInlineCertificateAsync()
        {
            using var cancellation = new CancellationTokenSource();
            m_provider.Setup(p => p.GetPrivateKeyCertificateAsync(
                    It.Is<CertificateIdentifier>(i =>
                        i.Thumbprint == m_identity.Thumbprint &&
                        i.StorePath == m_identifier.StorePath &&
                        i.StoreType == m_identifier.StoreType),
                    null, null, cancellation.Token))
                .Returns(() => new ValueTask<Certificate?>(m_identity.AddRef()));
            m_manager.Setup(m => m.UpdateApplicationCertificateAsync(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                    It.Is<Certificate>(c => c.Thumbprint == m_identity.Thumbprint && c.HasPrivateKey),
                    null, cancellation.Token))
                .Returns(Task.CompletedTask);

            await m_credentials.ActivateAsync(Encoded(m_identity), cancellation.Token).ConfigureAwait(false);

            Assert.That(m_identifier.RawData, Is.Null);
            Assert.That(m_identifier.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            Assert.That(m_identifier.SubjectName, Is.EqualTo(m_identity.Subject));
            Assert.That(m_identity.HasPrivateKey, Is.True);
            m_provider.VerifyAll();
            m_manager.Verify(m => m.UpdateApplicationCertificateAsync(
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType, It.IsAny<Certificate>(),
                null, cancellation.Token), Times.Once);
        }

        /// <summary>
        /// A missing key, public-only certificate, or different key cannot alter the configured active identity.
        /// </summary>
        [TestCase("missing")]
        [TestCase("public-only")]
        [TestCase("different")]
        public async Task ActivateRejectsUnavailableOrDifferentLocalCredentialAsync(string localCredential)
        {
            using Certificate other = CreateIdentity(kApplicationUri);
            m_provider.Setup(p => p.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(), null, null, It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<Certificate?>(localCredential switch
                {
                    "missing" => null,
                    "public-only" => Certificate.FromRawData(m_identity.RawData),
                    _ => other.AddRef()
                }));
            byte[] original = m_identifier.RawData!;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.ActivateAsync(Encoded(m_identity)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(m_identifier.RawData, Is.SameAs(original));
            Assert.That(m_identifier.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            m_manager.Verify(m => m.UpdateApplicationCertificateAsync(
                It.IsAny<NodeId>(), It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The generated request proves possession of the existing device key rather than generating a replacement.
        /// </summary>
        [Test]
        public async Task SigningRequestIsSignedByExistingDeviceKeyAndBindsOneApplicationAsync()
        {
            SetupActiveKey();
            ByteString encoded = await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile)
                .ConfigureAwait(false);
            var request = new Pkcs10CertificationRequest(encoded.ToArray());
            AsnReader sequence = new AsnReader(encoded.Memory, AsnEncodingRules.DER).ReadSequence();
            sequence.ReadEncodedValue();
            sequence.ReadSequence();
            byte[] signature = sequence.ReadBitString(out int unusedBits);
            using RSA publicKey = m_identity.GetRSAPublicKey()!;

            Assert.That(unusedBits, Is.Zero);
            Assert.That(request.Verify(), Is.True);
            Assert.That(request.Subject.RawData, Is.EqualTo(m_identity.SubjectName.RawData));
            Assert.That(publicKey.VerifyData(
                request.GetCertificationRequestInfo(), signature, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1), Is.True);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.CreateSigningRequestAsync(new NodeId("other-device", 0), s_profile)
                    .ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            m_manager.Verify(m => m.AcquireApplicationCertificateByType(
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType), Times.Once);
        }

        /// <summary>
        /// Unsupported certificate types and null application identifiers do not access the device key.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task SigningRequestRejectsInvalidApplicationOrCertificateTypeAsync(bool nullApplication)
        {
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.CreateSigningRequestAsync(
                    nullApplication ? NodeId.Null : s_applicationId,
                    nullApplication ? s_profile : s_profile with { CertificateTypeId = new NodeId("unsupported", 0) })
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            m_manager.Verify(m => m.AcquireApplicationCertificateByType(It.IsAny<NodeId>()), Times.Never);
        }

        /// <summary>
        /// A recreated provider can restore its application/profile binding without issuing a replacement CSR.
        /// </summary>
        [Test]
        public async Task RestoredSigningRequestProvesOriginalKeyAndRetainsProfileAsync()
        {
            SetupActiveKey();
            await m_credentials.RestoreSigningRequestAsync(s_applicationId, s_profile, Encoded(m_identity))
                .ConfigureAwait(false);
            var changed = s_profile with { CertificateGroupId = new NodeId("other-group", 0) };

            ServiceResultException? mismatch = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.CreateSigningRequestAsync(s_applicationId, changed).ConfigureAwait(false));
            Assert.That(mismatch!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            var restored = new PullDcaCredentials(default, [], CompleteTrustList());
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.InstallAsync(new NodeId("other-application", 0), restored).ConfigureAwait(false));
            using Certificate wrongUri = CreateIdentity("urn:other:application");
            ServiceResultException? certificateError = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.InstallAsync(s_applicationId,
                    new PullDcaCredentials(Encoded(wrongUri), [], CompleteTrustList())).ConfigureAwait(false));
            Assert.That(certificateError!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
            m_manager.Verify(m => m.AcquireApplicationCertificateByType(
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType), Times.Once);
            VerifyNoInstallation();
        }

        /// <summary>
        /// A retained checkpoint with another key cannot restore the binding or permit credential installation.
        /// </summary>
        [Test]
        public async Task RestorationRejectsDifferentRetainedKeyBeforeBindingApplicationAsync()
        {
            SetupActiveKey();
            using Certificate other = CreateIdentity(kApplicationUri);

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await m_credentials.RestoreSigningRequestAsync(s_applicationId, s_profile, Encoded(other))
                    .ConfigureAwait(false));
            ServiceResultException? unbound = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.InstallAsync(s_applicationId,
                    new PullDcaCredentials(Encoded(m_identity), [], CompleteTrustList())).ConfigureAwait(false));

            Assert.That(unbound!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            VerifyNoInstallation();
        }

        /// <summary>
        /// Installation cannot precede a CSR or target an application other than the one bound to that CSR.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InstallationRejectsMissingOrDifferentRequestBeforeUsingCertificateAsync(bool requestStarted)
        {
            if (requestStarted)
            {
                SetupActiveKey();
                await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile).ConfigureAwait(false);
            }
            var received = new PullDcaCredentials(default, [], new TrustListDataType());

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.InstallAsync(
                    requestStarted ? new NodeId("other-device", 0) : s_applicationId, received).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            VerifyNoInstallation();
        }

        /// <summary>
        /// A certificate for a different application URI must not reach trust validation or installation.
        /// </summary>
        [Test]
        public async Task InstallationRejectsDifferentApplicationUriAsync()
        {
            SetupActiveKey();
            await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile).ConfigureAwait(false);
            using Certificate other = CreateIdentity("urn:other-device");

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.InstallAsync(s_applicationId,
                    new PullDcaCredentials(Encoded(other), [], CompleteTrustList())).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
            VerifyNoInstallation();
        }

        /// <summary>
        /// Matching the application URI is insufficient when the issued public key differs from the device key.
        /// </summary>
        [Test]
        public async Task InstallationRejectsDifferentPublicKeyBeforeTrustValidationAsync()
        {
            SetupActiveKey();
            await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile).ConfigureAwait(false);
            using Certificate other = CreateIdentity(kApplicationUri);

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await m_credentials.InstallAsync(s_applicationId,
                    new PullDcaCredentials(Encoded(other), [], CompleteTrustList())).ConfigureAwait(false));

            VerifyNoInstallation();
        }

        /// <summary>
        /// An incomplete trust-list download cannot replace any current peer trust.
        /// </summary>
        [Test]
        public async Task InstallationRejectsPartialTrustListAsync()
        {
            SetupActiveKey();
            await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile).ConfigureAwait(false);
            TrustListDataType trust = CompleteTrustList();
            trust.SpecifiedLists = (uint)TrustListMasks.TrustedCertificates;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.InstallAsync(s_applicationId,
                    new PullDcaCredentials(Encoded(m_identity), [], trust)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            VerifyNoInstallation();
        }

        /// <summary>
        /// Received trust is isolated from ambient trust and validation failure prevents every installation effect.
        /// </summary>
        [Test]
        public async Task InstallationUsesReceivedTrustWithoutAcceptingValidationErrorsAsync()
        {
            SetupActiveKey();
            await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile).ConfigureAwait(false);
            using Certificate authority = CreateIdentity("urn:sample:authority");
            TrustListDataType trust = CompleteTrustList();
            trust.TrustedCertificates = [Encoded(authority)];
            trust.IssuerCertificates = [Encoded(m_identity)];
            using var cancellation = new CancellationTokenSource();
            m_manager.Setup(m => m.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), cancellation.Token))
                .Callback<CertificateCollection, TrustListIdentifier?, ValidationOptions?, CancellationToken>(
                    (chain, _, options, _) =>
                    {
                        Assert.That(chain[0].RawData, Is.EqualTo(m_identity.RawData));
                        Assert.That(options!.TrustScope!.TrustedCertificates, Is.EqualTo(trust.TrustedCertificates));
                        Assert.That(options.TrustScope.IssuerCertificates, Is.EqualTo(trust.IssuerCertificates));
                        Assert.That(options.RejectUnknownRevocationStatus, Is.True);
                        Assert.That(options.RejectSHA1SignedCertificates, Is.True);
                        Assert.That(options.MinimumCertificateKeySize, Is.EqualTo((ushort)2048));
                        Assert.That(options.AutoAcceptUntrustedCertificates, Is.False);
                        Assert.That(options.RecordRejectedCertificates, Is.False);
                        Assert.That(options.AcceptError!(m_identity,
                            new ServiceResult(StatusCodes.BadCertificateUntrusted)), Is.False);
                    })
                .ReturnsAsync(new CertificateValidationResult(false, StatusCodes.BadCertificateUntrusted, [], false));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_credentials.InstallAsync(s_applicationId,
                    new PullDcaCredentials(Encoded(m_identity), [], trust), cancellation.Token).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            m_manager.Verify(m => m.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.IsAny<ValidationOptions>(), cancellation.Token), Times.Once);
            VerifyNoInstallation(verifyNoValidation: false);
        }

        /// <summary>
        /// Certificate storage precedes trust replacement and activation; each failure stops subsequent effects.
        /// </summary>
        [TestCase("none")]
        [TestCase("certificate-store")]
        [TestCase("trust-list")]
        [TestCase("activation")]
        public async Task InstallationOrdersEffectsAndClearsPasswordAfterStorageAsync(string failureStage)
        {
            SetupActiveKey();
            var effects = new List<string>();
            char[] password = "test-store-password".ToCharArray();
            var passwordProvider = new Mock<ICertificatePasswordProvider>(MockBehavior.Strict);
            passwordProvider.Setup(p => p.GetPassword(m_identifier)).Returns(password);
            m_configuration.SecurityConfiguration.CertificatePasswordProvider = passwordProvider.Object;
            using var cancellation = new CancellationTokenSource();
            using Certificate authority = CreateIdentity("urn:sample:authority");
            TrustListDataType received = CompleteTrustList();
            received.TrustedCertificates = [Encoded(authority)];
            var failure = new InvalidOperationException("Simulated installation failure.");
            var store = new Mock<ICertificateStore>(MockBehavior.Strict);
            string storePath = m_identifier.StorePath ??
                throw new AssertionException("The application certificate store must have a path.");
            store.Setup(s => s.Open(storePath, false));
            store.Setup(s => s.Dispose());
            store.Setup(s => s.AddAsync(It.IsAny<Certificate>(), password, cancellation.Token))
                .Callback<Certificate, char[]?, CancellationToken>((certificate, actualPassword, _) =>
                {
                    Assert.That(certificate.RawData, Is.EqualTo(m_identity.RawData));
                    Assert.That(certificate.HasPrivateKey, Is.True);
                    Assert.That(actualPassword, Is.EqualTo("test-store-password".ToCharArray()));
                    effects.Add("certificate-store");
                })
                .Returns(() => failureStage == "certificate-store"
                    ? Task.FromException(failure) : Task.CompletedTask);
            var storeProvider = new Mock<ICertificateStoreProvider>(MockBehavior.Strict);
            storeProvider.SetupGet(p => p.StoreTypeName).Returns("SampleCredentialStore");
            storeProvider.Setup(p => p.CreateStore(m_telemetry)).Returns(store.Object);
            m_identifier.StoreType = "SampleCredentialStore";
            m_credentials = new OnboardingDeviceCredentials(
                m_configuration, m_telemetry, [storeProvider.Object]);
            await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile).ConfigureAwait(false);
            m_manager.Setup(m => m.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), cancellation.Token))
                .ReturnsAsync(CertificateValidationResult.Success);
            m_manager.Setup(m => m.WriteTrustListAsync(
                    TrustListIdentifier.Peers, It.IsAny<TrustListData>(), TrustListMasks.All, cancellation.Token))
                .Callback<TrustListIdentifier, TrustListData, TrustListMasks, CancellationToken>(
                    (_, trust, _, _) =>
                    {
                        Assert.That(trust.TrustedCertificates, Has.Count.EqualTo(1));
                        Assert.That(trust.TrustedCertificates[0].RawData, Is.EqualTo(authority.RawData));
                        effects.Add("trust-list");
                    })
                .Returns(() => failureStage == "trust-list" ? Task.FromException(failure) : Task.CompletedTask);
            m_manager.Setup(m => m.UpdateApplicationCertificateAsync(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                    It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(), cancellation.Token))
                .Callback<NodeId, Certificate, CertificateCollection?, CancellationToken>(
                    (_, certificate, issuers, _) =>
                    {
                        Assert.That(certificate.HasPrivateKey, Is.True);
                        Assert.That(certificate.RawData, Is.EqualTo(m_identity.RawData));
                        Assert.That(issuers!, Has.Count.EqualTo(1));
                        Assert.That(issuers[0].RawData, Is.EqualTo(authority.RawData));
                        effects.Add("activation");
                    })
                .Returns(() => failureStage == "activation" ? Task.FromException(failure) : Task.CompletedTask);
            var credentials = new PullDcaCredentials(Encoded(m_identity), [Encoded(authority)], received);

            if (failureStage == "none")
            {
                await m_credentials.InstallAsync(s_applicationId, credentials, cancellation.Token)
                    .ConfigureAwait(false);
                Assert.That(m_identifier.RawData, Is.Null);
                Assert.That(m_identifier.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
                Assert.That(m_identifier.SubjectName, Is.EqualTo(m_identity.Subject));
            }
            else
            {
                InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await m_credentials.InstallAsync(s_applicationId, credentials, cancellation.Token)
                        .ConfigureAwait(false));
                Assert.That(error, Is.SameAs(failure));
                Assert.That(m_identifier.RawData, Is.EqualTo(m_identity.RawData));
                Assert.That(m_identifier.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            }
            string[] expected = failureStage switch
            {
                "certificate-store" => ["certificate-store"],
                "trust-list" => ["certificate-store", "trust-list"],
                _ => ["certificate-store", "trust-list", "activation"]
            };
            Assert.That(effects, Is.EqualTo(expected));
            Assert.That(password, Is.All.EqualTo('\0'));
            Assert.That(m_identity.HasPrivateKey, Is.True);
            store.Verify(s => s.Dispose(), Times.Once);
            store.Verify(s => s.AddAsync(It.IsAny<Certificate>(), password, cancellation.Token), Times.Once);
            storeProvider.Verify(p => p.CreateStore(m_telemetry), Times.Once);
        }

        /// <summary>
        /// Cancellation is observed before key lookup, request creation, or installation side effects.
        /// </summary>
        [Test]
        public async Task CancellationPreventsAllCredentialOperationsAsync()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_credentials.ActivateAsync(default, cancellation.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_credentials.CreateSigningRequestAsync(s_applicationId, s_profile, cancellation.Token)
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await m_credentials.InstallAsync(s_applicationId,
                    new PullDcaCredentials(default, [], CompleteTrustList()), cancellation.Token)
                    .ConfigureAwait(false));
            m_provider.VerifyNoOtherCalls();
            m_manager.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Supplies a separately owned entry for each production acquisition.
        /// </summary>
        private void SetupActiveKey()
        {
            m_manager.Setup(m => m.AcquireApplicationCertificateByType(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType))
                .Returns(() =>
                {
                    using var issuers = new CertificateCollection();
                    return new CertificateEntry(
                        m_identity, issuers, CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
                });
        }

        /// <summary>
        /// Reject paths must not publish certificate or trust updates.
        /// </summary>
        private void VerifyNoInstallation(bool verifyNoValidation = true)
        {
            m_manager.Verify(m => m.WriteTrustListAsync(
                It.IsAny<TrustListIdentifier>(), It.IsAny<TrustListData>(),
                It.IsAny<TrustListMasks>(), It.IsAny<CancellationToken>()), Times.Never);
            m_manager.Verify(m => m.UpdateApplicationCertificateAsync(
                It.IsAny<NodeId>(), It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(),
                It.IsAny<CancellationToken>()), Times.Never);
            if (verifyNoValidation)
            {
                m_manager.Verify(m => m.ValidateAsync(
                    It.IsAny<CertificateCollection>(), It.IsAny<TrustListIdentifier>(),
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            Assert.That(m_identifier.RawData, Is.EqualTo(m_identity.RawData));
            Assert.That(m_identifier.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
        }

        /// <summary>
        /// Creates an application certificate with a real exportable RSA private key.
        /// </summary>
        private static Certificate CreateIdentity(string applicationUri)
        {
            return CertificateBuilder.Create("CN=Sample DCA")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(2))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Copies the certificate bytes independently from the fixture's handle.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>
        /// Represents a downloaded trust list with all standard sections present.
        /// </summary>
        private static TrustListDataType CompleteTrustList()
        {
            return new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All };
        }

        /// <summary>The application URI installed on this device.</summary>
        private const string kApplicationUri = "urn:sample:onboarding:dca";

        /// <summary>The registrar-assigned DCA application identifier.</summary>
        private static readonly NodeId s_applicationId = new("sample-dca", 0);

        /// <summary>The sample's supported credential profile.</summary>
        private static readonly PullDcaCertificateProfile s_profile = new(
            new NodeId("default-group", 0), CoreObjectTypeIds.RsaSha256ApplicationCertificateType);

        /// <summary>The fixture-owned private identity.</summary>
        private Certificate m_identity = null!;

        /// <summary>The strict on-demand key provider.</summary>
        private Mock<ICertificateProvider> m_provider = null!;

        /// <summary>The strict manager observing installation side effects.</summary>
        private Mock<ICertificateManager> m_manager = null!;

        /// <summary>The configured application certificate identifier.</summary>
        private CertificateIdentifier m_identifier = null!;

        /// <summary>The host-owned sample configuration.</summary>
        private ApplicationConfiguration m_configuration = null!;

        /// <summary>The sample implementation under test.</summary>
        private OnboardingDeviceCredentials m_credentials = null!;

        /// <summary>The test's telemetry context.</summary>
        private ITelemetryContext m_telemetry = null!;
    }
}
