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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Creates the internal adapter for a compositional node source.
    /// </summary>
    internal sealed class NodeSourceNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes the factory and snapshots the source namespaces.
        /// </summary>
        public NodeSourceNodeManagerFactory(INodeSource source)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            NamespacesUris = ValidateNamespaceUris(source.NamespaceUris);
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris { get; }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            ILogger logger = server.Telemetry.CreateLogger<NodeSourceNodeManager>();
            var namespaceUris = new string[NamespacesUris.Count];
            for (int i = 0; i < NamespacesUris.Count; i++)
            {
                namespaceUris[i] = NamespacesUris[i];
            }

#pragma warning disable CA2000 // Ownership transfers to the master node manager.
            var manager = new NodeSourceNodeManager(
                server,
                configuration,
                logger,
                m_source,
                namespaceUris);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private static ArrayOf<string> ValidateNamespaceUris(
            ArrayOf<string> namespaceUris)
        {
            if (namespaceUris.IsNull || namespaceUris.Count == 0)
            {
                throw new InvalidOperationException(
                    "A node source must declare at least one namespace URI.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new string[namespaceUris.Count];
            for (int i = 0; i < namespaceUris.Count; i++)
            {
                string namespaceUri = namespaceUris[i];
                if (string.IsNullOrWhiteSpace(namespaceUri))
                {
                    throw new InvalidOperationException(
                        $"Node source namespace URI at index {i} is null or empty.");
                }
                if (string.Equals(
                    namespaceUri,
                    Opc.Ua.Types.Namespaces.OpcUa,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "A node source cannot own the OPC UA base namespace.");
                }
                if (!seen.Add(namespaceUri))
                {
                    throw new InvalidOperationException(
                        $"Node source namespace URI '{namespaceUri}' is declared more than once.");
                }
                result[i] = namespaceUri;
            }
            return new ArrayOf<string>(result);
        }

        private readonly INodeSource m_source;
    }

    /// <summary>
    /// Sealed adapter that runs an <see cref="INodeSource"/> on the existing
    /// fluent asynchronous NodeManager engine.
    /// </summary>
    internal sealed class NodeSourceNodeManager :
        FluentNodeManagerBase,
        INodeManagerReloadParticipant
    {
        /// <summary>
        /// Initializes a source generation.
        /// </summary>
        public NodeSourceNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            INodeSource source,
            params string[] namespaceUris)
            : base(server, configuration, logger, namespaceUris)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (externalReferences is null)
            {
                throw new ArgumentNullException(nameof(externalReferences));
            }
            if (Interlocked.CompareExchange(ref m_buildStarted, 1, 0) != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The node source has already built this manager generation.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndex);
            builder.EnableGraphAuthoring();
            await m_source.BuildAsync(builder, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await builder.RegisterAuthoredNodesAsync(
                (node, ct) => AddPredefinedNodeAsync(
                    SystemContext,
                    node,
                    ct),
                cancellationToken).ConfigureAwait(false);

            await CompleteConfigureAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
            m_dispatcher = builder.Dispatcher;
            builder.SealGraphAuthoring();
            foreach (KeyValuePair<NodeId, NodeState> entry in PredefinedNodes)
            {
                builder.Dispatcher.NotifyNodeAdded(SystemContext, entry.Value);
            }
            builder.StartSimulations();
        }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (!node.NodeId.IsNull)
            {
                return node.NodeId;
            }

            string? browseName = node.BrowseName.Name;
            if (string.IsNullOrEmpty(browseName))
            {
                return base.New(context, node);
            }

            ushort namespaceIndex = node.BrowseName.NamespaceIndex;
            if (namespaceIndex == 0)
            {
                namespaceIndex = NamespaceIndex;
            }

            if (node is BaseInstanceState { Parent: { } parent } &&
                !parent.NodeId.IsNull &&
                parent.NodeId.NamespaceIndex == namespaceIndex)
            {
                return new NodeId(
                    $"{parent.NodeId.IdentifierAsString}_{browseName}",
                    namespaceIndex);
            }

            return new NodeId(browseName, namespaceIndex);
        }

        /// <inheritdoc/>
        public override async ValueTask AddReferencesAsync(
            IDictionary<NodeId, IList<IReference>> references,
            CancellationToken cancellationToken = default)
        {
            await base.AddReferencesAsync(references, cancellationToken)
                .ConfigureAwait(false);

            lock (m_addedReferencesLock)
            {
                foreach (KeyValuePair<NodeId, IList<IReference>> entry in references)
                {
                    if (!PredefinedNodes.ContainsKey(entry.Key))
                    {
                        continue;
                    }

                    if (!m_addedReferences.TryGetValue(
                        entry.Key,
                        out List<IReference>? added))
                    {
                        m_addedReferences[entry.Key] = added = [];
                    }

                    foreach (IReference reference in entry.Value)
                    {
                        if (!added.Any(existing =>
                            existing.ReferenceTypeId == reference.ReferenceTypeId &&
                            existing.IsInverse == reference.IsInverse &&
                            existing.TargetId == reference.TargetId))
                        {
                            added.Add(reference);
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<ServiceResult> DeleteReferenceAsync(
            object sourceHandle,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional,
            CancellationToken cancellationToken = default)
        {
            ServiceResult result = await base.DeleteReferenceAsync(
                sourceHandle,
                referenceTypeId,
                isInverse,
                targetId,
                deleteBidirectional,
                cancellationToken).ConfigureAwait(false);

            if (ServiceResult.IsGood(result) &&
                sourceHandle is NodeHandle handle)
            {
                lock (m_addedReferencesLock)
                {
                    if (m_addedReferences.TryGetValue(
                        handle.NodeId,
                        out List<IReference>? references))
                    {
                        references.RemoveAll(reference =>
                            reference.ReferenceTypeId == referenceTypeId &&
                            reference.IsInverse == isInverse &&
                            reference.TargetId == targetId);
                        if (references.Count == 0)
                        {
                            m_addedReferences.Remove(handle.NodeId);
                        }
                    }
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
            IAsyncNodeManager replacement,
            CancellationToken ct = default)
        {
            if (replacement is not NodeSourceNodeManager replacementSource)
            {
                throw new NotSupportedException(
                    "A node source registration can only be reloaded with another node source.");
            }

            Dictionary<NodeId, IList<IReference>> addedReferences =
                GetAddedReferences();
            await replacementSource
                .AddReferencesAsync(addedReferences, ct)
                .ConfigureAwait(false);

            var droppedReferences = new List<LocalReference>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in addedReferences)
            {
                if (replacementSource.PredefinedNodes.ContainsKey(entry.Key))
                {
                    continue;
                }

                foreach (IReference reference in entry.Value)
                {
                    if (!reference.TargetId.IsAbsolute)
                    {
                        var sourceId = (NodeId)reference.TargetId;
                        droppedReferences.Add(new LocalReference(
                            sourceId,
                            reference.ReferenceTypeId,
                            !reference.IsInverse,
                            entry.Key));
                    }
                }
            }
            return new ArrayOf<LocalReference>(droppedReferences.ToArray());
        }

        /// <inheritdoc/>
        protected override async ValueTask AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            await base.AddPredefinedNodeAsync(context, node, cancellationToken)
                .ConfigureAwait(false);
            m_dispatcher?.NotifyNodeAdded(context, node);
        }

        /// <inheritdoc/>
        protected override ValueTask OnNodeRemovedAsync(
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            m_dispatcher?.NotifyNodeRemoved(SystemContext, node);
            return base.OnNodeRemovedAsync(node, cancellationToken);
        }

        /// <inheritdoc/>
        protected override void OnMonitoredItemCreated(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            base.OnMonitoredItemCreated(context, handle, monitoredItem);
            if (handle?.Node is { } node)
            {
                m_dispatcher?.NotifyMonitoredItemCreated(context, node, monitoredItem);
            }
        }

        private Dictionary<NodeId, IList<IReference>> GetAddedReferences()
        {
            lock (m_addedReferencesLock)
            {
                return m_addedReferences.ToDictionary(
                    entry => entry.Key,
                    entry => (IList<IReference>)[.. entry.Value]);
            }
        }

        private readonly INodeSource m_source;
        private readonly Lock m_addedReferencesLock = new();
        private readonly Dictionary<NodeId, List<IReference>> m_addedReferences = [];
        private IFluentDispatcher? m_dispatcher;
        private int m_buildStarted;
    }
}
