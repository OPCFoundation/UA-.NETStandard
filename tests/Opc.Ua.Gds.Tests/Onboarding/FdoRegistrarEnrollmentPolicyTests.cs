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
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingRegistrar;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the sample's durable eligibility check independently of certificate trust and the FDO transport.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class FdoRegistrarEnrollmentPolicyTests
    {
        /// <summary>
        /// Issuance alone cannot admit a certificate; only the exact committed leaf and approved identities qualify.
        /// </summary>
        [Test]
        public async Task OnlyCommittedMatchingCertificateIsAdmittedAfterRestart()
        {
            string directory = Path.Combine(Path.GetTempPath(), "fdo-admission-" + Guid.NewGuid().ToString("N"));
            Guid run = Guid.NewGuid();
            try
            {
                using Certificate issued = CertificateBuilder.Create(Subject).CreateForRSA();
                Mock<ICertificateGroupWithAuthenticatedSigningRequest> issuer = CreateIssuer(issued);
                ByteString certificate;
                using (FdoRegistrarEnrollmentPolicy policy = CreatePolicy(directory, issuer.Object))
                {
                    certificate = await policy.AuthorizeAndIssueAsync(run, s_device, s_attestation, s_csr)
                        .ConfigureAwait(false);
                    Assert.That(certificate, Is.EqualTo(ByteString.From(issued.RawData.AsSpan())));
                    await AssertDeniedAsync(policy, certificate, [Product, Application]).ConfigureAwait(false);
                }

                using (FdoRegistrarEnrollmentPolicy policy = CreatePolicy(directory, issuer.Object))
                {
                    await AssertDeniedAsync(policy, certificate, [Product, Application]).ConfigureAwait(false);
                    await policy.CommitAsync(run, s_device, s_attestation, s_csr, certificate)
                        .ConfigureAwait(false);
                    await policy.CommitAsync(run, s_device, s_attestation, s_csr, certificate)
                        .ConfigureAwait(false);
                }

                using (FdoRegistrarEnrollmentPolicy restarted = CreatePolicy(directory, issuer.Object))
                {
                    await AssertDeniedAsync(restarted, s_otherCertificate, [Product, Application])
                        .ConfigureAwait(false);
                    await AssertDeniedAsync(restarted, certificate, [Product]).ConfigureAwait(false);
                    DeviceRegistrarExternalAuthenticationResult decision = await restarted.EvaluateAsync(
                        certificate, [Product, Application]).ConfigureAwait(false);
                    Assert.That(decision.ProductInstanceUri, Is.EqualTo(Product));
                    Assert.That(decision.Decision.Application.ApplicationUri, Is.EqualTo(Application));
                }

                using var changedApproval = new FdoRegistrarEnrollmentPolicy(
                    directory, Product, Application, Subject,
                    s_device, ByteString.From(SHA256.HashData(s_otherCertificate.Span)), issuer.Object);
                await AssertDeniedAsync(changedApproval, certificate, [Product, Application]).ConfigureAwait(false);
                issuer.VerifyAll();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// A commit cannot invent, change, or replace a retained issuance.
        /// </summary>
        [Test]
        public async Task CommitRejectsAbsentOrChangedIssuance()
        {
            string directory = Path.Combine(Path.GetTempPath(), "fdo-admission-" + Guid.NewGuid().ToString("N"));
            Guid run = Guid.NewGuid();
            try
            {
                using Certificate issued = CertificateBuilder.Create(Subject).CreateForRSA();
                Mock<ICertificateGroupWithAuthenticatedSigningRequest> issuer = CreateIssuer(issued);
                using FdoRegistrarEnrollmentPolicy policy = CreatePolicy(directory, issuer.Object);
                ServiceResultException absent = await Assert.ThrowsAsync<ServiceResultException>(
                    () => policy.CommitAsync(run, s_device, s_attestation, s_csr, s_otherCertificate).AsTask());
                Assert.That(absent.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

                ByteString certificate = await policy.AuthorizeAndIssueAsync(run, s_device, s_attestation, s_csr)
                    .ConfigureAwait(false);
                ServiceResultException changed = await Assert.ThrowsAsync<ServiceResultException>(
                    () => policy.CommitAsync(run, s_device, s_attestation, s_csr, s_otherCertificate).AsTask());
                Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException competing = await Assert.ThrowsAsync<ServiceResultException>(
                    () => policy.AuthorizeAndIssueAsync(Guid.NewGuid(), s_device, s_attestation, s_csr).AsTask());
                Assert.That(competing.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                await AssertDeniedAsync(policy, certificate, [Product, Application]).ConfigureAwait(false);
                issuer.Verify(group => group.SigningRequestForAuthenticatedEvidenceAsync(
                    It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<ArrayOf<string>>(), It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// The trusted protocol evidence must match both parts of the operator's Device approval before signing.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task UnapprovedDeviceCannotReachTheIssuer(bool wrongGuid)
        {
            string directory = Path.Combine(Path.GetTempPath(), "fdo-admission-" + Guid.NewGuid().ToString("N"));
            byte[] alternateGuid = s_device.ToArray();
            alternateGuid[0] = 1;
            ByteString device = wrongGuid ? ByteString.From(alternateGuid) : s_device;
            ByteString attestation = wrongGuid ? s_attestation : s_otherCertificate;
            try
            {
                var issuer = new Mock<ICertificateGroupWithAuthenticatedSigningRequest>(MockBehavior.Strict);
                using FdoRegistrarEnrollmentPolicy policy = CreatePolicy(directory, issuer.Object);

                ServiceResultException denied = await Assert.ThrowsAsync<ServiceResultException>(
                    () => policy.AuthorizeAndIssueAsync(Guid.NewGuid(), device, attestation, s_csr).AsTask());

                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                issuer.VerifyNoOtherCalls();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// Constructs the policy with the operator's approved Device and issuance identity.
        /// </summary>
        private static FdoRegistrarEnrollmentPolicy CreatePolicy(
            string directory,
            ICertificateGroupWithAuthenticatedSigningRequest issuer)
        {
            return new FdoRegistrarEnrollmentPolicy(
                directory, Product, Application, Subject,
                s_device, ByteString.From(SHA256.HashData(s_attestation.Span)), issuer);
        }

        /// <summary>
        /// Accepts only the exact authenticated CSR and the operator-selected certificate identity.
        /// </summary>
        private static Mock<ICertificateGroupWithAuthenticatedSigningRequest> CreateIssuer(Certificate issued)
        {
            var issuer = new Mock<ICertificateGroupWithAuthenticatedSigningRequest>(MockBehavior.Strict);
            issuer.Setup(group => group.SigningRequestForAuthenticatedEvidenceAsync(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType, Subject, Application, Product,
                    It.Is<ArrayOf<string>>(uris => uris.Count == 0),
                    It.Is<ArrayOf<string>>(domains => domains.Count == 0), s_csr, CancellationToken.None))
                .ReturnsAsync(() => issued.AddRef());
            return issuer;
        }

        /// <summary>
        /// Confirms the policy denies admission rather than selecting another authentication path.
        /// </summary>
        private static async Task AssertDeniedAsync(
            FdoRegistrarEnrollmentPolicy policy, ByteString certificate, ArrayOf<string> certificateUris)
        {
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                () => policy.EvaluateAsync(certificate, certificateUris).AsTask());
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>The explicitly approved product instance.</summary>
        private const string Product = "urn:sample:product";
        /// <summary>The explicitly approved DCA application.</summary>
        private const string Application = "urn:sample:dca";
        /// <summary>The operator-approved DCA certificate subject.</summary>
        private const string Subject = "CN=Sample DCA";
        /// <summary>The wire-order Device GUID.</summary>
        private static readonly ByteString s_device = ByteString.From(new byte[16]);
        /// <summary>Attestation bytes already checked by the role.</summary>
        private static readonly ByteString s_attestation = ByteString.From(new byte[] { 1 });
        /// <summary>Request bytes already checked by the role.</summary>
        private static readonly ByteString s_csr = ByteString.From(new byte[] { 2 });
        /// <summary>A different leaf presented to the policy.</summary>
        private static readonly ByteString s_otherCertificate = ByteString.From(new byte[] { 4 });
    }
}
#endif
