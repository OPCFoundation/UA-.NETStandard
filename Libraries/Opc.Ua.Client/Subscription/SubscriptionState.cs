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

namespace Opc.Ua.Client
{
    /// <summary>
    /// State object that is used for snapshotting the subscription state
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    public partial record class SubscriptionState : SubscriptionOptions
    {
        /// <summary>
        /// Create subscription state
        /// </summary>
        public SubscriptionState()
        {
        }

        /// <summary>
        /// Create subscription state with current options
        /// </summary>
        /// <param name="options"></param>
        public SubscriptionState(SubscriptionOptions options)
            : base(options)
        {
        }

        /// <summary>
        /// Allows the list of monitored items to be saved/restored
        /// when the object is serialized.
        /// </summary>
        [DataTypeField(Order = 11, StructureHandling = StructureHandling.Inline)]
        public ArrayOf<MonitoredItemState> MonitoredItems { get; set; }

        /// <summary>
        /// The current publishing interval.
        /// </summary>
        [DataTypeField(Order = 20)]
        public double CurrentPublishingInterval { get; set; }

        /// <summary>
        /// The current keep alive count.
        /// </summary>
        [DataTypeField(Order = 21)]
        public uint CurrentKeepAliveCount { get; set; }

        /// <summary>
        /// The current lifetime count.
        /// </summary>
        [DataTypeField(Order = 22)]
        public uint CurrentLifetimeCount { get; set; }

        /// <summary>
        /// When the state was created.
        /// </summary>
        [DataTypeField(Order = 23)]
        public DateTimeUtc Timestamp { get; set; } = DateTimeUtc.Now;
    }

    /// <summary>
    /// A collection of subscription states.
    /// </summary>
    public class SubscriptionStateCollection : List<SubscriptionState>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public SubscriptionStateCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The existing collection to use as
        /// the basis of creating this collection</param>
        public SubscriptionStateCollection(IEnumerable<SubscriptionState> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max. capacity of the collection</param>
        public SubscriptionStateCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            var clone = new SubscriptionStateCollection();
            clone.AddRange(this.Select(item => item with { }));
            return clone;
        }
    }
}
