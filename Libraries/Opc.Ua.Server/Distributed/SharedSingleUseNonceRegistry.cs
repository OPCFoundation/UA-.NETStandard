/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// <see cref="ISingleUseNonceRegistry"/> backed by an
    /// <see cref="ISharedKeyValueStore"/>. Consumption is a single
    /// compare-and-swap that creates a marker key only when it is absent, so the
    /// store's atomicity guarantees exactly one replica wins the race to consume
    /// a given nonce. The nonce itself is never stored: the key is the SHA-256
    /// digest of the nonce, so the secret-bearing keyspace stays one-way
    /// (security-review Finding 6 hygiene).
    /// </summary>
    public sealed class SharedSingleUseNonceRegistry : ISingleUseNonceRegistry
    {
        /// <summary>
        /// Creates a registry over a shared key/value backend.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="keyPrefix">
        /// The key prefix under which consumed-nonce markers are recorded.
        /// </param>
        public SharedSingleUseNonceRegistry(ISharedKeyValueStore store, string keyPrefix = "nonce/")
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        }

        /// <inheritdoc/>
        public ValueTask<bool> TryConsumeAsync(ByteString nonce, CancellationToken ct = default)
        {
            if (nonce.IsNull || nonce.IsEmpty)
            {
                throw new ArgumentException("Nonce must not be null or empty.", nameof(nonce));
            }

            string key = m_keyPrefix + Digest(nonce);
            byte[] marker = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(marker, DateTime.UtcNow.Ticks);

            // expected = default(ByteString) => require the key to be absent, so
            // the swap succeeds only for the first consumer of this nonce.
            return m_store.CompareAndSwapAsync(key, default, new ByteString(marker), ct);
        }

        private static string Digest(ByteString nonce)
        {
            byte[] data = nonce.ToArray();
#if NET8_0_OR_GREATER
            byte[] hash = SHA256.HashData(data);
#else
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(data);
            }
#endif
            return Convert.ToBase64String(hash);
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly string m_keyPrefix;
    }
}
