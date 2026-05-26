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

// define USE_LEGACY_IMPLEMENTATION to use the original implementation
// #define USE_LEGACY_IMPLEMENTATION

// benchmarks revealed that the use of a standard Dictionary<NodeId> class
// with efficent hash code implementations is up to 10xfaster than
// the original implementation using multiple SortedDictionary instances

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// A dictionary designed to provide efficient lookups for objects identified
    /// by a NodeId. When enumerating the dictionary the items are not returned
    /// in any particular order (i.e the order the data was addeded). If order
    /// is required e.g. during comparison against expected content during testing,
    /// create a copy and sort on key.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class NodeIdDictionary<T> : ConcurrentDictionary<NodeId, T>
    {
        private static readonly NodeIdComparer s_comparer = new();

        /// <summary>
        /// Creates an empty dictionary.
        /// </summary>
        public NodeIdDictionary()
            : base(s_comparer)
        {
        }

        /// <summary>
        /// Creates an empty dictionary with capacity.
        /// </summary>
        public NodeIdDictionary(int capacity)
            : base(Environment.ProcessorCount, capacity, s_comparer)
        {
        }

        // helpers for the legacy implementation

        /// <inheritdoc cref="IDictionary.Add"/>
        public void Add(NodeId key, T value)
        {
            if (!TryAdd(key, value))
            {
                throw new ArgumentException("An element with the same key already exists.");
            }
        }

        /// <inheritdoc cref="IDictionary.Remove"/>
        public void Remove(NodeId key)
        {
            TryRemove(key, out _);
        }

        /// <summary>
        /// remove a entry from the dictionary only if it has the provided value
        /// https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
        /// </summary>
        /// <param name="key">the key of the entry to remove</param>
        /// <param name="value">the value of the entry to remove</param>
        /// <returns>true if removed, false if not removed</returns>
        public bool TryRemove(NodeId key, T value)
        {
            return ((ICollection<KeyValuePair<NodeId, T>>)this).Remove(
                new KeyValuePair<NodeId, T>(key, value));
        }
    }
}
