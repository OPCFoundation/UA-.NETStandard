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
    public partial class MasterNodeManager : IDynamicNodeManagerBatchHost
    {
        NodeManagerRoutingTable.RoutingSnapshot IDynamicNodeManagerBatchHost.RoutingRevision
            => m_nodeManagers.Revision;

        IDisposable IDynamicNodeManagerBatchHost.UseLiveRouting()
        {
            return m_nodeManagers.UseLiveRouting();
        }

        IDisposable IDynamicNodeManagerBatchHost.UseTypeImage(TypeTable typeTree, EncodeableFactory factory)
        {
            return m_nodeManagers.UseTypeImage(typeTree, factory);
        }

        IAsyncDisposable IDynamicNodeManagerBatchHost.SuspendBindingAdmission()
        {
            return m_currentBindingAdmission.Value?.Suspend() ?? BindingAdmissionSuspension.Empty;
        }

        bool IDynamicNodeManagerBatchHost.IsRemovingBinding(IMonitoredItem monitoredItem)
        {
            return m_currentBindingAdmission.Value?.IsRemoving(monitoredItem) == true;
        }

        async ValueTask<(ByteString ServerNonce, ServiceResult ActivationStatus)>
            IDynamicNodeManagerBatchHost.DispatchSessionActivationAsync(
                Func<ValueTask<(ByteString ServerNonce, ServiceResult ActivationStatus)>> activateAsync,
                CancellationToken cancellationToken)
        {
        await m_bindingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        using BindingAdmission admission = EnterBindingAdmission();
        using IDisposable routing = m_nodeManagers.UseLiveRouting();
        return await activateAsync().ConfigureAwait(false);
        }

        async ValueTask IDynamicNodeManagerBatchHost.CommitBatchAsync(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            NodeManagerRoutingTable.RoutingSnapshot routingRevision,
            TypeTable typeTree,
            TypeTable originalTypes,
            long typeRevision,
            EncodeableFactory factory,
            EncodeableFactory originalFactory,
            long factoryRevision,
            Func<CancellationToken, ValueTask> decideAsync,
            Action published,
            Func<ValueTask> reconcileBindingsAsync,
            Action<Exception> reportCleanupFailure,
            CancellationToken cancellationToken)
        {
            var referenceUpdates = new List<NodeState.ReferenceUpdate>();
            bool bindingAdmissionHeld = false;
            try
            {
                await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await m_startupShutdownSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        foreach (PreparedNodeManager candidate in candidates)
                        {
                            ValidatePreparedNodeManager(candidate);
                            if (!candidate.Staged)
                            {
                                throw new InvalidOperationException("A batch candidate is not staged.");
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
                        foreach (IAsyncNodeManager manager in retiring)
                        {
                            if (!m_dynamicExternalReferences.ContainsKey(manager))
                            {
                                throw new InvalidOperationException("A batch retirement is no longer registered.");
                            }
                        }

                        var nextReferences = m_dynamicExternalReferences
                            .Where(entry => !retiring.Contains(entry.Key))
                            .ToDictionary(entry => entry.Key, entry => entry.Value);
                        foreach (PreparedNodeManager candidate in candidates)
                        {
                            nextReferences.Add(candidate.NodeManager, candidate.ExternalReferences);
                        }
                        Dictionary<NodeState, NodeState.ReferenceSnapshot> referenceImages =
                            routingRevision.References.ToDictionary(entry => entry.Key, entry => entry.Value);
                        using (m_nodeManagers.UseTypeImage(typeTree, factory))
                        {
                            ArrayOf<PreparedReferenceSource> sources = await PrepareReferenceSourcesAsync(
                                candidates, retiring, nextReferences, cancellationToken).ConfigureAwait(false);
                            foreach (PreparedReferenceSource source in sources)
                            {
                                referenceUpdates.Add(m_nodeManagers.PrepareReferences(
                                    source.Owner, source.Node, source.Additions, source.Removals, referenceImages));
                            }
                        }

                        using NodeManagerRoutingTable.PreparedRoutes routes =
                            m_nodeManagers.PrepareBatch(
                                candidates, removed, routingRevision, ResolveNamespaceIndexes,
                                typeTree, factory, referenceImages);
                        routes.Reserve();
                        using TypeTable.Publication types = Server.TypeTree.BeginPublication(originalTypes, typeRevision);
                        using EncodeableFactory.Publication registrations =
                            originalFactory.BeginPublication(originalFactory, factoryRevision);
                        foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                        {
                            update.Reserve();
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        await decideAsync(cancellationToken).ConfigureAwait(false);

                        // Lifecycle-waiting callbacks suspend their admission while the committed image is installed.
                        await m_bindingSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        bindingAdmissionHeld = true;
                        foreach (IAsyncNodeManager manager in retiring)
                        {
                            RetainRetiredGenerationNotifications(manager);
                        }
                        routes.Publish();
                        types.Complete();
                        registrations.Complete();
                        foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                        {
                            update.Complete();
                        }
                        foreach (IAsyncNodeManager manager in retiring)
                        {
                            m_dynamicExternalReferences.Remove(manager);
                        }
                        foreach (PreparedNodeManager candidate in candidates)
                        {
                            m_dynamicExternalReferences.Add(candidate.NodeManager, candidate.ExternalReferences);
                            candidate.Staged = false;
                            candidate.Published = true;
                            candidate.ReplacedNodeManager = null;
                            candidate.ReplacedExternalReferences = null;
                            SetPreparing(candidate.NodeManager, preparing: false);
                        }
                        // Callbacks may mutate the committed image after internal publication bookkeeping.
                        routes.Dispose();
                        published();
                    }
                    finally
                    {
                        m_startupShutdownSemaphoreSlim.Release();
                    }
                }
                finally
                {
                    m_dynamicMutationSemaphore.Release();
                }
                await reconcileBindingsAsync().ConfigureAwait(false);
                m_bindingSemaphore.Release();
                bindingAdmissionHeld = false;
                foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                {
                    update.Notify(reportCleanupFailure);
                }
            }
            finally
            {
                if (bindingAdmissionHeld)
                {
                    m_bindingSemaphore.Release();
                }
                foreach (NodeState.ReferenceUpdate update in referenceUpdates)
                {
                    update.Dispose();
                }
            }
        }

        private async ValueTask<ArrayOf<PreparedReferenceSource>> PrepareReferenceSourcesAsync(
            ArrayOf<PreparedNodeManager> candidates,
            HashSet<IAsyncNodeManager> retiring,
            Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> nextReferences,
            CancellationToken cancellationToken)
        {
            Dictionary<NodeId, ReferenceDictionary<bool>> previous = Collect(m_dynamicExternalReferences.Values);
            Dictionary<NodeId, ReferenceDictionary<bool>> next = Collect(nextReferences.Values);
            var sources = new List<PreparedReferenceSource>();
            var newOwners = new HashSet<IAsyncNodeManager>();
            foreach (PreparedNodeManager candidate in candidates)
            {
                newOwners.Add(candidate.NodeManager);
            }
            IAsyncNodeManager[] owners =
            [
                .. m_nodeManagers.Where(manager => !retiring.Contains(manager)),
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
                foreach (IAsyncNodeManager owner in owners)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object handle = await owner.GetManagerHandleAsync(sourceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (handle is null)
                    {
                        continue;
                    }
                    if ((owner is not AsyncCustomNodeManager && owner.SyncNodeManager is not CustomNodeManager2) ||
                        handle is not NodeHandle { Validated: true, Node: { } node })
                    {
                        throw new NotSupportedException(
                            "Prepared external references require an in-memory NodeState owner.");
                    }
                    if (!nodes.Add(node))
                    {
                        throw new NotSupportedException(
                            "A prepared reference source has multiple NodeManager owners.");
                    }
                    sources.Add(new PreparedReferenceSource(
                        owner, node, additions, newOwners.Contains(owner) ? [] : removals));
                }
            }
            return [.. sources];

            Dictionary<NodeId, ReferenceDictionary<bool>> Collect(
                IEnumerable<Dictionary<NodeId, IList<IReference>>> references)
            {
                var result = new Dictionary<NodeId, ReferenceDictionary<bool>>();
                if (m_startupExternalReferences is { } startup)
                {
                    Add(startup);
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

        private sealed record PreparedReferenceSource(
            IAsyncNodeManager Owner,
            NodeState Node,
            ArrayOf<IReference> Additions,
            ArrayOf<IReference> Removals);
    }
}
