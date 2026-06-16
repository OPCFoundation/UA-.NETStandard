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
    /// bytes for <c>MessageRandom</c> and combines them with the
    /// fixed publisher-id projection per Part 14 Table 156.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.2">
    /// Part 14 §7.2.4.4.3.2 (Table 156) PubSub nonce composition</see>.
    /// Thread-safe — concurrent <see cref="GetNext"/> calls serialise
    /// through an internal <see cref="System.Threading.Lock"/>.
    /// </remarks>
    public sealed class RandomNonceProvider : INonceProvider, IDisposable
    {
        private readonly Lock m_lock = new();
        private readonly RandomNumberGenerator m_rng;
        private readonly ulong m_publisherIdLow64;
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
        public RandomNonceProvider(
            in PublisherId publisherId,
            TimeProvider? timeProvider = null)
        {
            _ = timeProvider;
            m_publisherIdLow64 = AesCtrNonceLayout.ToLow64(publisherId);
            m_rng = RandomNumberGenerator.Create();
        }

        /// <summary>
        /// Stable 64-bit projection of the configured PublisherId.
        /// </summary>
        public ulong PublisherIdLow64 => m_publisherIdLow64;

        /// <inheritdoc/>
        public void GetNext(Span<byte> buffer)
        {
            if (buffer.Length != AesCtrNonceLayout.NonceLength)
            {
                throw new ArgumentException(
                    $"Nonce buffer must be exactly {AesCtrNonceLayout.NonceLength} bytes.",
                    nameof(buffer));
            }

            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(RandomNonceProvider));
                }
                Span<byte> messageRandom = stackalloc byte[AesCtrNonceLayout.MessageRandomLength];
#if NET6_0_OR_GREATER
                m_rng.GetBytes(messageRandom);
#else
                byte[] tmp = new byte[AesCtrNonceLayout.MessageRandomLength];
                m_rng.GetBytes(tmp);
                tmp.AsSpan().CopyTo(messageRandom);
#endif
                uint random = BinaryPrimitives.ReadUInt32BigEndian(messageRandom);
                AesCtrNonceLayout.Build(random, m_publisherIdLow64, buffer);
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
    }
}
