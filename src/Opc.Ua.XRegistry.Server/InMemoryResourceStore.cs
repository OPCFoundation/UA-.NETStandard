// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    public sealed class InMemoryResourceStore : IXRegistryAtomicResourceStore
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
            ByteString data,
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
        /// <param name="resourceKey">The store key of the resource.</param>
        /// <param name="document">The complete replacement document.</param>
        /// <param name="ct">Cancels preparation before publication.</param>
        public ValueTask ReplaceAsync(
            string resourceKey,
            ByteString document,
            CancellationToken ct = default)
        {
            ValidateKey(resourceKey);
            ct.ThrowIfCancellationRequested();
            List<byte> replacement = [.. document.Span];
            lock (m_lock)
            {
                ct.ThrowIfCancellationRequested();
                m_documents[resourceKey] = replacement;
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
