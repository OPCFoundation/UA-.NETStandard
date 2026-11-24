// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.SourceGeneration.Dependency
{
    /// <summary>
    /// Coordinates decoding without retaining completed dependency graphs.
    /// </summary>
    internal sealed class DependencyDecodeCache
    {
        /// <summary>
        /// Creates a cache with optional content sharing for read-only generator consumers.
        /// </summary>
        internal DependencyDecodeCache(
            bool shareByContent,
            Func<string, CancellationToken, ModelDependencyV1?> decode)
        {
            m_shareByContent = shareByContent;
            m_decode = decode;
        }

        /// <summary>
        /// Gets a live decoded graph, or decodes once for concurrent callers.
        /// The cancellation token belongs only to this call, never to the cached holder.
        /// </summary>
        internal ModelDependencyV1? GetDependency(string payload, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }
            if (!m_identity.TryGetValue(payload, out Entry? entry))
            {
                entry = m_identity.GetValue(payload, CreateEntry);
            }
            return entry.GetDependency(payload, m_decode, cancellationToken);
        }

        /// <summary>
        /// Finds a shared holder before any expensive decoding starts.
        /// </summary>
        private Entry CreateEntry(string payload)
        {
            if (!m_shareByContent)
            {
                return new Entry();
            }
            lock (m_lock)
            {
                for (int index = m_content.Count - 1; index >= 0; index--)
                {
                    (WeakReference<string> key, Entry entry) = m_content[index];
                    if (!key.TryGetTarget(out string? candidate))
                    {
                        m_content.RemoveAt(index);
                    }
                    else if (string.Equals(candidate, payload, StringComparison.Ordinal))
                    {
                        // Keep the index useful when the older equal key is released.
                        m_content[index] = (new WeakReference<string>(payload), entry);
                        return entry;
                    }
                }
                if (m_content.Count == kContentCapacity)
                {
                    m_content.RemoveAt(0);
                }
                var created = new Entry();
                m_content.Add((new WeakReference<string>(payload), created));
                return created;
            }
        }

        /// <summary>
        /// Serializes cold decodes and keeps only a weak completed value.
        /// </summary>
        private sealed class Entry
        {
            /// <summary>
            /// Gets the completed value or retries a collected or cancelled decode.
            /// </summary>
            internal ModelDependencyV1? GetDependency(
                string payload,
                Func<string, CancellationToken, ModelDependencyV1?> decode,
                CancellationToken cancellationToken)
            {
                lock (m_lock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (m_isNull)
                    {
                        return null;
                    }
                    if (m_value != null && m_value.TryGetTarget(out ModelDependencyV1? value))
                    {
                        return value;
                    }
                    value = decode(payload, cancellationToken);
                    if (value == null)
                    {
                        m_isNull = true;
                    }
                    else
                    {
                        m_value = new WeakReference<ModelDependencyV1>(value);
                    }
                    return value;
                }
            }

            private readonly Lock m_lock = new();
            private WeakReference<ModelDependencyV1>? m_value;
            private bool m_isNull;
        }

        private const int kContentCapacity = 128;
        private readonly bool m_shareByContent;
        private readonly Func<string, CancellationToken, ModelDependencyV1?> m_decode;
        private readonly ConditionalWeakTable<string, Entry> m_identity =
#if NET8_0_OR_GREATER
            [];
#else
            new();
#endif
        private readonly List<(WeakReference<string> Key, Entry Value)> m_content = [];
        private readonly Lock m_lock = new();
    }
}
