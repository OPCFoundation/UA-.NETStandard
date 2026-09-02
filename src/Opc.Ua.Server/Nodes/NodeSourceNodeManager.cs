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
using System.Runtime.ExceptionServices;
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
            : this(
                source,
                serviceProvider: null,
                NodeBehaviorGenerationIdentity.CreateInitial())
        {
        }

        /// <summary>
        /// Initializes a DI-backed factory and snapshots the source namespaces.
        /// </summary>
        public NodeSourceNodeManagerFactory(
            INodeSource source,
            IServiceProvider serviceProvider)
            : this(
                source,
                serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider)),
                NodeBehaviorGenerationIdentity.CreateInitial())
        {
        }

        private NodeSourceNodeManagerFactory(
            INodeSource source,
            IServiceProvider? serviceProvider,
            NodeBehaviorGenerationIdentity generation)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            m_serviceProvider = serviceProvider;
            m_generation = generation ??
                throw new ArgumentNullException(nameof(generation));
            NamespacesUris = ValidateNamespaceUris(source.NamespaceUris);
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris { get; }

        /// <summary>
        /// Creates a direct lifecycle replacement that continues the behavior identity.
        /// </summary>
        public static NodeSourceNodeManagerFactory CreateReplacement(
            INodeSource source,
            NodeManagerRegistration registration)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            NodeSourceNodeManager? manager =
                registration.NodeManager as NodeSourceNodeManager;
            NodeBehaviorGenerationIdentity generation = manager is not null
                ? manager.BehaviorGeneration.Next()
                : new NodeBehaviorGenerationIdentity(
                    registration.Id,
                    checked(registration.Generation + 1));
            return new NodeSourceNodeManagerFactory(
                source,
                manager?.Services,
                generation);
        }

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
                m_serviceProvider,
                m_generation,
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
        private readonly IServiceProvider? m_serviceProvider;
        private readonly NodeBehaviorGenerationIdentity m_generation;
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
            IServiceProvider? serviceProvider,
            NodeBehaviorGenerationIdentity behaviorGeneration,
            params string[] namespaceUris)
            : base(server, configuration, logger, namespaceUris)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            m_serviceProvider = serviceProvider;
            BehaviorGeneration = behaviorGeneration ??
                throw new ArgumentNullException(nameof(behaviorGeneration));
        }

        /// <summary>
        /// Gets the identity used by behavior contexts for this generation.
        /// </summary>
        public NodeBehaviorGenerationIdentity BehaviorGeneration { get; }

        /// <summary>
        /// Gets the source registration's service provider, if available.
        /// </summary>
        internal IServiceProvider? Services => m_serviceProvider;

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
            builder.EnableGraphAuthoring(
                m_source as INodeSetImportFactoryProvider);
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
            builder.SealGraphAuthoring();
            await ActivateBehaviorsAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            m_dispatcher = builder.Dispatcher;
            foreach (KeyValuePair<NodeId, NodeState> entry in PredefinedNodes)
            {
                builder.Dispatcher.NotifyNodeAdded(SystemContext, entry.Value);
            }
            builder.StartSimulations();
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            NodeBehaviorActivation? activation =
                Interlocked.Exchange(ref m_behaviorActivation, null);
            Exception? behaviorException = null;
            if (activation is not null)
            {
                try
                {
                    await activation
                        .DeactivateAndDisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    behaviorException = ex;
                }
            }

            Exception? baseException = null;
            try
            {
                await base
                    .DeleteAddressSpaceAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                baseException = ex;
            }

            if (behaviorException is not null && baseException is not null)
            {
                throw new AggregateException(
                    "Node behavior and address-space cleanup both failed.",
                    behaviorException,
                    baseException);
            }
            if (behaviorException is not null)
            {
                ExceptionDispatchInfo.Capture(behaviorException).Throw();
            }
            if (baseException is not null)
            {
                ExceptionDispatchInfo.Capture(baseException).Throw();
            }
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
                !parent.NodeId.IsNull)
            {
                string parentIdentifier =
                    parent.NodeId.NamespaceIndex == namespaceIndex
                        ? parent.NodeId.IdentifierAsString
                        : parent.NodeId.ToString();
                return new NodeId(
                    $"{parentIdentifier}_{browseName}",
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

        private async ValueTask ActivateBehaviorsAsync(
            CancellationToken cancellationToken)
        {
            if (m_source is not INodeBehaviorFactoryProvider provider)
            {
                return;
            }

            var registry = new NodeBehaviorRegistry(
                provider.GetNodeBehaviorFactories(),
                Server.NamespaceUris,
                Server.TypeTree);
            if (registry.IsEmpty)
            {
                return;
            }

            var addressSpace = new NodeBehaviorAddressSpace(
                Server.NamespaceUris,
                Find);
            var activation = new NodeBehaviorActivation(
                registry,
                addressSpace,
                SystemContext,
                m_serviceProvider,
                Server.Telemetry,
                (Server as ITimeProviderProvider)?.TimeProvider ??
                    TimeProvider.System,
                m_source,
                BehaviorGeneration);
            m_behaviorActivation = activation;
            await activation
                .ActivateAsync(
                    new ArrayOf<NodeState>(PredefinedNodes.Values.ToArray()),
                    cancellationToken)
                .ConfigureAwait(false);
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
        private readonly IServiceProvider? m_serviceProvider;
        private readonly Lock m_addedReferencesLock = new();
        private readonly Dictionary<NodeId, List<IReference>> m_addedReferences = [];
        private NodeBehaviorActivation? m_behaviorActivation;
        private IFluentDispatcher? m_dispatcher;
        private int m_buildStarted;
    }
}
