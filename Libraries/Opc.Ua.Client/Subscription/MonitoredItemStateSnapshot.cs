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
    /// Immutable snapshot of a V2 <see cref="IMonitoredItem"/>'s
    /// configuration plus the server-side identifiers needed to take
    /// over the item on a transferred subscription.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced by <see cref="MonitoredItem.Snapshot"/> and consumed by
    /// <see cref="ISubscriptionManager.LoadAsync"/>.
    /// Per-item runtime values (filter result, last sample,
    /// current sampling interval) are intentionally not captured — the
    /// transfer path re-binds them from the server via
    /// <c>GetMonitoredItems</c>, and the recreate path mints fresh ones.
    /// </para>
    /// <para>
    /// The snapshot is itself an <see cref="IEncodeable"/> via the
    /// <see cref="DataTypeAttribute"/> source generator. The fields
    /// carried on the wire are simple primitives (e.g. <see cref="int"/>
    /// milliseconds for durations, <see cref="uint"/> for enums); the
    /// non-encoded <see cref="ToOptions"/> projection exposes a
    /// consumer-friendly <see cref="MonitoredItemOptions"/> built from
    /// those surrogate fields. The companion <see cref="AsOptions"/>
    /// factory does the inverse mapping at <see cref="MonitoredItem.Snapshot"/>
    /// time.
    /// </para>
    /// </remarks>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    public sealed partial record class MonitoredItemStateSnapshot
    {
        /// <summary>
        /// Stable, manager-unique name (the lookup key used by
        /// <see cref="IMonitoredItemCollection.TryGetMonitoredItemByName"/>
        /// and by <see cref="IMonitoredItemCollection.TryAdd"/>).
        /// </summary>
        [DataTypeField(Order = 1)]
        public partial string Name { get; init; }

        /// <summary>
        /// Client-assigned handle at snapshot time. Used by the
        /// transfer leg of restore to re-bind to the server-side
        /// monitored item via the saved <see cref="ServerId"/>;
        /// ignored by the recreate leg (the V2 state machine mints a
        /// fresh client handle).
        /// </summary>
        [DataTypeField(Order = 2)]
        public partial uint ClientHandle { get; init; }

        /// <summary>
        /// Server-assigned monitored item id, or <c>0</c> if the item
        /// had not been created on the server yet. Used by the
        /// transfer leg to match this item via the
        /// <c>GetMonitoredItems</c> server-handle table.
        /// </summary>
        [DataTypeField(Order = 3)]
        public partial uint ServerId { get; init; }

        /// <summary>
        /// Client handle of the monitored item that triggers this item,
        /// or <c>0</c> if not triggered.
        /// </summary>
        [DataTypeField(Order = 4)]
        public partial uint TriggeringItemClientHandle { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.Order"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 10)]
        public partial uint Order { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.StartNodeId"/> surrogate.
        /// Null sentinel: <see cref="NodeId.Null"/>.
        /// </summary>
        [DataTypeField(Order = 11)]
        public partial NodeId StartNodeId { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.TimestampsToReturn"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 12)]
        public partial uint TimestampsToReturn { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.AttributeId"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 13)]
        public partial uint AttributeId { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.IndexRange"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 14)]
        public partial string IndexRange { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.Encoding"/> surrogate. Null
        /// sentinel: <see cref="QualifiedName.Null"/>.
        /// </summary>
        [DataTypeField(Order = 15)]
        public partial QualifiedName Encoding { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.MonitoringMode"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 16)]
        public partial uint MonitoringMode { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.SamplingInterval"/> as whole
        /// milliseconds.
        /// </summary>
        [DataTypeField(Order = 17)]
        public partial int SamplingIntervalMs { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.Filter"/> surrogate.
        /// Polymorphic; encoded via <see cref="ExtensionObject"/> so the
        /// concrete <see cref="DataChangeFilter"/> /
        /// <see cref="EventFilter"/> / <see cref="AggregateFilter"/>
        /// type round-trips.
        /// </summary>
        [DataTypeField(Order = 18, StructureHandling = StructureHandling.ExtensionObject)]
        public partial MonitoringFilter? Filter { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.QueueSize"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 19)]
        public partial uint QueueSize { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.DiscardOldest"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 20)]
        public partial bool DiscardOldest { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.AutoSetQueueSize"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 21)]
        public partial bool AutoSetQueueSize { get; init; }

        /// <summary>
        /// <see cref="MonitoredItemOptions.Affinity"/> surrogate.
        /// Round-trips the strict-affinity tag so a restored
        /// subscription regroups items into the same logical
        /// partition the source had.
        /// </summary>
        [DataTypeField(Order = 22)]
        public partial string? Affinity { get; init; }

        /// <summary>
        /// Project the encoded surrogate fields back into a live
        /// <see cref="MonitoredItemOptions"/>. Not serialized.
        /// </summary>
        public MonitoredItemOptions ToOptions()
        {
            return new MonitoredItemOptions
            {
                Order = Order,
                StartNodeId = StartNodeId.IsNull ? NodeId.Null : StartNodeId,
                TimestampsToReturn = (TimestampsToReturn)TimestampsToReturn,
                AttributeId = AttributeId,
                IndexRange = IndexRange,
                Encoding = Encoding.IsNull ? null : Encoding,
                MonitoringMode = (MonitoringMode)MonitoringMode,
                SamplingInterval = TimeSpan.FromMilliseconds(SamplingIntervalMs),
                Filter = Filter,
                QueueSize = QueueSize,
                DiscardOldest = DiscardOldest,
                AutoSetQueueSize = AutoSetQueueSize,
                Affinity = Affinity
            };
        }

        /// <summary>
        /// Construct a <see cref="MonitoredItemStateSnapshot"/> from a
        /// live <see cref="MonitoredItemOptions"/> + the captured
        /// server-side state.
        /// </summary>
        public static MonitoredItemStateSnapshot AsOptions(
            string name,
            MonitoredItemOptions options,
            uint clientHandle,
            uint serverId,
            uint triggeringItemClientHandle)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return new MonitoredItemStateSnapshot
            {
                Name = name ?? string.Empty,
                ClientHandle = clientHandle,
                ServerId = serverId,
                TriggeringItemClientHandle = triggeringItemClientHandle,
                Order = options.Order,
                StartNodeId = options.StartNodeId.IsNull ? NodeId.Null : options.StartNodeId,
                TimestampsToReturn = (uint)options.TimestampsToReturn,
                AttributeId = options.AttributeId,
                IndexRange = options.IndexRange ?? string.Empty,
                Encoding = options.Encoding.HasValue && !options.Encoding.Value.IsNull
                    ? options.Encoding.Value
                    : QualifiedName.Null,
                MonitoringMode = (uint)options.MonitoringMode,
                SamplingIntervalMs = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(0, options.SamplingInterval.TotalMilliseconds)),
                Filter = options.Filter,
                QueueSize = options.QueueSize,
                DiscardOldest = options.DiscardOldest,
                AutoSetQueueSize = options.AutoSetQueueSize,
                Affinity = options.Affinity
            };
        }
    }
}
