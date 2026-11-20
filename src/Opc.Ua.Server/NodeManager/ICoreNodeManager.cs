// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The default node manager for the server.
    /// </summary>
    /// <remarks>
    /// Every Server has one instance of this NodeManager.
    /// It stores objects that implement ILocalNode and indexes them by NodeId.
    /// </remarks>
    public interface ICoreNodeManager : IAsyncNodeManager
    {
        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        ValueTask ImportNodesAsync(
            ISystemContext context,
            IEnumerable<NodeState> predefinedNodes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        ValueTask ImportNodesAsync(
            ISystemContext context,
            IEnumerable<NodeState> predefinedNodes,
            bool isInternal,
            CancellationToken cancellationToken = default);
    }
}
