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

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Immutable snapshot of a V2 <see cref="IMonitoredItem"/>'s
    /// configuration plus the server-side identifiers needed to take
    /// over the item on a transferred subscription.
    /// </summary>
    /// <remarks>
    /// Produced by <see cref="MonitoredItem.Snapshot"/> and consumed by
    /// <see cref="Opc.Ua.Client.Subscriptions.ISubscriptionManager.RestoreAsync"/>.
    /// Per-item runtime values (filter result, last sample,
    /// current sampling interval) are intentionally not captured — the
    /// transfer path re-binds them from the server via
    /// <c>GetMonitoredItems</c>, and the recreate path mints fresh ones.
    /// </remarks>
    public sealed record MonitoredItemStateSnapshot
    {
        /// <summary>
        /// Stable, manager-unique name (the lookup key used by
        /// <see cref="IMonitoredItemCollection.TryGetMonitoredItemByName"/>
        /// and by <see cref="IMonitoredItemCollection.TryAdd"/>).
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The live <see cref="MonitoredItemOptions"/> at snapshot time.
        /// </summary>
        public required MonitoredItemOptions Options { get; init; }

        /// <summary>
        /// Client-assigned handle at snapshot time. Used by the
        /// transfer leg of restore to re-bind to the server-side
        /// monitored item via the saved
        /// <see cref="ServerId"/>; ignored by the recreate leg
        /// (the V2 state machine mints a fresh client handle).
        /// </summary>
        public uint ClientHandle { get; init; }

        /// <summary>
        /// Server-assigned monitored item id, or <c>0</c> if the item
        /// had not been created on the server yet. Used by the
        /// transfer leg to match this item via the
        /// <c>GetMonitoredItems</c> server-handle table.
        /// </summary>
        public uint ServerId { get; init; }

        /// <summary>
        /// Client handle of the monitored item that triggers this item,
        /// or <c>0</c> if not triggered. Captured for replay via
        /// <see cref="ISubscription.SetTriggeringAsync"/> after restore.
        /// </summary>
        public uint TriggeringItemClientHandle { get; init; }

        /// <summary>
        /// Client handles of items triggered by this item. Captured for
        /// replay via <see cref="ISubscription.SetTriggeringAsync"/>
        /// after restore.
        /// </summary>
        public ArrayOf<uint> TriggeredItemClientHandles { get; init; }
    }
}
