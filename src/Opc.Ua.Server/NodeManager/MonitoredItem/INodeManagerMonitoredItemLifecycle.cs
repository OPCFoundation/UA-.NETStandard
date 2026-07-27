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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Moves existing MonitoredItems between NodeManager generations while the server is running.
    /// <para>
    /// When a NodeManager is reloaded, its MonitoredItems are detached from the outgoing
    /// generation and attached to the incoming one, so a Client keeps the same MonitoredItem and
    /// does not observe a transient bad status for Nodes that still exist. This is unrelated to
    /// restoring durable Subscriptions at server startup, which is handled by the synchronous
    /// <see cref="IMonitoredItemManager.RestoreMonitoredItem"/> path.
    /// </para>
    /// </summary>
    internal interface INodeManagerMonitoredItemLifecycle
    {
        /// <summary>
        /// Returns a stable snapshot of the MonitoredItems owned by this NodeManager, optionally
        /// limited to the given Nodes.
        /// </summary>
        /// <param name="nodeIds">The Nodes to report, or <c>null</c> for all of them.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        ValueTask<IReadOnlyList<IMonitoredItem>> GetMonitoredItemsSnapshotAsync(
            IReadOnlyCollection<NodeId>? nodeIds = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports whether an existing MonitoredItem could attach to this NodeManager, which is
        /// checked before anything is detached so that an incompatible reload is rejected while it
        /// can still be rolled back.
        /// </summary>
        /// <param name="monitoredItem">The MonitoredItem to test.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>
        /// A good result when the MonitoredItem can attach, otherwise the reason why it cannot.
        /// </returns>
        ValueTask<ServiceResult> CanAttachMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Detaches a MonitoredItem from this NodeManager without disposing it, so it keeps its
        /// identity and queue while it has no owner.
        /// </summary>
        /// <param name="monitoredItem">The MonitoredItem to detach.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        ValueTask<ServiceResult> DetachMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attaches a detached MonitoredItem to the matching Node in this NodeManager, which is
        /// how it is handed to the incoming generation once a reload is committed.
        /// </summary>
        /// <param name="monitoredItem">The MonitoredItem to attach.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        ValueTask<ServiceResult> AttachMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gives a detached MonitoredItem back to the NodeManager it came from after a lifecycle
        /// operation failed. Rolling back means the operation is undone and the outgoing
        /// generation stays in service, so the MonitoredItem has to keep working as if the reload
        /// had never been started.
        /// </summary>
        /// <param name="monitoredItem">The MonitoredItem to recover.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        ValueTask<ServiceResult> RecoverMonitoredItemAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);
    }
}
