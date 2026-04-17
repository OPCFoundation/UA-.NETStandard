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

namespace Opc.Ua.Client
{
    /// <summary>
    /// State object that is used for snapshotting the monitored item
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    public partial record class MonitoredItemState : MonitoredItemOptions
    {
        /// <summary>
        /// Create monitored item state
        /// </summary>
        public MonitoredItemState()
        {
        }

        /// <summary>
        /// Create state from options
        /// </summary>
        /// <param name="options"></param>
        public MonitoredItemState(MonitoredItemOptions options)
            : base(options)
        {
        }

        /// <summary>
        /// Server-side identifier assigned to this monitored item (the
        /// <c>monitoredItemId</c>). Stored so the client can correlate notifications
        /// and perform Modify/SetMonitoringMode/Delete operations across reconnects.
        /// 0 indicates not yet created or invalidated.
        /// </summary>
        [DataTypeField(Order = 13)]
        public partial uint ServerId { get; init; }

        /// <summary>
        /// Client-assigned handle used in Publish notifications (clientHandle)
        /// to quickly map incoming data changes or events to local application
        /// structures without lookups on serverId. Should be unique per subscription.
        /// Typically used as an index/key into client data structures.
        /// </summary>
        [DataTypeField(Order = 14)]
        public partial uint ClientId { get; init; }

        /// <summary>
        /// When the state was created.
        /// </summary>
        [DataTypeField(Order = 15)]
        public DateTimeUtc Timestamp { get; set; } = DateTimeUtc.Now;

        /// <summary>
        /// Server-side identifier of the triggering item if this monitored item
        /// is triggered by another item. 0 indicates this item is not triggered.
        /// Used to restore triggering links after session reconnect.
        /// </summary>
        [DataTypeField(Order = 16)]
        public partial uint TriggeringItemId { get; init; }

        /// <summary>
        /// Collection of server-side identifiers of monitored items that are
        /// triggered by this item. Empty or null if this item does not trigger
        /// any other items. Used to restore triggering links after session reconnect.
        /// </summary>
        [DataTypeField(Order = 17)]
        public partial ArrayOf<uint> TriggeredItems { get; init; }

        /// <summary>
        /// The queue size used by the client-side cache.
        /// </summary>
        [DataTypeField(Order = 18)]
        public partial uint CacheQueueSize { get; init; }
    }

}
