// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    internal sealed partial class SubscriptionManager
    {
        /// <summary>
        /// Whether this manager has registered any opt-in subscription.
        /// </summary>
        internal bool HasValueRefresh => Volatile.Read(ref m_hasValueRefresh) != 0;

        /// <summary>
        /// Invalidates refresh work from a preceding connection before a new recovery starts.
        /// </summary>
        internal void BeginValueRecovery()
        {
            if (!HasValueRefresh)
            {
                return;
            }
            lock (m_subscriptionLock)
            {
                m_valueRecoveryEpoch++;
                m_refreshReactivatedValues = false;
                m_transferredValueRefresh?.Clear();
                m_reactivationValueRefresh?.Clear();
                foreach (IManagedSubscription owned in m_subscriptions)
                {
                    if (owned is Subscription { RefreshValuesOnRecovery: true, Created: true } partition)
                    {
                        (m_reactivationValueRefresh ??= []).Add(
                            partition, new(partition.Id, partition.MessageGeneration));
                    }
                }
            }
        }

        /// <summary>
        /// Records a reactivation until the session can serve ordinary requests.
        /// </summary>
        internal void RecordReactivatedValueRefresh()
        {
            if (!HasValueRefresh)
            {
                return;
            }
            lock (m_subscriptionLock)
            {
                m_refreshReactivatedValues = true;
            }
        }

        /// <summary>
        /// Schedules refresh of the surviving partitions without delaying connection recovery.
        /// </summary>
        internal void ScheduleValueRefresh(ISessionClient session, Func<bool> isSessionAvailable)
        {
            lock (m_subscriptionLock)
            {
                if (!m_refreshReactivatedValues && m_transferredValueRefresh is not { Count: > 0 })
                {
                    return;
                }
                long epoch = m_valueRecoveryEpoch;
                NodeId sessionId = m_session.SessionId;
                foreach (IManagedSubscription owned in m_subscriptions)
                {
                    if (owned is not Subscription { RefreshValuesOnRecovery: true, Created: true } partition)
                    {
                        continue;
                    }
                    if ((m_transferredValueRefresh == null ||
                            !m_transferredValueRefresh.TryGetValue(partition, out ValueRefreshBinding binding)) &&
                        (!m_refreshReactivatedValues ||
                            m_reactivationValueRefresh == null ||
                            !m_reactivationValueRefresh.TryGetValue(partition, out binding)))
                    {
                        continue;
                    }
                    partition.ScheduleValueRefresh(session, binding.Id, binding.Generation, IsCurrent);

                    bool IsCurrent()
                    {
                        if (!isSessionAvailable())
                        {
                            return false;
                        }
                        lock (m_subscriptionLock)
                        {
                            return Volatile.Read(ref m_disposed) == 0 &&
                                epoch == m_valueRecoveryEpoch &&
                                sessionId == m_session.SessionId &&
                                !m_superseded.Contains(partition) &&
                                IsLiveOwnerLocked(partition, binding.Id);
                        }
                    }
                }
                m_refreshReactivatedValues = false;
                m_reactivationValueRefresh?.Clear();
                m_transferredValueRefresh?.Clear();
            }
        }

        private static SubscriptionValueSnapshotRegistration? CreateValueRefreshRegistration(
            ISubscriptionNotificationHandler handler, SubscriptionOptions options)
        {
            if (!options.RefreshValuesOnRecovery)
            {
                return null;
            }
            return handler is ISubscriptionValueSnapshotHandler snapshotHandler
                ? new SubscriptionValueSnapshotRegistration(snapshotHandler)
                : throw new ArgumentException(
                    "Recovery value refresh requires an ISubscriptionValueSnapshotHandler.", nameof(handler));
        }

        private void RecordTransferredValueRefresh(IManagedSubscription subscription)
        {
            if (subscription is Subscription { RefreshValuesOnRecovery: true } partition)
            {
                lock (m_subscriptionLock)
                {
                    (m_transferredValueRefresh ??= [])[partition] = new(partition.Id, partition.MessageGeneration);
                }
            }
        }

        private readonly record struct ValueRefreshBinding(uint Id, long Generation);

        private int m_hasValueRefresh;
        private long m_valueRecoveryEpoch;
        private bool m_refreshReactivatedValues;
        private Dictionary<Subscription, ValueRefreshBinding>? m_reactivationValueRefresh;
        private Dictionary<Subscription, ValueRefreshBinding>? m_transferredValueRefresh;
    }

    /// <summary>
    /// Shares an opted-in snapshot destination and callback ordering across a logical subscription's partitions.
    /// </summary>
    internal sealed class SubscriptionValueSnapshotRegistration(ISubscriptionValueSnapshotHandler handler)
    {
        /// <summary>
        /// Connects the destination after the primary partition has been constructed.
        /// </summary>
        public void Bind(ISubscription logical, PartitionForwardingHandler? forwarding)
        {
            m_logical = logical;
            m_forwarding = forwarding;
        }

        /// <summary>
        /// Installs refresh tracking before a partition starts processing monitored-item changes.
        /// </summary>
        public void InitializePartition(IManagedSubscription partition)
        {
            if (partition is not Subscription concrete)
            {
                throw new NotSupportedException("Recovery value refresh requires the V2 subscription engine.");
            }
            concrete.EnableValueRefresh(this);
        }

        /// <summary>
        /// Runs the snapshot callback with the same cross-partition serialization as notifications.
        /// </summary>
        public ValueTask DispatchAsync(Func<ValueTask> callback, CancellationToken ct)
        {
            return m_forwarding == null ? callback() : m_forwarding.DispatchValueSnapshotsAsync(callback, ct);
        }

        /// <summary>
        /// Delivers an accepted value to the logical subscription's snapshot handler.
        /// </summary>
        public ValueTask DeliverAsync(IMonitoredItem item, DataValue value, CancellationToken ct)
        {
            return handler.OnValueSnapshotAsync(m_logical!, item, value, ct);
        }

        private ISubscription? m_logical;
        private PartitionForwardingHandler? m_forwarding;
    }
}
