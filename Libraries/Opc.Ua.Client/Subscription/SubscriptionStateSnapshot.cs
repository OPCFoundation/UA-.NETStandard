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
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Immutable snapshot of a V2 <see cref="ISubscription"/>'s
    /// configuration plus the server-side identifiers needed to take
    /// over the subscription on a different session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced by <see cref="Subscription.Snapshot"/> and consumed by
    /// <see cref="ISubscriptionManager.LoadAsync"/>.
    /// </para>
    /// <para>
    /// The snapshot is itself an <see cref="IEncodeable"/> via the
    /// <see cref="DataTypeAttribute"/> source generator. The fields
    /// carried on the wire are simple primitives (e.g. <see cref="int"/>
    /// milliseconds for durations, <see cref="uint"/> for enums); the
    /// non-encoded <see cref="ToOptions"/> projection exposes a
    /// consumer-friendly <see cref="SubscriptionOptions"/> built from
    /// those surrogate fields. The companion <see cref="AsOptions"/>
    /// factory does the inverse mapping at <see cref="Subscription.Snapshot"/>
    /// time. This split keeps the wire schema independent of
    /// .NET-specific types like <see cref="TimeSpan"/> that the source
    /// generator does not encode natively.
    /// </para>
    /// </remarks>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    public sealed partial record class SubscriptionStateSnapshot
    {
        /// <summary>
        /// Server-assigned subscription id, or <c>0</c> if the
        /// subscription had not been created on the server yet.
        /// </summary>
        [DataTypeField(Order = 1)]
        public partial uint ServerId { get; init; }

        /// <summary>
        /// Server's reported retransmission-queue sequence numbers at
        /// snapshot time. Diagnostic-only for the transfer leg of
        /// restore; transfer uses the <c>TransferSubscriptions</c>
        /// response's authoritative list.
        /// </summary>
        [DataTypeField(Order = 2)]
        public partial ArrayOf<uint> AvailableSequenceNumbers { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.Disabled"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 10)]
        public partial bool Disabled { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.KeepAliveCount"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 11)]
        public partial uint KeepAliveCount { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.LifetimeCount"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 12)]
        public partial uint LifetimeCount { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.Priority"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 13)]
        public partial byte Priority { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.PublishingInterval"/> as
        /// whole milliseconds (the <see cref="TimeSpan"/> projection is
        /// rebuilt in the <see cref="ToOptions"/> getter).
        /// </summary>
        [DataTypeField(Order = 14)]
        public partial int PublishingIntervalMs { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.PublishingEnabled"/> surrogate.
        /// </summary>
        [DataTypeField(Order = 15)]
        public partial bool PublishingEnabled { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.MaxNotificationsPerPublish"/>
        /// surrogate.
        /// </summary>
        [DataTypeField(Order = 16)]
        public partial uint MaxNotificationsPerPublish { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.MinLifetimeInterval"/> as
        /// whole milliseconds.
        /// </summary>
        [DataTypeField(Order = 17)]
        public partial int MinLifetimeIntervalMs { get; init; }

        /// <summary>
        /// <see cref="SubscriptionOptions.SendInitialValuesOnTransfer"/>
        /// surrogate.
        /// </summary>
        [DataTypeField(Order = 18)]
        public partial bool SendInitialValuesOnTransfer { get; init; }

        /// <summary>
        /// Per-item state at snapshot time. Encoded inline (not via
        /// <see cref="ExtensionObject"/>) because
        /// <see cref="MonitoredItemStateSnapshot"/> is <c>sealed</c>
        /// and carries its own ordered fields.
        /// </summary>
        [DataTypeField(Order = 20, StructureHandling = StructureHandling.Inline)]
        public partial ArrayOf<MonitoredItemStateSnapshot> MonitoredItems { get; init; }

        /// <summary>
        /// Project the encoded surrogate fields back into a live
        /// <see cref="SubscriptionOptions"/>. Not serialized.
        /// </summary>
        public SubscriptionOptions ToOptions()
        {
            return new SubscriptionOptions
            {
                Disabled = Disabled,
                KeepAliveCount = KeepAliveCount,
                LifetimeCount = LifetimeCount,
                Priority = Priority,
                PublishingInterval = TimeSpan.FromMilliseconds(PublishingIntervalMs),
                PublishingEnabled = PublishingEnabled,
                MaxNotificationsPerPublish = MaxNotificationsPerPublish,
                MinLifetimeInterval = TimeSpan.FromMilliseconds(MinLifetimeIntervalMs),
                SendInitialValuesOnTransfer = SendInitialValuesOnTransfer
            };
        }

        /// <summary>
        /// Construct a <see cref="SubscriptionStateSnapshot"/> from a
        /// live <see cref="SubscriptionOptions"/> + the captured
        /// server-side state. The factory populates every surrogate
        /// field so the snapshot round-trips through
        /// <see cref="ISubscriptionManager.LoadAsync"/> without losing
        /// any options-field value.
        /// </summary>
        /// <param name="options">Live options at snapshot time.</param>
        /// <param name="serverId">Server-assigned subscription id, or
        /// <c>0</c> if not yet created on the server.</param>
        /// <param name="availableSequenceNumbers">Server's
        /// retransmission-queue sequence numbers.</param>
        /// <param name="monitoredItems">Per-item snapshots.</param>
        public static SubscriptionStateSnapshot AsOptions(
            SubscriptionOptions options,
            uint serverId,
            ArrayOf<uint> availableSequenceNumbers,
            ArrayOf<MonitoredItemStateSnapshot> monitoredItems)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return new SubscriptionStateSnapshot
            {
                ServerId = serverId,
                AvailableSequenceNumbers = availableSequenceNumbers,
                Disabled = options.Disabled,
                KeepAliveCount = options.KeepAliveCount,
                LifetimeCount = options.LifetimeCount,
                Priority = options.Priority,
                PublishingIntervalMs = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(0, options.PublishingInterval.TotalMilliseconds)),
                PublishingEnabled = options.PublishingEnabled,
                MaxNotificationsPerPublish = options.MaxNotificationsPerPublish,
                MinLifetimeIntervalMs = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(0, options.MinLifetimeInterval.TotalMilliseconds)),
                SendInitialValuesOnTransfer = options.SendInitialValuesOnTransfer,
                MonitoredItems = monitoredItems
            };
        }
    }
}
