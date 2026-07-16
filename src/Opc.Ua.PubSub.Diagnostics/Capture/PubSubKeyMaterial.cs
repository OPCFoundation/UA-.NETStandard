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

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Immutable snapshot of one PubSub security key, decoupled from the
    /// runtime key ring so it can be written to / read from a key log and
    /// used to drive offline decryption of captured, encrypted UADP
    /// NetworkMessages (Part 14 §8.3, Annex A.2.2.5 PubSub-Aes-CTR).
    /// </summary>
    /// <remarks>
    /// All key bytes are defensive copies and are zeroed on
    /// <see cref="Dispose"/> so the snapshot is safe to keep alive after the
    /// originating key has rolled over.
    /// </remarks>
    public sealed class PubSubKeyMaterial : IDisposable
    {
        /// <summary>
        /// Constructs an immutable key snapshot.
        /// </summary>
        /// <param name="securityGroupId">
        /// The SecurityGroupId the key belongs to (SKS grouping).
        /// </param>
        /// <param name="tokenId">
        /// The SecurityTokenId carried in the UADP SecurityHeader that
        /// selects this key.
        /// </param>
        /// <param name="securityPolicyUri">
        /// The PubSub security policy URI (e.g.
        /// <c>http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR</c>).
        /// </param>
        /// <param name="signingKey">HMAC signing key bytes.</param>
        /// <param name="encryptingKey">AES-CTR encrypting key bytes.</param>
        /// <param name="keyNonce">Key nonce bytes.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="securityGroupId"/> or
        /// <paramref name="securityPolicyUri"/> is <see langword="null"/>.
        /// </exception>
        public PubSubKeyMaterial(
            string securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            byte[]? signingKey,
            byte[]? encryptingKey,
            byte[]? keyNonce)
        {
            ArgumentNullException.ThrowIfNull(securityGroupId);
            ArgumentNullException.ThrowIfNull(securityPolicyUri);

            SecurityGroupId = securityGroupId;
            TokenId = tokenId;
            SecurityPolicyUri = securityPolicyUri;
            m_signingKey = Copy(signingKey);
            m_encryptingKey = Copy(encryptingKey);
            m_keyNonce = Copy(keyNonce);
        }

        /// <summary>
        /// The SecurityGroupId the key belongs to.
        /// </summary>
        public string SecurityGroupId { get; }

        /// <summary>
        /// The SecurityTokenId that selects this key.
        /// </summary>
        public uint TokenId { get; }

        /// <summary>
        /// The PubSub security policy URI.
        /// </summary>
        public string SecurityPolicyUri { get; }

        /// <summary>
        /// The HMAC signing key bytes. Empty when not present.
        /// </summary>
        public ReadOnlySpan<byte> SigningKey => m_disposed ? default : m_signingKey;

        /// <summary>
        /// The AES-CTR encrypting key bytes. Empty when not present.
        /// </summary>
        public ReadOnlySpan<byte> EncryptingKey => m_disposed ? default : m_encryptingKey;

        /// <summary>
        /// The key nonce bytes. Empty when not present.
        /// </summary>
        public ReadOnlySpan<byte> KeyNonce => m_disposed ? default : m_keyNonce;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            Array.Clear(m_signingKey, 0, m_signingKey.Length);
            Array.Clear(m_encryptingKey, 0, m_encryptingKey.Length);
            Array.Clear(m_keyNonce, 0, m_keyNonce.Length);
        }

        private static byte[] Copy(byte[]? source)
        {
            if (source is null || source.Length == 0)
            {
                return [];
            }
            byte[] copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }

        private readonly byte[] m_signingKey;
        private readonly byte[] m_encryptingKey;
        private readonly byte[] m_keyNonce;
        private bool m_disposed;
    }
}
