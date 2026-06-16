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
using System.Collections.Generic;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Output arguments returned by a single
    /// <c>PubSubKeyServiceType.GetSecurityKeys</c> call.
    /// </summary>
    /// <remarks>
    /// Implements the output-argument set defined by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. Each entry of
    /// <see cref="Keys"/> is the concatenation
    /// <c>SigningKey || EncryptingKey || KeyNonce</c> whose component
    /// lengths are determined by <see cref="SecurityPolicyUri"/>; the
    /// derived <see cref="Unpacked"/> view splits that material into
    /// per-token <see cref="PubSubSecurityKey"/> instances using the
    /// resolved <see cref="IPubSubSecurityPolicy"/>.
    /// </remarks>
    public sealed record SksKeyResponse
    {
        private IReadOnlyList<PubSubSecurityKey>? m_unpacked;

        /// <summary>
        /// Initializes a new <see cref="SksKeyResponse"/>.
        /// </summary>
        /// <param name="securityPolicyUri">
        /// URI of the security policy whose lengths govern key unpacking.
        /// </param>
        /// <param name="firstTokenId">
        /// Token id of the first key in <paramref name="keys"/>.
        /// </param>
        /// <param name="keys">
        /// Packed key material; one entry per token. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="timeToNextKey">
        /// Time remaining before the next rotation. May be
        /// <see cref="TimeSpan.Zero"/> if the SKS does not predict the
        /// next rotation.
        /// </param>
        /// <param name="keyLifetime">
        /// Validity duration assigned to every key in
        /// <paramref name="keys"/>.
        /// </param>
        public SksKeyResponse(
            string securityPolicyUri,
            uint firstTokenId,
            IReadOnlyList<byte[]> keys,
            TimeSpan timeToNextKey,
            TimeSpan keyLifetime)
        {
            if (securityPolicyUri is null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }
            if (keys is null)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            if (keyLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(keyLifetime),
                    "Key lifetime must be positive.");
            }
            SecurityPolicyUri = securityPolicyUri;
            FirstTokenId = firstTokenId;
            Keys = keys;
            TimeToNextKey = timeToNextKey;
            KeyLifetime = keyLifetime;
        }

        /// <summary>
        /// URI of the security policy that produced the keys.
        /// </summary>
        public string SecurityPolicyUri { get; }

        /// <summary>
        /// SKS-assigned token id of the first entry in
        /// <see cref="Keys"/>. Subsequent entries occupy
        /// monotonically increasing token ids.
        /// </summary>
        public uint FirstTokenId { get; }

        /// <summary>
        /// Packed key material — one ByteString per token id.
        /// </summary>
        public IReadOnlyList<byte[]> Keys { get; }

        /// <summary>
        /// Time remaining before the SKS expects to rotate the
        /// active token.
        /// </summary>
        public TimeSpan TimeToNextKey { get; }

        /// <summary>
        /// Validity duration applied to every entry of
        /// <see cref="Keys"/>.
        /// </summary>
        public TimeSpan KeyLifetime { get; }

        /// <summary>
        /// Splits each entry of <see cref="Keys"/> into a
        /// <see cref="PubSubSecurityKey"/> using the policy's
        /// signing/encrypting/nonce lengths.
        /// </summary>
        /// <remarks>
        /// Returns an empty list when <see cref="SecurityPolicyUri"/>
        /// is the <c>None</c> URI or is not registered. Throws
        /// <see cref="InvalidOperationException"/> when a packed key
        /// has the wrong length for the resolved policy.
        /// </remarks>
        public IReadOnlyList<PubSubSecurityKey> Unpacked
        {
            get
            {
                IReadOnlyList<PubSubSecurityKey>? cached = m_unpacked;
                if (cached is not null)
                {
                    return cached;
                }
                cached = UnpackKeys();
                m_unpacked = cached;
                return cached;
            }
        }

        private PubSubSecurityKey[] UnpackKeys()
        {
            IPubSubSecurityPolicy? policy =
                PubSubSecurityPolicyRegistry.GetByUri(SecurityPolicyUri);
            if (policy is null)
            {
                return Array.Empty<PubSubSecurityKey>();
            }
            int signingLength = policy.SigningKeyLength;
            int encryptingLength = policy.EncryptingKeyLength;
            int nonceLength = policy.NonceLength;
            int totalLength = signingLength + encryptingLength + nonceLength;
            if (totalLength == 0)
            {
                return Array.Empty<PubSubSecurityKey>();
            }
            DateTimeUtc issuedAt = DateTimeUtc.From(DateTime.UtcNow);
            var unpacked = new PubSubSecurityKey[Keys.Count];
            for (int i = 0; i < Keys.Count; i++)
            {
                byte[] packed = Keys[i] ?? throw new InvalidOperationException(
                    "Packed key material must not be null.");
                if (packed.Length != totalLength)
                {
                    throw new InvalidOperationException(
                        $"Packed key length {packed.Length} does not match " +
                        $"policy '{SecurityPolicyUri}' total length {totalLength}.");
                }
                byte[] signing = new byte[signingLength];
                byte[] encrypting = new byte[encryptingLength];
                byte[] nonce = new byte[nonceLength];
                Array.Copy(packed, 0, signing, 0, signingLength);
                Array.Copy(packed, signingLength, encrypting, 0, encryptingLength);
                Array.Copy(
                    packed,
                    signingLength + encryptingLength,
                    nonce,
                    0,
                    nonceLength);
                unpacked[i] = new PubSubSecurityKey(
                    unchecked(FirstTokenId + (uint)i),
                    ByteString.Create(signing),
                    ByteString.Create(encrypting),
                    ByteString.Create(nonce),
                    issuedAt,
                    KeyLifetime);
            }
            return unpacked;
        }
    }
}
