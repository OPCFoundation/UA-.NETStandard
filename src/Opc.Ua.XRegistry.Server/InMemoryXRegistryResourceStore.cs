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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Default <see cref="IXRegistryResourceStore"/> that keeps resource documents in the server
    /// process. Suitable for a single-server registry; a high-availability deployment substitutes a
    /// shared store so the documents outlive one process.
    /// </summary>
    public sealed class InMemoryXRegistryResourceStore : IXRegistryResourceStore
    {
        /// <inheritdoc/>
        public ValueTask<ByteString> ReadAsync(string resourceKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }

            lock (m_lock)
            {
                return m_documents.TryGetValue(resourceKey, out byte[]? document)
                    ? new ValueTask<ByteString>(ByteString.From(document))
                    : new ValueTask<ByteString>(default(ByteString));
            }
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            string resourceKey,
            ReadOnlyMemory<byte> document,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }

            byte[] copy = document.ToArray();
            lock (m_lock)
            {
                m_documents[resourceKey] = copy;
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }

            lock (m_lock)
            {
                return new ValueTask<bool>(m_documents.Remove(resourceKey));
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, byte[]> m_documents = [];
    }
}
