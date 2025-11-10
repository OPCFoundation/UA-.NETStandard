/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
