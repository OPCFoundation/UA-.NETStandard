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
using System.Collections.Concurrent;
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
    /// <c>DefaultAccessRestrictions</c>/<c>DefaultRolePermissions</c>/
    /// <c>DefaultUserRolePermissions</c> properties so permission caches,
    /// including the namespace defaults remembered by long-lived operation
    /// contexts (<see cref="NamespaceDefaultPermissionReader.Generation"/>),
    /// can be invalidated through
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
        /// Raised when the <c>DefaultAccessRestrictions</c>,
        /// <c>DefaultRolePermissions</c> or <c>DefaultUserRolePermissions</c>
        /// value of any tracked metadata node changes, or when one of these
        /// properties is added, replaced or removed. The sender is the host
        /// node manager.
        /// </summary>
        public event EventHandler? DefaultPermissionsChanged;

        /// <summary>
        /// Gets the metadata node for <paramref name="namespaceUri"/>, or
        /// <c>null</c> when none exists. Results are cached until the
        /// <c>Namespaces</c> object changes.
        /// </summary>
        /// <remarks>
        /// Permission checks call this for every request. Cached results,
        /// found and missing, are read without a lock; only filling and
        /// invalidating the cache take it.
        /// </remarks>
        public async ValueTask<NamespaceMetadataState?> GetAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            if (m_statesByUri.TryGetValue(namespaceUri, out NamespaceMetadataState? value))
            {
                return value;
            }

            // A missing metadata node is only remembered while the
            // Namespaces node is unchanged: a node manager registered later
            // can link its NamespaceMetadata through a reference that raises
            // no StateChanged event, and its default permissions must then
            // be found instead of the cached miss. Such changes either bump
            // the generation (Invalidate) or leave pending change masks on
            // the Namespaces node, both checked without enumerating it.
            NodeStateChangeMasks pendingChanges = GetPendingNamespacesChanges();
            long generation = Interlocked.Read(ref m_generation);
            if (IsCachedMiss(m_missingByUri, namespaceUri, generation, pendingChanges))
            {
                return null;
            }

            NamespaceMetadataState? namespaceMetadataState = await FindAsync(
                namespaceUri, cancellationToken).ConfigureAwait(false);

            // metadata linked by reference from another node manager is tracked
            // as well, so its default changes invalidate permission caches.
            if (namespaceMetadataState != null)
            {
                SubscribeToDefaultPermissions(namespaceMetadataState);
            }

            lock (m_lock)
            {
                // remember the result for faster access. A miss found before an
                // invalidation carries the old generation and is never used.
                if (namespaceMetadataState != null)
                {
                    m_statesByUri[namespaceUri] = namespaceMetadataState;
                    m_missingByUri.TryRemove(namespaceUri, out _);
                }
                else
                {
                    m_missingByUri[namespaceUri] = (generation, pendingChanges);
                }
            }

            return namespaceMetadataState;
        }

        /// <summary>
        /// Gets the metadata node for <paramref name="namespaceIndex"/>, or
        /// <c>null</c> when none exists.
        /// </summary>
        /// <remarks>
        /// A namespace without a metadata node is remembered by index as well,
        /// with the same validity as the miss remembered by URI, so the common
        /// case of a namespace without metadata resolves no URI.
        /// </remarks>
        public async ValueTask<NamespaceMetadataState?> GetAsync(
            ushort namespaceIndex,
            CancellationToken cancellationToken = default)
        {
            if (m_statesByIndex.TryGetValue(namespaceIndex, out NamespaceMetadataState? value))
            {
                return value;
            }

            NodeStateChangeMasks pendingChanges = GetPendingNamespacesChanges();
            long generation = Interlocked.Read(ref m_generation);
            if (IsCachedMiss(m_missingByIndex, namespaceIndex, generation, pendingChanges))
            {
                return null;
            }

            string? namespaceUri = m_host.SystemContext.Server.NamespaceUris.GetString(namespaceIndex);
            NamespaceMetadataState? namespaceMetadataState = await GetAsync(
                namespaceUri!, cancellationToken).ConfigureAwait(false);

            lock (m_lock)
            {
                if (namespaceMetadataState != null)
                {
                    m_statesByIndex[namespaceIndex] = namespaceMetadataState;
                    m_missingByIndex.TryRemove(namespaceIndex, out _);
                }
                else
                {
                    m_missingByIndex[namespaceIndex] = (generation, pendingChanges);
                }
            }

            return namespaceMetadataState;
        }

        /// <summary>
        /// Resolves the metadata node for <paramref name="namespaceIndex"/>
        /// without awaiting: from the lookup cache, the children of
        /// <c>Server/Namespaces</c> and the reference targets resolved by
        /// <see cref="PrimeReferencesAsync"/> or an earlier asynchronous lookup.
        /// </summary>
        /// <returns>
        /// <c>true</c> when the result (including a definitive absence) is
        /// known; <c>false</c> when an unresolved reference of
        /// <c>Server/Namespaces</c> could hold the metadata node.
        /// </returns>
        public bool TryGetSnapshot(ushort namespaceIndex, out NamespaceMetadataState? state)
        {
            if (m_statesByIndex.TryGetValue(namespaceIndex, out state))
            {
                return true;
            }

            NodeStateChangeMasks pendingChanges = GetPendingNamespacesChanges();
            long generation = Interlocked.Read(ref m_generation);
            if (IsCachedMiss(m_missingByIndex, namespaceIndex, generation, pendingChanges))
            {
                state = null;
                return true;
            }

            string? namespaceUri = m_host.SystemContext.Server.NamespaceUris.GetString(namespaceIndex);
            if (namespaceUri == null)
            {
                state = null;
                return true;
            }

            if (m_statesByUri.TryGetValue(namespaceUri, out state))
            {
                return true;
            }

            if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
            {
                state = null;
                return true;
            }

            ServerSystemContext context = m_host.SystemContext;
            IList<BaseInstanceState> children = [];
            serverNamespacesNode.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is NamespaceMetadataState metadata &&
                    metadata.NamespaceUri?.Value == namespaceUri)
                {
                    state = metadata;
                    break;
                }
            }

            if (state == null)
            {
                IList<IReference> references = [];
                serverNamespacesNode.GetReferences(context, references);
                foreach (IReference reference in references)
                {
                    if (reference.IsInverse || reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                    {
                        continue;
                    }

                    var targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.Server.NamespaceUris);
                    if (targetId.IsNull ||
                        !m_referenceTargets.TryGetValue(targetId, out NamespaceMetadataState? target))
                    {
                        // an unresolved link can only be followed asynchronously.
                        return false;
                    }

                    if (target?.NamespaceUri?.Value == namespaceUri)
                    {
                        state = target;
                        break;
                    }
                }
            }

            if (state != null)
            {
                SubscribeToDefaultPermissions(state);
            }

            lock (m_lock)
            {
                if (state != null)
                {
                    m_statesByUri[namespaceUri] = state;
                    m_statesByIndex[namespaceIndex] = state;
                    m_missingByIndex.TryRemove(namespaceIndex, out _);
                }
                else
                {
                    m_missingByIndex[namespaceIndex] = (generation, pendingChanges);
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves every forward reference of <c>Server/Namespaces</c> into
        /// another node manager so <see cref="TryGetSnapshot"/> can answer
        /// synchronous lookups, and tracks the metadata nodes found.
        /// </summary>
        public async ValueTask PrimeReferencesAsync(CancellationToken cancellationToken = default)
        {
            if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
            {
                return;
            }

            ServerSystemContext context = m_host.SystemContext;
            IList<IReference> references = [];
            serverNamespacesNode.GetReferences(context, references);
            foreach (IReference reference in references)
            {
                if (reference.IsInverse || reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                {
                    continue;
                }

                var targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.Server.NamespaceUris);
                if (targetId.IsNull)
                {
                    continue;
                }

                NodeState? target;
                try
                {
                    target = await context.Server.NodeManager.FindNodeInAddressSpaceAsync(
                        targetId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_logger.ErrorSearchingNamespaceMetadata(ex, targetId.ToString());
                    continue;
                }

                if (target == null)
                {
                    // not resolvable yet, e.g. the owning node manager is still starting.
                    m_referenceTargets.TryRemove(targetId, out _);
                    continue;
                }

                var metadata = target as NamespaceMetadataState;
                if (metadata != null)
                {
                    SubscribeToDefaultPermissions(metadata);
                }

                m_referenceTargets[targetId] = metadata;
            }
        }

        /// <summary>
        /// Whether a remembered miss is still valid: it was recorded in the current
        /// generation, and no change of <c>Server/Namespaces</c> is pending now that
        /// was not pending when the miss was recorded.
        /// </summary>
        /// <typeparam name="TKey">The namespace URI or index.</typeparam>
        private static bool IsCachedMiss<TKey>(
            ConcurrentDictionary<TKey, (long Generation, NodeStateChangeMasks PendingChanges)> missing,
            TKey key,
            long generation,
            NodeStateChangeMasks pendingChanges)
            where TKey : notnull
        {
            return missing.TryGetValue(
                    key,
                    out (long Generation, NodeStateChangeMasks PendingChanges) entry) &&
                entry.Generation == generation &&
                (pendingChanges & ~entry.PendingChanges) == 0;
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
                Interlocked.Increment(ref m_generation);
                m_statesByUri.Clear();
                m_statesByIndex.Clear();
                m_missingByUri.Clear();
                m_missingByIndex.Clear();
                m_referenceTargets.Clear();
            }

            foreach (NamespaceMetadataState metadataState in tracked)
            {
                metadataState.StateChanged -= OnNamespaceChildrenChanged;
                UnsubscribeDefaultProperties(metadataState);
            }

            NamespaceDefaultPermissionReader.Invalidate();
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
                        NodeState? targetNode = await context.Server.NodeManager.FindNodeInAddressSpaceAsync(
                            nameSpaceNodeId, cancellationToken).ConfigureAwait(false);
                        if (targetNode is not NamespaceMetadataState namespaceMetadata)
                        {
                            // only a resolved node is known not to be metadata; an unresolved
                            // target keeps the synchronous lookup on the asynchronous path.
                            if (targetNode != null)
                            {
                                m_referenceTargets[nameSpaceNodeId] = null;
                            }

                            continue;
                        }

                        m_referenceTargets[nameSpaceNodeId] = namespaceMetadata;

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
        /// Drops every cached lookup result. The host calls it when it
        /// changes the references of <c>Server/Namespaces</c> without a
        /// <c>StateChanged</c> notification, e.g. when another node manager
        /// links its <see cref="NamespaceMetadataState"/>.
        /// </summary>
        public void Invalidate()
        {
            lock (m_lock)
            {
                // The generation moves first, so a lookup that still finds a miss
                // that is about to be cleared compares it against the new generation.
                Interlocked.Increment(ref m_generation);
                m_statesByUri.Clear();
                m_statesByIndex.Clear();
                m_missingByUri.Clear();
                m_missingByIndex.Clear();
            }

            // a linked, relinked or removed metadata node changes the defaults of its namespace.
            NamespaceDefaultPermissionReader.Invalidate();
        }

        /// <summary>
        /// Returns the child and reference changes of <c>Server/Namespaces</c>
        /// that have not been announced through <c>StateChanged</c> yet. A
        /// cached miss is only valid while no new such change appears.
        /// </summary>
        private NodeStateChangeMasks GetPendingNamespacesChanges()
        {
            if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
            {
                return NodeStateChangeMasks.None;
            }

            return serverNamespacesNode.ChangeMasks &
                (NodeStateChangeMasks.Children | NodeStateChangeMasks.References);
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
                    Invalidate();

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
        /// Subscribes to the <c>StateChanged</c> events of the <c>DefaultAccessRestrictions</c>,
        /// <c>DefaultRolePermissions</c> and <c>DefaultUserRolePermissions</c> child nodes of a
        /// <see cref="NamespaceMetadataState"/> to detect changes that require permission cache
        /// invalidation, and remembers the node and properties so a replaced property and
        /// <see cref="Detach"/> release their handlers.
        /// </summary>
        private void SubscribeToDefaultPermissions(NamespaceMetadataState namespaceMetadataState)
        {
            BaseVariableState?[] properties =
            [
                namespaceMetadataState.DefaultAccessRestrictions,
                namespaceMetadataState.DefaultRolePermissions,
                namespaceMetadataState.DefaultUserRolePermissions
            ];

            BaseVariableState?[]? previous;
            lock (m_lock)
            {
                m_tracked.Add(namespaceMetadataState);
                m_trackedProperties.TryGetValue(namespaceMetadataState, out previous);
                m_trackedProperties[namespaceMetadataState] = properties;
            }

            if (previous != null)
            {
                foreach (BaseVariableState? property in previous)
                {
                    if (property != null && Array.IndexOf(properties, property) < 0)
                    {
                        property.StateChanged -= OnDefaultPermissionsChanged;
                    }
                }
            }

            foreach (BaseVariableState? property in properties)
            {
                if (property != null)
                {
                    // unsubscribe first to avoid duplicate subscriptions if called multiple times
                    property.StateChanged -= OnDefaultPermissionsChanged;
                    property.StateChanged += OnDefaultPermissionsChanged;
                }
            }

            namespaceMetadataState.StateChanged -= OnNamespaceChildrenChanged;
            namespaceMetadataState.StateChanged += OnNamespaceChildrenChanged;
        }

        /// <summary>
        /// Releases the handlers of the default properties a metadata node had when last subscribed.
        /// </summary>
        private void UnsubscribeDefaultProperties(NamespaceMetadataState namespaceMetadataState)
        {
            BaseVariableState?[]? previous;
            lock (m_lock)
            {
                m_trackedProperties.TryGetValue(namespaceMetadataState, out previous);
                m_trackedProperties.Remove(namespaceMetadataState);
            }

            BaseVariableState?[] properties = previous ??
            [
                namespaceMetadataState.DefaultAccessRestrictions,
                namespaceMetadataState.DefaultRolePermissions,
                namespaceMetadataState.DefaultUserRolePermissions
            ];
            foreach (BaseVariableState? property in properties)
            {
                property?.StateChanged -= OnDefaultPermissionsChanged;
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

                // an added, replaced or removed default property changes the defaults.
                NamespaceDefaultPermissionReader.Invalidate();
                DefaultPermissionsChanged?.Invoke(m_host, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Handles value changes on <c>DefaultAccessRestrictions</c>, <c>DefaultRolePermissions</c>
        /// or <c>DefaultUserRolePermissions</c>, invalidates the namespace defaults remembered by
        /// operation contexts and raises the <see cref="DefaultPermissionsChanged"/> event with the
        /// host as sender.
        /// </summary>
        private void OnDefaultPermissionsChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Value) != 0)
            {
                NamespaceDefaultPermissionReader.Invalidate();
                DefaultPermissionsChanged?.Invoke(m_host, EventArgs.Empty);
            }
        }

        private readonly INamespaceMetadataHost m_host;
        private readonly ILogger m_logger;
        // Read without the lock; filled and cleared under it.
        private readonly ConcurrentDictionary<string, NamespaceMetadataState> m_statesByUri = new();
        private readonly ConcurrentDictionary<ushort, NamespaceMetadataState> m_statesByIndex = new();
        private readonly ConcurrentDictionary<string, (long Generation, NodeStateChangeMasks PendingChanges)> m_missingByUri = new();
        private readonly ConcurrentDictionary<ushort, (long Generation, NodeStateChangeMasks PendingChanges)> m_missingByIndex = new();
        private long m_generation;
        private readonly ConcurrentDictionary<NodeId, NamespaceMetadataState?> m_referenceTargets = new();
        private readonly HashSet<NamespaceMetadataState> m_tracked = [];
        private readonly Dictionary<NamespaceMetadataState, BaseVariableState?[]> m_trackedProperties = [];
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
