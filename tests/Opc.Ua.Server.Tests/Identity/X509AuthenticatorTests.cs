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
                [],
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

        [Test]
        public async Task AuthenticateAsyncNonX509TokenIsNotHandled()
        {
            var authenticator = new X509Authenticator(
                new TestCertificateValidator(CertificateValidationResult.Success));
            var context = new AuthenticationContext(
                new AnonymousIdentityTokenHandler(),
                new UserTokenPolicy { TokenType = UserTokenType.Anonymous },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));

            AuthenticationResult result = await authenticator.AuthenticateAsync(context)
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        [Test]
        public async Task AuthenticateAsyncEmptyCertificateRejectedAsInvalid()
        {
            var authenticator = new X509Authenticator(
                new TestCertificateValidator(CertificateValidationResult.Success));
            var handler = new X509IdentityTokenHandler(new X509IdentityToken
            {
                PolicyId = "x509",
                CertificateData = ByteString.Empty
            });

            AuthenticationResult result = await authenticator.AuthenticateAsync(CreateContext(handler))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncUseNotAllowedMapsToInvalidIdentityToken()
        {
            using Certificate certificate = CreateCertificate("CN=WrongUse");
            var validationResult = new CertificateValidationResult(
                false,
                StatusCodes.BadCertificateUseNotAllowed,
                [],
                false);
            var authenticator = new X509Authenticator(new TestCertificateValidator(validationResult));

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(CreateHandler(certificate)))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncDelegateVerifierAcceptsRejectsAndMapsServiceResultException()
        {
            using Certificate certificate = CreateCertificate("CN=DelegateUser");
            X509IdentityTokenHandler handler = CreateHandler(certificate);
            var acceptedIdentity = new UserIdentity("delegate", []);
            var accepting = new X509Authenticator(
                (h, ct) => new ValueTask<IUserIdentity>(acceptedIdentity));
            var nullVerifier = new X509Authenticator(
                (h, ct) => new ValueTask<IUserIdentity>((IUserIdentity)null!));
            var throwing = new X509Authenticator(
                (h, ct) => throw new ServiceResultException(StatusCodes.BadIdentityTokenInvalid));

            AuthenticationResult accepted = await accepting.AuthenticateAsync(CreateContext(handler))
                .ConfigureAwait(false);
            AuthenticationResult rejected = await nullVerifier.AuthenticateAsync(CreateContext(handler))
                .ConfigureAwait(false);
            AuthenticationResult mapped = await throwing.AuthenticateAsync(CreateContext(handler))
                .ConfigureAwait(false);

            Assert.That(accepted.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(accepted.Identity, Is.SameAs(acceptedIdentity));
            Assert.That(rejected.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(mapped.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
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

        private static AuthenticationContext CreateContext(X509IdentityTokenHandler handler)
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
                Security.Certificates.CertificateValidationOptions? options = null,
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
