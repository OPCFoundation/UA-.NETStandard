/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
    /// Preserves server-selected identifier policy through factory replacement and node registration.
    /// </summary>
    public interface INodeIdFactoryPolicy
    {
        /// <summary>
        /// Applies the server policy to a namespace-rebased replacement factory.
        /// </summary>
        IRebasableNodeIdFactory Apply(IRebasableNodeIdFactory factory);

        /// <summary>
        /// Validates a node's assigned identity before it enters the node manager's index.
        /// </summary>
        /// <param name="context">The node manager's system context.</param>
        /// <param name="node">The node and its assigned descendant identities.</param>
        /// <param name="isServerInfrastructure">
        /// The registering manager owns built-in core, diagnostics or configuration infrastructure.
        /// Application nodes in shared namespaces must still be validated.
        /// </param>
        void ValidateRegistration(ISystemContext context, NodeState node, bool isServerInfrastructure = false);

        /// <summary>
        /// Rebinds identity-dependent state after a runtime node-manager composition change.
        /// </summary>
        /// <param name="server">The server exposing the committed manager composition.</param>
        /// <param name="cancellationToken">Cancels the awaited rebind.</param>
        ValueTask OnNodeManagersChangedAsync(IServerContext server, CancellationToken cancellationToken = default);

        /// <summary>
        /// Hydrates assigned identities in a prepared manager before its client routes are published.
        /// </summary>
        ValueTask PrepareNodeManagerAsync(
            IServerContext server,
            IAsyncNodeManager nodeManager,
            IAsyncNodeManager? replacedNodeManager,
            CancellationToken cancellationToken = default);
    }
}
