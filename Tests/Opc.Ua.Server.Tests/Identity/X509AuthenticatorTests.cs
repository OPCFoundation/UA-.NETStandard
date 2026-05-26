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
 *
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

#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public class X509AuthenticatorTests
    {
        [Test]
        public async Task AuthenticateAsyncTrustedCertificateAccepted()
        {
            using Certificate certificate = CreateCertificate("CN=TrustedUser");
            var validator = new TestCertificateValidator(CertificateValidationResult.Success);
            var authenticator = new X509Authenticator(validator);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(CreateHandler(certificate)))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.Not.Null);
            Assert.That(result.Identity.TokenType, Is.EqualTo(UserTokenType.Certificate));
            Assert.That(validator.LastTrustList, Is.EqualTo(TrustListIdentifier.Users));
        }

        [Test]
        public async Task AuthenticateAsyncUntrustedCertificateRejected()
        {
            using Certificate certificate = CreateCertificate("CN=UntrustedUser");
            var validationResult = new CertificateValidationResult(
                false,
                StatusCodes.BadCertificateUntrusted,
                Array.Empty<ServiceResult>(),
                false);
            var authenticator = new X509Authenticator(new TestCertificateValidator(validationResult));

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(CreateHandler(certificate)))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AuthenticateAsyncSubjectExtractedIntoClaims()
        {
            using Certificate certificate = CreateCertificate("CN=ClaimUser");
            var authenticator = new X509Authenticator(
                new TestCertificateValidator(CertificateValidationResult.Success));

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(CreateHandler(certificate)))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            var claims = result.Identity as IIdentityClaims;
            Assert.That(claims, Is.Not.Null);
            Assert.That(claims.Subject, Is.EqualTo(certificate.Subject));
            Assert.That(claims.Claims["x509.subject"], Is.EqualTo(certificate.Subject));
        }

        private static Certificate CreateCertificate(string subject)
        {
            return CertificateBuilder.Create(subject)
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
        }

        private static X509IdentityTokenHandler CreateHandler(Certificate certificate)
        {
            return new X509IdentityTokenHandler(new X509IdentityToken
            {
                PolicyId = "x509",
                CertificateData = certificate.RawData.ToByteString()
            });
        }

        private static AuthenticationContext CreateContext(IUserIdentityTokenHandler handler)
        {
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy { TokenType = handler.TokenType, PolicyId = "x509" },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

#nullable enable

        private sealed class TestCertificateValidator : ICertificateValidatorEx
        {
            private readonly CertificateValidationResult m_result;

            public TestCertificateValidator(CertificateValidationResult result)
            {
                m_result = result;
            }

            public TrustListIdentifier? LastTrustList { get; private set; }

            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                Opc.Ua.Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                LastTrustList = trustList;
                return Task.FromResult(m_result);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                LastTrustList = trustList;
                return Task.FromResult(m_result);
            }
        }
    }
}
