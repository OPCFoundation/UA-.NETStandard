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

        /// <summary>
        /// <para>
        /// Optional affinity tag. When the V2 subscription engine
        /// runs in unbounded-item mode (the default — see
        /// <see cref="SubscriptionOptions.DisableUnboundedItemMode"/>),
        /// monitored items that share the same non-null
        /// <see cref="Affinity"/> value are guaranteed to land in
        /// the same underlying server-side partition subscription.
        /// </para>
        /// <para>
        /// Co-location is required for cross-item OPC UA features
        /// that are scoped to a single subscription on the server,
        /// most notably <c>SetTriggering</c> (OPC UA Part 4 §5.13.5):
        /// the imperative <see cref="ISubscription.SetTriggeringAsync"/>
        /// rejects calls whose triggering and triggered items live in
        /// different partitions, and the declarative
        /// <see cref="TriggeredByNames"/> list only resolves names
        /// inside the same partition. Tagging every member of a
        /// triggering relationship with the same
        /// <see cref="Affinity"/> value guarantees they cannot be
        /// split.
        /// </para>
        /// <para>
        /// The affinity contract is <em>strict</em>: once an affinity
        /// group reaches the per-partition capacity, further
        /// <see cref="IMonitoredItemCollection.TryAdd"/> calls for
        /// the same tag return <c>false</c> rather than silently
        /// splitting the group across partitions. Callers that want
        /// to allow splitting must drop the tag or move the items to
        /// a different logical subscription.
        /// </para>
        /// <para>
        /// A <c>null</c> value (the default) places no co-location
        /// constraint on the item.
        /// </para>
        /// </summary>
        public string? Affinity { get; init; }

        /// <summary>
        /// Initial declarative set of monitored-item names that
        /// trigger this item (OPC UA Part 4 §5.13.5 SetTriggering).
        /// Each entry must be a stable monitored-item name registered
        /// with the owning subscription's
        /// <see cref="IMonitoredItemCollection.TryGetMonitoredItemByName"/>.
        /// <para>
        /// The OPC UA spec supports an N:M triggering topology — a
        /// triggered item may be linked to more than one triggering
        /// item. Use this list to declare every triggering item that
        /// should cause this item to report; the subscription engine
        /// batches one <c>SetTriggering</c> RPC per distinct triggering
        /// item when applying changes.
        /// </para>
        /// <para>
        /// This field is the **initial declarative input**. The
        /// canonical runtime source of truth is the subscription
        /// engine's per-item desired-state runtime field, initialized
        /// from this list at construction and mutated by both
        /// imperative <c>SetTriggeringAsync</c> calls and subsequent
        /// options-change events.
        /// </para>
        /// <para>
        /// Validation (applied at options-change time): null, empty,
        /// and whitespace-only entries are rejected with
        /// <see cref="ArgumentException"/>. Duplicate entries are
        /// silently de-duplicated using an ordinal case-sensitive
        /// comparer (matching the subscription's name dictionary).
        /// Insertion order is preserved for deterministic snapshot
        /// output.
        /// </para>
        /// <para>
        /// When the V2 unbounded-item mode is active and the
        /// triggering item lands in a different partition from the
        /// triggered item, the link resolves to
        /// <c>Bad_MonitoredItemIdInvalid</c> because OPC UA
        /// <c>SetTriggering</c> is per-subscription. Use a shared
        /// <see cref="Affinity"/> value to keep the items co-located.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> TriggeredByNames { get; init; } = [];
    }
}
