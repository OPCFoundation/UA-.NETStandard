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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An async-aware version of <see cref="MonitoredNode2"/> that uses
    /// <see cref="IAsyncNodeManager"/> for permission validation and
    /// <see cref="NodeState.ReadAttributeAsync"/> for value reads.
    /// </summary>
    /// <remarks>
    /// <see cref="OnMonitoredNodeChanged"/>, <see cref="OnReportEvent"/>, and
    /// <see cref="QueueValue"/> are overridden to fire-and-forget background
    /// <see cref="Task"/>s that properly await
    /// <see cref="IAsyncNodeManager.ValidateRolePermissionsAsync"/>,
    /// <see cref="IAsyncNodeManager.ValidateEventRolePermissionsAsync"/> and
    /// <see cref="NodeState.ReadAttributeAsync"/>.
    /// </remarks>
    public class AsyncMonitoredNode : MonitoredNode2, IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncMonitoredNode"/> class.
        /// </summary>
        /// <param name="asyncNodeManager">The async node manager that owns this node.</param>
        /// <param name="server">The server instance.</param>
        /// <param name="node">The node being monitored.</param>
        public AsyncMonitoredNode(IAsyncNodeManager asyncNodeManager, IServerInternal server, NodeState node)
            : base((INodeManager3)asyncNodeManager.SyncNodeManager, server, node)
        {
            AsyncNodeManager = asyncNodeManager;
        }

        /// <summary>
        /// Gets the async node manager that owns this node.
        /// </summary>
        public IAsyncNodeManager AsyncNodeManager { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// Overridden to fire-and-forget a background task that properly awaits
        /// <see cref="IAsyncNodeManager.ValidateEventRolePermissionsAsync"/> for
        /// each subscribed event monitored item.
        /// </remarks>
        public override void OnReportEvent(ISystemContext context, NodeState node, IFilterTarget e)
        {
            var items = new List<IEventMonitoredItem>(EventMonitoredItems.Values);

            _ = Task.Run(async () =>
            {
                await m_eventSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    foreach (IEventMonitoredItem monitoredItem in items)
                    {
                        if (e is AuditEventState)
                        {
                            if (!Server.Auditing)
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

                        if (IsEventForOtherSession(context, monitoredItem))
                        {
                            continue;
                        }

                        monitoredItem?.QueueEvent(e);
                    }
                }
                finally
                {
                    m_eventSemaphore.Release();
                }
            });
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Overridden to fire-and-forget a background task that properly awaits
        /// <see cref="IAsyncNodeManager.ValidateRolePermissionsAsync"/> and
        /// <see cref="NodeState.ReadAttributeAsync"/> for each subscribed data-change
        /// monitored item.
        /// </remarks>
        public override void OnMonitoredNodeChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if (DataChangeMonitoredItems == null)
            {
                return;
            }

            if ((changes & NodeStateChangeMasks.RolePermissions) != 0)
            {
                m_permissionCache.Clear();
                m_asyncPermissionCache.Clear();
            }

            var items = new List<(IDataChangeMonitoredItem2 Item, ServerSystemContext? Ctx, OperationContext OpCtx, bool IsValue)>();

            foreach (System.Collections.Generic.KeyValuePair<uint, IDataChangeMonitoredItem2> kvp
                     in DataChangeMonitoredItems)
            {
                IDataChangeMonitoredItem2 monitoredItem = kvp.Value;

                ServerSystemContext? contextToUse = context is ServerSystemContext serverContext
                    ? GetOrCreateContext(serverContext, monitoredItem)
                    : null;

                OperationContext opCtx = contextToUse?.OperationContext
                    ?? new OperationContext(monitoredItem);

                bool isValueChange = monitoredItem.AttributeId == Attributes.Value &&
                                     (changes & NodeStateChangeMasks.Value) != 0;

                bool isNonValueChange = monitoredItem.AttributeId != Attributes.Value &&
                                        (changes & NodeStateChangeMasks.NonValue) != 0;

                if (!isValueChange && !isNonValueChange)
                {
                    continue;
                }

                items.Add((monitoredItem, contextToUse, opCtx, isValueChange));
            }

            _ = Task.Run(async () =>
            {
                await m_dataChangeSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    foreach ((IDataChangeMonitoredItem2 monitoredItem,
                              ServerSystemContext? ctxToUse,
                              OperationContext opCtx,
                              bool isValue) in items)
                    {
                        if (isValue)
                        {
                            if (!m_asyncPermissionCache.TryGetValue(monitoredItem.Id,
                                    out ServiceResult? validationResult))
                            {
                                validationResult = await AsyncNodeManager
                                    .ValidateRolePermissionsAsync(
                                        opCtx,
                                        node.NodeId,
                                        PermissionType.Read)
                                    .ConfigureAwait(false);

                                m_asyncPermissionCache[monitoredItem.Id] = validationResult;
                            }

                            if (ServiceResult.IsBad(validationResult))
                            {
                                continue;
                            }
                        }

                        await QueueValueAsync(ctxToUse ?? context, node, monitoredItem)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_dataChangeSemaphore.Release();
                }
            });
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Overridden to fire-and-forget a background task that properly awaits
        /// <see cref="QueueValueAsync"/>.
        /// </remarks>
        public override void QueueValue(
            ISystemContext context,
            NodeState node,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            _ = QueueValueAsync(context, node, monitoredItem).AsTask();
        }

        /// <summary>
        /// Reads the value of an attribute asynchronously via
        /// <see cref="NodeState.ReadAttributeAsync"/> and queues it on the monitored item.
        /// </summary>
        public async ValueTask QueueValueAsync(
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

        private bool m_disposed;

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overridable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    m_dataChangeSemaphore.Dispose();
                    m_eventSemaphore.Dispose();
                }

                m_disposed = true;
            }
        }

        private static bool IsEventForOtherSession(ISystemContext context, IEventMonitoredItem monitoredItem)
        {
            return context is ISessionSystemContext sessionContext &&
                   sessionContext.SessionId is { IsNull: false } contextSessionId &&
                   monitoredItem?.Session?.Id is { IsNull: false } monitoredItemSessionId &&
                   !monitoredItemSessionId.Equals(contextSessionId);
        }

        // Async permission cache for ValidateRolePermissionsAsync results, keyed by monitored
        // item id. Separate from the base-class sync cache (m_permissionCache) so both can
        // co-exist without interference.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, ServiceResult>
            m_asyncPermissionCache = new();

        private readonly SemaphoreSlim m_dataChangeSemaphore = new(1, 1);
        private readonly SemaphoreSlim m_eventSemaphore = new(1, 1);
    }
}
