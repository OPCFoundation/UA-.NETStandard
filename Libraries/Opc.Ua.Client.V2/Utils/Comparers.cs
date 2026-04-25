// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Collections.Generic;

namespace Opc.Ua
{
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
        public bool Equals(ExpandedNodeId x, ExpandedNodeId y)
        {
            return x == y;
        }

        /// <inheritdoc/>
        public int GetHashCode(ExpandedNodeId obj)
        {
            return obj.GetHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(NodeId x, NodeId y)
        {
            return x == y;
        }

        /// <inheritdoc/>
        public int GetHashCode(NodeId obj)
        {
            return obj.GetHashCode();
        }
    }
}