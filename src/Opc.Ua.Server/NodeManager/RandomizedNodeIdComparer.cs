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
