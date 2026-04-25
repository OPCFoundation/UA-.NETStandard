#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    using System;

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
#endif
