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

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// The user identities of the <c>connect</c> scenario. The client and the server child
    /// derive the same credentials from their lengths, so the server can check them without
    /// a user database: the measured ActivateSession cost is the token decryption and the
    /// session bookkeeping, not a password hash.
    /// </summary>
    internal static class HarnessIdentity
    {
        public const string UserName = "perfuser";

        /// <summary>
        /// The password of <paramref name="length"/> bytes (printable ASCII).
        /// </summary>
        public static byte[] Password(int length, bool wrong = false)
        {
            byte[] password = new byte[length];
            for (int i = 0; i < length; i++)
            {
                password[i] = (byte)('a' + (i % 26));
            }
            if (wrong && length > 0)
            {
                password[length - 1] = (byte)'!';
            }
            return password;
        }

        /// <summary>
        /// A fake JWT of <paramref name="length"/> bytes (up to two more, a base64url payload
        /// grows in steps): an unsigned header, a payload with the <c>iss</c> and <c>sub</c>
        /// claims the server derives the ClientUserId from, padded by a <c>pad</c> claim, and a
        /// fixed signature segment. The harness authenticator compares the bytes; it does not
        /// check the signature.
        /// </summary>
        public static byte[] Token(int length, bool wrong = false)
        {
            const string header = "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0";
            string signature = wrong ? "YmFk" : "c2ln";
            string token;
            int padding = 0;
            do
            {
                string payload = "{\"iss\":\"urn:opcfoundation.org:perf\",\"sub\":\"" + UserName +
                    "\",\"pad\":\"" + new string('x', padding) + "\"}";
                string encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(payload))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                token = header + "." + encoded + "." + signature;
                padding++;
            } while (token.Length < length);
            return Encoding.ASCII.GetBytes(token);
        }

        /// <summary>
        /// Creates the client identity of <c>--identity anonymous|username|issued</c>, or null
        /// for anonymous.
        /// </summary>
        public static IUserIdentity? CreateClientIdentity(
            string kind,
            int passwordLength,
            int tokenLength,
            string tokenEncryption,
            bool wrong)
        {
            switch (kind)
            {
                case "anonymous":
                    return null;
                case "username":
                    var handler = new UserNameIdentityTokenHandler(UserName, Password(passwordLength, wrong));
                    return tokenEncryption == "auto"
                        ? new UserIdentity(handler)
                        : new UserIdentity(new ForcedFormatUserNameTokenHandler(handler, tokenEncryption == "secret"));
                case "issued":
                    if (tokenEncryption == "secret")
                    {
                        // Neither the client nor the server handler has an RSAEncryptedSecret
                        // path for IssuedIdentityToken; under RSA policies it is always legacy.
                        throw new ArgumentException(
                            "--token-encryption secret is not supported for issued tokens: the stack encrypts " +
                            "IssuedIdentityToken data under RSA policies with the legacy format only.");
                    }
                    return new UserIdentity(Token(tokenLength, wrong), Profiles.JwtUserToken);
                default:
                    throw new ArgumentException("--identity must be anonymous, username or issued: " + kind);
            }
        }

        /// <summary>
        /// The user token policies the server child offers in addition to anonymous.
        /// </summary>
        public static UserTokenPolicy[] ServerPolicies()
        {
            return
            [
                new UserTokenPolicy(UserTokenType.UserName),
                new UserTokenPolicy(UserTokenType.IssuedToken) { IssuedTokenType = Profiles.JwtUserToken }
            ];
        }
    }

    /// <summary>
    /// Accepts the harness user and token on the server child and counts the outcomes.
    /// Validation failures are reported to the client (and counted by the workload as errors)
    /// and appear in the server report as <c>authFailed</c>.
    /// </summary>
    internal sealed class HarnessAuthenticator
    {
        public HarnessAuthenticator(int passwordLength, int tokenLength)
        {
            m_password = HarnessIdentity.Password(passwordLength);
            m_token = HarnessIdentity.Token(tokenLength);
        }

        /// <summary>
        /// Replaces the UserName and JWT authenticators of the running server.
        /// </summary>
        public void Register(IServerInternal server)
        {
            m_namespaceUris = server.NamespaceUris;
            server.IdentityRegistry.Register(new UserNamePasswordAuthenticator(VerifyUserNameAsync));
            server.IdentityRegistry.Register(new JwtAuthenticator(VerifyIssuedAsync));
        }

        public void Mark()
        {
            Interlocked.Exchange(ref m_accepted, 0);
            Interlocked.Exchange(ref m_failed, 0);
        }

        public string Report()
        {
            return FormattableString.Invariant(
                $"authOk={Interlocked.Read(ref m_accepted)} authFailed={Interlocked.Read(ref m_failed)}");
        }

        private ValueTask<IUserIdentity> VerifyUserNameAsync(UserNameIdentityTokenHandler token, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? password = token.DecryptedPassword;
            if (token.UserName != HarnessIdentity.UserName ||
                password == null ||
                !CryptographicOperations.FixedTimeEquals(password, m_password))
            {
                Interlocked.Increment(ref m_failed);
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Invalid harness user or password.");
            }
            Interlocked.Increment(ref m_accepted);
            return new ValueTask<IUserIdentity>(Accept(token));
        }

        private ValueTask<IUserIdentity?> VerifyIssuedAsync(IssuedIdentityTokenHandler token, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? data = token.DecryptedTokenData;
            if (data == null || !CryptographicOperations.FixedTimeEquals(data, m_token))
            {
                Interlocked.Increment(ref m_failed);
                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected, "Invalid harness token.");
            }
            Interlocked.Increment(ref m_accepted);
            return new ValueTask<IUserIdentity?>(Accept(token));
        }

        private RoleBasedIdentity Accept(IUserIdentityTokenHandler token)
        {
            return new RoleBasedIdentity(new UserIdentity(token), [Role.AuthenticatedUser], m_namespaceUris!);
        }

        private readonly byte[] m_password;
        private readonly byte[] m_token;
        private NamespaceTable? m_namespaceUris;
        private long m_accepted;
        private long m_failed;
    }

    /// <summary>
    /// A UserName token handler that encrypts the password under RSA policies in a fixed format:
    /// the legacy EncryptedData (<c>EncryptionAlgorithm</c> set) or the RSAEncryptedSecret. The
    /// stock handler picks the format by password length (RSAEncryptedSecret above 64 bytes).
    /// ECC policies and SecurityPolicy None use the stock handler.
    /// </summary>
    internal sealed class ForcedFormatUserNameTokenHandler : IUserIdentityTokenHandler
    {
        public ForcedFormatUserNameTokenHandler(UserNameIdentityTokenHandler inner, bool encryptedSecret)
        {
            m_inner = inner;
            m_encryptedSecret = encryptedSecret;
        }

        public UserIdentityToken Token => m_inner.Token;

        public string DisplayName => m_inner.DisplayName;

        public UserTokenType TokenType => m_inner.TokenType;

        public void UpdatePolicy(UserTokenPolicy userTokenPolicy)
        {
            m_inner.UpdatePolicy(userTokenPolicy);
        }

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
            SecurityPolicyInfo? policy = string.IsNullOrEmpty(securityPolicyUri)
                ? null
                : SecurityPolicies.Default.GetInfo(securityPolicyUri);
            byte[]? password = m_inner.DecryptedPassword;
            if (policy == null ||
                policy.Uri == SecurityPolicies.None ||
                policy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None ||
                password == null ||
                receiverCertificate == null)
            {
                return m_inner.EncryptAsync(
                    receiverCertificate!, receiverNonce, securityPolicyUri, context, receiverEphemeralKey,
                    senderCertificate, senderIssuerCertificates, doNotEncodeSenderCertificate, ct);
            }

            var token = (UserNameIdentityToken)m_inner.Token;
            if (m_encryptedSecret)
            {
                using var secret = EncryptedSecret.CreateForRsa(context, policy.Uri, receiverCertificate);
                token.Password = secret.Encrypt(password, receiverNonce).ToByteString();
                token.EncryptionAlgorithm = null;
            }
            else
            {
                byte[] plainText = new byte[password.Length + (receiverNonce?.Length ?? 0)];
                password.CopyTo(plainText, 0);
                receiverNonce?.CopyTo(plainText, password.Length);
                EncryptedData encrypted = SecurityPolicies.Default.Encrypt(receiverCertificate, policy.Uri, plainText);
                Array.Clear(plainText);
                token.Password = encrypted.Data.ToByteString();
                token.EncryptionAlgorithm = encrypted.Algorithm;
            }
            return default;
        }

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
            return m_inner.DecryptAsync(
                certificate, receiverNonce, securityPolicyUri, context, ephemeralKey,
                senderCertificate, senderIssuerCertificates, validator, ct);
        }

        public ValueTask<SignatureData> SignAsync(byte[] dataToSign, string securityPolicyUri, CancellationToken ct = default)
        {
            return m_inner.SignAsync(dataToSign, securityPolicyUri, ct);
        }

        public ValueTask<bool> VerifyAsync(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            return m_inner.VerifyAsync(dataToVerify, signatureData, securityPolicyUri, ct);
        }

        public object Clone()
        {
            return new ForcedFormatUserNameTokenHandler(m_inner.Copy(), m_encryptedSecret);
        }

        public bool Equals(IUserIdentityTokenHandler? other)
        {
            return other is ForcedFormatUserNameTokenHandler forced &&
                forced.m_encryptedSecret == m_encryptedSecret &&
                m_inner.Equals(forced.m_inner);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as IUserIdentityTokenHandler);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_inner.GetHashCode(), m_encryptedSecret);
        }

        private readonly UserNameIdentityTokenHandler m_inner;
        private readonly bool m_encryptedSecret;
    }
}
