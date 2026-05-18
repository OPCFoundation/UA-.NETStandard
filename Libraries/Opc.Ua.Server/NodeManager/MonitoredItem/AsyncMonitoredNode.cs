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

namespace Opc.Ua.Server
{
    /// <summary>
    /// A standalone async node monitoring class that uses <see cref="IAsyncNodeManager"/> for
    /// permission validation and <see cref="NodeState.ReadAttributeAsync"/> for value reads.
    /// </summary>
    /// <remarks>
    /// This class does NOT extend <see cref="MonitoredNode2"/>. It exposes proper
    /// <c>async Task</c> method signatures (<see cref="OnReportEventAsync"/>,
    /// <see cref="OnMonitoredNodeChangedAsync"/>, <see cref="QueueValueAsync"/>).
    /// The synchronous <see cref="MonitoredNode2"/> interface is provided by
    /// <see cref="AsyncMonitoredNodeAdapter"/>, which bridges sync calls to this class via
    /// <c>.AsTask().GetAwaiter().GetResult()</c>.
    /// </remarks>
    public class AsyncMonitoredNode
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncMonitoredNode"/> class.
        /// </summary>
        /// <param name="asyncNodeManager">The async node manager that owns this node.</param>
        /// <param name="server">The server instance.</param>
        /// <param name="node">The node being monitored.</param>
        public AsyncMonitoredNode(IAsyncNodeManager asyncNodeManager, IServerInternal server, NodeState node)
        {
            AsyncNodeManager = asyncNodeManager;
            NodeManager = (INodeManager3)asyncNodeManager.SyncNodeManager;
            m_server = server;
            Node = node;
        }

        /// <summary>Gets the sync node manager view of the async node manager.</summary>
        public INodeManager3 NodeManager { get; set; }

        /// <summary>Gets the async node manager that owns this node.</summary>
        public IAsyncNodeManager AsyncNodeManager { get; }

        /// <summary>Gets or sets the node being monitored.</summary>
        public NodeState Node { get; set; }

        /// <summary>Gets the data-change monitored items for this node.</summary>
        public ConcurrentDictionary<uint, IDataChangeMonitoredItem2> DataChangeMonitoredItems { get; } = new();

        /// <summary>Gets the event monitored items for this node.</summary>
        public ConcurrentDictionary<uint, IEventMonitoredItem> EventMonitoredItems { get; } = new();

        /// <summary>Gets a value indicating whether any monitored items are registered.</summary>
        public bool HasMonitoredItems =>
            !DataChangeMonitoredItems.IsEmpty || !EventMonitoredItems.IsEmpty;

        /// <summary>
        /// Invalidates the permission cache so all entries are re-validated on the next change.
        /// Called by <see cref="AsyncMonitoredNodeAdapter"/> when namespace default permissions change.
        /// </summary>
        public void InvalidatePermissionCache() => m_permissionCache.Clear();

        /// <summary>
        /// Removes the caches for a specific monitored item (called on removal).
        /// </summary>
        public void RemoveFromCaches(uint monitoredItemId)
        {
            m_contextCache.TryRemove(monitoredItemId, out _);
            m_permissionCache.TryRemove(monitoredItemId, out _);
        }

        /// <summary>
        /// Validates event role permissions and queues the event on each subscribed event monitored item.
        /// </summary>
        public async Task OnReportEventAsync(ISystemContext context, NodeState node, IFilterTarget e)
        {
            foreach (KeyValuePair<uint, IEventMonitoredItem> kvp in EventMonitoredItems)
            {
                IEventMonitoredItem monitoredItem = kvp.Value;

                if (e is AuditEventState)
                {
                    if (!m_server.Auditing)
                    {
                        continue;
                    }

                    if (monitoredItem?.Session?.EndpointDescription?.SecurityMode !=
                            MessageSecurityMode.SignAndEncrypt &&
                        monitoredItem?.Session?.EndpointDescription?.TransportProfileUri !=
                            Profiles.HttpsBinaryTransport)
                    {
                        continue;
                    }
                }

                ServiceResult validationResult = await AsyncNodeManager
                    .ValidateEventRolePermissionsAsync(monitoredItem, e)
                    .ConfigureAwait(false);

                if (ServiceResult.IsBad(validationResult))
                {
                    continue;
                }

                if (context is ISessionSystemContext sessionContext &&
                    sessionContext.SessionId is { IsNull: false } contextSessionId &&
                    monitoredItem?.Session?.Id is { IsNull: false } monitoredItemSessionId &&
                    !monitoredItemSessionId.Equals(contextSessionId))
                {
                    continue;
                }

                monitoredItem?.QueueEvent(e);
            }
        }

