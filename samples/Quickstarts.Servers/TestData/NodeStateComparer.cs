// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Opc.Ua;

namespace TestData
{
    /// <summary>
    /// Helper which implements IEqualityComparer for Linq queries.
    /// </summary>
    public class NodeStateComparer : IEqualityComparer<NodeState>
    {
        /// <inheritdoc/>
        public bool Equals(NodeState? x, NodeState? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.NodeId == y.NodeId;
        }

        /// <inheritdoc/>
        public int GetHashCode(NodeState obj)
        {
            if (obj is null)
            {
                return 0;
            }

            return obj.NodeId.GetHashCode();
        }
    }
}
