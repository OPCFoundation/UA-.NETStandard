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

using System;
using System.ComponentModel;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Frozen wire shape of <see cref="SubscriptionStateSnapshot"/> as
    /// written by the first, unversioned stream format of
    /// <see cref="SubscriptionManagerSerializer"/>. The binary encoding is
    /// positional, so a stream from that format must be decoded with
    /// exactly the fields it carried; <see cref="ToCurrent"/> then fills
    /// the fields added since with the option defaults of the time.
    /// Never written; do not add fields. Public only because the generated
    /// activator requires it; not part of the supported API.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed partial record class SubscriptionStateSnapshotV0
    {
        /// <inheritdoc cref="SubscriptionStateSnapshot.ServerId"/>
        [DataTypeField(Order = 1)]
        public partial uint ServerId { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.AvailableSequenceNumbers"/>
        [DataTypeField(Order = 2)]
        public partial ArrayOf<uint> AvailableSequenceNumbers { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.Disabled"/>
        [DataTypeField(Order = 10)]
        public partial bool Disabled { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.KeepAliveCount"/>
        [DataTypeField(Order = 11)]
        public partial uint KeepAliveCount { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.LifetimeCount"/>
        [DataTypeField(Order = 12)]
        public partial uint LifetimeCount { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.Priority"/>
        [DataTypeField(Order = 13)]
        public partial byte Priority { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.PublishingIntervalMs"/>
        [DataTypeField(Order = 14)]
        public partial int PublishingIntervalMs { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.PublishingEnabled"/>
        [DataTypeField(Order = 15)]
        public partial bool PublishingEnabled { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.MaxNotificationsPerPublish"/>
        [DataTypeField(Order = 16)]
        public partial uint MaxNotificationsPerPublish { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.MinLifetimeIntervalMs"/>
        [DataTypeField(Order = 17)]
        public partial int MinLifetimeIntervalMs { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.SendInitialValuesOnTransfer"/>
        [DataTypeField(Order = 18)]
        public partial bool SendInitialValuesOnTransfer { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.MonitoredItems"/>
        [DataTypeField(Order = 20, StructureHandling = StructureHandling.Inline)]
        public partial ArrayOf<MonitoredItemStateSnapshot> MonitoredItems { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.LogicalGroupId"/>
        [DataTypeField(Order = 30)]
        public partial string? LogicalGroupId { get; init; }

        /// <inheritdoc cref="SubscriptionStateSnapshot.PartitionIndex"/>
        [DataTypeField(Order = 31)]
        public partial int PartitionIndex { get; init; }

        /// <summary>
        /// Project onto the current snapshot. The partition options did
        /// not exist when this shape was written, so they take the
        /// <see cref="SubscriptionOptions"/> defaults, exactly as a load of
        /// that time did.
        /// </summary>
        public SubscriptionStateSnapshot ToCurrent()
        {
            var defaults = new SubscriptionOptions();
            return new SubscriptionStateSnapshot
            {
                ServerId = ServerId,
                AvailableSequenceNumbers = AvailableSequenceNumbers,
                Disabled = Disabled,
                KeepAliveCount = KeepAliveCount,
                LifetimeCount = LifetimeCount,
                Priority = Priority,
                PublishingIntervalMs = PublishingIntervalMs,
                PublishingEnabled = PublishingEnabled,
                MaxNotificationsPerPublish = MaxNotificationsPerPublish,
                MinLifetimeIntervalMs = MinLifetimeIntervalMs,
                SendInitialValuesOnTransfer = SendInitialValuesOnTransfer,
                MonitoredItems = MonitoredItems,
                LogicalGroupId = LogicalGroupId,
                PartitionIndex = PartitionIndex,
                RecoveryPolicy = (uint)defaults.RecoveryPolicy,
                DisableUnboundedItemMode = defaults.DisableUnboundedItemMode,
                MaxMonitoredItemsPerPartition = defaults.MaxMonitoredItemsPerPartition ?? 0,
                MaxPartitionCount = defaults.MaxPartitionCount,
                SecondaryPartitionIdleTimeoutMs = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(-1, defaults.SecondaryPartitionIdleTimeout.TotalMilliseconds))
            };
        }
    }
}
