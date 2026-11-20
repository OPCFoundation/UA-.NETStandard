// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
