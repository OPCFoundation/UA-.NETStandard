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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class AsyncCustomNodeManager
    {
        /// <inheritdoc/>
        public ValueTask WaitForValuePublicationAsync(
            ArrayOf<NodeState> nodes,
            CancellationToken cancellationToken = default)
        {
            return WaitForValuePublicationAsync(nodes, TimeSpan.FromSeconds(30), cancellationToken);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentOutOfRangeException">The deadline is not positive and finite.</exception>
        /// <exception cref="NotSupportedException">
        /// A relevant monitored item cannot observe final publication.
        /// </exception>
        /// <exception cref="ServiceResultException">A root is not owned or a final sample cannot be read.</exception>
        /// <exception cref="TimeoutException">
        /// The final samples did not enter Publish responses before the deadline.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Observation was cancelled or the manager is stopping.
        /// </exception>
        public async ValueTask WaitForValuePublicationAsync(
            ArrayOf<NodeState> nodes,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            MonitoredValuePublication.ValidateTimeout(timeout);
            cancellationToken.ThrowIfCancellationRequested();
            using NodeManagerOperation operation = BeginNodeManagerOperation();
            TimeProvider clock = (Server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System;
            using CancellationTokenSource deadline = clock.CreateCancellationTokenSource(timeout);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, deadline.Token, m_valuePublicationShutdown.Token);
            CancellationToken ct = lifetime.Token;
            try
            {
                var items = new List<(IDataChangeMonitoredItem2 Item, ISession Session)>();
                var monitoredNodes = new HashSet<MonitoredNode2>();
                using (await AcquireSemaphoreAsync(m_monitoredItemSemaphore, ct).ConfigureAwait(false))
                {
                    var nodeIds = new HashSet<NodeId>(NodeIdComparer.Default);
                    var subtree = new List<NodeState>(nodes.Count);
                    foreach (NodeState node in nodes)
                    {
                        subtree.Add(node);
                    }
                    for (int index = 0; index < subtree.Count; index++)
                    {
                        ct.ThrowIfCancellationRequested();
                        NodeState node = subtree[index];
                        if (node == null ||
                            !PredefinedNodes.TryGetValue(node.NodeId, out NodeState? registered) ||
                            !ReferenceEquals(node, registered))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdUnknown,
                                "A publication root or descendant is not registered here.");
                        }
                        if (!nodeIds.Add(node.NodeId))
                        {
                            continue;
                        }
                        var children = new List<BaseInstanceState>();
                        node.GetChildren(SystemContext, children);
                        subtree.AddRange(children);
                    }

                    if (m_monitoredItemManager is not IMonitoredItemManagerLifecycle lifecycle)
                    {
                        if (!MonitoredItems.IsEmpty)
                        {
                            throw new NotSupportedException(
                                "The monitored-item manager cannot capture publication membership.");
                        }
                    }
                    else
                    {
                        foreach (IMonitoredItem item in lifecycle.GetMonitoredItemsSnapshot(nodeIds))
                        {
                            if ((item.MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0 ||
                                item.MonitoringMode != MonitoringMode.Reporting ||
                                item.Session is not ISession session ||
                                session.IsClosing)
                            {
                                continue;
                            }
                            if (item is not IDataChangeMonitoredItem2 dataItem)
                            {
                                throw new NotSupportedException("A data-change item does not expose its attribute.");
                            }
                            if (dataItem.AttributeId != Attributes.Value)
                            {
                                continue;
                            }
                            if (item is not IMonitoredItemValuePublication)
                            {
                                throw new NotSupportedException(
                                    "A reporting item does not support final-value publication.");
                            }
                            items.Add((dataItem, session));
                            if (item.ManagerHandle is NodeHandle { MonitoredNode: not null } handle)
                            {
                                monitoredNodes.Add(handle.MonitoredNode);
                            }
                        }
                    }
                }

                // No node-manager gate is held while reads, consumers, or Publish requests run.
                foreach (MonitoredNode2 monitoredNode in monitoredNodes)
                {
                    await monitoredNode.WaitForPendingNotificationsAsync(ct).ConfigureAwait(false);
                }
                await ObserveFinalValuesAsync(items, timeout, lifetime).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (
                deadline.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested &&
                !m_valuePublicationShutdown.IsCancellationRequested)
            {
                throw new TimeoutException("Final values did not enter Publish responses before the deadline.");
            }
        }

        private async Task ObserveFinalValuesAsync(
            List<(IDataChangeMonitoredItem2 Item, ISession Session)> items,
            TimeSpan timeout,
            CancellationTokenSource lifetime)
        {
            var observations = new List<Task>(items.Count);
            foreach ((IDataChangeMonitoredItem2 item, ISession session) in items)
            {
                observations.Add(ObserveFinalValueAsync(item, session, timeout, lifetime));
            }
            await Task.WhenAll(observations).ConfigureAwait(false);
        }

        private async Task ObserveFinalValueAsync(
            IDataChangeMonitoredItem2 item,
            ISession session,
            TimeSpan timeout,
            CancellationTokenSource lifetime)
        {
            CancellationToken ct = lifetime.Token;
            try
            {
                using var context = new OperationContext(session, DiagnosticsMasks.None);
                ServiceResult permission = await ValidateRolePermissionsAsync(
                    context, item.NodeId, PermissionType.Read, ct).ConfigureAwait(false);
                if (ServiceResult.IsBad(permission))
                {
                    throw new ServiceResultException(permission);
                }

                var read = new ReadValueId
                {
                    NodeId = item.NodeId,
                    AttributeId = Attributes.Value,
                    IndexRange = item.IndexRange.ToString(),
                    ParsedIndexRange = item.IndexRange,
                    DataEncoding = item.DataEncoding
                };
                var values = new List<DataValue> { default };
                var errors = new List<ServiceResult> { ServiceResult.Good };
                await ReadAsync(context, 0, [read], values, errors, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (ServiceResult.IsBad(errors[0]))
                {
                    throw new ServiceResultException(errors[0]);
                }
                await ((IMonitoredItemValuePublication)item).WaitForValuePublicationAsync(
                    session, values[0], timeout, ct).ConfigureAwait(false);
            }
            catch
            {
                lifetime.Cancel();
                throw;
            }
        }

        /// <summary>Ends publication waits before disposal waits for admitted operations to drain.</summary>
        private readonly CancellationTokenSource m_valuePublicationShutdown = new();
    }
}
