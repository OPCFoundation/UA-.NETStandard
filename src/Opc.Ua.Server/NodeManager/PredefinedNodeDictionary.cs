/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A concurrent node index with reference-identity conditional mutations.
    /// </summary>
    /// <remarks>
    /// Every write creates a distinct entry, so removing or updating a captured node cannot
    /// remove a different node that has since been stored under the same NodeId, even when
    /// the two nodes compare equal.
    /// </remarks>
    public sealed class PredefinedNodeDictionary :
        IDictionary<NodeId, NodeState>,
        IReadOnlyDictionary<NodeId, NodeState>
    {
        /// <summary>
        /// Gets or replaces the node indexed by the specified NodeId.
        /// </summary>
        // NodeId is the existing dictionary key contract, not an ordinal position.
        // TODO: Remove this exception when CA1043 supports NodeId-keyed dictionaries.
        [SuppressMessage("Design", "CA1043:Use Integral Or String Argument For Indexers",
            Justification = "The index implements the existing NodeId-keyed dictionary contract.")]
        public NodeState this[NodeId key]
        {
            get => m_entries[key].Node;
            set => m_entries[key] = new Entry(value);
        }

        /// <summary>
        /// Gets a snapshot of the indexed NodeIds.
        /// </summary>
        public ICollection<NodeId> Keys => m_entries.Keys;

        /// <summary>
        /// Gets a snapshot of the indexed nodes.
        /// </summary>
        public ICollection<NodeState> Values
        {
            get
            {
                KeyValuePair<NodeId, Entry>[] entries = m_entries.ToArray();
                var nodes = new NodeState[entries.Length];
                for (int ii = 0; ii < entries.Length; ii++)
                {
                    nodes[ii] = entries[ii].Value.Node;
                }
                return nodes;
            }
        }

        /// <summary>
        /// Gets the number of indexed nodes.
        /// </summary>
        public int Count => m_entries.Count;

        /// <summary>
        /// Gets whether the index contains no nodes.
        /// </summary>
        public bool IsEmpty => m_entries.IsEmpty;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        IEnumerable<NodeId> IReadOnlyDictionary<NodeId, NodeState>.Keys => Keys;

        /// <inheritdoc/>
        IEnumerable<NodeState> IReadOnlyDictionary<NodeId, NodeState>.Values => Values;

        /// <summary>
        /// Adds a node, rejecting an existing NodeId.
        /// </summary>
        /// <exception cref="ArgumentException">The supplied key is already indexed.</exception>
        public void Add(NodeId key, NodeState value)
        {
            if (!TryAdd(key, value))
            {
                throw new ArgumentException("An element with the same key already exists.");
            }
        }

        /// <summary>
        /// Adds a node if its NodeId is not indexed.
        /// </summary>
        public bool TryAdd(NodeId key, NodeState value)
        {
            return m_entries.TryAdd(key, new Entry(value));
        }

        /// <summary>
        /// Adds a node or replaces the current entry with the value returned by the update callback.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="updateValueFactory"/> is null.</exception>
        public NodeState AddOrUpdate(
            NodeId key,
            NodeState addValue,
            Func<NodeId, NodeState, NodeState> updateValueFactory)
        {
            if (updateValueFactory == null)
            {
                throw new ArgumentNullException(nameof(updateValueFactory));
            }
            return m_entries.AddOrUpdate(
                key,
                new Entry(addValue),
                (id, current) => new Entry(updateValueFactory(id, current.Node))).Node;
        }

        /// <summary>
        /// Adds a node if absent and returns the node indexed by the NodeId.
        /// </summary>
        public NodeState GetOrAdd(NodeId key, NodeState value)
        {
            return m_entries.GetOrAdd(key, new Entry(value)).Node;
        }

        /// <summary>
        /// Adds a node produced by the callback if absent and returns the indexed node.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="valueFactory"/> is null.</exception>
        public NodeState GetOrAdd(NodeId key, Func<NodeId, NodeState> valueFactory)
        {
            if (valueFactory == null)
            {
                throw new ArgumentNullException(nameof(valueFactory));
            }
            return m_entries.GetOrAdd(key, id => new Entry(valueFactory(id))).Node;
        }

        /// <summary>
        /// Replaces an entry only if it still contains the exact comparison node.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="comparisonValue"/> is null.</exception>
        public bool TryUpdate(NodeId key, NodeState value, NodeState comparisonValue)
        {
            if (comparisonValue == null)
            {
                throw new ArgumentNullException(nameof(comparisonValue));
            }
            var replacement = new Entry(value);
            while (m_entries.TryGetValue(key, out Entry? current))
            {
                if (!ReferenceEquals(current.Node, comparisonValue))
                {
                    return false;
                }
                if (m_entries.TryUpdate(key, replacement, current))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks whether a NodeId is indexed.
        /// </summary>
        public bool ContainsKey(NodeId key)
        {
            return m_entries.ContainsKey(key);
        }

        /// <summary>
        /// Gets the node currently indexed by a NodeId.
        /// </summary>
        public bool TryGetValue(NodeId key, [NotNullWhen(true)] out NodeState? value)
        {
            if (m_entries.TryGetValue(key, out Entry? entry))
            {
                value = entry.Node;
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Removes and returns the current entry for a NodeId.
        /// </summary>
        public bool TryRemove(NodeId key, [NotNullWhen(true)] out NodeState? value)
        {
            if (m_entries.TryRemove(key, out Entry? entry))
            {
                value = entry.Node;
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Removes an entry only if it still contains the exact supplied node.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
        public bool TryRemove(NodeId key, NodeState value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            while (m_entries.TryGetValue(key, out Entry? entry))
            {
                if (!ReferenceEquals(entry.Node, value))
                {
                    return false;
                }
                if (((ICollection<KeyValuePair<NodeId, Entry>>)m_entries).Remove(
                    new KeyValuePair<NodeId, Entry>(key, entry)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes the current entry for a NodeId.
        /// </summary>
        public bool Remove(NodeId key)
        {
            return m_entries.TryRemove(key, out _);
        }

        /// <summary>
        /// Removes all entries.
        /// </summary>
        public void Clear()
        {
            m_entries.Clear();
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<NodeId, NodeState> item)
        {
            Add(item.Key, item.Value);
        }

        /// <inheritdoc/>
        public bool Contains(KeyValuePair<NodeId, NodeState> item)
        {
            return TryGetValue(item.Key, out NodeState? current) && ReferenceEquals(current, item.Value);
        }

        /// <inheritdoc/>
        public bool Remove(KeyValuePair<NodeId, NodeState> item)
        {
            return TryRemove(item.Key, item.Value);
        }

        /// <inheritdoc/>
        public void CopyTo(KeyValuePair<NodeId, NodeState>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            KeyValuePair<NodeId, NodeState>[] snapshot = CreateSnapshot();
            Array.Copy(snapshot, 0, array, arrayIndex, snapshot.Length);
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<NodeId, NodeState>> GetEnumerator()
        {
            foreach (KeyValuePair<NodeId, Entry> entry in m_entries)
            {
                yield return new KeyValuePair<NodeId, NodeState>(entry.Key, entry.Value.Node);
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private KeyValuePair<NodeId, NodeState>[] CreateSnapshot()
        {
            KeyValuePair<NodeId, Entry>[] entries = m_entries.ToArray();
            var result = new KeyValuePair<NodeId, NodeState>[entries.Length];
            for (int ii = 0; ii < entries.Length; ii++)
            {
                result[ii] = new KeyValuePair<NodeId, NodeState>(entries[ii].Key, entries[ii].Value.Node);
            }
            return result;
        }

        private readonly ConcurrentDictionary<NodeId, Entry> m_entries = new(new NodeIdComparer());

        /// <summary>
        /// Gives each published value its own identity independent of NodeState equality.
        /// </summary>
        private sealed class Entry
        {
            /// <summary>
            /// Creates an entry for a non-null node.
            /// </summary>
            public Entry(NodeState node)
            {
                Node = node ?? throw new ArgumentNullException(nameof(node));
            }

            /// <summary>
            /// Gets the node stored in this entry.
            /// </summary>
            public NodeState Node { get; }
        }
    }
}
