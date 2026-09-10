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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Nodes
{
    /// <summary>
    /// Creates one behavior lease for a node the registration matched.
    /// </summary>
    /// <remarks>
    /// The engine is deliberately host-neutral: it knows nothing about how the node
    /// manager obtained its nodes. Everything a behavior needs arrives through
    /// <see cref="NodeBehaviorContext"/>.
    /// </remarks>
    internal interface INodeBehaviorFactory
    {
        /// <summary>
        /// Gets the namespace-stable target type definition, or a null id for a
        /// registration that does not match on type.
        /// </summary>
        ExpandedNodeId TypeDefinitionId { get; }

        /// <summary>
        /// Creates a behavior lease without activating it.
        /// </summary>
        /// <remarks>
        /// Returning <c>null</c> declines the node: no lease is recorded and nothing is
        /// unwound for it later. Use it when a factory matches a type but a particular
        /// instance carries no work.
        /// </remarks>
        ValueTask<INodeBehaviorLease?> CreateAsync(
            NodeBehaviorContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Owns the activated behavior attached to one node and factory.
    /// </summary>
    internal interface INodeBehaviorLease : IAsyncDisposable
    {
        /// <summary>
        /// Activates the behavior.
        /// </summary>
        /// <remarks>
        /// The cancellation token is valid only for this call and must not be captured
        /// by background work; a behavior that starts a loop takes its shutdown signal
        /// from whatever the host wired into the lease instead.
        /// </remarks>
        ValueTask ActivateAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Deactivates the behavior.
        /// </summary>
        /// <remarks>
        /// Cleanup uses a non-cancelable token, so implementations must complete
        /// promptly. <see cref="IAsyncDisposable.DisposeAsync"/> must release every
        /// owned resource even when activation failed before this method could run.
        /// </remarks>
        ValueTask DeactivateAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// One behavior registration bound to a node the caller already resolved.
    /// </summary>
    /// <remarks>
    /// Pinned registrations bypass type matching. They share the activation order and
    /// the reverse unwind with type-keyed registrations.
    /// </remarks>
    internal readonly record struct NodeBehaviorPinnedRegistration(
        NodeState Node,
        INodeBehaviorFactory Factory);

    /// <summary>
    /// Resolves nodes owned by the fully indexed node manager.
    /// </summary>
    internal sealed class NodeBehaviorAddressSpace
    {
        /// <summary>
        /// Initializes a resolver for one indexed graph.
        /// </summary>
        public NodeBehaviorAddressSpace(
            NamespaceTable namespaceUris,
            Func<NodeId, NodeState?> find)
        {
            m_namespaceUris = namespaceUris ??
                throw new ArgumentNullException(nameof(namespaceUris));
            m_find = find ?? throw new ArgumentNullException(nameof(find));
        }

        /// <summary>
        /// Finds a node by its final NodeId.
        /// </summary>
        public NodeState? Find(NodeId nodeId)
        {
            return nodeId.IsNull ? null : m_find(nodeId);
        }

        /// <summary>
        /// Finds a node after resolving a namespace-stable ExpandedNodeId.
        /// </summary>
        public NodeState? Find(ExpandedNodeId nodeId)
        {
            NodeId finalNodeId = ExpandedNodeId.ToNodeId(nodeId, m_namespaceUris);
            return finalNodeId.IsNull ? null : m_find(finalNodeId);
        }

        private readonly NamespaceTable m_namespaceUris;
        private readonly Func<NodeId, NodeState?> m_find;
    }

    /// <summary>
    /// Read-only inputs shared with behavior factories.
    /// </summary>
    internal sealed class NodeBehaviorContext
    {
        /// <summary>
        /// Initializes a context for one node, or for the node manager itself.
        /// </summary>
        public NodeBehaviorContext(
            NodeState? node,
            ISystemContext systemContext,
            NodeBehaviorAddressSpace addressSpace,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            Node = node;
            SystemContext = systemContext ??
                throw new ArgumentNullException(nameof(systemContext));
            AddressSpace = addressSpace ??
                throw new ArgumentNullException(nameof(addressSpace));
            Telemetry = telemetry ??
                throw new ArgumentNullException(nameof(telemetry));
            TimeProvider = timeProvider ??
                throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <summary>
        /// Gets the node receiving the behavior, or <c>null</c> for a manager-scoped
        /// registration that owns no node.
        /// </summary>
        public NodeState? Node { get; }

        /// <summary>
        /// Gets the node manager's system context.
        /// </summary>
        public ISystemContext SystemContext { get; }

        /// <summary>
        /// Gets the resolver for the fully indexed graph.
        /// </summary>
        public NodeBehaviorAddressSpace AddressSpace { get; }

        /// <summary>
        /// Gets the server telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the server time provider.
        /// </summary>
        public TimeProvider TimeProvider { get; }
    }
}
