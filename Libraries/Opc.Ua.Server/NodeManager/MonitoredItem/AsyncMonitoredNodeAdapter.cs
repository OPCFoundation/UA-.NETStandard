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
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Adapter that exposes the synchronous <see cref="MonitoredNode2"/> interface by delegating
    /// to a wrapped <see cref="AsyncMonitoredNode"/> instance.
    /// </summary>
    /// <remarks>
    /// The three notification methods (<see cref="OnReportEvent"/>,
    /// <see cref="OnMonitoredNodeChanged"/>, <see cref="QueueValue"/>) forward to the
    /// corresponding <c>async Task</c> methods on <see cref="AsyncMonitoredNode"/> via
    /// <c>.AsTask().GetAwaiter().GetResult()</c>, ensuring the async logic is properly
    /// awaited without fire-and-forget.
    /// </remarks>
    public class AsyncMonitoredNodeAdapter : MonitoredNode2
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncMonitoredNodeAdapter"/> class.
        /// </summary>
        /// <param name="asyncNodeManager">The async node manager that owns this node.</param>
        /// <param name="server">The server instance.</param>
        /// <param name="node">The node being monitored.</param>
        public AsyncMonitoredNodeAdapter(IAsyncNodeManager asyncNodeManager, IServerInternal server, NodeState node)
            : base((INodeManager3)asyncNodeManager.SyncNodeManager, server, node)
        {
            AsyncNode = new AsyncMonitoredNode(asyncNodeManager, server, node);
        }

        /// <summary>Gets the underlying async monitored node.</summary>
        public AsyncMonitoredNode AsyncNode { get; }

        /// <inheritdoc/>
        public override ConcurrentDictionary<uint, IDataChangeMonitoredItem2> DataChangeMonitoredItems
            => AsyncNode.DataChangeMonitoredItems;

        /// <inheritdoc/>
        public override ConcurrentDictionary<uint, IEventMonitoredItem> EventMonitoredItems
            => AsyncNode.EventMonitoredItems;

        /// <inheritdoc/>
        public override bool HasMonitoredItems => AsyncNode.HasMonitoredItems;

        /// <inheritdoc/>
        public override void Add(IDataChangeMonitoredItem2 datachangeItem)
        {
            bool wasEmpty = AsyncNode.DataChangeMonitoredItems.IsEmpty;
            AsyncNode.DataChangeMonitoredItems.TryAdd(datachangeItem.Id, datachangeItem);

            // Register the sync adapter's override as the node-state-changed callback so that
            // virtual dispatch routes changes through OnMonitoredNodeChanged → async awaited path.
            Node.OnStateChanged = OnMonitoredNodeChanged;

            if (wasEmpty && Server.ConfigurationNodeManager != null)
            {
                Server.ConfigurationNodeManager.DefaultPermissionsChanged += OnDefaultPermissionsChanged;
            }
        }

        /// <inheritdoc/>
        public override void Remove(IDataChangeMonitoredItem2 datachangeItem)
        {
            if (AsyncNode.DataChangeMonitoredItems.TryRemove(datachangeItem.Id, out _))
            {
                AsyncNode.RemoveFromCaches(datachangeItem.Id);
            }

            if (AsyncNode.DataChangeMonitoredItems.IsEmpty)
            {
                Node.OnStateChanged = null;
                Server.ConfigurationNodeManager?.DefaultPermissionsChanged -= OnDefaultPermissionsChanged;
            }
        }

        /// <inheritdoc/>
        public override void Add(IEventMonitoredItem eventItem)
        {
            AsyncNode.EventMonitoredItems.TryAdd(eventItem.Id, eventItem);
            Node.OnReportEvent = OnReportEvent;
        }

        /// <inheritdoc/>
        public override void Remove(IEventMonitoredItem eventItem)
        {
            AsyncNode.EventMonitoredItems.TryRemove(eventItem.Id, out _);

            if (AsyncNode.EventMonitoredItems.IsEmpty)
            {
                Node.OnReportEvent = null;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Bridges to <see cref="AsyncMonitoredNode.OnReportEventAsync"/> via
        /// <c>.GetAwaiter().GetResult()</c>.
        /// </remarks>
        public override void OnReportEvent(ISystemContext context, NodeState node, IFilterTarget e)
        {
            lock (m_eventLock)
            {
                AsyncNode.OnReportEventAsync(context, node, e).GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Bridges to <see cref="AsyncMonitoredNode.OnMonitoredNodeChangedAsync"/> via
        /// <c>.GetAwaiter().GetResult()</c>.
        /// </remarks>
        public override void OnMonitoredNodeChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            lock (m_dataChangeLock)
            {
                AsyncNode.OnMonitoredNodeChangedAsync(context, node, changes).GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Bridges to <see cref="AsyncMonitoredNode.QueueValueAsync"/> via
        /// <c>.GetAwaiter().GetResult()</c>.
        /// </remarks>
        public override void QueueValue(
            ISystemContext context,
            NodeState node,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            AsyncNode.QueueValueAsync(context, node, monitoredItem).GetAwaiter().GetResult();
        }

        private void OnDefaultPermissionsChanged(object? sender, EventArgs e)
        {
            AsyncNode.InvalidatePermissionCache();
        }

        private readonly Lock m_dataChangeLock = new();
        private readonly Lock m_eventLock = new();
    }
}
