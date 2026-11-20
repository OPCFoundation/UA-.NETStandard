// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Generates fresh <see cref="PubSubSecurityKey"/> material for
    /// an in-memory SKS implementation.
    /// </summary>
    /// <remarks>
    /// The lengths of the signing key, encrypting key and key nonce
    /// are taken from the supplied <see cref="IPubSubSecurityPolicy"/>
    /// — see
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>. The
    /// random material comes from
    /// <see cref="RandomNumberGenerator"/> so the keys are
    /// cryptographically strong.
    /// </remarks>
    internal static class SksKeyGenerator
    {
        /// <summary>
        /// Produces a single fresh <see cref="PubSubSecurityKey"/>
        /// for <paramref name="policy"/>.
        /// </summary>
        /// <param name="policy">Security policy bundle.</param>
        /// <param name="tokenId">Token id assigned to the new key.</param>
        /// <param name="issuedAt">Issuance timestamp.</param>
        /// <param name="lifetime">Key validity duration.</param>
        /// <returns>The generated key.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static PubSubSecurityKey Generate(
            IPubSubSecurityPolicy policy,
            uint tokenId,
            DateTimeUtc issuedAt,
            TimeSpan lifetime)
        {
            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            int signingLength = policy.SigningKeyLength;
            int encryptingLength = policy.EncryptingKeyLength;
            int nonceLength = AesCtrNonceLayout.GetKeyNonceLength(policy);

            byte[]? signing = null;
            byte[]? encrypting = null;
            byte[]? nonce = null;
            try
            {
                signing = NewRandom(signingLength);
                encrypting = NewRandom(encryptingLength);
                nonce = NewRandom(nonceLength);

                return new PubSubSecurityKey(
                    tokenId,
                    ByteString.Create(signing),
                    ByteString.Create(encrypting),
                    ByteString.Create(nonce),
                    issuedAt,
                    lifetime);
            }
            finally
            {
                ClearSensitiveBuffer(signing);
                ClearSensitiveBuffer(encrypting);
                ClearSensitiveBuffer(nonce);
            }
        }

        /// <summary>
        /// Concatenates a key's signing/encrypting/nonce material
        /// into the wire format expected by the
        /// <c>GetSecurityKeys</c> response.
        /// </summary>
        /// <param name="key">Key whose components to pack.</param>
        /// <returns>The packed bytes.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static byte[] Pack(PubSubSecurityKey key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            ReadOnlySpan<byte> signing = key.SigningKey.Span;
            ReadOnlySpan<byte> encrypting = key.EncryptingKey.Span;
            ReadOnlySpan<byte> nonce = key.KeyNonce.Span;
            byte[] packed = new byte[signing.Length + encrypting.Length + nonce.Length];
            try
            {
                signing.CopyTo(packed.AsSpan(0, signing.Length));
                encrypting.CopyTo(packed.AsSpan(signing.Length, encrypting.Length));
                nonce.CopyTo(packed.AsSpan(signing.Length + encrypting.Length, nonce.Length));
                return packed;
            }
            catch
            {
                ClearSensitiveBuffer(packed);
                throw;
            }
        }

        private static void ClearSensitiveBuffer(byte[]? buffer)
        {
            if (buffer is null)
            {
                return;
            }
            CryptoUtils.ZeroMemory(buffer);
        }

        private static byte[] NewRandom(int length)
        {
            byte[] bytes = new byte[length];
            if (length > 0)
            {
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(bytes);
            }
            return bytes;
        }
    }
}
