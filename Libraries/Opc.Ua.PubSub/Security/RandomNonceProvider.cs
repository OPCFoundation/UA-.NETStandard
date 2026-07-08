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
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Default <see cref="INonceProvider"/> backed by a cryptographic
    /// RNG. Each call to <see cref="GetNext"/> generates 4 random
    /// bytes for <c>MessageRandom</c> and appends a monotonic per-key
    /// <c>MessageSequenceNumber</c> per Part 14 Table 156. The counter
    /// resets whenever the active key changes and is hard-capped so a
    /// publisher never reuses a <c>(key, nonce)</c> pair.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.2">
    /// Part 14 §7.2.4.4.3.2 (Table 156) PubSub nonce composition</see>.
    /// Thread-safe — concurrent <see cref="GetNext"/> calls serialise
    /// through an internal <see cref="Lock"/>.
    /// </remarks>
    public sealed class RandomNonceProvider : INonceProvider, IDisposable
    {
        /// <summary>
        /// Default maximum number of messages emitted under a single
        /// key before a rollover is forced. Comfortably below the
        /// 2^64 sequence space and the AES block-count guidance while
        /// remaining generous for high-rate publishers.
        /// </summary>
        public const ulong DefaultMaxMessagesPerKey = 1UL << 48;

        private readonly Lock m_lock = new();
        private readonly RandomNumberGenerator m_rng;
        private readonly ulong m_publisherIdLow64;
        private readonly ulong m_maxMessagesPerKey;
        private bool m_hasKey;
        private uint m_currentKeyId;
        private ulong m_messageCount;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="RandomNonceProvider"/>.
        /// </summary>
        /// <param name="publisherId">PublisherId of the local node.</param>
        /// <param name="timeProvider">
        /// Time source. Currently unused — accepted for API symmetry
        /// with other PubSub services and to allow future replay /
        /// rate-limit enforcement based on wall-clock.
        /// </param>
        /// <param name="maxMessagesPerKey">
        /// Hard cap on the number of messages emitted under a single
        /// key. <see cref="GetNext"/> throws once the cap is reached so
        /// the publisher forces a key rollover before the per-key
        /// counter could repeat a nonce. Defaults to
        /// <see cref="DefaultMaxMessagesPerKey"/>.
        /// </param>
        public RandomNonceProvider(
            in PublisherId publisherId,
            TimeProvider? timeProvider = null,
            ulong maxMessagesPerKey = DefaultMaxMessagesPerKey)
        {
            _ = timeProvider;
            if (maxMessagesPerKey == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxMessagesPerKey),
                    "The per-key message cap must be positive.");
            }
            m_publisherIdLow64 = AesCtrNonceLayout.ToLow64(publisherId);
            m_maxMessagesPerKey = maxMessagesPerKey;
            m_rng = RandomNumberGenerator.Create();
        }

        /// <summary>
        /// Stable 64-bit projection of the configured PublisherId.
        /// </summary>
        public ulong PublisherIdLow64 => m_publisherIdLow64;

        /// <summary>
        /// Hard cap on the number of messages emitted under a single
        /// key before a rollover is forced.
        /// </summary>
        public ulong MaxMessagesPerKey => m_maxMessagesPerKey;

        /// <inheritdoc/>
        public void GetNext(uint keyId, ReadOnlySpan<byte> keyNonce, Span<byte> buffer)
        {
            if (buffer.Length != AesCtrNonceLayout.NonceLength)
            {
                throw new ArgumentException(
                    $"Nonce buffer must be exactly {AesCtrNonceLayout.NonceLength} bytes.",
                    nameof(buffer));
            }

            uint keyNonceFold = Fold32(keyNonce);

            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(RandomNonceProvider));
                }

                if (!m_hasKey || m_currentKeyId != keyId)
                {
                    m_hasKey = true;
                    m_currentKeyId = keyId;
                    m_messageCount = 0;
                }

                if (m_messageCount >= m_maxMessagesPerKey)
                {
                    throw new InvalidOperationException(
                        "PubSub nonce counter exhausted for key " +
                        keyId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "; a key rollover is required before sending further messages.");
                }

                ulong sequenceNumber = m_messageCount;
                m_messageCount++;

                Span<byte> messageRandom = stackalloc byte[AesCtrNonceLayout.MessageRandomLength];
#if NET6_0_OR_GREATER
                m_rng.GetBytes(messageRandom);
#else
                byte[] tmp = new byte[AesCtrNonceLayout.MessageRandomLength];
                m_rng.GetBytes(tmp);
                tmp.AsSpan().CopyTo(messageRandom);
#endif
                uint random = BinaryPrimitives.ReadUInt32BigEndian(messageRandom) ^ keyNonceFold;
                AesCtrNonceLayout.Build(random, sequenceNumber, buffer);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_rng.Dispose();
            }
        }

        private static uint Fold32(ReadOnlySpan<byte> data)
        {
            unchecked
            {
                const uint offsetBasis = 2166136261u;
                const uint prime = 16777619u;
                uint hash = offsetBasis;
                for (int i = 0; i < data.Length; i++)
                {
                    hash = (hash ^ data[i]) * prime;
                }
                return hash;
            }
        }
    }
}
