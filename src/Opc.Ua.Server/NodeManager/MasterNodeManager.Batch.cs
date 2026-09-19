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

        async ValueTask IDynamicNodeManagerBatchHost.CommitBatchAsync(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            NodeManagerRoutingTable.RoutingSnapshot routingRevision,
            Func<CancellationToken, ValueTask> decideAsync,
            Action published,
            CancellationToken cancellationToken)
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
                    for (int index = 0; index < candidates.Count; index++)
                    {
                        PreparedNodeManager candidate = candidates[index];
                        foreach (Dictionary<NodeId, IList<IReference>> references in nextReferences.Values)
                        {
                            await candidate.NodeManager.AddReferencesAsync(references, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }

                    NodeManagerRoutingTable.PreparedRoutes routes =
                        m_nodeManagers.PrepareBatch(candidates, removed, routingRevision, ResolveNamespaceIndexes);
                    routes.Validate();
                    cancellationToken.ThrowIfCancellationRequested();
                    await decideAsync(cancellationToken).ConfigureAwait(false);

                    foreach (IAsyncNodeManager manager in retiring)
                    {
                        RetainRetiredGenerationNotifications(manager);
                    }
                    routes.Publish();
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
        }
    }
}
