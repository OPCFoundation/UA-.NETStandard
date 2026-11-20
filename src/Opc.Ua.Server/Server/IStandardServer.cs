/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The standard implementation of a UA server.
    /// </summary>
    public interface IStandardServer : ISessionServer
    {
        /// <summary>
        /// The async node manager factories that are used on startup of the server.
        /// </summary>
        IEnumerable<IAsyncNodeManagerFactory> AsyncNodeManagerFactories { get; }

        /// <summary>
        /// The state object associated with the server.
        /// It provides the shared components for the Server.
        /// </summary>
        /// <value>The current instance.</value>
        /// <exception cref="ServiceResultException"></exception>
        IServerInternal CurrentInstance { get; }

        /// <summary>
        /// The current state of the Server
        /// </summary>
        ServerState CurrentState { get; }

        /// <summary>
        /// The node manager factories that are used on startup of the server.
        /// </summary>
        IEnumerable<INodeManagerFactory> NodeManagerFactories { get; }

        /// <summary>
        /// Add a node manager factory which is used on server start
        /// to instantiate the node manager in the server.
        /// </summary>
        /// <param name="nodeManagerFactory">The node manager factory used to create the NodeManager.</param>
        void AddNodeManager(IAsyncNodeManagerFactory nodeManagerFactory);

        /// <summary>
        /// Add a node manager factory which is used on server start
        /// to instantiate the node manager in the server.
        /// </summary>
        /// <param name="nodeManagerFactory">The node manager factory used to create the NodeManager.</param>
        void AddNodeManager(INodeManagerFactory nodeManagerFactory);

        /// <summary>
        /// Registers the server with the discovery server.
        /// </summary>
        /// <returns>Boolean value.</returns>
        ValueTask<bool> RegisterWithDiscoveryServerAsync(CancellationToken ct = default);

        /// <summary>
        /// Remove a node manager factory from the list of node managers.
        /// Does not remove a NodeManager from a running server,
        /// only removes the factory before the server starts.
        /// </summary>
        /// <param name="nodeManagerFactory">The node manager factory to remove.</param>
        void RemoveNodeManager(IAsyncNodeManagerFactory nodeManagerFactory);

        /// <summary>
        /// Remove a node manager factory from the list of node managers.
        /// Does not remove a NodeManager from a running server,
        /// only removes the factory before the server starts.
        /// </summary>
        /// <param name="nodeManagerFactory">The node manager factory to remove.</param>
        void RemoveNodeManager(INodeManagerFactory nodeManagerFactory);
    }
}
