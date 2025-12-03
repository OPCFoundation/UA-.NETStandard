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
using System.Linq;
using System.Runtime.Serialization;

namespace Opc.Ua.Client
{
    /// <summary>
    /// State object that is used for snapshotting the monitored item
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public record class MonitoredItemState : MonitoredItemOptions
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
        [DataMember(Order = 13)]
        public uint ServerId { get; init; }

        /// <summary>
        /// Client-assigned handle used in Publish notifications (clientHandle)
        /// to quickly map incoming data changes or events to local application
        /// structures without lookups on serverId. Should be unique per subscription.
        /// Typically used as an index/key into client data structures.
        /// </summary>
        [DataMember(Order = 14)]
        public uint ClientId { get; init; }

        /// <summary>
        /// When the state was created.
        /// </summary>
        [DataMember(Order = 15)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// A collection of monitored item states.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfMonitoredItems",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "MonitoredItems")]
    public class MonitoredItemStateCollection : List<MonitoredItemState>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public MonitoredItemStateCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The existing collection to use as
        /// the basis of creating this collection</param>
        public MonitoredItemStateCollection(IEnumerable<MonitoredItemState> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max. capacity of the collection</param>
        public MonitoredItemStateCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            var clone = new MonitoredItemStateCollection();
            clone.AddRange(this.Select(item => item with { }));
            return clone;
        }
    }
}
