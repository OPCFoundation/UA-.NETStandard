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
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

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
        public IssuedIdentityTokenHandler(IssuedIdentityToken token)
        {
            m_token = token ?? throw new ArgumentNullException(nameof(token));
            IssuedTokenTypeProfileUri = m_token.PolicyId ??= Profiles.JwtUserToken;
        }

        /// <summary>
        /// Create handler
        /// </summary>
        public IssuedIdentityTokenHandler(
            string issuedTokenTypeProfileUri,
            ReadOnlySpan<byte> decryptedTokenData)
        {
            m_token = new IssuedIdentityToken
            {
                PolicyId = issuedTokenTypeProfileUri
            };
            m_decryptedTokenData = decryptedTokenData.ToArray();
            IssuedTokenTypeProfileUri = m_token.PolicyId;
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
        public string IssuedTokenTypeProfileUri { get; set; }

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
        public byte[] DecryptedTokenData
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
        public void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false)
        {
            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_token.TokenData = m_decryptedTokenData.ToByteString();
                m_token.EncryptionAlgorithm = string.Empty;
                return;
            }

            byte[] dataToEncrypt = Utils.Append(m_decryptedTokenData, receiverNonce);

            ILogger logger = context.Telemetry.CreateLogger<IssuedIdentityTokenHandler>();
            EncryptedData encryptedData = SecurityPolicies.Encrypt(
                receiverCertificate,
                securityPolicyUri,
                dataToEncrypt,
                logger);

            Array.Clear(dataToEncrypt, 0, dataToEncrypt.Length);

            m_token.TokenData = encryptedData.Data.ToByteString();
            m_token.EncryptionAlgorithm = encryptedData.Algorithm;
        }

        /// <inheritdoc/>
        public void Decrypt(
            X509Certificate2 certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null)
        {
            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                DecryptedTokenData = m_token.TokenData.ToArray();
                return;
            }

            var encryptedData = new EncryptedData
            {
                Data = m_token.TokenData.ToArray(),
                Algorithm = m_token.EncryptionAlgorithm
            };

            ILogger logger = context.Telemetry.CreateLogger<IssuedIdentityTokenHandler>();
            byte[] decryptedTokenData = SecurityPolicies.Decrypt(
                certificate,
                securityPolicyUri,
                encryptedData,
                logger);

            // verify the sender's nonce.
            int startOfNonce = decryptedTokenData.Length;

            if (receiverNonce != null)
            {
                startOfNonce -= receiverNonce.Data.Length;

                for (int ii = 0; ii < receiverNonce.Data.Length; ii++)
                {
                    if (receiverNonce.Data[ii] != decryptedTokenData[ii + startOfNonce])
                    {
                        throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                    }
                }
            }

            // copy results.
            m_decryptedTokenData = new byte[startOfNonce];
            Array.Copy(decryptedTokenData, m_decryptedTokenData, startOfNonce);
            Array.Clear(decryptedTokenData, 0, decryptedTokenData.Length);
        }

        /// <inheritdoc/>
        public SignatureData Sign(
            byte[] dataToSign,
            string securityPolicyUri)
        {
            return null;
        }

        /// <inheritdoc/>
        public bool Verify(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri)
        {
            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_decryptedTokenData != null)
            {
                Array.Clear(m_decryptedTokenData, 0, m_decryptedTokenData.Length);
                m_decryptedTokenData = null;
            }
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new IssuedIdentityTokenHandler(m_token)
            {
                IssuedTokenTypeProfileUri = IssuedTokenTypeProfileUri,
                DecryptedTokenData = m_decryptedTokenData
            };
        }

        /// <inheritdoc/>
        public bool Equals(IUserIdentityTokenHandler other)
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

        private byte[] m_decryptedTokenData;
        private readonly IssuedIdentityToken m_token;
    }
}
