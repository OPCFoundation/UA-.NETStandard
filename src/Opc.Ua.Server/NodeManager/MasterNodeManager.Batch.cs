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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Coordinates atomic node-manager batches and captured read images.
    /// </summary>
    public partial class MasterNodeManager : IDynamicNodeManagerBatchHost, INodeManagerReadImageSource
    {
        /// <inheritdoc/>
        public INodeManagerReadImage? GetReadImage(IAsyncNodeManager owner)
        {
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            return m_nodeManagers.GetReadImage(owner);
        }

        /// <inheritdoc/>
        NodeManagerRoutingTable.RoutingSnapshot IDynamicNodeManagerBatchHost.RoutingRevision
            => m_nodeManagers.Revision;

        /// <inheritdoc/>
        void IDynamicNodeManagerBatchHost.ReleaseRetiringEventGeneration(NodeManagerGeneration generation)
        {
            if (generation is null)
            {
                throw new ArgumentNullException(nameof(generation));
            }

            lock (m_retiringEventGenerationsLock)
            {
                NodeManagerGeneration[] current = m_retiringEventGenerations;
                int index = Array.IndexOf(current, generation);
                if (index < 0)
                {
                    return;
                }
                var next = new NodeManagerGeneration[current.Length - 1];
                Array.Copy(current, 0, next, 0, index);
                Array.Copy(current, index + 1, next, index, next.Length - index);
                Volatile.Write(ref m_retiringEventGenerations, next);
            }
        }

        /// <summary>
        /// Gets the retired generations whose all-events binding records have not been released yet. Item deletion
        /// releases its binding on these generations so a deleted item is never retained by a retired manager.
        /// The array is empty unless a lifecycle change retired a generation.
        /// </summary>
        internal NodeManagerGeneration[] RetiringEventGenerations => Volatile.Read(ref m_retiringEventGenerations);

        private void TrackRetiringEventGenerations(List<NodeManagerGeneration> generations)
        {
            if (generations.Count == 0)
            {
                return;
            }
            lock (m_retiringEventGenerationsLock)
            {
                NodeManagerGeneration[] current = m_retiringEventGenerations;
                var next = new NodeManagerGeneration[current.Length + generations.Count];
                current.CopyTo(next, 0);
                generations.CopyTo(next, current.Length);
                Volatile.Write(ref m_retiringEventGenerations, next);
            }
        }

        async ValueTask<PreparedAddressSpaceReferences> IDynamicNodeManagerBatchHost.PrepareReferencesAsync(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            NodeManagerRoutingTable.RoutingSnapshot view,
            CancellationToken cancellationToken)
        {
            if (!view.TryPin(out int stripe))
            {
                throw new InvalidOperationException("The reference preparation view has retired.");
            }
            try
            {
                Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> previous;
                Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> previousSnapshot;
                Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> next;
                HashSet<IAsyncNodeManager> retiring = [.. removed];
                await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(view, m_nodeManagers.Revision))
                    {
                        throw new InvalidOperationException("The address space changed before reference discovery.");
                    }
                    previous = m_dynamicExternalReferences;
                    previousSnapshot = previous.ToDictionary(entry => entry.Key, entry => entry.Value);
                    foreach (PreparedNodeManager candidate in candidates)
                    {
                        ValidatePreparedNodeManager(candidate);
                        if (candidate.Generation is not { Gate.State: NodeManagerGenerationState.Prepared })
                        {
                            throw new InvalidOperationException("A batch candidate has no prepared generation.");
                        }
                        if (candidate.ReplacedNodeManager is { } old)
                        {
                            retiring.Add(old);
                        }
                    }
                    next = previousSnapshot.Where(entry => !retiring.Contains(entry.Key))
                        .ToDictionary(entry => entry.Key, entry => entry.Value);
                    foreach (PreparedNodeManager candidate in candidates)
                    {
                        next.Add(candidate.NodeManager, candidate.ExternalReferences);
                    }
                }
                finally
                {
                    m_dynamicMutationSemaphore.Release();
                }

                (
                    ArrayOf<PreparedReferenceSource> sources,
                    Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> additions,
                    Dictionary<IAsyncNodeManager, List<LocalReference>> deferredRemovals) =
                    await PrepareReferenceSourcesAsync(
                        candidates, retiring, previousSnapshot, next, view, cancellationToken)
                        .ConfigureAwait(false);
                var owners = new Dictionary<NodeState.ReferenceUpdate, NodeManagerGeneration>();
                foreach (PreparedReferenceSource source in sources)
                {
                    owners.Add(source.Update,
                        (candidates.ToArray() ?? []).FirstOrDefault(candidate =>
                            ReferenceEquals(candidate.NodeManager, source.Owner))?.Generation ??
                        view.GetGeneration(source.Owner));
                }
                return new PreparedAddressSpaceReferences(view, previous, next, owners, additions, deferredRemovals);
            }
            finally
            {
                view.Epoch.Exit(stripe);
            }
        }

        async ValueTask IDynamicNodeManagerBatchHost.CommitBatchAsync(
            NodeManagerBatchContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ArrayOf<PreparedNodeManager> candidates = context.Candidates;
            ArrayOf<IAsyncNodeManager> removed = context.Removed;
            TypeTable typeTree = context.TypeTree;
            TypeTable originalTypes = context.OriginalTypes;
            long typeRevision = context.TypeRevision;
            EncodeableFactory factory = context.Factory;
            EncodeableFactory originalFactory = context.OriginalFactory;
            long factoryRevision = context.FactoryRevision;
            ArrayOf<INodeManagerReadImage> readImages = context.ReadImages;
            Func<CancellationToken, ValueTask>? decideAsync = context.DecideAsync;
            Func<ValueTask> published = context.Published;
            Func<ValueTask> reconcileBindingsAsync = context.ReconcileBindingsAsync;
            CancellationToken cancellationToken = context.CancellationToken;

            var referenceUpdates = new List<NodeState.ReferenceUpdate>();
            var referenceOwners = new Dictionary<NodeState.ReferenceUpdate, NodeManagerGeneration>();
            NodeManagerGeneration[] publishedOwners = [];
            var referenceAdditions = new Dictionary<
                IAsyncNodeManager,
                Dictionary<NodeId, IList<IReference>>>();
            bool dynamicMutationSemaphoreHeld = false;
            bool startupShutdownSemaphoreHeld = false;
            bool decisionCommitted = false;
            try
            {
                await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                dynamicMutationSemaphoreHeld = true;
                try
                {
                    await m_startupShutdownSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                    startupShutdownSemaphoreHeld = true;
                    try
                    {
                        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                        {
                            PreparedNodeManager candidate = candidates[candidateIndex];
                            ValidatePreparedNodeManager(candidate);
                            if (candidate.Generation is not { Gate.State: NodeManagerGenerationState.Prepared })
                            {
                                throw new InvalidOperationException("A batch candidate has no prepared generation.");
                            }
                        }
                        HashSet<IAsyncNodeManager> retiring = [.. removed];
                        foreach (PreparedNodeManager candidate in candidates)
                        {
                            if (candidate.ReplacedNodeManager is { } old)
                            {
                                retiring.Add(old);
                            }
                        }
                        var retiringGenerations = new List<NodeManagerGeneration>(retiring.Count);
                        foreach (IAsyncNodeManager manager in retiring)
                        {
                            if (!m_dynamicExternalReferences.ContainsKey(manager))
                            {
                                throw new InvalidOperationException("A batch retirement is no longer registered.");
                            }
                            retiringGenerations.Add(m_nodeManagers.GetGeneration(manager));
                        }

                        typeTree = originalTypes.PrepareAdditiveMerge(typeTree, originalTypes, typeRevision);
                        factory = originalFactory.PrepareAdditiveMerge(
                            factory, originalFactory, factoryRevision, context.Preparation.NamespaceUris);
                        PreparedAddressSpaceReferences references = context.References ??
                            throw new InvalidOperationException("Reference discovery must precede publication.");
                        if (!ReferenceEquals(references.View, m_nodeManagers.Revision) ||
                            !ReferenceEquals(references.Previous, m_dynamicExternalReferences))
                        {
                            throw new InvalidOperationException("The reference preparation is stale.");
                        }
                        var nextReferences = references.Next;
                        referenceAdditions = references.Additions;
                        foreach (KeyValuePair<IAsyncNodeManager, List<LocalReference>> deferred in
                            references.DeferredRemovals)
                        {
                            context.DeferReferenceRemoval(deferred.Key, deferred.Value);
                        }
                        foreach (KeyValuePair<NodeState.ReferenceUpdate, NodeManagerGeneration> source in
                            references.Owners)
                        {
                            referenceUpdates.Add(source.Key);
                            referenceOwners.Add(source.Key, source.Value);
                        }

                        using NodeManagerRoutingTable.PreparedRoutes routes =
                            m_nodeManagers.PrepareBatch(
                                candidates, removed,
                                manager => (manager.NamespaceUris ?? Enumerable.Empty<string>())
                                    .Concat(ChangePreparer.GetServer(manager)?.NamespaceRegistrations ??
                                        Enumerable.Empty<string>())
                                    .Select(uri => (int)context.Preparation.NamespaceUris.GetIndexOrAppend(uri)),
                                originalTypes, originalFactory, readImages);
                        routes.Reserve();
                        using TypeTable.Publication types = Server.TypeTree.BeginPublication(originalTypes, typeRevision);
                        using EncodeableFactory.Publication registrations =
                            originalFactory.BeginPublication(originalFactory, factoryRevision);
                        using StringTable.Publication namespaces = Server.NamespaceUris.BeginPublication(
                            context.Preparation.NamespaceUris, context.Preparation.NamespaceRevision);
                        using StringTable.Publication serverUris = Server.ServerUris.BeginPublication(
                            context.Preparation.ServerUris, context.Preparation.ServerRevision);
                        foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                        {
                            update.Reserve();
                        }
                        var publisher = new AddressSpacePublisher(
                            routes,
                            namespaces,
                            serverUris,
                            types,
                            registrations,
                            typeTree,
                            factory,
                            candidates,
                            referenceUpdates,
                            () =>
                            {
                                context.ViewPublished();
                                m_dynamicExternalReferences = nextReferences;
                                TrackRetiringEventGenerations(retiringGenerations);
                                foreach (PreparedNodeManager candidate in candidates)
                                {
                                    candidate.Published = true;
                                    candidate.ReplacedNodeManager = null;
                                    SetPreparing(candidate.NodeManager, preparing: false);
                                }
                            });
                        cancellationToken.ThrowIfCancellationRequested();
                        if (decideAsync is null)
                        {
                            return;
                        }
                        // The lifecycle writer excludes server teardown; do not hold the startup/shutdown semaphore
                        // while the durable decision callback performs external I/O. The prepared batch retains
                        // its lifecycle operation until commit or disposal, so shutdown cannot dispose this host.
                        m_startupShutdownSemaphoreSlim.Release();
                        startupShutdownSemaphoreHeld = false;
                        m_dynamicMutationSemaphore.Release();
                        dynamicMutationSemaphoreHeld = false;
                        await decideAsync(cancellationToken).ConfigureAwait(false);
                        decisionCommitted = true;

                        // The publication invocation remains an active lifecycle operation until
                        // reconciliation finishes, so shutdown cannot tear down this host first.
                        publisher.Publish();
                        publishedOwners =
                            m_nodeManagers.Select(manager => m_nodeManagers.GetGeneration(manager)).ToArray();
                        // Callbacks may mutate the committed image after internal publication bookkeeping.
                        routes.Dispose();
                        namespaces.Dispose();
                        serverUris.Dispose();
                        types.Dispose();
                        registrations.Dispose();
                        foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                        {
                            update.Dispose();
                        }
                        await published().ConfigureAwait(false);
                    }
                    finally
                    {
                        if (startupShutdownSemaphoreHeld)
                        {
                            m_startupShutdownSemaphoreSlim.Release();
                            startupShutdownSemaphoreHeld = false;
                        }
                    }
                }
                finally
                {
                    if (dynamicMutationSemaphoreHeld)
                    {
                        m_dynamicMutationSemaphore.Release();
                        dynamicMutationSemaphoreHeld = false;
                    }
                }
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    PreparedNodeManager candidate = candidates[candidateIndex];
                    if (!referenceAdditions.TryGetValue(
                        candidate.NodeManager,
                        out Dictionary<NodeId, IList<IReference>>? additions))
                    {
                        continue;
                    }
                    foreach (NodeManagerGeneration owner in publishedOwners)
                    {
                        await context.Reconciliation.RunAsync(
                            NodeManagerReconciliationStage.References,
                            [candidate.Generation!, owner],
                            () => owner.NodeManager.AddReferencesAsync(additions, CancellationToken.None))
                            .ConfigureAwait(false);
                    }
                }
                await reconcileBindingsAsync().ConfigureAwait(false);
                foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                {
                    await context.Reconciliation.RunAsync(
                        NodeManagerReconciliationStage.References,
                        (candidates.ToArray() ?? []).Select(candidate => candidate.Generation!)
                            .Append(referenceOwners[update]).Distinct().ToArray(),
                        () =>
                        {
                            var failures = new List<Exception>();
                            update.Notify(failures.Add);
                            if (failures.Count != 0)
                            {
                                throw new AggregateException("Reference observers remain pending.", failures);
                            }
                            return default;
                        }).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException && !decisionCommitted)
            {
                throw;
            }
            finally
            {
                if (dynamicMutationSemaphoreHeld)
                {
                    m_dynamicMutationSemaphore.Release();
                }
                foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                {
                    update.Dispose();
                }
            }
        }

        /// <summary>
        /// Computes the external reference additions and removals that prepared node managers contribute to nodes owned
        /// by in-memory node managers.
        /// </summary>
        /// <param name="candidates">The prepared node managers.</param>
        /// <param name="retiring">The node managers that are retired.</param>
        /// <param name="previousReferences">Captured contributions before the batch.</param>
        /// <param name="nextReferences">The external references after the batch.</param>
        /// <param name="view">The pinned serving view used for owner discovery.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The reference sources to update.</returns>
        private async ValueTask<(
            ArrayOf<PreparedReferenceSource> Sources,
            Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> Additions,
            Dictionary<IAsyncNodeManager, List<LocalReference>> DeferredRemovals)>
            PrepareReferenceSourcesAsync(
            ArrayOf<PreparedNodeManager> candidates,
            HashSet<IAsyncNodeManager> retiring,
            Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> previousReferences,
            Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> nextReferences,
            NodeManagerRoutingTable.RoutingSnapshot view,
            CancellationToken cancellationToken)
        {
            Dictionary<NodeId, ReferenceDictionary<bool>> previous = Collect(previousReferences.Values);
            Dictionary<NodeId, ReferenceDictionary<bool>> next = Collect(nextReferences.Values);
            var sources = new List<PreparedReferenceSource>();
            var referenceAdditions =
                new Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>>();
            var deferredRemovals = new Dictionary<IAsyncNodeManager, List<LocalReference>>();
            var newOwners = new HashSet<IAsyncNodeManager>();
            foreach (PreparedNodeManager candidate in candidates)
            {
                newOwners.Add(candidate.NodeManager);
            }
            IAsyncNodeManager[] owners =
            [
                .. view.VisibleNodeManagers.Where(manager => !retiring.Contains(manager)),
                .. newOwners
            ];
            var nodes = new HashSet<NodeState>();
            foreach (NodeId sourceId in previous.Keys.Union(next.Keys))
            {
                previous.TryGetValue(sourceId, out ReferenceDictionary<bool>? oldReferences);
                next.TryGetValue(sourceId, out ReferenceDictionary<bool>? newReferences);
                ArrayOf<IReference> additions = newReferences is null ? [] : [.. newReferences.Keys];
                ArrayOf<IReference> removals = oldReferences is null
                    ? []
                    : [.. oldReferences.Keys.Where(reference => newReferences?.ContainsKey(reference) != true)];
                var ownerNodes = new List<(IAsyncNodeManager Owner, NodeState Node)>();
                bool hasUnsupportedOwner = false;
                foreach (IAsyncNodeManager owner in owners)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    NodeManagerGeneration? generation = newOwners.Contains(owner) ? null : view.GetGeneration(owner);
                    int stripe = 0;
                    if (generation is not null && !generation.Gate.TryEnter(out stripe))
                    {
                        throw new InvalidOperationException("A reference owner retired during discovery.");
                    }
                    object handle;
                    try
                    {
                        handle = await owner.GetManagerHandleAsync(sourceId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        generation?.Gate.Exit(stripe);
                    }
                    if (handle is null)
                    {
                        continue;
                    }
                    if ((owner is not AsyncCustomNodeManager && owner.SyncNodeManager is not CustomNodeManager2) ||
                        handle is not NodeHandle { Validated: true, Node: { } node })
                    {
                        hasUnsupportedOwner = true;
                        continue;
                    }
                    if (!nodes.Add(node))
                    {
                        throw new NotSupportedException(
                            "A prepared reference source has multiple NodeManager owners.");
                    }
                    ownerNodes.Add((owner, node));
                    sources.Add(new PreparedReferenceSource(
                        owner, node.PrepareReferences(additions, newOwners.Contains(owner) ? [] : removals)));
                }
                if (hasUnsupportedOwner && ownerNodes.Count > 0)
                {
                    throw new NotSupportedException(
                        "Prepared external references have both in-memory and external owners.");
                }
                if (hasUnsupportedOwner || ownerNodes.Count == 0)
                {
                    if (removals.Count > 0)
                    {
                        foreach (IReference removal in removals)
                        {
                            IAsyncNodeManager? retirementOwner = retiring.FirstOrDefault(manager =>
                                previousReferences.TryGetValue(
                                    manager,
                                    out Dictionary<NodeId, IList<IReference>>? contributed) &&
                                contributed.TryGetValue(sourceId, out IList<IReference>? references) &&
                                references.Any(reference =>
                                    reference.ReferenceTypeId == removal.ReferenceTypeId &&
                                    reference.IsInverse == removal.IsInverse &&
                                    reference.TargetId == removal.TargetId));
                            if (retirementOwner is null)
                            {
                                throw new NotSupportedException(
                                    "Removing prepared external references requires an in-memory NodeState owner " +
                                    "or a retiring contributor.");
                            }
                            NodeId targetId = ExpandedNodeId.ToNodeId(removal.TargetId, Server.NamespaceUris);
                            if (!targetId.IsNull)
                            {
                                if (!deferredRemovals.TryGetValue(
                                    retirementOwner,
                                    out List<LocalReference>? pending))
                                {
                                    deferredRemovals.Add(retirementOwner, pending = []);
                                }
                                pending.Add(new LocalReference(
                                    sourceId,
                                    removal.ReferenceTypeId,
                                    removal.IsInverse,
                                    targetId));
                            }
                        }
                    }
                    foreach (PreparedNodeManager candidate in candidates)
                    {
                        if (!candidate.ExternalReferences.TryGetValue(
                            sourceId,
                            out IList<IReference>? candidateReferences))
                        {
                            continue;
                        }
                        var candidateAdditions = new List<IReference>();
                        foreach (IReference reference in candidateReferences)
                        {
                            var frozen = new NodeStateReference(
                                reference.ReferenceTypeId,
                                reference.IsInverse,
                                reference.TargetId);
                            if (newReferences?.ContainsKey(frozen) == true &&
                                oldReferences?.ContainsKey(frozen) != true)
                            {
                                candidateAdditions.Add(frozen);
                            }
                        }
                        if (candidateAdditions.Count == 0)
                        {
                            continue;
                        }
                        if (!referenceAdditions.TryGetValue(
                            candidate.NodeManager,
                            out Dictionary<NodeId, IList<IReference>>? candidateReferencesBySource))
                        {
                            referenceAdditions.Add(
                                candidate.NodeManager,
                                candidateReferencesBySource = []);
                        }
                        candidateReferencesBySource[sourceId] = candidateAdditions;
                    }
                }
            }
            return ([.. sources], referenceAdditions, deferredRemovals);

            Dictionary<NodeId, ReferenceDictionary<bool>> Collect(
                IEnumerable<Dictionary<NodeId, IList<IReference>>> references)
            {
                var result = new Dictionary<NodeId, ReferenceDictionary<bool>>();
                if (m_foundationExternalReferences is { } foundation)
                {
                    Add(foundation);
                }
                foreach (Dictionary<NodeId, IList<IReference>> source in references)
                {
                    Add(source);
                }
                return result;

                void Add(Dictionary<NodeId, IList<IReference>> source)
                {
                    foreach (KeyValuePair<NodeId, IList<IReference>> entry in source)
                    {
                        if (!result.TryGetValue(entry.Key, out ReferenceDictionary<bool>? collected))
                        {
                            result.Add(entry.Key, collected = []);
                        }
                        foreach (IReference reference in entry.Value)
                        {
                            collected[new NodeStateReference(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)] = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Describes the reference changes for one source node.
        /// </summary>
        /// <param name="Owner">The node manager that owns the node.</param>
        /// <param name="Update">Versioned reference edits captured before subsequent owner callbacks.</param>
        private sealed record PreparedReferenceSource(
            IAsyncNodeManager Owner,
            NodeState.ReferenceUpdate Update);
    }
}
