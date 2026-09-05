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
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Nodes;
using static Opc.Ua.Server.RuntimeNodeSet.RuntimeNodeSetNodeManagerFactory;

namespace Opc.Ua.Server.RuntimeNodeSet
{
    /// <summary>
    /// Internal <see cref="FluentNodeManagerBase"/> that imports one or
    /// more NodeSet2 documents and optionally applies a fluent
    /// <see cref="INodeManagerBuilder"/> configuration callback.
    /// </summary>
    /// <remarks>
    /// Created exclusively by
    /// <see cref="RuntimeNodeSetNodeManagerFactory.CreateAsync"/>; callers
    /// should not instantiate this class directly.
    /// </remarks>
    internal sealed class RuntimeNodeSetNodeManager :
        FluentNodeManagerBase,
        INodeManagerReloadParticipant
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        /// <param name="server">The server that owns this manager.</param>
        /// <param name="configuration">Application configuration.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="modelNamespaceUris">
        /// The model namespace URIs this manager owns (passed to the base
        /// class so it registers them in the server's namespace table).
        /// </param>
        /// <param name="documents">
        /// Topologically sorted array of parsed NodeSet2 documents.
        /// </param>
        /// <param name="defaultNamespaceUri">
        /// Namespace URI used as the default for browse-path lookups in
        /// the fluent builder. May be <c>null</c> when neither
        /// <paramref name="configure"/> nor <paramref name="configureAsync"/>
        /// is set.
        /// </param>
        /// <param name="configure">
        /// Optional fluent configuration callback invoked after all
        /// NodeSet2 nodes have been added to the address space.
        /// </param>
        /// <param name="configureAsync">
        /// Optional asynchronous fluent configuration callback invoked
        /// after <paramref name="configure"/> (if also set) has run,
        /// using the same unsealed builder. May return an
        /// <see cref="IAsyncDisposable"/> owned by this manager's
        /// generation; it is disposed asynchronously when the generation
        /// is torn down.
        /// </param>
        internal RuntimeNodeSetNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            string[] modelNamespaceUris,
            ParsedNodeSetDocument[] documents,
            string? defaultNamespaceUri,
            Action<INodeManagerBuilder>? configure,
            Func<INodeManagerBuilder, CancellationToken, ValueTask<IAsyncDisposable?>>? configureAsync)
            : base(server, configuration, logger, modelNamespaceUris)
        {
            m_documents = documents
                ?? throw new ArgumentNullException(nameof(documents));
            m_defaultNamespaceUri = defaultNamespaceUri;
            m_configure = configure;
            m_configureAsync = configureAsync;
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

            ushort defaultNamespaceIndex =
                m_configure is not null || m_configureAsync is not null
                    ? ResolveDefaultNamespaceIndex()
                    : NamespaceIndex;
            NodeManagerBuilder builder = CreateFluentBuilder(defaultNamespaceIndex);
            builder.EnableGraphAuthoring(
                importFactoryProvider: null,
                ValidateOwnedNodeNamespace);

            // Import every document into one graph-builder batch. The builder
            // links parent-child relationships once when registration begins.
            foreach (ParsedNodeSetDocument doc in m_documents)
            {
                ((INodeGraphBuilder)builder).Import(doc.NodeSet);
            }

            await builder.RegisterAuthoredNodesAsync(
                (node, ct) => AddPredefinedNodeAsync(
                    SystemContext,
                    node,
                    ct),
                cancellationToken).ConfigureAwait(false);

            await CompleteConfigureAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            ReportUnbackedExternalParents(builder.ImportedNodes);

            m_configure?.Invoke(builder);

            IAsyncDisposable? generationOwner = m_configureAsync is null
                ? null
                : await m_configureAsync(builder, cancellationToken).ConfigureAwait(false);
            try
            {
                builder.SealGraphAuthoring();
                cancellationToken.ThrowIfCancellationRequested();
                m_dispatcher = builder.Dispatcher;
                foreach (KeyValuePair<NodeId, NodeState> entry in PredefinedNodes)
                {
                    builder.Dispatcher.NotifyNodeAdded(SystemContext, entry.Value);
                }
                builder.StartSimulations();
            }
            catch (Exception activationException) when (
                activationException is not OutOfMemoryException)
            {
                if (generationOwner is not null)
                {
                    try
                    {
                        await generationOwner.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception disposalException) when (
                        disposalException is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "RuntimeNodeSet configuration and generation owner disposal both failed.",
                            activationException,
                            disposalException);
                    }
                }
                throw;
            }

            m_generationOwner = generationOwner;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Disposes the <see cref="IAsyncDisposable"/> generation owner
        /// returned by <see cref="RuntimeNodeSetOptions.ConfigureAsync"/>
        /// (if any) before releasing the base class's address-space
        /// resources. Both steps always run, even if one of them fails,
        /// so the owner can never leak. If both fail, the base cleanup
        /// failure is treated as primary and reported together with the
        /// owner disposal failure via <see cref="AggregateException"/>.
        /// </remarks>
        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            IAsyncDisposable? owner = Interlocked.Exchange(ref m_generationOwner, null);
            Exception? ownerException = null;

            if (owner is not null)
            {
                try
                {
                    await owner.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    ownerException = ex;
                }
            }

            Exception? baseException = null;
            try
            {
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                baseException = ex;
            }

            if (baseException is not null && ownerException is not null)
            {
                throw new AggregateException(
                    "RuntimeNodeSet address-space cleanup and generation owner disposal both failed.",
                    baseException,
                    ownerException);
            }

            if (baseException is not null)
            {
                ExceptionDispatchInfo.Capture(baseException).Throw();
            }

            if (ownerException is not null)
            {
                ExceptionDispatchInfo.Capture(ownerException).Throw();
            }
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

        protected override ValueTask OnNodeRemovedAsync(
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            m_dispatcher?.NotifyNodeRemoved(SystemContext, node);
            return base.OnNodeRemovedAsync(node, cancellationToken);
        }

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

        internal IReadOnlyDictionary<NodeId, DataTypeDefinition> GetDataTypeDefinitions()
        {
            var definitions = new Dictionary<NodeId, DataTypeDefinition>();
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is DataTypeState dataType &&
                    dataType.DataTypeDefinition.TryGetValue(
                        out DataTypeDefinition? definition))
                {
                    definitions[dataType.NodeId] = definition;
                }
            }
            return definitions;
        }

        internal IReadOnlyDictionary<NodeId, ArrayOf<NodeId>> GetDataTypeEncodings()
        {
            var encodings = new Dictionary<NodeId, ArrayOf<NodeId>>();
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is not DataTypeState dataType)
                {
                    continue;
                }

                var references = new List<IReference>();
                dataType.GetReferences(
                    SystemContext,
                    references,
                    ReferenceTypeIds.HasEncoding,
                    isInverse: false);
                var encodingIds = new List<NodeId>();
                foreach (IReference reference in references)
                {
                    if (!reference.TargetId.IsAbsolute)
                    {
                        encodingIds.Add((NodeId)reference.TargetId);
                    }
                }
                encodings[dataType.NodeId] =
                    new ArrayOf<NodeId>(encodingIds.ToArray());
            }
            return encodings;
        }

        internal IReadOnlyDictionary<
            NodeId,
            IReadOnlyDictionary<QualifiedName, Variant>> GetSemanticProperties()
        {
            var nodes = new Dictionary<
                NodeId,
                IReadOnlyDictionary<QualifiedName, Variant>>();
            foreach (NodeState node in PredefinedNodes.Values)
            {
                var children = new List<BaseInstanceState>();
                node.GetChildren(SystemContext, children);
                var properties = new Dictionary<QualifiedName, Variant>();
                foreach (BaseInstanceState child in children)
                {
                    if (child is BaseVariableState property &&
                        (property.AccessLevel & AccessLevels.SemanticChange) != 0)
                    {
                        properties[property.BrowseName] = property.Value.Copy();
                    }
                }

                if (properties.Count > 0)
                {
                    nodes[node.NodeId] = properties;
                }
            }
            return nodes;
        }

        internal Dictionary<NodeId, IList<IReference>> GetAddedReferences()
        {
            lock (m_addedReferencesLock)
            {
                return m_addedReferences.ToDictionary(
                    entry => entry.Key,
                    entry => (IList<IReference>)[.. entry.Value]);
            }
        }

        internal bool ContainsNode(NodeId nodeId)
        {
            return PredefinedNodes.ContainsKey(nodeId);
        }

        public async ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
            IAsyncNodeManager replacement,
            CancellationToken ct = default)
        {
            if (replacement is not RuntimeNodeSetNodeManager replacementRuntime)
            {
                throw new NotSupportedException(
                    "A runtime NodeSet registration can only be reloaded " +
                    "with another runtime NodeSet NodeManager.");
            }

            Dictionary<NodeId, IList<IReference>> addedReferences =
                GetAddedReferences();
            await replacementRuntime
                .AddReferencesAsync(addedReferences, ct)
                .ConfigureAwait(false);

            var droppedReferences = new List<LocalReference>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in addedReferences)
            {
                if (replacementRuntime.ContainsNode(entry.Key))
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

        /// <summary>
        /// Reports an imported node whose declared <c>ParentNodeId</c> is
        /// outside this manager and is not backed by an explicit inverse
        /// hierarchical Reference.
        /// </summary>
        /// <remarks>
        /// In NodeSet2 the <c>ParentNodeId</c> attribute is a hint; the
        /// authoritative edge is the entry under <c>&lt;References&gt;</c>,
        /// which the reverse-reference pass turns into an external reference.
        /// A node that names an out-of-manager parent but carries no such
        /// Reference therefore materializes with no path from that parent,
        /// and no ReferenceType can be inferred to repair it. That is an
        /// authoring defect, so it is reported rather than guessed at or
        /// silently accepted.
        /// </remarks>
        private void ReportUnbackedExternalParents(NodeStateCollection nodes)
        {
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (nodes[ii] is not BaseInstanceState { Parent: null } instance)
                {
                    continue;
                }

                if (!UANodeSet.TryGetUnresolvedParentNodeId(instance, out NodeId parentNodeId))
                {
                    continue;
                }

                var references = new List<IReference>();
                instance.GetReferences(SystemContext, references);

                bool backed = false;
                for (int jj = 0; jj < references.Count; jj++)
                {
                    IReference reference = references[jj];
                    if (reference.IsInverse &&
                        !reference.TargetId.IsNull &&
                        !reference.TargetId.IsAbsolute &&
                        (NodeId)reference.TargetId == parentNodeId)
                    {
                        backed = true;
                        break;
                    }
                }

                if (!backed)
                {
                    m_logger.UnbackedExternalParent(instance.NodeId, parentNodeId);
                }
            }
        }

        /// <summary>
        /// Rejects nodes defined in namespaces that this manager does not own.
        /// Referenced namespaces may appear in NodeSet references, but not as
        /// NodeIds of nodes imported by this manager.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ValidateOwnedNodeNamespace(NodeState node)
        {
            NodeId nodeId = node.NodeId;
            if (nodeId.IsNull)
            {
                return;
            }

            for (int i = 0; i < NamespaceIndexes.Count; i++)
            {
                if (NamespaceIndexes[i] == nodeId.NamespaceIndex)
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Node '{nodeId}' is defined in namespace index " +
                $"{nodeId.NamespaceIndex}, which is not owned by this runtime NodeSet manager.");
        }

        /// <summary>
        /// Resolves the namespace index that corresponds to
        /// <see cref="m_defaultNamespaceUri"/>. Throws when the URI is
        /// not registered in the server's namespace table.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private ushort ResolveDefaultNamespaceIndex()
        {
            if (string.IsNullOrEmpty(m_defaultNamespaceUri))
            {
                throw new InvalidOperationException(
                    "No default namespace URI is available for the RuntimeNodeSet fluent " +
                    "builder. Set RuntimeNodeSetOptions.DefaultNamespaceUri explicitly.");
            }

            int index = Server.NamespaceUris.GetIndex(m_defaultNamespaceUri!);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"The default namespace URI '{m_defaultNamespaceUri}' is not registered " +
                    "in the server's namespace table. Verify RuntimeNodeSetOptions.DefaultNamespaceUri.");
            }

            return (ushort)index;
        }

        private readonly ParsedNodeSetDocument[] m_documents;
        private readonly string? m_defaultNamespaceUri;
        private readonly Action<INodeManagerBuilder>? m_configure;
        private readonly Func<INodeManagerBuilder, CancellationToken, ValueTask<IAsyncDisposable?>>? m_configureAsync;
        private readonly Lock m_addedReferencesLock = new();
        private readonly Dictionary<NodeId, List<IReference>> m_addedReferences = [];
        private IFluentDispatcher? m_dispatcher;
        private IAsyncDisposable? m_generationOwner;
    }

    internal static partial class RuntimeNodeSetNodeManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.RuntimeNodeSetNodeManager + 0, Level = LogLevel.Warning,
            Message = "Node {NodeId} declares ParentNodeId {ParentNodeId}, which is outside this " +
                "NodeManager and is not backed by an inverse hierarchical Reference, so no path to " +
                "it is created. Add the Reference to the NodeSet.")]
        public static partial void UnbackedExternalParent(
            this ILogger logger, NodeId nodeId, NodeId parentNodeId);
    }
}
