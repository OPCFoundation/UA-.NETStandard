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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Abstraction over the in-process node graph that an
    /// <see cref="IAddressSpaceSynchronizer"/> reads from (outbound capture)
    /// and writes to (inbound apply). A node manager adapts its
    /// <c>PredefinedNodes</c> dictionary to this interface; tests use a
    /// dictionary-backed implementation.
    /// </summary>
    public interface ILocalAddressSpace
    {
        /// <summary>
        /// The system context used to (de)serialize nodes.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// A snapshot of the current top-level nodes.
        /// </summary>
        IEnumerable<NodeState> Nodes { get; }

        /// <summary>
        /// Raised after a node is added to the address space.
        /// </summary>
        event Action<NodeState>? NodeAdded;

        /// <summary>
        /// Raised after a node is removed from the address space.
        /// </summary>
        event Action<NodeId>? NodeRemoved;

        /// <summary>
        /// Looks up a node by identifier.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="node">The resolved node when found.</param>
        /// <returns><c>true</c> when the node exists.</returns>
        bool TryGetNode(NodeId nodeId, [NotNullWhen(true)] out NodeState? node);

        /// <summary>
        /// Adds or replaces a node, raising <see cref="NodeAdded"/>.
        /// </summary>
        /// <param name="node">The node to add or replace.</param>
        void AddOrUpdateNode(NodeState node);

        /// <summary>
        /// Removes a node, raising <see cref="NodeRemoved"/> when present.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns><c>true</c> when a node was removed.</returns>
        bool RemoveNode(NodeId nodeId);
    }
}
