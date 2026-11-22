// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

            // User certificates are validated on their own terms: the application
            // certificate settings (AutoAcceptUntrustedCertificates and the global
            // AcceptError hook) apply to application instance certificates only.
            // The options override both, so every validation error rejects the token.
            using var chain = new CertificateCollection { userCertificate };
            CertificateValidationResult validationResult = await m_certificateValidator!
                .ValidateAsync(chain, m_trustList, s_userTokenValidationOptions, ct)
                .ConfigureAwait(false);

            if (!validationResult.IsValid)
            {
                StatusCode statusCode = validationResult.StatusCode == StatusCodes.BadCertificateUseNotAllowed
                    ? StatusCodes.BadIdentityTokenInvalid
                    : StatusCodes.BadIdentityTokenRejected;

                // The validation error travels as the inner result so the session
                // manager can raise the AuditCertificate event Part 4 §6.5.6 requires;
                // it is stripped before the result goes back to the client.
                return AuthenticationResult.Reject(new ServiceResult(
                    null,
                    statusCode,
                    new LocalizedText($"'{userCertificate.Subject}' is not a trusted user certificate."),
                    null,
                    // keep the detailed chain (e.g. BadCertificateInvalid wrapping
                    // the specific cause) so the cause-specific audit event is raised.
                    validationResult.Errors.Count > 0
                        ? validationResult.Errors[0]
                        : new ServiceResult(validationResult.StatusCode)));
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

        /// <summary>
        /// Validation options for user token certificates: an untrusted certificate
        /// or any other suppressible error is never accepted, whatever the
        /// application certificate configuration of the validator is. A user
        /// certificate only signs, so its key usage is checked for signing rather
        /// than for the data encipherment an application certificate needs.
        /// </summary>
        private static readonly Security.Certificates.CertificateValidationOptions s_userTokenValidationOptions = new()
        {
            AutoAcceptUntrustedCertificates = false,
            ValidateForSigning = true,
            AcceptError = static (_, _) => false
        };

        private static AuthenticationResult Reject(StatusCode statusCode, string message)
        {
            return AuthenticationResult.Reject(
                new ServiceResult(statusCode, new LocalizedText(message)));
        }

        /// <summary>
        /// The identity of a validated X.509 user token. It expires with the certificate:
        /// Part 4 §7.40.5 requires the Session to lose these credentials once the
        /// certificate is no longer valid.
        /// </summary>
        private sealed class X509UserIdentity : IUserIdentity, IIdentityClaims, IIdentityExpiration
        {
            private readonly UserIdentity m_innerIdentity;

            public X509UserIdentity(X509IdentityTokenHandler tokenHandler, Certificate certificate)
            {
                m_innerIdentity = new UserIdentity(tokenHandler);
                Subject = certificate.Subject;
                Issuer = certificate.Issuer;
                ExpiresAt = certificate.NotAfter.ToUniversalTime();
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

            public DateTime? ExpiresAt { get; }

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
