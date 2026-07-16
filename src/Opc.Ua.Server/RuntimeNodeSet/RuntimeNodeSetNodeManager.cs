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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;
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
    internal sealed class RuntimeNodeSetNodeManager : FluentNodeManagerBase
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
        /// the fluent builder. May be <c>null</c> when no
        /// <paramref name="configure"/> callback is set.
        /// </param>
        /// <param name="configure">
        /// Optional fluent configuration callback invoked after all
        /// NodeSet2 nodes have been added to the address space.
        /// </param>
        internal RuntimeNodeSetNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            string[] modelNamespaceUris,
            ParsedNodeSetDocument[] documents,
            string? defaultNamespaceUri,
            Action<INodeManagerBuilder>? configure)
            : base(server, configuration, logger, modelNamespaceUris)
        {
            m_documents = documents
                ?? throw new ArgumentNullException(nameof(documents));
            m_defaultNamespaceUri = defaultNamespaceUri;
            m_configure = configure;
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

            // Step 1 – Ensure all mapping NamespaceUris (external references)
            // from every document are registered before any Import call, so
            // that node ids in those namespaces resolve correctly.
            foreach (ParsedNodeSetDocument doc in m_documents)
            {
                RegisterMappingNamespaces(doc.NodeSet);
            }

            // Step 2 – Import each document in topological order.
            var predefinedNodes = new NodeStateCollection();

            foreach (ParsedNodeSetDocument doc in m_documents)
            {
                doc.NodeSet.Import(SystemContext, predefinedNodes, linkParentChild: true);
            }

            // Step 3 – Detect duplicate NodeIds across all loaded sources.
            DetectDuplicateNodeIds(predefinedNodes);
            ValidateOwnedNodeNamespaces(predefinedNodes);

            // Step 4 – Add every imported node through the base flow so they
            // are indexed and properly linked.
            for (int i = 0; i < predefinedNodes.Count; i++)
            {
                if (predefinedNodes[i] is BaseInstanceState { Parent: not null })
                {
                    continue;
                }

                await AddPredefinedNodeAsync(
                    SystemContext,
                    predefinedNodes[i],
                    cancellationToken).ConfigureAwait(false);
            }

            // Step 5 – Establish reverse references to external node managers.
            await AddReverseReferencesAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            // Step 6 – Apply the optional fluent configuration.
            if (m_configure is not null)
            {
                ushort defaultNsIndex = ResolveDefaultNamespaceIndex();

                NodeManagerBuilder builder = CreateFluentBuilder(defaultNsIndex);
                m_configure(builder);
                builder.Seal();
                m_dispatcher = builder.Dispatcher;

                // Step 7 – Replay NotifyNodeAdded for every predefined node
                // so that OnNodeAdded handlers registered in Configure fire.
                foreach (KeyValuePair<NodeId, NodeState> kvp in PredefinedNodes)
                {
                    builder.NotifyNodeAdded(SystemContext, kvp.Value);
                }
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

        /// <inheritdoc/>
        protected override async ValueTask RemovePredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            List<LocalReference> referencesToRemove,
            CancellationToken cancellationToken = default)
        {
            m_dispatcher?.NotifyNodeRemoved(context, node);

            await base.RemovePredefinedNodeAsync(
                context,
                node,
                referencesToRemove,
                cancellationToken).ConfigureAwait(false);
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

        /// <summary>
        /// Appends all <c>NamespaceUris</c> entries from the NodeSet2
        /// document to the server's namespace table without claiming them.
        /// These are mapping/reference namespaces required for resolving
        /// node ids that belong to external models.
        /// </summary>
        private void RegisterMappingNamespaces(UANodeSet nodeSet)
        {
            if (nodeSet.NamespaceUris is null)
            {
                return;
            }

            foreach (string uri in nodeSet.NamespaceUris)
            {
                if (!string.IsNullOrEmpty(uri))
                {
                    Server.NamespaceUris.GetIndexOrAppend(uri);
                }
            }
        }

        /// <summary>
        /// Scans the imported node collection for duplicate
        /// <see cref="NodeId"/> values and throws
        /// <see cref="InvalidOperationException"/> on the first duplicate
        /// detected.
        /// </summary>
        private static void DetectDuplicateNodeIds(NodeStateCollection nodes)
        {
            var seen = new HashSet<NodeId>();

            for (int i = 0; i < nodes.Count; i++)
            {
                NodeId id = nodes[i].NodeId;

                if (!id.IsNull && !seen.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Duplicate NodeId '{id}' detected across the loaded NodeSet2 " +
                        "sources. Each node must have a unique NodeId.");
                }
            }
        }

        /// <summary>
        /// Rejects nodes defined in namespaces that this manager does not own.
        /// Referenced namespaces may appear in NodeSet references, but not as
        /// NodeIds of nodes imported by this manager.
        /// </summary>
        private void ValidateOwnedNodeNamespaces(NodeStateCollection nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeId nodeId = nodes[i].NodeId;
                if (nodeId.IsNull)
                {
                    continue;
                }

                bool owned = false;
                for (int j = 0; j < NamespaceIndexes.Count; j++)
                {
                    if (NamespaceIndexes[j] == nodeId.NamespaceIndex)
                    {
                        owned = true;
                        break;
                    }
                }

                if (!owned)
                {
                    throw new InvalidOperationException(
                        $"Node '{nodeId}' is defined in namespace index " +
                        $"{nodeId.NamespaceIndex}, which is not owned by this runtime NodeSet manager.");
                }
            }
        }

        /// <summary>
        /// Resolves the namespace index that corresponds to
        /// <see cref="m_defaultNamespaceUri"/>. Throws when the URI is
        /// not registered in the server's namespace table.
        /// </summary>
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
        private IFluentDispatcher? m_dispatcher;
    }
}
