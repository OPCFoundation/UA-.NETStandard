// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Size limits for the secret of a user identity token, enforced before the
    /// server spends a private-key operation on it.
    /// </summary>
    /// <remarks>
    /// The legacy token encryption (OPC 10000-4 7.41.2.2, OPC 10000-6 6.8.1)
    /// encrypts the length prefix, the secret and the server nonce block by
    /// block with RSA, so the private-key work grows with the size the sender
    /// picks. A client only has to open a session to make the server decrypt,
    /// which made a 1 MB secret (4 095 blocks, ~2 s of CPU) a cheap way to pin
    /// the server. The ciphertext is therefore rejected with
    /// Bad_IdentityTokenInvalid once it holds more RSA blocks than the largest
    /// legitimate secret needs, before the first decryption.
    /// </remarks>
    internal static class IdentityTokenLimits
    {
        /// <summary>
        /// The longest UserName token password, in bytes. Passwords are typed
        /// or stored secrets, so 1 KiB is far above any real one; bearer
        /// tokens belong in an IssuedIdentityToken.
        /// </summary>
        public const int MaxPasswordLength = 1024;

        /// <summary>
        /// The longest IssuedIdentityToken token data, in bytes. Large enough
        /// for JWT access tokens with many claims and for SAML assertions, which
        /// identity providers keep within the 16 KiB a HTTP header may carry.
        /// </summary>
        public const int MaxIssuedTokenDataLength = 16 * 1024;

        /// <summary>
        /// The longest server nonce the legacy token encryption appends after
        /// the secret, in bytes (a session nonce is 32 bytes).
        /// </summary>
        public const int MaxServerNonceLength = 128;

        /// <summary>
        /// The longest legacy encrypted plaintext any identity token can need:
        /// the length prefix, the largest token data and the server nonce.
        /// </summary>
        public const int MaxLegacyPlainTextLength =
            sizeof(int) + MaxIssuedTokenDataLength + MaxServerNonceLength;

        /// <summary>
        /// The largest RSA padding overhead a token encryption algorithm uses
        /// (OAEP with SHA-256), so a bound computed with it holds for every
        /// algorithm.
        /// </summary>
        private const int kMaxRsaPaddingOverhead = 66;

        /// <summary>
        /// Decrypts a legacy RSA encrypted token secret (OPC 10000-4 7.41.2.2)
        /// whose secret may be at most <paramref name="maxSecretLength"/> bytes,
        /// rejecting a longer cipher text before the first private-key operation.
        /// </summary>
        /// <param name="registry">The registry that decrypts.</param>
        /// <param name="certificate">The certificate whose private key decrypts.</param>
        /// <param name="securityPolicyUri">The canonical security policy URI.</param>
        /// <param name="encryptedData">The encrypted secret.</param>
        /// <param name="maxSecretLength">The longest legitimate secret.</param>
        /// <param name="receiverNonce">The server nonce appended to the secret.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The plaintext: the secret followed by the server nonce.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static ValueTask<byte[]?> DecryptLegacyAsync(
            ISecurityPolicyRegistry registry,
            Certificate certificate,
            string securityPolicyUri,
            EncryptedData encryptedData,
            int maxSecretLength,
            Nonce? receiverNonce,
            CancellationToken ct)
        {
            int maxPlainTextLength = sizeof(int) + maxSecretLength + (receiverNonce?.Data?.Length ?? 0);

            if (registry is SecurityPolicies policies)
            {
                // checked on the private key the decryption opens anyway.
                return policies.DecryptAsync(
                    certificate,
                    securityPolicyUri,
                    encryptedData,
                    maxPlainTextLength,
                    ct);
            }

            ThrowIfRsaCipherTextTooLong(
                encryptedData.Data?.Length ?? 0,
                certificate,
                maxPlainTextLength);
            return registry.DecryptAsync(certificate, securityPolicyUri, encryptedData, ct);
        }

        /// <summary>
        /// Rejects a legacy RSA encrypted token secret that holds more RSA blocks
        /// than a plaintext of <paramref name="maxPlainTextLength"/> bytes needs.
        /// </summary>
        /// <param name="cipherTextLength">The length of the encrypted secret.</param>
        /// <param name="certificate">The certificate whose key decrypts it.</param>
        /// <param name="maxPlainTextLength">
        /// The longest legitimate plaintext, including the length prefix and the
        /// server nonce.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Bad_IdentityTokenInvalid when the secret is too long.
        /// </exception>
        /// <remarks>
        /// Reads the public key, which allocates. The decryption in
        /// <see cref="RsaUtils"/> checks the same bound with the key it opens
        /// anyway; this overload is for a token handler whose security policy
        /// registry is not <see cref="SecurityPolicies"/> and so cannot be given
        /// the limit. The padding is not known there, so the one with the
        /// largest overhead is assumed.
        /// </remarks>
        public static void ThrowIfRsaCipherTextTooLong(
            int cipherTextLength,
            Certificate certificate,
            int maxPlainTextLength)
        {
            using RSA? rsa = certificate.GetRSAPublicKey();
            if (rsa == null)
            {
                // not an RSA key: the decryption fails on its own without
                // private-key work.
                return;
            }

            int cipherTextBlockSize = RsaUtils.GetCipherTextBlockSize(rsa);
            ThrowIfRsaCipherTextTooLong(
                cipherTextLength,
                cipherTextBlockSize,
                cipherTextBlockSize - kMaxRsaPaddingOverhead,
                maxPlainTextLength);
        }

        /// <summary>
        /// Rejects a legacy RSA encrypted token secret that holds more RSA blocks
        /// than a plaintext of <paramref name="maxPlainTextLength"/> bytes needs.
        /// </summary>
        /// <param name="cipherTextLength">The length of the encrypted secret.</param>
        /// <param name="cipherTextBlockSize">The RSA cipher text block size.</param>
        /// <param name="plainTextBlockSize">
        /// The RSA plain text block size for the padding in use.
        /// </param>
        /// <param name="maxPlainTextLength">
        /// The longest legitimate plaintext, including the length prefix and the
        /// server nonce.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Bad_IdentityTokenInvalid when the secret is too long.
        /// </exception>
        public static void ThrowIfRsaCipherTextTooLong(
            int cipherTextLength,
            int cipherTextBlockSize,
            int plainTextBlockSize,
            int maxPlainTextLength)
        {
            if (cipherTextBlockSize <= 0 || plainTextBlockSize <= 0)
            {
                return;
            }

            // RsaUtils.Encrypt always appends a block (length / block size + 1),
            // so a block-aligned plaintext of the maximum length takes one block
            // more than it fills; allow that block.
            long maxBlocks = ((long)maxPlainTextLength / plainTextBlockSize) + 1;
            if (cipherTextLength > maxBlocks * cipherTextBlockSize)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The encrypted identity token secret is longer than {0} RSA blocks.",
                    maxBlocks);
            }
        }

        /// <summary>
        /// Rejects a decrypted token secret that is longer than allowed, so every
        /// token encryption format accepts the same secrets.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Bad_IdentityTokenInvalid when the secret is too long.
        /// </exception>
        public static void ThrowIfSecretTooLong(byte[]? secret, int maxLength)
        {
            if (secret != null && secret.Length > maxLength)
            {
                CryptoUtils.ZeroMemory(secret);
                ThrowIfSecretTooLong(secret.Length, maxLength);
            }
        }

        /// <summary>
        /// Rejects a decrypted token secret length that is longer than allowed.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Bad_IdentityTokenInvalid when the secret is too long.
        /// </exception>
        public static void ThrowIfSecretTooLong(int secretLength, int maxLength)
        {
            if (secretLength > maxLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The identity token secret is longer than {0} bytes.",
                    maxLength);
            }
        }
    }
}
