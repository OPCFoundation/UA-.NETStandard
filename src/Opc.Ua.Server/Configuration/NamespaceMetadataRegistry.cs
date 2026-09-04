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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The node-manager services a <see cref="NamespaceMetadataRegistry"/>
    /// needs from its host: the context and namespace to mint metadata nodes
    /// into, the <c>Server/Namespaces</c> node and node registration.
    /// <see cref="AsyncCustomNodeManager"/> already provides every member
    /// except <see cref="FindServerNamespacesNode"/>.
    /// </summary>
    internal interface INamespaceMetadataHost
    {
        /// <summary>
        /// The host manager's system context; its
        /// <see cref="ServerSystemContext.Server"/> resolves namespaces and
        /// looks nodes up across node managers.
        /// </summary>
        ServerSystemContext SystemContext { get; }

        /// <summary>
        /// The namespace index metadata BrowseNames are qualified with.
        /// </summary>
        ushort NamespaceIndex { get; }

        /// <summary>
        /// Resolves the <c>Server/Namespaces</c> node owned by the host, or
        /// <c>null</c> when it has not been loaded.
        /// </summary>
        NamespacesState? FindServerNamespacesNode();

        /// <summary>
        /// Registers a newly created metadata node with the host manager.
        /// </summary>
        ValueTask AddPredefinedNodeAsync(NodeState node, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Owns the <see cref="NamespaceMetadataState"/> nodes under
    /// <c>Server/Namespaces</c> (OPC 10000-5 §6.3.13) on behalf of the
    /// configuration node manager: lookup by URI or index with a cache that
    /// is invalidated when the <c>Namespaces</c> object changes, lazy
    /// creation of missing metadata objects, and change tracking of the
    /// <c>DefaultRolePermissions</c>/<c>DefaultUserRolePermissions</c>
    /// properties so permission caches can be invalidated through
    /// <see cref="DefaultPermissionsChanged"/>. The registry remembers every
    /// node it subscribed to, including nodes owned by other node managers
    /// that are referenced from <c>Server/Namespaces</c>, so
    /// <see cref="Detach"/> releases all of them.
    /// </summary>
    internal sealed class NamespaceMetadataRegistry
    {
        /// <summary>
        /// Creates a registry bound to <paramref name="host"/>.
        /// </summary>
        /// <param name="host">The node manager owning the namespace nodes.</param>
        /// <param name="logger">The logger.</param>
        public NamespaceMetadataRegistry(INamespaceMetadataHost host, ILogger logger)
        {
            m_host = host ?? throw new ArgumentNullException(nameof(host));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Raised when the <c>DefaultRolePermissions</c> or
        /// <c>DefaultUserRolePermissions</c> value of any tracked metadata
        /// node changes. The sender is the host node manager.
        /// </summary>
        public event EventHandler? DefaultPermissionsChanged;

        /// <summary>
        /// Gets the metadata node for <paramref name="namespaceUri"/>, or
        /// <c>null</c> when none exists. Results are cached until the
        /// <c>Namespaces</c> object changes.
        /// </summary>
        public async ValueTask<NamespaceMetadataState?> GetAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            lock (m_lock)
            {
                if (m_statesByUri.TryGetValue(namespaceUri, out NamespaceMetadataState? value))
                {
                    return value;
                }
            }

            NamespaceMetadataState? namespaceMetadataState = await FindAsync(
                namespaceUri, cancellationToken).ConfigureAwait(false);

            lock (m_lock)
            {
                // remember the result for faster access.
                m_statesByUri[namespaceUri] = namespaceMetadataState!;
            }

            return namespaceMetadataState;
        }

        /// <summary>
        /// Gets the metadata node for <paramref name="namespaceIndex"/>, or
        /// <c>null</c> when none exists.
        /// </summary>
        public async ValueTask<NamespaceMetadataState?> GetAsync(
            ushort namespaceIndex,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                if (m_statesByIndex.TryGetValue(namespaceIndex, out NamespaceMetadataState? value))
                {
                    return value;
                }
            }

            string? namespaceUri = m_host.SystemContext.Server.NamespaceUris.GetString(namespaceIndex);
            NamespaceMetadataState? namespaceMetadataState = await GetAsync(
                namespaceUri!, cancellationToken).ConfigureAwait(false);

            lock (m_lock)
            {
                m_statesByIndex[namespaceIndex] = namespaceMetadataState!;
            }

            return namespaceMetadataState!;
        }

        /// <summary>
        /// Returns the metadata node for <paramref name="namespaceUri"/>,
        /// creating and registering it under <c>Server/Namespaces</c> when it
        /// does not exist yet, and subscribes to its default-permission
        /// properties.
        /// </summary>
        public async ValueTask<NamespaceMetadataState> CreateAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            NamespaceMetadataState? namespaceMetadataState = await FindAsync(
                namespaceUri, cancellationToken).ConfigureAwait(false);

            if (namespaceMetadataState == null)
            {
                // find ServerNamespaces node
                if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
                {
                    m_logger.CannotCreateNamespaceMetadataState(namespaceUri);
                    return null!;
                }

                ServerSystemContext context = m_host.SystemContext;

                // create the NamespaceMetadata node
                namespaceMetadataState = context.CreateInstanceOfNamespaceMetadataType(
                    serverNamespacesNode,
                    new QualifiedName(namespaceUri, m_host.NamespaceIndex));
                namespaceMetadataState.NodeId = context.NodeIdFactory.New(context, namespaceMetadataState);
                namespaceMetadataState.DisplayName = LocalizedText.From(namespaceUri);
                namespaceMetadataState.SymbolicName = namespaceUri;
                namespaceMetadataState!.NamespaceUri!.Value = namespaceUri;
                namespaceMetadataState.AddDefaultRolePermissions(context)
                    .AddDefaultUserRolePermissions(context);

                // add node as child of ServerNamespaces and in predefined nodes
                serverNamespacesNode.AddChild(namespaceMetadataState);
                await serverNamespacesNode.ClearChangeMasksAsync(context, true, cancellationToken)
                    .ConfigureAwait(false);
                await m_host.AddPredefinedNodeAsync(namespaceMetadataState, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Subscribe to the default permission properties so that any future changes
            // trigger a DefaultPermissionsChanged notification to allow caches to be invalidated.
            SubscribeToDefaultPermissions(namespaceMetadataState);

            return namespaceMetadataState;
        }

        /// <summary>
        /// Starts tracking the <c>Server/Namespaces</c> node: cache
        /// invalidation on child/reference changes and default-permission
        /// tracking for every metadata node already present. Calling it
        /// again re-subscribes without duplicating handlers.
        /// </summary>
        /// <param name="context">The context used to enumerate children.</param>
        public void Attach(ISystemContext context)
        {
            if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
            {
                return;
            }

            // unsubscribe first so a repeated Attach never registers the handler twice
            serverNamespacesNode.StateChanged -= OnServerNamespacesChanged;
            serverNamespacesNode.StateChanged += OnServerNamespacesChanged;

            IList<BaseInstanceState> children = [];
            serverNamespacesNode.GetChildren(context, children);

            foreach (BaseInstanceState child in children)
            {
                if (child is NamespaceMetadataState metadataState)
                {
                    SubscribeToDefaultPermissions(metadataState);
                }
            }
        }

        /// <summary>
        /// Stops tracking: unsubscribes from the <c>Server/Namespaces</c>
        /// node and from every metadata node the registry subscribed to,
        /// and drops the lookup cache.
        /// </summary>
        public void Detach()
        {
            if (m_host.FindServerNamespacesNode() is NamespacesState serverNamespacesNode)
            {
                serverNamespacesNode.StateChanged -= OnServerNamespacesChanged;
            }

            NamespaceMetadataState[] tracked;
            lock (m_lock)
            {
                tracked = [.. m_tracked];
                m_tracked.Clear();
                m_statesByUri.Clear();
                m_statesByIndex.Clear();
            }

            foreach (NamespaceMetadataState metadataState in tracked)
            {
                metadataState.StateChanged -= OnNamespaceChildrenChanged;
                metadataState.DefaultRolePermissions?.StateChanged -= OnDefaultPermissionsChanged;
                metadataState.DefaultUserRolePermissions?.StateChanged -= OnDefaultPermissionsChanged;
            }
        }

        /// <summary>
        /// Finds the metadata node for <paramref name="namespaceUri"/> among
        /// the children and forward references of <c>Server/Namespaces</c>,
        /// resolving references into other node managers.
        /// </summary>
        private async ValueTask<NamespaceMetadataState?> FindAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // find ServerNamespaces node
                if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
                {
                    m_logger.CannotFindServerNamespacesNode();
                    return null;
                }

                ServerSystemContext context = m_host.SystemContext;

                IList<BaseInstanceState> serverNamespacesChildren = [];
                serverNamespacesNode.GetChildren(context, serverNamespacesChildren);

                foreach (BaseInstanceState namespacesReference in serverNamespacesChildren)
                {
                    // Find NamespaceMetadata node of NamespaceUri in Namespaces children
                    if (namespacesReference is not NamespaceMetadataState namespaceMetadata)
                    {
                        continue;
                    }

                    if (namespaceMetadata!.NamespaceUri!.Value == namespaceUri)
                    {
                        return namespaceMetadata;
                    }
                }

                IList<IReference> serverNamespacesReferences = [];
                serverNamespacesNode.GetReferences(context, serverNamespacesReferences);

                foreach (IReference serverNamespacesReference in serverNamespacesReferences)
                {
                    if (!serverNamespacesReference.IsInverse)
                    {
                        // Find NamespaceMetadata node of NamespaceUri in Namespaces references.
                        var nameSpaceNodeId = ExpandedNodeId.ToNodeId(
                            serverNamespacesReference.TargetId,
                            context.Server.NamespaceUris);
                        if (await context.Server.NodeManager.FindNodeInAddressSpaceAsync(
                            nameSpaceNodeId, cancellationToken).ConfigureAwait(false) is not NamespaceMetadataState namespaceMetadata)
                        {
                            continue;
                        }

                        if (namespaceMetadata!.NamespaceUri!.Value == namespaceUri)
                        {
                            return namespaceMetadata;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                m_logger.ErrorSearchingNamespaceMetadata(ex, namespaceUri);
                return null;
            }
        }

        /// <summary>
        /// Clear NamespaceMetadata nodes cache in case nodes are added or deleted
        /// </summary>
        private void OnServerNamespacesChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) != 0 ||
                (changes & NodeStateChangeMasks.References) != 0)
            {
                try
                {
                    lock (m_lock)
                    {
                        m_statesByUri.Clear();
                        m_statesByIndex.Clear();
                    }

                    if (node is NamespacesState serverNamespacesNode)
                    {
                        IList<BaseInstanceState> children = [];
                        serverNamespacesNode.GetChildren(context, children);

                        foreach (BaseInstanceState child in children)
                        {
                            if (child is NamespaceMetadataState metadataState)
                            {
                                SubscribeToDefaultPermissions(metadataState);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore errors
                }
            }
        }

        /// <summary>
        /// Subscribes to the <c>StateChanged</c> events of the <c>DefaultRolePermissions</c>
        /// and <c>DefaultUserRolePermissions</c> child nodes of a <see cref="NamespaceMetadataState"/>
        /// to detect changes that require permission cache invalidation, and remembers the
        /// node so <see cref="Detach"/> can release it.
        /// </summary>
        private void SubscribeToDefaultPermissions(NamespaceMetadataState namespaceMetadataState)
        {
            if (namespaceMetadataState.DefaultRolePermissions != null)
            {
                // unsubscribe first to avoid duplicate subscriptions if called multiple times
                namespaceMetadataState.DefaultRolePermissions.StateChanged -= OnDefaultPermissionsChanged;
                namespaceMetadataState.DefaultRolePermissions.StateChanged += OnDefaultPermissionsChanged;
            }

            if (namespaceMetadataState.DefaultUserRolePermissions != null)
            {
                namespaceMetadataState.DefaultUserRolePermissions.StateChanged -= OnDefaultPermissionsChanged;
                namespaceMetadataState.DefaultUserRolePermissions.StateChanged += OnDefaultPermissionsChanged;
            }

            namespaceMetadataState.StateChanged -= OnNamespaceChildrenChanged;
            namespaceMetadataState.StateChanged += OnNamespaceChildrenChanged;

            lock (m_lock)
            {
                m_tracked.Add(namespaceMetadataState);
            }
        }

        /// <summary>
        /// Handles children change on NamespaceMetadataState and resubscribes to the default permissions nodes
        /// to ensure we are notified of changes on those nodes even if they are recreated.
        /// </summary>
        private void OnNamespaceChildrenChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) != 0 &&
                node is NamespaceMetadataState namespaceMetadataState)
            {
                SubscribeToDefaultPermissions(namespaceMetadataState);
            }
        }

        /// <summary>
        /// Handles value changes on <c>DefaultRolePermissions</c> or <c>DefaultUserRolePermissions</c>
        /// and raises the <see cref="DefaultPermissionsChanged"/> event with the host as sender.
        /// </summary>
        private void OnDefaultPermissionsChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Value) != 0)
            {
                DefaultPermissionsChanged?.Invoke(m_host, EventArgs.Empty);
            }
        }

        private readonly INamespaceMetadataHost m_host;
        private readonly ILogger m_logger;
        private readonly Dictionary<string, NamespaceMetadataState> m_statesByUri = [];
        private readonly Dictionary<ushort, NamespaceMetadataState> m_statesByIndex = [];
        private readonly HashSet<NamespaceMetadataState> m_tracked = [];
        private readonly Lock m_lock = new();
    }

    internal static partial class NamespaceMetadataRegistryLog
    {
        [LoggerMessage(EventId = ServerEventIds.NamespaceMetadataRegistry + 0, Level = LogLevel.Error,
            Message = "Cannot create NamespaceMetadataState for namespace '{NamespaceUri}'.")]
        public static partial void CannotCreateNamespaceMetadataState(this ILogger logger, string namespaceUri);

        [LoggerMessage(EventId = ServerEventIds.NamespaceMetadataRegistry + 1, Level = LogLevel.Error,
            Message = "Cannot find ObjectIds.Server_Namespaces node.")]
        public static partial void CannotFindServerNamespacesNode(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.NamespaceMetadataRegistry + 2, Level = LogLevel.Error,
            Message = "Error searching NamespaceMetadata for namespaceUri {NamespaceUri}.")]
        public static partial void ErrorSearchingNamespaceMetadata(
            this ILogger logger,
            Exception ex,
            string namespaceUri);
    }
}
