// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Anonymous identity token handler.
    /// </summary>
    public sealed class AnonymousIdentityTokenHandler : IUserIdentityTokenHandler
    {
        /// <summary>
        /// Create anonymous identity token handler.
        /// </summary>
        public AnonymousIdentityTokenHandler(AnonymousIdentityToken? token = null)
        {
            m_token = token ?? new AnonymousIdentityToken();
        }

        /// <summary>
        /// Create anonymous identity token handler for policy.
        /// </summary>
        public static AnonymousIdentityTokenHandler Create(UserTokenPolicy policy)
        {
            return new AnonymousIdentityTokenHandler(new AnonymousIdentityToken
            {
                PolicyId = policy.PolicyId
            });
        }

        /// <inheritdoc/>
        public UserIdentityToken Token => m_token;

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.Anonymous;

        /// <inheritdoc/>
        public string DisplayName => "Anonymous";

        /// <inheritdoc/>
        public void UpdatePolicy(UserTokenPolicy userTokenPolicy)
        {
            m_token.PolicyId = userTokenPolicy.PolicyId;
        }

        /// <inheritdoc/>
        public ValueTask EncryptAsync(
            Certificate receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce? receiverEphemeralKey = null,
            Certificate? senderCertificate = null,
            CertificateCollection? senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false,
            CancellationToken ct = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DecryptAsync(
            Certificate certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce? ephemeralKey = null,
            Certificate? senderCertificate = null,
            CertificateCollection? senderIssuerCertificates = null,
            ICertificateValidatorEx? validator = null,
            CancellationToken ct = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<SignatureData> SignAsync(
            byte[] dataToSign,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            return new ValueTask<SignatureData>(new SignatureData());
        }

        /// <inheritdoc/>
        public ValueTask<bool> VerifyAsync(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            return new ValueTask<bool>(true);
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new AnonymousIdentityTokenHandler(CoreUtils.Clone(m_token));
        }

        /// <inheritdoc/>
        public bool Equals(IUserIdentityTokenHandler? other)
        {
            return other?.TokenType == TokenType;
        }

        private readonly AnonymousIdentityToken m_token;
    }
}
