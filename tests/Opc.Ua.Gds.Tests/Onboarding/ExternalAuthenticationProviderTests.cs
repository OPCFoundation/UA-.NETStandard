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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises external issuer validation and the independent ProductInstanceUri approval boundary.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ExternalAuthenticationProviderTests
    {
        /// <summary>
        /// Dedicated CA validation precedes approval and does not inherit application peer trust or error overrides.
        /// </summary>
        [Test]
        public async Task TrustedIssuerRequiresExactUriApprovalAndDedicatedValidationScope()
        {
            using Certificate authority = CertificateBuilder.Create("CN=External Authority")
                .SetCAConstraint(-1).SetRSAKeySize(2048).CreateForRSA();
            using Certificate identity = Identity();
            ByteString encoded = ByteString.From(identity.RawData.AsSpan());
            var policy = Policy();
            var manager = Manager(authority, valid: true);
            ValidationOptions? captured = null;
            manager.Setup(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), ExternalAuthenticationProvider.AuthenticationAuthorities,
                It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .Callback<CertificateCollection, TrustListIdentifier, ValidationOptions, CancellationToken>(
                    (chain, _, options, _) =>
                    {
                        Assert.That(chain[0].RawData, Is.EqualTo(identity.RawData));
                        captured = options;
                    })
                .ReturnsAsync(CertificateValidationResult.Success);
            var provider = new ExternalAuthenticationProvider(manager.Object, policy.Object);

            DeviceRegistrarExternalAuthenticationResult? result = await provider.EvaluateAsync(
                ByteString.From(identity.RawData.AsSpan())).ConfigureAwait(false);

            Assert.That(result!.ProductInstanceUri, Is.EqualTo(Product));
            Assert.That(result.Decision.Application.ApplicationUri, Is.EqualTo(Product));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.TrustScope!.TrustedCertificates.Count, Is.EqualTo(1));
            Assert.That(captured.TrustScope.TrustedCertificates[0].ToArray(), Is.EqualTo(authority.RawData));
            Assert.That(captured.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(captured.RejectSHA1SignedCertificates, Is.True);
            Assert.That(captured.RejectUnknownRevocationStatus, Is.True);
            Assert.That(captured.MinimumCertificateKeySize, Is.EqualTo((ushort)2048));
            Assert.That(captured.AcceptError!(identity, new ServiceResult(StatusCodes.BadCertificateUntrusted)),
                Is.False);
            policy.Verify(value => value.EvaluateAsync(
                It.Is<ByteString>(bytes => bytes == encoded),
                It.Is<ArrayOf<string>>(uris => uris.Count == 1 && uris[0] == Product),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Neither an untrusted issuer nor an unavailable certificate engine reaches the approval policy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UntrustedOrUnavailableValidationCannotApproveDevice(bool unavailable)
        {
            using Certificate authority = CertificateBuilder.Create("CN=External Authority")
                .SetCAConstraint(-1).SetRSAKeySize(2048).CreateForRSA();
            using Certificate identity = Identity();
            var manager = Manager(authority, valid: false);
            var policy = new Mock<IDeviceRegistrarExternalAuthenticationPolicy>(MockBehavior.Strict);
            if (unavailable)
            {
                manager.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), ExternalAuthenticationProvider.AuthenticationAuthorities,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                    .Throws(new InvalidOperationException("Certificate engine unavailable."));
            }
            var provider = new ExternalAuthenticationProvider(manager.Object, policy.Object);
            if (unavailable)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await provider.EvaluateAsync(ByteString.From(identity.RawData.AsSpan())).ConfigureAwait(false));
            }
            else
            {
                Assert.That(await provider.EvaluateAsync(ByteString.From(identity.RawData.AsSpan()))
                    .ConfigureAwait(false), Is.Null);
            }
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Revocation and validity failures are errors, not an unknown issuer that permits ticket fallback.
        /// </summary>
        [TestCase("revoked", false)]
        [TestCase("unknown", false)]
        [TestCase("expired", false)]
        [TestCase("revoked", true)]
        [TestCase("unknown", true)]
        [TestCase("expired", true)]
        public async Task CertificateRejectionCannotFallBackToTicketAuthentication(string failure, bool detailed)
        {
            using Certificate authority = CertificateBuilder.Create("CN=External Authority")
                .SetCAConstraint(-1).SetRSAKeySize(2048).CreateForRSA();
            using Certificate identity = Identity();
            StatusCode status = failure switch
            {
                "revoked" => StatusCodes.BadCertificateRevoked,
                "unknown" => StatusCodes.BadCertificateRevocationUnknown,
                _ => StatusCodes.BadCertificateTimeInvalid
            };
            var manager = Manager(authority, valid: false);
            manager.Setup(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), ExternalAuthenticationProvider.AuthenticationAuthorities,
                It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CertificateValidationResult(false,
                    detailed ? StatusCodes.BadCertificateUntrusted : status,
                    detailed ? [new ServiceResult(status)] : [], false));
            var policy = new Mock<IDeviceRegistrarExternalAuthenticationPolicy>(MockBehavior.Strict);
            var provider = new ExternalAuthenticationProvider(manager.Object, policy.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.EvaluateAsync(ByteString.From(identity.RawData.AsSpan())).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(status));
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Empty trust and leaf pinning cannot replace the configured authentication-service issuer boundary.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingCaOrLeafAnchorRejectsConfiguration(bool leafAnchor)
        {
            using Certificate identity = Identity();
            var manager = Manager(leafAnchor ? identity : null, valid: true);
            var policy = new Mock<IDeviceRegistrarExternalAuthenticationPolicy>(MockBehavior.Strict);
            var provider = new ExternalAuthenticationProvider(manager.Object, policy.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.EvaluateAsync(ByteString.From(identity.RawData.AsSpan())).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            policy.VerifyNoOtherCalls();
            manager.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), It.IsAny<TrustListIdentifier?>(),
                It.IsAny<ValidationOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A trusted certificate still requires URI evidence and an exact host-approved product/application pair.
        /// </summary>
        [TestCase("missingUri")]
        [TestCase("product")]
        [TestCase("application")]
        [TestCase("approval")]
        public async Task TrustedCertificateCannotSubstituteProductApplicationOrApproval(string failure)
        {
            using Certificate authority = CertificateBuilder.Create("CN=External Authority")
                .SetCAConstraint(-1).SetRSAKeySize(2048).CreateForRSA();
            using Certificate identity = Identity(failure != "missingUri");
            var policy = Policy();
            if (failure == "approval")
            {
                policy.Setup(value => value.EvaluateAsync(
                    It.IsAny<ByteString>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            }
            else if (failure != "missingUri")
            {
                DeviceRegistrarExternalAuthenticationResult decision = Approved();
                if (failure == "product")
                {
                    decision = decision with { ProductInstanceUri = "urn:external:Other" };
                }
                else
                {
                    decision.Decision.Application.ApplicationUri = "urn:external:other-application";
                }
                policy.Setup(value => value.EvaluateAsync(
                    It.IsAny<ByteString>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(decision);
            }
            var provider = new ExternalAuthenticationProvider(Manager(authority, valid: true).Object, policy.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.EvaluateAsync(ByteString.From(identity.RawData.AsSpan())).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(failure switch
            {
                "missingUri" => StatusCodes.BadCertificateUriInvalid,
                "approval" => StatusCodes.BadUserAccessDenied,
                _ => StatusCodes.BadConfigurationError
            }));
        }

        /// <summary>
        /// External provenance retains detached certificate evidence without pretending a ticket was validated.
        /// </summary>
        [Test]
        public void ExternalEvidenceHasNoTicketOrTicketEnvelope()
        {
            byte[] source = [1, 2, 3];
            OnboardingIdentitySelection identity =
                OnboardingIdentitySelection.FromExternalAuthentication(ByteString.From(source), Product);
            source[0] = 9;

            Assert.That(identity.IsExternalAuthentication, Is.True);
            Assert.That(identity.SelectedIdentity.Span[0], Is.EqualTo(1));
            Assert.That(identity.EncodedTicket, Is.Empty);
            Assert.That(identity.EncodedComposite, Is.Empty);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() => _ = identity.MatchingTicket);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTicketRequired));
        }

        /// <summary>
        /// Cancellation precedes any parsing or provider effect; fluent registration rejects duplicate selection.
        /// </summary>
        [Test]
        public async Task CancellationAndDuplicateProviderSelectionAreRejected()
        {
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var policy = new Mock<IDeviceRegistrarExternalAuthenticationPolicy>(MockBehavior.Strict);
            var provider = new ExternalAuthenticationProvider(manager.Object, policy.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.EvaluateAsync(default, cancellation.Token).ConfigureAwait(false));
            manager.VerifyNoOtherCalls();
            policy.VerifyNoOtherCalls();
            var services = new ServiceCollection();
            services.AddDeviceRegistrarExternalAuthenticationProvider(_ => provider);
            Assert.That(() => services.AddDeviceRegistrarExternalAuthenticationProvider(_ => provider),
                Throws.InvalidOperationException);
            using ServiceProvider container = services.BuildServiceProvider();
            Assert.That(container.GetRequiredService<IDeviceRegistrarExternalAuthenticationProvider>(),
                Is.SameAs(provider));
        }

        /// <summary>
        /// Creates an isolated managed CA trust-list snapshot per call.
        /// </summary>
        private static Mock<ICertificateManager> Manager(Certificate? anchor, bool valid)
        {
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.Setup(value => value.ReadTrustListAsync(
                ExternalAuthenticationProvider.AuthenticationAuthorities, TrustListMasks.All,
                It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var trust = new TrustListData();
                    if (anchor != null)
                    {
                        trust.TrustedCertificates.Add(anchor);
                    }
                    return Task.FromResult(trust);
                });
            manager.Setup(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), ExternalAuthenticationProvider.AuthenticationAuthorities,
                It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CertificateValidationResult(
                    valid, valid ? StatusCodes.Good : StatusCodes.BadCertificateUntrusted, [], false));
            return manager;
        }

        /// <summary>
        /// Supplies only the one explicitly configured device approval.
        /// </summary>
        private static Mock<IDeviceRegistrarExternalAuthenticationPolicy> Policy()
        {
            var policy = new Mock<IDeviceRegistrarExternalAuthenticationPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.EvaluateAsync(
                It.IsAny<ByteString>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Approved());
            return policy;
        }

        /// <summary>
        /// Creates DCA metadata whose ApplicationUri and ProductInstanceUri are present in the certificate.
        /// </summary>
        private static DeviceRegistrarExternalAuthenticationResult Approved()
        {
            return new DeviceRegistrarExternalAuthenticationResult(Product,
                new DeviceRegistrarOnboardingDecision(new ApplicationRecordDataType
                {
                    ApplicationUri = Product,
                    ApplicationType = ApplicationType.Client,
                    ApplicationNames = [new LocalizedText("External DCA")],
                    ProductUri = "urn:external:product"
                }, false, null));
        }

        /// <summary>
        /// Creates a leaf with optional identity URI evidence.
        /// </summary>
        private static Certificate Identity(bool withUri = true)
        {
            var builder = CertificateBuilder.Create("CN=External DCA");
            if (withUri)
            {
                builder.AddExtension(new X509SubjectAltNameExtension(Product, ["localhost"]));
            }
            return builder.SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>The exact case-sensitive product and DCA application URI approved by the host.</summary>
        private const string Product = "urn:external:device";
    }
}
