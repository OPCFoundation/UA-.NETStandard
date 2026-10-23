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
        /// Pins the current routing view for the request's lifetime.
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
