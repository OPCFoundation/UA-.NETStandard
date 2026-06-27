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

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: server-wide registry of <see cref="INodeStateStore"/> instances,
    /// resolved per node with the same three-scope precedence as the
    /// historian registry: exact NodeId, then namespace, then a single
    /// default fallback.
    /// </summary>
    public interface INodeStateStoreRegistry
    {
        /// <summary>
        /// Adds a NodeId-scoped binding.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="store">The store to bind.</param>
        void RegisterForNode(NodeId nodeId, INodeStateStore store);

        /// <summary>
        /// Adds a namespace-scoped binding.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="store">The store to bind.</param>
        void RegisterForNamespace(string namespaceUri, INodeStateStore store);

        /// <summary>
        /// Sets the default fallback store.
        /// </summary>
        /// <param name="store">The store to use as fallback.</param>
        void RegisterDefault(INodeStateStore store);

        /// <summary>
        /// Removes the NodeId-scoped binding (no-op when absent).
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns><c>true</c> when a binding was removed.</returns>
        bool UnregisterForNode(NodeId nodeId);

        /// <summary>
        /// Removes the namespace-scoped binding (no-op when absent).
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <returns><c>true</c> when a binding was removed.</returns>
        bool UnregisterForNamespace(string namespaceUri);

        /// <summary>
        /// Clears the default store.
        /// </summary>
        void ClearDefault();

        /// <summary>
        /// Resolves the store for a node, or <c>null</c> when no binding
        /// matches.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        INodeStateStore? Resolve(NodeId nodeId);

        /// <summary>
        /// Returns a snapshot of every registered store.
        /// </summary>
        IReadOnlyCollection<INodeStateStore> Stores { get; }
    }
}
