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
 *
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Prepares reciprocal destruction edits against one serving image without invoking observers.
        /// </summary>
        private async ValueTask<DestroyedReferenceCleanupPlan> PrepareDestroyedReferenceCleanupAsync(
            List<LocalReference> references,
            CancellationToken cancellationToken)
        {
            Task ready = Task.CompletedTask;
            while (true)
            {
                await ready.WaitAsync(cancellationToken).ConfigureAwait(false);
                await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                m_dynamicMutationSemaphore.Release();
                (DestroyedReferenceCleanupPlan? plan, ready) = await TryPrepareDestroyedReferenceCleanupAsync(
                    references, cancellationToken).ConfigureAwait(false);
                if (plan is not null)
                {
                    return plan;
                }
            }
        }

        /// <summary>
        /// Releases all pinned owners and prepared state before a contended cleanup waits or retries.
        /// </summary>
        private async ValueTask<(DestroyedReferenceCleanupPlan? Plan, Task Ready)>
            TryPrepareDestroyedReferenceCleanupAsync(
                List<LocalReference> references,
                CancellationToken cancellationToken)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.DeleteReferences, RequestLifetime.None);
            context.PinRouting(m_nodeManagers);
            NodeManagerRoutingTable.RoutingSnapshot view = context.RoutingSnapshot!;
            var targets = new Dictionary<NodeId, IAsyncNodeManager?>();
            var sources = new Dictionary<NodeId, (object? Handle, IAsyncNodeManager? Owner)>();
            var removals = new Dictionary<NodeState, List<IReference>>();
            var updates = new List<NodeState.ReferenceUpdate>();
            var owners = new HashSet<IAsyncNodeManager>();
            var admissions = new List<(GenerationGate Gate, int Slot)>();
            var external = new List<(IAsyncNodeManager Owner, object Handle, LocalReference Reference)>();
            var inherited = new List<(IAsyncNodeManager Owner, LocalReference Reference)>();
            bool transferred = false;
            bool mutationHeld = false;
            try
            {
                foreach (LocalReference reference in references)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!targets.TryGetValue(reference.TargetId, out IAsyncNodeManager? targetOwner))
                    {
                        (object? target, targetOwner) = await m_serviceDispatch.GetManagerHandleAsync(
                            context, reference.TargetId, cancellationToken).ConfigureAwait(false);
                        bool serving = false;
                        if (target is not null && targetOwner is not null)
                        {
                            GenerationGate gate = view.GetGeneration(targetOwner).Gate;
                            if (!gate.TryEnter(out int slot))
                            {
                                return (null, m_nodeManagers.GetPublicationChange(view));
                            }
                            try
                            {
                                serving = await targetOwner.GetNodeMetadataAsync(
                                    context, target, BrowseResultMask.None, cancellationToken)
                                    .ConfigureAwait(false) is not null;
                            }
                            finally
                            {
                                gate.Exit(slot);
                            }
                        }
                        if (!serving)
                        {
                            targetOwner = null;
                        }
                        targets.Add(reference.TargetId, targetOwner);
                    }
                    if (!sources.TryGetValue(reference.SourceId, out var source))
                    {
                        (object? candidateHandle, IAsyncNodeManager? candidateOwner) =
                            await m_serviceDispatch.GetManagerHandleAsync(
                                context, reference.SourceId, cancellationToken).ConfigureAwait(false);
                        if (candidateHandle is null || candidateOwner is null ||
                            ReferenceEquals(candidateOwner, targetOwner))
                        {
                            continue;
                        }
                        if (owners.Add(candidateOwner))
                        {
                            GenerationGate gate = view.GetGeneration(candidateOwner).Gate;
                            int slot = -1;
                            try
                            {
                                if (!gate.TryEnter(out slot))
                                {
                                    return (null, m_nodeManagers.GetPublicationChange(view));
                                }
                                admissions.Add((gate, slot));
                                slot = -1;
                            }
                            finally
                            {
                                if (slot >= 0)
                                {
                                    gate.Exit(slot);
                                }
                            }
                        }
                        source = await candidateOwner.GetNodeMetadataAsync(
                            context, candidateHandle, BrowseResultMask.None, cancellationToken)
                            .ConfigureAwait(false) is null
                            ? (null, null)
                            : (candidateHandle, candidateOwner);
                        sources.Add(reference.SourceId, source);
                    }
                    var (handle, owner) = source;
                    if (handle is null || owner is null || ReferenceEquals(owner, targetOwner))
                    {
                        continue;
                    }
                    if (targetOwner is not null)
                    {
                        inherited.Add((targetOwner, reference));
                        continue;
                    }
                    if (UsesDirectReferenceStorage(owner) &&
                        handle is NodeHandle { Validated: true, Node: { } node })
                    {
                        if (!removals.TryGetValue(node, out List<IReference>? pending))
                        {
                            removals.Add(node, pending = []);
                        }
                        pending.Add(new NodeStateReference(
                            reference.ReferenceTypeId, reference.IsInverse, reference.TargetId));
                    }
                    else
                    {
                        external.Add((owner, handle, reference));
                    }
                }
                if (removals.Count != 0 || external.Count != 0 || inherited.Count != 0)
                {
                    if (!await m_dynamicMutationSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                    {
                        return (null, Task.CompletedTask);
                    }
                    mutationHeld = true;
                    using NodeManagerRoutingTable.PreparedRoutes reservation = m_nodeManagers.PrepareBatch(
                        [], [], static _ => [], Server.TypeTree, view.Factory);
                    if (!reservation.TryReserve(out Task ready))
                    {
                        return (null, ready);
                    }
                    if (!ReferenceEquals(view, m_nodeManagers.Revision))
                    {
                        return (null, Task.CompletedTask);
                    }
                    foreach (KeyValuePair<NodeState, List<IReference>> removal in removals)
                    {
                        updates.Add(removal.Key.PrepareReferences([], [.. removal.Value]));
                    }
                    foreach (NodeState.ReferenceUpdate update in updates)
                    {
                        update.Reserve();
                    }
                    foreach (NodeState.ReferenceUpdate update in updates)
                    {
                        update.Remove();
                        update.Complete();
                    }
                    foreach (NodeState.ReferenceUpdate update in updates)
                    {
                        update.Dispose();
                    }
                    foreach ((IAsyncNodeManager owner, LocalReference reference) in inherited)
                    {
                        Dictionary<(NodeId, NodeId, bool, NodeId), LocalReference> pending =
                            m_inheritedReferenceCleanup.GetValue(owner, static _ => []);
                        pending[(reference.SourceId, reference.ReferenceTypeId,
                            reference.IsInverse, reference.TargetId)] = reference;
                    }
                }
                var plan = new DestroyedReferenceCleanupPlan(updates, admissions, external);
                transferred = true;
                return (plan, Task.CompletedTask);
            }
            finally
            {
                foreach (NodeState.ReferenceUpdate update in updates)
                {
                    update.Dispose();
                }
                if (mutationHeld)
                {
                    m_dynamicMutationSemaphore.Release();
                }
                if (!transferred)
                {
                    foreach ((GenerationGate gate, int slot) in admissions)
                    {
                        gate.Exit(slot);
                    }
                }
            }
        }

        /// <summary>
        /// Identifies built-in storage edits without bypassing a manager's deletion override.
        /// </summary>
        private static bool UsesDirectReferenceStorage(IAsyncNodeManager owner)
        {
            if (owner is AsyncCustomNodeManager asynchronous)
            {
                return UsesDirectReferenceStorage(asynchronous);
            }
            if (owner.SyncNodeManager is CustomNodeManager2 synchronous)
            {
                return UsesDirectReferenceStorage(synchronous);
            }
            return false;
        }

        /// <summary>
        /// Uses direct edits only when the base implementation is not overridden.
        /// </summary>
        private static bool UsesDirectReferenceStorage(AsyncCustomNodeManager owner)
        {
            return owner.GetType() == typeof(AsyncCustomNodeManager);
        }

        /// <summary>
        /// Uses direct edits only when the base implementation is not overridden.
        /// </summary>
        private static bool UsesDirectReferenceStorage(CustomNodeManager2 owner)
        {
            return owner.GetType() == typeof(CustomNodeManager2);
        }

        /// <summary>
        /// Retains only unfinished callbacks and their exact owner admissions after reference storage changes.
        /// </summary>
        /// <param name="updates">The applied node-owned reference edits.</param>
        /// <param name="admissions">The owner admissions retained until callbacks finish.</param>
        /// <param name="external">The legacy external-owner removals.</param>
        private sealed class DestroyedReferenceCleanupPlan(
            List<NodeState.ReferenceUpdate> updates,
            List<(GenerationGate Gate, int Slot)> admissions,
            List<(IAsyncNodeManager Owner, object Handle, LocalReference Reference)> external) : IDisposable
        {
            /// <summary>
            /// Notifies in-memory observers and completes legacy external-owner deletion outside publication.
            /// </summary>
            public async ValueTask CompleteAsync(CancellationToken cancellationToken)
            {
                var failures = new List<Exception>();
                foreach (NodeState.ReferenceUpdate update in updates)
                {
                    update.Notify(failures.Add);
                }
                if (failures.Count != 0)
                {
                    throw new AggregateException("Reciprocal reference observers remain pending.", failures);
                }
                while (m_nextExternal < external.Count)
                {
                    var (owner, handle, reference) = external[m_nextExternal];
                    ServiceResult result = await owner.DeleteReferenceAsync(
                        handle, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId,
                        false, cancellationToken).ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        throw new ServiceResultException(result);
                    }
                    m_nextExternal++;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                foreach ((GenerationGate gate, int slot) in admissions)
                {
                    gate.Exit(slot);
                }
            }

            /// <summary>Records completed external callbacks so a later retry does not repeat them.</summary>
            private int m_nextExternal;

            /// <summary>Prevents an admission from being released more than once.</summary>
            private int m_disposed;
        }

        /// <summary>Retains failed destruction cleanup until its owning retired manager is reclaimed.</summary>
        private readonly ConditionalWeakTable<IAsyncNodeManager, DestroyedReferenceCleanupPlan>
            m_destroyedReferenceCleanup = new();

        /// <summary>
        /// Transfers deduplicated cleanup obligations under serving-image reservation, consumed after owner withdrawal.
        /// </summary>
        private readonly ConditionalWeakTable<
            IAsyncNodeManager,
            Dictionary<(NodeId, NodeId, bool, NodeId), LocalReference>> m_inheritedReferenceCleanup = new();
    }
}
