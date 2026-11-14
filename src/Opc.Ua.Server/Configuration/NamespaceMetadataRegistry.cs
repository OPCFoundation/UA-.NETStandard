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
        /// While an untyped metadata Object's <c>NamespaceUri</c> cannot be
        /// resolved, a namespace without other metadata gets that Object's
        /// incomplete view, as the Object may describe it, unless the Object's
        /// BrowseName names another namespace of the server.
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
                long missEpoch = Interlocked.Read(ref m_missEpoch);
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
                    SubscribeToDefaultPermissions(namespaceMetadataState, generation);
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
                        if (Interlocked.Read(ref m_missEpoch) == missEpoch)
                        {
                            m_missingByUri[namespaceUri] = (generation, pendingChanges);
                        }
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
                long missEpoch = Interlocked.Read(ref m_missEpoch);
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
                        if (Interlocked.Read(ref m_missEpoch) == missEpoch)
                        {
                            m_missingByIndex[namespaceIndex] = (generation, pendingChanges);
                        }
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
        /// known; <c>false</c> when no exact match exists but an unresolved
        /// reference of <c>Server/Namespaces</c>, or an untyped metadata Object
        /// whose properties are not resolved yet, could hold the metadata node,
        /// or when an invalidation ran during the lookup. An exact match is
        /// returned even when such an entry precedes it.
        /// </returns>
        public bool TryGetSnapshot(ushort namespaceIndex, out NamespaceMetadataNode? state)
        {
            if (m_statesByIndex.TryGetValue(namespaceIndex, out state))
            {
                return true;
            }

            NodeStateChangeMasks pendingChanges = GetPendingNamespacesChanges();
            long generation = Interlocked.Read(ref m_generation);
            long missEpoch = Interlocked.Read(ref m_missEpoch);
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

            // an entry of unknown identity that needs an asynchronous lookup only decides the
            // result when no exact match follows it; one known to describe the namespace decides
            // it at once, as the asynchronous lookup returns it first (see FindAsync).
            bool indeterminate = false;
            ServerSystemContext context = m_host.SystemContext;
            IList<BaseInstanceState> children = [];
            serverNamespacesNode.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (!TryResolve(context, child, out NamespaceMetadataNode? metadata))
                {
                    // the properties of an untyped metadata node are resolved asynchronously.
                    m_stale.TryGetValue(child.NodeId, out NamespaceMetadataNode? retiredChild);
                    UnresolvedEntry entry = ClassifyUnresolved(context, child, retiredChild, namespaceUri);
                    if (entry == UnresolvedEntry.SameNamespace)
                    {
                        state = null;
                        return false;
                    }

                    indeterminate |= entry == UnresolvedEntry.Unknown;
                    continue;
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
                        indeterminate = true;
                        continue;
                    }

                    if (!m_referenceTargets.TryGetValue(targetId, out NamespaceMetadataNode? target))
                    {
                        // an unresolved link can only be followed asynchronously; only its retired
                        // view, if any, tells which namespace it describes.
                        UnresolvedEntry entry = m_stale.TryGetValue(targetId, out NamespaceMetadataNode? retired)
                            ? ClassifyUnresolved(context, retired.Node, retired, namespaceUri)
                            : UnresolvedEntry.Unknown;
                        if (entry == UnresolvedEntry.SameNamespace)
                        {
                            state = null;
                            return false;
                        }

                        indeterminate |= entry == UnresolvedEntry.Unknown;
                        continue;
                    }

                    if (target != null && target.NamespaceUri == namespaceUri)
                    {
                        state = target;
                        break;
                    }
                }
            }

            if (state == null && indeterminate)
            {
                return false;
            }

            if (state != null)
            {
                SubscribeToDefaultPermissions(state, generation);
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
                else if (Interlocked.Read(ref m_missEpoch) == missEpoch)
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
        /// Runs a pending prime (see <see cref="PrimeReferencesAsync"/>), or resolves the
        /// untyped metadata Objects that were resolved incomplete again, without any cost
        /// beyond two volatile reads when neither is pending.
        /// </summary>
        private ValueTask PrimeIfRequiredAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref m_primeRequired) == 1 &&
                Interlocked.CompareExchange(ref m_primeRequired, 0, 1) == 1)
            {
                return PrimeReferencesAsync(cancellationToken);
            }

            if (Volatile.Read(ref m_incompleteCount) != 0)
            {
                return ResolveIncompleteAsync(cancellationToken);
            }

            return default;
        }

        /// <summary>
        /// Resolves the untyped metadata Objects whose views are incomplete again, so a
        /// property target that became available, e.g. because its node manager finished
        /// starting, is seen by synchronous lookups without waiting for an invalidation.
        /// </summary>
        /// <remarks>
        /// Only one resolution runs at a time; concurrent lookups skip it. A property
        /// reference that never resolves, e.g. a dangling reference of a malformed NodeSet,
        /// keeps its namespace failing closed and costs one targeted resolution of its
        /// Object per asynchronous lookup.
        /// </remarks>
        private async ValueTask ResolveIncompleteAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref m_resolvingIncomplete, 1, 0) != 0)
            {
                return;
            }

            try
            {
                long generation = Interlocked.Read(ref m_generation);
                KeyValuePair<NodeId, (BaseObjectState Node, bool IsReferenceTarget)>[] incomplete;
                lock (m_lock)
                {
                    incomplete = [.. m_incomplete];
                }

                ServerSystemContext context = m_host.SystemContext;
                foreach (KeyValuePair<NodeId, (BaseObjectState Node, bool IsReferenceTarget)> entry in incomplete)
                {
                    try
                    {
                        NamespaceMetadataNode? metadata = await ResolveAsync(
                            context, entry.Value.Node, generation, cancellationToken).ConfigureAwait(false);
                        if (metadata == null)
                        {
                            continue;
                        }

                        SubscribeToDefaultPermissions(metadata, generation);
                        if (entry.Value.IsReferenceTarget)
                        {
                            PublishReferenceTarget(entry.Key, entry.Value.Node, metadata, generation);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.ErrorSearchingNamespaceMetadata(ex, entry.Key.ToString());
                    }
                }
            }
            finally
            {
                Volatile.Write(ref m_resolvingIncomplete, 0);
            }
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
                        SubscribeToDefaultPermissions(metadata, generation);
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
                    SubscribeToDefaultPermissions(metadata, generation);
                }

                PublishReferenceTarget(targetId, target, metadata, generation);
            }
        }

        /// <summary>
        /// Remembers the metadata view of a resolved reference target of <c>Server/Namespaces</c>
        /// while the registry is still in <paramref name="generation"/>. A target that is not
        /// resolvable yet, e.g. because its node manager is still starting, or whose view is
        /// incomplete is forgotten, so synchronous lookups wait for an asynchronous one; an
        /// incomplete view is resolved again by the next asynchronous lookup.
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
                    if (metadata is { IsIncomplete: true })
                    {
                        m_incomplete[targetId] = (metadata.Node, true);
                        Volatile.Write(ref m_incompleteCount, m_incomplete.Count);
                    }
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
        /// its own namespace wait until its properties are resolved, and the Object is
        /// resolved again by the next asynchronous lookup (<see cref="ResolveIncompleteAsync"/>).
        /// </summary>
        private void PublishUntyped(BaseObjectState node, NamespaceMetadataNode metadata, long generation)
        {
            bool completed = false;
            lock (m_lock)
            {
                if (Interlocked.Read(ref m_generation) != generation)
                {
                    return;
                }

                if (metadata.IsIncomplete)
                {
                    m_stale[node.NodeId] = metadata;
                    if (!m_incomplete.ContainsKey(node.NodeId))
                    {
                        m_incomplete[node.NodeId] = (node, false);
                    }
                }
                else
                {
                    m_untyped[node] = metadata;
                    m_stale.TryRemove(node.NodeId, out _);
                    // an Object incomplete in this or an invalidated generation completes now.
                    completed = m_wasIncomplete.Remove(node.NodeId);
                    completed = m_incomplete.Remove(node.NodeId) || completed;
                    if (completed)
                    {
                        // a miss remembered while the Object was incomplete relied on its
                        // BrowseName hint, which its now known NamespaceUri may contradict.
                        Interlocked.Increment(ref m_missEpoch);
                        m_missingByUri.Clear();
                        m_missingByIndex.Clear();
                    }
                }

                Volatile.Write(ref m_incompleteCount, m_incomplete.Count);
            }

            if (completed)
            {
                // the defaults of the namespace were unknown until now, so every verdict derived
                // from them, including the permission caches of active monitored items, is stale.
                NamespaceDefaultPermissionReader.Invalidate();
                DefaultPermissionsChanged?.Invoke(m_host, EventArgs.Empty);
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
        /// cannot be created, when the namespace is already described by an
        /// untyped metadata Object, which is kept and never duplicated, or when
        /// an untyped metadata Object whose <c>NamespaceUri</c> cannot be resolved
        /// yet may describe the namespace.
        /// </returns>
        public async ValueTask<NamespaceMetadataState?> CreateAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            NamespaceMetadataNode? metadata = await GetOrCreateNodeAsync(namespaceUri, cancellationToken)
                .ConfigureAwait(false);
            if (metadata != null && metadata.TypedState == null)
            {
                if (metadata.NamespaceUri == namespaceUri)
                {
                    m_logger.UntypedNamespaceMetadataKept(namespaceUri, metadata.Node.NodeId);
                }
                else
                {
                    m_logger.NamespaceMetadataIdentityUnresolved(namespaceUri, metadata.Node.NodeId);
                }
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
        /// The metadata node, the incomplete view of an untyped metadata Object
        /// whose <c>NamespaceUri</c> cannot be resolved yet and may describe the
        /// namespace (nothing is created then), or <c>null</c> when it cannot be
        /// created.
        /// </returns>
        public async ValueTask<NamespaceMetadataNode?> GetOrCreateNodeAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            long generation = Interlocked.Read(ref m_generation);
            NamespaceMetadataNode? existing = await FindAsync(
                namespaceUri, generation, cancellationToken).ConfigureAwait(false);

            if (existing != null)
            {
                // Subscribe to the default permission properties so that any future changes
                // trigger a DefaultPermissionsChanged notification to allow caches to be invalidated.
                // An incomplete untyped Object is existing metadata as well and never duplicated.
                SubscribeToDefaultPermissions(existing, generation);
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
            SubscribeToDefaultPermissions(created, Interlocked.Read(ref m_generation));

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
                    SubscribeToDefaultPermissions(metadata, Interlocked.Read(ref m_generation));
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

            lock (m_lock)
            {
                // released under the lock, so a concurrent subscription is either released or made after.
                foreach (BaseObjectState metadataNode in m_tracked)
                {
                    metadataNode.StateChanged -= OnNamespaceChildrenChanged;
                    UnsubscribeDefaultProperties(metadataNode);
                }

                m_tracked.Clear();
                m_trackedProperties.Clear();
                m_replacedIn.Clear();
                Interlocked.Increment(ref m_generation);
                m_statesByUri.Clear();
                m_statesByIndex.Clear();
                m_missingByUri.Clear();
                m_missingByIndex.Clear();
                m_referenceTargets.Clear();
                m_untyped.Clear();
                m_stale.Clear();
                m_incomplete.Clear();
                m_wasIncomplete.Clear();
                Volatile.Write(ref m_incompleteCount, 0);
            }

            NamespaceDefaultPermissionReader.Invalidate();
        }

        /// <summary>
        /// Finds the metadata node for <paramref name="namespaceUri"/> among
        /// the children and forward references of <c>Server/Namespaces</c>,
        /// resolving references into other node managers. Without an exact match,
        /// the incomplete view of an untyped metadata Object whose <c>NamespaceUri</c>
        /// cannot be resolved yet is returned, since it may describe the namespace.
        /// The views found are remembered only while the registry is still in
        /// <paramref name="generation"/>.
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

                // an untyped Object whose NamespaceUri cannot be resolved yet may describe the
                // namespace unless its BrowseName names another one; without an exact match it is
                // returned, so the namespace fails closed and no miss is cached or replacement
                // created until its identity is known.
                NamespaceMetadataNode? indeterminate = null;

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

                    indeterminate ??= AsIndeterminate(context, namespaceMetadata, namespaceUri);
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

                        indeterminate ??= AsIndeterminate(context, namespaceMetadata, namespaceUri);
                    }
                }

                return indeterminate;
            }
            catch (OperationCanceledException)
            {
                // a cancelled lookup is no definitive absence and must not be remembered as one.
                throw;
            }
            catch (Exception ex)
            {
                m_logger.ErrorSearchingNamespaceMetadata(ex, namespaceUri);
                return null;
            }
        }

        /// <summary>
        /// Returns <paramref name="metadata"/> when it is the incomplete view of an untyped
        /// metadata Object whose <c>NamespaceUri</c> is unknown and that may describe
        /// <paramref name="namespaceUri"/>, otherwise <c>null</c>. Its BrowseName naming
        /// another namespace of the server rules <paramref name="namespaceUri"/> out
        /// (see <see cref="GetIdentity"/>).
        /// </summary>
        private static NamespaceMetadataNode? AsIndeterminate(
            ISystemContext context,
            NamespaceMetadataNode? metadata,
            string namespaceUri)
        {
            if (metadata is not { IsIncomplete: true, NamespaceUri: null })
            {
                return null;
            }

            string? identity = GetIdentity(context, metadata);
            return identity == null || identity == namespaceUri ? metadata : null;
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

                // the prime resolves every untyped Object again, the incomplete ones included; they
                // stay known as incomplete, so their completion still retires the verdicts derived
                // from their unknown defaults.
                foreach (NodeId incompleteId in m_incomplete.Keys)
                {
                    m_wasIncomplete.Add(incompleteId);
                }

                m_incomplete.Clear();
                Volatile.Write(ref m_incompleteCount, 0);
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
                                SubscribeToDefaultPermissions(metadata, Interlocked.Read(ref m_generation));
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
        /// <param name="metadata">The metadata view.</param>
        /// <param name="generation">
        /// The generation the view was resolved in. Only a view that reflects the current
        /// properties replaces the properties subscribed before (see
        /// <see cref="ReplacesSubscriptions"/>); any other view only adds its properties, so it
        /// never releases the handler of a live property. Validation and subscription share
        /// the lock.
        /// </param>
        private void SubscribeToDefaultPermissions(NamespaceMetadataNode metadata, long generation)
        {
            BaseObjectState metadataNode = metadata.Node;
            BaseVariableState?[] properties =
            [
                metadata.DefaultAccessRestrictions,
                metadata.DefaultRolePermissions,
                metadata.DefaultUserRolePermissions
            ];

            // the events are field-like and take no lock, and no handler runs under this lock.
            lock (m_lock)
            {
                m_tracked.Add(metadataNode);
                if (!m_trackedProperties.TryGetValue(metadataNode, out HashSet<BaseVariableState>? tracked))
                {
                    tracked = [];
                    m_trackedProperties[metadataNode] = tracked;
                }

                if (ReplacesSubscriptions(metadata, generation))
                {
                    foreach (BaseVariableState previous in tracked)
                    {
                        if (Array.IndexOf(properties, previous) < 0)
                        {
                            previous.StateChanged -= OnDefaultPermissionsChanged;
                        }
                    }

                    tracked.Clear();
                }

                foreach (BaseVariableState? property in properties)
                {
                    if (property != null)
                    {
                        // unsubscribe first to avoid duplicate subscriptions if called multiple times
                        property.StateChanged -= OnDefaultPermissionsChanged;
                        property.StateChanged += OnDefaultPermissionsChanged;
                        tracked.Add(property);
                    }
                }

                metadataNode.StateChanged -= OnNamespaceChildrenChanged;
                metadataNode.StateChanged += OnNamespaceChildrenChanged;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="metadata"/>, resolved in
        /// <paramref name="generation"/>, reflects the current properties of its Object, so its
        /// properties replace the ones subscribed before. Called under <c>m_lock</c>.
        /// </summary>
        /// <remarks>
        /// A generated state reads its property slots live. An untyped view captures its
        /// properties when it is resolved, and lookups of one generation can complete in any
        /// order: an incomplete view or a later view of the same generation may lack a property
        /// that an earlier complete view subscribed. So only the first complete untyped view of
        /// the current generation replaces; every other view only adds its properties. A
        /// handler left on a replaced property raises a spurious invalidation at most, and
        /// <see cref="Detach"/> releases it.
        /// </remarks>
        private bool ReplacesSubscriptions(NamespaceMetadataNode metadata, long generation)
        {
            if (Interlocked.Read(ref m_generation) != generation)
            {
                return false;
            }

            if (metadata.TypedState != null)
            {
                return true;
            }

            if (metadata.IsIncomplete ||
                (m_replacedIn.TryGetValue(metadata.Node, out long replacedIn) && replacedIn == generation))
            {
                return false;
            }

            m_replacedIn[metadata.Node] = generation;
            return true;
        }

        /// <summary>
        /// Releases the handlers of every default property subscribed for a metadata node.
        /// Called under <c>m_lock</c>.
        /// </summary>
        private void UnsubscribeDefaultProperties(BaseObjectState metadataNode)
        {
            m_replacedIn.Remove(metadataNode);
            if (m_trackedProperties.TryGetValue(metadataNode, out HashSet<BaseVariableState>? tracked))
            {
                foreach (BaseVariableState property in tracked)
                {
                    property.StateChanged -= OnDefaultPermissionsChanged;
                }

                m_trackedProperties.Remove(metadataNode);
                return;
            }

            if (metadataNode is NamespaceMetadataState namespaceMetadataState)
            {
                namespaceMetadataState.DefaultAccessRestrictions?.StateChanged -= OnDefaultPermissionsChanged;
                namespaceMetadataState.DefaultRolePermissions?.StateChanged -= OnDefaultPermissionsChanged;
                namespaceMetadataState.DefaultUserRolePermissions?.StateChanged -= OnDefaultPermissionsChanged;
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
                SubscribeToDefaultPermissions(
                    NamespaceMetadataNode.From(namespaceMetadataState)!,
                    Interlocked.Read(ref m_generation));
            }
            else
            {
                // The properties of an untyped node are resolved when it is discovered,
                // so every lookup that resolved it is dropped and it is resolved again.
                Invalidate();
                if (TryResolve(context, metadataNode, out NamespaceMetadataNode? metadata) && metadata != null)
                {
                    SubscribeToDefaultPermissions(metadata, Interlocked.Read(ref m_generation));
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
            if ((Array.IndexOf(properties, null) >= 0 && HasUnlinkedPropertyReferences(context, objectNode)) ||
                IsIdentityUnavailable(properties[0]))
            {
                // incomplete: resolved asynchronously, which retries it until it is complete.
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
        /// a property is missing makes the view incomplete
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
                    // a target on another server is never a property of the local Object; it is
                    // skipped before the conversion, which ignores the server index.
                    if (reference.IsInverse ||
                        reference.ReferenceTypeId != ReferenceTypeIds.HasProperty ||
                        reference.TargetId.ServerIndex != 0)
                    {
                        continue;
                    }

                    var targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.Server.NamespaceUris);
                    if (targetId.IsNull)
                    {
                        // a local target in a namespace that is not registered yet cannot be resolved now.
                        unresolved |= !reference.TargetId.IsNull;
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

            // the BrowseName of an unresolved target is unknown, so it may be any missing property:
            // a missing default makes the defaults unknown and a missing NamespaceUri the namespace.
            // A NamespaceUri property without a value leaves the namespace unknown as well.
            bool isIncomplete = (unresolved && Array.IndexOf(properties, null) >= 0) ||
                IsIdentityUnavailable(properties[0]);
            NamespaceMetadataNode metadata = CreateUntyped(objectNode, properties, isIncomplete);
            PublishUntyped(objectNode, metadata, generation);
            return metadata;
        }

        /// <summary>
        /// Classifies an untyped metadata Object that is not resolved yet for a synchronous
        /// lookup of <paramref name="namespaceUri"/>, the way <see cref="FindAsync"/> treats its
        /// incomplete view: a known <c>NamespaceUri</c> (the value of its <c>NamespaceUri</c>
        /// child or of its retired view <paramref name="retired"/>) that matches makes it the
        /// metadata of the namespace. An unknown one makes it a possible match, unless an
        /// asynchronous lookup already found it incomplete and its BrowseName names another
        /// namespace of the server (see <see cref="GetIdentity"/>); an Object that was never
        /// resolved waits for the asynchronous lookup.
        /// </summary>
        private static UnresolvedEntry ClassifyUnresolved(
            ISystemContext context,
            NodeState node,
            NamespaceMetadataNode? retired,
            string namespaceUri)
        {
            string? knownUri = null;
            if (node.FindChildWithQualifiedName(context, s_propertyNames[0]) is BaseVariableState property &&
                !IsIdentityUnavailable(property))
            {
                property.Value.TryGetValue(out knownUri);
            }
            else
            {
                knownUri = retired?.NamespaceUri;
            }

            if (knownUri != null)
            {
                return knownUri == namespaceUri ? UnresolvedEntry.SameNamespace : UnresolvedEntry.OtherNamespace;
            }

            if (retired is not { IsIncomplete: true })
            {
                return UnresolvedEntry.Unknown;
            }

            string? hint = GetBrowseNameIdentity(context, node);
            return hint == null || hint == namespaceUri ? UnresolvedEntry.Unknown : UnresolvedEntry.OtherNamespace;
        }

        /// <summary>
        /// Returns the namespace <paramref name="metadata"/> describes: its <c>NamespaceUri</c>,
        /// or, while that property of an incomplete view cannot be resolved, the name of the
        /// Object's BrowseName when it is a namespace URI of the server. OPC 10000-5 §6.3.14
        /// requires the BrowseName of an Object under <c>Namespaces</c> to be derived from the
        /// namespace it represents, e.g. with the namespace URI as name.
        /// </summary>
        /// <returns>The namespace URI, or <c>null</c> when the namespace is unknown.</returns>
        private static string? GetIdentity(ISystemContext context, NamespaceMetadataNode metadata)
        {
            if (metadata.NamespaceUri is string namespaceUri)
            {
                return namespaceUri;
            }

            return metadata.IsIncomplete ? GetBrowseNameIdentity(context, metadata.Node) : null;
        }

        /// <summary>
        /// Returns the name of the BrowseName of <paramref name="node"/> when it is a namespace
        /// URI registered in the server's <c>NamespaceArray</c>, otherwise <c>null</c>
        /// (see <see cref="GetIdentity"/>).
        /// </summary>
        private static string? GetBrowseNameIdentity(ISystemContext context, NodeState node)
        {
            string? name = node.BrowseName.Name;
            return !string.IsNullOrEmpty(name) && context.NamespaceUris.GetIndex(name) >= 0 ? name : null;
        }

        /// <summary>
        /// Returns <c>true</c> when the <c>NamespaceUri</c> property of an untyped metadata
        /// Object is present but holds no namespace URI yet, e.g. a property declared without a
        /// value or not initialized yet: the namespace the Object describes is unknown.
        /// </summary>
        private static bool IsIdentityUnavailable(BaseVariableState? namespaceUri)
        {
            return namespaceUri != null &&
                (!namespaceUri.Value.TryGetValue(out string? value) || string.IsNullOrEmpty(value));
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
                if (reference.IsInverse ||
                    reference.ReferenceTypeId != ReferenceTypeIds.HasProperty ||
                    reference.TargetId.ServerIndex != 0)
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
        // Moved, under m_lock, when remembered misses are dropped without an invalidation; a miss
        // found by a lookup that started before is not remembered.
        private long m_missEpoch;
        // Untyped metadata Objects resolved incomplete in the current generation, by node id, and
        // whether they are reference targets of Server/Namespaces; guarded by m_lock. The count is
        // read without the lock by every asynchronous lookup.
        private readonly Dictionary<NodeId, (BaseObjectState Node, bool IsReferenceTarget)> m_incomplete = [];
        // Untyped metadata Objects that were incomplete when an invalidation retired them; their
        // completion in a later generation still raises DefaultPermissionsChanged. Guarded by m_lock.
        private readonly HashSet<NodeId> m_wasIncomplete = [];
        private int m_incompleteCount;
        private int m_resolvingIncomplete;
        private readonly HashSet<BaseObjectState> m_tracked = [];
        // Every default property subscribed per metadata node since its last replacement.
        private readonly Dictionary<BaseObjectState, HashSet<BaseVariableState>> m_trackedProperties = [];
        // The generation in which an untyped metadata node last replaced its subscriptions.
        private readonly Dictionary<BaseObjectState, long> m_replacedIn = [];
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

        /// <summary>
        /// How an untyped metadata Object that is not resolved yet relates to the namespace a
        /// synchronous lookup is for (see <see cref="ClassifyUnresolved"/>).
        /// </summary>
        private enum UnresolvedEntry
        {
            /// <summary>
            /// The Object is known to describe another namespace.
            /// </summary>
            OtherNamespace,

            /// <summary>
            /// The Object may describe the namespace.
            /// </summary>
            Unknown,

            /// <summary>
            /// The Object is known to describe the namespace.
            /// </summary>
            SameNamespace
        }
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

        [LoggerMessage(EventId = ServerEventIds.NamespaceMetadataRegistry + 4, Level = LogLevel.Warning,
            Message = "The NamespaceUri of the untyped NamespaceMetadataType object {NodeId} cannot be " +
                "resolved yet; no NamespaceMetadataState is created for namespace '{NamespaceUri}'.")]
        public static partial void NamespaceMetadataIdentityUnresolved(
            this ILogger logger,
            string namespaceUri,
            NodeId nodeId);
    }
}
