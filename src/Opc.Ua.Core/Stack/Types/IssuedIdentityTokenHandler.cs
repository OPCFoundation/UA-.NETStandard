// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The issued token type.
    /// </summary>
    public enum IssuedTokenType
    {
        /// <summary>
        /// Web services security (WSS) token.
        /// </summary>
        GenericWSS,

        /// <summary>
        /// Security Assertions Markup Language (SAML) token.
        /// </summary>
        SAML,

        /// <summary>
        /// JSON web token.
        /// </summary>
        JWT,

        /// <summary>
        /// Kerberos token.
        /// </summary>
        KerberosBinary,

        /// <summary>
        /// Unknown token.
        /// </summary>
        Unknown
    }

    /// <summary>
    /// The IssuedIdentityToken handler class.
    /// </summary>
    public sealed class IssuedIdentityTokenHandler : IUserIdentityTokenHandler
    {
        /// <summary>
        /// Create handler
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <param name="token">The token to handle.</param>
        /// <param name="securityPolicies">
        /// The policies to resolve the token's security policy URI against, or
        /// <see langword="null"/> to use <see cref="SecurityPolicies.Default"/>.
        /// </param>
        public IssuedIdentityTokenHandler(
            IssuedIdentityToken token,
            ISecurityPolicyRegistry? securityPolicies = null)
        {
            m_token = token ?? throw new ArgumentNullException(nameof(token));
            IssuedTokenTypeProfileUri = m_token.PolicyId ??= Profiles.JwtUserToken;
            m_securityPolicies = securityPolicies ?? SecurityPolicies.Default;
        }

        internal IssuedIdentityTokenHandler(
            IssuedIdentityToken token,
            ISecurityPolicyRegistry? securityPolicies,
            Func<SecurityPolicyInfo, Nonce> nonceFactory)
            : this(token, securityPolicies)
        {
            m_nonceFactory = nonceFactory ?? throw new ArgumentNullException(nameof(nonceFactory));
        }

        /// <summary>
        /// Create handler
        /// </summary>
        /// <param name="issuedTokenTypeProfileUri">The issued token profile.</param>
        /// <param name="decryptedTokenData">The decrypted token data.</param>
        /// <param name="securityPolicies">
        /// The policies to resolve the token's security policy URI against, or
        /// <see langword="null"/> to use <see cref="SecurityPolicies.Default"/>.
        /// </param>
        public IssuedIdentityTokenHandler(
            string issuedTokenTypeProfileUri,
            ReadOnlySpan<byte> decryptedTokenData,
            ISecurityPolicyRegistry? securityPolicies = null)
        {
            m_token = new IssuedIdentityToken
            {
                PolicyId = issuedTokenTypeProfileUri
            };
            m_decryptedTokenData = decryptedTokenData.ToArray();
            IssuedTokenTypeProfileUri = m_token.PolicyId;
            m_securityPolicies = securityPolicies ?? SecurityPolicies.Default;
        }

        /// <summary>
        /// The type of issued token.
        /// </summary>
        public IssuedTokenType IssuedTokenType => IssuedTokenTypeProfileUri switch
        {
            Namespaces.OpcUa + "UserToken#GenericWSS" => IssuedTokenType.GenericWSS,
            Namespaces.OpcUa + "UserToken#SAML" => IssuedTokenType.SAML,
            Profiles.JwtUserToken => IssuedTokenType.JWT,
            Namespaces.OpcUa + "UserToken#KerberosBinary" => IssuedTokenType.KerberosBinary,
            _ => IssuedTokenType.Unknown
        };

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.IssuedToken;

        /// <inheritdoc/>
        public string DisplayName => IssuedTokenType switch
        {
            IssuedTokenType.GenericWSS => "Generic WSS Token",
            IssuedTokenType.SAML => "SAML Token",
            IssuedTokenType.JWT => "JWT",
            IssuedTokenType.KerberosBinary => "Kerberos Token",
            _ => "Issued Token"
        };

        /// <summary>
        /// Token profile uri. Set from the token policy on the server.
        /// </summary>
        public string? IssuedTokenTypeProfileUri { get; set; }

        /// <inheritdoc/>
        public UserIdentityToken Token => m_token;

        /// <inheritdoc/>
        public void UpdatePolicy(UserTokenPolicy userTokenPolicy)
        {
            m_token.PolicyId = userTokenPolicy.PolicyId;
            IssuedTokenTypeProfileUri = userTokenPolicy.IssuedTokenType;
        }

        /// <summary>
        /// The decrypted password associated with the token.
        /// </summary>
        /// <remarks>
        /// Internally always creates a deep copy on get and set, so that the user
        /// can clear the token data after using or setting it.
        /// </remarks>
        public byte[]? DecryptedTokenData
        {
            get
            {
                if (m_decryptedTokenData != null)
                {
                    byte[] result = new byte[m_decryptedTokenData.Length];
                    Array.Copy(m_decryptedTokenData, result, m_decryptedTokenData.Length);
                    return result;
                }
                return null;
            }
            set
            {
                if (m_decryptedTokenData != null)
                {
                    Array.Clear(m_decryptedTokenData, 0, m_decryptedTokenData.Length);
                    m_decryptedTokenData = null;
                }
                if (value != null)
                {
                    m_decryptedTokenData = new byte[value.Length];
                    Array.Copy(value, m_decryptedTokenData, value.Length);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask EncryptAsync(
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
            await Task.CompletedTask.ConfigureAwait(false);

            // a policy may be given by its short name; compare the canonical URI.
            SecurityPolicyInfo? securityPolicy = string.IsNullOrEmpty(securityPolicyUri)
                ? null
                : m_securityPolicies.GetInfo(securityPolicyUri);

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicy?.Uri == SecurityPolicies.None)
            {
                m_token.TokenData = m_decryptedTokenData.ToByteString();
                m_token.EncryptionAlgorithm = string.Empty;
                return;
            }

            if (securityPolicy == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unknown security policy: " + securityPolicyUri);
            }

            securityPolicyUri = securityPolicy.Uri;

            if (securityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                byte[] dataToEncrypt = Utils.Append(m_decryptedTokenData, receiverNonce);
                EncryptedData encryptedData = m_securityPolicies.Encrypt(
                    receiverCertificate,
                    securityPolicyUri,
                    dataToEncrypt);

                Array.Clear(dataToEncrypt, 0, dataToEncrypt.Length);

                m_token.TokenData = encryptedData.Data.ToByteString();
                m_token.EncryptionAlgorithm = encryptedData.Algorithm;
                return;
            }

            using CertificateCollection? issuers = senderIssuerCertificates != null &&
                senderIssuerCertificates.Count > 0 &&
                senderIssuerCertificates[0].Thumbprint == senderCertificate?.Thumbprint
                ? new CertificateCollection()
                : null;
            if (issuers != null)
            {
                for (int ii = 1; ii < senderIssuerCertificates!.Count; ii++)
                {
                    issuers.Add(senderIssuerCertificates[ii]);
                }

                senderIssuerCertificates = issuers;
            }

            using Nonce senderNonce = m_nonceFactory(securityPolicy);
            using var secret = EncryptedSecret.CreateForEcc(
                context: context,
                securityPolicyUri: securityPolicyUri,
                senderIssuerCertificates: senderIssuerCertificates!,
                receiverCertificate: receiverCertificate,
                receiverNonce: receiverEphemeralKey!,
                senderCertificate: senderCertificate!,
                senderNonce: senderNonce,
                doNotEncodeSenderCertificate: doNotEncodeSenderCertificate);

            byte[] tokenData = m_decryptedTokenData ??
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "IssuedIdentityToken does not contain token data.");
            m_token.TokenData = secret.Encrypt(tokenData, receiverNonce).ToByteString();
            m_token.EncryptionAlgorithm = null;
        }

        /// <inheritdoc/>
        public async ValueTask DecryptAsync(
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
            // a policy may be given by its short name; compare the canonical URI.
            SecurityPolicyInfo? securityPolicy = string.IsNullOrEmpty(securityPolicyUri)
                ? null
                : m_securityPolicies.GetInfo(securityPolicyUri);

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicy?.Uri == SecurityPolicies.None)
            {
                DecryptedTokenData = m_token.TokenData.ToArray();
                return;
            }

            if (securityPolicy == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unknown security policy: " + securityPolicyUri);
            }

            securityPolicyUri = securityPolicy.Uri;

            var encryptedData = new EncryptedData
            {
                Data = m_token.TokenData.ToArray(),
                Algorithm = m_token.EncryptionAlgorithm
            };

            if (securityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                // Part 4 §7.40.6: an encrypted IssuedIdentityToken uses the EncryptedSecret
                // format and the server ignores encryptionAlgorithm. The envelope is
                // recognized by its RsaEncryptedSecret type id, so the legacy format
                // (Table 191, raw RSA blocks) still falls back below. TryDecryptRsa throws
                // the specific cause for any malformed RsaEncryptedSecret.
                byte[] tokenData = m_token.TokenData.ToArray();
                if (StartsWithRsaEncryptedSecretTypeId(context, tokenData))
                {
                    using var encryptedSecret = EncryptedSecret.CreateForRsa(
                        context,
                        securityPolicyUri,
                        certificate,
                        receiverNonce);
                    if (encryptedSecret.TryDecryptRsa(
                        tokenData,
                        receiverNonce?.Data,
                        out byte[]? rsaSecret))
                    {
                        IdentityTokenLimits.ThrowIfSecretTooLong(
                            rsaSecret,
                            IdentityTokenLimits.MaxIssuedTokenDataLength);
                        m_decryptedTokenData = rsaSecret;
                        return;
                    }
                }

                // the legacy format decrypts every RSA block of the token data, so
                // the blocks are bounded to what the longest issued token needs
                // before the first private-key operation (OPC 10000-4 7.41.2.2).
                byte[]? decryptedTokenData = await IdentityTokenLimits.DecryptLegacyAsync(
                    m_securityPolicies,
                    certificate,
                    securityPolicyUri,
                    encryptedData,
                    IdentityTokenLimits.MaxIssuedTokenDataLength,
                    receiverNonce,
                    ct).ConfigureAwait(false);

                // verify the sender's nonce.
                int startOfNonce = decryptedTokenData!.Length;

                if (receiverNonce != null)
                {
                    startOfNonce -= receiverNonce.Data!.Length;

                    for (int ii = 0; ii < receiverNonce.Data.Length; ii++)
                    {
                        if (receiverNonce.Data[ii] != decryptedTokenData[ii + startOfNonce])
                        {
                            throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                        }
                    }
                }

                if (startOfNonce > IdentityTokenLimits.MaxIssuedTokenDataLength)
                {
                    Array.Clear(decryptedTokenData, 0, decryptedTokenData.Length);
                    IdentityTokenLimits.ThrowIfSecretTooLong(
                        startOfNonce,
                        IdentityTokenLimits.MaxIssuedTokenDataLength);
                }

                // copy results.
                m_decryptedTokenData = new byte[startOfNonce];
                Array.Copy(decryptedTokenData, m_decryptedTokenData, startOfNonce);
                Array.Clear(decryptedTokenData, 0, decryptedTokenData.Length);
                return;
            }

            using var secret = EncryptedSecret.CreateForEcc(
                context: context,
                securityPolicyUri: securityPolicyUri,
                senderIssuerCertificates: senderIssuerCertificates!,
                receiverCertificate: certificate,
                receiverNonce: ephemeralKey!,
                senderCertificate: senderCertificate!,
                senderNonce: null!,
                validator: validator);

            (bool ok, byte[]? decryptedSecret) = await secret.TryDecryptAsync(
                m_token.TokenData.ToArray(),
                receiverNonce?.Data,
                ct).ConfigureAwait(false);
            if (!ok)
            {
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Failed to decrypt IssuedIdentityToken token data using ECC encrypted secret.");
            }

            IdentityTokenLimits.ThrowIfSecretTooLong(
                decryptedSecret,
                IdentityTokenLimits.MaxIssuedTokenDataLength);
            m_decryptedTokenData = decryptedSecret;
        }

        /// <inheritdoc/>
        public ValueTask<SignatureData> SignAsync(
            byte[] dataToSign,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            return new ValueTask<SignatureData>((SignatureData)null!);
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
            return new IssuedIdentityTokenHandler(CoreUtils.Clone(m_token)!, m_securityPolicies)
            {
                IssuedTokenTypeProfileUri = IssuedTokenTypeProfileUri,
                DecryptedTokenData = m_decryptedTokenData
            };
        }

        /// <inheritdoc/>
        public bool Equals(IUserIdentityTokenHandler? other)
        {
            if (other is not IssuedIdentityTokenHandler tokenHandler)
            {
                return false;
            }
            if (tokenHandler.m_decryptedTokenData != null &&
                m_decryptedTokenData != null)
            {
                return Utils.IsEqual(tokenHandler.m_decryptedTokenData, m_decryptedTokenData);
            }
            return Utils.IsEqual(m_token.TokenData, tokenHandler.m_token.TokenData);
        }

        private byte[]? m_decryptedTokenData;

        /// <summary>
        /// Returns <c>true</c> when the token data starts with a binary encoded
        /// RsaEncryptedSecret type id, i.e. it is an EncryptedSecret envelope and not
        /// legacy RSA blocks (whose random leading bytes match with negligible probability).
        /// </summary>
        private static bool StartsWithRsaEncryptedSecretTypeId(
            IServiceMessageContext context,
            byte[] tokenData)
        {
            // decode the type id so every valid NodeId encoding is recognized; data that
            // does not decode is not an EncryptedSecret and stays on the legacy path.
            if (tokenData.Length < 8)
            {
                return false;
            }

            try
            {
                using var decoder = new BinaryDecoder(tokenData, context);
                return decoder.ReadNodeId(null) == DataTypeIds.RsaEncryptedSecret;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        private readonly IssuedIdentityToken m_token;
        private readonly ISecurityPolicyRegistry m_securityPolicies;
        private readonly Func<SecurityPolicyInfo, Nonce> m_nonceFactory = Nonce.CreateNonce;
    }
}
