// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
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
        private ArrayOf<PubSubSecurityKey>? m_unpacked;

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
            ArrayOf<byte[]> keys,
            TimeSpan timeToNextKey,
            TimeSpan keyLifetime)
        {
            if (securityPolicyUri is null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
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
        public ArrayOf<byte[]> Keys { get; }

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
        public ArrayOf<PubSubSecurityKey> Unpacked =>
            m_unpacked ??= UnpackKeys(_ => DateTimeUtc.From(DateTime.UtcNow));

        /// <summary>
        /// Unpacks the response using the supplied time provider to timestamp the keys.
        /// </summary>
        /// <param name="timeProvider">The time source used for key issue timestamps.</param>
        /// <returns>The unpacked security keys.</returns>
        internal ArrayOf<PubSubSecurityKey> Unpack(TimeProvider timeProvider)
        {
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            var now = DateTimeUtc.From(timeProvider.GetUtcNow().UtcDateTime);
            return UnpackKeys(_ => now);
        }

        /// <summary>
        /// Unpacks the keys of a response to a request for the current key
        /// (StartingTokenId 0), giving every key the time it becomes the
        /// current key: the first key is current until
        /// <see cref="TimeToNextKey"/> has elapsed, and every later key is
        /// current for <see cref="KeyLifetime"/> after its predecessor
        /// (Part 14 §8.3.2).
        /// </summary>
        /// <param name="timeProvider">Clock that supplies the current time.</param>
        /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
        internal ArrayOf<PubSubSecurityKey> UnpackScheduled(TimeProvider timeProvider)
        {
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            var now = DateTimeUtc.From(timeProvider.GetUtcNow().UtcDateTime);
            TimeSpan lifetime = KeyLifetime;
            if (lifetime <= TimeSpan.Zero ||
                Keys.Count > MaxScheduledHorizon.Ticks / lifetime.Ticks)
            {
                return UnpackKeys(_ => now);
            }
            // A TimeToNextKey of 0 (unknown) or above the lifetime is taken
            // as a full lifetime for the current key.
            TimeSpan untilNextKey = TimeToNextKey > TimeSpan.Zero && TimeToNextKey < lifetime
                ? TimeToNextKey
                : lifetime;
            DateTimeUtc firstExpiry = now + untilNextKey;
            return UnpackKeys(index =>
                firstExpiry + TimeSpan.FromTicks(lifetime.Ticks * (index - 1)));
        }

        /// <summary>
        /// Longest time span the keys of one response are scheduled over.
        /// Bounds the arithmetic for a key count and lifetime from an
        /// untrusted SKS.
        /// </summary>
        private static readonly TimeSpan MaxScheduledHorizon = TimeSpan.FromDays(100 * 365);

        private ArrayOf<PubSubSecurityKey> UnpackKeys(Func<int, DateTimeUtc> issuedAt)
        {
            IPubSubSecurityPolicy? policy =
                PubSubSecurityPolicyRegistry.Default.GetByUri(SecurityPolicyUri);
            if (policy is null)
            {
                return [];
            }
            int signingLength = policy.SigningKeyLength;
            int encryptingLength = policy.EncryptingKeyLength;
            int nonceLength = AesCtrNonceLayout.GetKeyNonceLength(policy);
            int totalLength = signingLength + encryptingLength + nonceLength;
            if (totalLength == 0)
            {
                return [];
            }
            var unpacked = new PubSubSecurityKey[Keys.Count];
            for (int i = 0; i < Keys.Count; i++)
            {
                byte[] packed = Keys[i] ??
                    throw new InvalidOperationException(
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

                try
                {
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
                        issuedAt(i),
                        KeyLifetime);
                }
                finally
                {
                    // ByteString.Create copies, so these intermediates are the
                    // stack's own plain copies of the key material and nothing
                    // else refers to them. Left alone they would sit in the heap
                    // for the lifetime of the process; the key itself is zeroed
                    // by PubSubSecurityKey.Dispose when the ring retires it.
                    CryptoUtils.ZeroMemory(signing);
                    CryptoUtils.ZeroMemory(encrypting);
                    CryptoUtils.ZeroMemory(nonce);
                }
            }
            return unpacked;
        }
    }
}
