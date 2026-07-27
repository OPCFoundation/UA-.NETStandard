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
    public sealed class InMemoryResourceStore : IXRegistryResourceStore
    {
        /// <inheritdoc/>
        public ValueTask<ByteString> ReadAsync(
            string resourceKey,
            long offset,
            int count,
            CancellationToken ct = default)
        {
            ValidateKey(resourceKey);
            ValidateRange(offset, count);

            lock (m_lock)
            {
                if (!m_documents.TryGetValue(resourceKey, out List<byte>? document))
                {
                    return new ValueTask<ByteString>(default(ByteString));
                }

                if (offset >= document.Count || count == 0)
                {
                    return new ValueTask<ByteString>(ByteString.From([]));
                }

                int take = (int)Math.Min(count, document.Count - offset);
                var chunk = new byte[take];
                document.CopyTo((int)offset, chunk, 0, take);
                return new ValueTask<ByteString>(ByteString.From(chunk));
            }
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(
            string resourceKey,
            long offset,
            ReadOnlyMemory<byte> data,
            CancellationToken ct = default)
        {
            ValidateKey(resourceKey);
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            lock (m_lock)
            {
                if (!m_documents.TryGetValue(resourceKey, out List<byte>? document))
                {
                    document = [];
                    m_documents[resourceKey] = document;
                }

                // Writing past the end grows the document; the gap reads back as zero bytes.
                while (document.Count < offset)
                {
                    document.Add(0);
                }

                ReadOnlySpan<byte> span = data.Span;
                int at = (int)offset;
                int overwrite = Math.Min(span.Length, document.Count - at);
                for (int i = 0; i < overwrite; i++)
                {
                    document[at + i] = span[i];
                }
                for (int i = overwrite; i < span.Length; i++)
                {
                    document.Add(span[i]);
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default)
        {
            ValidateKey(resourceKey);

            lock (m_lock)
            {
                return new ValueTask<long>(
                    m_documents.TryGetValue(resourceKey, out List<byte>? document) ? document.Count : -1);
            }
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
        {
            ValidateKey(resourceKey);

            lock (m_lock)
            {
                return new ValueTask<bool>(m_documents.Remove(resourceKey));
            }
        }

        private static void ValidateKey(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }
        }

        private static void ValidateRange(long offset, int count)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, List<byte>> m_documents = [];
    }
}
