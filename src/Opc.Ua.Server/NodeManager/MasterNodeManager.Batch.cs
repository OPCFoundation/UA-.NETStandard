// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        /// <summary>
        /// Holds host-private synchronization while exposing only lifecycle admission capabilities.
        /// </summary>
        private sealed class BatchAdmission : INodeManagerBatchAdmission
        {
            /// <summary>
            /// Creates an admission from capabilities acquired by the host.
            /// </summary>
            /// <param name="addressSpaceAdmission">The private address-space admission, if acquired.</param>
            /// <param name="serverLifecycleAdmission">The private server-lifecycle admission, if acquired.</param>
            /// <param name="callbackBorrow">The complete callback-owned admission, if borrowed.</param>
            internal BatchAdmission(
                SemaphoreSlim? addressSpaceAdmission,
                SemaphoreSlim? serverLifecycleAdmission,
                NodeManagerCallbackPermit.Borrow? callbackBorrow = null)
            {
                m_addressSpaceAdmission = addressSpaceAdmission;
                m_serverLifecycleAdmission = serverLifecycleAdmission;
                m_callbackBorrow = callbackBorrow;
                m_borrowedCapabilities = (int)(callbackBorrow?.Capabilities ?? NodeManagerAdmissionCapability.None);
                m_capabilities =
                    (addressSpaceAdmission is null
                        ? 0 : (int)NodeManagerAdmissionCapability.AddressSpaceMutation) |
                    (serverLifecycleAdmission is null
                        ? 0 : (int)NodeManagerAdmissionCapability.ServerLifecycle) |
                    m_borrowedCapabilities;
            }

            /// <inheritdoc/>
            public bool Includes(NodeManagerAdmissionCapability capability)
            {
                return capability != NodeManagerAdmissionCapability.None &&
                    (Volatile.Read(ref m_capabilities) & (int)capability) == (int)capability;
            }

            /// <inheritdoc/>
            public void Release(NodeManagerAdmissionCapability capability)
            {
                if ((capability & NodeManagerAdmissionCapability.ServerLifecycle) != 0)
                {
                    Revoke(NodeManagerAdmissionCapability.ServerLifecycle);
                    Interlocked.Exchange(ref m_serverLifecycleAdmission, null)?.Release();
                }
                if ((capability & NodeManagerAdmissionCapability.AddressSpaceMutation) != 0)
                {
                    Revoke(NodeManagerAdmissionCapability.AddressSpaceMutation);
                    Interlocked.Exchange(ref m_addressSpaceAdmission, null)?.Release();
                }
                ReleaseBorrowedCapabilities(capability);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Release(
                    NodeManagerAdmissionCapability.ServerLifecycle |
                    NodeManagerAdmissionCapability.AddressSpaceMutation);
            }

            private void Revoke(NodeManagerAdmissionCapability capability)
            {
                while (true)
                {
                    int current = Volatile.Read(ref m_capabilities);
                    int next = current & ~(int)capability;
                    if (Interlocked.CompareExchange(ref m_capabilities, next, current) == current)
                    {
                        return;
                    }
                }
            }

            private void ReleaseBorrowedCapabilities(NodeManagerAdmissionCapability capability)
            {
                while (true)
                {
                    int current = Volatile.Read(ref m_borrowedCapabilities);
                    int next = current & ~(int)capability;
                    if (Interlocked.CompareExchange(ref m_borrowedCapabilities, next, current) == current)
                    {
                        if (current != 0 && next == 0)
                        {
                            Interlocked.Exchange(ref m_callbackBorrow, null)?.Dispose();
                        }
                        return;
                    }
                }
            }

            private SemaphoreSlim? m_addressSpaceAdmission;
            private SemaphoreSlim? m_serverLifecycleAdmission;
            private NodeManagerCallbackPermit.Borrow? m_callbackBorrow;
            private int m_borrowedCapabilities;
            private int m_capabilities;
        }

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

        NodeManagerCallbackPermit? IDynamicNodeManagerBatchHost.CaptureLifecycleCallbackPermit()
            => CaptureLifecycleCallbackPermit();

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
                var next = new List<NodeManagerGeneration>(current);
                foreach (NodeManagerGeneration generation in generations)
                {
                    AddIfMissing(generation);
                }
                Volatile.Write(ref m_retiringEventGenerations, [.. next]);

                void AddIfMissing(NodeManagerGeneration generation)
                {
                    if (!next.Contains(generation))
                    {
                        next.Add(generation);
                    }
                }
            }
        }

        ValueTask<PreparedAddressSpaceReferences> IDynamicNodeManagerBatchHost.PrepareReferencesAsync(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            NodeManagerRoutingTable.RoutingSnapshot view,
            CancellationToken cancellationToken,
            NodeManagerCallbackPermit? callbackPermit)
        {
            ValueTask<PreparedAddressSpaceReferences> preparation =
                PrepareReferencesAsync(candidates, removed, view, callbackPermit, cancellationToken);
            m_hostBoundary?.ReferencePreparationDispatched();
            return preparation;
        }

        /// <summary>
        /// Prepares reference owners under the captured routing view.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A manager being removed or replaced has no registered owner in the captured routing view.
        /// </exception>
        private async ValueTask<PreparedAddressSpaceReferences> PrepareReferencesAsync(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            NodeManagerRoutingTable.RoutingSnapshot view,
            NodeManagerCallbackPermit? callbackPermit,
            CancellationToken cancellationToken)
        {
            if (!view.TryPin(out int stripe))
            {
                throw new NodeManagerRoutingConflictException("The reference preparation view has retired.", view)
                {
                    StaleDiscovery = true
                };
            }
            try
            {
                Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> previous;
                Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> previousSnapshot;
                Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> next;
                var retiring = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
                for (int index = 0; index < removed.Count; index++)
                {
                    retiring.Add(view.FindRegisteredOwner(removed[index]) ??
                        throw new InvalidOperationException("A batch retirement is not registered."));
                }
                using (await AcquireHostAdmissionAsync(
                    callbackPermit,
                    NodeManagerAdmissionCapability.AddressSpaceMutation,
                    cancellationToken).ConfigureAwait(false))
                {
                    if (!ReferenceEquals(view, m_nodeManagers.Revision))
                    {
                        throw new NodeManagerRoutingConflictException(
                            "The address space changed before reference discovery.", view)
                        {
                            StaleDiscovery = true
                        };
                    }
                    ApplyDeferredReferencePrunes();
                    previous = m_dynamicExternalReferences;
                    previousSnapshot = new Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>>(
                        previous, RefEqualityComparer.Default);
                    foreach (PreparedNodeManager candidate in candidates)
                    {
                        ValidatePreparedNodeManager(candidate);
                        if (candidate.ReplacedNodeManager is { } old)
                        {
                            retiring.Add(view.FindRegisteredOwner(old) ??
                                throw new InvalidOperationException("A batch retirement is not registered."));
                        }
                    }
                    IEqualityComparer<IAsyncNodeManager> ownerComparer = RefEqualityComparer.Default;
                    next = previousSnapshot.Where(entry => !retiring.Contains(entry.Key))
                        .ToDictionary(entry => entry.Key, entry => entry.Value, ownerComparer);
                    foreach (PreparedNodeManager candidate in candidates)
                    {
                        ChangePreparer.GetServer(candidate.NodeManager)?.PruneStagedExternalReferences();
                        next.Add(candidate.NodeManager, candidate.ExternalReferences);
                    }
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

        /// <summary>
        /// Acquires the host capabilities required by a lifecycle commit.
        /// </summary>
        ValueTask<INodeManagerBatchAdmission> IDynamicNodeManagerBatchHost.AcquireCommitAdmissionAsync(
            NodeManagerCallbackPermit? callbackPermit,
            CancellationToken cancellationToken)
        {
            return AcquireHostAdmissionAsync(
                callbackPermit,
                NodeManagerAdmissionCapability.AddressSpaceMutation |
                NodeManagerAdmissionCapability.ServerLifecycle,
                cancellationToken);
        }

        /// <summary>
        /// Acquires the requested host admission and any capabilities inherited from the active callback.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The missing callback capabilities identify neither address-space mutation nor server lifecycle admission.
        /// </exception>
        // BatchAdmission owns a successful borrow; the finally blocks dispose every untransferred borrow.
        // TODO: Remove this exception when the native lifecycle admission contract replaces this adapter.
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Returned BatchAdmission owns transferred borrows; failure paths release them in finally.")]
        private async ValueTask<INodeManagerBatchAdmission> AcquireHostAdmissionAsync(
            NodeManagerCallbackPermit? callbackPermit,
            NodeManagerAdmissionCapability required,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeManagerCallbackPermit? permit = FindActiveCallbackPermit(callbackPermit);
                if (permit is null)
                {
                    return await AcquireUnborrowedAdmissionAsync(required, cancellationToken).ConfigureAwait(false);
                }

                NodeManagerAdmissionCapability inherited = permit.Capabilities;
                NodeManagerAdmissionCapability missing = required & ~inherited;
                if (missing == NodeManagerAdmissionCapability.None)
                {
                    NodeManagerCallbackPermit.Borrow? borrow = await permit.BorrowAsync(inherited, cancellationToken)
                        .ConfigureAwait(false);
                    if (borrow is not null)
                    {
                        try
                        {
                            var admission = new BatchAdmission(null, null, borrow);
                            borrow = null;
                            return admission;
                        }
                        finally
                        {
                            borrow?.Dispose();
                        }
                    }
                    continue;
                }

                SemaphoreSlim missingSemaphore = missing switch
                {
                    NodeManagerAdmissionCapability.AddressSpaceMutation => m_dynamicMutationSemaphore,
                    NodeManagerAdmissionCapability.ServerLifecycle => m_startupShutdownSemaphoreSlim,
                    _ => throw new InvalidOperationException("The callback admission has invalid capabilities.")
                };
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    SemaphoreSlim? acquired = null;
                    NodeManagerCallbackPermit.Borrow? borrow = null;
                    try
                    {
                        if (attempt == 0)
                        {
                            await missingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                            acquired = missingSemaphore;
                            borrow = permit.TryBorrow(inherited);
                        }
                        else
                        {
                            borrow = await permit.BorrowAsync(inherited, cancellationToken).ConfigureAwait(false);
                            if (borrow is null || !missingSemaphore.Wait(0, CancellationToken.None))
                            {
                                continue;
                            }
                            acquired = missingSemaphore;
                        }
                        if (borrow is null)
                        {
                            if (!permit.IsActive)
                            {
                                break;
                            }
                            continue;
                        }

                        var admission = new BatchAdmission(
                            missing == NodeManagerAdmissionCapability.AddressSpaceMutation ? acquired : null,
                            missing == NodeManagerAdmissionCapability.ServerLifecycle ? acquired : null,
                            borrow);
                        acquired = null;
                        borrow = null;
                        return admission;
                    }
                    finally
                    {
                        try
                        {
                            acquired?.Release();
                        }
                        finally
                        {
                            borrow?.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Acquires host semaphores without retaining one while waiting for the other.
        /// </summary>
        private async ValueTask<INodeManagerBatchAdmission> AcquireUnborrowedAdmissionAsync(
            NodeManagerAdmissionCapability required,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                SemaphoreSlim? addressSpaceAdmission = null;
                SemaphoreSlim? serverLifecycleAdmission = null;
                try
                {
                    if ((required & NodeManagerAdmissionCapability.AddressSpaceMutation) != 0)
                    {
                        await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                        addressSpaceAdmission = m_dynamicMutationSemaphore;
                    }
                    if ((required & NodeManagerAdmissionCapability.ServerLifecycle) != 0)
                    {
                        if (addressSpaceAdmission is null)
                        {
                            await m_startupShutdownSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                            serverLifecycleAdmission = m_startupShutdownSemaphoreSlim;
                        }
                        else if (m_startupShutdownSemaphoreSlim.Wait(0, CancellationToken.None))
                        {
                            serverLifecycleAdmission = m_startupShutdownSemaphoreSlim;
                        }
                        else
                        {
                            addressSpaceAdmission.Release();
                            addressSpaceAdmission = null;
                            await m_startupShutdownSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                            serverLifecycleAdmission = m_startupShutdownSemaphoreSlim;
                            if (!m_dynamicMutationSemaphore.Wait(0, CancellationToken.None))
                            {
                                continue;
                            }
                            addressSpaceAdmission = m_dynamicMutationSemaphore;
                        }
                    }
                    var admission = new BatchAdmission(addressSpaceAdmission, serverLifecycleAdmission);
                    addressSpaceAdmission = null;
                    serverLifecycleAdmission = null;
                    return admission;
                }
                finally
                {
                    serverLifecycleAdmission?.Release();
                    addressSpaceAdmission?.Release();
                }
            }
        }

        private static NodeManagerCallbackPermit? FindActiveCallbackPermit(NodeManagerCallbackPermit? permit)
        {
            for (; permit is not null; permit = permit.Previous)
            {
                if (permit.IsActive && permit.Capabilities != NodeManagerAdmissionCapability.None)
                {
                    return permit;
                }
            }
            return null;
        }

        /// <summary>
        /// Commits a prepared node-manager batch under its captured lifecycle admission.
        /// </summary>
        /// <param name="context">The prepared batch and its atomic lifecycle bookkeeping.</param>
        /// <returns>Reference reconciliation owned independently of the publication reservation.</returns>
        ValueTask<PublishedReferenceChanges?> IDynamicNodeManagerBatchHost.CommitBatchAsync(
            NodeManagerPublicationPlan context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return m_hostBoundary is { } hostBoundary
                ? hostBoundary.CommitBatchAsync(context, CommitBatchCoreAsync)
                : CommitBatchCoreAsync(context);
        }

        /// <summary>
        /// Commits a prepared batch through the master NodeManager host.
        /// </summary>
        /// <param name="context">The prepared batch and its atomic bookkeeping.</param>
        /// <returns>The committed reference changes, when publication occurs.</returns>
        private async ValueTask<PublishedReferenceChanges?> CommitBatchCoreAsync(
            NodeManagerPublicationPlan context)
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
            INodeManagerPublicationCommit commit = context.Commit;
            CancellationToken cancellationToken = context.CancellationToken;

            INodeManagerBatchAdmission admission = context.Admission ??
                throw new InvalidOperationException("Lifecycle commit admission must precede mutation ownership.");
            var referenceUpdates = new List<NodeState.ReferenceUpdate>();
            var referenceOwners = new Dictionary<NodeState.ReferenceUpdate, NodeManagerGeneration>();
            NodeManagerGeneration[] publishedOwners = [];
            var referenceAdditions = new Dictionary<
                IAsyncNodeManager,
                Dictionary<NodeId, IList<IReference>>>(RefEqualityComparer.Default);
            bool dynamicMutationSemaphoreHeld =
                admission.Includes(NodeManagerAdmissionCapability.AddressSpaceMutation);
            bool startupShutdownSemaphoreHeld =
                admission.Includes(NodeManagerAdmissionCapability.ServerLifecycle);
            bool decisionCommitted = false;
            bool lifecycleDecisionWindowStarted = false;
            bool namespaceRegistrationAdmissionHeld = false;
            LifecycleWriter.Mutation.DecisionWindow? publicationScope = null;
            NamespacePublication? namespaceReservation = null;
            NodeManagerRoutingTable.PreparedRoutes? routes = null;
            PublishedReferenceChanges? committedReferences = null;
            try
            {
                await m_namespaceRegistrationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                namespaceRegistrationAdmissionHeld = true;
                publicationScope = commit.EnterPublicationScope();
                Volatile.Write(ref m_namespacePublicationOwner, publicationScope);
                if (!dynamicMutationSemaphoreHeld)
                {
                    throw new InvalidOperationException(
                        "Lifecycle commit lacks address-space mutation admission.");
                }
                try
                {
                    if (!startupShutdownSemaphoreHeld)
                    {
                        throw new InvalidOperationException(
                            "Lifecycle commit lacks server lifecycle admission.");
                    }
                    try
                    {
                        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                        {
                            PreparedNodeManager candidate = candidates[candidateIndex];
                            ValidatePreparedNodeManager(candidate);
                        }
                        HashSet<IAsyncNodeManager> retiring = CreateRetiringSet(removed);
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
                            if (!m_nodeManagers.Revision.TryGetGeneration(
                                manager, out NodeManagerGeneration? generation))
                            {
                                throw new InvalidOperationException("A batch retirement is no longer registered.");
                            }
                            retiringGenerations.Add(generation!);
                        }

                        // A rebased batch merges only its own registrations onto the serving images; the
                        // revisions it merged onto are the ones reserved below.
                        typeTree = originalTypes.PrepareAdditiveMerge(
                            typeTree, originalTypes, typeRevision, context.Rebase, out typeRevision);
                        factory = originalFactory.PrepareAdditiveMerge(
                            factory, originalFactory, factoryRevision, context.Preparation.NamespaceUris,
                            context.Rebase, out factoryRevision);
                        PreparedAddressSpaceReferences references = context.References ??
                            throw new InvalidOperationException("Reference discovery must precede publication.");
                        if (!ReferenceEquals(references.View, m_nodeManagers.Revision) ||
                            !ReferenceEquals(references.Previous, m_dynamicExternalReferences))
                        {
                            throw new OptimisticPublicationConflictException("The reference preparation is stale.");
                        }
                        var nextReferences = references.Next;
                        referenceAdditions = references.Additions;
                        foreach (KeyValuePair<NodeState.ReferenceUpdate, NodeManagerGeneration> source in
                            references.Owners)
                        {
                            referenceUpdates.Add(source.Key);
                            referenceOwners.Add(source.Key, source.Value);
                        }

                        // Route preparation can invoke context-free manager properties and enumerate their results.
                        using (publicationScope.EnterCallbackFlow())
                        {
                            routes = m_nodeManagers.PrepareBatch(
                                candidates, removed,
                                manager => (manager.NamespaceUris ?? Enumerable.Empty<string>())
                                    .Concat(ChangePreparer.GetServer(manager)?.NamespaceRegistrations ??
                                        Enumerable.Empty<string>())
                                    .Select(uri => (int)context.Preparation.NamespaceUris.GetIndexOrAppend(uri)),
                                originalTypes, originalFactory, readImages);
                        }
                        OverrideBrowsePublicationValidator.Validate(m_nodeManagers.Revision, routes.Next);
                        routes.Reserve();
                        using TypeTable.Publication types =
                            Server.TypeTree.BeginPublication(originalTypes, typeRevision);
                        using EncodeableFactory.Publication registrations =
                            originalFactory.BeginPublication(originalFactory, factoryRevision);
                        using StringTable.Publication namespaces = Server.NamespaceUris.BeginPublication(
                            context.Preparation.NamespaceUris, context.Preparation.NamespaceRevision, context.Rebase);
                        namespaceReservation = new NamespacePublication(
                            routes, namespaces,
                            context.Preparation.NamespaceUris.ToArray().Skip(Server.NamespaceUris.Count));
                        using StringTable.Publication serverUris = Server.ServerUris.BeginPublication(
                            context.Preparation.ServerUris, context.Preparation.ServerRevision, context.Rebase);
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
                            referenceOwners,
                            historianRegistrationsChanged =>
                            {
                                commit.Publish(historianRegistrationsChanged);
                                m_dynamicExternalReferences = nextReferences;
                                // Deletions during the decision window queued pruning against the prepared image.
                                ApplyDeferredReferencePrunes();
                                if (!m_deferredServingRemovals.IsEmpty || !m_deferredTypeRemovals.IsEmpty)
                                {
                                    // Restarts a drain that stopped after a failed retry kept its removals queued.
                                    ScheduleDeferredReferenceDrain();
                                }
                                TrackRetiringEventGenerations(retiringGenerations);
                                foreach (PreparedNodeManager candidate in candidates)
                                {
                                    candidate.Publish();
                                    SetPreparing(candidate.NodeManager, preparing: false);
                                }
                            },
                            context.Progress);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!commit.Publishes)
                        {
                            return null;
                        }
                        BeginLifecycleDecisionWindow(publicationScope);
                        lifecycleDecisionWindowStarted = true;
                        // The lifecycle writer excludes server teardown; do not hold the startup/shutdown semaphore
                        // while the durable decision callback performs external I/O. The prepared batch retains
                        // its lifecycle operation until commit or disposal, so shutdown cannot dispose this host.
                        if (startupShutdownSemaphoreHeld)
                        {
                            admission.Release(NodeManagerAdmissionCapability.ServerLifecycle);
                            startupShutdownSemaphoreHeld = false;
                        }
                        if (dynamicMutationSemaphoreHeld)
                        {
                            admission.Release(NodeManagerAdmissionCapability.AddressSpaceMutation);
                            dynamicMutationSemaphoreHeld = false;
                        }
                        // Enter the decision under admission so its final revision check precedes permitted aliases.
                        ValueTask decision = commit.DecideAsync(publicationScope, cancellationToken);
                        if (decision.IsCompleted)
                        {
                            await decision.ConfigureAwait(false);
                            decisionCommitted = true;
                        }
                        else
                        {
                            m_namespacePublication = namespaceReservation;
                            m_namespaceRegistrationSemaphore.Release();
                            namespaceRegistrationAdmissionHeld = false;
                            try
                            {
                                await decision.ConfigureAwait(false);
                                decisionCommitted = true;
                            }
                            finally
                            {
                                // Reacquire before either publication or reservation disposal. A completed durable
                                // decision cannot be cancelled while excluding an in-flight permitted alias mutation.
                                await m_namespaceRegistrationSemaphore.WaitAsync(CancellationToken.None)
                                    .ConfigureAwait(false);
                                namespaceRegistrationAdmissionHeld = true;
                            }
                        }

                        // The publication invocation remains an active lifecycle operation until
                        // reconciliation finishes, so shutdown cannot tear down this host first.
                        publisher.Publish();
                        publishedOwners =
                            m_nodeManagers.Select(manager => m_nodeManagers.GetGeneration(manager)).ToArray();
                        committedReferences = new PublishedReferenceChanges(
                            candidates, publishedOwners, referenceAdditions, referenceOwners);
                        context.Commit.CaptureCommittedReferences(committedReferences);
                        context.CaptureCommittedReferences(committedReferences);
                        // Callbacks may mutate the committed image after internal publication bookkeeping.
                        namespaces.Dispose();
                        serverUris.Dispose();
                        types.Dispose();
                        registrations.Dispose();
                        foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                        {
                            update.Dispose();
                        }
                        routes.Dispose();
                        // Application-state reconciliation can re-enter lifecycle publication after reservations
                        // release.
                        EndLifecycleDecisionWindow();
                        lifecycleDecisionWindowStarted = false;
                        publicationScope.Dispose();
                        Volatile.Write(ref m_namespacePublicationOwner, null);
                        if (ReferenceEquals(m_namespacePublication, namespaceReservation))
                        {
                            m_namespacePublication = null;
                        }
                        namespaceReservation.ReleaseWaiters();
                        m_namespaceRegistrationSemaphore.Release();
                        namespaceRegistrationAdmissionHeld = false;
                    }
                    finally
                    {
                        try
                        {
                            foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                            {
                                update.Dispose();
                            }
                            routes?.Dispose();
                        }
                        finally
                        {
                            if (lifecycleDecisionWindowStarted)
                            {
                                EndLifecycleDecisionWindow();
                                lifecycleDecisionWindowStarted = false;
                            }
                            if (startupShutdownSemaphoreHeld)
                            {
                                admission.Release(NodeManagerAdmissionCapability.ServerLifecycle);
                                startupShutdownSemaphoreHeld = false;
                            }
                        }
                    }
                }
                finally
                {
                    if (dynamicMutationSemaphoreHeld)
                    {
                        admission.Release(NodeManagerAdmissionCapability.AddressSpaceMutation);
                        dynamicMutationSemaphoreHeld = false;
                    }
                }
                return committedReferences;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException && !decisionCommitted)
            {
                throw;
            }
            finally
            {
                if (namespaceRegistrationAdmissionHeld)
                {
                    publicationScope?.Dispose();
                    Volatile.Write(ref m_namespacePublicationOwner, null);
                    if (ReferenceEquals(m_namespacePublication, namespaceReservation))
                    {
                        m_namespacePublication = null;
                    }
                    namespaceReservation?.ReleaseWaiters();
                    m_namespaceRegistrationSemaphore.Release();
                }
                if (dynamicMutationSemaphoreHeld)
                {
                    admission.Release(NodeManagerAdmissionCapability.AddressSpaceMutation);
                }
                admission.Dispose();
                foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                {
                    update.Dispose();
                }
            }
        }

        /// <summary>
        /// Resolves the in-memory node that can contribute an abortable reference update.
        /// </summary>
        private static NodeState? GetPreparedReferenceNode(
            IAsyncNodeManager owner,
            NodeId sourceId,
            object handle)
        {
            if (handle is not NodeHandle { Validated: true, Node: { } node })
            {
                return null;
            }
            if (owner is AsyncCustomNodeManager)
            {
                return node;
            }
#pragma warning disable CS0618, UA0031 // Preserve reference staging for legacy synchronous owners until they are removed.
            if (owner.SyncNodeManager is CustomNodeManager2)
            {
                return node;
            }
#pragma warning restore CS0618, UA0031
            // A facade may return a validated handle without owning an in-memory node.
            // Only adopt the handle when the underlying manager indexes that exact instance.
            return NodeManagerGenerationRegistry.GetIdentity(owner) is AsyncCustomNodeManager resource &&
                ReferenceEquals(resource.Find(sourceId), node)
                ? node
                : null;
        }

        /// <summary>
        /// Collects the exact managers explicitly retired by a lifecycle batch.
        /// </summary>
        private static HashSet<IAsyncNodeManager> CreateRetiringSet(ArrayOf<IAsyncNodeManager> removed)
        {
            var retiring = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
            foreach (IAsyncNodeManager manager in removed)
            {
                retiring.Add(manager);
            }
            return retiring;
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
                new Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>>(RefEqualityComparer.Default);
            var deferredRemovals = new Dictionary<IAsyncNodeManager, List<LocalReference>>(RefEqualityComparer.Default);
            var newOwners = new HashSet<IAsyncNodeManager>(RefEqualityComparer.Default);
            foreach (PreparedNodeManager candidate in candidates)
            {
                newOwners.Add(candidate.NodeManager);
            }
            IAsyncNodeManager[] owners =
            [
                .. view.VisibleNodeManagers.Where(manager => !retiring.Contains(manager)),
                .. newOwners
            ];
            var nodes = new HashSet<NodeState>(RefEqualityComparer.Default);
            foreach (NodeId sourceId in previous.Keys.Union(next.Keys))
            {
                previous.TryGetValue(sourceId, out ReferenceDictionary<bool>? oldReferences);
                next.TryGetValue(sourceId, out ReferenceDictionary<bool>? newReferences);
                ArrayOf<IReference> additions = newReferences is null ? [] : [.. newReferences.Keys];
                ArrayOf<IReference> removals = oldReferences is null
                    ? []
                    : [.. oldReferences.Keys.Where(reference => newReferences?.ContainsKey(reference) != true)];
                bool contributionsChanged = removals.Count != 0 ||
                    newReferences?.Keys.Any(reference => oldReferences?.ContainsKey(reference) != true) == true;
                var ownerNodes = new List<(IAsyncNodeManager Owner, NodeState Node)>();
                bool hasUnsupportedOwner = false;
                foreach (IAsyncNodeManager owner in owners)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    NodeManagerGeneration? generation = newOwners.Contains(owner) ? null : view.GetGeneration(owner);
                    int stripe = 0;
                    if (generation is not null && !generation.Gate.TryEnter(out stripe))
                    {
                        throw new NodeManagerRoutingConflictException(
                            "A reference owner retired during discovery.", view)
                        {
                            StaleDiscovery = true
                        };
                    }
                    NodeState? node;
                    try
                    {
                        object? handle = await owner.GetManagerHandleAsync(sourceId, cancellationToken)
                            .ConfigureAwait(false);
                        if (handle is null)
                        {
                            continue;
                        }
                        node = GetPreparedReferenceNode(owner, sourceId, handle);
                        if (node is null)
                        {
                            hasUnsupportedOwner = true;
                            continue;
                        }
                    }
                    finally
                    {
                        generation?.Gate.Exit(stripe);
                    }
                    if (!nodes.Add(node))
                    {
                        throw new NotSupportedException(
                            "A prepared reference source has multiple NodeManager owners.");
                    }
                    ownerNodes.Add((owner, node));
                    bool newOwner = newOwners.Contains(owner);
                    if (newOwner || contributionsChanged)
                    {
                        sources.Add(new PreparedReferenceSource(
                            owner, node.PrepareReferences(additions, newOwner ? [] : removals)));
                    }
                }
                if (hasUnsupportedOwner && ownerNodes.Count > 0)
                {
                    throw new NotSupportedException(
                        "Prepared external references have both in-memory and external owners.");
                }
                if (!hasUnsupportedOwner && ownerNodes.Count == 0)
                {
                    foreach (PreparedNodeManager candidate in candidates)
                    {
                        if (ChangePreparer.GetServer(candidate.NodeManager)?.RequiresExternalSource(sourceId) == true)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdUnknown, "A staged external reference source is not available.");
                        }
                    }
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
