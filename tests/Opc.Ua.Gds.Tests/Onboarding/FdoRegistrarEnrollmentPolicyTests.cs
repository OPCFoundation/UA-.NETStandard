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
using System.Threading.Tasks;
using NUnit.Framework;
using OnboardingRegistrar;
using Opc.Ua.Gds.Server.Onboarding;

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
                using (var policy = new FdoRegistrarEnrollmentPolicy(directory, Product, Application))
                {
                    await policy.RecordIssuanceAsync(run, s_device, s_attestation, s_csr, s_certificate)
                        .ConfigureAwait(false);
                    await AssertDeniedAsync(policy, s_certificate, [Product, Application]).ConfigureAwait(false);
                }

                using (var policy = new FdoRegistrarEnrollmentPolicy(directory, Product, Application))
                {
                    await AssertDeniedAsync(policy, s_certificate, [Product, Application]).ConfigureAwait(false);
                    await policy.CommitAsync(run, s_device, s_attestation, s_csr, s_certificate)
                        .ConfigureAwait(false);
                    await policy.CommitAsync(run, s_device, s_attestation, s_csr, s_certificate)
                        .ConfigureAwait(false);
                }

                using var restarted = new FdoRegistrarEnrollmentPolicy(directory, Product, Application);
                await AssertDeniedAsync(restarted, s_otherCertificate, [Product, Application]).ConfigureAwait(false);
                await AssertDeniedAsync(restarted, s_certificate, [Product]).ConfigureAwait(false);
                DeviceRegistrarExternalAuthenticationResult decision = await restarted.EvaluateAsync(
                    s_certificate, [Product, Application]).ConfigureAwait(false);
                Assert.That(decision.ProductInstanceUri, Is.EqualTo(Product));
                Assert.That(decision.Decision.Application.ApplicationUri, Is.EqualTo(Application));
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
                using var policy = new FdoRegistrarEnrollmentPolicy(directory, Product, Application);
                ServiceResultException absent = await Assert.ThrowsAsync<ServiceResultException>(
                    () => policy.CommitAsync(run, s_device, s_attestation, s_csr, s_certificate).AsTask());
                Assert.That(absent.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

                await policy.RecordIssuanceAsync(run, s_device, s_attestation, s_csr, s_certificate)
                    .ConfigureAwait(false);
                ServiceResultException changed = await Assert.ThrowsAsync<ServiceResultException>(
                    () => policy.CommitAsync(run, s_device, s_attestation, s_csr, s_otherCertificate).AsTask());
                Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException competing = await Assert.ThrowsAsync<ServiceResultException>(
                    () => policy.RecordIssuanceAsync(
                        Guid.NewGuid(), s_device, s_attestation, s_csr, s_certificate).AsTask());
                Assert.That(competing.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                await AssertDeniedAsync(policy, s_certificate, [Product, Application]).ConfigureAwait(false);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
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
        /// <summary>The wire-order Device GUID.</summary>
        private static readonly ByteString s_device = ByteString.From(new byte[16]);
        /// <summary>Attestation bytes already checked by the role.</summary>
        private static readonly ByteString s_attestation = ByteString.From(new byte[] { 1 });
        /// <summary>Request bytes already checked by the role.</summary>
        private static readonly ByteString s_csr = ByteString.From(new byte[] { 2 });
        /// <summary>The issued leaf.</summary>
        private static readonly ByteString s_certificate = ByteString.From(new byte[] { 3 });
        /// <summary>A different leaf presented to the policy.</summary>
        private static readonly ByteString s_otherCertificate = ByteString.From(new byte[] { 4 });
    }
}
#endif
