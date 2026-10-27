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
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.DeleteReferences, RequestLifetime.None);
            context.PinRouting(m_nodeManagers);
            NodeManagerRoutingTable.RoutingSnapshot view = context.RoutingSnapshot!;
            var targets = new Dictionary<NodeId, bool>();
            var removals = new Dictionary<NodeState, List<IReference>>();
            var updates = new List<NodeState.ReferenceUpdate>();
            var owners = new HashSet<IAsyncNodeManager>();
            var admissions = new List<(GenerationGate Gate, int Slot)>();
            var external = new List<(IAsyncNodeManager Owner, object Handle, LocalReference Reference)>();
            bool transferred = false;
            try
            {
                foreach (LocalReference reference in references)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!targets.TryGetValue(reference.TargetId, out bool serving))
                    {
                        (object? target, _) = await m_serviceDispatch.GetManagerHandleAsync(
                            context, reference.TargetId, cancellationToken).ConfigureAwait(false);
                        targets.Add(reference.TargetId, serving = target is not null);
                    }
                    if (serving)
                    {
                        continue;
                    }
                    (object? handle, IAsyncNodeManager? owner) = await m_serviceDispatch.GetManagerHandleAsync(
                        context, reference.SourceId, cancellationToken).ConfigureAwait(false);
                    if (handle is null || owner is null)
                    {
                        continue;
                    }
                    if (owners.Add(owner))
                    {
                        GenerationGate gate = view.GetGeneration(owner).Gate;
                        int slot = -1;
                        try
                        {
                            if (!gate.TryEnter(out slot))
                            {
                                throw new InvalidOperationException("A reciprocal reference owner has retired.");
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
                foreach (KeyValuePair<NodeState, List<IReference>> removal in removals)
                {
                    updates.Add(removal.Key.PrepareReferences([], [.. removal.Value]));
                }
                if (updates.Count != 0 || external.Count != 0)
                {
                    using NodeManagerRoutingTable.PreparedRoutes reservation = m_nodeManagers.PrepareBatch(
                        [], [], static _ => [], Server.TypeTree, view.Factory);
                    reservation.Reserve();
                    if (!ReferenceEquals(view, m_nodeManagers.Revision))
                    {
                        throw new InvalidOperationException("Routing changed during reciprocal reference cleanup.");
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
                }
                var plan = new DestroyedReferenceCleanupPlan(updates, admissions, external);
                transferred = true;
                return plan;
            }
            finally
            {
                foreach (NodeState.ReferenceUpdate update in updates)
                {
                    update.Dispose();
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
            if (owner is AsyncCustomNodeManager)
            {
                Func<object, NodeId, bool, ExpandedNodeId, bool, CancellationToken, ValueTask<ServiceResult>> deletion =
                    owner.DeleteReferenceAsync;
                return deletion.Method.DeclaringType == typeof(AsyncCustomNodeManager);
            }
            if (owner.SyncNodeManager is CustomNodeManager2 synchronous)
            {
                Func<object, NodeId, bool, ExpandedNodeId, bool, ServiceResult> deletion = synchronous.DeleteReference;
                return deletion.Method.DeclaringType == typeof(CustomNodeManager2);
            }
            return false;
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
    }
}
