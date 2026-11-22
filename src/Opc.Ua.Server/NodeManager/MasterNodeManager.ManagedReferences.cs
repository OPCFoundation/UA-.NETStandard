// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Adds a live reference to every in-memory source owner and records its contribution.
        /// </summary>
        /// <returns>False when the source requires the existing opaque-owner path.</returns>
        internal async ValueTask<bool> TryAddManagedNodeReferenceAsync(
            AsyncCustomNodeManager owner,
            NodeState target,
            LocalReference reference,
            CancellationToken cancellationToken)
        {
            NodeManagerCallbackPermit? currentPermit = CaptureLifecycleCallbackPermit();
            if (currentPermit is { IsActive: true, Capabilities: NodeManagerAdmissionCapability.None })
            {
                if (await HasOpaqueReferenceSourceAsync(reference, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
                throw new InvalidOperationException(
                    "A reference cannot be added from this deletion callback while another " +
                    "address-space change is in progress.");
            }
            using INodeManagerBatchAdmission admission = await AcquireDynamicMutationAdmissionAsync(
                currentPermit, cancellationToken).ConfigureAwait(false);
            var sources = new List<LiveReferenceSource>();
            bool targetAdded = false;
            bool targetAttempted = false;
            bool contributionRecorded = false;
            bool rollbackAttempted = false;
            IAsyncNodeManager? facade = null;
            NodeManagerGeneration? targetGeneration = null;
            int targetSlot = -1;
            await using var callbackPermit = new LegacyNodeManagerCallbackScope(
                m_lifecycleCallbackPermit,
                GetAdmissionCapabilities(admission));
            try
            {
                ApplyDeferredReferencePrunes();
                facade = GetPublishedRootNotifierOwner(owner);
                if (facade is null)
                {
                    return false;
                }
                while (true)
                {
                    using var context = new OperationContext(
                        new RequestHeader(), null, RequestType.AddReferences, RequestLifetime.None);
                    context.PinRouting(m_nodeManagers);
                    NodeManagerRoutingTable.RoutingSnapshot view = context.RoutingSnapshot!;
                    if (!Array.Exists(view.VisibleNodeManagers, manager => ReferenceEquals(manager, facade)))
                    {
                        throw new InvalidOperationException("The reference target owner is no longer published.");
                    }
                    NodeManagerGeneration currentGeneration = view.GetGeneration(facade);
                    if (targetGeneration is null)
                    {
                        if (!currentGeneration.Gate.TryEnter(out targetSlot))
                        {
                            throw new InvalidOperationException("The reference target owner retired.");
                        }
                        targetGeneration = currentGeneration;
                    }
                    if (!ReferenceEquals(targetGeneration, currentGeneration) ||
                        !ReferenceEquals(owner.Find(target.NodeId), target))
                    {
                        throw new InvalidOperationException("The reference target was removed or replaced.");
                    }

                    bool found = false;
                    bool opaque = false;
                    foreach (IAsyncNodeManager source in GetReferenceSourceOwners(view, reference.SourceId))
                    {
                        if (!NodeManagerServiceDispatcher.CanDispatchTo(view, source, null))
                        {
                            continue;
                        }
                        NodeManagerGeneration generation = view.GetGeneration(source);
                        if (sources.Exists(entry =>
                            ReferenceEquals(entry.Owner, source) && ReferenceEquals(entry.Generation, generation)))
                        {
                            found = true;
                            continue;
                        }
                        if (!generation.Gate.TryEnter(out int slot))
                        {
                            throw new InvalidOperationException("A reference source retired during discovery.");
                        }
                        bool retained = false;
                        try
                        {
                            object? handle = await source.GetManagerHandleAsync(reference.SourceId, cancellationToken)
                                .ConfigureAwait(false);
                            if (handle is null)
                            {
                                continue;
                            }
                            found = true;
                            if (handle is not NodeHandle { Validated: true, Node: { } node })
                            {
                                opaque = true;
                                break;
                            }
                            if (!sources.Exists(entry =>
                                ReferenceEquals(entry.Node, node) &&
                                Array.Exists(
                                    view.VisibleNodeManagers, manager => ReferenceEquals(manager, entry.Owner)) &&
                                ReferenceEquals(view.GetGeneration(entry.Owner), entry.Generation)))
                            {
                                sources.Add(new LiveReferenceSource(source, generation, slot, handle, node));
                                retained = true;
                            }
                        }
                        finally
                        {
                            if (!retained)
                            {
                                generation.Gate.Exit(slot);
                            }
                        }
                    }
                    if (opaque)
                    {
                        rollbackAttempted = true;
                        List<Exception> rollbackFailures = await RollbackManagedReferenceAsync(
                            owner, target, reference, sources, targetAdded).ConfigureAwait(false);
                        if (rollbackFailures.Count != 0)
                        {
                            throw new AggregateException(
                                "The in-memory reference changes could not be rolled back.", rollbackFailures);
                        }
                        return false;
                    }
                    if (!found)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdUnknown, "The reference source is not available.");
                    }
                    if (!targetAttempted)
                    {
                        targetAttempted = true;
                        target.AddReferenceIfMissing(
                            reference.ReferenceTypeId, !reference.IsInverse, reference.SourceId, out targetAdded);
                    }
                    foreach (LiveReferenceSource source in sources)
                    {
                        if (source.Attempted || !IsCurrentLiveReferenceSource(source))
                        {
                            continue;
                        }
                        source.Existed = source.Node.ReferenceExists(
                            reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                        source.Attempted = true;
                        await source.Owner.AddReferencesAsync(
                            CreateReferenceAddition(reference), cancellationToken).ConfigureAwait(false);
                        if (IsCurrentLiveReferenceSource(source))
                        {
                            object? current = await source.Owner.GetManagerHandleAsync(
                                reference.SourceId, cancellationToken).ConfigureAwait(false);
                            if (IsCurrentLiveReferenceSource(source))
                            {
                                if (current is not NodeHandle { Validated: true, Node: { } node } ||
                                    !ReferenceEquals(node, source.Node))
                                {
                                    throw new InvalidOperationException(
                                        "The reference source was removed or replaced.");
                                }
                                if (!source.Node.ReferenceExists(
                                    reference.ReferenceTypeId, reference.IsInverse, reference.TargetId))
                                {
                                    throw new InvalidOperationException(
                                        "The reference source did not retain the added reference.");
                                }
                            }
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ReferenceEquals(view, m_nodeManagers.Revision))
                    {
                        break;
                    }
                }

                if (!ReferenceEquals(owner.Find(target.NodeId), target) || !IsPublishedNodeManager(facade))
                {
                    throw new InvalidOperationException("The reference target was removed or replaced.");
                }
                RecordManagedReference(owner, facade, target, reference);
                contributionRecorded = true;
                if (!ReferenceEquals(owner.Find(target.NodeId), target))
                {
                    throw new InvalidOperationException("The reference target was removed or replaced.");
                }
                return true;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                if (rollbackAttempted)
                {
                    throw;
                }
                if (contributionRecorded && facade is not null)
                {
                    var removed = new List<LocalReference>
                    {
                        new(
                            reference.SourceId,
                            reference.ReferenceTypeId,
                            reference.IsInverse,
                            reference.TargetId,
                            target)
                    };
                    PruneDynamicExternalReferences(facade, removed);
                    ChangePreparer.GetServer(owner)?.DiscardCapturedReferences(removed);
                }
                List<Exception> rollbackFailures = await RollbackManagedReferenceAsync(
                    owner, target, reference, sources, targetAdded).ConfigureAwait(false);
                if (rollbackFailures.Count != 0)
                {
                    rollbackFailures.Insert(0, failure);
                    throw new AggregateException(
                        "Reference addition and rollback both failed.", rollbackFailures);
                }
                throw;
            }
            finally
            {
                foreach (LiveReferenceSource source in sources)
                {
                    source.Generation.Gate.Exit(source.Slot);
                }
                if (targetGeneration is not null)
                {
                    targetGeneration.Gate.Exit(targetSlot);
                }
            }
        }

        /// <summary>
        /// Creates one source callback's reference addition.
        /// </summary>
        private static Dictionary<NodeId, IList<IReference>> CreateReferenceAddition(LocalReference reference)
        {
            return new Dictionary<NodeId, IList<IReference>>
            {
                [reference.SourceId] =
                [
                    new NodeStateReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)
                ]
            };
        }

        /// <summary>
        /// Checks whether a captured source still serves through the same generation.
        /// </summary>
        private bool IsCurrentLiveReferenceSource(LiveReferenceSource source)
        {
            NodeManagerRoutingTable.RoutingSnapshot view = m_nodeManagers.Revision;
            return Array.Exists(view.VisibleNodeManagers, manager => ReferenceEquals(manager, source.Owner)) &&
                NodeManagerServiceDispatcher.CanDispatchTo(view, source.Owner, null) &&
                ReferenceEquals(view.GetGeneration(source.Owner), source.Generation);
        }

        /// <summary>
        /// Checks whether a serving source requires the legacy opaque-owner path.
        /// </summary>
        private async ValueTask<bool> HasOpaqueReferenceSourceAsync(
            LocalReference reference,
            CancellationToken cancellationToken)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.AddReferences, RequestLifetime.None);
            context.PinRouting(m_nodeManagers);
            NodeManagerRoutingTable.RoutingSnapshot view = context.RoutingSnapshot!;
            foreach (IAsyncNodeManager source in GetReferenceSourceOwners(view, reference.SourceId))
            {
                if (!NodeManagerServiceDispatcher.CanDispatchTo(view, source, null))
                {
                    continue;
                }
                NodeManagerGeneration generation = view.GetGeneration(source);
                if (!generation.Gate.TryEnter(out int slot))
                {
                    throw new InvalidOperationException("A reference source retired during discovery.");
                }
                try
                {
                    object? handle = await source.GetManagerHandleAsync(reference.SourceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (handle is not null && handle is not NodeHandle { Validated: true, Node: not null })
                    {
                        return true;
                    }
                }
                finally
                {
                    generation.Gate.Exit(slot);
                }
            }
            return false;
        }

        /// <summary>
        /// Removes additions made by a failed live reference operation.
        /// </summary>
        private async ValueTask<List<Exception>> RollbackManagedReferenceAsync(
            AsyncCustomNodeManager owner,
            NodeState target,
            LocalReference reference,
            List<LiveReferenceSource> sources,
            bool targetAdded)
        {
            var failures = new List<Exception>();
            for (int index = sources.Count - 1; index >= 0; index--)
            {
                LiveReferenceSource source = sources[index];
                if (!source.Attempted)
                {
                    continue;
                }
                if (source.Existed)
                {
                    try
                    {
                        await RestorePreexistingReferenceAsync(source, reference).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        failures.Add(ex);
                    }
                    continue;
                }
                if (HasReferenceContribution(reference) ||
                    ReplacementOwnsReference(owner, target, reference))
                {
                    continue;
                }
                try
                {
                    object? current = await source.Owner.GetManagerHandleAsync(
                        reference.SourceId, CancellationToken.None).ConfigureAwait(false);
                    if (current is not NodeHandle { Validated: true, Node: { } currentNode } ||
                        !ReferenceEquals(currentNode, source.Node) ||
                        HasReferenceContribution(reference) ||
                        ReplacementOwnsReference(owner, target, reference))
                    {
                        continue;
                    }
                    ServiceResult result = await source.Owner.DeleteReferenceAsync(
                        source.Handle,
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        reference.TargetId,
                        false,
                        CancellationToken.None).ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        throw new ServiceResultException(result);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
                try
                {
                    await RestoreReplacementReferenceAsync(
                        owner, target, reference, source.Owner, source.Node).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
            }
            if (targetAdded && !HasReferenceContribution(reference) && failures.Count == 0)
            {
                try
                {
                    target.RemoveReference(
                        reference.ReferenceTypeId, !reference.IsInverse, reference.SourceId);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
            }
            return failures;
        }

        /// <summary>
        /// Restores a reference that existed before a failed source callback.
        /// </summary>
        private static async ValueTask RestorePreexistingReferenceAsync(
            LiveReferenceSource source,
            LocalReference reference)
        {
            if (source.Node.ReferenceExists(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId))
            {
                return;
            }
            object? current = await source.Owner.GetManagerHandleAsync(
                reference.SourceId, CancellationToken.None).ConfigureAwait(false);
            if (current is not NodeHandle { Validated: true, Node: { } node } ||
                !ReferenceEquals(node, source.Node))
            {
                throw new InvalidOperationException(
                    "A pre-existing reference could not be restored because its source changed.");
            }
            await source.Owner.AddReferencesAsync(
                CreateReferenceAddition(reference), CancellationToken.None).ConfigureAwait(false);
            if (!source.Node.ReferenceExists(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId))
            {
                throw new InvalidOperationException("The source did not restore its pre-existing reference.");
            }
        }

        /// <summary>
        /// Checks whether a replacement target has registered the reciprocal reference.
        /// </summary>
        private static bool ReplacementOwnsReference(
            AsyncCustomNodeManager owner,
            NodeState deleted,
            LocalReference reference)
        {
            return owner.Find(reference.TargetId) is { } replacement &&
                !ReferenceEquals(replacement, deleted) &&
                replacement.ReferenceExists(
                    reference.ReferenceTypeId, !reference.IsInverse, reference.SourceId);
        }

        /// <summary>
        /// Checks whether a recorded contribution still owns a reference.
        /// </summary>
        private bool HasReferenceContribution(LocalReference reference)
        {
            if (ContainsReferenceContribution(m_foundationExternalReferences, reference))
            {
                return true;
            }
            foreach (Dictionary<NodeId, IList<IReference>> contribution in m_dynamicExternalReferences.Values)
            {
                if (ContainsReferenceContribution(contribution, reference))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Finds a reference in one contribution set.
        /// </summary>
        private static bool ContainsReferenceContribution(
            Dictionary<NodeId, IList<IReference>>? contributions,
            LocalReference reference)
        {
            if (contributions is null ||
                !contributions.TryGetValue(reference.SourceId, out IList<IReference>? references))
            {
                return false;
            }
            foreach (IReference candidate in references)
            {
                if (candidate.ReferenceTypeId == reference.ReferenceTypeId &&
                    candidate.IsInverse == reference.IsInverse &&
                    !candidate.TargetId.IsAbsolute &&
                    (NodeId)candidate.TargetId == reference.TargetId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Publishes a copy-on-write contribution for a live reference.
        /// </summary>
        private void RecordManagedReference(
            AsyncCustomNodeManager owner,
            IAsyncNodeManager facade,
            NodeState target,
            LocalReference reference)
        {
            Dictionary<NodeId, IList<IReference>> contributions = m_dynamicExternalReferences.TryGetValue(
                facade, out Dictionary<NodeId, IList<IReference>>? previous)
                ? new Dictionary<NodeId, IList<IReference>>(previous)
                : [];
            List<IReference> references = contributions.TryGetValue(
                reference.SourceId, out IList<IReference>? existing)
                ? new List<IReference>(existing)
                : [];
            var addition = new OwnedExternalReference(reference.ReferenceTypeId, reference.IsInverse, target);
            int index = references.FindIndex(candidate =>
                candidate.ReferenceTypeId == reference.ReferenceTypeId &&
                candidate.IsInverse == reference.IsInverse &&
                !candidate.TargetId.IsAbsolute &&
                (NodeId)candidate.TargetId == reference.TargetId);
            if (index < 0)
            {
                references.Add(addition);
            }
            else
            {
                references[index] = addition;
            }
            contributions[reference.SourceId] = references;
            var next = new Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>>(
                m_dynamicExternalReferences, RefEqualityComparer.Default)
            {
                [facade] = contributions
            };
            ChangePreparer.GetServer(owner)?.CapturePublishedReference(reference, target);
            m_dynamicExternalReferences = next;
        }

        /// <summary>
        /// Holds one admitted source and its pre-addition reference state.
        /// </summary>
        private sealed class LiveReferenceSource(
            IAsyncNodeManager owner,
            NodeManagerGeneration generation,
            int slot,
            object handle,
            NodeState node)
        {
            /// <summary>
            /// Gets the registered source facade.
            /// </summary>
            public IAsyncNodeManager Owner { get; } = owner;

            /// <summary>
            /// Gets the admitted source generation.
            /// </summary>
            public NodeManagerGeneration Generation { get; } = generation;

            /// <summary>
            /// Gets the admission stripe.
            /// </summary>
            public int Slot { get; } = slot;

            /// <summary>
            /// Gets the source handle used for rollback.
            /// </summary>
            public object Handle { get; } = handle;

            /// <summary>
            /// Gets the physical source node.
            /// </summary>
            public NodeState Node { get; } = node;

            /// <summary>
            /// Gets or sets whether the source callback was invoked.
            /// </summary>
            public bool Attempted { get; set; }

            /// <summary>
            /// Gets or sets whether the reference was present before the callback.
            /// </summary>
            public bool Existed { get; set; }
        }
    }
}
