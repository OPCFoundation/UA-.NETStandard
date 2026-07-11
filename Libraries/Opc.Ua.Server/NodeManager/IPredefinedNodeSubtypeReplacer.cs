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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Capability interface implemented by node managers that can replace an
    /// already registered predefined instance node with a differently-typed
    /// instance (for example a generated subtype) at runtime while preserving
    /// the node's identity in the address space.
    /// </summary>
    /// <remarks>
    /// Implemented by <see cref="AsyncCustomNodeManager"/>. Resolve it from a
    /// node manager (for example
    /// <c>server.DiagnosticsNodeManager as IPredefinedNodeSubtypeReplacer</c>)
    /// to materialize the correct generated subtype of a well-known instance
    /// node whose concrete type depends on runtime configuration.
    /// </remarks>
    public interface IPredefinedNodeSubtypeReplacer
    {
        /// <summary>
        /// Replaces an already registered predefined instance node with a
        /// differently-typed instance while preserving the node's identity,
        /// well-known child NodeIds and values, and emitting a ModelChange for
        /// live clients.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The caller creates <paramref name="newInstance"/> via the generated
        /// <c>CreateInstanceOf&lt;Type&gt;</c> factory; this method swaps it into
        /// the live address space. The replacement inherits the existing node's
        /// <see cref="NodeState.NodeId"/>, <see cref="NodeState.BrowseName"/>,
        /// <see cref="NodeState.SymbolicName"/>, <see cref="NodeState.DisplayName"/>
        /// and <see cref="BaseInstanceState.ReferenceTypeId"/>. Children shared by
        /// both instances (matched by <see cref="NodeState.BrowseName"/> at any
        /// depth) keep the existing child's NodeId and value; children that only
        /// exist on the replacement take their NodeId from
        /// <paramref name="newChildNodeIds"/> when supplied, otherwise a fresh
        /// NodeId is minted.
        /// </para>
        /// </remarks>
        /// <param name="context">The system context.</param>
        /// <param name="existingNode">The registered predefined instance to replace.</param>
        /// <param name="newInstance">The replacement instance (typically a subtype).</param>
        /// <param name="newChildNodeIds">
        /// Optional well-known NodeIds, keyed by BrowseName, for children that only
        /// exist on <paramref name="newInstance"/>.
        /// </param>
        /// <param name="onReplaced">
        /// Optional callback invoked with <paramref name="newInstance"/> after it is
        /// attached, allowing the caller to update a typed parent slot.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The registered replacement instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="existingNode"/> or <paramref name="newInstance"/> is null.
        /// </exception>
        ValueTask<BaseInstanceState> ReplacePredefinedInstanceSubtypeAsync(
            ISystemContext context,
            BaseInstanceState existingNode,
            BaseInstanceState newInstance,
            IReadOnlyDictionary<QualifiedName, NodeId>? newChildNodeIds = null,
            Action<BaseInstanceState>? onReplaced = null,
            CancellationToken cancellationToken = default);
    }
}
