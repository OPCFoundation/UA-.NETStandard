// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua
{
    using System.Collections.Generic;

    /// <summary>
    /// Node id comparer
    /// </summary>
    internal class Comparers : IEqualityComparer<ExpandedNodeId>,
        IEqualityComparer<NodeId>
    {
        /// <summary>
        /// Get singleton comparer
        /// </summary>
        public static Comparers Instance { get; } = new Comparers();

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId? x, ExpandedNodeId? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            return x == y;
        }

        /// <inheritdoc/>
        public int GetHashCode(ExpandedNodeId obj)
        {
            return obj.GetHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(NodeId? x, NodeId? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            return x == y;
        }

        /// <inheritdoc/>
        public int GetHashCode(NodeId obj)
        {
            if (obj is null)
            {
                return 0;
            }
            return obj.GetHashCode();
        }
    }
}
