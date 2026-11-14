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
    /// configuration node manager. An existing Object whose type definition
    /// is <c>NamespaceMetadataType</c> or a subtype is recognized as metadata
    /// even when it is not a <see cref="NamespaceMetadataState"/>, e.g. when
    /// an untyped NodeSet import created it as <see cref="BaseObjectState"/>;
    /// its properties are resolved by BrowseName (<see cref="NamespaceMetadataNode"/>).
    /// The registry provides lookup by URI or index with a cache that
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
        /// Gets the <see cref="NamespaceMetadataState"/> for
        /// <paramref name="namespaceUri"/>, or <c>null</c> when none exists or
        /// when the namespace is described by an untyped metadata Object.
        /// </summary>
        public async ValueTask<NamespaceMetadataState?> GetAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            NamespaceMetadataNode? metadata = await GetNodeAsync(namespaceUri, cancellationToken)
                .ConfigureAwait(false);
            return metadata?.TypedState;
        }

        /// <summary>
        /// Gets the <see cref="NamespaceMetadataState"/> for
        /// <paramref name="namespaceIndex"/>, or <c>null</c> when none exists
        /// or when the namespace is described by an untyped metadata Object.
        /// </summary>
        public async ValueTask<NamespaceMetadataState?> GetAsync(
            ushort namespaceIndex,
            CancellationToken cancellationToken = default)
        {
            NamespaceMetadataNode? metadata = await GetNodeAsync(namespaceIndex, cancellationToken)
                .ConfigureAwait(false);
            return metadata?.TypedState;
        }

        /// <summary>
        /// Gets the metadata node for <paramref name="namespaceUri"/>, or
        /// <c>null</c> when none exists. Results are cached until the
        /// <c>Namespaces</c> object changes; an incomplete untyped view
        /// (<see cref="NamespaceMetadataNode.IsIncomplete"/>) is never cached.
        /// </summary>
        /// <remarks>
        /// Permission checks call this for every request. Cached results,
        /// found and missing, are read without a lock; only filling and
        /// invalidating the cache take it. A result resolved while an
        /// invalidation ran is resolved again.
        /// </remarks>
        public async ValueTask<NamespaceMetadataNode?> GetNodeAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            NamespaceMetadataNode? namespaceMetadataState = null;
            for (int attempt = 0; attempt < kMaxResolveAttempts; attempt++)
            {
                // untyped metadata retired by an invalidation is resolved again first, also when
                // the result is cached, so that synchronous lookups answer from refreshed views.
                await PrimeIfRequiredAsync(cancellationToken).ConfigureAwait(false);

                if (m_statesByUri.TryGetValue(namespaceUri, out NamespaceMetadataNode? value))
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

                namespaceMetadataState = await FindAsync(
                    namespaceUri, generation, cancellationToken).ConfigureAwait(false);

                // metadata linked by reference from another node manager is tracked
                // as well, so its default changes invalidate permission caches.
                if (namespaceMetadataState != null)
                {
                    SubscribeToDefaultPermissions(namespaceMetadataState);
                }

                lock (m_lock)
                {
                    // a result resolved before an invalidation can miss the change it
                    // announced, so it is never remembered and resolved again.
                    if (Interlocked.Read(ref m_generation) != generation)
                    {
                        continue;
                    }

                    if (namespaceMetadataState == null)
                    {
                        m_missingByUri[namespaceUri] = (generation, pendingChanges);
                    }
                    else if (!namespaceMetadataState.IsIncomplete)
                    {
                        m_statesByUri[namespaceUri] = namespaceMetadataState;
                        m_missingByUri.TryRemove(namespaceUri, out _);
                    }
                }

                return namespaceMetadataState;
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
        public async ValueTask<NamespaceMetadataNode?> GetNodeAsync(
            ushort namespaceIndex,
            CancellationToken cancellationToken = default)
        {
            NamespaceMetadataNode? namespaceMetadataState = null;
            for (int attempt = 0; attempt < kMaxResolveAttempts; attempt++)
            {
                await PrimeIfRequiredAsync(cancellationToken).ConfigureAwait(false);

                if (m_statesByIndex.TryGetValue(namespaceIndex, out NamespaceMetadataNode? value))
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
                namespaceMetadataState = await GetNodeAsync(
                    namespaceUri!, cancellationToken).ConfigureAwait(false);

                lock (m_lock)
                {
                    if (Interlocked.Read(ref m_generation) != generation)
                    {
                        continue;
                    }

                    if (namespaceMetadataState == null)
                    {
                        m_missingByIndex[namespaceIndex] = (generation, pendingChanges);
                    }
                    else if (!namespaceMetadataState.IsIncomplete)
                    {
                        m_statesByIndex[namespaceIndex] = namespaceMetadataState;
                        m_missingByIndex.TryRemove(namespaceIndex, out _);
                    }
                }

                return namespaceMetadataState;
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
        /// <c>Server/Namespaces</c>, or an untyped metadata Object whose
        /// properties are not resolved yet, could hold the metadata node, or
        /// when an invalidation ran during the lookup.
        /// </returns>
        public bool TryGetSnapshot(ushort namespaceIndex, out NamespaceMetadataNode? state)
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
                if (!TryResolve(context, child, out NamespaceMetadataNode? metadata))
                {
                    if (IsOtherNamespace(context, child, namespaceUri))
                    {
                        continue;
                    }

                    // the properties of an untyped metadata node are resolved asynchronously.
                    state = null;
                    return false;
                }

                if (metadata != null && metadata.NamespaceUri == namespaceUri)
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
                    if (targetId.IsNull)
                    {
                        return false;
                    }

                    if (!m_referenceTargets.TryGetValue(targetId, out NamespaceMetadataNode? target))
                    {
                        if (m_stale.TryGetValue(targetId, out NamespaceMetadataNode? retired) &&
                            retired.NamespaceUri is string retiredUri &&
                            retiredUri != namespaceUri)
                        {
                            // retired metadata of another namespace.
                            continue;
                        }

                        // an unresolved link can only be followed asynchronously.
                        return false;
                    }

                    if (target != null && target.NamespaceUri == namespaceUri)
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
                // a result found while an invalidation ran may be built from retired views.
                if (Interlocked.Read(ref m_generation) != generation)
                {
                    state = null;
                    return false;
                }

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
        /// another node manager, and the properties of every untyped metadata
        /// child, so <see cref="TryGetSnapshot"/> can answer synchronous
        /// lookups, and tracks the metadata nodes found. The next asynchronous
        /// lookup after <see cref="Invalidate"/>, or after a prime that did not
        /// complete, primes again.
        /// </summary>
        public async ValueTask PrimeReferencesAsync(CancellationToken cancellationToken = default)
        {
            // the flag is cleared before the generation is read, so an invalidation that
            // is missed by this prime sets it again.
            Volatile.Write(ref m_primeRequired, 0);
            long generation = Interlocked.Read(ref m_generation);
            try
            {
                await PrimeAsync(generation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Volatile.Write(ref m_primeRequired, 1);
                throw;
            }
        }

        /// <summary>
        /// Runs a pending prime (see <see cref="PrimeReferencesAsync"/>) without
        /// any cost when none is pending.
        /// </summary>
        private ValueTask PrimeIfRequiredAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref m_primeRequired) == 1 &&
                Interlocked.CompareExchange(ref m_primeRequired, 0, 1) == 1)
            {
                return PrimeReferencesAsync(cancellationToken);
            }

            return default;
        }

        /// <summary>
        /// Resolves the reference targets and untyped children of <c>Server/Namespaces</c>
        /// and publishes the views found while the registry is still in <paramref name="generation"/>.
        /// </summary>
        private async ValueTask PrimeAsync(long generation, CancellationToken cancellationToken)
        {
            if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
            {
                return;
            }

            ServerSystemContext context = m_host.SystemContext;
            IList<BaseInstanceState> children = [];
            serverNamespacesNode.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is NamespaceMetadataState || TryResolve(context, child, out _))
                {
                    continue;
                }

                try
                {
                    NamespaceMetadataNode? metadata = await ResolveAsync(
                        context, child, generation, cancellationToken).ConfigureAwait(false);
                    if (metadata != null)
                    {
                        SubscribeToDefaultPermissions(metadata);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_logger.ErrorSearchingNamespaceMetadata(ex, child.NodeId.ToString());
                }
            }

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
                NamespaceMetadataNode? metadata;
                try
                {
                    target = await context.Server.NodeManager.FindNodeInAddressSpaceAsync(
                        targetId, cancellationToken).ConfigureAwait(false);
                    metadata = target == null
                        ? null
                        : await ResolveAsync(context, target, generation, cancellationToken).ConfigureAwait(false);
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

                if (metadata != null)
                {
                    SubscribeToDefaultPermissions(metadata);
                }

                PublishReferenceTarget(targetId, target, metadata, generation);
            }
        }

        /// <summary>
        /// Remembers the metadata view of a resolved reference target of <c>Server/Namespaces</c>
        /// while the registry is still in <paramref name="generation"/>. A target that is not
        /// resolvable yet, e.g. because its node manager is still starting, or whose view is
        /// incomplete is forgotten, so synchronous lookups wait for an asynchronous one.
        /// </summary>
        private void PublishReferenceTarget(
            NodeId targetId,
            NodeState? target,
            NamespaceMetadataNode? metadata,
            long generation)
        {
            lock (m_lock)
            {
                if (Interlocked.Read(ref m_generation) != generation)
                {
                    return;
                }

                if (target == null || metadata is { IsIncomplete: true })
                {
                    m_referenceTargets.TryRemove(targetId, out _);
                }
                else
                {
                    m_referenceTargets[targetId] = metadata;
                }
            }
        }

        /// <summary>
        /// Remembers the view of an untyped metadata Object while the registry is still in
        /// <paramref name="generation"/>. An incomplete view is only remembered as retired,
        /// so synchronous lookups of other namespaces can skip the Object while lookups of
        /// its own namespace wait until its properties are resolved.
        /// </summary>
        private void PublishUntyped(BaseObjectState node, NamespaceMetadataNode metadata, long generation)
        {
            lock (m_lock)
            {
                if (Interlocked.Read(ref m_generation) != generation)
                {
                    return;
                }

                if (metadata.IsIncomplete)
                {
                    m_stale[node.NodeId] = metadata;
                }
                else
                {
                    m_untyped[node] = metadata;
                    m_stale.TryRemove(node.NodeId, out _);
                }
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
        /// <returns>
        /// The <see cref="NamespaceMetadataState"/>, or <c>null</c> when it
        /// cannot be created or when the namespace is already described by an
        /// untyped metadata Object, which is kept and never duplicated.
        /// </returns>
        public async ValueTask<NamespaceMetadataState?> CreateAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            NamespaceMetadataNode? metadata = await GetOrCreateNodeAsync(namespaceUri, cancellationToken)
                .ConfigureAwait(false);
            if (metadata != null && metadata.TypedState == null)
            {
                m_logger.UntypedNamespaceMetadataKept(namespaceUri, metadata.Node.NodeId);
            }

            return metadata?.TypedState;
        }

        /// <summary>
        /// Returns the metadata node for <paramref name="namespaceUri"/>, typed
        /// or untyped, creating and registering a
        /// <see cref="NamespaceMetadataState"/> under <c>Server/Namespaces</c>
        /// only when no metadata node exists yet, and subscribes to its
        /// default-permission properties.
        /// </summary>
        /// <returns>
        /// The metadata node, or <c>null</c> when it cannot be created.
        /// </returns>
        public async ValueTask<NamespaceMetadataNode?> GetOrCreateNodeAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            NamespaceMetadataNode? existing = await FindAsync(
                namespaceUri, Interlocked.Read(ref m_generation), cancellationToken).ConfigureAwait(false);

            if (existing != null)
            {
                // Subscribe to the default permission properties so that any future changes
                // trigger a DefaultPermissionsChanged notification to allow caches to be invalidated.
                // An incomplete untyped Object is existing metadata as well and never duplicated.
                SubscribeToDefaultPermissions(existing);
                return existing;
            }

            // find ServerNamespaces node
            if (m_host.FindServerNamespacesNode() is not NamespacesState serverNamespacesNode)
            {
                m_logger.CannotCreateNamespaceMetadataState(namespaceUri);
                return null;
            }

            ServerSystemContext context = m_host.SystemContext;

            // create the NamespaceMetadata node
            NamespaceMetadataState namespaceMetadataState = context.CreateInstanceOfNamespaceMetadataType(
                serverNamespacesNode,
                new QualifiedName(namespaceUri, m_host.NamespaceIndex));
            namespaceMetadataState.NodeId = context.NodeIdFactory.New(context, namespaceMetadataState);
            namespaceMetadataState.DisplayName = LocalizedText.From(namespaceUri);
            namespaceMetadataState.SymbolicName = namespaceUri;
            namespaceMetadataState.NamespaceUri!.Value = namespaceUri;
            namespaceMetadataState.AddDefaultRolePermissions(context)
                .AddDefaultUserRolePermissions(context);

            // add node as child of ServerNamespaces and in predefined nodes
            serverNamespacesNode.AddChild(namespaceMetadataState);
            await serverNamespacesNode.ClearChangeMasksAsync(context, true, cancellationToken)
                .ConfigureAwait(false);
            await m_host.AddPredefinedNodeAsync(namespaceMetadataState, cancellationToken)
                .ConfigureAwait(false);

            NamespaceMetadataNode created = NamespaceMetadataNode.From(namespaceMetadataState)!;
            SubscribeToDefaultPermissions(created);

            return created;
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
                if (TryResolve(context, child, out NamespaceMetadataNode? metadata) && metadata != null)
                {
                    SubscribeToDefaultPermissions(metadata);
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

            BaseObjectState[] tracked;
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
                m_untyped.Clear();
                m_stale.Clear();
            }

            foreach (BaseObjectState metadataNode in tracked)
            {
                metadataNode.StateChanged -= OnNamespaceChildrenChanged;
                UnsubscribeDefaultProperties(metadataNode);
            }

            NamespaceDefaultPermissionReader.Invalidate();
        }

        /// <summary>
        /// Finds the metadata node for <paramref name="namespaceUri"/> among
        /// the children and forward references of <c>Server/Namespaces</c>,
        /// resolving references into other node managers. The views found are
        /// remembered only while the registry is still in <paramref name="generation"/>.
        /// </summary>
        private async ValueTask<NamespaceMetadataNode?> FindAsync(
            string namespaceUri,
            long generation,
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
                    NamespaceMetadataNode? namespaceMetadata = await ResolveAsync(
                        context, namespacesReference, generation, cancellationToken).ConfigureAwait(false);
                    if (namespaceMetadata != null && namespaceMetadata.NamespaceUri == namespaceUri)
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
                        if (targetNode == null)
                        {
                            // only a resolved node is known not to be metadata; an unresolved
                            // target keeps the synchronous lookup on the asynchronous path.
                            PublishReferenceTarget(nameSpaceNodeId, null, null, generation);
                            continue;
                        }

                        NamespaceMetadataNode? namespaceMetadata = await ResolveAsync(
                            context, targetNode, generation, cancellationToken).ConfigureAwait(false);
                        PublishReferenceTarget(nameSpaceNodeId, targetNode, namespaceMetadata, generation);

                        if (namespaceMetadata != null && namespaceMetadata.NamespaceUri == namespaceUri)
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
        /// links its <see cref="NamespaceMetadataState"/>. The captured views
        /// of untyped metadata nodes are retired as well: synchronous lookups
        /// that could match them answer only after the next asynchronous lookup
        /// or <see cref="PrimeReferencesAsync"/> resolved their properties again.
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

                // The properties of an untyped node are captured when it is resolved, so its
                // view is retired until an asynchronous lookup resolves it again.
                foreach (KeyValuePair<BaseObjectState, NamespaceMetadataNode> entry in m_untyped.ToArray())
                {
                    m_stale[entry.Key.NodeId] = entry.Value;
                }

                m_untyped.Clear();
                foreach (KeyValuePair<NodeId, NamespaceMetadataNode?> entry in m_referenceTargets.ToArray())
                {
                    if (entry.Value is NamespaceMetadataNode { TypedState: null } untyped &&
                        ((ICollection<KeyValuePair<NodeId, NamespaceMetadataNode?>>)m_referenceTargets).Remove(entry))
                    {
                        m_stale[entry.Key] = untyped;
                    }
                }

                Volatile.Write(ref m_primeRequired, 1);
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
                            if (TryResolve(context, child, out NamespaceMetadataNode? metadata) && metadata != null)
                            {
                                SubscribeToDefaultPermissions(metadata);
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
        /// <c>DefaultRolePermissions</c> and <c>DefaultUserRolePermissions</c> properties of a
        /// metadata node to detect changes that require permission cache invalidation, and
        /// remembers the node and properties so a replaced property and <see cref="Detach"/>
        /// release their handlers.
        /// </summary>
        private void SubscribeToDefaultPermissions(NamespaceMetadataNode metadata)
        {
            BaseObjectState metadataNode = metadata.Node;
            BaseVariableState?[] properties =
            [
                metadata.DefaultAccessRestrictions,
                metadata.DefaultRolePermissions,
                metadata.DefaultUserRolePermissions
            ];

            BaseVariableState?[]? previous;
            lock (m_lock)
            {
                m_tracked.Add(metadataNode);
                m_trackedProperties.TryGetValue(metadataNode, out previous);
                m_trackedProperties[metadataNode] = properties;
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

            metadataNode.StateChanged -= OnNamespaceChildrenChanged;
            metadataNode.StateChanged += OnNamespaceChildrenChanged;
        }

        /// <summary>
        /// Releases the handlers of the default properties a metadata node had when last subscribed.
        /// </summary>
        private void UnsubscribeDefaultProperties(BaseObjectState metadataNode)
        {
            BaseVariableState?[]? previous;
            lock (m_lock)
            {
                m_trackedProperties.TryGetValue(metadataNode, out previous);
                m_trackedProperties.Remove(metadataNode);
            }

            BaseVariableState?[] properties = previous ??
                (metadataNode is NamespaceMetadataState namespaceMetadataState
                    ?
                    [
                        namespaceMetadataState.DefaultAccessRestrictions,
                        namespaceMetadataState.DefaultRolePermissions,
                        namespaceMetadataState.DefaultUserRolePermissions
                    ]
                    : []);
            foreach (BaseVariableState? property in properties)
            {
                property?.StateChanged -= OnDefaultPermissionsChanged;
            }
        }

        /// <summary>
        /// Handles children change on a metadata node and resubscribes to the default permissions nodes
        /// to ensure we are notified of changes on those nodes even if they are recreated.
        /// </summary>
        private void OnNamespaceChildrenChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) == 0 || node is not BaseObjectState metadataNode)
            {
                return;
            }

            if (metadataNode is NamespaceMetadataState namespaceMetadataState)
            {
                SubscribeToDefaultPermissions(NamespaceMetadataNode.From(namespaceMetadataState)!);
            }
            else
            {
                // The properties of an untyped node are resolved when it is discovered,
                // so every lookup that resolved it is dropped and it is resolved again.
                Invalidate();
                if (TryResolve(context, metadataNode, out NamespaceMetadataNode? metadata) && metadata != null)
                {
                    SubscribeToDefaultPermissions(metadata);
                }
            }

            // an added, replaced or removed default property changes the defaults.
            NamespaceDefaultPermissionReader.Invalidate();
            DefaultPermissionsChanged?.Invoke(m_host, EventArgs.Empty);
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

        /// <summary>
        /// Classifies <paramref name="node"/> without awaiting.
        /// </summary>
        /// <param name="context">The context used to enumerate the node.</param>
        /// <param name="node">The child or reference target of <c>Server/Namespaces</c>.</param>
        /// <param name="metadata">
        /// The metadata view of the node, or <c>null</c> when the node is not
        /// namespace metadata.
        /// </param>
        /// <returns>
        /// <c>false</c> when the node is an untyped metadata Object whose
        /// properties are only reachable through references to other nodes,
        /// which are resolved by <see cref="ResolveAsync"/>.
        /// </returns>
        private bool TryResolve(ISystemContext context, NodeState node, out NamespaceMetadataNode? metadata)
        {
            if (node is NamespaceMetadataState namespaceMetadataState)
            {
                metadata = NamespaceMetadataNode.From(namespaceMetadataState);
                return true;
            }

            metadata = null;
            if (node is not BaseObjectState objectNode ||
                !NamespaceMetadataNode.IsNamespaceMetadataType(context, objectNode.TypeDefinitionId))
            {
                return true;
            }

            if (m_untyped.TryGetValue(objectNode, out metadata))
            {
                return true;
            }

            long generation = Interlocked.Read(ref m_generation);
            BaseVariableState?[] properties = FindPropertyChildren(context, objectNode);
            if (Array.IndexOf(properties, null) >= 0 && HasUnlinkedPropertyReferences(context, objectNode))
            {
                return false;
            }

            metadata = CreateUntyped(objectNode, properties, isIncomplete: false);
            PublishUntyped(objectNode, metadata, generation);
            return true;
        }

        /// <summary>
        /// Returns the metadata view of <paramref name="node"/>, or <c>null</c>
        /// when the node is not namespace metadata. The properties of an untyped
        /// metadata Object are its children with the property BrowseName or,
        /// when it has no such child, the targets of its forward
        /// <c>HasProperty</c> references, e.g. when a NodeSet was imported
        /// without linking its children. A target that cannot be resolved while
        /// a default property is missing makes the view incomplete
        /// (<see cref="NamespaceMetadataNode.IsIncomplete"/>). The view is
        /// remembered only while the registry is still in <paramref name="generation"/>.
        /// </summary>
        private async ValueTask<NamespaceMetadataNode?> ResolveAsync(
            ServerSystemContext context,
            NodeState node,
            long generation,
            CancellationToken cancellationToken)
        {
            if (node is NamespaceMetadataState namespaceMetadataState)
            {
                return NamespaceMetadataNode.From(namespaceMetadataState);
            }

            if (node is not BaseObjectState objectNode ||
                !NamespaceMetadataNode.IsNamespaceMetadataType(context, objectNode.TypeDefinitionId))
            {
                return null;
            }

            BaseVariableState?[] properties = FindPropertyChildren(context, objectNode);
            bool unresolved = false;
            if (Array.IndexOf(properties, null) >= 0)
            {
                IList<IReference> references = [];
                objectNode.GetReferences(context, references);
                foreach (IReference reference in references)
                {
                    if (reference.IsInverse || reference.ReferenceTypeId != ReferenceTypeIds.HasProperty)
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
                        target = null;
                    }

                    if (target == null)
                    {
                        // e.g. the owning node manager is still starting.
                        unresolved = true;
                        continue;
                    }

                    if (target is not BaseVariableState property)
                    {
                        continue;
                    }

                    int index = Array.IndexOf(s_propertyNames, property.BrowseName);
                    if (index >= 0 && properties[index] == null)
                    {
                        properties[index] = property;
                    }
                }
            }

            // the BrowseName of an unresolved target is unknown, so it may be one of the missing
            // defaults, which follow the NamespaceUri in s_propertyNames.
            bool isIncomplete = unresolved && Array.IndexOf(properties, null, 1) >= 0;
            NamespaceMetadataNode metadata = CreateUntyped(objectNode, properties, isIncomplete);
            PublishUntyped(objectNode, metadata, generation);
            return metadata;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="node"/>, an untyped metadata
        /// Object that is not resolved yet, is known to describe another namespace
        /// than <paramref name="namespaceUri"/>: from its <c>NamespaceUri</c> child
        /// or the view retired by the last invalidation.
        /// </summary>
        private bool IsOtherNamespace(ISystemContext context, NodeState node, string namespaceUri)
        {
            string? candidateUri = null;
            if (node.FindChildWithQualifiedName(context, s_propertyNames[0]) is BaseVariableState property)
            {
                if (property.Value.TryGetValue(out string? value))
                {
                    candidateUri = value;
                }
            }
            else if (m_stale.TryGetValue(node.NodeId, out NamespaceMetadataNode? retired))
            {
                candidateUri = retired.NamespaceUri;
            }

            return candidateUri != null && candidateUri != namespaceUri;
        }

        /// <summary>
        /// Returns the children of an untyped metadata Object named like the
        /// properties in <see cref="s_propertyNames"/>, in that order.
        /// </summary>
        private static BaseVariableState?[] FindPropertyChildren(ISystemContext context, BaseObjectState node)
        {
            var properties = new BaseVariableState?[s_propertyNames.Length];
            for (int ii = 0; ii < s_propertyNames.Length; ii++)
            {
                properties[ii] = node.FindChildWithQualifiedName(context, s_propertyNames[ii]) as BaseVariableState;
            }

            return properties;
        }

        /// <summary>
        /// Returns <c>true</c> when the node has forward <c>HasProperty</c>
        /// references to nodes that are not its children, whose targets can
        /// only be resolved asynchronously.
        /// </summary>
        private static bool HasUnlinkedPropertyReferences(ISystemContext context, BaseObjectState node)
        {
            IList<IReference> references = [];
            node.GetReferences(context, references);
            HashSet<NodeId>? children = null;
            foreach (IReference reference in references)
            {
                if (reference.IsInverse || reference.ReferenceTypeId != ReferenceTypeIds.HasProperty)
                {
                    continue;
                }

                if (children == null)
                {
                    IList<BaseInstanceState> childNodes = [];
                    node.GetChildren(context, childNodes);
                    children = [];
                    foreach (BaseInstanceState child in childNodes)
                    {
                        children.Add(child.NodeId);
                    }
                }

                var targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                if (targetId.IsNull || !children.Contains(targetId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates the view of an untyped metadata Object from the properties
        /// found in the order of <see cref="s_propertyNames"/>.
        /// </summary>
        private static NamespaceMetadataNode CreateUntyped(
            BaseObjectState node,
            BaseVariableState?[] properties,
            bool isIncomplete)
        {
            return new NamespaceMetadataNode(
                node, properties[0], properties[1], properties[2], properties[3], isIncomplete);
        }

        /// <summary>
        /// How often a lookup is resolved again when an invalidation retired its result.
        /// </summary>
        private const int kMaxResolveAttempts = 3;

        private readonly INamespaceMetadataHost m_host;
        private readonly ILogger m_logger;
        // Read without the lock; filled and cleared under it.
        private readonly ConcurrentDictionary<string, NamespaceMetadataNode> m_statesByUri = new();
        private readonly ConcurrentDictionary<ushort, NamespaceMetadataNode> m_statesByIndex = new();
        private readonly ConcurrentDictionary<string, (long Generation, NodeStateChangeMasks PendingChanges)> m_missingByUri = new();
        private readonly ConcurrentDictionary<ushort, (long Generation, NodeStateChangeMasks PendingChanges)> m_missingByIndex = new();
        private long m_generation;
        private readonly ConcurrentDictionary<NodeId, NamespaceMetadataNode?> m_referenceTargets = new();
        private readonly ConcurrentDictionary<BaseObjectState, NamespaceMetadataNode> m_untyped = new();
        // Untyped views retired by Invalidate or resolved incomplete, keyed by node id; their live
        // NamespaceUri lets a synchronous lookup skip metadata of other namespaces until they are
        // resolved again.
        private readonly ConcurrentDictionary<NodeId, NamespaceMetadataNode> m_stale = new();
        private int m_primeRequired;
        private readonly HashSet<BaseObjectState> m_tracked = [];
        private readonly Dictionary<BaseObjectState, BaseVariableState?[]> m_trackedProperties = [];
        private readonly Lock m_lock = new();

        /// <summary>
        /// The BrowseNames of the <c>NamespaceUri</c>, <c>DefaultAccessRestrictions</c>,
        /// <c>DefaultRolePermissions</c> and <c>DefaultUserRolePermissions</c> properties.
        /// </summary>
        private static readonly QualifiedName[] s_propertyNames =
        [
            new QualifiedName(BrowseNames.NamespaceUri),
            new QualifiedName(BrowseNames.DefaultAccessRestrictions),
            new QualifiedName(BrowseNames.DefaultRolePermissions),
            new QualifiedName(BrowseNames.DefaultUserRolePermissions)
        ];
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

        [LoggerMessage(EventId = ServerEventIds.NamespaceMetadataRegistry + 3, Level = LogLevel.Debug,
            Message = "Namespace '{NamespaceUri}' is described by the untyped NamespaceMetadataType " +
                "object {NodeId}; no NamespaceMetadataState is created.")]
        public static partial void UntypedNamespaceMetadataKept(
            this ILogger logger,
            string namespaceUri,
            NodeId nodeId);
    }
}
