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
using System.Runtime.InteropServices;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Immutable material for a single PubSub security token: the
    /// SKS-issued signing and encrypting keys together with the
    /// per-group key nonce and lifetime metadata. Sensitive — must
    /// never be logged or serialized to telemetry.
    /// </summary>
    /// <remarks>
    /// Implements the SecurityKey representation described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>. One instance per
    /// <c>TokenId</c>; new tokens are produced by an
    /// <see cref="IPubSubSecurityKeyProvider"/> on rotation.
    /// </remarks>
    public sealed class PubSubSecurityKey : IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubSecurityKey"/>.
        /// </summary>
        /// <param name="tokenId">SKS-assigned token identifier.</param>
        /// <param name="signingKey">Signing key (HMAC).</param>
        /// <param name="encryptingKey">Encrypting key (AES-CTR).</param>
        /// <param name="keyNonce">Per-group nonce material.</param>
        /// <param name="issuedAt">When the SKS issued the token.</param>
        /// <param name="lifetime">Validity duration.</param>
        public PubSubSecurityKey(
            uint tokenId,
            ByteString signingKey,
            ByteString encryptingKey,
            ByteString keyNonce,
            DateTimeUtc issuedAt,
            TimeSpan lifetime)
        {
            if (signingKey.IsNull)
            {
                throw new ArgumentException("Signing key must not be null.", nameof(signingKey));
            }
            if (encryptingKey.IsNull)
            {
                throw new ArgumentException("Encrypting key must not be null.", nameof(encryptingKey));
            }
            if (keyNonce.IsNull)
            {
                throw new ArgumentException("Key nonce must not be null.", nameof(keyNonce));
            }
            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime), "Lifetime must be positive.");
            }

            TokenId = tokenId;
            SigningKey = signingKey;
            EncryptingKey = encryptingKey;
            KeyNonce = keyNonce;
            IssuedAt = issuedAt;
            Lifetime = lifetime;
        }

        /// <summary>
        /// SKS-assigned token identifier echoed in the SecurityHeader
        /// of every secured NetworkMessage.
        /// </summary>
        public uint TokenId { get; }

        /// <summary>
        /// Signing key. Sensitive material.
        /// </summary>
        public ByteString SigningKey { get; }

        /// <summary>
        /// Encrypting key. Sensitive material.
        /// </summary>
        public ByteString EncryptingKey { get; }

        /// <summary>
        /// Per-group key nonce used as input to the per-message
        /// nonce derivation (see Part 14 §7.2.4.4.3.2).
        /// </summary>
        public ByteString KeyNonce { get; }

        /// <summary>
        /// Token issuance timestamp.
        /// </summary>
        public DateTimeUtc IssuedAt { get; }

        /// <summary>
        /// Token validity duration.
        /// </summary>
        public TimeSpan Lifetime { get; }

        /// <summary>
        /// Returns <see langword="true"/> if the supplied clock is
        /// past <see cref="IssuedAt"/> + <see cref="Lifetime"/>.
        /// </summary>
        /// <param name="clock">Time source to query.</param>
        /// <returns>Whether the token has expired.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool IsExpired(TimeProvider clock)
        {
            if (clock is null)
            {
                throw new ArgumentNullException(nameof(clock));
            }
            DateTimeUtc now = DateTimeUtc.From(clock.GetUtcNow().UtcDateTime);
            return (now - IssuedAt) >= Lifetime;
        }

        /// <summary>
        /// Zeroizes the key material held by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            ClearSensitiveMemory(SigningKey.Memory);
            ClearSensitiveMemory(EncryptingKey.Memory);
            ClearSensitiveMemory(KeyNonce.Memory);
            m_disposed = true;
        }

        private static void ClearSensitiveMemory(ReadOnlyMemory<byte> memory)
        {
            if (!MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment) ||
                segment.Array is null ||
                segment.Count == 0)
            {
                return;
            }

            Span<byte> span = segment.Array.AsSpan(segment.Offset, segment.Count);
            CryptoUtils.ZeroMemory(span);
        }

        private bool m_disposed;
    }
}
