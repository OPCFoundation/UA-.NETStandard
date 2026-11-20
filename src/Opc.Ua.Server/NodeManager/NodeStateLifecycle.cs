// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Completes the compatibility create lifecycle at the registration seam.
    /// </summary>
    internal static class NodeStateLifecycle
    {
        /// <summary>
        /// Completes the lifecycle if needed and reports whether it ran.
        /// </summary>
        public static bool CompleteForRegistration(
            ISystemContext context,
            NodeState node,
            ILogger? logger,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (node.IsCreated)
            {
                return false;
            }

            node.CreateAsPredefinedNode(context, ct);
            logger?.PredefinedNodeLifecycleCompletedAtRegistration(
                node.NodeId,
                node.BrowseName);
            return true;
        }
    }
}
