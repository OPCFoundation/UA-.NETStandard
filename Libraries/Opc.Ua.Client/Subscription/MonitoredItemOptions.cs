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

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Monitored item options base
    /// </summary>
    public record class MonitoredItemOptions
    {
        /// <summary>
        /// The order of the item in the subscription
        /// </summary>
        public uint Order { get; init; }

        /// <summary>
        /// The start node for the browse path that
        /// identifies the node to monitor.
        /// </summary>
        public NodeId StartNodeId { get; init; }
            = NodeId.Null;

        /// <summary>
        /// Timestamps to return
        /// </summary>
        public TimestampsToReturn TimestampsToReturn { get; init; }
            = TimestampsToReturn.Both;

        /// <summary>
        /// The attribute to monitor.
        /// </summary>
        public uint AttributeId { get; init; }
            = Attributes.Value;

        /// <summary>
        /// The range of array indexes to monitor.
        /// </summary>
        public string? IndexRange { get; init; }

        /// <summary>
        /// The encoding to use when returning notifications.
        /// </summary>
        public QualifiedName? Encoding { get; init; }

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        public MonitoringMode MonitoringMode { get; init; }
            = MonitoringMode.Reporting;

        /// <summary>
        /// The sampling interval.
        /// </summary>
        public TimeSpan SamplingInterval { get; init; }

        /// <summary>
        /// The filter to use to select values to return.
        /// </summary>
        public MonitoringFilter? Filter { get; init; }

        /// <summary>
        /// The length of the queue used to buffer values.
        /// </summary>
        public uint QueueSize { get; init; }

        /// <summary>
        /// Whether to discard the oldest entries in the
        /// queue when it is full.
        /// </summary>
        public bool DiscardOldest { get; init; } = true;

        /// <summary>
        /// Auto calculate a queue size and apply
        /// </summary>
        public bool AutoSetQueueSize { get; init; }
    }
}
