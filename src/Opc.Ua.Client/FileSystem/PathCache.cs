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
using System.Collections.Generic;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Bounded LRU cache of resolved
    /// <c>(parent NodeId, browse name) → child NodeId</c> entries used
    /// by the <c>FileSystemClient</c> to avoid repeated
    /// <c>TranslateBrowsePathsToNodeIds</c> round-trips for the same
    /// path. Not thread-safe — callers must synchronise access.
    /// </summary>
    /// <remarks>
    /// A capacity of zero disables caching. The cache is best-effort:
    /// callers must be prepared for a stale entry (the next
    /// translate call will fail with <c>BadNodeIdUnknown</c> /
    /// <c>BadNoMatch</c>, and the entry must be evicted before
    /// retrying).
    /// </remarks>
    internal sealed class PathCache
    {
        public PathCache(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            m_capacity = capacity;
            m_map = capacity == 0
                ? null
                : new Dictionary<Key, LinkedListNode<Entry>>(capacity);
            m_lru = capacity == 0 ? null : new LinkedList<Entry>();
        }

        /// <summary>
        /// Returns the cached child <see cref="NodeId"/> for
        /// <paramref name="parent"/> + <paramref name="name"/>, or
        /// <c>null</c> when the entry is absent or caching is disabled.
        /// </summary>
        public NodeId? TryGet(NodeId parent, QualifiedName name)
        {
            if (m_capacity == 0 ||
                m_map == null ||
                m_lru == null)
            {
                return null;
            }
            var key = new Key(parent, name);
            if (!m_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                return null;
            }
            // Move to front (MRU position).
            m_lru.Remove(node);
            m_lru.AddFirst(node);
            return node.Value.Child;
        }

        /// <summary>
        /// Inserts (or replaces) a cache entry for
        /// <paramref name="parent"/> + <paramref name="name"/> →
        /// <paramref name="child"/>. Evicts the least recently used
        /// entry when the capacity is exceeded.
        /// </summary>
        public void Put(NodeId parent, QualifiedName name, NodeId child)
        {
            if (m_capacity == 0 ||
                m_map == null ||
                m_lru == null)
            {
                return;
            }

            var key = new Key(parent, name);
            if (m_map.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                m_lru.Remove(existing);
                existing.Value = new Entry(key, child);
                m_lru.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, child));
            m_lru.AddFirst(node);
            m_map[key] = node;

            if (m_map.Count > m_capacity)
            {
                LinkedListNode<Entry>? lru = m_lru.Last;
                if (lru != null)
                {
                    m_lru.RemoveLast();
                    m_map.Remove(lru.Value.Key);
                }
            }
        }

        /// <summary>
        /// Removes the entry for <paramref name="parent"/> +
        /// <paramref name="name"/> if present.
        /// </summary>
        public void Invalidate(NodeId parent, QualifiedName name)
        {
            if (m_capacity == 0 ||
                m_map == null ||
                m_lru == null)
            {
                return;
            }
            var key = new Key(parent, name);
            if (m_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                m_lru.Remove(node);
                m_map.Remove(key);
            }
        }

        /// <summary>
        /// Removes every entry whose parent equals
        /// <paramref name="parent"/>. Used after a directory mutation to
        /// avoid serving stale child NodeIds.
        /// </summary>
        public void InvalidateChildrenOf(NodeId parent)
        {
            if (m_capacity == 0 ||
                m_map == null ||
                m_lru == null)
            {
                return;
            }
            // Collect first; mutating during iteration is unsafe.
            List<Key>? toRemove = null;
            foreach (Key key in m_map.Keys)
            {
                if (key.Parent.Equals(parent))
                {
                    (toRemove ??= []).Add(key);
                }
            }
            if (toRemove == null)
            {
                return;
            }
            foreach (Key key in toRemove)
            {
                if (m_map.TryGetValue(key, out LinkedListNode<Entry>? node))
                {
                    m_lru.Remove(node);
                    m_map.Remove(key);
                }
            }
        }

        /// <summary>
        /// Clears every entry.
        /// </summary>
        public void Clear()
        {
            m_map?.Clear();
            m_lru?.Clear();
        }

        /// <summary>
        /// Number of entries currently cached.
        /// </summary>
        public int Count => m_map?.Count ?? 0;

        private readonly int m_capacity;
        private readonly Dictionary<Key, LinkedListNode<Entry>>? m_map;
        private readonly LinkedList<Entry>? m_lru;

        private readonly struct Key : IEquatable<Key>
        {
            public Key(NodeId parent, QualifiedName name)
            {
                Parent = parent;
                Name = name;
            }

            public NodeId Parent { get; }
            public QualifiedName Name { get; }

            public bool Equals(Key other)
            {
                return Parent.Equals(other.Parent) && Name.Equals(other.Name);
            }

            public override bool Equals(object? obj)
            {
                return obj is Key k && Equals(k);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Parent, Name);
            }
        }

        private readonly struct Entry
        {
            public Entry(Key key, NodeId child)
            {
                Key = key;
                Child = child;
            }

            public Key Key { get; }
            public NodeId Child { get; }
        }
    }
}
