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

#nullable enable

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests HTTPS TLS client-certificate validation against the configured UA validator.
    /// </summary>
    [TestFixture]
    [Category("HttpsClientCertificateValidation")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class HttpsTransportListenerClientCertificateValidationTests
    {
        [Test]
        public void PresentedCertificateWithNoSslErrorsStillRequiresUaValidation()
        {
            using X509Certificate2 certificate = CreateClientCertificate();
            var validator = new RecordingCertificateValidator(s_rejection);

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                validator,
                certificate,
                SslPolicyErrors.None);

            Assert.That(accepted, Is.False);
            Assert.That(validator.ValidationCount, Is.EqualTo(1));
        }

        [Test]
        public void PresentedCertificateWithSslErrorsCanBeAcceptedByUaValidation()
        {
            using X509Certificate2 certificate = CreateClientCertificate();
            var validator = new RecordingCertificateValidator(CertificateValidationResult.Success);

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                validator,
                certificate,
                SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(accepted, Is.True);
            Assert.That(validator.ValidationCount, Is.EqualTo(1));
        }

        [TestCase(SslPolicyErrors.None, true)]
        [TestCase(SslPolicyErrors.RemoteCertificateNotAvailable, false)]
        public void MissingOptionalCertificateUsesSslPolicyResult(
            SslPolicyErrors sslPolicyErrors,
            bool expected)
        {
            var validator = new RecordingCertificateValidator(s_rejection);

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                validator,
                clientCertificate: null,
                sslPolicyErrors);

            Assert.That(accepted, Is.EqualTo(expected));
            Assert.That(validator.ValidationCount, Is.Zero);
        }

        private static X509Certificate2 CreateClientCertificate()
        {
            using Certificate certificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:localhost:HttpsTransportListenerClientCertificateValidationTests",
                    "HttpsTransportListenerClientCertificateValidationTests",
                    "CN=HttpsTransportListenerClientCertificateValidationTests",
                    ["localhost"])
                .SetLifeTime(TimeSpan.FromDays(1))
                .CreateForRSA();
            return certificate.AsX509Certificate2();
        }

        private sealed class RecordingCertificateValidator : ICertificateValidatorEx
        {
            public RecordingCertificateValidator(CertificateValidationResult result)
            {
                m_result = result;
            }

            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public int ValidationCount { get; private set; }

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                global::Opc.Ua.Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                ValidationCount++;
                return Task.FromResult(m_result);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                ValidationCount++;
                return Task.FromResult(m_result);
            }

            private readonly CertificateValidationResult m_result;
        }

        private static readonly CertificateValidationResult s_rejection =
            new(isValid: false, StatusCodes.BadCertificateUntrusted, [], false);
    }
}
