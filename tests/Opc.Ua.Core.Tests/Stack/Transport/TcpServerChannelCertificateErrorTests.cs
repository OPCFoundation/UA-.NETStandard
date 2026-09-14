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
                    out ServiceResultException reportable),
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
                TcpServerChannel.TryGetReportableCertificateError(error, out ServiceResultException reportable),
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
        public void OtherExceptionsAreReportedAsSecurityChecksFailed()
        {
            Assert.That(
                TcpServerChannel.TryGetReportableCertificateError(new InvalidOperationException(), out _),
                Is.False);
        }
    }
}
