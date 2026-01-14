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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The standard implementation of a UA server.
    /// </summary>
    public interface IStandardServer: ISessionServer
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
