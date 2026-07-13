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
using System.Buffers;
using Opc.Ua.PubSub.Security.Internal;

namespace Opc.Ua.PubSub.Security.Policies
{
    /// <summary>
    /// PubSub security policy combining HMAC-SHA-256 signing with
    /// AES-128 CTR encryption.
    /// </summary>
    /// <remarks>
    /// Implements the <c>PubSub-Aes128-CTR</c> entry of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>. Key sizes,
    /// nonce length and signature length are fixed by the spec:
    /// 32-byte HMAC-SHA-256 signing key, 16-byte AES-128 encrypting
    /// key, 12-byte message nonce and a 32-byte HMAC tag.
    /// </remarks>
    public sealed class PubSubAes128CtrPolicy : IPubSubSecurityPolicy
    {
        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static readonly PubSubAes128CtrPolicy Instance = new();

        private PubSubAes128CtrPolicy()
        {
        }

        /// <inheritdoc/>
        public string PolicyUri => PubSubSecurityPolicyUri.PubSubAes128Ctr;

        /// <inheritdoc/>
        public int SigningKeyLength => 32;

        /// <inheritdoc/>
        public int EncryptingKeyLength => 16;

        /// <inheritdoc/>
        public int NonceLength => 12;

        /// <inheritdoc/>
        public int SignatureLength => 32;

        /// <inheritdoc/>
        public void Sign(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signingKey,
            Span<byte> signature)
        {
            if (signingKey.Length != SigningKeyLength)
            {
                throw new ArgumentException(
                    $"Signing key must be exactly {SigningKeyLength} bytes.",
                    nameof(signingKey));
            }
            if (signature.Length < SignatureLength)
            {
                throw new ArgumentException(
                    $"Signature buffer must be at least {SignatureLength} bytes.",
                    nameof(signature));
            }
            HmacSha256.HashData(signingKey, data, signature);
        }

        /// <inheritdoc/>
        public bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature,
            ReadOnlySpan<byte> signingKey)
        {
            if (signingKey.Length != SigningKeyLength)
            {
                return false;
            }
            if (signature.Length != SignatureLength)
            {
                return false;
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(SignatureLength);
            try
            {
                Span<byte> computed = rented.AsSpan(0, SignatureLength);
                HmacSha256.HashData(signingKey, data, computed);
                return CryptoUtils.FixedTimeEquals(computed, signature);
            }
            finally
            {
                Array.Clear(rented, 0, SignatureLength);
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        /// <inheritdoc/>
        public void Encrypt(
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> ciphertext)
        {
            if (encryptingKey.Length != EncryptingKeyLength)
            {
                throw new ArgumentException(
                    $"Encrypting key must be exactly {EncryptingKeyLength} bytes.",
                    nameof(encryptingKey));
            }
            AesCtrTransform.EncryptOrDecrypt(encryptingKey, nonce, plaintext, ciphertext);
        }

        /// <inheritdoc/>
        public void Decrypt(
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> plaintext)
        {
            // AES-CTR is symmetric — decryption is the same XOR keystream
            // operation as encryption.
            Encrypt(ciphertext, encryptingKey, nonce, plaintext);
        }
    }
}
