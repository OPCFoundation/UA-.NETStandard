// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public bool Disabled { get; set; }

        /// <summary>
        /// Set keep alive count
        /// </summary>
        public uint KeepAliveCount { get; set; }

        /// <summary>
        /// The life time of the subscription in counts of
        /// publish interval.
        /// LifetimeCount shall be at least 3*KeepAliveCount.
        /// </summary>
        public uint LifetimeCount { get; set; }

        /// <summary>
        /// Set desired priority of the subscription
        /// </summary>
        public byte Priority { get; set; }

        /// <summary>
        /// Set desired publishing interval
        /// </summary>
        public TimeSpan PublishingInterval { get; set; }

        /// <summary>
        /// Set desired publishing enabled
        /// </summary>
        public bool PublishingEnabled { get; set; }

        /// <summary>
        /// Set max notifications per publish
        /// </summary>
        public uint MaxNotificationsPerPublish { get; set; }

        /// <summary>
        /// Set min lifetime interval
        /// </summary>
        public TimeSpan MinLifetimeInterval { get; set; }

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
        public bool SendInitialValuesOnTransfer { get; set; }

        /// <summary>
        /// Requests current values after managed session reactivation or subscription transfer.
        /// If ResendData fails, current values are read and delivered to an
        /// <see cref="ISubscriptionValueSnapshotHandler"/>. Defaults to false.
        /// Set this when registering the subscription; later options changes do not toggle it.
        /// </summary>
        public bool RefreshValuesOnRecovery { get; set; }

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
        public SubscriptionRecoveryPolicy RecoveryPolicy { get; set; }

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
        public bool DisableUnboundedItemMode { get; set; }

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
        public uint? MaxMonitoredItemsPerPartition { get; set; }

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
        public uint MaxPartitionCount { get; set; } = 32;

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
        public TimeSpan SecondaryPartitionIdleTimeout { get; set; }
            = TimeSpan.FromSeconds(30);
    }
}