        /// <summary>
        /// Validates role permissions and queues data-change values for each subscribed monitored item.
        /// </summary>
        public async Task OnMonitoredNodeChangedAsync(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.RolePermissions) != 0)
            {
                m_permissionCache.Clear();
            }

            foreach (KeyValuePair<uint, IDataChangeMonitoredItem2> kvp in DataChangeMonitoredItems)
            {
                IDataChangeMonitoredItem2 monitoredItem = kvp.Value;
                ISystemContext contextToUse;
                OperationContext operationContext;

                if (context is ServerSystemContext serverContext)
                {
                    ServerSystemContext serverSystemContextToUse = GetOrCreateContext(serverContext, monitoredItem);
                    operationContext = serverSystemContextToUse.OperationContext!;
                    contextToUse = serverSystemContextToUse;
                }
                else
                {
                    operationContext = new OperationContext(monitoredItem);
                    contextToUse = context;
                }

                if (monitoredItem.AttributeId == Attributes.Value &&
                    (changes & NodeStateChangeMasks.Value) != 0)
                {
                    if (!m_permissionCache.TryGetValue(monitoredItem.Id, out ServiceResult? validationResult))
                    {
                        validationResult = await AsyncNodeManager
                            .ValidateRolePermissionsAsync(operationContext, node.NodeId, PermissionType.Read)
                            .ConfigureAwait(false);

                        m_permissionCache[monitoredItem.Id] = validationResult;
                    }

                    if (ServiceResult.IsBad(validationResult))
                    {
                        continue;
                    }

                    await QueueValueAsync(contextToUse, node, monitoredItem).ConfigureAwait(false);
                    continue;
                }

                if (monitoredItem.AttributeId != Attributes.Value &&
                    (changes & NodeStateChangeMasks.NonValue) != 0)
                {
                    await QueueValueAsync(contextToUse, node, monitoredItem).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Reads the value of an attribute asynchronously and queues it on the monitored item.
        /// </summary>
        public async Task QueueValueAsync(
            ISystemContext context,
            NodeState node,
            IDataChangeMonitoredItem2 monitoredItem,
            CancellationToken cancellationToken = default)
        {
            var value = new DataValue
            {
                WrappedValue = default,
                ServerTimestamp = DateTime.UtcNow,
                SourceTimestamp = DateTime.MinValue,
                StatusCode = StatusCodes.Good
            };

            ServiceResult error = await node.ReadAttributeAsync(
                context,
                monitoredItem.AttributeId,
                monitoredItem.IndexRange,
                monitoredItem.DataEncoding,
                value,
                cancellationToken).ConfigureAwait(false);

            if (ServiceResult.IsBad(error))
            {
                value = null;
            }

            monitoredItem.QueueValue(value!, error);
        }

        private ServerSystemContext GetOrCreateContext(
            ServerSystemContext context,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            uint monitoredItemId = monitoredItem.Id;
            int currentTicks = HiResClock.TickCount;
            OperationContext operationContext;

            if (m_contextCache.TryGetValue(
                    monitoredItemId,
                    out (ServerSystemContext Context, int CreatedAtTicks) cachedEntry))
            {
                if (cachedEntry.Context.OperationContext!.Session != monitoredItem.Session ||
                    cachedEntry.Context.OperationContext.UserIdentity != monitoredItem.EffectiveIdentity ||
                    (currentTicks - cachedEntry.CreatedAtTicks) > m_cacheLifetimeTicks)
                {
                    operationContext = new OperationContext(monitoredItem);
                    ServerSystemContext updatedContext = context.Copy(operationContext);
                    m_contextCache[monitoredItemId] = (updatedContext, currentTicks);
                    m_permissionCache.TryRemove(monitoredItemId, out _);
                    return updatedContext;
                }

                return cachedEntry.Context;
            }

            operationContext = new OperationContext(monitoredItem);
            ServerSystemContext newContext = context.Copy(operationContext);
            m_contextCache.TryAdd(monitoredItemId, (newContext, currentTicks));
            return newContext;
        }

        private readonly IServerInternal m_server;

        private readonly ConcurrentDictionary<uint, ServiceResult> m_permissionCache = new();

        private readonly ConcurrentDictionary<uint, (ServerSystemContext Context, int CreatedAtTicks)>
            m_contextCache = new();

        private readonly int m_cacheLifetimeTicks = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
    }
}
