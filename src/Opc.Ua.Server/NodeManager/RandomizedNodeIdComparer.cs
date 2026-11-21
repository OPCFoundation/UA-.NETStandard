// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Compares NodeIds for dictionaries that are filled with NodeIds chosen by a client,
    /// such as the per-request permission attribute cache.
    /// </summary>
    /// <remarks>
    /// A numeric NodeId hashes to its identifier, so a client can choose distinct ids that
    /// are multiples of the dictionary's bucket count and put every key in one bucket chain,
    /// which makes filling the dictionary quadratic. The hash is mixed with the per-process
    /// random seed of <see cref="HashCode"/>, which keeps distinct hash codes distinct but
    /// makes their buckets unpredictable.
    /// </remarks>
    internal sealed class RandomizedNodeIdComparer : IEqualityComparer<NodeId>
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static readonly RandomizedNodeIdComparer Instance = new();

        private RandomizedNodeIdComparer()
        {
        }

        /// <inheritdoc/>
        public bool Equals(NodeId x, NodeId y)
        {
            return x.Equals(y);
        }

        /// <inheritdoc/>
        public int GetHashCode(NodeId obj)
        {
            // NodeId.GetHashCode folds the namespace into the identifier hash
            // ((identifier) ^ (namespace << 16)), so ids in different
            // namespaces can share one hash; mixing the namespace in on its
            // own keeps them apart.
            return HashCode.Combine(obj.NamespaceIndex, obj.GetHashCode());
        }
    }
}
