// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Provides per-message nonces honouring the AES-CTR layout for
    /// PubSub security. A nonce is composed of a publisher-chosen
    /// <c>MessageRandom</c> prefix (CSPRNG) followed by a monotonic
    /// per-key <c>MessageSequenceNumber</c> suffix that increments for
    /// every encrypted NetworkMessage produced under a given key. The
    /// SKS-issued <c>KeyNonce</c> participates as domain-separation
    /// input and the per-key counter resets whenever the active key
    /// changes, so no nonce value repeats within a key's lifetime.
    /// </summary>
    /// <remarks>
    /// Implements the nonce layout from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.2">
    /// Part 14 §7.2.4.4.3.2 (Table 156) PubSub nonce composition</see>.
    /// </remarks>
    public interface INonceProvider
    {
        /// <summary>
        /// Writes the next nonce for the supplied key into
        /// <paramref name="buffer"/>. The buffer length must equal the
        /// policy's <see cref="IPubSubSecurityPolicy.NonceLength"/>.
        /// Implementations track the monotonic message counter per
        /// <paramref name="keyId"/>; the counter resets when the key
        /// changes. Implementations may throw when the per-key message
        /// count would exceed the configured cap, signalling that a key
        /// rollover is required before any further message is sent.
        /// </summary>
        /// <param name="keyId">
        /// SecurityTokenId of the key the nonce is generated for.
        /// </param>
        /// <param name="keyNonce">
        /// SKS-issued <c>KeyNonce</c> material for the key, folded in
        /// as domain-separation input. May be empty.
        /// </param>
        /// <param name="buffer">Destination span receiving the nonce.</param>
        void GetNext(uint keyId, ReadOnlySpan<byte> keyNonce, Span<byte> buffer);
    }
}
