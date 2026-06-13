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
using System.Threading;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Subscription options
    /// </summary>
    public record class SubscriptionOptions
    {
        /// <summary>
        /// The subscription is disabled
        /// </summary>
        public bool Disabled { get; init; }

        /// <summary>
        /// Set keep alive count
        /// </summary>
        public uint KeepAliveCount { get; init; }

        /// <summary>
        /// The life time of the subscription in counts of
        /// publish interval.
        /// LifetimeCount shall be at least 3*KeepAliveCount.
        /// </summary>
        public uint LifetimeCount { get; init; }

        /// <summary>
        /// Set desired priority of the subscription
        /// </summary>
        public byte Priority { get; init; }

        /// <summary>
        /// Set desired publishing interval
        /// </summary>
        public TimeSpan PublishingInterval { get; init; }

        /// <summary>
        /// Set desired publishing enabled
        /// </summary>
        public bool PublishingEnabled { get; init; }

        /// <summary>
        /// Set max notifications per publish
        /// </summary>
        public uint MaxNotificationsPerPublish { get; init; }

        /// <summary>
        /// Set min lifetime interval
        /// </summary>
        public TimeSpan MinLifetimeInterval { get; init; }

        /// <summary>
        /// When the V2 manager restores this subscription via
        /// <see cref="ISubscriptionManager.LoadAsync"/> with
        /// <c>transferSubscriptions: true</c>, request the server to
        /// send the latest cached value of every monitored item as
        /// part of the take-over (OPC UA Part 4 §5.13.7
        /// <c>TransferSubscriptions</c>'s <c>sendInitialValues</c>
        /// argument). Defaults to <c>false</c>: the server only sends
        /// values that arrived after the subscription was suspended,
        /// matching the post-disconnect semantics that the V2 manager
        /// expects for failover-on-recreate.
        /// </summary>
        public bool SendInitialValuesOnTransfer { get; init; }

        /// <summary>
        /// Controls how the client reacts to an unsolicited
        /// <c>Good_SubscriptionTransferred</c> StatusChangeNotification
        /// from the server. Defaults to
        /// <see cref="SubscriptionRecoveryPolicy.ReportOnly"/> which
        /// matches the spec-strict behaviour (OPC UA Part 4 §5.14.7).
        /// Set to
        /// <see cref="SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer"/>
        /// to automatically recreate the subscription on the same
        /// session when the notification arrives without the client
        /// having initiated a TransferSubscriptions request — this
        /// addresses server quirks that leave subscriptions stuck.
        /// </summary>
        public SubscriptionRecoveryPolicy RecoveryPolicy { get; init; }

        /// <summary>
        /// <para>
        /// Opts the logical subscription out of the V2 unbounded-item
        /// behaviour. When <c>false</c> (the default), the engine
        /// transparently splits monitored items across multiple
        /// server-side partition subscriptions as needed so callers
        /// can add an effectively unlimited number of items. When
        /// <c>true</c>, the subscription is bound to a single
        /// server-side subscription and
        /// <see cref="MonitoredItems.IMonitoredItemCollection.TryAdd"/>
        /// calls that would exceed the server's
        /// <see cref="ServerCapabilities.MaxMonitoredItemsPerSubscription"/>
        /// cap surface the server's
        /// <c>Bad_TooManyMonitoredItems</c> per-item status (the
        /// pre-V2 behaviour).
        /// </para>
        /// </summary>
        public bool DisableUnboundedItemMode { get; init; }

        /// <summary>
        /// <para>
        /// Per-partition upper bound on monitored items. When the
        /// logical subscription decides to split (see
        /// <see cref="DisableUnboundedItemMode"/>) the wrapper uses
        /// this value as the proactive per-partition cap; new items
        /// land on a fresh partition once the current one reaches
        /// the cap.
        /// </para>
        /// <para>
        /// The reactive fallback further pins the effective cap of
        /// any individual partition if the server returns
        /// <c>Bad_TooManyMonitoredItems</c> for a
        /// <c>CreateMonitoredItems</c> request — the partition is
        /// marked no-grow and the next add mints a new partition.
        /// This lets the wrapper handle servers that enforce a
        /// stricter limit than they advertise (or that advertise
        /// none at all).
        /// </para>
        /// <para>
        /// <c>null</c> (the default) means "no proactive cap" — the
        /// wrapper relies entirely on the reactive fallback to
        /// discover the server's actual limit. Set this to a smaller
        /// value to keep each partition smaller for snapshot /
        /// transfer scaling, or for predictable partitioning at a
        /// specific threshold; the wrapper does not consult
        /// <see cref="ServerCapabilities.MaxMonitoredItemsPerSubscription"/>
        /// to seed this value automatically.
        /// </para>
        /// <para>
        /// Has no effect when <see cref="DisableUnboundedItemMode"/>
        /// is <c>true</c>.
        /// </para>
        /// </summary>
        public uint? MaxMonitoredItemsPerPartition { get; init; }

        /// <summary>
        /// <para>
        /// Hard upper bound on the number of partitions a single
        /// logical subscription is allowed to grow to. The wrapper
        /// refuses to mint a new partition once the count reaches
        /// this value; the failed <c>TryAdd</c> returns <c>false</c>
        /// and the caller observes the standard add-failed path.
        /// </para>
        /// <para>
        /// This cap exists primarily as a denial-of-service
        /// safeguard: a hostile or buggy server that rejects every
        /// <c>CreateMonitoredItems</c> request with
        /// <c>Bad_TooManyMonitoredItems</c> would otherwise cause
        /// the wrapper's reactive fallback to mint a fresh server-
        /// side subscription per add attempt, growing client memory
        /// and server handle space without bound. The cap defaults
        /// to <c>32</c>, which comfortably covers legitimate
        /// fan-out scenarios (a server allowing 1000 items per
        /// subscription supports 32k items per logical subscription)
        /// but stops runaway growth.
        /// </para>
        /// <para>
        /// Has no effect when <see cref="DisableUnboundedItemMode"/>
        /// is <c>true</c>.
        /// </para>
        /// </summary>
        public uint MaxPartitionCount { get; init; } = 32;

        /// <summary>
        /// <para>
        /// Idle timeout before a secondary (non-primary) partition
        /// subscription is deleted on the server after its last
        /// monitored item is removed. The primary partition is never
        /// deleted while the logical subscription is alive so the
        /// logical subscription's server-side identifier remains
        /// stable for callers that log or correlate by id.
        /// </para>
        /// <para>
        /// Defaults to <c>30s</c>. Set to <see cref="TimeSpan.Zero"/>
        /// to delete empty secondary partitions immediately; set to
        /// <see cref="Timeout.InfiniteTimeSpan"/> to never delete
        /// empty secondary partitions.
        /// </para>
        /// <para>
        /// Has no effect when <see cref="DisableUnboundedItemMode"/>
        /// is <c>true</c>.
        /// </para>
        /// </summary>
        public TimeSpan SecondaryPartitionIdleTimeout { get; init; }
            = TimeSpan.FromSeconds(30);
    }
}
