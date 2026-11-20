// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
