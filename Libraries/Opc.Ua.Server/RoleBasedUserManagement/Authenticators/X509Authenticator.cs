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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Validates X.509 user identity tokens against a configured trust list.
    /// </summary>
    public sealed class X509Authenticator : IUserTokenAuthenticator
    {
        private readonly ICertificateValidatorEx? m_certificateValidator;
        private readonly TrustListIdentifier m_trustList;
        private readonly Func<X509IdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity>>? m_verify;

        /// <summary>
        /// Creates an X.509 authenticator.
        /// </summary>
        public X509Authenticator(
            ICertificateValidatorEx certificateValidator,
            TrustListIdentifier? trustList = null)
        {
            m_certificateValidator = certificateValidator ??
                throw new ArgumentNullException(nameof(certificateValidator));
            m_trustList = trustList ?? TrustListIdentifier.Users;
        }

        /// <summary>
        /// Creates a delegate-backed X.509 authenticator.
        /// </summary>
        public X509Authenticator(
            Func<X509IdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity>> verify)
        {
            m_verify = verify ?? throw new ArgumentNullException(nameof(verify));
            m_trustList = TrustListIdentifier.Users;
        }

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.Certificate;

        /// <inheritdoc/>
        public string? IssuedTokenProfileUri => null;

        /// <inheritdoc/>
        public async ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            if (context.TokenHandler is not X509IdentityTokenHandler x509TokenHandler)
            {
                return AuthenticationResult.NotHandled;
            }

            if (m_verify != null)
            {
                return await AuthenticateWithVerifierAsync(x509TokenHandler, ct).ConfigureAwait(false);
            }

            var wireToken = (X509IdentityToken)x509TokenHandler.Token;
            using Certificate? userCertificate = wireToken.CertificateData.IsEmpty
                ? null
                : Certificate.FromRawData(wireToken.CertificateData);

            if (userCertificate == null)
            {
                return Reject(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid X.509 token. A certificate is required.");
            }

            CertificateValidationResult validationResult = await m_certificateValidator!
                .ValidateAsync(userCertificate, m_trustList, ct)
                .ConfigureAwait(false);

            if (!validationResult.IsValid)
            {
                StatusCode statusCode = validationResult.StatusCode == StatusCodes.BadCertificateUseNotAllowed
                    ? StatusCodes.BadIdentityTokenInvalid
                    : StatusCodes.BadIdentityTokenRejected;
                return Reject(
                    statusCode,
                    $"'{userCertificate.Subject}' is not a trusted user certificate.");
            }

            return AuthenticationResult.Accept(new X509UserIdentity(x509TokenHandler, userCertificate));
        }

        private async ValueTask<AuthenticationResult> AuthenticateWithVerifierAsync(
            X509IdentityTokenHandler x509TokenHandler,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                IUserIdentity? identity = await m_verify!(x509TokenHandler, ct).ConfigureAwait(false);
                return identity == null
                    ? Reject(
                        StatusCodes.BadIdentityTokenRejected,
                        "X.509 token verifier did not return an identity.")
                    : AuthenticationResult.Accept(identity);
            }
            catch (ServiceResultException ex)
            {
                return AuthenticationResult.Reject(ex.Result);
            }
        }

        private static AuthenticationResult Reject(StatusCode statusCode, string message)
        {
            return AuthenticationResult.Reject(
                new ServiceResult(statusCode, new LocalizedText(message)));
        }

        private sealed class X509UserIdentity : IUserIdentity, IIdentityClaims
        {
            private readonly UserIdentity m_innerIdentity;

            public X509UserIdentity(X509IdentityTokenHandler tokenHandler, Certificate certificate)
            {
                m_innerIdentity = new UserIdentity(tokenHandler);
                Subject = certificate.Subject;
                Issuer = certificate.Issuer;
                Claims = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["sub"] = Subject,
                    ["x509.subject"] = Subject,
                    ["x509.issuer"] = Issuer
                };
                Groups = [];
                Roles = [];
            }

            public IReadOnlyDictionary<string, object?> Claims { get; }

            public IReadOnlyList<string> Groups { get; }

            public IReadOnlyList<string> Roles { get; }

            public string? Issuer { get; }

            public string? Subject { get; }

            public string DisplayName => m_innerIdentity.DisplayName;

            public string PolicyId => m_innerIdentity.PolicyId;

            public UserTokenType TokenType => m_innerIdentity.TokenType;

            public XmlQualifiedName IssuedTokenType => m_innerIdentity.IssuedTokenType;

            public bool SupportsSignatures => m_innerIdentity.SupportsSignatures;

            public ArrayOf<NodeId> GrantedRoleIds => [];

            public IUserIdentityTokenHandler TokenHandler => m_innerIdentity.TokenHandler;
        }
    }
}
