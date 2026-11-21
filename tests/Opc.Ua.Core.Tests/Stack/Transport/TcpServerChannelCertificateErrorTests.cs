// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// The status code a server reports to the client of an OpenSecureChannel request whose
    /// client certificate failed validation (Part 4 §6.1.3, Table 106).
    /// </summary>
    [TestFixture]
    [Category("Transport")]
    [Parallelizable]
    public class TcpServerChannelCertificateErrorTests
    {
        private static readonly StatusCode[] s_maskedCodes =
        [
            StatusCodes.BadCertificateInvalid,
            StatusCodes.BadCertificateChainIncomplete,
            StatusCodes.BadCertificatePolicyCheckFailed,
            StatusCodes.BadCertificateUntrusted,
            StatusCodes.BadCertificateRevoked,
            StatusCodes.BadCertificateIssuerRevoked,
            StatusCodes.BadCertificateRevocationUnknown,
            StatusCodes.BadCertificateIssuerRevocationUnknown
        ];

        private static readonly StatusCode[] s_reportedCodes =
        [
            StatusCodes.BadCertificateTimeInvalid,
            StatusCodes.BadCertificateIssuerTimeInvalid,
            StatusCodes.BadCertificateHostNameInvalid,
            StatusCodes.BadCertificateUriInvalid,
            StatusCodes.BadCertificateUseNotAllowed,
            StatusCodes.BadCertificateIssuerUseNotAllowed
        ];

        [TestCaseSource(nameof(s_maskedCodes))]
        public void TrustListAndRevocationErrorsAreReportedAsSecurityChecksFailed(StatusCode statusCode)
        {
            // The CTT (Security Certificate Validation 002.js) connects with a certificate whose
            // issuer has no revocation list; the server must not reveal that to the client.
            Assert.That(
                TcpServerChannel.TryGetReportableCertificateError(
                    new ServiceResultException(statusCode),
                    out ServiceResultException? reportable),
                Is.False);
            Assert.That(reportable, Is.Null);

            Assert.That(
                TcpServerChannel.TryGetReportableCertificateError(
                    new InvalidOperationException("wrapped", new ServiceResultException(statusCode)),
                    out _),
                Is.False);
        }

        [TestCaseSource(nameof(s_reportedCodes))]
        public void ValidityHostNameUriAndUsageErrorsAreReported(StatusCode statusCode)
        {
            var error = new ServiceResultException(statusCode);

            Assert.That(
                TcpServerChannel.TryGetReportableCertificateError(error, out ServiceResultException? reportable),
                Is.True);
            Assert.That(reportable, Is.SameAs(error));

            var wrapped = new InvalidOperationException("wrapped", error);
            Assert.That(
                TcpServerChannel.TryGetReportableCertificateError(wrapped, out reportable),
                Is.True);
            Assert.That(reportable, Is.SameAs(error));
        }

        [Test]
        public void ErrorOfAnUntrustedCertificateIsReportedAsSecurityChecksFailed()
        {
            var error = new ServiceResultException(
                new ServiceResult(
                    StatusCodes.BadCertificateTimeInvalid,
                    new ServiceResult(StatusCodes.BadCertificateUntrusted)));

            Assert.That(TcpServerChannel.TryGetReportableCertificateError(error, out _), Is.False);
        }

        [Test]
        public void ReportableErrorsNestedBelowAReportableErrorAreReported()
        {
            var error = new ServiceResultException(
                new ServiceResult(
                    StatusCodes.BadCertificateTimeInvalid,
                    new ServiceResult(
                        StatusCodes.BadCertificateHostNameInvalid,
                        new ServiceResult(StatusCodes.BadCertificateUseNotAllowed))));

            Assert.That(
                TcpServerChannel.TryGetReportableCertificateError(error, out ServiceResultException? reportable),
                Is.True);
            Assert.That(reportable!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateTimeInvalid));
        }

        [TestCaseSource(nameof(s_maskedCodes))]
        public void MaskedErrorNestedDeepBelowAReportableErrorIsReportedAsSecurityChecksFailed(StatusCode statusCode)
        {
            var error = new ServiceResultException(
                new ServiceResult(
                    StatusCodes.BadCertificateTimeInvalid,
                    new ServiceResult(
                        StatusCodes.BadCertificateHostNameInvalid,
                        new ServiceResult(statusCode))));

            Assert.That(TcpServerChannel.TryGetReportableCertificateError(error, out _), Is.False);
        }

        [Test]
        public void OtherExceptionsAreReportedAsSecurityChecksFailed()
        {
            Assert.That(
                TcpServerChannel.TryGetReportableCertificateError(new InvalidOperationException(), out _),
                Is.False);
        }

        /// <summary>
        /// A custom RSA policy without a key length window (the init properties default to 0)
        /// has no bound, as for the server certificate selection; a set bound is enforced.
        /// </summary>
        [TestCase(0, 0, 2048, true)]
        [TestCase(0, 0, 8192, true)]
        [TestCase(2048, 0, 8192, true)]
        [TestCase(2048, 0, 1024, false)]
        [TestCase(0, 2048, 1024, true)]
        [TestCase(0, 2048, 4096, false)]
        [TestCase(2048, 4096, 2048, true)]
        [TestCase(2048, 4096, 4096, true)]
        [TestCase(2048, 4096, 1024, false)]
        [TestCase(2048, 4096, 8192, false)]
        public void RsaKeyLengthWindowTreatsNonPositiveBoundsAsUnbounded(
            int minKeyLength,
            int maxKeyLength,
            int keySize,
            bool allowed)
        {
            var policy = new SecurityPolicyInfo("urn:test:CustomRsaPolicy")
            {
                CertificateKeyFamily = CertificateKeyFamily.RSA,
                MinAsymmetricKeyLength = minKeyLength,
                MaxAsymmetricKeyLength = maxKeyLength
            };

            Assert.That(UaSCUaBinaryChannel.IsRsaKeySizeAllowed(policy, keySize), Is.EqualTo(allowed));
        }
    }
}
