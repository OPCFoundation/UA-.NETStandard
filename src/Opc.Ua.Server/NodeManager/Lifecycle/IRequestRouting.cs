/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Attaches a routing view to an admitted request without exposing the routing implementation.
    /// </summary>
    internal interface IRequestRouting
    {
        /// <summary>
        /// Borrows the operation's routing view until the returned scope is disposed.
        /// </summary>
        /// <param name="context">The operation selecting routes.</param>
        /// <returns>The scope releasing borrowed service ownership.</returns>
        OperationContext.RoutingScope CaptureRequest(OperationContext context);

        /// <summary>
        /// Pins the current routing view for request-lifetime consistency.
        /// Node-management writes defer pinning until their mutation admission is acquired.
        /// </summary>
        /// <param name="context">The admitted request.</param>
        void PinRequest(OperationContext context);

        /// <summary>
        /// Resolves a handle through the operation's explicitly selected view.
        /// </summary>
        /// <param name="context">The operation selecting routes.</param>
        /// <param name="nodeId">The node to resolve.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The handle and owning manager, if found.</returns>
        ValueTask<(object? handle, IAsyncNodeManager? nodeManager)> GetManagerHandleAsync(
            OperationContext context,
            NodeId nodeId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Finds an in-memory node through the operation's explicitly selected view.
        /// </summary>
        /// <param name="context">The operation selecting routes.</param>
        /// <param name="nodeId">The node to find.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The node, if found.</returns>
        ValueTask<NodeState?> FindNodeInAddressSpaceAsync(
            OperationContext context,
            NodeId nodeId,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resolves request-owned handles while preserving support for custom master implementations.
    /// </summary>
    internal static class RequestRoutingExtensions
    {
        /// <summary>
        /// Uses explicit request routes when available, or the custom master's live lookup otherwise.
        /// </summary>
        /// <param name="master">The master serving the operation.</param>
        /// <param name="context">The operation, or null for contextless internal work.</param>
        /// <param name="nodeId">The node to resolve.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The handle and owning manager, if found.</returns>
        internal static ValueTask<(object? handle, IAsyncNodeManager? nodeManager)> ResolveManagerHandleAsync(
            this IMasterNodeManager master,
            OperationContext? context,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            return context is not null && master is IRequestRouting routing
                ? routing.GetManagerHandleAsync(context, nodeId, cancellationToken)
                : master.GetManagerHandleAsync(nodeId, cancellationToken);
        }

        /// <summary>
        /// Finds an in-memory node using explicit request routes when available.
        /// </summary>
        /// <param name="master">The master serving the operation.</param>
        /// <param name="context">The operation, or null for contextless internal work.</param>
        /// <param name="nodeId">The node to find.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The node, if found.</returns>
        internal static ValueTask<NodeState?> ResolveNodeInAddressSpaceAsync(
            this IMasterNodeManager master,
            OperationContext? context,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            return context is not null && master is IRequestRouting routing
                ? routing.FindNodeInAddressSpaceAsync(context, nodeId, cancellationToken)
                : master.FindNodeInAddressSpaceAsync(nodeId, cancellationToken);
        }
    }
}
