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
            int nonceLength = policy.NonceLength;

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
