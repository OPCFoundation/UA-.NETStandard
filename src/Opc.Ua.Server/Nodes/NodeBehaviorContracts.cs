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
    /// Internal opt-in seam for node sources that supply behavior factories.
    /// </summary>
    internal interface INodeBehaviorFactoryProvider
    {
        /// <summary>
        /// Gets the behavior factories for one source generation.
        /// </summary>
        ArrayOf<INodeBehaviorFactory> GetNodeBehaviorFactories();
    }

    /// <summary>
    /// Creates one behavior lease for an instance of the target type definition.
    /// </summary>
    internal interface INodeBehaviorFactory
    {
        /// <summary>
        /// Gets the namespace-stable target type definition.
        /// </summary>
        ExpandedNodeId TypeDefinitionId { get; }

        /// <summary>
        /// Creates a behavior lease without activating it.
        /// </summary>
        ValueTask<INodeBehaviorLease> CreateAsync(
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
        /// Reload preparation overlaps the active previous generation. Implementations
        /// which own exclusive resources must scope them with
        /// <see cref="NodeBehaviorContext.Generation"/> or otherwise coordinate the
        /// overlap. The cancellation token is valid only for this call and must not be
        /// captured by background work.
        /// </remarks>
        ValueTask ActivateAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Deactivates the behavior.
        /// </summary>
        /// <remarks>
        /// Cleanup uses a non-cancelable token after the generation becomes invisible.
        /// Implementations must complete promptly. <see cref="IAsyncDisposable.DisposeAsync"/>
        /// must release every owned resource even when activation failed before this
        /// method could be called.
        /// </remarks>
        ValueTask DeactivateAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resolves final nodes owned by the fully indexed prepared graph.
    /// </summary>
    internal sealed class NodeBehaviorAddressSpace
    {
        /// <summary>
        /// Initializes a resolver for one prepared graph.
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
    /// Identifies one behavior-bearing source generation.
    /// </summary>
    internal sealed record NodeBehaviorGenerationIdentity
    {
        /// <summary>
        /// Initializes an identity.
        /// </summary>
        public NodeBehaviorGenerationIdentity(Guid sourceId, long generation)
        {
            if (sourceId == Guid.Empty)
            {
                throw new ArgumentException(
                    "The source identity cannot be empty.",
                    nameof(sourceId));
            }
            if (generation <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generation),
                    "The generation must be positive.");
            }

            SourceId = sourceId;
            Generation = generation;
        }

        /// <summary>
        /// Gets the stable identity shared by source generations.
        /// </summary>
        /// <remarks>
        /// The identity is process-scoped. A failed reload attempt may reuse the same
        /// generation ordinal because it never became the live generation.
        /// </remarks>
        public Guid SourceId { get; }

        /// <summary>
        /// Gets the generation ordinal.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Creates the next generation identity.
        /// </summary>
        public NodeBehaviorGenerationIdentity Next()
        {
            return new NodeBehaviorGenerationIdentity(
                SourceId,
                checked(Generation + 1));
        }

        /// <summary>
        /// Creates an initial source generation identity.
        /// </summary>
        public static NodeBehaviorGenerationIdentity CreateInitial()
        {
            return new NodeBehaviorGenerationIdentity(Guid.NewGuid(), 1);
        }
    }

    /// <summary>
    /// Read-only inputs shared with behavior factories.
    /// </summary>
    internal sealed class NodeBehaviorContext
    {
        /// <summary>
        /// Initializes a context for one final node.
        /// </summary>
        public NodeBehaviorContext(
            NodeState node,
            ISystemContext systemContext,
            NodeBehaviorAddressSpace addressSpace,
            IServiceProvider? services,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            INodeSource source,
            NodeBehaviorGenerationIdentity generation)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            SystemContext = systemContext ??
                throw new ArgumentNullException(nameof(systemContext));
            AddressSpace = addressSpace ??
                throw new ArgumentNullException(nameof(addressSpace));
            Services = services;
            Telemetry = telemetry ??
                throw new ArgumentNullException(nameof(telemetry));
            TimeProvider = timeProvider ??
                throw new ArgumentNullException(nameof(timeProvider));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Generation = generation ??
                throw new ArgumentNullException(nameof(generation));
        }

        /// <summary>
        /// Gets the final node receiving the behavior.
        /// </summary>
        public NodeState Node { get; }

        /// <summary>
        /// Gets the prepared manager's system context.
        /// </summary>
        public ISystemContext SystemContext { get; }

        /// <summary>
        /// Gets the resolver for the fully indexed prepared graph.
        /// </summary>
        public NodeBehaviorAddressSpace AddressSpace { get; }

        /// <summary>
        /// Gets the source registration's service provider, if it was DI-created.
        /// </summary>
        public IServiceProvider? Services { get; }

        /// <summary>
        /// Gets the server telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the server time provider.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// Gets the source that owns the generation.
        /// </summary>
        public INodeSource Source { get; }

        /// <summary>
        /// Gets the source generation identity.
        /// </summary>
        public NodeBehaviorGenerationIdentity Generation { get; }
    }
}
